import { Component, OnDestroy, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api } from './api';
import { EDITABLE_TEXTS, TextStore } from './text-store';

interface Settings {
  studyOpen: boolean;
  minCorrectForLottery: number;
}

interface GroupCount {
  assigned: number;
  completed: number;
  inProgress: number;
}

interface ParticipantRow {
  code: string;
  isTest: boolean;
  group: string | null;
  step: string;
  taskIndex: number;
  total: number;
  consentAt: string;
  finishedAt: string | null;
  aiClicks: number;
  correct: number;
}

interface Overview {
  settings: Settings;
  counts: { high: GroupCount; low: GroupCount; notAssigned: number; lotteryEntries: number; events: number };
  participants: ParticipantRow[];
}

export interface Question {
  id: number;
  sortOrder: number;
  title: string;
  text: string;
  correctAnswer: number | null;
  tolerance: number;
  aiResponse: string;
  aiAnswerValue: number | null;
}

type Tab = 'overview' | 'questions' | 'texts' | 'data';

const KEY_STORAGE = 'exp_admin_key';

const STEP_LABELS: Record<string, string | undefined> = {
  instructions: 'Instruktion',
  notice: 'Gruppebesked',
  task: 'Opgaver',
  manipulation: 'Manipulationstjek',
  background: 'Baggrund',
  debrief: 'Færdig',
};

@Component({
  selector: 'app-admin',
  imports: [FormsModule, DatePipe],
  templateUrl: './admin.html',
})
export class Admin implements OnDestroy {
  private readonly api = inject(Api);
  private readonly textStore = inject(TextStore);

  protected readonly key = signal<string | null>(this.loadKey());
  protected password = '';
  protected readonly loginError = signal<string | null>(null);
  protected readonly tab = signal<Tab>('overview');
  protected readonly overview = signal<Overview | null>(null);
  protected readonly questions = signal<Question[]>([]);
  protected readonly editing = signal<Question | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected minCorrect = 3;
  protected resetConfirm = '';
  protected resetLottery = false;
  protected readonly stepLabels = STEP_LABELS;
  protected readonly editableTexts = EDITABLE_TEXTS;
  /** Tekstfelternes indhold på fanen "Tekster" (aktuel tekst = ændret eller standard). */
  protected textDraft: Record<string, string> = {};
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    if (this.key()) void this.loadAll();
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  // ---------- Login ----------

  protected async login(): Promise<void> {
    this.loginError.set(null);
    const res = await this.call<Overview>('GET', '/api/admin/overview', undefined, this.password);
    if (res.status === 200) {
      this.key.set(this.password);
      try { sessionStorage.setItem(KEY_STORAGE, this.password); } catch { /* ignore */ }
      this.password = '';
      await this.loadAll();
    } else {
      this.loginError.set(res.status === 503 ? 'Admin-adgangskoden er ikke sat på serveren.' : 'Forkert adgangskode.');
    }
  }

  protected logout(): void {
    try { sessionStorage.removeItem(KEY_STORAGE); } catch { /* ignore */ }
    this.key.set(null);
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  private async loadAll(): Promise<void> {
    await Promise.all([this.refresh(), this.loadQuestions(), this.loadTexts()]);
    this.timer ??= setInterval(() => void this.refresh(), 5000);
  }

  // ---------- Oversigt ----------

  protected async refresh(): Promise<void> {
    const res = await this.call<Overview>('GET', '/api/admin/overview');
    if (res.status === 200 && res.data) {
      const firstLoad = !this.overview();
      this.overview.set(res.data);
      if (firstLoad) this.minCorrect = res.data.settings.minCorrectForLottery;
    }
  }

  protected async setOpen(open: boolean): Promise<void> {
    const s = this.overview()?.settings;
    if (!s) return;
    if (!open && !confirm('Luk eksperimentet for nye deltagere? Deltagere der er i gang kan stadig gøre det færdigt.')) return;
    await this.saveSettings({ ...s, studyOpen: open });
  }

  protected async saveMinCorrect(): Promise<void> {
    const s = this.overview()?.settings;
    if (!s) return;
    await this.saveSettings({ ...s, minCorrectForLottery: Math.max(0, Math.round(this.minCorrect)) });
    this.flash('Gemt.');
  }

  private async saveSettings(s: Settings): Promise<void> {
    await this.call('PUT', '/api/admin/settings', s);
    await this.refresh();
  }

  // ---------- Spørgsmål ----------

  protected async loadQuestions(): Promise<void> {
    const res = await this.call<Question[]>('GET', '/api/admin/questions');
    if (res.data) this.questions.set(res.data);
  }

  protected newQuestion(): void {
    this.editing.set({ id: 0, sortOrder: 0, title: '', text: '', correctAnswer: null, tolerance: 0.01, aiResponse: '', aiAnswerValue: null });
  }

  protected edit(q: Question): void {
    this.editing.set({ ...q });
  }

  protected async saveQuestion(): Promise<void> {
    const q = this.editing();
    if (!q) return;
    const body = {
      title: q.title,
      text: q.text,
      correctAnswer: toNumber(q.correctAnswer),
      tolerance: toNumber(q.tolerance) ?? 0.01,
      aiResponse: q.aiResponse,
      aiAnswerValue: toNumber(q.aiAnswerValue),
    };
    const res = q.id
      ? await this.call('PUT', `/api/admin/questions/${q.id}`, body)
      : await this.call('POST', '/api/admin/questions', body);
    if (res.status === 200) {
      this.editing.set(null);
      await this.loadQuestions();
      this.flash('Spørgsmålet er gemt.');
    } else {
      alert(typeof res.data === 'string' ? res.data : 'Kunne ikke gemme. Udfyld opgavetekst og AI-svar.');
    }
  }

  protected async deleteQuestion(q: Question): Promise<void> {
    if (!confirm(`Slet "${q.title || q.text.slice(0, 40)}"?\n\nDeltagere der allerede er i gang påvirkes ikke.`)) return;
    await this.call('DELETE', `/api/admin/questions/${q.id}`);
    await this.loadQuestions();
  }

  protected async move(index: number, delta: number): Promise<void> {
    const list = [...this.questions()];
    const target = index + delta;
    if (target < 0 || target >= list.length) return;
    [list[index], list[target]] = [list[target], list[index]];
    this.questions.set(list);
    await this.call('PUT', '/api/admin/questions/order', list.map((q) => q.id));
  }

  // ---------- Tekster ----------

  private async loadTexts(): Promise<void> {
    await this.textStore.load();
    const draft: Record<string, string> = {};
    for (const s of EDITABLE_TEXTS) for (const f of s.fields) draft[f.key] = this.textStore.valueFor(f.key);
    this.textDraft = draft;
  }

  protected isChanged(key: string): boolean {
    return this.textDraft[key] !== this.textStore.defaultFor(key);
  }

  protected resetText(key: string): void {
    this.textDraft[key] = this.textStore.defaultFor(key);
  }

  protected async saveTexts(): Promise<void> {
    // Kun ændrede tekster gemmes; tekster der er som standard sendes som null (= slet ændringen).
    const body: Record<string, string | null> = {};
    for (const [key, value] of Object.entries(this.textDraft)) {
      body[key] = value.trim() && value !== this.textStore.defaultFor(key) ? value : null;
    }
    const res = await this.call<Record<string, string>>('PUT', '/api/admin/texts', body);
    if (res.status === 200 && res.data) {
      this.textStore.overrides.set(res.data);
      await this.loadTexts();
      this.flash('Teksterne er gemt. Nye sidevisninger bruger dem med det samme.');
    } else {
      alert('Kunne ikke gemme teksterne.');
    }
  }

  // ---------- Deltagere ----------

  protected async deleteParticipant(p: ParticipantRow): Promise<void> {
    const status = p.finishedAt ? 'færdig' : 'IKKE færdig';
    if (!confirm(`Slet deltager ${p.code} (${status})?\n\nSvar og hændelseslog slettes permanent. Kan ikke fortrydes.`)) return;
    const res = await this.call('DELETE', `/api/admin/participants/${encodeURIComponent(p.code)}`);
    if (res.status === 204) this.flash(`${p.code} er slettet.`);
    await this.refresh();
  }

  /** Andel i procent af de fordelte (ikke-test) deltagere. */
  protected share(n: number): string {
    const c = this.overview()?.counts;
    const total = (c?.high.assigned ?? 0) + (c?.low.assigned ?? 0);
    return total ? `${Math.round((n / total) * 100)} %` : '–';
  }

  // ---------- Data ----------

  protected async download(name: string): Promise<void> {
    try {
      await this.api.download(`/api/admin/export/${name}`, this.key()!);
    } catch {
      alert('Download fejlede.');
    }
  }

  protected async reset(): Promise<void> {
    if (this.resetConfirm !== 'SLET') return;
    if (!confirm('Alle deltagerdata slettes permanent. Har du eksporteret det, du skal bruge?')) return;
    const res = await this.call('POST', '/api/admin/reset', { confirm: 'SLET', includeLottery: this.resetLottery });
    if (res.status === 204) {
      this.resetConfirm = '';
      this.flash('Deltagerdata er slettet.');
      await this.refresh();
    }
  }

  protected testLink(group: 'high' | 'low'): string {
    return `${location.origin}/?test=${group}`;
  }

  protected get participantLink(): string {
    return `${location.origin}/`;
  }

  protected async copy(text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    this.flash('Link kopieret.');
  }

  // ---------- Hjælpere ----------

  private async call<T = unknown>(method: string, url: string, body?: unknown, key = this.key() ?? '') {
    this.busy.set(true);
    try {
      const res = await this.api.request<T>(method, url, body, { 'X-Admin-Key': key }, 2);
      if (res.status === 401 && key === this.key()) this.logout();
      return res;
    } finally {
      this.busy.set(false);
    }
  }

  private flash(msg: string): void {
    this.message.set(msg);
    setTimeout(() => this.message.set(null), 2500);
  }

  private loadKey(): string | null {
    try { return sessionStorage.getItem(KEY_STORAGE); } catch { return null; }
  }
}

function toNumber(v: unknown): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(String(v).replace(',', '.'));
  return Number.isFinite(n) ? n : null;
}

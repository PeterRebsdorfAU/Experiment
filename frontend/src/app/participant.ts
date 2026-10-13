import { Component, HostListener, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, StateDto } from './api';
import { EventLog } from './event-log';
import { MonitoringOverlay } from './monitoring-overlay';
import { TEXTS } from './texts';

type LocalScreen = 'loading' | 'waiting' | 'consent' | 'error';

const AI_THINKING_MS = 1600;

@Component({
  selector: 'app-participant',
  imports: [FormsModule, MonitoringOverlay],
  templateUrl: './participant.html',
})
export class Participant implements OnInit, OnDestroy {
  private readonly api = inject(Api);
  private readonly events = inject(EventLog);

  protected readonly t = TEXTS;
  protected readonly retrying = this.api.retrying;
  protected readonly screen = signal<LocalScreen>('loading');
  protected readonly state = signal<StateDto | null>(null);
  protected readonly busy = signal(false);
  protected readonly clockOffset = signal(0);

  // Opgaveskærm
  protected answer = '';
  protected readonly answerError = signal(false);
  protected readonly aiThinking = signal(false);
  protected readonly aiResponse = signal<string | null>(null);

  // Afsluttende spørgsmål
  protected manipulation: number | null = null;
  protected gender: string | null = null;
  protected aiUsage: string | null = null;
  protected email = '';
  protected readonly emailError = signal(false);

  private readonly testGroup = new URLSearchParams(location.search).get('test');
  private readonly isTestLink = this.testGroup === 'high' || this.testGroup === 'low';
  private readonly storageKey = this.isTestLink ? 'exp_pid_test' : 'exp_pid';
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private answerLogTimer: ReturnType<typeof setTimeout> | null = null;

  async ngOnInit(): Promise<void> {
    // Forhindr at browserens tilbage-knap forlader forløbet.
    history.pushState(null, '', location.href);
    try {
      const pid = this.readPid();
      if (pid) {
        const res = await this.api.request<StateDto>('GET', `/api/participants/${pid}`);
        if (res.status === 200 && res.data) {
          this.applyState(res.data);
          return;
        }
        this.clearPid();
      }
      await this.checkOpen();
    } catch {
      this.screen.set('error');
    }
  }

  ngOnDestroy(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
  }

  @HostListener('window:popstate')
  onBack(): void {
    history.pushState(null, '', location.href);
  }

  @HostListener('document:visibilitychange')
  onVisibility(): void {
    this.events.log(document.hidden ? 'tab_hidden' : 'tab_visible', this.currentQuestionId());
  }

  @HostListener('window:blur')
  onBlur(): void {
    this.events.log('window_blur', this.currentQuestionId());
  }

  @HostListener('window:focus')
  onFocus(): void {
    this.events.log('window_focus', this.currentQuestionId());
  }

  // ---------- Før deltageren findes ----------

  private async checkOpen(): Promise<void> {
    if (this.isTestLink) {
      this.screen.set('consent');
      return;
    }
    const res = await this.api.request<{ open: boolean }>('GET', '/api/status');
    if (res.data?.open) {
      this.stopPolling();
      this.screen.set('consent');
    } else {
      this.screen.set('waiting');
      this.pollTimer ??= setInterval(() => void this.checkOpen().catch(() => {}), 3000);
    }
  }

  private stopPolling(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = null;
  }

  protected async acceptConsent(): Promise<void> {
    await this.run(async () => {
      const res = await this.api.request<StateDto>('POST', '/api/participants', {
        testGroup: this.isTestLink ? this.testGroup : null,
      });
      if (res.status === 423) {
        await this.checkOpen();
        return;
      }
      if (res.status !== 200 || !res.data) throw new Error('start failed');
      this.writePid(res.data.participantId);
      this.applyState(res.data);
    });
  }

  // ---------- Trin ----------

  protected next(action: string, body: unknown = {}): Promise<void> {
    return this.run(async () => {
      const s = this.state()!;
      const res = await this.api.request<StateDto>('POST', `/api/participants/${s.participantId}/${action}`, body);
      if ((res.status === 200 || res.status === 409) && res.data?.step) {
        this.applyState(res.data);
      } else if (res.status === 409) {
        await this.reload();
      } else if (res.status !== 400) {
        throw new Error(`HTTP ${res.status}`);
      }
    });
  }

  protected async submitAnswer(): Promise<void> {
    const task = this.state()?.task;
    if (!task) return;
    if (!this.answer.trim()) {
      this.answerError.set(true);
      return;
    }
    if (this.answerLogTimer) this.logAnswerChange();
    await this.events.flush();
    await this.next('answer', { questionId: task.questionId, answer: this.answer });
  }

  protected async getAiHelp(): Promise<void> {
    const s = this.state();
    const task = s?.task;
    if (!s || !task || this.aiThinking()) return;
    this.aiThinking.set(true);
    this.aiResponse.set(null);
    try {
      const [res] = await Promise.all([
        this.api.request<{ aiResponse: string }>('POST', `/api/participants/${s.participantId}/ai-help`, {
          questionId: task.questionId,
        }),
        new Promise((r) => setTimeout(r, AI_THINKING_MS)),
      ]);
      if (res.status === 200 && res.data) {
        this.aiResponse.set(res.data.aiResponse);
        this.events.log('ai_response_shown', task.questionId);
      } else if (res.status === 409) {
        await this.reload();
      }
    } catch {
      this.screen.set('error');
    } finally {
      this.aiThinking.set(false);
    }
  }

  protected onAnswerInput(): void {
    this.answerError.set(false);
    // Logger svarfeltets indhold, når deltageren holder en pause i skrivningen.
    if (this.answerLogTimer) clearTimeout(this.answerLogTimer);
    this.answerLogTimer = setTimeout(() => this.logAnswerChange(), 800);
  }

  /** Sender en afventende "answer_change" med det samme (bruges før der skiftes opgave). */
  private logAnswerChange(): void {
    if (this.answerLogTimer) clearTimeout(this.answerLogTimer);
    this.answerLogTimer = null;
    this.events.log('answer_change', this.currentQuestionId(), this.answer.slice(0, 100));
  }

  protected onPaste(e: ClipboardEvent): void {
    this.events.log('answer_paste', this.currentQuestionId(), (e.clipboardData?.getData('text') ?? '').slice(0, 200));
  }

  protected onCopyAi(): void {
    this.events.log('ai_text_copy', this.currentQuestionId(), (getSelection()?.toString() ?? '').slice(0, 200));
  }

  protected async submitBackground(): Promise<void> {
    if (!this.gender || !this.aiUsage) return;
    await this.next('background', { gender: this.gender, aiUsageFrequency: this.aiUsage });
  }

  protected async submitLottery(): Promise<void> {
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(this.email.trim())) {
      this.emailError.set(true);
      return;
    }
    this.emailError.set(false);
    await this.next('lottery', { email: this.email.trim() });
  }

  /** Kun for test-links: start et nyt forløb. */
  protected restartTest(): void {
    this.clearPid();
    location.reload();
  }

  // ---------- Hjælpere ----------

  private applyState(s: StateDto): void {
    const prevQuestion = this.state()?.task?.questionId;
    this.state.set(s);
    this.clockOffset.set(Date.parse(s.serverNow) - Date.now());
    this.events.start(s.participantId);
    this.stopPolling();
    if (s.task && s.task.questionId !== prevQuestion) {
      this.answer = '';
      this.answerError.set(false);
      this.aiResponse.set(s.task.aiShown ? s.task.aiResponse : null);
      scrollTo({ top: 0 });
    }
  }

  private async reload(): Promise<void> {
    const pid = this.state()?.participantId;
    if (!pid) return;
    const res = await this.api.request<StateDto>('GET', `/api/participants/${pid}`);
    if (res.data) this.applyState(res.data);
  }

  private async run(fn: () => Promise<void>): Promise<void> {
    if (this.busy()) return; // forhindrer dobbeltklik
    this.busy.set(true);
    try {
      await fn();
    } catch {
      this.screen.set('error');
    } finally {
      this.busy.set(false);
    }
  }

  private currentQuestionId(): number | null {
    return this.state()?.task?.questionId ?? null;
  }

  private readPid(): string | null {
    try { return localStorage.getItem(this.storageKey); } catch { return null; }
  }

  private writePid(pid: string): void {
    try { localStorage.setItem(this.storageKey, pid); } catch { /* privat tilstand */ }
  }

  private clearPid(): void {
    try { localStorage.removeItem(this.storageKey); } catch { /* ignore */ }
  }

  protected readonly scale = [1, 2, 3, 4, 5, 6, 7];
  protected readonly isTestLinkView = this.isTestLink;
}

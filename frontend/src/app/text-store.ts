import { Injectable, computed, signal } from '@angular/core';
import { TEXTS } from './texts';

export type Texts = typeof TEXTS;

export interface EditableField {
  key: string;
  label: string;
  multiline?: boolean;
}

/** De tekster, der kan rettes på admin-siden, grupperet som på siden. Nøglen er stien i TEXTS. */
export const EDITABLE_TEXTS: { section: string; fields: EditableField[] }[] = [
  { section: 'Generelt', fields: [{ key: 'studyTitle', label: 'Studiets titel (øverst på siden)' }] },
  {
    section: 'Venteside (før eksperimentet startes)',
    fields: [
      { key: 'waiting.title', label: 'Overskrift' },
      { key: 'waiting.body', label: 'Tekst', multiline: true },
    ],
  },
  {
    section: 'Samtykke',
    fields: [
      { key: 'consent.title', label: 'Overskrift' },
      { key: 'consent.body', label: 'Tekst', multiline: true },
      { key: 'consent.accept', label: 'Knaptekst' },
    ],
  },
  {
    section: 'Instruktion (fælles for alle)',
    fields: [
      { key: 'instructions.title', label: 'Overskrift' },
      { key: 'instructions.body', label: 'Tekst', multiline: true },
    ],
  },
  {
    section: 'Gruppebesked',
    fields: [
      { key: 'notice.title', label: 'Overskrift' },
      { key: 'notice.high', label: 'Høj overvågning', multiline: true },
      { key: 'notice.low', label: 'Lav overvågning', multiline: true },
      { key: 'notice.continue', label: 'Knaptekst' },
    ],
  },
  {
    section: 'Overvågning (kun høj-gruppen, på opgaveskærmene)',
    fields: [
      { key: 'monitoring.banner', label: 'Rød bjælke øverst' },
      { key: 'monitoring.actionLogged', label: 'Activity log-besked' },
    ],
  },
  {
    section: 'Afsluttende spørgsmål',
    fields: [
      { key: 'manipulation.question', label: 'Manipulationstjek (skala 1–7)' },
      { key: 'manipulation.low', label: 'Label for 1' },
      { key: 'manipulation.high', label: 'Label for 7' },
    ],
  },
  {
    section: 'Debriefing',
    fields: [
      { key: 'debrief.title', label: 'Overskrift' },
      { key: 'debrief.body', label: 'Tekst', multiline: true },
      { key: 'debrief.lotteryTitle', label: 'Lodtrækning: overskrift' },
      { key: 'debrief.lotteryBody', label: 'Lodtrækning: tekst', multiline: true },
      { key: 'debrief.lotteryThanks', label: 'Lodtrækning: tak-besked' },
    ],
  },
];

/** Henter tekster rettet i admin og lægger dem oven på standardteksterne i texts.ts. */
@Injectable({ providedIn: 'root' })
export class TextStore {
  readonly overrides = signal<Record<string, string>>({});
  readonly texts = computed(() => applyOverrides(TEXTS, this.overrides(), ''));

  async load(): Promise<void> {
    try {
      const res = await fetch('/api/texts');
      if (res.ok) this.overrides.set(await res.json());
    } catch {
      // Standardteksterne bruges, hvis serveren ikke svarer.
    }
  }

  defaultFor(key: string): string {
    return key.split('.').reduce<unknown>((o, k) => (o as Record<string, unknown>)?.[k], TEXTS) as string;
  }

  valueFor(key: string): string {
    return this.overrides()[key] ?? this.defaultFor(key);
  }
}

function applyOverrides<T>(obj: T, overrides: Record<string, string>, prefix: string): T {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${k}` : k;
    if (typeof v === 'string') out[k] = overrides[path] ?? v;
    else if (v && typeof v === 'object' && !Array.isArray(v)) out[k] = applyOverrides(v, overrides, path);
    else out[k] = v; // funktioner og lister bruges uændret
  }
  return out as T;
}

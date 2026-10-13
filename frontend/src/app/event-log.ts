import { Injectable } from '@angular/core';

interface PendingEvent {
  id: string;
  type: string;
  questionId: number | null;
  clientTime: string;
  data: string | null;
}

const STORAGE_KEY = 'exp_pending_events';

/**
 * Rå hændelseslog fra browseren. Hændelser lægges i kø (også i localStorage, så de overlever
 * en genindlæsning) og sendes i bundter hvert 2. sekund. Hver hændelse har et unikt ID,
 * så serveren kan sortere dubletter fra, hvis et bundt sendes igen efter en netværksfejl.
 */
@Injectable({ providedIn: 'root' })
export class EventLog {
  private participantId: string | null = null;
  private queue: PendingEvent[] = [];
  private sending = false;
  private timer: ReturnType<typeof setInterval> | null = null;

  start(participantId: string): void {
    if (this.participantId === participantId) return;
    this.participantId = participantId;
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]') as PendingEvent[];
      if (Array.isArray(saved)) this.queue.push(...saved);
    } catch { /* tom kø */ }
    this.timer ??= setInterval(() => void this.flush(), 2000);
    addEventListener('pagehide', () => this.flushBeacon());
  }

  log(type: string, questionId: number | null = null, data: string | null = null): void {
    if (!this.participantId) return;
    this.queue.push({ id: crypto.randomUUID(), type, questionId, clientTime: new Date().toISOString(), data });
    this.persist();
  }

  async flush(): Promise<void> {
    if (this.sending || !this.participantId || this.queue.length === 0) return;
    this.sending = true;
    const batch = this.queue.slice(0, 100);
    try {
      const res = await fetch(`/api/participants/${this.participantId}/events`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ events: batch }),
      });
      if (res.ok || (res.status >= 400 && res.status < 500)) {
        const sent = new Set(batch.map((e) => e.id));
        this.queue = this.queue.filter((e) => !sent.has(e.id));
        this.persist();
      }
    } catch {
      // Netværksfejl: prøver igen ved næste interval.
    } finally {
      this.sending = false;
    }
  }

  /** Når siden lukkes: send resten med sendBeacon (virker også mens siden lukker). */
  private flushBeacon(): void {
    if (!this.participantId || this.queue.length === 0) return;
    const blob = new Blob([JSON.stringify({ events: this.queue.slice(0, 100) })], { type: 'application/json' });
    navigator.sendBeacon(`/api/participants/${this.participantId}/events`, blob);
  }

  private persist(): void {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(this.queue)); } catch { /* privat tilstand */ }
  }
}

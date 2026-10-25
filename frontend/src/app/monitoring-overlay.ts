import { Component, HostListener, OnDestroy, OnInit, inject, input, signal } from '@angular/core';
import { TextStore } from './text-store';

interface LoggedItem {
  id: number;
  time: string;
}

/**
 * Overvågnings-elementerne for gruppen med høj overvågning: rød bjælke, blinkende ● REC med tæller
 * og en lille "activity log". Alt er kun visuelt – der optages intet ud over den samme anonyme
 * kliklog, som alle deltagere har.
 */
@Component({
  selector: 'app-monitoring-overlay',
  template: `
    <div class="mon-banner" role="status">
      <span class="mon-dot"></span>{{ t.banner }}
    </div>
    <div class="mon-rec" aria-hidden="true">
      <span class="mon-dot blink"></span>{{ t.rec }} <span class="mon-time">{{ elapsed() }}</span>
    </div>
    <div class="mon-log" aria-hidden="true">
      @for (item of items(); track item.id) {
        <div class="mon-log-item">✓ {{ t.actionLogged }} · {{ item.time }}</div>
      }
    </div>
  `,
})
export class MonitoringOverlay implements OnInit, OnDestroy {
  /** Tidspunktet hvor opgaverne startede (fra serveren), så tælleren fortsætter ved genindlæsning. */
  readonly startedAt = input<string | null>(null);
  /** Forskel mellem server- og klient-ur i ms. */
  readonly clockOffset = input(0);

  private readonly textStore = inject(TextStore);

  protected get t() {
    return this.textStore.texts().monitoring;
  }
  protected readonly elapsed = signal('00:00:00');
  protected readonly items = signal<LoggedItem[]>([]);
  private timer: ReturnType<typeof setInterval> | null = null;
  private nextId = 0;
  private lastLogged = 0;

  ngOnInit(): void {
    this.tick();
    this.timer = setInterval(() => this.tick(), 1000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  @HostListener('document:click')
  @HostListener('document:keydown')
  onActivity(): void {
    const now = Date.now();
    if (now - this.lastLogged < 1200) return; // højst ca. én besked pr. sekund
    this.lastLogged = now;
    const id = this.nextId++;
    const time = new Date().toLocaleTimeString('da-DK', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    this.items.update((list) => [...list.slice(-2), { id, time }]);
    setTimeout(() => this.items.update((list) => list.filter((i) => i.id !== id)), 3500);
  }

  private tick(): void {
    const start = this.startedAt() ? Date.parse(this.startedAt()!) : Date.now();
    const secs = Math.max(0, Math.floor((Date.now() + this.clockOffset() - start) / 1000));
    const pad = (n: number) => String(n).padStart(2, '0');
    this.elapsed.set(`${pad(Math.floor(secs / 3600))}:${pad(Math.floor((secs % 3600) / 60))}:${pad(secs % 60)}`);
  }
}

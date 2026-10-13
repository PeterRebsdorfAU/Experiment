import { Injectable, signal } from '@angular/core';

export type Step = 'instructions' | 'notice' | 'task' | 'manipulation' | 'background' | 'debrief';
export type Group = 'high' | 'low';

export interface TaskDto {
  questionId: number;
  position: number;
  total: number;
  title: string;
  text: string;
  aiShown: boolean;
  aiResponse: string | null;
}

export interface StateDto {
  participantId: string;
  code: string;
  step: Step;
  group: Group | null;
  isTest: boolean;
  task: TaskDto | null;
  tasksStartedAt: string | null;
  lotterySubmitted: boolean;
  serverNow: string;
}

export interface ApiResult<T> {
  status: number;
  data: T | null;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/**
 * Lille fetch-wrapper med automatisk genforsøg ved netværksfejl og serverfejl,
 * så en ustabil wifi-forbindelse ikke koster data.
 */
@Injectable({ providedIn: 'root' })
export class Api {
  /** true mens vi venter på et genforsøg (vises som en diskret besked). */
  readonly retrying = signal(false);

  async request<T>(method: string, url: string, body?: unknown, headers: Record<string, string> = {}, retries = 6): Promise<ApiResult<T>> {
    for (let attempt = 0; ; attempt++) {
      try {
        const res = await fetch(url, {
          method,
          headers: { ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...headers },
          body: body !== undefined ? JSON.stringify(body) : undefined,
        });
        if (res.status >= 500 && attempt < retries) throw new Error(`HTTP ${res.status}`);
        this.retrying.set(false);
        const text = await res.text();
        let data: T | null = null;
        try { data = text ? (JSON.parse(text) as T) : null; } catch { data = null; }
        return { status: res.status, data };
      } catch (err) {
        if (attempt >= retries) {
          this.retrying.set(false);
          throw err;
        }
        this.retrying.set(true);
        await sleep(Math.min(8000, 500 * 2 ** attempt));
      }
    }
  }

  async download(url: string, adminKey: string): Promise<void> {
    const res = await fetch(url, { headers: { 'X-Admin-Key': adminKey } });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const name = /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? 'export';
    const blobUrl = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = blobUrl;
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(blobUrl), 1000);
  }
}

import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActionCenterResult } from '../models';
import { RealtimeService } from './realtime.service';

@Injectable({ providedIn: 'root' })
export class ActionCenterService {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly realtime = inject(RealtimeService);
  private baseUrl = '';
  private pollingInterval: ReturnType<typeof setInterval> | null = null;

  readonly items = signal<ActionCenterResult | null>(null);
  readonly count = signal(0);
  readonly isOpen = signal(false);
  readonly loading = signal(false);

  setBaseUrl(url: string): void {
    this.baseUrl = url;
  }

  loadCount(): void {
    this.http
      .get<{ count: number }>(`${this.baseUrl}/api/workspace/action-center/count`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => this.count.set(res.count),
        error: () => this.count.set(0),
      });
  }

  loadItems(): void {
    this.loading.set(true);
    this.http
      .get<ActionCenterResult>(`${this.baseUrl}/api/workspace/action-center`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.items.set(result);
          this.count.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
        },
      });
  }

  toggle(): void {
    const wasOpen = this.isOpen();
    this.isOpen.set(!wasOpen);
    if (!wasOpen) {
      this.loadItems();
    }
  }

  close(): void {
    this.isOpen.set(false);
  }

  /**
   * Default cadence is now a slow fallback (5 min). Realtime push on the
   * <c>action-center</c> channel does the heavy lifting — the poll only
   * exists so transports that can't establish a hub (corporate proxies
   * blocking WebSocket + long-polling, etc.) still see eventual updates.
   * If you genuinely want fast polling, pass a smaller intervalMs.
   */
  startPolling(intervalMs = 300_000): void {
    this.loadCount();
    this.pollingInterval = setInterval(() => this.loadCount(), intervalMs);

    // Realtime — re-fetch the count on every "changed" ping from the
    // server. Payload is empty by design; we always re-fetch so we never
    // disagree with the authoritative server count. If the dropdown is
    // currently open, also re-fetch the full items list so what's
    // visible stays in sync.
    this.realtime
      .onChannel('action-center')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.isOpen()) {
          this.loadItems();
        } else {
          this.loadCount();
        }
      });

    this.destroyRef.onDestroy(() => {
      if (this.pollingInterval) {
        clearInterval(this.pollingInterval);
      }
    });
  }
}

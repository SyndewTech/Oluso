import { Injectable, signal, computed } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
  type: 'success' | 'error' | 'warning' | 'info';
  /**
   * Optional handler fired when the toast body is clicked. Toasts that
   * deep-link (mentions, "approval requested", etc.) set this; system
   * messages without an action leave it null and stay non-interactive.
   */
  onClick?: () => void;
  /**
   * Optional short label hint (e.g. "View"). Surfaced as a chevron-style
   * affordance on the toast so users know clicking it does something.
   */
  actionLabel?: string;
}

export interface ToastOptions {
  onClick?: () => void;
  actionLabel?: string;
  /** Override the auto-dismiss duration in ms. */
  durationMs?: number;
}

@Injectable({
  providedIn: 'root',
})
export class ToastService {
  private nextId = 0;
  private _toasts = signal<Toast[]>([]);

  readonly toasts = computed(() => this._toasts());

  success(message: string, options?: ToastOptions): void {
    this.add(message, 'success', options);
  }

  error(message: string, options?: ToastOptions): void {
    this.add(message, 'error', options);
  }

  warning(message: string, options?: ToastOptions): void {
    this.add(message, 'warning', options);
  }

  info(message: string, options?: ToastOptions): void {
    this.add(message, 'info', options);
  }

  dismiss(id: number): void {
    this._toasts.update((toasts) => toasts.filter((t) => t.id !== id));
  }

  private add(message: string, type: Toast['type'], options?: ToastOptions): void {
    const id = this.nextId++;
    this._toasts.update((toasts) => [
      ...toasts,
      { id, message, type, onClick: options?.onClick, actionLabel: options?.actionLabel },
    ]);

    // Interactive toasts stick around longer so the user has time to
    // notice and click; otherwise default to 4s / 6s for errors.
    const duration = options?.durationMs
      ?? (options?.onClick ? 8000 : (type === 'error' ? 6000 : 4000));
    setTimeout(() => this.dismiss(id), duration);
  }
}

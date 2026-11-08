import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService, type Toast } from '../../services/toast.service';

/**
 * Renders toasts queued via {@link ToastService}. Drop one of these in
 * every app shell layout — the service is providedIn root and emits a
 * signal, so the same toast appears wherever a container is mounted.
 */
@Component({
  // Existing admin-shell templates already use `oluso-toast-container`;
  // keep the selector so moving this from admin-ui to ui-core doesn't
  // touch any consumer markup.
  selector: 'oluso-toast-container',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed top-4 right-4 z-[9999] flex flex-col gap-2 max-w-sm">
      @for (toast of toastService.toasts(); track toast.id) {
        <!-- Toast body — clickable when an onClick is wired. Close button
             stays separate so dismissing never triggers the action. -->
        <div
          class="rounded-lg shadow-lg px-4 py-3 flex items-start gap-3 animate-slide-in"
          [class]="getToastClasses(toast.type)"
          role="alert"
        >
          <button
            type="button"
            class="text-left flex-1 min-w-0"
            [class.cursor-pointer]="!!toast.onClick"
            [class.cursor-default]="!toast.onClick"
            [attr.disabled]="toast.onClick ? null : true"
            (click)="handleClick(toast)"
          >
            <span class="text-sm font-medium block">{{ toast.message }}</span>
            @if (toast.actionLabel && toast.onClick) {
              <span class="text-xs opacity-80 mt-0.5 inline-flex items-center gap-1">
                {{ toast.actionLabel }} →
              </span>
            }
          </button>
          <button
            type="button"
            (click)="toastService.dismiss(toast.id); $event.stopPropagation()"
            class="opacity-70 hover:opacity-100 text-lg leading-none shrink-0"
            aria-label="Dismiss"
          >
            &times;
          </button>
        </div>
      }
    </div>
  `,
  styles: [
    `
      @keyframes slideIn {
        from {
          transform: translateX(100%);
          opacity: 0;
        }
        to {
          transform: translateX(0);
          opacity: 1;
        }
      }
      .animate-slide-in {
        animation: slideIn 0.2s ease-out;
      }
    `,
  ],
})
export class ToastContainerComponent {
  protected readonly toastService = inject(ToastService);

  getToastClasses(type: string): string {
    switch (type) {
      case 'success': return 'bg-green-600 text-white';
      case 'error':   return 'bg-red-600 text-white';
      case 'warning': return 'bg-yellow-500 text-white';
      case 'info':    return 'bg-blue-600 text-white';
      default:        return 'bg-gray-700 text-white';
    }
  }

  /**
   * Fire the toast's onClick (if any) and dismiss it. The dismiss is
   * eager so the toast doesn't reappear while the user is navigating
   * away to the destination.
   */
  handleClick(toast: Toast): void {
    if (!toast.onClick) return;
    this.toastService.dismiss(toast.id);
    toast.onClick();
  }
}

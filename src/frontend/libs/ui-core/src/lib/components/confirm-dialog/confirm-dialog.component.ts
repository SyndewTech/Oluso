import { Component, input, output, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export type ConfirmDialogVariant = 'danger' | 'warning' | 'info' | 'default';

@Component({
  selector: 'ui-confirm-dialog',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    @if (open()) {
      <div
        class="fixed inset-0 z-50 overflow-y-auto"
        role="dialog"
        aria-modal="true"
      >
        <!-- Backdrop -->
        <div
          class="fixed inset-0 bg-black/50 transition-opacity"
          (click)="onBackdropClick()"
        ></div>

        <!-- Dialog container -->
        <div class="flex min-h-full items-center justify-center p-4">
          <div
            class="relative bg-white rounded-lg shadow-xl transform transition-all w-full max-w-md"
            (click)="$event.stopPropagation()"
          >
            <!-- Icon and Content -->
            <div class="p-6">
              <div class="flex items-start">
                <!-- Icon -->
                <div [class]="iconContainerClasses()">
                  @switch (variant()) {
                    @case ('danger') {
                      <ui-icon name="exclamation-triangle" class="h-6 w-6 text-red-600" strokeWidth="1.5" />
                    }
                    @case ('warning') {
                      <ui-icon name="exclamation-circle" class="h-6 w-6 text-yellow-600" strokeWidth="1.5" />
                    }
                    @case ('info') {
                      <ui-icon name="information-circle" class="h-6 w-6 text-blue-600" strokeWidth="1.5" />
                    }
                    @default {
                      <ui-icon name="question-mark-circle" class="h-6 w-6 text-gray-600" strokeWidth="1.5" />
                    }
                  }
                </div>

                <!-- Content -->
                <div class="ml-4 flex-1">
                  <h3 class="text-lg font-semibold text-gray-900">
                    {{ title() }}
                  </h3>
                  @if (message()) {
                    <p class="mt-2 text-sm text-gray-500">
                      {{ message() }}
                    </p>
                  }
                </div>
              </div>
            </div>

            <!-- Footer -->
            <div class="bg-gray-50 px-6 py-4 flex justify-end gap-3 rounded-b-lg">
              <button
                type="button"
                class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500"
                (click)="onCancel()"
                [disabled]="loading()"
              >
                {{ cancelText() }}
              </button>
              <button
                type="button"
                [class]="confirmButtonClasses()"
                (click)="onConfirm()"
                [disabled]="loading()"
              >
                @if (loading()) {
                  <svg class="animate-spin -ml-1 mr-2 h-4 w-4" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                }
                {{ confirmText() }}
              </button>
            </div>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmDialogComponent {
  readonly open = input<boolean>(false);
  readonly title = input<string>('Confirm Action');
  readonly message = input<string>('');
  readonly confirmText = input<string>('Confirm');
  readonly cancelText = input<string>('Cancel');
  readonly variant = input<ConfirmDialogVariant>('default');
  readonly loading = input<boolean>(false);
  /** Whether clicking the backdrop closes the dialog. Default: true */
  readonly closeOnBackdrop = input<boolean>(true);
  /** Whether pressing Escape closes the dialog. Default: true */
  readonly closeOnEscape = input<boolean>(true);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    if (this.open() && !this.loading() && this.closeOnEscape()) {
      this.onCancel();
    }
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop() && !this.loading()) {
      this.onCancel();
    }
  }

  iconContainerClasses(): string {
    const baseClasses = 'flex-shrink-0 flex items-center justify-center h-12 w-12 rounded-full';

    switch (this.variant()) {
      case 'danger':
        return `${baseClasses} bg-red-100`;
      case 'warning':
        return `${baseClasses} bg-yellow-100`;
      case 'info':
        return `${baseClasses} bg-blue-100`;
      default:
        return `${baseClasses} bg-gray-100`;
    }
  }

  confirmButtonClasses(): string {
    const baseClasses = 'inline-flex items-center px-4 py-2 text-sm font-medium rounded-md focus:outline-none focus:ring-2 focus:ring-offset-2 disabled:opacity-50';

    switch (this.variant()) {
      case 'danger':
        return `${baseClasses} text-white bg-red-600 hover:bg-red-700 focus:ring-red-500`;
      case 'warning':
        return `${baseClasses} text-white bg-yellow-600 hover:bg-yellow-700 focus:ring-yellow-500`;
      case 'info':
        return `${baseClasses} text-white bg-blue-600 hover:bg-blue-700 focus:ring-blue-500`;
      default:
        return `${baseClasses} text-white bg-primary-600 hover:bg-primary-700 focus:ring-primary-500`;
    }
  }

  onConfirm(): void {
    if (!this.loading()) {
      this.confirmed.emit();
    }
  }

  onCancel(): void {
    if (!this.loading()) {
      this.cancelled.emit();
    }
  }
}

import { Component, input, output, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export type SlidePanelSize = 'sm' | 'md' | 'lg' | 'xl' | '2xl' | '3xl' | '4xl';

@Component({
  selector: 'ui-slide-panel',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    @if (open()) {
      <!-- Backdrop -->
      <div
        class="fixed inset-0 z-[55] bg-black/30 transition-opacity"
        (click)="onBackdropClick()"
      ></div>

      <!-- Panel - sits below the workspace top bar (h-14 = 3.5rem) -->
      <div
        class="fixed top-14 bottom-0 right-0 z-[55] flex"
      >
        <div
          [class]="panelClasses()"
          (click)="$event.stopPropagation()"
        >
          <!-- Header -->
          <div class="flex items-center justify-between px-6 py-4 border-b border-gray-200 bg-white">
            <h2 class="text-lg font-semibold text-gray-900">{{ title() }}</h2>
            @if (showCloseButton()) {
              <button
                type="button"
                class="rounded-md p-1 text-gray-400 hover:text-gray-600 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-primary-500 transition-colors"
                (click)="close()"
              >
                <span class="sr-only">Close</span>
                <ui-icon name="x-mark" class="h-5 w-5" />
              </button>
            }
          </div>

          <!-- Body -->
          <div class="flex-1 overflow-y-auto px-6 py-4">
            <ng-content></ng-content>
          </div>

          <!-- Footer -->
          <ng-content select="[panel-footer]"></ng-content>
        </div>
      </div>
    }
  `,
})
export class SlidePanelComponent {
  readonly open = input<boolean>(false);
  readonly title = input<string>('');
  readonly size = input<SlidePanelSize>('md');
  readonly showCloseButton = input<boolean>(true);
  readonly closeOnBackdrop = input<boolean>(true);
  readonly closeOnEscape = input<boolean>(true);

  readonly closed = output<void>();

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    if (this.open() && this.closeOnEscape()) {
      this.close();
    }
  }

  panelClasses(): string {
    // As wide as the size allows (never wider than the screen), not just as wide as the content.
    const base = 'relative flex flex-col bg-white shadow-2xl w-screen h-full';
    const sizeClasses: Record<SlidePanelSize, string> = {
      sm: 'max-w-sm',
      md: 'max-w-md',
      lg: 'max-w-lg',
      xl: 'max-w-xl',
      '2xl': 'max-w-2xl',
      '3xl': 'max-w-3xl',
      '4xl': 'max-w-4xl',
    };
    return `${base} ${sizeClasses[this.size()]}`;
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop()) {
      this.close();
    }
  }

  close(): void {
    this.closed.emit();
  }
}

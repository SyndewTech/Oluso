import { Component, input, output, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export type ModalSize = 'sm' | 'md' | 'lg' | 'xl' | '2xl' | 'full';

@Component({
  selector: 'ui-modal',
  standalone: true,
  imports: [CommonModule, IconComponent],
  templateUrl: './modal.component.html',
})
export class ModalComponent {
  readonly open = input<boolean>(false);
  readonly title = input<string>();
  readonly size = input<ModalSize>('md');
  readonly showCloseButton = input<boolean>(true);
  readonly closeOnBackdrop = input<boolean>(true);
  readonly closeOnEscape = input<boolean>(true);
  readonly scrollable = input<boolean>(false);

  readonly closed = output<void>();

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    if (this.open() && this.closeOnEscape()) {
      this.close();
    }
  }

  modalClasses(): string {
    const baseClasses =
      'relative bg-white rounded-lg shadow-xl transform transition-all w-full';

    const sizeClasses: Record<ModalSize, string> = {
      sm: 'max-w-sm',
      md: 'max-w-md',
      lg: 'max-w-lg',
      xl: 'max-w-xl',
      '2xl': 'max-w-2xl',
      full: 'max-w-4xl',
    };

    const scrollClass = this.scrollable() ? 'max-h-[90vh] flex flex-col' : '';

    return `${baseClasses} ${sizeClasses[this.size()]} ${scrollClass}`;
  }

  bodyClasses(): string {
    return this.scrollable() ? 'px-6 py-4 overflow-y-auto flex-1' : 'px-6 py-4';
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

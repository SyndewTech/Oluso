import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './card.component.html',
  host: {
    class: 'block',
  },
})
export class CardComponent {
  readonly title = input<string>();
  readonly subtitle = input<string>();
  readonly padding = input<boolean>(true);
  readonly shadow = input<'none' | 'sm' | 'md' | 'lg'>('sm');

  hasFooter = false;

  cardClasses(): string {
    const shadowClasses: Record<string, string> = {
      none: '',
      sm: 'shadow-sm',
      md: 'shadow-md',
      lg: 'shadow-lg',
    };

    return `bg-white overflow-hidden rounded-lg ${shadowClasses[this.shadow()]}`;
  }

  bodyClasses(): string {
    return this.padding() ? 'px-4 py-5 sm:p-6' : '';
  }
}

import { Component, inject } from '@angular/core';
import { FeedbackService } from '../../services/feedback.service';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'ui-feedback-fab',
  standalone: true,
  imports: [IconComponent],
  template: `
    @if (!feedbackService.isOpen()) {
      <button
        type="button"
        class="fixed bottom-6 z-40 flex items-center justify-center w-12 h-12 rounded-full bg-primary-600 text-white shadow-lg hover:bg-primary-700 hover:shadow-xl active:bg-primary-800 transition-all duration-200"
        [style.right.px]="feedbackService.fabRightOffset"
        (click)="feedbackService.toggle()"
        title="Send Feedback"
      >
        <ui-icon name="chat-bubble-left" class="h-5 w-5" />
      </button>
    }
  `,
})
export class FeedbackFabComponent {
  readonly feedbackService = inject(FeedbackService);
}

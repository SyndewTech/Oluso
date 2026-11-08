import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FeedbackService } from '../../services/feedback.service';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'ui-feedback-panel',
  standalone: true,
  imports: [FormsModule, IconComponent],
  template: `
    @if (feedbackService.isOpen()) {
      <div
        class="fixed top-0 right-0 bottom-0 z-[55] flex flex-col bg-white border-l border-gray-200 shadow-lg w-[400px]"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-4 py-3 border-b border-gray-200 bg-gray-50 shrink-0">
          <h3 class="text-sm font-semibold text-gray-900">Send Feedback</h3>
          <button
            type="button"
            class="p-1.5 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 transition-colors"
            (click)="feedbackService.close()"
            title="Close feedback panel"
          >
            <ui-icon name="close" class="h-4 w-4" />
          </button>
        </div>

        @if (feedbackService.submitSuccess()) {
          <!-- Success state -->
          <div class="flex-1 flex flex-col items-center justify-center px-6 text-center">
            <div class="w-12 h-12 rounded-full bg-green-100 flex items-center justify-center mb-4">
              <ui-icon name="check" class="h-6 w-6 text-green-600" />
            </div>
            <h4 class="text-lg font-semibold text-gray-900 mb-2">Thank you!</h4>
            <p class="text-sm text-gray-500 mb-6">Your feedback has been submitted and will be reviewed by the team.</p>
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium text-primary-600 bg-primary-50 rounded-lg hover:bg-primary-100 transition-colors"
              (click)="sendAnother()"
            >
              Send another
            </button>
          </div>
        } @else {
          <!-- Form -->
          <div class="flex-1 overflow-y-auto px-4 py-4 space-y-4">
            <!-- Category selector -->
            <div>
              <label class="block text-xs font-medium text-gray-500 uppercase tracking-wider mb-2">Category</label>
              <div class="flex gap-2">
                @for (cat of categories; track cat.value) {
                  <button
                    type="button"
                    class="flex-1 px-3 py-2 text-sm font-medium rounded-lg border transition-colors"
                    [class.border-primary-500]="category() === cat.value"
                    [class.bg-primary-50]="category() === cat.value"
                    [class.text-primary-700]="category() === cat.value"
                    [class.border-gray-200]="category() !== cat.value"
                    [class.text-gray-600]="category() !== cat.value"
                    [class.hover:border-gray-300]="category() !== cat.value"
                    (click)="category.set(cat.value)"
                  >
                    {{ cat.label }}
                  </button>
                }
              </div>
            </div>

            <!-- Title -->
            <div>
              <label for="feedback-title" class="block text-sm font-medium text-gray-700 mb-1">Title</label>
              <input
                id="feedback-title"
                type="text"
                [(ngModel)]="title"
                placeholder="Brief summary of your feedback"
                class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-primary-500 placeholder:text-gray-400"
                maxlength="200"
              />
            </div>

            <!-- Description -->
            <div>
              <label for="feedback-desc" class="block text-sm font-medium text-gray-700 mb-1">Description</label>
              <textarea
                id="feedback-desc"
                [(ngModel)]="description"
                placeholder="Describe the issue or suggestion in detail..."
                rows="4"
                class="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-primary-500 placeholder:text-gray-400 resize-none"
              ></textarea>
            </div>

            <!-- Screenshot -->
            <div>
              <div class="flex items-center justify-between mb-2">
                <label class="block text-sm font-medium text-gray-700">Screenshot</label>
                <button
                  type="button"
                  class="text-xs font-medium text-primary-600 hover:text-primary-700 transition-colors"
                  [disabled]="feedbackService.isCapturing()"
                  (click)="captureScreenshot()"
                >
                  @if (feedbackService.isCapturing()) {
                    Capturing...
                  } @else if (feedbackService.screenshot()) {
                    Retake
                  } @else {
                    Capture Screenshot
                  }
                </button>
              </div>
              @if (feedbackService.screenshot(); as screenshot) {
                <div class="relative rounded-lg border border-gray-200 overflow-hidden">
                  <img
                    [src]="screenshot"
                    alt="Screenshot preview"
                    class="w-full h-auto max-h-48 object-cover object-top"
                  />
                  <button
                    type="button"
                    class="absolute top-1 right-1 p-1 rounded-md bg-black/50 text-white hover:bg-black/70 transition-colors"
                    (click)="feedbackService.screenshot.set(null)"
                    title="Remove screenshot"
                  >
                    <ui-icon name="close" class="h-3 w-3" />
                  </button>
                </div>
              } @else {
                <div class="flex items-center justify-center h-24 rounded-lg border-2 border-dashed border-gray-200 text-sm text-gray-400">
                  No screenshot captured
                </div>
              }
            </div>

            <!-- Error -->
            @if (feedbackService.submitError(); as error) {
              <div class="p-3 rounded-lg bg-red-50 text-sm text-red-600">
                {{ error }}
              </div>
            }
          </div>

          <!-- Footer -->
          <div class="shrink-0 px-4 py-3 border-t border-gray-200 bg-gray-50 flex items-center justify-end gap-2">
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
              (click)="feedbackService.close()"
            >
              Cancel
            </button>
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              [disabled]="!canSubmit() || feedbackService.isSubmitting()"
              (click)="submit()"
            >
              @if (feedbackService.isSubmitting()) {
                <span class="flex items-center gap-2">
                  <span class="h-4 w-4 border-2 border-white/30 border-t-white rounded-full animate-spin"></span>
                  Submitting...
                </span>
              } @else {
                Submit
              }
            </button>
          </div>
        }
      </div>
    }
  `,
})
export class FeedbackPanelComponent {
  readonly feedbackService = inject(FeedbackService);

  title = '';
  description = '';
  readonly category = signal<'bug' | 'feature' | 'note'>('bug');

  readonly categories = [
    { value: 'bug' as const, label: 'Bug' },
    { value: 'feature' as const, label: 'Feature' },
    { value: 'note' as const, label: 'Note' },
  ];

  canSubmit(): boolean {
    return this.title.trim().length > 0 && this.description.trim().length > 0;
  }

  async captureScreenshot(): Promise<void> {
    // Temporarily hide the panel for cleaner screenshot
    this.feedbackService.isOpen.set(false);
    // Wait for Angular change detection + DOM repaint
    await new Promise((r) => setTimeout(r, 300));
    await this.feedbackService.captureScreenshot();
    this.feedbackService.isOpen.set(true);
  }

  async submit(): Promise<void> {
    await this.feedbackService.submit({
      title: this.title,
      description: this.description,
      category: this.category(),
    });
  }

  sendAnother(): void {
    this.title = '';
    this.description = '';
    this.category.set('bug');
    this.feedbackService.reset();
  }
}

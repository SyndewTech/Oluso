import { Component, HostListener, computed, effect, input, output, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export type PromptDialogInputType = 'text' | 'number' | 'date' | 'datetime-local' | 'textarea';

/**
 * Asks for one value: a title, an optional message, one input and Confirm / Cancel.
 * `confirmed` emits the trimmed value once it passes validation (`required`, `maxLength`
 * and the optional `validate` callback, which returns an error message or null).
 * The input resets to `initialValue` each time the dialog opens.
 */
@Component({
  selector: 'ui-prompt-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    @if (open()) {
      <div class="fixed inset-0 z-50 overflow-y-auto" role="dialog" aria-modal="true" [attr.aria-labelledby]="titleId">
        <div class="fixed inset-0 bg-black/50 transition-opacity" (click)="onBackdropClick()"></div>

        <div class="flex min-h-full items-center justify-center p-4">
          <form
            class="relative bg-white rounded-lg shadow-xl transform transition-all w-full max-w-md"
            (click)="$event.stopPropagation()"
            (ngSubmit)="onConfirm()"
          >
            <div class="p-6 space-y-3">
              <h3 [id]="titleId" class="text-lg font-semibold text-gray-900">{{ title() }}</h3>
              @if (message()) {
                <p class="text-sm text-gray-500">{{ message() }}</p>
              }
              <div>
                @if (label()) {
                  <label [for]="inputId" class="block text-sm font-medium text-gray-700">
                    {{ label() }}@if (required()) {<span class="text-red-500"> *</span>}
                  </label>
                }
                @if (inputType() === 'textarea') {
                  <textarea
                    [id]="inputId"
                    name="value"
                    rows="3"
                    [ngModel]="value()"
                    (ngModelChange)="onInput($event)"
                    [placeholder]="placeholder()"
                    [attr.maxlength]="maxLength()"
                    [attr.aria-invalid]="!!visibleError()"
                    class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:ring-primary-500 focus:border-primary-500 sm:text-sm"
                  ></textarea>
                } @else {
                  <input
                    [id]="inputId"
                    name="value"
                    [type]="inputType()"
                    [ngModel]="value()"
                    (ngModelChange)="onInput($event)"
                    [placeholder]="placeholder()"
                    [attr.maxlength]="maxLength()"
                    [attr.aria-invalid]="!!visibleError()"
                    class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:ring-primary-500 focus:border-primary-500 sm:text-sm"
                  />
                }
                @if (visibleError(); as err) {
                  <p class="mt-1 text-sm text-red-600">{{ err }}</p>
                }
              </div>
            </div>

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
                type="submit"
                class="inline-flex items-center px-4 py-2 text-sm font-medium rounded-md text-white bg-primary-600 hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500 disabled:opacity-50"
                [disabled]="loading()"
              >
                {{ confirmText() }}
              </button>
            </div>
          </form>
        </div>
      </div>
    }
  `,
})
export class PromptDialogComponent {
  private static nextId = 0;
  protected readonly titleId = `ui-prompt-title-${PromptDialogComponent.nextId}`;
  protected readonly inputId = `ui-prompt-input-${PromptDialogComponent.nextId++}`;

  readonly open = input<boolean>(false);
  readonly title = input<string>('');
  readonly message = input<string>('');
  readonly label = input<string>('');
  readonly placeholder = input<string>('');
  readonly initialValue = input<string>('');
  readonly inputType = input<PromptDialogInputType>('text');
  readonly required = input<boolean>(false);
  readonly maxLength = input<number | null>(null);
  /** Extra validation: return an error message for an invalid value, or null. */
  readonly validate = input<((value: string) => string | null) | null>(null);
  readonly confirmText = input<string>('OK');
  readonly cancelText = input<string>('Cancel');
  readonly loading = input<boolean>(false);
  readonly closeOnBackdrop = input<boolean>(true);
  readonly closeOnEscape = input<boolean>(true);

  readonly confirmed = output<string>();
  readonly cancelled = output<void>();

  protected readonly value = signal('');
  private readonly attempted = signal(false);

  /** The current validation error, whether or not it's shown yet. */
  readonly error = computed(() => {
    const v = this.value().trim();
    if (this.required() && v === '') return 'This field is required.';
    const max = this.maxLength();
    if (max != null && v.length > max) return `Use at most ${max} characters.`;
    const check = this.validate();
    return check ? check(v) : null;
  });

  protected readonly visibleError = computed(() => (this.attempted() ? this.error() : null));

  constructor() {
    effect(() => {
      if (this.open()) {
        const initial = this.initialValue();
        untracked(() => {
          this.value.set(initial);
          this.attempted.set(false);
        });
      }
    });
  }

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    if (this.open() && !this.loading() && this.closeOnEscape()) this.onCancel();
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop() && !this.loading()) this.onCancel();
  }

  onInput(value: unknown): void {
    this.value.set(value == null ? '' : String(value));
  }

  onConfirm(): void {
    if (this.loading()) return;
    this.attempted.set(true);
    if (this.error()) return;
    this.confirmed.emit(this.value().trim());
  }

  onCancel(): void {
    if (!this.loading()) this.cancelled.emit();
  }
}

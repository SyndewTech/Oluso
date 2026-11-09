import { Component, input, output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DynamicFieldConfig, DynamicFieldValue } from './dynamic-form.types';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'ui-dynamic-form-field',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <fieldset [disabled]="readonly()" class="m-0 min-w-0 border-0 p-0 disabled:[&_button]:pointer-events-none disabled:[&_input]:pointer-events-none disabled:[&_label]:pointer-events-none disabled:[&_select]:pointer-events-none disabled:[&_textarea]:pointer-events-none">
    <div class="space-y-2">
      <!-- Label -->
      <label class="block text-sm font-medium text-gray-700">
        {{ field().label }}
        @if (field().required) {
          <span class="text-red-500">*</span>
        }
      </label>

      <!-- Description -->
      @if (field().description) {
        <p class="text-sm text-gray-500">{{ field().description }}</p>
      }

      <!-- Field Input -->
      <div class="mt-1">
        @switch (field().type) {
          @case ('checkbox') {
            <label class="inline-flex items-center cursor-pointer">
              <input
                type="checkbox"
                [checked]="currentValue() === true"
                (change)="onCheckboxChange($event)"
                class="h-4 w-4 text-primary-600 focus:ring-primary-500 border-gray-300 rounded"
              />
              <span class="ml-2 text-sm text-gray-700">{{ field().label }}</span>
            </label>
          }

          @case ('yesno') {
            <div class="flex gap-2">
              <button
                type="button"
                (click)="setValue('yes')"
                [class]="getToggleButtonClass('yes')"
              >
                <ui-icon name="check" class="h-4 w-4 mr-1" />
                Yes
              </button>
              <button
                type="button"
                (click)="setValue('no')"
                [class]="getToggleButtonClass('no')"
              >
                <ui-icon name="x-mark" class="h-4 w-4 mr-1" />
                No
              </button>
            </div>
          }

          @case ('passfail') {
            <div class="flex gap-2">
              <button
                type="button"
                (click)="setValue('pass')"
                [class]="getPassFailButtonClass('pass')"
              >
                <ui-icon name="check-circle" class="h-4 w-4 mr-1" />
                Pass
              </button>
              <button
                type="button"
                (click)="setValue('fail')"
                [class]="getPassFailButtonClass('fail')"
              >
                <ui-icon name="x-circle" class="h-4 w-4 mr-1" />
                Fail
              </button>
            </div>
          }

          @case ('rating') {
            <div class="flex items-center gap-1">
              @for (star of ratingStars(); track star) {
                <button
                  type="button"
                  (click)="setValue(star)"
                  (mouseenter)="hoverRating.set(star)"
                  (mouseleave)="hoverRating.set(0)"
                  class="focus:outline-none"
                >
                  <ui-icon
                    name="star"
                    [class]="getStarClass(star)"
                  />
                </button>
              }
              @if (currentValue()) {
                <span class="ml-2 text-sm text-gray-600">
                  {{ currentValue() }} / {{ field().ratingMax || 5 }}
                </span>
              }
            </div>
          }

          @case ('text') {
            @if (field().multiline) {
              <textarea
                [value]="currentValue() ?? ''"
                (input)="onTextChange($event)"
                [attr.maxlength]="field().maxLength"
                rows="3"
                class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                [placeholder]="field().description || ''"
              ></textarea>
            } @else {
              <input
                type="text"
                [value]="currentValue() ?? ''"
                (input)="onTextChange($event)"
                [attr.maxlength]="field().maxLength"
                class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                [placeholder]="field().description || ''"
              />
            }
            @if (field().maxLength) {
              <p class="mt-1 text-xs text-gray-400">
                {{ (currentValue()?.toString() || '').length }} / {{ field().maxLength }}
              </p>
            }
          }

          @case ('number') {
            <input
              type="number"
              [value]="currentValue() ?? ''"
              (input)="onNumberChange($event)"
              [min]="field().min"
              [max]="field().max"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              [placeholder]="field().description || ''"
            />
            @if (field().min !== undefined || field().max !== undefined) {
              <p class="mt-1 text-xs text-gray-400">
                @if (field().min !== undefined && field().max !== undefined) {
                  Range: {{ field().min }} - {{ field().max }}
                } @else if (field().min !== undefined) {
                  Min: {{ field().min }}
                } @else if (field().max !== undefined) {
                  Max: {{ field().max }}
                }
              </p>
            }
          }

          @case ('photo') {
            <div class="space-y-2">
              <div class="flex flex-wrap gap-2">
                @for (url of photoUrls(); track url) {
                  <div class="relative group">
                    <img
                      [src]="url"
                      alt="Uploaded photo"
                      class="h-20 w-20 object-cover rounded-lg border border-gray-200"
                    />
                    @if (!readonly()) {
                      <button
                        type="button"
                        (click)="removePhoto(url)"
                        class="absolute -top-2 -right-2 bg-red-500 text-white rounded-full p-1 opacity-0 group-hover:opacity-100 transition-opacity"
                      >
                        <ui-icon name="x-mark" class="h-3 w-3" />
                      </button>
                    }
                  </div>
                }
              </div>
              @if (readonly()) {
                @if (photoUrls().length === 0) { <p class="text-sm text-gray-500">No photos</p> }
              } @else {
              <label class="inline-flex items-center px-3 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 cursor-pointer">
                <ui-icon name="camera" class="h-4 w-4 mr-2" />
                Add Photo
                <input
                  type="file"
                  accept="image/*"
                  (change)="onPhotoUpload($event)"
                  class="sr-only"
                  multiple
                />
              </label>
              }
            </div>
          }

          @case ('signature') {
            <div class="space-y-2">
              @if (currentValue()) {
                <div class="border border-gray-200 rounded-lg p-2 bg-gray-50">
                  <img
                    [src]="currentValue()"
                    alt="Signature"
                    class="max-h-24"
                  />
                </div>
                @if (!readonly()) {
                  <button
                    type="button"
                    (click)="clearSignature()"
                    class="text-sm text-red-600 hover:text-red-700"
                  >
                    Clear Signature
                  </button>
                }
              } @else if (readonly()) {
                <p class="text-sm text-gray-500">Not signed</p>
              } @else {
                <div
                  class="border-2 border-dashed border-gray-300 rounded-lg p-8 text-center cursor-pointer hover:border-primary-400 transition-colors"
                  (click)="openSignatureCapture()"
                >
                  <ui-icon name="pencil" class="h-8 w-8 mx-auto text-gray-400" />
                  <p class="mt-2 text-sm text-gray-600">Click to sign</p>
                </div>
              }
            </div>
          }

          @case ('select') {
            <select
              [value]="currentValue() ?? ''"
              (change)="onSelectChange($event)"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            >
              <option value="">Select an option...</option>
              @for (option of field().options || []; track option.value) {
                <option [value]="option.value">{{ option.label }}</option>
              }
            </select>
          }

          @case ('multiselect') {
            <div class="space-y-2">
              @for (option of field().options || []; track option.value) {
                <label class="inline-flex items-center mr-4">
                  <input
                    type="checkbox"
                    [checked]="isOptionSelected(option.value)"
                    (change)="toggleMultiselectOption(option.value)"
                    class="h-4 w-4 text-primary-600 focus:ring-primary-500 border-gray-300 rounded"
                  />
                  <span class="ml-2 text-sm text-gray-700">{{ option.label }}</span>
                </label>
              }
            </div>
          }
        }
      </div>

      <!-- Notes section -->
      @if (allowNotes() && readonly()) {
        @if (notes()) {
          <p class="mt-2 whitespace-pre-line rounded-md bg-gray-50 px-3 py-2 text-sm text-gray-700"><span class="font-medium">Notes:</span> {{ notes() }}</p>
        }
      } @else if (allowNotes()) {
        <div class="mt-2">
          <button
            type="button"
            (click)="showNotes.set(!showNotes())"
            class="text-sm text-primary-600 hover:text-primary-700 inline-flex items-center"
          >
            <ui-icon name="chat-bubble-left" class="h-4 w-4 mr-1" />
            {{ showNotes() ? 'Hide notes' : 'Add notes' }}
          </button>
          @if (showNotes()) {
            <textarea
              [value]="notes()"
              (input)="onNotesChange($event)"
              rows="2"
              class="mt-2 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              placeholder="Add notes..."
            ></textarea>
          }
        </div>
      }
    </div>
    </fieldset>
  `,
  host: {
    class: 'block',
  },
})
export class DynamicFormFieldComponent {
  readonly field = input.required<DynamicFieldConfig>();
  readonly value = input<DynamicFieldValue | null>(null);
  readonly allowNotes = input<boolean>(false);
  readonly allowPhotos = input<boolean>(false);
  /** Show the recorded answer without letting it be changed (e.g. a completed form). */
  readonly readonly = input<boolean>(false);

  readonly valueChange = output<DynamicFieldValue>();

  readonly hoverRating = signal(0);
  readonly showNotes = signal(false);

  readonly currentValue = computed(() => this.value()?.value ?? null);
  readonly notes = computed(() => this.value()?.notes ?? '');
  readonly photoUrls = computed(() => this.value()?.photoUrls ?? []);

  readonly ratingStars = computed(() => {
    const min = this.field().ratingMin ?? 1;
    const max = this.field().ratingMax ?? 5;
    return Array.from({ length: max - min + 1 }, (_, i) => min + i);
  });

  getToggleButtonClass(value: string): string {
    const baseClasses =
      'inline-flex items-center px-4 py-2 border rounded-md text-sm font-medium focus:outline-none focus:ring-2 focus:ring-offset-2 transition-colors';
    const isSelected = this.currentValue() === value;

    if (isSelected) {
      return `${baseClasses} border-primary-500 bg-primary-50 text-primary-700 focus:ring-primary-500`;
    }
    return `${baseClasses} border-gray-300 bg-white text-gray-700 hover:bg-gray-50 focus:ring-primary-500`;
  }

  getPassFailButtonClass(value: string): string {
    const baseClasses =
      'inline-flex items-center px-4 py-2 border rounded-md text-sm font-medium focus:outline-none focus:ring-2 focus:ring-offset-2 transition-colors';
    const isSelected = this.currentValue() === value;

    if (isSelected && value === 'pass') {
      return `${baseClasses} border-green-500 bg-green-50 text-green-700 focus:ring-green-500`;
    }
    if (isSelected && value === 'fail') {
      return `${baseClasses} border-red-500 bg-red-50 text-red-700 focus:ring-red-500`;
    }
    return `${baseClasses} border-gray-300 bg-white text-gray-700 hover:bg-gray-50 focus:ring-primary-500`;
  }

  getStarClass(star: number): string {
    const currentRating = (this.currentValue() as number) || 0;
    const hoverValue = this.hoverRating();
    const isHighlighted = hoverValue ? star <= hoverValue : star <= currentRating;

    return isHighlighted
      ? 'h-6 w-6 text-yellow-400 fill-current'
      : 'h-6 w-6 text-gray-300';
  }

  isOptionSelected(value: string): boolean {
    const currentVal = this.currentValue();
    if (Array.isArray(currentVal)) {
      return currentVal.includes(value);
    }
    return false;
  }

  setValue(value: string | number | boolean): void {
    this.emitChange(value);
  }

  onCheckboxChange(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.emitChange(checked);
  }

  onTextChange(event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.emitChange(value);
  }

  onNumberChange(event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.emitChange(isNaN(value) ? null : value);
  }

  onSelectChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.emitChange(value || null);
  }

  toggleMultiselectOption(optionValue: string): void {
    const currentVal = this.currentValue();
    const currentArray = Array.isArray(currentVal) ? [...currentVal] : [];

    const index = currentArray.indexOf(optionValue);
    if (index === -1) {
      currentArray.push(optionValue);
    } else {
      currentArray.splice(index, 1);
    }

    this.emitChange(currentArray.length > 0 ? currentArray : null);
  }

  onNotesChange(event: Event): void {
    const notes = (event.target as HTMLTextAreaElement).value;
    const currentFieldValue = this.value() || {
      fieldId: this.field().id,
      value: null,
    };
    this.valueChange.emit({
      ...currentFieldValue,
      notes: notes || undefined,
    });
  }

  onPhotoUpload(event: Event): void {
    const files = (event.target as HTMLInputElement).files;
    if (!files) return;

    // In a real implementation, you would upload these files to a server
    // and get back URLs. For now, we'll create object URLs.
    const newUrls = Array.from(files).map((file) => URL.createObjectURL(file));
    const currentUrls = this.photoUrls();

    const currentFieldValue = this.value() || {
      fieldId: this.field().id,
      value: null,
    };
    this.valueChange.emit({
      ...currentFieldValue,
      photoUrls: [...currentUrls, ...newUrls],
    });
  }

  removePhoto(url: string): void {
    const currentUrls = this.photoUrls().filter((u) => u !== url);
    const currentFieldValue = this.value() || {
      fieldId: this.field().id,
      value: null,
    };
    this.valueChange.emit({
      ...currentFieldValue,
      photoUrls: currentUrls.length > 0 ? currentUrls : undefined,
    });
  }

  clearSignature(): void {
    this.emitChange(null);
  }

  openSignatureCapture(): void {
    // Placeholder: a real implementation opens a signature capture modal/canvas
    // and emits the captured value. Parent can wire this up via an output.
  }

  private emitChange(value: string | number | boolean | string[] | null): void {
    const currentFieldValue = this.value() || {
      fieldId: this.field().id,
      value: null,
    };
    this.valueChange.emit({
      ...currentFieldValue,
      fieldId: this.field().id,
      value,
    });
  }
}

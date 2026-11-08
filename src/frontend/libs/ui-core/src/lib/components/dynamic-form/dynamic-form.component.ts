import { Component, input, output, computed, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  DynamicFormConfig,
  DynamicFieldConfig,
  DynamicFieldValue,
  DynamicFieldOutcome,
} from './dynamic-form.types';
import { DynamicFormFieldComponent } from './dynamic-form-field.component';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'ui-dynamic-form',
  standalone: true,
  imports: [CommonModule, DynamicFormFieldComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <!-- Progress Indicator -->
      @if (showProgress()) {
        <div class="mb-6">
          <div class="flex items-center justify-between text-sm text-gray-600 mb-2">
            <span>Progress</span>
            <span>{{ completedCount() }} / {{ totalRequiredCount() }} required fields</span>
          </div>
          <div class="w-full bg-gray-200 rounded-full h-2">
            <div
              class="bg-primary-600 h-2 rounded-full transition-all duration-300"
              [style.width.%]="progressPercentage()"
            ></div>
          </div>
          @if (allRequiredComplete()) {
            <p class="mt-2 text-sm text-green-600 flex items-center">
              <ui-icon name="check-circle" class="h-4 w-4 mr-1" />
              All required fields complete
            </p>
          }
        </div>
      }

      <!-- Form Fields -->
      <div class="space-y-6">
        @for (field of visibleFields(); track field.id) {
          <div
            class="p-4 bg-white border border-gray-200 rounded-lg"
            [class]="outcomeStripe(field)"
          >
            <ui-dynamic-form-field
              [field]="field"
              [value]="getFieldValue(field.id)"
              [allowNotes]="config().allowNotes ?? false"
              [allowPhotos]="config().allowPhotos ?? false"
              [readonly]="readonly()"
              (valueChange)="onFieldValueChange($event)"
            />
          </div>
        }
      </div>

      <!-- Validation Errors -->
      @if (showValidation() && validationErrors().length > 0) {
        <div class="mt-4 p-4 bg-red-50 border border-red-200 rounded-lg">
          <h4 class="text-sm font-medium text-red-800 flex items-center">
            <ui-icon name="exclamation-triangle" class="h-4 w-4 mr-2" />
            Please complete the following required fields:
          </h4>
          <ul class="mt-2 list-disc list-inside text-sm text-red-700">
            @for (error of validationErrors(); track error) {
              <li>{{ error }}</li>
            }
          </ul>
        </div>
      }

      <!-- Submit Section -->
      @if (showSubmitButton() && !readonly()) {
        <div class="flex items-center justify-end gap-3 pt-4 border-t border-gray-200">
          @if (showClearButton()) {
            <button
              type="button"
              (click)="onClear()"
              class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500"
            >
              Clear All
            </button>
          }
          <button
            type="button"
            (click)="onSubmit()"
            [disabled]="submitDisabled()"
            class="px-4 py-2 text-sm font-medium text-white bg-primary-600 border border-transparent rounded-md hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {{ submitLabel() }}
          </button>
        </div>
      }
    </div>
  `,
  host: {
    class: 'block',
  },
})
export class DynamicFormComponent {
  readonly config = input.required<DynamicFormConfig>();
  readonly initialValues = input<DynamicFieldValue[]>([]);
  readonly showProgress = input<boolean>(true);
  readonly showSubmitButton = input<boolean>(true);
  readonly showClearButton = input<boolean>(true);
  readonly submitLabel = input<string>('Submit');
  readonly requireAllFields = input<boolean>(false);
  /** Render the answers without letting them be changed; no value changes are emitted. */
  readonly readonly = input<boolean>(false);

  readonly formValueChange = output<DynamicFieldValue[]>();
  readonly formSubmit = output<DynamicFieldValue[]>();
  readonly formClear = output<void>();

  readonly values = signal<Map<string, DynamicFieldValue>>(new Map());
  readonly showValidation = signal(false);

  constructor() {
    // Initialize values from initialValues input
    effect(
      () => {
        const initial = this.initialValues();
        if (initial.length > 0) {
          const valueMap = new Map<string, DynamicFieldValue>();
          initial.forEach((v) => valueMap.set(v.fieldId, v));
          this.values.set(valueMap);
        }
      },
      { allowSignalWrites: true }
    );
  }

  readonly sortedFields = computed(() => {
    const fields = [...this.config().fields];
    return fields.sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
  });

  readonly visibleFields = computed(() => {
    return this.sortedFields().filter((field) => this.isFieldVisible(field));
  });

  readonly requiredFields = computed(() => {
    return this.visibleFields().filter((f) => f.required);
  });

  readonly totalRequiredCount = computed(() => {
    return this.requiredFields().length;
  });

  readonly completedCount = computed(() => {
    return this.requiredFields().filter((f) => this.isFieldComplete(f.id)).length;
  });

  readonly progressPercentage = computed(() => {
    const total = this.totalRequiredCount();
    if (total === 0) return 100;
    return Math.round((this.completedCount() / total) * 100);
  });

  readonly allRequiredComplete = computed(() => {
    return this.completedCount() === this.totalRequiredCount();
  });

  readonly validationErrors = computed(() => {
    const errors: string[] = [];
    const fieldsToValidate = this.requireAllFields()
      ? this.visibleFields()
      : this.requiredFields();

    fieldsToValidate.forEach((field) => {
      if (!this.isFieldComplete(field.id)) {
        errors.push(field.label);
      }
    });

    return errors;
  });

  readonly submitDisabled = computed(() => {
    if (this.requireAllFields()) {
      return this.visibleFields().some((f) => !this.isFieldComplete(f.id));
    }
    return !this.allRequiredComplete();
  });

  isFieldVisible(field: DynamicFieldConfig): boolean {
    if (!field.visibleWhen) return true;

    const { fieldId, equals } = field.visibleWhen;
    const dependentValue = this.values().get(fieldId);

    if (!dependentValue) return false;

    return dependentValue.value === equals;
  }

  /** The answer's result: the owner's verdict when given, else the field's passing value, else Pass/Fail and Yes/No. */
  fieldOutcome(field: DynamicFieldConfig): DynamicFieldOutcome | null {
    if (!this.isFieldComplete(field.id)) return null;
    const answer = this.values().get(field.id)!;
    if (answer.outcome) return answer.outcome;
    const raw = Array.isArray(answer.value) ? answer.value.join(',') : String(answer.value);
    if (field.passingValue != null && field.passingValue !== '') {
      return raw.toLowerCase() === field.passingValue.toLowerCase() ? 'pass' : 'fail';
    }
    if (field.type === 'passfail') return raw === 'pass' ? 'pass' : 'fail';
    return 'neutral';
  }

  outcomeStripe(field: DynamicFieldConfig): string {
    switch (this.fieldOutcome(field)) {
      case 'pass': return 'border-l-4 border-l-green-500';
      case 'fail': return field.critical ? 'border-l-4 border-l-red-500' : 'border-l-4 border-l-amber-400';
      case 'neutral': return 'border-l-4 border-l-primary-400';
      default: return '';
    }
  }

  isFieldComplete(fieldId: string): boolean {
    const value = this.values().get(fieldId);
    if (!value) return false;

    const val = value.value;

    // Check for null/undefined
    if (val === null || val === undefined) return false;

    // Check for empty string
    if (typeof val === 'string' && val.trim() === '') return false;

    // Check for empty array
    if (Array.isArray(val) && val.length === 0) return false;

    return true;
  }

  getFieldValue(fieldId: string): DynamicFieldValue | null {
    return this.values().get(fieldId) ?? null;
  }

  onFieldValueChange(fieldValue: DynamicFieldValue): void {
    if (this.readonly()) return;
    const currentValues = new Map(this.values());
    currentValues.set(fieldValue.fieldId, fieldValue);
    this.values.set(currentValues);

    // Emit all values
    this.formValueChange.emit(Array.from(currentValues.values()));
  }

  onSubmit(): void {
    this.showValidation.set(true);

    if (this.submitDisabled()) {
      return;
    }

    this.formSubmit.emit(Array.from(this.values().values()));
  }

  onClear(): void {
    this.values.set(new Map());
    this.showValidation.set(false);
    this.formClear.emit();
    this.formValueChange.emit([]);
  }
}

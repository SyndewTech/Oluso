import { Component, input, output, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR, FormsModule } from '@angular/forms';

export type InputType = 'text' | 'email' | 'password' | 'number' | 'tel' | 'url' | 'search';

@Component({
  selector: 'ui-input',
  standalone: true,
  imports: [CommonModule, FormsModule],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => InputComponent),
      multi: true,
    },
  ],
  templateUrl: './input.component.html',
})
export class InputComponent implements ControlValueAccessor {
  readonly type = input<InputType>('text');
  readonly label = input<string>();
  readonly placeholder = input<string>('');
  readonly disabled = input<boolean>(false);
  readonly readonly = input<boolean>(false);
  readonly required = input<boolean>(false);
  readonly error = input<string>();
  readonly hint = input<string>();
  readonly inputId = input<string>(`input-${Math.random().toString(36).slice(2)}`);
  readonly prefixIcon = input<boolean>(false);
  readonly suffixIcon = input<boolean>(false);

  readonly valueChange = output<string>();

  value = '';
  onChange: (value: string) => void = () => {};
  onTouched: () => void = () => {};

  inputClasses(): string {
    const baseClasses =
      'block w-full rounded-md shadow-sm sm:text-sm transition-colors';

    const stateClasses = this.error()
      ? 'border-red-300 text-red-900 placeholder-red-300 focus:ring-red-500 focus:border-red-500'
      : 'border-gray-300 focus:ring-primary-500 focus:border-primary-500';

    const disabledClasses = this.disabled() ? 'bg-gray-100 cursor-not-allowed' : '';

    const paddingClasses = this.prefixIcon()
      ? 'pl-10'
      : this.suffixIcon()
        ? 'pr-10'
        : '';

    return `${baseClasses} ${stateClasses} ${disabledClasses} ${paddingClasses}`;
  }

  onValueChange(value: string): void {
    this.value = value;
    this.onChange(value);
    this.valueChange.emit(value);
  }

  writeValue(value: string): void {
    this.value = value || '';
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    // The disabled state is handled via the input signal
  }
}

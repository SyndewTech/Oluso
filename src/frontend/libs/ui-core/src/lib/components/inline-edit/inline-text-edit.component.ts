import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '../../services/toast.service';

/**
 * Click-to-edit text field. Replaces modal dialogs for simple single-line
 * field updates (name, email, phone, dates, etc.).
 *
 * Usage:
 *   <ui-inline-text-edit
 *     [value]="user.email"
 *     placeholder="No email on file"
 *     label="Email"
 *     type="email"
 *     [required]="true"
 *     (saved)="updateEmail($event)"
 *   />
 */
@Component({
  selector: 'ui-inline-text-edit',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="group relative w-full">
      @if (mode() === 'display') {
        <button
          type="button"
          (click)="enterEdit()"
          class="flex w-full items-center gap-2 rounded px-2 py-1 text-left text-sm text-slate-800
                 hover:bg-slate-50 focus:bg-slate-50 focus:outline-none focus:ring-1 focus:ring-indigo-400"
          [attr.aria-label]="'Edit ' + (label || 'value')"
        >
          @if (value) {
            <span class="truncate">{{ value }}</span>
          } @else {
            <span class="italic text-slate-400">{{ placeholder }}</span>
          }
          <svg
            class="ml-auto h-3.5 w-3.5 flex-shrink-0 text-slate-400 opacity-0 transition-opacity
                   group-hover:opacity-100 group-focus-within:opacity-100"
            fill="none" stroke="currentColor" viewBox="0 0 24 24"
          >
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
              d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
          </svg>
        </button>
      } @else {
        <div class="flex flex-col gap-1">
          @if (label) {
            <label class="text-xs font-medium text-slate-600">{{ label }}</label>
          }
          <input
            #editInput
            [type]="type"
            [value]="draft()"
            (input)="draft.set($any($event.target).value)"
            (blur)="save()"
            (keydown)="onKeydown($event)"
            [attr.placeholder]="placeholder"
            class="w-full rounded-md border border-indigo-400 bg-white px-2 py-1 text-sm text-slate-900
                   placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none
                   focus:ring-1 focus:ring-indigo-500"
            [class.border-rose-500]="error()"
            [class.focus:border-rose-500]="error()"
            [class.focus:ring-rose-500]="error()"
          />
          @if (error()) {
            <span class="text-xs text-rose-600">{{ error() }}</span>
          }
        </div>
      }
    </div>
  `,
})
export class InlineTextEditComponent {
  @Input() value: string | null = '';
  @Input() placeholder = 'Click to add';
  @Input() label = '';
  @Input() type: 'text' | 'email' | 'tel' | 'date' = 'text';
  @Input() required = false;

  @Output() saved = new EventEmitter<string>();

  @ViewChild('editInput') editInput?: ElementRef<HTMLInputElement>;

  private readonly toast = inject(ToastService);

  readonly mode = signal<'display' | 'editing'>('display');
  readonly draft = signal('');
  readonly error = signal<string | null>(null);
  private original = '';
  private suppressBlur = false;

  enterEdit(): void {
    this.original = this.value ?? '';
    this.draft.set(this.original);
    this.error.set(null);
    this.mode.set('editing');
    setTimeout(() => {
      const el = this.editInput?.nativeElement;
      if (el) {
        el.focus();
        el.select();
      }
    });
  }

  save(): void {
    if (this.suppressBlur) {
      this.suppressBlur = false;
      return;
    }
    const next = this.draft().trim();
    if (this.required && !next) {
      this.error.set(`${this.label || 'Value'} is required`);
      setTimeout(() => this.editInput?.nativeElement.focus());
      return;
    }
    this.error.set(null);
    if (next === this.original) {
      this.mode.set('display');
      return;
    }
    this.value = next;
    this.mode.set('display');
    this.saved.emit(next);
    this.toast.success('Saved');
  }

  cancel(): void {
    this.suppressBlur = true;
    this.draft.set(this.original);
    this.error.set(null);
    this.mode.set('display');
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.editInput?.nativeElement.blur();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.cancel();
    }
  }
}

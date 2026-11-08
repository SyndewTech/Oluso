import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';

/**
 * A single bulk action definition used by {@link BulkActionsToolbarComponent}.
 *
 * `key` is emitted via `actionClicked` when the user triggers the action;
 * hosts use it to dispatch the corresponding operation (confirm, cancel, etc.).
 */
export interface BulkAction {
  key: string;
  label: string;
  icon?: string;
  variant?: 'default' | 'primary' | 'danger' | 'success';
  requireConfirm?: boolean;
}

/**
 * Floating bulk-action toolbar for list pages.
 *
 * Renders a fixed, centered card at the bottom of the viewport whenever the
 * parent reports a non-zero `selectedCount`. Provides "select all", "clear",
 * and a host-defined list of action buttons. Actions flagged with
 * `requireConfirm` display an inline confirmation overlay before the event
 * is emitted, so destructive operations never fire on a stray click.
 *
 * Example:
 *   <ui-bulk-actions-toolbar
 *     [selectedCount]="selection().length"
 *     [totalCount]="rows().length"
 *     [actions]="bulkActions"
 *     (actionClicked)="runBulk($event)"
 *     (selectAll)="selectAllRows()"
 *     (clearSelection)="clearSelection()"
 *   />
 */
@Component({
  selector: 'ui-bulk-actions-toolbar',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (selectedCount > 0) {
      <div
        class="fixed bottom-6 left-1/2 z-40 w-[calc(100%-1.5rem)] max-w-3xl -translate-x-1/2 transform
               transition-all duration-200 ease-out
               motion-safe:animate-[slideUp_200ms_ease-out]"
        role="region"
        aria-label="Bulk actions"
      >
        <div
          class="flex flex-col gap-3 rounded-xl bg-white px-4 py-3 shadow-2xl ring-1 ring-black/5
                 sm:flex-row sm:items-center sm:justify-between sm:gap-4 sm:px-5 sm:py-3"
        >
          <!-- Left: selection summary and bulk selection controls -->
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <span
              class="inline-flex items-center gap-1.5 font-semibold text-slate-900"
              aria-live="polite"
            >
              <span
                class="inline-flex h-6 min-w-[1.5rem] items-center justify-center rounded-full
                       bg-indigo-100 px-1.5 text-xs font-semibold text-indigo-700"
              >
                {{ selectedCount }}
              </span>
              <span>selected</span>
            </span>

            @if (totalCount > selectedCount) {
              <button
                type="button"
                class="rounded-md px-2 py-1 text-xs font-medium text-indigo-600 transition-colors
                       hover:bg-indigo-50 hover:text-indigo-700 focus:outline-none
                       focus-visible:ring-2 focus-visible:ring-indigo-500"
                (click)="onSelectAll()"
              >
                Select all {{ totalCount }}
              </button>
            }

            <button
              type="button"
              class="rounded-md px-2 py-1 text-xs font-medium text-slate-500 transition-colors
                     hover:bg-slate-100 hover:text-slate-700 focus:outline-none
                     focus-visible:ring-2 focus-visible:ring-slate-400"
              (click)="onClear()"
            >
              Clear
            </button>
          </div>

          <!-- Right: action buttons -->
          <div class="flex flex-wrap items-center justify-end gap-2">
            @for (action of actions; track action.key) {
              <button
                type="button"
                [class]="buttonClassFor(action)"
                [attr.aria-label]="action.label"
                (click)="onActionClick(action)"
              >
                @if (action.icon) {
                  <svg
                    class="h-4 w-4 shrink-0"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    stroke-width="2"
                    aria-hidden="true"
                  >
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      [attr.d]="action.icon"
                    />
                  </svg>
                }
                <span>{{ action.label }}</span>
              </button>
            }
          </div>
        </div>

        <!-- Confirmation overlay -->
        @if (pendingAction(); as pending) {
          <div
            class="absolute inset-x-0 bottom-full mb-2 flex justify-center"
            role="alertdialog"
            aria-live="assertive"
          >
            <div
              class="flex w-full max-w-sm items-center gap-3 rounded-lg bg-slate-900 px-4 py-3
                     text-sm text-white shadow-2xl ring-1 ring-white/10"
            >
              <svg
                class="h-5 w-5 shrink-0 text-amber-300"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                stroke-width="2"
                aria-hidden="true"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  d="M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"
                />
              </svg>
              <span class="flex-1">
                Confirm bulk {{ pending.label.toLowerCase() }}?
              </span>
              <button
                type="button"
                class="rounded-md px-2 py-1 text-xs font-medium text-slate-300 transition-colors
                       hover:bg-white/10 hover:text-white focus:outline-none
                       focus-visible:ring-2 focus-visible:ring-white/40"
                (click)="cancelConfirm()"
              >
                Cancel
              </button>
              <button
                type="button"
                [class]="confirmButtonClassFor(pending)"
                (click)="confirmPending()"
              >
                Confirm
              </button>
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [
    `
      @keyframes slideUp {
        from {
          opacity: 0;
          transform: translate(-50%, 1rem);
        }
        to {
          opacity: 1;
          transform: translate(-50%, 0);
        }
      }
    `,
  ],
})
export class BulkActionsToolbarComponent {
  /** Number of items currently selected. Toolbar hides when this is 0. */
  @Input() selectedCount = 0;

  /** Total number of items on the current page/list (used by "Select all"). */
  @Input() totalCount = 0;

  /** Action buttons rendered on the right side of the toolbar. */
  @Input() actions: BulkAction[] = [];

  /** Emits the `key` of the action the user triggered (post-confirmation). */
  @Output() actionClicked = new EventEmitter<string>();

  /** Emits when the user clicks "Select all {totalCount}". */
  @Output() selectAll = new EventEmitter<void>();

  /** Emits when the user clicks "Clear". */
  @Output() clearSelection = new EventEmitter<void>();

  /** The action awaiting confirmation, or null when no overlay is shown. */
  readonly pendingAction = signal<BulkAction | null>(null);

  onActionClick(action: BulkAction): void {
    if (action.requireConfirm) {
      this.pendingAction.set(action);
      return;
    }
    this.actionClicked.emit(action.key);
  }

  confirmPending(): void {
    const pending = this.pendingAction();
    if (!pending) return;
    this.pendingAction.set(null);
    this.actionClicked.emit(pending.key);
  }

  cancelConfirm(): void {
    this.pendingAction.set(null);
  }

  onSelectAll(): void {
    this.selectAll.emit();
  }

  onClear(): void {
    this.pendingAction.set(null);
    this.clearSelection.emit();
  }

  /** Tailwind classes for an action button, keyed off its variant. */
  buttonClassFor(action: BulkAction): string {
    const base =
      'inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium ' +
      'transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-1 ' +
      'disabled:cursor-not-allowed disabled:opacity-50';
    switch (action.variant) {
      case 'primary':
        return `${base} bg-indigo-600 text-white hover:bg-indigo-700 focus-visible:ring-indigo-500`;
      case 'danger':
        return `${base} bg-red-600 text-white hover:bg-red-700 focus-visible:ring-red-500`;
      case 'success':
        return `${base} bg-emerald-600 text-white hover:bg-emerald-700 focus-visible:ring-emerald-500`;
      default:
        return (
          `${base} border border-slate-300 bg-white text-slate-700 ` +
          'hover:bg-slate-50 hover:text-slate-900 focus-visible:ring-slate-400'
        );
    }
  }

  /** Tailwind classes for the confirmation button, matching the action's variant. */
  confirmButtonClassFor(action: BulkAction): string {
    const base =
      'inline-flex items-center rounded-md px-3 py-1 text-xs font-semibold text-white ' +
      'transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-1 ' +
      'focus-visible:ring-offset-slate-900';
    switch (action.variant) {
      case 'danger':
        return `${base} bg-red-600 hover:bg-red-500 focus-visible:ring-red-400`;
      case 'success':
        return `${base} bg-emerald-600 hover:bg-emerald-500 focus-visible:ring-emerald-400`;
      case 'primary':
      default:
        return `${base} bg-indigo-600 hover:bg-indigo-500 focus-visible:ring-indigo-400`;
    }
  }
}

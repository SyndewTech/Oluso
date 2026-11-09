import { Component, Input, Output, EventEmitter, signal, HostListener, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface ContextAction {
  key: string;
  label: string;
  icon?: string;
  variant?: 'default' | 'danger' | 'success' | 'warning';
  disabled?: boolean;
  dividerBefore?: boolean;
}

/**
 * Reusable context menu — shows a floating action list on right-click or kebab button click.
 * Renders a vertical dropdown with styled action items. Closes on click-outside, Escape, or action click.
 *
 * Two trigger modes:
 * 1. Kebab button (always visible — `<ui-context-menu [actions]="getActions(row)" (actionClicked)="onAction($event, row)">`)
 * 2. Right-click (attach via directive or manual `(contextmenu)="menu.openAt($event)"`)
 *
 * Usage:
 *   <ui-context-menu
 *     [actions]="getActionsForRow(row)"
 *     (actionClicked)="handleAction($event.key, row)"
 *   />
 */
@Component({
  selector: 'ui-context-menu',
  standalone: true,
  imports: [CommonModule],
  template: `
    <!-- Kebab trigger button -->
    <div class="relative inline-flex">
      <button
        type="button"
        class="inline-flex items-center justify-center rounded p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        [class.bg-slate-100]="isOpen()"
        [class.text-slate-600]="isOpen()"
        (click)="toggle($event)"
        aria-label="Actions"
      >
        <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
          <path d="M10 6a2 2 0 110-4 2 2 0 010 4zM10 12a2 2 0 110-4 2 2 0 010 4zM10 18a2 2 0 110-4 2 2 0 010 4z" />
        </svg>
      </button>

      <!-- Dropdown menu -->
      @if (isOpen()) {
        <div
          class="absolute right-0 top-full z-50 mt-1 w-52 origin-top-right rounded-lg border border-slate-200 bg-white py-1 shadow-lg ring-1 ring-black/5 focus:outline-none"
          [class.bottom-full]="openUpward"
          [class.top-auto]="openUpward"
          [class.mb-1]="openUpward"
          [class.mt-0]="openUpward"
          role="menu"
        >
          @for (action of actions; track action.key) {
            @if (action.dividerBefore) {
              <div class="my-1 border-t border-slate-100"></div>
            }
            <button
              type="button"
              role="menuitem"
              class="flex w-full items-center gap-2.5 px-3 py-2 text-left text-sm transition-colors"
              [class]="actionClass(action)"
              [disabled]="action.disabled"
              (click)="onAction(action, $event)"
            >
              @if (action.icon) {
                <span class="flex h-4 w-4 items-center justify-center text-current opacity-60">
                  @switch (action.icon) {
                    @case ('eye') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" /><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" /></svg> }
                    @case ('pencil') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" /></svg> }
                    @case ('check') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" /></svg> }
                    @case ('x') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" /></svg> }
                    @case ('printer') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 17h2a2 2 0 002-2v-4a2 2 0 00-2-2H5a2 2 0 00-2 2v4a2 2 0 002 2h2m2 4h6a2 2 0 002-2v-4a2 2 0 00-2-2H9a2 2 0 00-2 2v4a2 2 0 002 2zm8-12V5a2 2 0 00-2-2H9a2 2 0 00-2 2v4h10z" /></svg> }
                    @case ('link') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" /></svg> }
                    @case ('clipboard') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" /></svg> }
                    @case ('arrow-right') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M14 5l7 7m0 0l-7 7m7-7H3" /></svg> }
                    @case ('clock') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" /></svg> }
                    @case ('user-plus') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M18 9v3m0 0v3m0-3h3m-3 0h-3m-2-5a4 4 0 11-8 0 4 4 0 018 0zM3 20a6 6 0 0112 0v1H3v-1z" /></svg> }
                    @case ('trash') { <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg> }
                    @default { <span class="text-xs">●</span> }
                  }
                </span>
              }
              <span>{{ action.label }}</span>
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class ContextMenuComponent {
  @Input({ required: true }) actions: ContextAction[] = [];
  @Input() openUpward = false;
  @Output() actionClicked = new EventEmitter<ContextAction>();

  private readonly el = inject(ElementRef);

  readonly isOpen = signal(false);

  toggle(event: Event): void {
    event.stopPropagation();
    this.isOpen.update((v) => !v);
  }

  /** Open at a specific position (for right-click usage via parent). */
  openAt(event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
  }

  onAction(action: ContextAction, event: Event): void {
    event.stopPropagation();
    if (action.disabled) return;
    this.close();
    this.actionClicked.emit(action);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (this.isOpen() && !this.el.nativeElement.contains(event.target)) {
      this.close();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen()) this.close();
  }

  actionClass(action: ContextAction): string {
    if (action.disabled) return 'text-slate-300 cursor-not-allowed';
    switch (action.variant) {
      case 'danger': return 'text-red-600 hover:bg-red-50';
      case 'success': return 'text-emerald-700 hover:bg-emerald-50';
      case 'warning': return 'text-amber-700 hover:bg-amber-50';
      default: return 'text-slate-700 hover:bg-slate-50';
    }
  }
}

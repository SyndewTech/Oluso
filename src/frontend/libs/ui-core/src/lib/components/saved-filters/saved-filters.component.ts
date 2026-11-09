import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SavedFilter, SavedFiltersService } from './saved-filters.service';

/**
 * Compact "Saved Views" dropdown that lives in a list page's filter bar.
 *
 * The component owns the open/close state of the menu and the inline
 * "save as new" prompt, but persistence is delegated to
 * {@link SavedFiltersService}. The parent is responsible for wiring the
 * `(filterApplied)` output back into its filter state.
 *
 * Usage:
 *   <ui-saved-filters
 *     pageKey="orders"
 *     [currentFilters]="filters()"
 *     (filterApplied)="applyFilters($event)" />
 */
@Component({
  selector: 'ui-saved-filters',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="relative inline-block text-left">
      <button
        type="button"
        class="inline-flex items-center gap-1.5 rounded-md border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-700 shadow-sm hover:bg-slate-50 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        [class.ring-2]="activeId() !== null"
        [class.ring-indigo-400]="activeId() !== null"
        (click)="toggle()"
      >
        <svg class="h-4 w-4 text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
          <path stroke-linecap="round" stroke-linejoin="round" d="M5 4h14M7 8h10M9 12h6M11 16h2" />
        </svg>
        <span>{{ buttonLabel() }}</span>
        <svg class="h-3.5 w-3.5 text-slate-400 transition-transform" [class.rotate-180]="isOpen()" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
          <path stroke-linecap="round" stroke-linejoin="round" d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      @if (isOpen()) {
        <div class="absolute left-0 z-40 mt-1 w-64 origin-top-left rounded-md bg-white shadow-lg ring-1 ring-black/5 focus:outline-none">
          <div class="max-h-64 overflow-y-auto py-1">
            @if (filters().length === 0) {
              <p class="px-3 py-2 text-xs text-slate-400">No saved views yet</p>
            } @else {
              @for (f of filters(); track f.id) {
                <div
                  class="group flex items-center justify-between px-3 py-1.5 text-sm hover:bg-slate-50"
                  [class.bg-indigo-50]="f.id === activeId()"
                >
                  <button
                    type="button"
                    class="flex-1 truncate text-left"
                    [class.font-semibold]="f.id === activeId()"
                    [class.text-indigo-700]="f.id === activeId()"
                    [class.text-slate-700]="f.id !== activeId()"
                    (click)="apply(f)"
                  >
                    {{ f.name }}
                  </button>
                  @if (confirmingId() === f.id) {
                    <div class="flex items-center gap-1">
                      <button class="rounded px-1.5 py-0.5 text-xs font-medium text-red-600 hover:bg-red-50" (click)="confirmDelete(f.id)">Delete</button>
                      <button class="rounded px-1.5 py-0.5 text-xs text-slate-500 hover:bg-slate-100" (click)="cancelDelete()">Cancel</button>
                    </div>
                  } @else {
                    <button
                      type="button"
                      class="ml-2 rounded p-1 text-slate-300 opacity-0 hover:bg-red-50 hover:text-red-500 group-hover:opacity-100"
                      aria-label="Delete saved view"
                      (click)="askDelete(f.id, $event)"
                    >
                      <svg class="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  }
                </div>
              }
            }
          </div>

          <div class="border-t border-slate-100 px-2 py-2">
            @if (!savePromptOpen()) {
              <button
                type="button"
                class="w-full rounded px-2 py-1.5 text-left text-xs font-medium text-indigo-600 hover:bg-indigo-50 disabled:cursor-not-allowed disabled:text-slate-300 disabled:hover:bg-transparent"
                [disabled]="!hasCurrentFilters()"
                (click)="openSavePrompt()"
              >
                — Save Current as New View —
              </button>
            } @else {
              <form class="flex items-center gap-1" (submit)="$event.preventDefault(); save()">
                <input
                  #nameInput
                  type="text"
                  [(ngModel)]="draftName"
                  name="savedFilterName"
                  placeholder="View name"
                  maxlength="40"
                  class="flex-1 rounded border border-slate-200 px-2 py-1 text-xs focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-400"
                />
                <button type="submit" class="rounded bg-indigo-600 px-2 py-1 text-xs font-medium text-white hover:bg-indigo-700 disabled:bg-slate-300" [disabled]="!draftName.trim()">Save</button>
                <button type="button" class="rounded px-1.5 py-1 text-xs text-slate-500 hover:bg-slate-100" (click)="cancelSave()">Cancel</button>
              </form>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class SavedFiltersComponent {
  private readonly svc = inject(SavedFiltersService);
  private readonly host = inject(ElementRef<HTMLElement>);

  @Input({ required: true }) pageKey!: string;
  @Input() currentFilters: Record<string, unknown> = {};
  @Output() filterApplied = new EventEmitter<Record<string, unknown>>();

  protected readonly isOpen = signal(false);
  protected readonly savePromptOpen = signal(false);
  protected readonly confirmingId = signal<string | null>(null);
  protected readonly filters = signal<SavedFilter[]>([]);
  protected draftName = '';

  protected readonly activeId = computed(() => {
    const current = this.serialize(this.currentFilters);
    const match = this.filters().find((f) => this.serialize(f.filters) === current);
    return match?.id ?? null;
  });

  protected readonly buttonLabel = computed(() => {
    const id = this.activeId();
    if (!id) return 'Saved Views';
    return this.filters().find((f) => f.id === id)?.name ?? 'Saved Views';
  });

  protected hasCurrentFilters(): boolean {
    return Object.values(this.currentFilters ?? {}).some(
      (v) => v !== null && v !== undefined && v !== '' && !(Array.isArray(v) && v.length === 0),
    );
  }

  protected toggle(): void {
    const next = !this.isOpen();
    if (next) this.refresh();
    this.isOpen.set(next);
    if (!next) this.resetTransient();
  }

  protected apply(f: SavedFilter): void {
    this.filterApplied.emit({ ...f.filters });
    this.isOpen.set(false);
    this.resetTransient();
  }

  protected askDelete(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.confirmingId.set(id);
  }

  protected confirmDelete(id: string): void {
    this.svc.deleteFilter(this.pageKey, id);
    this.confirmingId.set(null);
    this.refresh();
  }

  protected cancelDelete(): void {
    this.confirmingId.set(null);
  }

  protected openSavePrompt(): void {
    this.draftName = '';
    this.savePromptOpen.set(true);
  }

  protected cancelSave(): void {
    this.savePromptOpen.set(false);
    this.draftName = '';
  }

  protected save(): void {
    const name = this.draftName.trim();
    if (!name) return;
    this.svc.saveFilter(this.pageKey, name, this.currentFilters);
    this.savePromptOpen.set(false);
    this.draftName = '';
    this.refresh();
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (!this.isOpen()) return;
    const target = event.target as Node | null;
    if (target && !this.host.nativeElement.contains(target)) {
      this.isOpen.set(false);
      this.resetTransient();
    }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.isOpen()) {
      this.isOpen.set(false);
      this.resetTransient();
    }
  }

  private refresh(): void {
    this.filters.set(this.svc.getFilters(this.pageKey));
  }

  private resetTransient(): void {
    this.savePromptOpen.set(false);
    this.confirmingId.set(null);
    this.draftName = '';
  }

  private serialize(obj: Record<string, unknown>): string {
    const keys = Object.keys(obj ?? {}).sort();
    const normalized: Record<string, unknown> = {};
    for (const k of keys) {
      const v = obj[k];
      if (v === null || v === undefined || v === '') continue;
      if (Array.isArray(v) && v.length === 0) continue;
      normalized[k] = v;
    }
    return JSON.stringify(normalized);
  }
}

import {
  Component, ElementRef, HostListener, ViewChild, inject, signal, computed, effect, OnDestroy, Input,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, debounceTime, distinctUntilChanged, of, catchError, takeUntil, map, forkJoin } from 'rxjs';
import { CommandRegistryService, AnyCommand, DynamicCommandResult, DynamicCommandGroup, APP_SWITCH_PREFIX } from './command-registry.service';
import { KeyboardShortcutsService } from '../keyboard-shortcuts/keyboard-shortcuts.service';

/**
 * Cmd/Ctrl+K Command Palette — module-agnostic. Reads commands from the
 * CommandRegistryService, which narrows them to the current app's and the
 * global ones; `>app` (or "Switch app…") lists the apps to move between.
 *
 * Opening: bind the `open` input to a signal you toggle from a global keydown
 * handler (or use KeyboardShortcutsService which already wires Cmd+K).
 *
 * Example (in admin app shell):
 * ```html
 * <ui-command-palette [open]="paletteOpen()" (closed)="paletteOpen.set(false)" />
 * ```
 */
@Component({
  selector: 'ui-command-palette',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (isOpen()) {
      <div
        class="fixed inset-0 z-[60] flex items-start justify-center bg-slate-900/40 backdrop-blur-sm pt-[15vh] px-4"
        (click)="close()"
      >
        <div
          class="w-full max-w-xl overflow-hidden rounded-xl bg-white shadow-2xl ring-1 ring-black/5"
          (click)="$event.stopPropagation()"
        >
          <!-- Query input -->
          <div class="flex items-center gap-2 border-b border-slate-100 px-4">
            <svg class="h-4 w-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              #queryInput
              type="text"
              [value]="query()"
              (input)="onQueryChange($event)"
              (keydown)="onInputKeydown($event)"
              [placeholder]="placeholder"
              class="flex-1 border-0 bg-transparent py-3 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-0"
            />
            <span class="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] font-medium text-slate-500">ESC</span>
          </div>

          <!-- Results -->
          <div class="max-h-[60vh] overflow-y-auto py-1">
            @if (groupedItems().length === 0 && !loading()) {
              <div class="px-4 py-10 text-center text-sm text-slate-500">
                @if (query()) { No matches for "{{ query() }}" }
                @else { Start typing to search… }
              </div>
            }

            @for (group of groupedItems(); track group.group) {
              <div class="px-2 py-1">
                <div class="px-2 py-1 text-[10px] font-semibold uppercase tracking-wider text-slate-400">{{ group.group }}</div>
                @for (item of group.items; track item.id) {
                  <button
                    type="button"
                    class="flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors"
                    [class.bg-indigo-50]="activeIndex() === item._globalIndex"
                    [class.text-indigo-900]="activeIndex() === item._globalIndex"
                    [class.hover:bg-slate-50]="activeIndex() !== item._globalIndex"
                    (click)="execute(item)"
                    (mouseenter)="activeIndex.set(item._globalIndex)"
                  >
                    <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-md bg-slate-100 text-slate-500">
                      @if (item.icon) {
                        <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" [attr.d]="iconPath(item.icon)" />
                        </svg>
                      }
                    </span>
                    <div class="min-w-0 flex-1">
                      <div class="truncate text-slate-900">{{ item.label }}</div>
                      @if (item.description) {
                        <div class="truncate text-xs text-slate-500">{{ item.description }}</div>
                      }
                    </div>
                    @if (item.shortcut) {
                      <span class="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] font-mono text-slate-500">{{ item.shortcut }}</span>
                    }
                  </button>
                }
              </div>
            }

            @if (loading()) {
              <div class="flex items-center justify-center px-4 py-6 text-xs text-slate-400">
                <div class="mr-2 h-3 w-3 animate-spin rounded-full border border-slate-300 border-t-slate-500"></div>
                Searching…
              </div>
            }
          </div>

          <!-- Footer -->
          <div class="flex items-center justify-between border-t border-slate-100 bg-slate-50 px-3 py-2 text-[11px] text-slate-500">
            <div class="flex gap-3">
              <span>↑↓ navigate</span>
              <span>↵ select</span>
              <span>esc close</span>
              <span>{{ appSwitchPrefix }} switch app</span>
            </div>
            <span class="text-slate-400">{{ totalCount() }} results</span>
          </div>
        </div>
      </div>
    }
  `,
})
export class CommandPaletteComponent implements OnDestroy {
  /** Optional override — if provided, the palette uses this input instead of
   *  the signal from KeyboardShortcutsService. Usually you don't set this:
   *  the service is the source of truth and ⌘K toggles it. */
  @Input() open: boolean | null = null;
  @Input() placeholder = 'Search or type a command…';

  @ViewChild('queryInput') private queryInput?: ElementRef<HTMLInputElement>;

  private readonly registry = inject(CommandRegistryService);
  private readonly shortcuts = inject(KeyboardShortcutsService);
  private readonly destroy$ = new Subject<void>();
  private readonly query$ = new Subject<string>();

  /** True when the palette should render. Reads from the service unless @Input open is explicitly set. */
  readonly isOpen = computed(() => (this.open !== null ? this.open : this.shortcuts.commandPaletteOpen()));

  readonly appSwitchPrefix = APP_SWITCH_PREFIX;
  readonly query = signal('');
  readonly activeIndex = signal(0);
  readonly loading = signal(false);
  /** Results from dynamic command groups, keyed by group id. */
  readonly dynamicResults = signal<Map<string, { group: DynamicCommandGroup; items: DynamicCommandResult[] }>>(new Map());

  constructor() {
    // Auto-focus when opened; reset state when closed.
    effect(() => {
      if (this.isOpen()) {
        queueMicrotask(() => this.queryInput?.nativeElement.focus());
      } else {
        this.query.set('');
        this.activeIndex.set(0);
        this.dynamicResults.set(new Map());
      }
    });

    // Commands such as "Switch app…" change what the palette lists by asking for a query.
    effect(() => {
      const requested = this.registry.requestedQuery();
      if (requested === null) return;
      this.query.set(requested);
      this.activeIndex.set(0);
      this.dynamicResults.set(new Map());
      this.query$.next(requested);
      this.registry.clearRequestedQuery();
      queueMicrotask(() => this.queryInput?.nativeElement.focus());
    });

    // Debounce dynamic searches
    this.query$
      .pipe(debounceTime(200), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe((q) => this.runDynamicSearches(q));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onQueryChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.query.set(value);
    this.activeIndex.set(0);
    this.query$.next(value);
  }

  close(): void {
    this.shortcuts.commandPaletteOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen()) this.close();
  }

  /** Flattened list with dynamic results interleaved, grouped by `group`. */
  readonly groupedItems = computed<Array<{ group: string; items: Array<AnyCommand & { _globalIndex: number; description?: string }> }>>(() => {
    const q = this.query();
    const staticMatches = this.registry.filter(q);
    const dynamicMatches: Array<AnyCommand & { _globalIndex: number; description?: string }> = [];

    // Flatten dynamic results into command-like items (each result wraps onSelect in the click handler)
    this.dynamicResults().forEach(({ group, items }) => {
      items.forEach((r) => {
        dynamicMatches.push({
          kind: 'action',
          id: `dyn-${group.id}-${r.id}`,
          group: group.group,
          label: r.label,
          description: r.description,
          icon: r.icon ?? group.icon,
          execute: r.onSelect,
          _globalIndex: 0,
        } as AnyCommand & { _globalIndex: number; description?: string });
      });
    });

    // Combine, assign global indices for keyboard navigation, then group by `group`
    const all = [...staticMatches, ...dynamicMatches].map((c, i) => ({ ...c, _globalIndex: i } as AnyCommand & { _globalIndex: number; description?: string }));
    const byGroup = new Map<string, Array<AnyCommand & { _globalIndex: number; description?: string }>>();
    for (const item of all) {
      if (!byGroup.has(item.group)) byGroup.set(item.group, []);
      byGroup.get(item.group)!.push(item);
    }
    return Array.from(byGroup.entries()).map(([group, items]) => ({ group, items }));
  });

  readonly totalCount = computed(() => this.groupedItems().reduce((acc, g) => acc + g.items.length, 0));

  private runDynamicSearches(query: string): void {
    const dynGroups = this.registry.getDynamicGroups();
    if (dynGroups.length === 0 || !query.trim() || CommandRegistryService.isAppSwitchQuery(query)) {
      this.dynamicResults.set(new Map());
      return;
    }
    const eligible = dynGroups.filter((g) => query.length >= (g.minQueryLength ?? 1));
    if (eligible.length === 0) {
      this.dynamicResults.set(new Map());
      return;
    }
    this.loading.set(true);
    forkJoin(
      eligible.map((g) =>
        g.search(query).pipe(
          map((items) => [g, items] as [DynamicCommandGroup, DynamicCommandResult[]]),
          catchError(() => of([g, [] as DynamicCommandResult[]] as [DynamicCommandGroup, DynamicCommandResult[]])),
        ),
      ),
    )
      .pipe(takeUntil(this.destroy$))
      .subscribe((pairs) => {
        const map = new Map<string, { group: DynamicCommandGroup; items: DynamicCommandResult[] }>();
        for (const [group, items] of pairs) {
          if (items.length) map.set(group.id, { group, items });
        }
        this.dynamicResults.set(map);
        this.loading.set(false);
      });
  }

  execute(item: AnyCommand & { _globalIndex: number }): void {
    this.registry.execute(item);
    if (item.kind === 'action' && item.keepOpen) return;
    this.close();
  }

  onInputKeydown(event: KeyboardEvent): void {
    const total = this.totalCount();
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.activeIndex.set(Math.min(this.activeIndex() + 1, total - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.activeIndex.set(Math.max(this.activeIndex() - 1, 0));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const flat = this.groupedItems().flatMap((g) => g.items);
      const chosen = flat[this.activeIndex()];
      if (chosen) this.execute(chosen);
    }
  }

  /** Minimal icon-name → SVG-path map. Unknown names render a dot. */
  iconPath(name: string): string {
    return ICON_PATHS[name] ?? 'M10 12a2 2 0 100-4 2 2 0 000 4z';
  }
}

const ICON_PATHS: Record<string, string> = {
  users: 'M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-5a4 4 0 11-8 0 4 4 0 018 0zm6 3a4 4 0 11-8 0 4 4 0 018 0z',
  user: 'M5.121 17.804A13.937 13.937 0 0112 16c2.5 0 4.847.655 6.879 1.804M15 10a3 3 0 11-6 0 3 3 0 016 0zm6 2a9 9 0 11-18 0 9 9 0 0118 0z',
  calendar: 'M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z',
  clipboard: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2',
  pill: 'M19.428 15.428l-3.428-3.428m-3-3l-2 2m0 0l-2 2m2-2l2 2m-2-2l-2-2m12.728 7.728a8 8 0 11-11.314-11.314 8 8 0 0111.314 11.314z',
  flask: 'M19.428 15.428l-3.428-3.428a4 4 0 00-5.656 5.656l3.428 3.428m-9-7l3.428-3.428a4 4 0 015.656 0l3.428 3.428',
  forward: 'M14 5l7 7m0 0l-7 7m7-7H3',
  receipt: 'M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16l3.5-2 3.5 2 3.5-2 3.5 2z',
  stethoscope: 'M9 12l2 2 4-4',
  plus: 'M12 6v6m0 0v6m0-6h6m-6 0H6',
  'squares-2x2': 'M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z',
};

import {
  Component, ElementRef, HostListener, OnDestroy, OnInit, computed, inject, signal, viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, switchMap, takeUntil, of, catchError, tap } from 'rxjs';
import { IconComponent } from '@oluso/ui-core';
import {
  AdminSearchService, AdminSearchGroup, AdminSearchProvider, AdminSearchResultItem, AdminSearchResponse,
  AdminSearchBadgeTone, PLATFORM_MODULE, MIN_SEARCH_LENGTH,
} from '../../services/admin-search.service';
import { TenantService, RegisteredModuleInfo } from '../../services/tenant.service';
import { MODULE_LABEL_FALLBACKS, badgeTone, groupProvidersByModule, moduleTone, searchPageParams, workspaceHref } from './admin-search-ui';

interface FlatResult {
  group: AdminSearchGroup;
  item: AdminSearchResultItem;
}

/**
 * Header search across every record kind the caller may see. The server has
 * already applied the tenant-feature and permission gates, so this component
 * only renders what comes back: groups per record kind, a chip row to narrow
 * to one kind, and that kind's filters.
 */
@Component({
  selector: 'admin-global-search',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="relative group">
      <ui-icon name="search" class="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 group-focus-within:text-primary-500 transition-colors pointer-events-none" />
      <input
        #searchBox
        type="text"
        [placeholder]="placeholder()"
        [ngModel]="query()"
        (ngModelChange)="onQueryChange($event)"
        (focus)="openPanel()"
        (keydown)="onKeydown($event)"
        autocomplete="off"
        role="combobox"
        aria-label="Search"
        aria-controls="admin-global-search-results"
        aria-autocomplete="list"
        [attr.aria-expanded]="open()"
        class="w-full h-8 pl-8 pr-8 text-sm bg-gray-100/80 border border-transparent rounded-md hover:bg-gray-100 focus:bg-white focus:border-gray-300 focus:ring-0 placeholder:text-gray-400 transition-all"
      />
      @if (query()) {
        <button type="button" (click)="clear()" aria-label="Clear search"
          class="absolute right-2 top-1/2 -translate-y-1/2 p-0.5 text-gray-400 hover:text-gray-600 rounded">
          <ui-icon name="close" class="h-3.5 w-3.5" strokeWidth="2.5" />
        </button>
      }

      @if (open() && providers().length > 0) {
        <div class="absolute left-0 top-full mt-1.5 w-[44rem] max-w-[calc(100vw-2rem)] bg-white rounded-lg border border-gray-200 shadow-xl z-30 overflow-hidden">
          <!-- Record kinds, grouped by module -->
          <div class="px-3 py-2 border-b border-gray-100 space-y-1.5 max-h-40 overflow-y-auto">
            <div class="flex items-center gap-1.5 flex-wrap">
              <span class="w-20 shrink-0 text-[11px] font-semibold uppercase tracking-wider text-gray-400">Search in</span>
              <button type="button" (click)="selectProvider(null)"
                class="px-2 py-0.5 rounded-full text-xs font-medium border transition-colors"
                [class]="selectedProviderId() === null ? 'bg-gray-900 text-white border-gray-900' : 'bg-white text-gray-600 border-gray-200 hover:bg-gray-50'">
                Everything
              </button>
            </div>
            @for (moduleGroup of providerGroups(); track moduleGroup.module) {
              <div class="flex items-start gap-1.5 flex-wrap">
                <span class="w-20 shrink-0 pt-0.5 text-[11px] font-medium text-gray-500 truncate" [title]="moduleGroup.label">{{ moduleGroup.label }}</span>
                @for (provider of moduleGroup.providers; track provider.providerId) {
                  <button type="button" (click)="selectProvider(provider.providerId)"
                    class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium border transition-colors"
                    [class]="selectedProviderId() === provider.providerId ? 'bg-gray-900 text-white border-gray-900' : 'bg-white text-gray-600 border-gray-200 hover:bg-gray-50'">
                    <ui-icon [name]="provider.icon" class="h-3 w-3" />
                    {{ provider.displayName }}
                  </button>
                }
              </div>
            }
          </div>

          <!-- Filters for the selected record kind -->
          @if (selectedProvider(); as provider) {
            @if (provider.filters.length > 0) {
              <div class="flex flex-wrap items-end gap-3 px-3 py-2 border-b border-gray-100 bg-gray-50/60">
                @for (filter of provider.filters; track filter.key) {
                  <div class="flex flex-col gap-0.5 text-[11px] font-medium text-gray-500">
                    <span>{{ filter.label }}</span>
                    @switch (filter.type) {
                      @case ('select') {
                        <select [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                          class="h-7 text-xs border-gray-300 rounded-md py-0 pl-2 pr-7">
                          <option value="">Any</option>
                          @for (option of filter.options ?? []; track option.value) {
                            <option [value]="option.value">{{ option.label }}</option>
                          }
                        </select>
                      }
                      @case ('multiselect') {
                        <select [attr.aria-label]="filter.label" multiple [ngModel]="filterValues(filter.key)" (ngModelChange)="setFilter(filter.key, $event.join(','))"
                          class="text-xs border-gray-300 rounded-md py-0.5 min-w-[8rem]" size="3">
                          @for (option of filter.options ?? []; track option.value) {
                            <option [value]="option.value">{{ option.label }}</option>
                          }
                        </select>
                      }
                      @case ('boolean') {
                        <select [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                          class="h-7 text-xs border-gray-300 rounded-md py-0 pl-2 pr-7">
                          <option value="">Any</option>
                          <option value="true">Yes</option>
                          <option value="false">No</option>
                        </select>
                      }
                      @case ('date') {
                        <input [attr.aria-label]="filter.label" type="date" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                          class="h-7 text-xs border-gray-300 rounded-md py-0 px-2" />
                      }
                      @case ('daterange') {
                        <span class="flex items-center gap-1">
                          <input [attr.aria-label]="filter.label" type="date" [ngModel]="rangePart(filter.key, 0)" (ngModelChange)="setRange(filter.key, 0, $event)"
                            class="h-7 text-xs border-gray-300 rounded-md py-0 px-2" />
                          <span class="text-gray-400">to</span>
                          <input [attr.aria-label]="filter.label" type="date" [ngModel]="rangePart(filter.key, 1)" (ngModelChange)="setRange(filter.key, 1, $event)"
                            class="h-7 text-xs border-gray-300 rounded-md py-0 px-2" />
                        </span>
                      }
                      @case ('number') {
                        <input [attr.aria-label]="filter.label" type="number" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                          [placeholder]="filter.placeholder ?? ''" class="h-7 w-28 text-xs border-gray-300 rounded-md py-0 px-2" />
                      }
                      @case ('numberrange') {
                        <span class="flex items-center gap-1">
                          <input [attr.aria-label]="filter.label" type="number" placeholder="Min" [ngModel]="rangePart(filter.key, 0)" (ngModelChange)="setRange(filter.key, 0, $event)"
                            class="h-7 w-24 text-xs border-gray-300 rounded-md py-0 px-2" />
                          <span class="text-gray-400">to</span>
                          <input [attr.aria-label]="filter.label" type="number" placeholder="Max" [ngModel]="rangePart(filter.key, 1)" (ngModelChange)="setRange(filter.key, 1, $event)"
                            class="h-7 w-24 text-xs border-gray-300 rounded-md py-0 px-2" />
                        </span>
                      }
                      @default {
                        <input [attr.aria-label]="filter.label" type="text" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                          [placeholder]="filter.placeholder ?? ''" class="h-7 w-36 text-xs border-gray-300 rounded-md py-0 px-2" />
                      }
                    }
                  </div>
                }
                @if (hasFilters()) {
                  <button type="button" (click)="clearFilters()" class="h-7 text-xs text-gray-500 hover:text-gray-700 underline-offset-2 hover:underline">Reset</button>
                }
              </div>
            }
          }

          <!-- Results -->
          <div id="admin-global-search-results" class="max-h-[28rem] overflow-y-auto" role="listbox">
            @if (query().trim().length < minLength) {
              <p class="px-4 py-6 text-sm text-gray-500 text-center">Type at least {{ minLength }} characters to search.</p>
            } @else if (loading() && !results()) {
              <p class="px-4 py-6 text-sm text-gray-500 text-center">Searching…</p>
            } @else if (results() && results()!.groups.length === 0) {
              <p class="px-4 py-6 text-sm text-gray-500 text-center">No matches for “{{ results()!.query }}”.</p>
            } @else if (results(); as response) {
              @for (group of response.groups; track group.providerId) {
                <div class="border-b border-gray-100 last:border-b-0">
                  <div class="flex items-center justify-between px-3 pt-2 pb-1">
                    <div class="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wider text-gray-500">
                      <span class="inline-flex h-4 w-4 items-center justify-center rounded" [class]="moduleTone(group.module)">
                        <ui-icon [name]="group.icon" class="h-3 w-3" />
                      </span>
                      <span>{{ group.displayName }}</span>
                      @if (group.module !== platformModule) {
                        <span class="font-normal normal-case tracking-normal text-gray-400">· {{ moduleLabel(group.module) }}</span>
                      }
                    </div>
                    @if (group.seeAllRoute && group.totalCount > group.items.length) {
                      <button type="button" (click)="navigate(group.seeAllRoute)" class="text-xs text-primary-600 hover:text-primary-700">
                        See all {{ group.totalCount }}
                      </button>
                    }
                  </div>
                  @for (item of group.items; track item.id) {
                    <button type="button" role="option"
                      (click)="openItem(item)" (mouseenter)="activeIndex.set(indexOf(group, item))"
                      [attr.aria-selected]="isActive(group, item)"
                      class="flex w-full items-start gap-3 px-3 py-2 text-left transition-colors"
                      [class]="isActive(group, item) ? 'bg-primary-50' : 'hover:bg-gray-50'">
                      @if (item.imageUrl) {
                        <img [src]="item.imageUrl" alt="" class="h-8 w-8 rounded-full object-cover shrink-0" />
                      } @else {
                        <span class="inline-flex h-8 w-8 items-center justify-center rounded-full shrink-0" [class]="moduleTone(group.module)">
                          <ui-icon [name]="item.icon ?? group.icon" class="h-4 w-4" />
                        </span>
                      }
                      <span class="min-w-0 flex-1">
                        <span class="block truncate text-sm font-medium text-gray-900">{{ item.title }}</span>
                        @if (item.subtitle) {
                          <span class="block truncate text-xs text-gray-500">{{ item.subtitle }}</span>
                        }
                      </span>
                      @if (item.badges.length > 0) {
                        <span class="flex flex-wrap justify-end gap-1 shrink-0 max-w-[12rem]">
                          @for (badge of item.badges; track badge.label + badge.value) {
                            <span class="inline-flex px-1.5 py-0.5 rounded text-[11px] font-medium" [class]="badgeTone(badge.tone)" [title]="badge.label">{{ badge.value }}</span>
                          }
                        </span>
                      }
                    </button>
                  }
                </div>
              }
            }
          </div>

          <div class="flex items-center justify-between px-3 py-1.5 border-t border-gray-100 bg-gray-50 text-[11px] text-gray-400">
            <span>↑↓ to move · Enter for all results · Esc to close</span>
            @if (results(); as response) {
              @if (response.totalCount > 0) {
                <button type="button" (click)="openResultsPage()" class="text-primary-600 hover:text-primary-700">
                  All {{ response.totalCount }} {{ response.totalCount === 1 ? 'match' : 'matches' }} →
                </button>
              }
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class GlobalSearchComponent implements OnInit, OnDestroy {
  private readonly searchService = inject(AdminSearchService);
  private readonly tenantService = inject(TenantService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<void>();
  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  readonly minLength = MIN_SEARCH_LENGTH;
  readonly platformModule = PLATFORM_MODULE;

  readonly query = signal('');
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly providers = signal<AdminSearchProvider[]>([]);
  readonly modules = signal<Record<string, RegisteredModuleInfo>>({});
  readonly selectedProviderId = signal<string | null>(null);
  readonly filters = signal<Record<string, string>>({});
  readonly results = signal<AdminSearchResponse | null>(null);
  readonly activeIndex = signal(-1);

  readonly selectedProvider = computed(() =>
    this.providers().find(p => p.providerId === this.selectedProviderId()) ?? null);
  readonly hasFilters = computed(() => Object.values(this.filters()).some(v => v !== ''));
  readonly providerGroups = computed(() => groupProvidersByModule(this.providers(), m => this.moduleLabel(m)));
  readonly placeholder = computed(() => {
    const provider = this.selectedProvider();
    if (!provider) return 'Search';
    const scope = provider.module === PLATFORM_MODULE ? '' : `${this.moduleLabel(provider.module)} `;
    return `Search ${scope}${provider.displayName.toLowerCase()}`;
  });
  readonly flatResults = computed<FlatResult[]>(() =>
    (this.results()?.groups ?? []).flatMap(group => group.items.map(item => ({ group, item }))));

  ngOnInit(): void {
    this.searchService.getProviders()
      .pipe(takeUntil(this.destroy$))
      .subscribe(providers => this.providers.set(providers));

    this.tenantService.getRegisteredModules()
      .pipe(takeUntil(this.destroy$))
      .subscribe(response => {
        const byKey: Record<string, RegisteredModuleInfo> = {};
        for (const module of response.modules) byKey[module.featureKey] = module;
        this.modules.set(byKey);
      });

    this.search$
      .pipe(
        debounceTime(250),
        tap(() => this.loading.set(true)),
        switchMap(() => {
          const query = this.query().trim();
          if (query.length < MIN_SEARCH_LENGTH) return of(null);
          return this.searchService.search(query, {
            providerId: this.selectedProviderId() ?? undefined,
            filters: this.filters(),
            limit: this.selectedProviderId() ? 20 : 5,
          }).pipe(catchError(() => of({ query, totalCount: 0, groups: [] } as AdminSearchResponse)));
        }),
        distinctUntilChanged(),
        takeUntil(this.destroy$),
      )
      .subscribe(response => {
        this.results.set(response);
        this.activeIndex.set(-1);
        this.loading.set(false);
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.open.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  onGlobalKeydown(event: KeyboardEvent): void {
    if (event.key !== '/' || event.metaKey || event.ctrlKey || event.altKey) return;
    const target = event.target as HTMLElement | null;
    if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable)) return;
    event.preventDefault();
    this.searchBox()?.nativeElement.focus();
  }

  openPanel(): void {
    this.open.set(true);
  }

  onQueryChange(value: string): void {
    this.query.set(value);
    this.open.set(true);
    if (value.trim().length < MIN_SEARCH_LENGTH) {
      this.results.set(null);
      this.loading.set(false);
      return;
    }
    this.search$.next();
  }

  selectProvider(providerId: string | null): void {
    this.selectedProviderId.set(providerId);
    this.filters.set({});
    this.searchBox()?.nativeElement.focus();
    this.rerun();
  }

  filterValue(key: string): string {
    return this.filters()[key] ?? '';
  }

  filterValues(key: string): string[] {
    const value = this.filterValue(key);
    return value ? value.split(',') : [];
  }

  setFilter(key: string, value: string | null): void {
    this.filters.update(current => ({ ...current, [key]: value ?? '' }));
    this.rerun();
  }

  rangePart(key: string, index: 0 | 1): string {
    return (this.filterValue(key).split('..')[index] ?? '');
  }

  setRange(key: string, index: 0 | 1, value: string | number | null): void {
    const parts = this.filterValue(key).split('..');
    parts[index] = value == null ? '' : String(value);
    const joined = `${parts[0] ?? ''}..${parts[1] ?? ''}`;
    this.setFilter(key, joined === '..' ? '' : joined);
  }

  clearFilters(): void {
    this.filters.set({});
    this.rerun();
  }

  clear(): void {
    this.query.set('');
    this.results.set(null);
    this.searchBox()?.nativeElement.focus();
  }

  onKeydown(event: KeyboardEvent): void {
    const items = this.flatResults();
    switch (event.key) {
      case 'ArrowDown':
        if (items.length) { event.preventDefault(); this.activeIndex.set((this.activeIndex() + 1) % items.length); }
        break;
      case 'ArrowUp':
        if (items.length) { event.preventDefault(); this.activeIndex.set(this.activeIndex() <= 0 ? items.length - 1 : this.activeIndex() - 1); }
        break;
      case 'Enter': {
        event.preventDefault();
        const active = this.activeIndex() >= 0 ? items[this.activeIndex()] : undefined;
        if (active) this.openItem(active.item); else this.openResultsPage();
        break;
      }
      case 'Escape':
        this.open.set(false);
        this.searchBox()?.nativeElement.blur();
        break;
    }
  }

  indexOf(group: AdminSearchGroup, item: AdminSearchResultItem): number {
    return this.flatResults().findIndex(r => r.group.providerId === group.providerId && r.item.id === item.id);
  }

  isActive(group: AdminSearchGroup, item: AdminSearchResultItem): boolean {
    return this.indexOf(group, item) === this.activeIndex();
  }

  moduleLabel(module: string): string {
    return this.modules()[module]?.displayName ?? MODULE_LABEL_FALLBACKS[module] ?? module;
  }

  moduleTone(module: string): string {
    return moduleTone(this.modules()[module]?.color);
  }

  badgeTone(tone: AdminSearchBadgeTone): string {
    return badgeTone(tone);
  }

  /** Enter without a highlighted hit opens the results page for the current query, kind and filters. */
  openResultsPage(): void {
    const query = this.query().trim();
    if (query.length < MIN_SEARCH_LENGTH) return;
    this.open.set(false);
    this.router.navigate(['/search'], { queryParams: searchPageParams(query, this.selectedProviderId(), this.filters()) });
  }

  navigate(route: string): void {
    this.open.set(false);
    this.router.navigateByUrl(route);
  }

  /** Records without an admin page open on the workspace host, when the host is configured. */
  openItem(item: AdminSearchResultItem): void {
    if (item.route) {
      this.navigate(item.route);
      return;
    }
    const target = workspaceHref(item.workspaceRoute);
    if (target) {
      this.open.set(false);
      window.location.assign(target);
    }
  }

  private rerun(): void {
    if (this.query().trim().length >= MIN_SEARCH_LENGTH) this.search$.next();
  }
}

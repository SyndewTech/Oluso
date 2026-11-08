import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, catchError, of, switchMap, takeUntil } from 'rxjs';
import { IconComponent } from '@oluso/ui-core';
import {
  AdminSearchService, AdminSearchProvider, AdminSearchResultItem, AdminSearchResponse,
  AdminSearchBadgeTone, PLATFORM_MODULE, MIN_SEARCH_LENGTH,
} from '../../services/admin-search.service';
import { TenantService, RegisteredModuleInfo } from '../../services/tenant.service';
import { MODULE_LABEL_FALLBACKS, badgeTone, groupProvidersByModule, moduleTone, searchPageParams, workspaceHref } from '../../components/global-search/admin-search-ui';

/**
 * Full results for a query: every record kind the caller may see, or one kind
 * with its filters when narrowed. State lives in the URL so a result set can be
 * refreshed or shared.
 */
@Component({
  selector: 'admin-search-results-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">Search</h1>
          @if (response(); as result) {
            <p class="mt-1 text-sm text-gray-500">
              {{ result.totalCount }} {{ result.totalCount === 1 ? 'match' : 'matches' }} for “{{ result.query }}”
            </p>
          }
        </div>
        <form class="flex items-center gap-2 w-full max-w-lg" (submit)="submit($event)">
          <div class="relative flex-1">
            <ui-icon name="search" class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 pointer-events-none" />
            <input type="search" [ngModel]="draft" (ngModelChange)="onDraftChange($event)" name="q" aria-label="Search" [placeholder]="placeholder()"
              class="w-full pl-9 text-sm border-gray-300 rounded-md shadow-sm focus:border-primary-500 focus:ring-primary-500" />
          </div>
          <button type="submit" class="px-3 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700">Search</button>
        </form>
      </div>

      <!-- Record kinds, grouped by module -->
      <div class="rounded-lg border border-gray-200 bg-white p-3 space-y-2">
        <div class="flex items-center gap-1.5 flex-wrap">
          <span class="w-24 shrink-0 text-[11px] font-semibold uppercase tracking-wider text-gray-400">Search in</span>
          <button type="button" (click)="selectProvider(null)"
            class="px-2.5 py-1 rounded-full text-xs font-medium border transition-colors"
            [class]="providerId() === null ? 'bg-gray-900 text-white border-gray-900' : 'bg-white text-gray-600 border-gray-200 hover:bg-gray-50'">
            Everything
          </button>
        </div>
        @for (moduleGroup of providerGroups(); track moduleGroup.module) {
          <div class="flex items-start gap-1.5 flex-wrap">
            <span class="w-24 shrink-0 pt-1 text-xs font-medium text-gray-500 truncate" [title]="moduleGroup.label">{{ moduleGroup.label }}</span>
            @for (provider of moduleGroup.providers; track provider.providerId) {
              <button type="button" (click)="selectProvider(provider.providerId)"
                class="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium border transition-colors"
                [class]="providerId() === provider.providerId ? 'bg-gray-900 text-white border-gray-900' : 'bg-white text-gray-600 border-gray-200 hover:bg-gray-50'">
                <ui-icon [name]="provider.icon" class="h-3 w-3" />
                {{ provider.displayName }}
              </button>
            }
          </div>
        }
      </div>

      <!-- Filters for the selected kind -->
      @if (selectedProvider(); as provider) {
        @if (provider.filters.length > 0) {
          <div class="flex flex-wrap items-end gap-3 p-3 rounded-lg border border-gray-200 bg-gray-50/60">
            @for (filter of provider.filters; track filter.key) {
              <div class="flex flex-col gap-0.5 text-[11px] font-medium text-gray-500">
                <span>{{ filter.label }}</span>
                @switch (filter.type) {
                  @case ('select') {
                    <select [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                      class="h-8 text-xs border-gray-300 rounded-md py-0 pl-2 pr-7">
                      <option value="">Any</option>
                      @for (option of filter.options ?? []; track option.value) {
                        <option [value]="option.value">{{ option.label }}</option>
                      }
                    </select>
                  }
                  @case ('multiselect') {
                    <select multiple [attr.aria-label]="filter.label" [ngModel]="filterValues(filter.key)" (ngModelChange)="setFilter(filter.key, $event.join(','))"
                      class="text-xs border-gray-300 rounded-md py-0.5 min-w-[8rem]" size="3">
                      @for (option of filter.options ?? []; track option.value) {
                        <option [value]="option.value">{{ option.label }}</option>
                      }
                    </select>
                  }
                  @case ('boolean') {
                    <select [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                      class="h-8 text-xs border-gray-300 rounded-md py-0 pl-2 pr-7">
                      <option value="">Any</option>
                      <option value="true">Yes</option>
                      <option value="false">No</option>
                    </select>
                  }
                  @case ('date') {
                    <input type="date" [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                      class="h-8 text-xs border-gray-300 rounded-md py-0 px-2" />
                  }
                  @case ('daterange') {
                    <span class="flex items-center gap-1">
                      <input type="date" [attr.aria-label]="filter.label + ' from'" [ngModel]="rangePart(filter.key, 0)" (ngModelChange)="setRange(filter.key, 0, $event)"
                        class="h-8 text-xs border-gray-300 rounded-md py-0 px-2" />
                      <span class="text-gray-400">to</span>
                      <input type="date" [attr.aria-label]="filter.label + ' to'" [ngModel]="rangePart(filter.key, 1)" (ngModelChange)="setRange(filter.key, 1, $event)"
                        class="h-8 text-xs border-gray-300 rounded-md py-0 px-2" />
                    </span>
                  }
                  @case ('number') {
                    <input type="number" [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                      [placeholder]="filter.placeholder ?? ''" class="h-8 w-28 text-xs border-gray-300 rounded-md py-0 px-2" />
                  }
                  @case ('numberrange') {
                    <span class="flex items-center gap-1">
                      <input type="number" placeholder="Min" [attr.aria-label]="filter.label + ' minimum'" [ngModel]="rangePart(filter.key, 0)" (ngModelChange)="setRange(filter.key, 0, $event)"
                        class="h-8 w-24 text-xs border-gray-300 rounded-md py-0 px-2" />
                      <span class="text-gray-400">to</span>
                      <input type="number" placeholder="Max" [attr.aria-label]="filter.label + ' maximum'" [ngModel]="rangePart(filter.key, 1)" (ngModelChange)="setRange(filter.key, 1, $event)"
                        class="h-8 w-24 text-xs border-gray-300 rounded-md py-0 px-2" />
                    </span>
                  }
                  @default {
                    <input type="text" [attr.aria-label]="filter.label" [ngModel]="filterValue(filter.key)" (ngModelChange)="setFilter(filter.key, $event)"
                      [placeholder]="filter.placeholder ?? ''" class="h-8 w-40 text-xs border-gray-300 rounded-md py-0 px-2" />
                  }
                }
              </div>
            }
            @if (hasFilters()) {
              <button type="button" (click)="clearFilters()" class="h-8 text-xs text-gray-500 hover:text-gray-700 hover:underline">Reset filters</button>
            }
          </div>
        }
      }

      <!-- Results -->
      @if (query().length < minLength) {
        <div class="rounded-lg border border-dashed border-gray-200 p-10 text-center text-sm text-gray-500">
          Type at least {{ minLength }} characters to search.
        </div>
      } @else if (loading()) {
        <div class="rounded-lg border border-gray-200 p-10 text-center text-sm text-gray-500">Searching…</div>
      } @else if (response(); as result) {
        @if (result.groups.length === 0) {
          <div class="rounded-lg border border-dashed border-gray-200 p-10 text-center text-sm text-gray-500">
            No matches for “{{ result.query }}”.
          </div>
        }
        <div class="grid gap-4" [class.lg:grid-cols-2]="providerId() === null && result.groups.length > 1">
          @for (group of result.groups; track group.providerId) {
            <section class="rounded-lg border border-gray-200 bg-white overflow-hidden">
              <header class="flex items-center justify-between px-4 py-2.5 border-b border-gray-100 bg-gray-50/70">
                <div class="flex items-center gap-2 min-w-0">
                  <span class="inline-flex h-6 w-6 items-center justify-center rounded" [class]="moduleTone(group.module)">
                    <ui-icon [name]="group.icon" class="h-3.5 w-3.5" />
                  </span>
                  <h2 class="text-sm font-semibold text-gray-900 truncate">{{ group.displayName }}</h2>
                  @if (group.module !== platformModule) {
                    <span class="text-xs text-gray-400 truncate">· {{ moduleLabel(group.module) }}</span>
                  }
                  <span class="text-xs text-gray-400">({{ group.totalCount }})</span>
                </div>
                <div class="flex items-center gap-3 shrink-0 text-xs">
                  @if (providerId() === null && group.totalCount > group.items.length) {
                    <button type="button" (click)="selectProvider(group.providerId)" class="text-primary-600 hover:text-primary-700">Show all here</button>
                  }
                  @if (group.seeAllRoute) {
                    <a [href]="group.seeAllRoute" (click)="go($event, group.seeAllRoute)" class="text-gray-500 hover:text-gray-700">Open list</a>
                  }
                </div>
              </header>
              <ul class="divide-y divide-gray-100">
                @for (item of group.items; track item.id) {
                  <li>
                    <a [href]="hrefFor(item)" (click)="openItem($event, item)"
                      class="flex items-start gap-3 px-4 py-2.5 hover:bg-gray-50 transition-colors">
                      @if (item.imageUrl) {
                        <img [src]="item.imageUrl" alt="" class="h-9 w-9 rounded-full object-cover shrink-0" />
                      } @else {
                        <span class="inline-flex h-9 w-9 items-center justify-center rounded-full shrink-0" [class]="moduleTone(group.module)">
                          <ui-icon [name]="item.icon ?? group.icon" class="h-4 w-4" />
                        </span>
                      }
                      <span class="min-w-0 flex-1">
                        <span class="block truncate text-sm font-medium text-gray-900">{{ item.title }}</span>
                        @if (item.subtitle) {
                          <span class="block truncate text-xs text-gray-500">{{ item.subtitle }}</span>
                        }
                        @if (item.description) {
                          <span class="block truncate text-xs text-gray-400">{{ item.description }}</span>
                        }
                      </span>
                      @if (item.badges.length > 0) {
                        <span class="flex flex-wrap justify-end gap-1 shrink-0 max-w-[14rem]">
                          @for (badge of item.badges; track badge.label + badge.value) {
                            <span class="inline-flex px-1.5 py-0.5 rounded text-[11px] font-medium" [class]="badgeTone(badge.tone)" [title]="badge.label">{{ badge.value }}</span>
                          }
                        </span>
                      }
                    </a>
                  </li>
                }
              </ul>
            </section>
          }
        </div>
      }
    </div>
  `,
})
export class SearchResultsPageComponent implements OnInit, OnDestroy {
  private readonly searchService = inject(AdminSearchService);
  private readonly tenantService = inject(TenantService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy$ = new Subject<void>();

  readonly minLength = MIN_SEARCH_LENGTH;
  readonly platformModule = PLATFORM_MODULE;

  draft = '';
  readonly query = signal('');
  readonly providerId = signal<string | null>(null);
  readonly filters = signal<Record<string, string>>({});
  readonly providers = signal<AdminSearchProvider[]>([]);
  readonly modules = signal<Record<string, RegisteredModuleInfo>>({});
  readonly response = signal<AdminSearchResponse | null>(null);
  readonly loading = signal(false);

  readonly selectedProvider = computed(() => this.providers().find(p => p.providerId === this.providerId()) ?? null);
  readonly hasFilters = computed(() => Object.values(this.filters()).some(v => v !== ''));
  readonly providerGroups = computed(() => groupProvidersByModule(this.providers(), m => this.moduleLabel(m)));
  readonly placeholder = computed(() => {
    const provider = this.selectedProvider();
    if (!provider) return 'Search everything you can see';
    const scope = provider.module === PLATFORM_MODULE ? '' : `${this.moduleLabel(provider.module)} `;
    return `Search ${scope}${provider.displayName.toLowerCase()}`;
  });

  ngOnInit(): void {
    this.searchService.getProviders().pipe(takeUntil(this.destroy$)).subscribe(providers => this.providers.set(providers));
    this.tenantService.getRegisteredModules().pipe(takeUntil(this.destroy$)).subscribe(response => {
      const byKey: Record<string, RegisteredModuleInfo> = {};
      for (const module of response.modules) byKey[module.featureKey] = module;
      this.modules.set(byKey);
    });

    this.route.queryParamMap
      .pipe(
        switchMap(params => {
          const query = (params.get('q') ?? '').trim();
          const providerId = params.get('provider');
          const filters: Record<string, string> = {};
          for (const key of params.keys) {
            if (key.startsWith('f.')) filters[key.slice(2)] = params.get(key) ?? '';
          }
          this.query.set(query);
          this.draft = query;
          this.providerId.set(providerId);
          this.filters.set(filters);
          if (query.length < MIN_SEARCH_LENGTH) {
            this.response.set(null);
            this.loading.set(false);
            return of(null);
          }
          this.loading.set(true);
          return this.searchService.search(query, { providerId: providerId ?? undefined, filters, limit: providerId ? 50 : 10 })
            .pipe(catchError(() => of({ query, totalCount: 0, groups: [] } as AdminSearchResponse)));
        }),
        takeUntil(this.destroy$),
      )
      .subscribe(response => {
        this.response.set(response);
        this.loading.set(false);
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  submit(event: Event): void {
    event.preventDefault();
    this.apply(this.draft.trim(), this.providerId(), this.filters());
  }

  /** Emptying the box, by typing or the field's clear button, drops the results straight away. */
  onDraftChange(value: string): void {
    this.draft = value;
    if (value.trim() === '' && this.query() !== '') {
      this.apply('', this.providerId(), this.filters());
    }
  }

  selectProvider(providerId: string | null): void {
    this.apply(this.query(), providerId, {});
  }

  filterValue(key: string): string {
    return this.filters()[key] ?? '';
  }

  filterValues(key: string): string[] {
    const value = this.filterValue(key);
    return value ? value.split(',') : [];
  }

  setFilter(key: string, value: string | null): void {
    this.apply(this.query(), this.providerId(), { ...this.filters(), [key]: value ?? '' });
  }

  rangePart(key: string, index: 0 | 1): string {
    return this.filterValue(key).split('..')[index] ?? '';
  }

  setRange(key: string, index: 0 | 1, value: string | number | null): void {
    const parts = this.filterValue(key).split('..');
    parts[index] = value == null ? '' : String(value);
    const joined = `${parts[0] ?? ''}..${parts[1] ?? ''}`;
    this.setFilter(key, joined === '..' ? '' : joined);
  }

  clearFilters(): void {
    this.apply(this.query(), this.providerId(), {});
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

  hrefFor(item: AdminSearchResultItem): string {
    return item.route || workspaceHref(item.workspaceRoute) || '#';
  }

  openItem(event: MouseEvent, item: AdminSearchResultItem): void {
    if (item.route) {
      event.preventDefault();
      this.router.navigateByUrl(item.route);
      return;
    }
    if (!workspaceHref(item.workspaceRoute)) event.preventDefault();
  }

  go(event: MouseEvent, route: string): void {
    event.preventDefault();
    this.router.navigateByUrl(route);
  }

  private apply(query: string, providerId: string | null, filters: Record<string, string>): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: searchPageParams(query, providerId, filters) });
  }
}

import { Component, inject, signal, OnInit, OnDestroy, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject, takeUntil, interval } from 'rxjs';
import { IconComponent } from '@syndew/oluso-ui-core';
import { TelemetryService } from '../../services/telemetry.service';
import { LogEntry, LOG_LEVEL_CONFIG, LogLevel } from '../../models/telemetry.model';

@Component({
  selector: 'lib-telemetry-logs',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex justify-between items-center">
        <div>
          <div class="flex items-center gap-2">
            <h1 class="text-2xl font-semibold text-gray-900">Application Logs</h1>
            @if (backend(); as b) {
              <span
                class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                [class.bg-violet-100]="b === 'loki'"
                [class.text-violet-700]="b === 'loki'"
                [class.bg-gray-100]="b !== 'loki'"
                [class.text-gray-600]="b !== 'loki'"
                [title]="'Reading from ' + b"
              >
                {{ b }}
              </span>
            }
          </div>
          <p class="text-gray-500 mt-1">Search and filter logs from all services</p>
        </div>
        <div class="flex items-center gap-2">
          <!-- Theme toggle -->
          <button
            (click)="toggleTheme()"
            class="inline-flex items-center px-3 py-2 rounded-md text-sm bg-gray-100 text-gray-700 hover:bg-gray-200"
            [title]="darkMode() ? 'Switch to light mode' : 'Switch to dark mode'"
          >
            @if (darkMode()) {
              <ui-icon name="sun" class="h-4 w-4" />
            } @else {
              <ui-icon name="moon" class="h-4 w-4" />
            }
          </button>
          <button
            (click)="toggleLiveTail()"
            class="inline-flex items-center px-3 py-2 rounded-md text-sm"
            [class.bg-green-600]="liveTail()"
            [class.text-white]="liveTail()"
            [class.bg-gray-100]="!liveTail()"
            [class.text-gray-700]="!liveTail()"
            [class.hover:bg-gray-200]="!liveTail()"
          >
            @if (liveTail()) {
              <ui-icon name="stop-circle" class="h-4 w-4 mr-1" />
              Stop Live Tail
            } @else {
              <ui-icon name="play-circle" class="h-4 w-4 mr-1" />
              Live Tail
            }
          </button>
          <button
            (click)="loadLogs()"
            [disabled]="loading()"
            class="inline-flex items-center px-3 py-2 bg-gray-100 rounded-md hover:bg-gray-200 text-sm disabled:opacity-50"
          >
            <svg
              class="h-4 w-4 mr-1"
              [class.animate-spin]="loading()"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
              />
            </svg>
            Refresh
          </button>
        </div>
      </div>

      <!-- Filters -->
      <form (ngSubmit)="handleSearch()" class="flex gap-4 items-end">
        <div class="flex-1">
          <label class="block text-sm font-medium text-gray-700 mb-1">Search</label>
          <div class="relative">
            <ui-icon name="search" class="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              [(ngModel)]="searchTerm"
              name="searchTerm"
              placeholder="Search logs..."
              class="w-full pl-10 pr-4 py-2 border rounded-md"
            />
          </div>
        </div>
        <div class="w-40">
          <label class="block text-sm font-medium text-gray-700 mb-1">Level</label>
          <select
            [(ngModel)]="level"
            name="level"
            class="w-full border rounded-md px-3 py-2"
          >
            <option value="">All Levels</option>
            <option value="trace">Trace</option>
            <option value="debug">Debug</option>
            <option value="info">Info</option>
            <option value="warn">Warning</option>
            <option value="error">Error</option>
            <option value="fatal">Fatal</option>
          </select>
        </div>
        <div class="w-48">
          <label class="block text-sm font-medium text-gray-700 mb-1">Category</label>
          <select
            [(ngModel)]="category"
            name="category"
            class="w-full border rounded-md px-3 py-2"
          >
            <option value="">All Categories</option>
            @for (cat of categories(); track cat) {
              <option [value]="cat">{{ cat }}</option>
            }
          </select>
        </div>
        <button
          type="submit"
          class="inline-flex items-center px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700"
        >
          <ui-icon name="funnel" class="h-4 w-4 mr-1" />
          Filter
        </button>
      </form>

      @if (error()) {
        <div class="bg-red-50 text-red-700 p-4 rounded-md">{{ error() }}</div>
      }

      <!-- Results info -->
      <div class="flex justify-between items-center">
        <p class="text-sm text-gray-500">
          @if (totalCount() > 0) {
            Showing {{ (currentPage() - 1) * pageSize + 1 }} -
            {{ Math.min(currentPage() * pageSize, totalCount()) }} of
            {{ totalCount().toLocaleString() }} logs
          } @else {
            No logs found
          }
        </p>
        @if (liveTail()) {
          <span class="inline-flex items-center text-sm text-green-600">
            <span class="h-2 w-2 bg-green-500 rounded-full mr-2 animate-pulse"></span>
            Live updating...
          </span>
        }
      </div>

      <!-- Log Viewer -->
      <div
        class="rounded-lg overflow-hidden font-mono text-sm"
        [style.backgroundColor]="darkMode() ? '#000' : '#fff'"
        [style.color]="darkMode() ? '#f3f4f6' : '#111827'"
        [style.border]="darkMode() ? 'none' : '1px solid #e5e7eb'"
      >
        <div class="max-h-[600px] overflow-y-auto">
          @if (loading()) {
            <div class="p-8 text-center" [style.color]="darkMode() ? '#9ca3af' : '#6b7280'">Loading logs...</div>
          } @else if (logs().length === 0) {
            <div class="p-8 text-center" [style.color]="darkMode() ? '#9ca3af' : '#6b7280'">No logs found</div>
          } @else {
            @for (log of logs(); track log.id) {
              <div
                [style.borderBottom]="darkMode() ? '1px solid #1f2937' : '1px solid #e5e7eb'"
                [style.backgroundColor]="'transparent'"
                class="hover-row"
              >
                <div
                  class="flex items-start p-2 cursor-pointer"
                  (click)="toggleExpand(log.id)"
                >
                  <div class="w-5 flex-shrink-0 mt-0.5">
                    @if (hasDetails(log)) {
                      @if (expandedLogs().has(log.id)) {
                        <ui-icon name="chevron-down" class="h-4 w-4" [style.color]="darkMode() ? '#6b7280' : '#6b7280'" />
                      } @else {
                        <ui-icon name="chevron-right" class="h-4 w-4" [style.color]="darkMode() ? '#6b7280' : '#6b7280'" />
                      }
                    }
                  </div>
                  <span class="w-44 flex-shrink-0" [style.color]="darkMode() ? '#9ca3af' : '#6b7280'">
                    {{ formatDate(log.timestamp) }}
                  </span>
                  <span
                    class="px-2 py-0.5 rounded text-xs font-medium w-16 text-center flex-shrink-0"
                    [ngStyle]="getLevelStyles(log.level)"
                  >
                    {{ getLevelLabel(log.level) }}
                  </span>
                  <span class="w-32 flex-shrink-0 ml-2 truncate" [style.color]="darkMode() ? '#e879f9' : '#9333ea'">
                    {{ log.category }}
                  </span>
                  <span class="ml-2 flex-1 break-all" [style.color]="darkMode() ? '#fff' : '#1f2937'">
                    {{ log.message }}
                  </span>
                </div>
                @if (expandedLogs().has(log.id) && hasDetails(log)) {
                  <div class="px-8 pb-4 space-y-3 text-sm" [style.color]="darkMode() ? '#e5e7eb' : '#374151'">
                    <!-- Context info row -->
                    <div class="flex flex-wrap gap-4 text-xs">
                      @if (log.requestMethod || log.requestPath) {
                        <div class="flex items-center gap-1">
                          <span [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Request:</span>
                          <span class="font-semibold" [style.color]="darkMode() ? '#22d3ee' : '#0891b2'">{{ log.requestMethod }}</span>
                          <span [style.color]="darkMode() ? '#22d3ee' : '#0891b2'">{{ log.requestPath }}</span>
                        </div>
                      }
                      @if (log.userId) {
                        <div class="flex items-center gap-1">
                          <span [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">User:</span>
                          <span [style.color]="darkMode() ? '#00ff88' : '#16a34a'">{{ log.userId }}</span>
                        </div>
                      }
                      @if (log.clientId) {
                        <div class="flex items-center gap-1">
                          <span [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Client:</span>
                          <span [style.color]="darkMode() ? '#fbbf24' : '#d97706'">{{ log.clientId }}</span>
                        </div>
                      }
                      @if (log.machineName) {
                        <div class="flex items-center gap-1">
                          <span [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Machine:</span>
                          <span [style.color]="darkMode() ? '#d1d5db' : '#4b5563'">{{ log.machineName }}</span>
                        </div>
                      }
                    </div>

                    <!-- Trace info -->
                    @if (log.traceId) {
                      <div class="flex items-center gap-2 text-xs">
                        <span [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Trace:</span>
                        <button
                          (click)="navigateToTrace(log.traceId!); $event.stopPropagation()"
                          class="font-mono hover:underline"
                          [style.color]="darkMode() ? '#38bdf8' : '#2563eb'"
                        >
                          {{ log.traceId }}
                        </button>
                        @if (log.spanId) {
                          <span [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Span:</span>
                          <span class="font-mono" [style.color]="darkMode() ? '#9ca3af' : '#6b7280'">{{ log.spanId }}</span>
                        }
                      </div>
                    }

                    <!-- Properties -->
                    @if (log.properties && hasProperties(log.properties)) {
                      <div>
                        <span class="text-xs" [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Properties:</span>
                        <pre
                          class="mt-1 text-xs p-3 rounded border overflow-x-auto"
                          [style.color]="darkMode() ? '#00ff88' : '#15803d'"
                          [style.backgroundColor]="darkMode() ? '#000' : '#f9fafb'"
                          [style.borderColor]="darkMode() ? '#1f2937' : '#e5e7eb'"
                        >{{ formatJson(log.properties) }}</pre>
                      </div>
                    }

                    <!-- Exception details -->
                    @if (log.exception || log.exceptionType) {
                      <div>
                        <div class="flex items-center gap-2 text-xs mb-1">
                          <span class="font-semibold" [style.color]="darkMode() ? '#f87171' : '#dc2626'">Exception:</span>
                          @if (log.exceptionType) {
                            <span class="font-mono" [style.color]="darkMode() ? '#fca5a5' : '#dc2626'">{{ log.exceptionType }}</span>
                          }
                        </div>
                        @if (log.exception) {
                          <pre
                            class="text-xs p-3 rounded border overflow-x-auto whitespace-pre-wrap"
                            [style.color]="darkMode() ? '#fca5a5' : '#b91c1c'"
                            [style.backgroundColor]="darkMode() ? 'rgba(127, 29, 29, 0.3)' : '#fef2f2'"
                            [style.borderColor]="darkMode() ? '#7f1d1d' : '#fecaca'"
                          >{{ log.exception }}</pre>
                        }
                      </div>
                    }

                    <!-- Stack trace -->
                    @if (log.stackTrace) {
                      <div>
                        <span class="text-xs" [style.color]="darkMode() ? '#6b7280' : '#9ca3af'">Stack Trace:</span>
                        <pre
                          class="mt-1 text-xs p-3 rounded border overflow-x-auto whitespace-pre-wrap max-h-64 overflow-y-auto"
                          [style.color]="darkMode() ? '#fdba74' : '#c2410c'"
                          [style.backgroundColor]="darkMode() ? '#000' : '#f9fafb'"
                          [style.borderColor]="darkMode() ? '#1f2937' : '#e5e7eb'"
                        >{{ log.stackTrace }}</pre>
                      </div>
                    }
                  </div>
                }
              </div>
            }
          }
        </div>
      </div>

      <!-- Pagination -->
      @if (totalPages() > 1) {
        <div class="flex justify-center items-center gap-2">
          <button
            (click)="goToPage(currentPage() - 1)"
            [disabled]="currentPage() === 1"
            class="px-3 py-1 border rounded-md disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Previous
          </button>
          <span class="text-sm text-gray-600">
            Page {{ currentPage() }} of {{ totalPages() }}
          </span>
          <button
            (click)="goToPage(currentPage() + 1)"
            [disabled]="currentPage() === totalPages()"
            class="px-3 py-1 border rounded-md disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Next
          </button>
        </div>
      }
    </div>
  `,
})
export class TelemetryLogsComponent implements OnInit, OnDestroy {
  private readonly telemetryService = inject(TelemetryService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy$ = new Subject<void>();

  logs = signal<LogEntry[]>([]);
  categories = signal<string[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  totalCount = signal(0);
  liveTail = signal(false);
  expandedLogs = signal<Set<string>>(new Set());

  // Theme preference stored in localStorage
  readonly THEME_KEY = 'telemetry-logs-theme';
  darkMode = signal(this.loadThemePreference());

  // Filters
  searchTerm = '';
  level = '';
  category = '';
  currentPage = signal(1);
  readonly pageSize = 50;

  totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize) || 1);

  // Expose Math for template
  Math = Math;

  // Backend label rendered in the header. Null until the status endpoint
  // resolves so we don't flash an incorrect badge during the round-trip.
  backend = signal<string | null>(null);

  ngOnInit(): void {
    // Read initial filters from query params
    this.searchTerm = this.route.snapshot.queryParams['search'] || '';
    this.level = this.route.snapshot.queryParams['level'] || '';
    this.category = this.route.snapshot.queryParams['category'] || '';

    this.loadBackendStatus();
    this.loadCategories();
    this.loadLogs();
  }

  loadBackendStatus(): void {
    this.telemetryService.getLogBackendStatus().subscribe({
      next: (status) => this.backend.set(status.isConfigured ? status.backend : null),
      error: () => {
        // Status endpoint missing in older deployments — leave badge hidden
        // rather than guessing. The rest of the page still works.
        this.backend.set(null);
      },
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadCategories(): void {
    this.telemetryService.getLogCategories().subscribe({
      next: (cats) => this.categories.set(cats),
      error: (err) => console.error('Failed to load categories:', err),
    });
  }

  loadLogs(silent = false): void {
    if (!silent) {
      this.loading.set(true);
    }
    this.error.set(null);

    this.telemetryService
      .getLogs({
        searchTerm: this.searchTerm || undefined,
        level: this.level || undefined,
        category: this.category || undefined,
        page: this.currentPage(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (result) => {
          this.logs.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err) => {
          if (!silent) {
            this.error.set('Failed to load logs');
          }
          console.error(err);
          this.loading.set(false);
        },
      });
  }

  handleSearch(): void {
    this.currentPage.set(1);
    // Update URL params
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        search: this.searchTerm || null,
        level: this.level || null,
        category: this.category || null,
      },
      queryParamsHandling: 'merge',
    });
    this.loadLogs();
  }

  toggleLiveTail(): void {
    this.liveTail.update((v) => !v);
    if (this.liveTail()) {
      interval(2000)
        .pipe(takeUntil(this.destroy$))
        .subscribe(() => {
          if (this.liveTail()) {
            this.loadLogs(true); // Silent refresh to avoid flicker
          }
        });
    }
  }

  toggleExpand(logId: string): void {
    this.expandedLogs.update((set) => {
      const newSet = new Set(set);
      if (newSet.has(logId)) {
        newSet.delete(logId);
      } else {
        newSet.add(logId);
      }
      return newSet;
    });
  }

  hasDetails(log: LogEntry): boolean {
    return !!(
      log.properties ||
      log.exception ||
      log.exceptionType ||
      log.stackTrace ||
      log.traceId ||
      log.requestPath ||
      log.userId ||
      log.clientId ||
      log.machineName
    );
  }

  hasProperties(props: Record<string, unknown>): boolean {
    return Object.keys(props).length > 0;
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
      this.loadLogs();
    }
  }

  navigateToTrace(traceId: string): void {
    this.router.navigate(['/telemetry/traces', traceId]);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  formatJson(obj: unknown): string {
    return JSON.stringify(obj, null, 2);
  }

  /**
   * Normalize log level from backend format to frontend format
   * Backend returns: "Trace", "Debug", "Information", "Warning", "Error", "Critical"
   * Frontend expects: "trace", "debug", "info", "warn", "error", "fatal"
   */
  private normalizeLevel(level: string): LogLevel {
    const levelMap: Record<string, LogLevel> = {
      'trace': 'trace',
      'debug': 'debug',
      'information': 'info',
      'info': 'info',
      'warning': 'warn',
      'warn': 'warn',
      'error': 'error',
      'critical': 'fatal',
      'fatal': 'fatal',
    };
    return levelMap[level.toLowerCase()] || 'info';
  }

  getLevelClasses(level: string): string {
    const normalizedLevel = this.normalizeLevel(level);
    const config = LOG_LEVEL_CONFIG[normalizedLevel];
    return `${config.bgColor} ${config.textColor}`;
  }

  getLevelLabel(level: string): string {
    const normalizedLevel = this.normalizeLevel(level);
    return LOG_LEVEL_CONFIG[normalizedLevel].label;
  }

  getLevelStyles(level: string): Record<string, string> {
    const normalizedLevel = this.normalizeLevel(level);
    const styles: Record<LogLevel, { bg: string; color: string }> = {
      trace: { bg: '#374151', color: '#d1d5db' },
      debug: { bg: '#4b5563', color: '#d1d5db' },
      info: { bg: '#1e3a5f', color: '#93c5fd' },
      warn: { bg: '#713f12', color: '#fde047' },
      error: { bg: '#7f1d1d', color: '#fca5a5' },
      fatal: { bg: '#991b1b', color: '#fef2f2' },
    };
    const style = styles[normalizedLevel];
    return { backgroundColor: style.bg, color: style.color };
  }

  // Theme management
  loadThemePreference(): boolean {
    if (typeof localStorage !== 'undefined') {
      const saved = localStorage.getItem(this.THEME_KEY);
      return saved === null ? true : saved === 'dark'; // Default to dark
    }
    return true;
  }

  toggleTheme(): void {
    this.darkMode.update((v) => !v);
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(this.THEME_KEY, this.darkMode() ? 'dark' : 'light');
    }
  }

  // Get dynamic classes for dark/light mode - true black terminal style for dark mode
  getLogViewerClasses(): string {
    return this.darkMode()
      ? 'bg-black text-gray-100'
      : 'bg-white text-gray-900 border border-gray-200';
  }

  getLogRowClasses(): string {
    return this.darkMode()
      ? 'border-b border-gray-800 hover:bg-gray-900'
      : 'border-b border-gray-200 hover:bg-gray-50';
  }

  getTimestampClasses(): string {
    return this.darkMode() ? 'text-gray-400' : 'text-gray-500';
  }

  getCategoryClasses(): string {
    return this.darkMode() ? 'text-fuchsia-400' : 'text-purple-600';
  }

  getMessageClasses(): string {
    return this.darkMode() ? 'text-white' : 'text-gray-800';
  }

  getExpandIconClasses(): string {
    return this.darkMode() ? 'text-gray-500' : 'text-gray-500';
  }

  getDetailsContainerClasses(): string {
    return this.darkMode() ? 'text-gray-200' : 'text-gray-700';
  }

  getPropertiesPreClasses(): string {
    return this.darkMode()
      ? 'text-emerald-400 bg-black border-gray-800'
      : 'text-green-700 bg-gray-50 border-gray-200';
  }

  getExceptionPreClasses(): string {
    return this.darkMode()
      ? 'text-red-400 bg-red-950/50 border-red-900'
      : 'text-red-700 bg-red-50 border-red-200';
  }

  getStackTracePreClasses(): string {
    return this.darkMode()
      ? 'text-orange-300 bg-black border-gray-800'
      : 'text-red-700 bg-gray-50 border-gray-200';
  }

  getLabelClasses(): string {
    return this.darkMode() ? 'text-gray-500' : 'text-gray-400';
  }

  getContextValueClasses(type: 'request' | 'user' | 'client' | 'machine'): string {
    if (this.darkMode()) {
      switch (type) {
        case 'request': return 'text-cyan-300';
        case 'user': return 'text-emerald-400';
        case 'client': return 'text-amber-400';
        case 'machine': return 'text-gray-300';
      }
    } else {
      switch (type) {
        case 'request': return 'text-cyan-600';
        case 'user': return 'text-green-600';
        case 'client': return 'text-yellow-600';
        case 'machine': return 'text-gray-600';
      }
    }
  }

  getTraceLinkClasses(): string {
    return this.darkMode()
      ? 'text-sky-400 hover:text-sky-300 hover:underline font-mono'
      : 'text-blue-600 hover:underline font-mono';
  }

  getSpanIdClasses(): string {
    return this.darkMode() ? 'text-gray-400 font-mono' : 'text-gray-500 font-mono';
  }

  getExceptionLabelClasses(): string {
    return this.darkMode() ? 'text-red-500 font-semibold' : 'text-red-600 font-semibold';
  }

  getExceptionTypeClasses(): string {
    return this.darkMode() ? 'text-red-400 font-mono' : 'text-red-600 font-mono';
  }
}

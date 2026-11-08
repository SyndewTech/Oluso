import { Component, inject, signal, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { IconComponent } from '@oluso/ui-core';
import { TelemetryService } from '../../services/telemetry.service';
import {
  Trace,
  TraceSpan,
  TraceStatus,
  TRACE_STATUS_CONFIG,
  formatDuration,
} from '../../models/telemetry.model';

@Component({
  selector: 'lib-telemetry-traces',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      @if (traceId()) {
        <!-- Trace Detail View -->
        <div>
          <button
            (click)="handleBack()"
            class="inline-flex items-center text-gray-600 hover:text-gray-900 mb-4"
          >
            <ui-icon name="arrow-left" class="h-4 w-4 mr-1" />
            Back to Traces
          </button>
          <h1 class="text-2xl font-semibold text-gray-900">Trace Details</h1>
        </div>

        @if (error()) {
          <div class="bg-red-50 text-red-700 p-4 rounded-md">{{ error() }}</div>
        }

        @if (loading()) {
          <div class="flex items-center justify-center p-12">
            <svg
              class="h-8 w-8 animate-spin text-gray-400"
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
          </div>
        } @else if (selectedTrace()) {
          <!-- Trace Viewer -->
          <div class="bg-white rounded-lg shadow overflow-hidden">
            <div class="p-4 border-b bg-gray-50">
              <div class="flex items-center justify-between">
                <div>
                  <h3 class="font-medium text-gray-900">{{ selectedTrace()!.rootSpan.operationName }}</h3>
                  <p class="text-sm text-gray-500">
                    Trace ID: <code class="bg-gray-100 px-1 rounded">{{ selectedTrace()!.traceId }}</code>
                  </p>
                </div>
                <div class="text-right">
                  <p class="text-lg font-semibold">{{ formatDuration(selectedTrace()!.duration) }}</p>
                  <p class="text-sm text-gray-500">
                    {{ formatDate(selectedTrace()!.startTime) }}
                  </p>
                </div>
              </div>
            </div>
            <div class="divide-y">
              @for (span of getSpanTree(selectedTrace()!); track span.spanId) {
                <div
                  class="flex items-center py-2 px-2 hover:bg-gray-50 cursor-pointer border-b"
                  [style.padding-left.px]="span.depth * 24 + 8"
                  (click)="toggleSpanExpand(span.spanId)"
                >
                  <div class="w-5 flex-shrink-0">
                    @if (span.hasChildren) {
                      @if (expandedSpans().has(span.spanId)) {
                        <ui-icon name="chevron-down" class="h-4 w-4 text-gray-400" />
                      } @else {
                        <ui-icon name="chevron-right" class="h-4 w-4 text-gray-400" />
                      }
                    }
                  </div>
                  <!-- Status Icon -->
                  @if (span.status === 'ok') {
                    <ui-icon name="check-circle" [class]="'h-4 w-4 mr-2 ' + getStatusColor(span.status)" />
                  } @else if (span.status === 'error') {
                    <ui-icon name="x-circle" [class]="'h-4 w-4 mr-2 ' + getStatusColor(span.status)" />
                  } @else {
                    <svg [class]="'h-4 w-4 mr-2 ' + getStatusColor(span.status)" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12H9m12 0a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                  }
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                      <span class="font-medium text-gray-900 truncate">{{ span.operationName }}</span>
                      <span class="text-xs text-gray-500">{{ span.serviceName }}</span>
                    </div>
                  </div>
                  <!-- Timing bar -->
                  <div class="w-64 flex-shrink-0 mx-4">
                    <div class="h-2 bg-gray-100 rounded relative">
                      <div
                        class="absolute h-full rounded"
                        [class.bg-red-400]="span.status === 'error'"
                        [class.bg-blue-400]="span.status !== 'error'"
                        [style.left.%]="getSpanOffset(span, selectedTrace()!)"
                        [style.width.%]="getSpanWidth(span, selectedTrace()!)"
                      ></div>
                    </div>
                  </div>
                  <span class="text-sm text-gray-600 w-20 text-right">
                    {{ formatDuration(span.duration) }}
                  </span>
                </div>
              }
            </div>
          </div>
        } @else {
          <div class="text-center text-gray-500 p-12">Trace not found</div>
        }
      } @else {
        <!-- Trace List View -->
        <div class="flex justify-between items-center">
          <div>
            <h1 class="text-2xl font-semibold text-gray-900">Distributed Traces</h1>
            <p class="text-gray-500 mt-1">
              Track requests across services and identify performance bottlenecks
            </p>
          </div>
          <button
            (click)="loadTraces()"
            [disabled]="loading() || backendConfigured() === false"
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

        @if (backendConfigured() === false) {
          <!-- Trace backend not configured (e.g. dev/staging without Tempo) -->
          <div class="bg-amber-50 border border-amber-200 rounded-md p-6">
            <div class="flex items-start gap-3">
              <ui-icon name="information-circle" class="h-5 w-5 text-amber-600 flex-shrink-0 mt-0.5" />
              <div>
                <h3 class="font-medium text-amber-900">Distributed tracing isn't configured for this environment</h3>
                <p class="text-sm text-amber-800 mt-1">
                  Spans are still being captured and exported via OTLP, but no
                  query backend (e.g. Grafana Tempo) is wired up here so traces
                  can't be displayed. Set
                  <code class="bg-amber-100 px-1 rounded text-xs">Oluso__Telemetry__TempoEndpoint</code>
                  on the API service to point at your Tempo HTTP API
                  (typically <code class="bg-amber-100 px-1 rounded text-xs">http://tempo:3200</code>)
                  to enable this view.
                </p>
              </div>
            </div>
          </div>
        } @else {
          <!-- Search -->
          <form (ngSubmit)="handleSearch()" class="flex gap-4">
            <div class="flex-1 relative">
              <ui-icon name="search" class="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-gray-400" />
              <input
                type="text"
                [(ngModel)]="searchTerm"
                name="searchTerm"
                placeholder="Search by operation name, trace ID, or service..."
                class="w-full pl-10 pr-4 py-2 border rounded-md"
              />
            </div>
            <button
              type="submit"
              class="px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700"
            >
              Search
            </button>
          </form>

          @if (error()) {
            <div class="bg-red-50 text-red-700 p-4 rounded-md">{{ error() }}</div>
          }

        <!-- Results info -->
        <div>
          <p class="text-sm text-gray-500">
            @if (totalCount() > 0) {
              Showing {{ (currentPage() - 1) * pageSize + 1 }} -
              {{ Math.min(currentPage() * pageSize, totalCount()) }} of
              {{ totalCount().toLocaleString() }} traces
            } @else {
              No traces found
            }
          </p>
        </div>

        <!-- Trace list -->
        @if (loading() && traces().length === 0) {
          <div class="flex items-center justify-center p-12">
            <svg
              class="h-8 w-8 animate-spin text-gray-400"
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
          </div>
        } @else {
          <div class="bg-white rounded-lg shadow overflow-hidden">
            <table class="min-w-full divide-y divide-gray-200">
              <thead class="bg-gray-50">
                <tr>
                  <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Operation</th>
                  <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Service</th>
                  <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                  <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Duration</th>
                  <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Time</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-gray-200">
                @if (traces().length === 0) {
                  <tr>
                    <td colspan="5" class="px-4 py-8 text-center text-gray-500">No traces found</td>
                  </tr>
                } @else {
                  @for (trace of traces(); track trace.traceId) {
                    <tr
                      class="hover:bg-gray-50 cursor-pointer"
                      (click)="selectTrace(trace.traceId)"
                    >
                      <td class="px-4 py-3">
                        <span class="font-medium text-primary-600 hover:underline">
                          {{ trace.rootSpan.operationName }}
                        </span>
                      </td>
                      <td class="px-4 py-3 text-sm text-gray-600">{{ trace.serviceName }}</td>
                      <td class="px-4 py-3">
                        @if (trace.status === 'ok') {
                          <ui-icon name="check-circle" [class]="'h-5 w-5 ' + getStatusColor(trace.status)" />
                        } @else if (trace.status === 'error') {
                          <ui-icon name="x-circle" [class]="'h-5 w-5 ' + getStatusColor(trace.status)" />
                        } @else {
                          <svg [class]="'h-5 w-5 ' + getStatusColor(trace.status)" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12H9m12 0a9 9 0 11-18 0 9 9 0 0118 0z" />
                          </svg>
                        }
                      </td>
                      <td class="px-4 py-3 text-sm">{{ formatDuration(trace.duration) }}</td>
                      <td class="px-4 py-3 text-sm text-gray-500">{{ formatDate(trace.startTime) }}</td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        }

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
        }
      }
    </div>
  `,
})
export class TelemetryTracesComponent implements OnInit {
  private readonly telemetryService = inject(TelemetryService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  traces = signal<Trace[]>([]);
  selectedTrace = signal<Trace | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  totalCount = signal(0);
  expandedSpans = signal<Set<string>>(new Set());
  /**
   * Trace backend configured-state.
   * - `null`: status check still in flight (initial load)
   * - `true`: Tempo (or other) backend is wired; render search/list
   * - `false`: no backend; render the "not configured" notice instead
   */
  backendConfigured = signal<boolean | null>(null);

  searchTerm = '';
  currentPage = signal(1);
  readonly pageSize = 20;

  totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize) || 1);
  traceId = signal<string | null>(null);

  // Expose Math and formatDuration for template
  Math = Math;
  formatDuration = formatDuration;

  ngOnInit(): void {
    // Check trace backend status first so we can render the "not configured"
    // banner without firing a doomed search query.
    this.telemetryService.getTraceBackendStatus().subscribe({
      next: (status) => {
        this.backendConfigured.set(status.isConfigured);
        if (status.isConfigured) {
          this.bindRoute();
        }
      },
      error: () => {
        // Status endpoint missing or failing — assume configured so we still
        // attempt the load. A real failure surfaces via the search error path.
        this.backendConfigured.set(true);
        this.bindRoute();
      },
    });
  }

  private bindRoute(): void {
    this.route.paramMap.subscribe((params) => {
      const id = params.get('traceId');
      this.traceId.set(id);
      if (id) {
        this.loadTrace(id);
      } else {
        this.loadTraces();
      }
    });
  }

  loadTraces(): void {
    this.loading.set(true);
    this.error.set(null);

    this.telemetryService
      .getTraces({
        searchTerm: this.searchTerm || undefined,
        page: this.currentPage(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (result) => {
          this.traces.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set('Failed to load traces');
          console.error(err);
          this.loading.set(false);
        },
      });
  }

  loadTrace(id: string): void {
    this.loading.set(true);
    this.error.set(null);

    this.telemetryService.getTrace(id).subscribe({
      next: (trace) => {
        this.selectedTrace.set(trace);
        // Expand root span by default
        this.expandedSpans.set(new Set([trace.rootSpan.spanId]));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load trace details');
        console.error(err);
        this.loading.set(false);
      },
    });
  }

  handleSearch(): void {
    this.currentPage.set(1);
    this.loadTraces();
  }

  selectTrace(id: string): void {
    this.router.navigate(['/telemetry/traces', id]);
  }

  handleBack(): void {
    this.selectedTrace.set(null);
    this.router.navigate(['/telemetry/traces']);
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
      this.loadTraces();
    }
  }

  toggleSpanExpand(spanId: string): void {
    this.expandedSpans.update((set) => {
      const newSet = new Set(set);
      if (newSet.has(spanId)) {
        newSet.delete(spanId);
      } else {
        newSet.add(spanId);
      }
      return newSet;
    });
  }

  getStatusColor(status: TraceStatus): string {
    return TRACE_STATUS_CONFIG[status].textColor;
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  getSpanOffset(span: { startTime: string }, trace: Trace): number {
    const traceStart = new Date(trace.startTime).getTime();
    const spanStart = new Date(span.startTime).getTime();
    return ((spanStart - traceStart) / trace.duration) * 100;
  }

  getSpanWidth(span: { duration: number }, trace: Trace): number {
    return Math.max((span.duration / trace.duration) * 100, 1);
  }

  /**
   * Build a flattened tree of spans for rendering
   */
  getSpanTree(
    trace: Trace
  ): Array<TraceSpan & { depth: number; hasChildren: boolean }> {
    const result: Array<TraceSpan & { depth: number; hasChildren: boolean }> = [];
    const spansByParent = new Map<string | undefined, TraceSpan[]>();

    // Group spans by parent
    for (const span of trace.spans) {
      const children = spansByParent.get(span.parentSpanId) || [];
      children.push(span);
      spansByParent.set(span.parentSpanId, children);
    }

    // Recursive function to traverse
    const traverse = (span: TraceSpan, depth: number) => {
      const children = spansByParent.get(span.spanId) || [];
      const hasChildren = children.length > 0;
      const isExpanded = this.expandedSpans().has(span.spanId);

      result.push({ ...span, depth, hasChildren });

      if (hasChildren && isExpanded) {
        for (const child of children) {
          traverse(child, depth + 1);
        }
      }
    };

    traverse(trace.rootSpan, 0);
    return result;
  }
}

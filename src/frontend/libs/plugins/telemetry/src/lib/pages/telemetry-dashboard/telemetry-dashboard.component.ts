import { Component, inject, signal, OnInit, OnDestroy, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IconComponent } from '@oluso/ui-core';
import { forkJoin, Subject, takeUntil, interval } from 'rxjs';
import { TelemetryService } from '../../services/telemetry.service';
import {
  TelemetryMetrics,
  MetricSeries,
  TimeRange,
} from '../../models/telemetry.model';

@Component({
  selector: 'lib-telemetry-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, IconComponent],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex justify-between items-center">
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">Telemetry Dashboard</h1>
          <p class="text-gray-500 mt-1">
            Monitor system health, performance, and security metrics
          </p>
        </div>
        <div class="flex items-center gap-4">
          <label class="flex items-center gap-2 text-sm text-gray-600">
            <input
              type="checkbox"
              [checked]="autoRefresh()"
              (change)="toggleAutoRefresh()"
              class="rounded"
            />
            Auto-refresh
          </label>
          <select
            [ngModel]="timeRange()"
            (ngModelChange)="setTimeRange($event)"
            class="border rounded-md px-3 py-2 text-sm"
          >
            <option value="1h">Last 1 hour</option>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
            <option value="30d">Last 30 days</option>
          </select>
          <button
            (click)="loadData()"
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

      @if (error()) {
        <div class="bg-red-50 text-red-700 p-4 rounded-md">{{ error() }}</div>
      }

      <!-- Overview Metrics -->
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
        <!-- Tokens Issued -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">Tokens Issued</p>
              <p class="mt-1 text-2xl font-semibold text-gray-900">
                {{ metrics()?.tokensIssued?.toLocaleString() ?? '-' }}
              </p>
            </div>
            <div class="p-3 rounded-full bg-blue-50 text-blue-600">
              <svg class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 5v2m0 4v2m0 4v2M5 5a2 2 0 00-2 2v3a2 2 0 110 4v3a2 2 0 002 2h14a2 2 0 002-2v-3a2 2 0 110-4V7a2 2 0 00-2-2H5z" />
              </svg>
            </div>
          </div>
        </div>

        <!-- Active Users -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">Active Users</p>
              <p class="mt-1 text-2xl font-semibold text-gray-900">
                {{ metrics()?.activeUsers?.toLocaleString() ?? '-' }}
              </p>
            </div>
            <div class="p-3 rounded-full bg-green-50 text-green-600">
              <ui-icon name="user-group" class="h-6 w-6" />
            </div>
          </div>
        </div>

        <!-- Auth Success Rate -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">Auth Success Rate</p>
              <p class="mt-1 text-2xl font-semibold text-gray-900">
                {{ (metrics()?.authSuccessRate ?? 0).toFixed(1) }}%
              </p>
            </div>
            <div
              class="p-3 rounded-full"
              [class.bg-green-50]="(metrics()?.authSuccessRate ?? 0) >= 95"
              [class.text-green-600]="(metrics()?.authSuccessRate ?? 0) >= 95"
              [class.bg-yellow-50]="(metrics()?.authSuccessRate ?? 0) < 95"
              [class.text-yellow-600]="(metrics()?.authSuccessRate ?? 0) < 95"
            >
              <ui-icon name="check-circle" class="h-6 w-6" />
            </div>
          </div>
        </div>

        <!-- Avg Response Time -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">Avg Response Time</p>
              <div class="mt-1 flex items-baseline">
                <p class="text-2xl font-semibold text-gray-900">
                  {{ (metrics()?.avgRequestDuration ?? 0).toFixed(0) }}
                </p>
                <span class="ml-1 text-sm text-gray-500">ms</span>
              </div>
            </div>
            <div
              class="p-3 rounded-full"
              [class.bg-green-50]="(metrics()?.avgRequestDuration ?? 0) < 200"
              [class.text-green-600]="(metrics()?.avgRequestDuration ?? 0) < 200"
              [class.bg-yellow-50]="(metrics()?.avgRequestDuration ?? 0) >= 200"
              [class.text-yellow-600]="(metrics()?.avgRequestDuration ?? 0) >= 200"
            >
              <ui-icon name="clock" class="h-6 w-6" />
            </div>
          </div>
        </div>
      </div>

      <!-- Secondary Metrics -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
        <!-- Token Failures -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">Token Failures</p>
              <p class="mt-1 text-2xl font-semibold text-gray-900">
                {{ metrics()?.tokensFailed?.toLocaleString() ?? '-' }}
              </p>
            </div>
            <div class="p-3 rounded-full bg-red-50 text-red-600">
              <ui-icon name="warning" class="h-6 w-6" />
            </div>
          </div>
        </div>

        <!-- Active Sessions -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">Active Sessions</p>
              <p class="mt-1 text-2xl font-semibold text-gray-900">
                {{ metrics()?.activeSessions?.toLocaleString() ?? '-' }}
              </p>
            </div>
            <div class="p-3 rounded-full bg-purple-50 text-purple-600">
              <ui-icon name="users" class="h-6 w-6" />
            </div>
          </div>
        </div>

        <!-- System Health -->
        <div class="bg-white rounded-lg shadow p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-500">System Health</p>
              <p class="mt-1 text-2xl font-semibold text-gray-900">
                {{ systemHealth() }}
              </p>
            </div>
            <div
              class="p-3 rounded-full"
              [class.bg-green-50]="systemHealth() === 'Healthy'"
              [class.text-green-600]="systemHealth() === 'Healthy'"
              [class.bg-yellow-50]="systemHealth() !== 'Healthy'"
              [class.text-yellow-600]="systemHealth() !== 'Healthy'"
            >
              <ui-icon name="check-circle" class="h-6 w-6" />
            </div>
          </div>
        </div>
      </div>

      <!-- Charts -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <!-- Tokens Over Time -->
        @if (tokenSeries()) {
          <div class="bg-white rounded-lg shadow p-6">
            <h3 class="text-lg font-medium text-gray-900 mb-4">Tokens Issued Over Time</h3>
            <div class="h-48 flex items-end gap-1">
              @for (point of tokenSeries()!.data; track point.timestamp) {
                <div
                  class="flex-1 bg-blue-500 rounded-t hover:bg-blue-600 transition-colors"
                  [style.height.%]="getBarHeight(point.value, tokenSeries()!.data)"
                  [title]="formatChartLabel(point.timestamp) + ': ' + point.value"
                ></div>
              }
            </div>
            <div class="flex justify-between mt-2 text-xs text-gray-500">
              <span>{{ getFirstLabel(tokenSeries()!.data) }}</span>
              <span>{{ getLastLabel(tokenSeries()!.data) }}</span>
            </div>
          </div>
        }

        <!-- Auth Success Over Time -->
        @if (authSeries()) {
          <div class="bg-white rounded-lg shadow p-6">
            <h3 class="text-lg font-medium text-gray-900 mb-4">Authentication Success Over Time</h3>
            <div class="h-48 flex items-end gap-1">
              @for (point of authSeries()!.data; track point.timestamp) {
                <div
                  class="flex-1 bg-green-500 rounded-t hover:bg-green-600 transition-colors"
                  [style.height.%]="getBarHeight(point.value, authSeries()!.data)"
                  [title]="formatChartLabel(point.timestamp) + ': ' + point.value"
                ></div>
              }
            </div>
            <div class="flex justify-between mt-2 text-xs text-gray-500">
              <span>{{ getFirstLabel(authSeries()!.data) }}</span>
              <span>{{ getLastLabel(authSeries()!.data) }}</span>
            </div>
          </div>
        }
      </div>

      <!-- Quick Links -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <a
          routerLink="/telemetry/logs"
          class="block p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow"
        >
          <h3 class="font-medium text-gray-900">View Logs</h3>
          <p class="text-sm text-gray-500 mt-1">Search and filter application logs</p>
        </a>
        <a
          routerLink="/telemetry/traces"
          class="block p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow"
        >
          <h3 class="font-medium text-gray-900">View Traces</h3>
          <p class="text-sm text-gray-500 mt-1">Distributed tracing for requests</p>
        </a>
        <a
          routerLink="/audit-logs"
          class="block p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow"
        >
          <h3 class="font-medium text-gray-900">Audit Logs</h3>
          <p class="text-sm text-gray-500 mt-1">Security and compliance audit trail</p>
        </a>
      </div>
    </div>
  `,
})
export class TelemetryDashboardComponent implements OnInit, OnDestroy {
  private readonly telemetryService = inject(TelemetryService);
  private readonly destroy$ = new Subject<void>();

  metrics = signal<TelemetryMetrics | null>(null);
  tokenSeries = signal<MetricSeries | null>(null);
  authSeries = signal<MetricSeries | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  timeRange = signal<TimeRange>('24h');
  autoRefresh = signal(false);

  systemHealth = computed(() => {
    const m = this.metrics();
    if (!m) return '-';
    return m.authSuccessRate >= 95 && m.avgRequestDuration < 500 ? 'Healthy' : 'Degraded';
  });

  ngOnInit(): void {
    this.loadData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  setTimeRange(range: TimeRange): void {
    this.timeRange.set(range);
    this.loadData();
  }

  toggleAutoRefresh(): void {
    this.autoRefresh.update((v) => !v);
    if (this.autoRefresh()) {
      interval(30000)
        .pipe(takeUntil(this.destroy$))
        .subscribe(() => {
          if (this.autoRefresh()) {
            this.loadData();
          }
        });
    }
  }

  loadData(): void {
    this.loading.set(true);
    this.error.set(null);

    const range = this.timeRange();

    forkJoin({
      metrics: this.telemetryService.getOverviewMetrics(range),
      tokens: this.telemetryService.getMetricSeries('tokens_issued', range),
      auth: this.telemetryService.getMetricSeries('auth_success', range),
    }).subscribe({
      next: ({ metrics, tokens, auth }) => {
        this.metrics.set(metrics);
        this.tokenSeries.set(tokens);
        this.authSeries.set(auth);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load telemetry data');
        console.error(err);
        this.loading.set(false);
      },
    });
  }

  getBarHeight(value: number, data: { value: number }[]): number {
    const max = Math.max(...data.map((d) => d.value), 1);
    return Math.max((value / max) * 100, 2);
  }

  formatChartLabel(timestamp: string): string {
    return new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  getFirstLabel(data: { timestamp: string }[]): string {
    return data.length > 0 ? this.formatChartLabel(data[0].timestamp) : '';
  }

  getLastLabel(data: { timestamp: string }[]): string {
    return data.length > 0 ? this.formatChartLabel(data[data.length - 1].timestamp) : '';
  }
}

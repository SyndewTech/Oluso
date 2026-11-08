import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '@oluso/admin-ui';
import {
  TelemetryMetrics,
  MetricSeries,
  LogEntry,
  Trace,
  TelemetryQuery,
  PagedResult,
  TimeRange,
  getIntervalForRange,
} from '../models/telemetry.model';

@Injectable({
  providedIn: 'root',
})
export class TelemetryService {
  private readonly api = inject(ApiService);

  // ============================================
  // Dashboard Metrics
  // ============================================

  /**
   * Get overview metrics for the telemetry dashboard
   */
  getOverviewMetrics(timeRange: TimeRange = '24h'): Observable<TelemetryMetrics> {
    return this.api.get<TelemetryMetrics>('/telemetry/metrics/overview', { timeRange });
  }

  /**
   * Get time series data for a specific metric
   */
  getMetricSeries(
    metricName: string,
    timeRange: TimeRange = '24h'
  ): Observable<MetricSeries> {
    const interval = getIntervalForRange(timeRange);
    return this.api.get<MetricSeries>(`/telemetry/metrics/${metricName}`, {
      timeRange,
      interval,
    });
  }

  // ============================================
  // Logs
  // ============================================

  /**
   * Get application logs with filtering and pagination
   */
  getLogs(query: TelemetryQuery): Observable<PagedResult<LogEntry>> {
    return this.api.get<PagedResult<LogEntry>>('/telemetry/logs', this.cleanQuery(query));
  }

  /**
   * Get available log categories for filtering
   */
  getLogCategories(): Observable<string[]> {
    return this.api.get<string[]>('/telemetry/logs/categories');
  }

  // ============================================
  // Traces
  // ============================================

  /**
   * Get distributed traces with filtering and pagination
   */
  getTraces(query: TelemetryQuery): Observable<PagedResult<Trace>> {
    return this.api.get<PagedResult<Trace>>('/telemetry/traces', this.cleanQuery(query));
  }

  /**
   * Get details of a specific trace by ID
   */
  getTrace(traceId: string): Observable<Trace> {
    return this.api.get<Trace>(`/telemetry/traces/${traceId}`);
  }

  /**
   * Trace backend configuration status. Used by the Traces page to render
   * a "tracing not configured" notice in environments without Tempo.
   */
  getTraceBackendStatus(): Observable<{ isConfigured: boolean; backend: string }> {
    return this.api.get<{ isConfigured: boolean; backend: string }>('/telemetry/traces/status');
  }

  /**
   * Log backend configuration status. Returns the source the Logs page is
   * actually reading from ("database", "loki", "none") so the UI can
   * surface a small badge and degrade gracefully when nothing is wired.
   */
  getLogBackendStatus(): Observable<{ isConfigured: boolean; backend: string }> {
    return this.api.get<{ isConfigured: boolean; backend: string }>('/telemetry/logs/status');
  }

  // ============================================
  // Helper Methods
  // ============================================

  /**
   * Clean query object by removing undefined values
   */
  private cleanQuery(query: TelemetryQuery): Record<string, string | number | boolean> {
    const cleaned: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        cleaned[key] = value as string | number | boolean;
      }
    }
    return cleaned;
  }
}

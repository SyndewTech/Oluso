import { PaginatedResponse } from '@syndew/oluso-ui-core';

/**
 * Telemetry overview metrics
 */
export interface TelemetryMetrics {
  tokensIssued: number;
  tokensFailed: number;
  activeUsers: number;
  activeSessions: number;
  authSuccessRate: number;
  avgRequestDuration: number;
}

/**
 * A single data point in a metric time series
 */
export interface MetricDataPoint {
  timestamp: string;
  value: number;
  tags?: Record<string, string>;
}

/**
 * Time series data for a specific metric
 */
export interface MetricSeries {
  name: string;
  unit: string;
  data: MetricDataPoint[];
}

/**
 * Log level types
 */
export type LogLevel = 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'fatal';

/**
 * Application log entry
 */
export interface LogEntry {
  id: string;
  timestamp: string;
  level: LogLevel;
  message: string;
  category: string;
  properties?: Record<string, unknown>;
  exception?: string;
  exceptionType?: string;
  stackTrace?: string;
  traceId?: string;
  spanId?: string;
  requestPath?: string;
  requestMethod?: string;
  userId?: string;
  clientId?: string;
  machineName?: string;
}

/**
 * Audit log entry for security/compliance tracking
 */
export interface AuditLogEntry {
  id: string;
  timestamp: string;
  category: string;
  action: string;
  eventType: string;
  subjectId?: string;
  subjectType?: string;
  subjectName?: string;
  subjectEmail?: string;
  success: boolean;
  ipAddress?: string;
  userAgent?: string;
  resourceType?: string;
  resourceId?: string;
  resourceName?: string;
  details?: Record<string, unknown>;
  tenantId?: string;
  clientId?: string;
  activityId?: string;
  errorMessage?: string;
}

/**
 * Trace status types
 */
export type TraceStatus = 'ok' | 'error' | 'unset';

/**
 * An event within a trace span
 */
export interface TraceEvent {
  name: string;
  timestamp: string;
  attributes?: Record<string, unknown>;
}

/**
 * A span within a distributed trace
 */
export interface TraceSpan {
  traceId: string;
  spanId: string;
  parentSpanId?: string;
  operationName: string;
  serviceName: string;
  startTime: string;
  duration: number;
  status: TraceStatus;
  tags: Record<string, string>;
  events: TraceEvent[];
}

/**
 * A complete distributed trace
 */
export interface Trace {
  traceId: string;
  rootSpan: TraceSpan;
  spans: TraceSpan[];
  duration: number;
  startTime: string;
  serviceName: string;
  status: TraceStatus;
}

/**
 * Query parameters for telemetry data
 */
export interface TelemetryQuery {
  startTime?: string;
  endTime?: string;
  level?: string;
  category?: string;
  searchTerm?: string;
  traceId?: string;
  page?: number;
  pageSize?: number;
}

/**
 * Paginated response wrapper — alias kept for backward compatibility.
 */
export type PagedResult<T> = PaginatedResponse<T>;

/**
 * Time range options for dashboard
 */
export type TimeRange = '1h' | '24h' | '7d' | '30d';

/**
 * Get interval based on time range
 */
export function getIntervalForRange(range: TimeRange): string {
  switch (range) {
    case '1h':
      return '5m';
    case '24h':
      return '1h';
    case '7d':
      return '6h';
    case '30d':
      return '1d';
    default:
      return '1h';
  }
}

/**
 * Format duration in ms to human readable string
 */
export function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}us`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

/**
 * Log level display configuration (designed for dark terminal-style background)
 */
export const LOG_LEVEL_CONFIG: Record<
  LogLevel,
  { textColor: string; bgColor: string; label: string }
> = {
  trace: { textColor: 'text-gray-300', bgColor: 'bg-gray-700', label: 'TRACE' },
  debug: { textColor: 'text-gray-300', bgColor: 'bg-gray-600', label: 'DEBUG' },
  info: { textColor: 'text-blue-200', bgColor: 'bg-blue-900', label: 'INFO' },
  warn: { textColor: 'text-yellow-200', bgColor: 'bg-yellow-900', label: 'WARN' },
  error: { textColor: 'text-red-200', bgColor: 'bg-red-900', label: 'ERROR' },
  fatal: { textColor: 'text-red-100', bgColor: 'bg-red-800', label: 'FATAL' },
};

/**
 * Trace status display configuration
 */
export const TRACE_STATUS_CONFIG: Record<
  TraceStatus,
  { textColor: string; bgColor: string }
> = {
  ok: { textColor: 'text-green-500', bgColor: 'bg-green-100' },
  error: { textColor: 'text-red-500', bgColor: 'bg-red-100' },
  unset: { textColor: 'text-gray-400', bgColor: 'bg-gray-100' },
};

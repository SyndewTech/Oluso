import type { AxiosInstance } from 'axios';

let apiClient: AxiosInstance;

export function setApiClient(client: AxiosInstance) {
  apiClient = client;
}

// Types
export interface TelemetryMetrics {
  tokensIssued: number;
  tokensFailed: number;
  activeUsers: number;
  activeSessions: number;
  authSuccessRate: number;
  avgRequestDuration: number;
}

export interface MetricDataPoint {
  timestamp: string;
  value: number;
  tags?: Record<string, string>;
}

export interface MetricSeries {
  name: string;
  unit: string;
  data: MetricDataPoint[];
}

export interface LogEntry {
  id: string;
  timestamp: string;
  level: 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'fatal';
  message: string;
  category: string;
  properties?: Record<string, unknown>;
  exception?: string;
  traceId?: string;
  spanId?: string;
}

export interface TraceSpan {
  traceId: string;
  spanId: string;
  parentSpanId?: string;
  operationName: string;
  serviceName: string;
  startTime: string;
  duration: number;
  status: 'ok' | 'error' | 'unset';
  tags: Record<string, string>;
  events: TraceEvent[];
}

export interface TraceEvent {
  name: string;
  timestamp: string;
  attributes?: Record<string, unknown>;
}

export interface Trace {
  traceId: string;
  rootSpan: TraceSpan;
  spans: TraceSpan[];
  duration: number;
  startTime: string;
  serviceName: string;
  status: 'ok' | 'error' | 'unset';
}

export interface AuditLogEntry {
  id: string;
  timestamp: string;
  eventType: string;
  category: string;
  action: string;
  success: boolean;
  subjectId?: string;
  subjectName?: string;
  subjectEmail?: string;
  resourceType?: string;
  resourceId?: string;
  clientId?: string;
  ipAddress?: string;
  userAgent?: string;
  details?: Record<string, unknown>;
  errorMessage?: string;
  activityId?: string;
}

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

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// API functions
export const telemetryApi = {
  // Dashboard metrics
  async getOverviewMetrics(timeRange: string = '24h'): Promise<TelemetryMetrics> {
    const response = await apiClient.get('/telemetry/metrics/overview', {
      params: { timeRange },
    });
    return response.data;
  },

  async getMetricSeries(
    metricName: string,
    timeRange: string = '24h',
    interval: string = '1h'
  ): Promise<MetricSeries> {
    const response = await apiClient.get(`/telemetry/metrics/${metricName}`, {
      params: { timeRange, interval },
    });
    return response.data;
  },

  // Logs
  async getLogs(query: TelemetryQuery): Promise<PagedResult<LogEntry>> {
    const response = await apiClient.get('/telemetry/logs', {
      params: query,
    });
    return response.data;
  },

  async getLogCategories(): Promise<string[]> {
    const response = await apiClient.get('/telemetry/logs/categories');
    return response.data;
  },

  // Traces
  async getTraces(query: TelemetryQuery): Promise<PagedResult<Trace>> {
    const response = await apiClient.get('/telemetry/traces', {
      params: query,
    });
    return response.data;
  },

  async getTrace(traceId: string): Promise<Trace> {
    const response = await apiClient.get(`/telemetry/traces/${traceId}`);
    return response.data;
  },

  // Audit logs
  async getAuditLogs(query: TelemetryQuery): Promise<PagedResult<AuditLogEntry>> {
    const response = await apiClient.get('/telemetry/audit', {
      params: query,
    });
    return response.data;
  },

  async getAuditCategories(): Promise<string[]> {
    const response = await apiClient.get('/telemetry/audit/categories');
    return response.data;
  },

  // Real-time events (for live tail)
  getLogsStreamUrl(): string {
    return `${apiClient.defaults.baseURL}/telemetry/logs/stream`;
  },
};

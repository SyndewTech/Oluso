import { lazy } from 'react';
import { ChartBarIcon } from '@heroicons/react/24/outline';
import type { AxiosInstance } from 'axios';
import type { AdminUIPlugin } from '@oluso/ui-core';
import { setApiClient } from './api/telemetryApi';

// Re-export types
export type {
  TelemetryMetrics,
  MetricDataPoint,
  MetricSeries,
  LogEntry,
  TraceSpan,
  TraceEvent,
  Trace,
  AuditLogEntry,
  TelemetryQuery,
  PagedResult,
} from './api/telemetryApi';
export { telemetryApi } from './api/telemetryApi';

// Re-export components for custom usage
export { MetricCard } from './components/MetricCard';
export { SimpleBarChart, SimpleLineChart } from './components/SimpleChart';
export { LogViewer } from './components/LogViewer';
export { TraceViewer, TraceList } from './components/TraceViewer';
export { AuditLogViewer } from './components/AuditLogViewer';

// Lazy load pages for code splitting
const TelemetryDashboardPage = lazy(() => import('./pages/TelemetryDashboardPage'));
const LogsPage = lazy(() => import('./pages/LogsPage'));
const TracesPage = lazy(() => import('./pages/TracesPage'));
const AuditLogsPage = lazy(() => import('./pages/AuditLogsPage'));

/**
 * Configuration options for the Telemetry plugin
 */
export interface TelemetryPluginOptions {
  apiClient: AxiosInstance;
}

/**
 * Create the Telemetry Admin UI Plugin
 * @param options Plugin configuration options
 */
export function createTelemetryPlugin(options: TelemetryPluginOptions): AdminUIPlugin {
  return {
    id: 'oluso-telemetry',
    name: 'Oluso Telemetry',
    version: '1.0.0',
    requiredFeatures: [],

    navigation: [
      {
        id: 'telemetry',
        name: 'Telemetry',
        href: '/telemetry',
        icon: ChartBarIcon,
        order: 10,
        children: [
          {
            id: 'telemetry-dashboard',
            name: 'Dashboard',
            href: '/telemetry',
            icon: ChartBarIcon,
            order: 0,
          },
          {
            id: 'telemetry-logs',
            name: 'Logs',
            href: '/telemetry/logs',
            icon: ChartBarIcon,
            order: 1,
          },
          {
            id: 'telemetry-traces',
            name: 'Traces',
            href: '/telemetry/traces',
            icon: ChartBarIcon,
            order: 2,
          },
          {
            id: 'telemetry-audit',
            name: 'Audit Logs',
            href: '/telemetry/audit',
            icon: ChartBarIcon,
            order: 3,
          },
        ],
      },
    ],

    routes: [
      {
        path: '/telemetry',
        component: TelemetryDashboardPage,
      },
      {
        path: '/telemetry/logs',
        component: LogsPage,
      },
      {
        path: '/telemetry/traces',
        component: TracesPage,
      },
      {
        path: '/telemetry/traces/:traceId',
        component: TracesPage,
      },
      {
        path: '/telemetry/audit',
        component: AuditLogsPage,
      },
    ],

    initialize() {
      setApiClient(options.apiClient);
      console.log('Telemetry plugin initialized');
    },
  };
}

export default createTelemetryPlugin;

import { Routes } from '@angular/router';

export const TELEMETRY_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard',
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/telemetry-dashboard/telemetry-dashboard.component').then(
        (m) => m.TelemetryDashboardComponent
      ),
    data: { title: 'Telemetry Dashboard' },
  },
  {
    path: 'logs',
    loadComponent: () =>
      import('./pages/telemetry-logs/telemetry-logs.component').then(
        (m) => m.TelemetryLogsComponent
      ),
    data: { title: 'Application Logs' },
  },
  {
    path: 'traces',
    loadComponent: () =>
      import('./pages/telemetry-traces/telemetry-traces.component').then(
        (m) => m.TelemetryTracesComponent
      ),
    data: { title: 'Distributed Traces' },
  },
  {
    path: 'traces/:traceId',
    loadComponent: () =>
      import('./pages/telemetry-traces/telemetry-traces.component').then(
        (m) => m.TelemetryTracesComponent
      ),
    data: { title: 'Trace Details' },
  },
];

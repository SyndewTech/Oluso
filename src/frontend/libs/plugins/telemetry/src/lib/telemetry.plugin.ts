import { AdminPlugin, NavItem } from '@syndew/oluso-ui-core';
import { TELEMETRY_ROUTES } from './telemetry.routes';

/**
 * Telemetry Admin Plugin
 *
 * Provides system monitoring and observability functionality:
 * - Dashboard with overview metrics
 * - Application logs viewer with filtering and live tail
 * - Distributed traces explorer
 */
export const TELEMETRY_PLUGIN: AdminPlugin = {
  name: 'oluso-telemetry',
  version: '1.0.0',

  // Plugin routes - will be automatically added under /telemetry path
  routes: [
    {
      path: 'telemetry',
      children: TELEMETRY_ROUTES,
    },
  ],

  navItems: [
    {
      id: 'telemetry',
      label: 'Telemetry',
      path: '/telemetry',
      icon: 'chart-bar',
      order: 76,
      superAdminOnly: true,
      children: [
        { label: 'Dashboard', path: '/telemetry', icon: 'chart-bar', order: 0 },
        { label: 'Logs', path: '/telemetry/logs', icon: 'document-text', order: 1 },
        { label: 'Traces', path: '/telemetry/traces', icon: 'queue-list', order: 2 },
      ],
    } as NavItem,
  ],
};

/**
 * Helper function to get Telemetry navigation items
 */
export function getTelemetryNavItems(): NavItem[] {
  return TELEMETRY_PLUGIN.navItems ?? [];
}

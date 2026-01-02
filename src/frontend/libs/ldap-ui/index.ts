import { lazy } from 'react';
import { ServerIcon } from '@heroicons/react/24/outline';
import type { AxiosInstance } from 'axios';
import type { AdminUIPlugin } from '@oluso/ui-core';
import { setApiClient } from './api/ldapApi';
import LdapServerSettingsTab from './components/LdapServerSettingsTab';

// Re-export types
export type {
  LdapServerInfo,
  LdapServerSettings,
  UpdateLdapServerSettingsRequest,
  TestConnectionResponse,
} from './api/ldapApi';
export { ldapApi } from './api/ldapApi';

// Lazy load pages for code splitting
const LdapDashboardPage = lazy(() => import('./pages/LdapDashboardPage'));
const LdapServiceAccountsPage = lazy(() => import('./pages/LdapServiceAccountsPage'));

/**
 * Configuration options for the LDAP plugin
 */
export interface LdapPluginOptions {
  apiClient: AxiosInstance;
}

/**
 * Create the LDAP Admin UI Plugin
 *
 * This plugin provides management for LDAP Server - exposing tenant users
 * via the LDAP protocol for legacy application integration.
 *
 * @param options Plugin configuration options
 */
export function createLdapPlugin(options: LdapPluginOptions): AdminUIPlugin {
  return {
    id: 'oluso-ldap',
    name: 'Oluso LDAP',
    version: '1.0.0',
    requiredFeatures: [],

    navigation: [
      {
        id: 'ldap',
        name: 'LDAP Server',
        href: '/ldap',
        icon: ServerIcon,
        group: 'settings',
        order: 70,
      },
    ],

    routes: [
      {
        path: '/ldap',
        component: LdapDashboardPage,
      },
      {
        path: '/ldap/service-accounts',
        component: LdapServiceAccountsPage,
      },
    ],

    settingsTabs: [
      {
        id: 'ldap-server',
        label: 'LDAP Server',
        icon: ServerIcon,
        component: LdapServerSettingsTab,
        order: 410,
      },
    ],

    initialize() {
      setApiClient(options.apiClient);
      console.log('LDAP plugin initialized');
    },
  };
}

export default createLdapPlugin;

import { lazy } from 'react';
import {
  FingerPrintIcon,
  KeyIcon,
} from '@heroicons/react/24/outline';
import type { AxiosInstance } from 'axios';
import type { AdminUIPlugin, AccountUIPlugin } from '@oluso/ui-core';
import { setApiClient } from './api/fido2Api';

// Re-export types
export type {
  Fido2Credential,
  Fido2UserSummary,
  Fido2Stats,
  AccountPasskey,
  AccountPasskeyList,
} from './api/fido2Api';
export { fido2Api, fido2AccountApi } from './api/fido2Api';

// Lazy load pages for code splitting
const Fido2DashboardPage = lazy(() => import('./pages/Fido2DashboardPage'));
const CredentialsPage = lazy(() => import('./pages/CredentialsPage'));
const AccountPasskeysPage = lazy(() => import('./pages/account/PasskeysPage'));

// Import component directly for user detail tab (not lazy - it's small)
import { UserPasskeysTab } from './components/UserPasskeysTab';

/**
 * Configuration options for the FIDO2 plugin
 */
export interface Fido2PluginOptions {
  apiClient: AxiosInstance;
}

// ============================================================================
// Admin UI Plugin
// ============================================================================

/**
 * Create the FIDO2 Admin UI Plugin
 * @param options Plugin configuration options
 */
export function createFido2Plugin(options: Fido2PluginOptions): AdminUIPlugin {
  return {
    id: 'oluso-fido2',
    name: 'Oluso FIDO2',
    version: '1.0.0',
    requiredFeatures: [],

    navigation: [
      {
        id: 'fido2',
        name: 'FIDO2 / Passkeys',
        href: '/fido2',
        icon: FingerPrintIcon,
        group: 'settings',
        order: 70,
        children: [
          {
            id: 'fido2-dashboard',
            name: 'Dashboard',
            href: '/fido2',
            icon: FingerPrintIcon,
            order: 0,
          },
          {
            id: 'fido2-credentials',
            name: 'Credentials',
            href: '/fido2/credentials',
            icon: KeyIcon,
            order: 1,
            superAdminOnly: true,
          },
        ],
      },
    ],

    routes: [
      {
        path: '/fido2',
        component: Fido2DashboardPage,
      },
      {
        path: '/fido2/credentials',
        component: CredentialsPage,
        superAdminOnly: true,
      },
    ],

    // Add Passkeys tab to User Details page
    userDetailTabs: [
      {
        id: 'passkeys',
        label: 'Passkeys',
        icon: FingerPrintIcon,
        component: UserPasskeysTab,
        order: 450, // Between Sessions (400) and MFA (500)
      },
    ],

    initialize() {
      setApiClient(options.apiClient);
      console.log('FIDO2 Admin plugin initialized');
    },
  };
}

// ============================================================================
// Account UI Plugin (End-user self-service)
// ============================================================================

/**
 * Create the FIDO2 Account UI Plugin
 *
 * This plugin adds passkey management to the Account UI:
 * - View registered passkeys
 * - Register new passkeys
 * - Delete passkeys
 *
 * @param options Plugin configuration options
 */
export function createFido2AccountPlugin(options: Fido2PluginOptions): AccountUIPlugin {
  setApiClient(options.apiClient);

  return {
    id: 'oluso-fido2-account',
    name: 'Passkeys',

    navigation: [
      {
        label: 'Passkeys',
        path: '/security/passkeys',
        icon: FingerPrintIcon,
        group: 'settings',
      },
    ],

    routes: [
      {
        path: '/security/passkeys',
        component: AccountPasskeysPage,
      },
    ],
  };
}

export default createFido2Plugin;

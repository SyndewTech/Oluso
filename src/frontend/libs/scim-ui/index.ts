import { lazy } from 'react';
import { CloudArrowUpIcon } from '@heroicons/react/24/outline';
import type { AxiosInstance } from 'axios';
import type { AdminUIPlugin } from '@oluso/ui-core';
import { setApiClient, setServerBaseUrl } from './api/scimApi';

// Re-export types
export type {
  ScimClient,
  ScimProvisioningLog,
  CreateScimClientRequest,
  ScimAttributeMapping,
  CreateScimAttributeMappingRequest,
  ScimAttributeSuggestion,
  InternalPropertyOption,
} from './api/scimApi';
export { scimApi, getScimEndpointUrl, getServerBaseUrl } from './api/scimApi';

// Lazy load pages for code splitting
const ScimClientsPage = lazy(() => import('./pages/ScimClientsPage'));
const ScimClientDetailPage = lazy(() => import('./pages/ScimClientDetailPage'));
const ScimMappingsPage = lazy(() => import('./pages/ScimMappingsPage'));

/**
 * Configuration options for the SCIM plugin
 */
export interface ScimPluginOptions {
  apiClient: AxiosInstance;
  /** The base URL of the identity server (e.g., https://auth.example.com) */
  serverBaseUrl: string;
}

/**
 * Create the SCIM Admin UI Plugin
 * @param options Plugin configuration options
 */
export function createScimPlugin(options: ScimPluginOptions): AdminUIPlugin {
  return {
    id: 'oluso-scim',
    name: 'Oluso SCIM',
    version: '1.0.0',
    requiredFeatures: [],

    navigation: [
      {
        id: 'scim',
        name: 'SCIM Provisioning',
        href: '/scim/clients',
        icon: CloudArrowUpIcon,
        group: 'settings',
        order: 75,
        children: [
          {
            id: 'scim-clients',
            name: 'Clients',
            href: '/scim/clients',
            icon: CloudArrowUpIcon,
            order: 0,
          },
        ],
      },
    ],

    routes: [
      {
        path: '/scim/clients',
        component: ScimClientsPage,
      },
      {
        path: '/scim/clients/:id',
        component: ScimClientDetailPage,
      },
      {
        path: '/scim/clients/:clientId/mappings',
        component: ScimMappingsPage,
      },
    ],

    initialize() {
      setApiClient(options.apiClient);
      setServerBaseUrl(options.serverBaseUrl);
      console.log('SCIM plugin initialized');
    },
  };
}

export default createScimPlugin;

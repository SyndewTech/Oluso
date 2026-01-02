import { lazy } from 'react';
import {
  ShieldCheckIcon,
  BuildingOfficeIcon,
} from '@heroicons/react/24/outline';
import type { AxiosInstance } from 'axios';
import type { AdminUIPlugin } from '@oluso/ui-core';
import { setApiClient } from './api/samlApi';
import SamlIdpSettingsTab from './components/SamlIdpSettingsTab';

// Re-export types
export type {
  SamlServiceProvider,
  SamlServiceProviderEdit,
  IdpInfo,
  TestResult,
  SamlIdpConfiguration,
  UpdateSamlIdpConfigurationRequest,
  SamlCertificateInfo,
  UploadCertificateRequest,
} from './api/samlApi';
export { samlApi } from './api/samlApi';

// Lazy load pages for code splitting
const SamlDashboardPage = lazy(() => import('./pages/SamlDashboardPage'));
const ServiceProvidersPage = lazy(() => import('./pages/ServiceProvidersPage'));
const ServiceProviderDetailsPage = lazy(() => import('./pages/ServiceProviderDetailsPage'));

/**
 * Configuration options for the SAML plugin
 */
export interface SamlPluginOptions {
  apiClient: AxiosInstance;
}

/**
 * Create the SAML Admin UI Plugin
 *
 * This plugin provides management for SAML Service Providers - applications
 * that use this system as their SAML Identity Provider.
 *
 * @param options Plugin configuration options
 */
export function createSamlPlugin(options: SamlPluginOptions): AdminUIPlugin {
  return {
    id: 'oluso-saml',
    name: 'Oluso SAML',
    version: '1.0.0',
    requiredFeatures: [],

    navigation: [
      {
        id: 'saml',
        name: 'SAML IdP',
        href: '/saml',
        icon: ShieldCheckIcon,
        group: 'settings',
        order: 60,
        children: [
          {
            id: 'saml-dashboard',
            name: 'Dashboard',
            href: '/saml',
            icon: ShieldCheckIcon,
            order: 0,
          },
          {
            id: 'saml-service-providers',
            name: 'Service Providers',
            href: '/saml/service-providers',
            icon: BuildingOfficeIcon,
            order: 1,
          },
        ],
      },
    ],

    routes: [
      {
        path: '/saml',
        component: SamlDashboardPage,
      },
      {
        path: '/saml/service-providers',
        component: ServiceProvidersPage,
      },
      {
        path: '/saml/service-providers/new',
        component: ServiceProviderDetailsPage,
      },
      {
        path: '/saml/service-providers/:id',
        component: ServiceProviderDetailsPage,
      },
    ],

    settingsTabs: [
      {
        id: 'saml-idp',
        label: 'SAML IdP',
        icon: ShieldCheckIcon,
        component: SamlIdpSettingsTab,
        order: 400,
      },
    ],

    initialize() {
      setApiClient(options.apiClient);
      console.log('SAML plugin initialized');
    },
  };
}

export default createSamlPlugin;

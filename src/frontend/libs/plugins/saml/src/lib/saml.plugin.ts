import { AdminPlugin, NavItem } from '@syndew/oluso-ui-core';
import { SamlIdpSettingsTabComponent } from './components/saml-idp-settings-tab/saml-idp-settings-tab.component';
import { SAML_ROUTES } from './saml.routes';

/**
 * SAML Admin Plugin
 *
 * Provides SAML Identity Provider management functionality:
 * - Dashboard with IdP info and stats
 * - Service Provider management (CRUD)
 * - Settings tab for IdP configuration
 */
export const SAML_PLUGIN: AdminPlugin = {
  name: 'oluso-saml',
  version: '1.0.0',

  // Plugin routes - will be automatically added under /saml path
  routes: [
    {
      path: 'saml',
      children: SAML_ROUTES,
    },
  ],

  navItems: [
    {
      id: 'saml',
      label: 'SAML',
      path: '/saml',
      icon: 'shield-check',
      order: 10,
      mergeIntoPath: '/identity-providers',
      children: [
        { label: 'SAML Dashboard', path: '/saml/dashboard', icon: 'shield-check', permission: 'saml.read', order: 10 },
        { label: 'Service Providers', path: '/saml/service-providers', icon: 'shield-check', permission: 'saml.read', order: 20 },
      ],
    } as NavItem,
  ],

  settingsTabs: [
    {
      id: 'saml-idp',
      label: 'SAML IdP',
      icon: 'shield-check',
      component: SamlIdpSettingsTabComponent,
      order: 400,
    },
  ],
};

/**
 * Helper function to get SAML navigation items
 */
export function getSamlNavItems(): NavItem[] {
  return SAML_PLUGIN.navItems ?? [];
}

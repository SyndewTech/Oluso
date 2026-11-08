import { AdminPlugin, NavItem } from '@oluso/ui-core';
import { LdapServerSettingsTabComponent } from './components/ldap-server-settings-tab/ldap-server-settings-tab.component';
import { LDAP_ROUTES } from './ldap.routes';

/**
 * LDAP Admin Plugin
 *
 * Provides LDAP Server management functionality:
 * - Dashboard with server info and status
 * - Service account management (CRUD)
 * - Settings tab for LDAP server configuration
 */
export const LDAP_PLUGIN: AdminPlugin = {
  name: 'oluso-ldap',
  version: '1.0.0',

  // Plugin routes - will be automatically added under /ldap path
  routes: [
    {
      path: 'ldap',
      children: LDAP_ROUTES,
    },
  ],

  navItems: [
    {
      id: 'ldap',
      label: 'LDAP Server',
      path: '/ldap',
      icon: 'server',
      order: 20,
      permission: 'ldap.read',
      mergeIntoPath: '/identity-providers',
      children: [
        { label: 'Dashboard', path: '/ldap/dashboard', icon: 'home', order: 0 },
        { label: 'Service Accounts', path: '/ldap/service-accounts', icon: 'key', order: 10 },
      ],
    } as NavItem,
  ],

  settingsTabs: [
    {
      id: 'ldap-server',
      label: 'LDAP Server',
      icon: 'server',
      component: LdapServerSettingsTabComponent,
      order: 410,
    },
  ],
};

/**
 * Helper function to get LDAP navigation items
 */
export function getLdapNavItems(): NavItem[] {
  return LDAP_PLUGIN.navItems ?? [];
}

import { AdminPlugin, NavItem } from '@syndew/oluso-ui-core';
import { SCIM_ROUTES } from './scim.routes';

/**
 * SCIM Admin Plugin
 *
 * Provides SCIM provisioning management functionality:
 * - SCIM client management (CRUD): identity providers that push into us
 * - Token management and rotation
 * - Attribute mapping configuration
 * - Provisioning activity logs
 * - SCIM providers: downstream systems we push users and groups to
 */
export const SCIM_PLUGIN: AdminPlugin = {
  name: 'oluso-scim',
  version: '1.0.0',

  // Plugin routes - will be automatically added under /scim path
  routes: [
    {
      path: 'scim',
      children: SCIM_ROUTES,
    },
  ],

  navItems: [
    {
      id: 'scim',
      label: 'SCIM Provisioning',
      path: '/scim',
      icon: 'cloud-upload',
      order: 30,
      permission: 'scim.read',
      mergeIntoPath: '/identity-providers',
      children: [
        { label: 'Clients', path: '/scim/clients', icon: 'cloud-upload', order: 0 },
        { label: 'Providers', path: '/scim/providers', icon: 'cloud-upload', order: 1 },
      ],
    } as NavItem,
  ],
};

/**
 * Helper function to get SCIM navigation items
 */
export function getScimNavItems(): NavItem[] {
  return SCIM_PLUGIN.navItems ?? [];
}

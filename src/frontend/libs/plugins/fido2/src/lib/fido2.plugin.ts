import { AdminPlugin, AccountPlugin, NavItem } from '@syndew/oluso-ui-core';
import { UserPasskeysTabComponent } from './components/user-passkeys-tab/user-passkeys-tab.component';
import { PasskeysPageComponent } from './pages/account/passkeys-page.component';
import { FIDO2_ROUTES } from './fido2.routes';

/**
 * FIDO2 Admin Plugin
 *
 * Provides FIDO2/WebAuthn/Passkey management functionality:
 * - Dashboard with statistics
 * - User credential management
 * - Passkeys tab on user detail page
 */
export const FIDO2_PLUGIN: AdminPlugin = {
  name: 'oluso-fido2',
  version: '1.0.0',

  // Plugin routes - will be automatically added under /fido2 path
  routes: [
    {
      path: 'fido2',
      children: FIDO2_ROUTES,
    },
  ],

  // Merge passkeys nav item under User Management
  navItems: [
    {
      id: 'fido2',
      label: 'Passkeys',
      path: '/fido2',
      icon: 'fingerprint',
      mergeIntoPath: '/users', // Merge under User Management
      permission: 'fido2.read',
      order: 20,
      children: [
        { label: 'Overview', path: '/fido2/dashboard', icon: 'home', order: 0 },
        { label: 'Credentials', path: '/fido2/credentials', icon: 'fingerprint', order: 10 },
      ],
    } as NavItem,
  ],

  userDetailTabs: [
    {
      id: 'passkeys',
      label: 'Passkeys',
      icon: 'fingerprint',
      component: UserPasskeysTabComponent,
      order: 45, // Between MFA (40) and Claims (50)
    },
  ],
};

/**
 * FIDO2 Account Plugin
 *
 * Provides passkey self-service functionality for end users:
 * - View registered passkeys
 * - Register new passkeys
 * - Rename passkeys
 * - Delete passkeys
 */
export const FIDO2_ACCOUNT_PLUGIN: AccountPlugin = {
  name: 'oluso-fido2-account',
  version: '1.0.0',

  // Route for passkeys management page
  routes: [
    {
      path: 'security/passkeys',
      component: PasskeysPageComponent,
    },
  ],

  // Security tab for passkeys (optional - page can also be accessed via route)
  securityTabs: [
    {
      id: 'passkeys',
      label: 'Passkeys',
      icon: 'fingerprint',
      component: PasskeysPageComponent,
      order: 30,
    },
  ],
};

/**
 * Helper function to get FIDO2 navigation items
 */
export function getFido2NavItems(): NavItem[] {
  return FIDO2_PLUGIN.navItems ?? [];
}

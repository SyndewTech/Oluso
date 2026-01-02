// Re-export plugin types from shared package
export type {
  AccountUIPlugin,
  AccountNavItem,
  AccountRoute,
  AccountPluginOptions,
} from '@oluso/ui-core';

// For backwards compatibility
export type { AccountNavItem as AccountNavigationItem } from '@oluso/ui-core';

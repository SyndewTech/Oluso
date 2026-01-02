// ============ Main App Component ============
export { default as AccountApp } from './App';

// ============ Layout Components ============
export { AccountLayout } from './components/layout/AccountLayout';

// ============ Page Components ============
export { ProfilePage } from './pages/ProfilePage';
export { SecurityPage } from './pages/SecurityPage';
export { SessionsPage } from './pages/SessionsPage';
export { ConnectedAppsPage } from './pages/ConnectedAppsPage';
export { TenantSelectorPage } from './pages/TenantSelectorPage';

// ============ Contexts ============
export { AuthProvider, useAuth, apiBaseUrl } from './contexts/AuthContext';
export { TenantProvider, useTenant } from './contexts/TenantContext';

// ============ Services ============
export { apiClient, setApiBaseUrl } from './services/api';

// ============ Config ============
export { oidcConfig, type OidcConfig, createOidcConfig } from './config/oidc';

// ============ Types ============
export * from './types/plugin';

// Re-export ui-core types for convenience
export type {
  AccountUIPlugin,
  AccountNavItem,
  AccountRoute,
} from '@oluso/ui-core';

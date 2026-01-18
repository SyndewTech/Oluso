// Main App
export { default as WorkspaceApp } from './App';

// Layout
export { WorkspaceLayout } from './components/layout/WorkspaceLayout';

// Contexts & Hooks
export { AuthProvider, useAuth } from './contexts/AuthContext';
export { useAuthStore, useUser, useIsAuthenticated, useIsManager, usePermissions } from './store/slices/authSlice';

// Services
export { apiClient, setApiBaseUrl, getApiBaseUrl, setAuthToken, setTenantId } from './services/api';

// Config
export { serverUrl, apiUrl, oidcConfig, createOidcConfig } from './config';

// Pages (for custom routing)
export { default as DashboardPage } from './pages/DashboardPage';
export { default as ProfilePage } from './pages/ProfilePage';

// Re-export types from ui-core for convenience
export type {
  WorkspaceUIPlugin,
  WorkspaceNavItem,
  WorkspaceRoute,
  WorkspaceDashboardWidget,
  WorkspaceQuickAction,
  WorkspaceUser,
  WorkspaceWidgetProps,
  WorkspacePluginOptions,
  WorkspaceFilterOptions,
  ApprovalRequestType,
} from '@oluso/ui-core';

export { defineWorkspacePlugin } from '@oluso/ui-core';

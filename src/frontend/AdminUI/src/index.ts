// ============ Main App Component ============
export { default as AdminApp } from './App';

// ============ Layout Components ============
export { default as MainLayout } from './components/layout/MainLayout';

// ============ Page Components ============
export { default as Dashboard } from './pages/Dashboard';
export { default as ClientsPage } from './pages/ClientsPage';
export { default as ClientDetailsPage } from './pages/ClientDetailsPage';
export { default as ApiResourcesPage } from './pages/ApiResourcesPage';
export { default as ApiResourceDetailsPage } from './pages/ApiResourceDetailsPage';
export { default as ApiScopesPage } from './pages/ApiScopesPage';
export { default as ApiScopeDetailsPage } from './pages/ApiScopeDetailsPage';
export { default as IdentityResourcesPage } from './pages/IdentityResourcesPage';
export { default as IdentityProvidersPage } from './pages/IdentityProvidersPage';
export { default as IdentityProviderDetailsPage } from './pages/IdentityProviderDetailsPage';
export { default as UsersPage } from './pages/UsersPage';
export { default as UserDetailsPage } from './pages/UserDetailsPage';
export { default as RolesPage } from './pages/RolesPage';
export { default as GrantsPage } from './pages/GrantsPage';
export { default as AuditLogsPage } from './pages/AuditLogsPage';
export { default as SettingsPage } from './pages/SettingsPage';
export { default as LoginPage } from './pages/LoginPage';
export { default as JourneysPage } from './pages/JourneysPage';
export { default as JourneyBuilderPage } from './pages/JourneyBuilderPage';
export { default as PluginsPage } from './pages/PluginsPage';
export { default as SigningKeysPage } from './pages/SigningKeysPage';
export { default as TenantsPage } from './pages/TenantsPage';
export { default as TenantSettingsPage } from './pages/TenantSettingsPage';
export { default as WebhooksPage } from './pages/WebhooksPage';

// ============ Common Components ============
export { Badge } from './components/common/Badge';
export { default as Button } from './components/common/Button';
export { Card, CardHeader, CardContent, CardFooter } from './components/common/Card';
export { FeatureGate, LimitGate, UsageBar } from './components/common/FeatureGate';
export { default as Input } from './components/common/Input';
export { default as Modal } from './components/common/Modal';
export { default as SchemaFormRenderer } from './components/common/SchemaFormRenderer';
export { default as SecretInput } from './components/common/SecretInput';
export { Table } from './components/common/Table';
export { default as SlotRenderer } from './components/common/SlotRenderer';

// ============ Journey Components ============
export { ConditionBuilder, SimpleConditionBuilder, MappingBuilder } from './components/journey';
export type { FieldDefinition, ClaimMapping, TransformMapping } from './components/journey';

// ============ Contexts ============
export { TenantFeaturesProvider, useTenantFeatures, useFeature, useLimit } from './contexts/TenantFeaturesContext';
export {
  PluginProvider,
  usePlugins,
  useNavigation,
  useRoutes,
  useUserDetailTabs,
  usePageSlots,
  useDashboardWidgets,
  useTenantSettingsTabs,
  useSettingsTabs,
  useActionButtons,
  useDetailSections,
} from './contexts/PluginContext';
// Backward compatibility aliases
export { useNavigation as usePluginNavigation, useRoutes as usePluginRoutes } from './contexts/PluginContext';

// ============ Hooks ============
export { useEnumSource } from './hooks/useEnumSource';

// ============ Services ============
export { apiClient, setApiBaseUrl } from './services/api';

// ============ Store ============
export { useAuthStore } from './store/slices/authSlice';

// Re-export core types for convenience
export type {
  AdminUIPlugin,
  AdminNavItem,
  AdminRoute,
  FilterOptions,
  UserDetailTab,
  PageSlot,
  DashboardWidget,
} from '@oluso/ui-core';

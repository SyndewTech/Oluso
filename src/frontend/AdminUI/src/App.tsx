import { Suspense, useMemo } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { useAuthStore } from './store/slices/authSlice';
import { TenantFeaturesProvider, useTenantFeatures } from './contexts/TenantFeaturesContext';
import { PluginProvider, usePlugins, type AdminUIPlugin } from './contexts/PluginContext';
import MainLayout from './components/layout/MainLayout';
import Dashboard from './pages/Dashboard';
import ClientsPage from './pages/ClientsPage';
import ClientDetailsPage from './pages/ClientDetailsPage';
import ApiResourcesPage from './pages/ApiResourcesPage';
import ApiResourceDetailsPage from './pages/ApiResourceDetailsPage';
import ApiScopesPage from './pages/ApiScopesPage';
import ApiScopeDetailsPage from './pages/ApiScopeDetailsPage';
import IdentityResourcesPage from './pages/IdentityResourcesPage';
import IdentityProvidersPage from './pages/IdentityProvidersPage';
import IdentityProviderDetailsPage from './pages/IdentityProviderDetailsPage';
import UsersPage from './pages/UsersPage';
import UserDetailsPage from './pages/UserDetailsPage';
import RolesPage from './pages/RolesPage';
import GrantsPage from './pages/GrantsPage';
import AuditLogsPage from './pages/AuditLogsPage';
import SettingsPage from './pages/SettingsPage';
import LoginPage from './pages/LoginPage';
import JourneysPage from './pages/JourneysPage';
import JourneyBuilderPage from './pages/JourneyBuilderPage';
import PluginsPage from './pages/PluginsPage';
import SigningKeysPage from './pages/SigningKeysPage';
import TenantsPage from './pages/TenantsPage';
import TenantSettingsPage from './pages/TenantSettingsPage';
import WebhooksPage from './pages/WebhooksPage';
import SubmissionsPage from './pages/SubmissionsPage';

interface AppProps {
  plugins?: AdminUIPlugin[];
}

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const hasHydrated = useAuthStore((state) => state.hasHydrated);

  // Wait for auth state to be restored from localStorage
  if (!hasHydrated) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="text-gray-500">Loading...</div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

/** Loading fallback for lazy-loaded plugin pages */
function PluginPageLoader() {
  return (
    <div className="flex items-center justify-center h-64">
      <div className="text-gray-500">Loading...</div>
    </div>
  );
}

/** Main app routes including plugin routes - must be inside all providers */
function AppRoutes() {
  const { getRoutes } = usePlugins();
  const { hasFeature } = useTenantFeatures();
  const user = useAuthStore((state) => state.user);
  const isSuperAdmin = user?.roles?.includes('SuperAdmin') || false;

  // Get plugin routes with proper filtering
  const pluginRoutes = useMemo(() => {
    return getRoutes({ isSuperAdmin, hasFeature });
  }, [getRoutes, isSuperAdmin, hasFeature]);

  return (
    <Routes>
      {/* Core routes */}
      <Route path="/" element={<Dashboard />} />
      <Route path="/clients" element={<ClientsPage />} />
      <Route path="/clients/:id" element={<ClientDetailsPage />} />
      <Route path="/api-resources" element={<ApiResourcesPage />} />
      <Route path="/api-resources/:id" element={<ApiResourceDetailsPage />} />
      <Route path="/api-scopes" element={<ApiScopesPage />} />
      <Route path="/api-scopes/:id" element={<ApiScopeDetailsPage />} />
      <Route path="/identity-resources" element={<IdentityResourcesPage />} />
      <Route path="/identity-providers" element={<IdentityProvidersPage />} />
      <Route path="/identity-providers/:id" element={<IdentityProviderDetailsPage />} />
      <Route path="/users" element={<UsersPage />} />
      <Route path="/users/:id" element={<UserDetailsPage />} />
      <Route path="/roles" element={<RolesPage />} />
      <Route path="/grants" element={<GrantsPage />} />
      <Route path="/journeys" element={<JourneysPage />} />
      <Route path="/journeys/:policyId" element={<JourneyBuilderPage />} />
      <Route path="/submissions" element={<SubmissionsPage />} />
      <Route path="/plugins" element={<PluginsPage />} />
      <Route path="/signing-keys" element={<SigningKeysPage />} />
      {isSuperAdmin && (
        <>
          <Route path="/tenants" element={<TenantsPage />} />
          <Route path="/tenants/:tenantId" element={<TenantSettingsPage />} />
          <Route path="/tenants/:tenantId/settings" element={<TenantSettingsPage />} />
        </>
      )}
      <Route path="/webhooks" element={<WebhooksPage />} />
      <Route path="/audit-logs" element={<AuditLogsPage />} />
      <Route path="/settings" element={<SettingsPage />} />
      {/* Plugin routes - rendered inline */}
      {pluginRoutes.map((route) => (
        <Route
          key={route.path}
          path={route.path}
          element={
            <Suspense fallback={<PluginPageLoader />}>
              <route.component />
            </Suspense>
          }
        />
      ))}
    </Routes>
  );
}

function App({ plugins = [] }: AppProps) {
  return (
    <PluginProvider plugins={plugins}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          path="/*"
          element={
            <ProtectedRoute>
              <TenantFeaturesProvider>
                <MainLayout>
                  <AppRoutes />
                </MainLayout>
              </TenantFeaturesProvider>
            </ProtectedRoute>
          }
        />
      </Routes>
    </PluginProvider>
  );
}

export default App;

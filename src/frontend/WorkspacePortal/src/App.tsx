import { Suspense, lazy, useMemo } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider as OidcAuthProvider } from 'react-oidc-context';
import type { WorkspaceUIPlugin } from '@oluso/ui-core';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { WorkspaceLayout } from './components/layout/WorkspaceLayout';
import { oidcConfig } from './config';

// Lazy loaded pages
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const ProfilePage = lazy(() => import('./pages/ProfilePage'));

// Loading fallback
function LoadingSpinner() {
  return (
    <div className="flex items-center justify-center h-64">
      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
    </div>
  );
}

// Login page (shown when not authenticated)
function LoginPage() {
  const { login, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <LoadingSpinner />
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="max-w-md w-full space-y-8 p-8">
        <div className="text-center">
          <h1 className="text-3xl font-bold text-gray-900">Workspace Portal</h1>
          <p className="mt-2 text-gray-600">Sign in to access your workspace</p>
        </div>
        <button
          onClick={login}
          className="w-full flex justify-center py-3 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500"
        >
          Sign in with SSO
        </button>
      </div>
    </div>
  );
}

// OAuth callback handler
function CallbackPage() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="text-center">
        <LoadingSpinner />
        <p className="mt-4 text-gray-600">Completing sign in...</p>
      </div>
    </div>
  );
}

// Protected route wrapper
function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <LoadingSpinner />
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

// Route renderer for plugin routes
interface AppRoutesProps {
  plugins: WorkspaceUIPlugin[];
}

function AppRoutes({ plugins }: AppRoutesProps) {
  const { isManager, hasPermission, hasAnyPermission } = useAuth();

  // Stub feature check
  const hasFeature = (_: string) => true;

  // Collect all routes from plugins
  const pluginRoutes = useMemo(() => {
    return plugins
      .flatMap((p) => p.routes || [])
      .filter((route) => {
        if (route.managerOnly && !isManager) return false;
        if (route.permission && !hasPermission(route.permission)) return false;
        if (route.anyPermission && !hasAnyPermission(route.anyPermission)) return false;
        if (route.feature && !hasFeature(route.feature)) return false;
        return true;
      });
  }, [plugins, isManager, hasPermission, hasAnyPermission]);

  return (
    <Suspense fallback={<LoadingSpinner />}>
      <Routes>
        {/* Default routes */}
        <Route path="/" element={<DashboardPage plugins={plugins} />} />
        <Route path="/profile" element={<ProfilePage />} />

        {/* Plugin routes */}
        {pluginRoutes.map((route) => (
          <Route
            key={route.path}
            path={route.path}
            element={<route.component />}
          />
        ))}

        {/* Fallback */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Suspense>
  );
}

interface WorkspaceAppProps {
  plugins?: WorkspaceUIPlugin[];
  oidcConfigOverrides?: Partial<typeof oidcConfig>;
}

export default function WorkspaceApp({ plugins = [], oidcConfigOverrides }: WorkspaceAppProps) {
  const mergedOidcConfig = { ...oidcConfig, ...oidcConfigOverrides };

  // Initialize plugins
  useMemo(() => {
    plugins.forEach((plugin) => {
      if (plugin.initialize) {
        plugin.initialize();
      }
    });
  }, [plugins]);

  return (
    <OidcAuthProvider {...mergedOidcConfig}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/callback" element={<CallbackPage />} />
          <Route
            path="/*"
            element={
              <ProtectedRoute>
                <WorkspaceLayout plugins={plugins}>
                  <AppRoutes plugins={plugins} />
                </WorkspaceLayout>
              </ProtectedRoute>
            }
          />
        </Routes>
      </AuthProvider>
    </OidcAuthProvider>
  );
}

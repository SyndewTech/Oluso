import { Suspense } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { AccountLayout } from './components/layout/AccountLayout';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { TenantProvider } from './contexts/TenantContext';
import { AccountUIPlugin } from './types/plugin';

// Pages
import { ProfilePage } from './pages/ProfilePage';
import { SecurityPage } from './pages/SecurityPage';
import { TwoFactorPage } from './pages/TwoFactorPage';
import { VerifyPage } from './pages/VerifyPage';
import { SessionsPage } from './pages/SessionsPage';
import { ConnectedAppsPage } from './pages/ConnectedAppsPage';
import { TenantSelectorPage } from './pages/TenantSelectorPage';
import { CibaRequestsPage } from './pages/CibaRequestsPage';

// Loading component for lazy-loaded routes
const RouteLoading = () => (
  <div className="flex items-center justify-center h-64">
    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
  </div>
);

interface AppProps {
  plugins?: AccountUIPlugin[];
}

function AppRoutes({ plugins = [] }: AppProps) {
  const { isAuthenticated, isLoading, error, login, clearError } = useAuth();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600" />
      </div>
    );
  }

  // Show error state if authentication failed
  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-gray-50">
        <div className="max-w-md w-full bg-white shadow-lg rounded-lg p-8 text-center">
          <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-red-100 mb-4">
            <svg className="h-6 w-6 text-red-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
          </div>
          <h2 className="text-xl font-semibold text-gray-900 mb-2">
            {error.message}
          </h2>
          {error.details && (
            <p className="text-sm text-gray-500 mb-6">
              {error.details}
            </p>
          )}
          <button
            onClick={() => {
              clearError();
              login();
            }}
            className="w-full inline-flex justify-center items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            Try Again
          </button>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    // AuthContext handles automatic redirect to login
    // Show a loading state while redirecting
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600 mx-auto mb-4" />
          <p className="text-gray-600">Redirecting to login...</p>
        </div>
      </div>
    );
  }

  // Collect plugin routes
  const pluginRoutes = plugins.flatMap(plugin => plugin.routes || []);

  return (
    <AccountLayout plugins={plugins}>
      <Routes>
        {/* Core account pages */}
        <Route path="/" element={<Navigate to="/profile" replace />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route path="/security" element={<SecurityPage />} />
        <Route path="/security/two-factor" element={<TwoFactorPage />} />
        <Route path="/security/verify" element={<VerifyPage />} />
        <Route path="/sessions" element={<SessionsPage />} />
        <Route path="/connected-apps" element={<ConnectedAppsPage />} />
        <Route path="/authentication-requests" element={<CibaRequestsPage />} />
        <Route path="/organizations" element={<TenantSelectorPage />} />

        {/* Callback route for OAuth redirect - handled by oidc-client-ts */}
        <Route path="/callback" element={<Navigate to="/profile" replace />} />

        {/* Plugin routes - wrapped in Suspense for lazy-loaded components */}
        {pluginRoutes.map((route) => (
          <Route
            key={route.path}
            path={route.path}
            element={
              <Suspense fallback={<RouteLoading />}>
                <route.component />
              </Suspense>
            }
          />
        ))}

        {/* Fallback */}
        <Route path="*" element={<Navigate to="/profile" replace />} />
      </Routes>
    </AccountLayout>
  );
}

export default function App({ plugins = [] }: AppProps) {
  return (
    <AuthProvider>
      <TenantProvider>
        <AppRoutes plugins={plugins} />
      </TenantProvider>
    </AuthProvider>
  );
}

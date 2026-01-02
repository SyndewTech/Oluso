import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '../store/slices/authSlice';
import Button from '../components/common/Button';
import Input from '../components/common/Input';
import api from '../services/api';

interface TenantOption {
  identifier: string;
  displayName: string | null;
}

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const { login } = useAuthStore();

  // Multi-tenant account selection
  const [availableTenants, setAvailableTenants] = useState<TenantOption[]>([]);
  const [selectedTenant, setSelectedTenant] = useState<string>('');
  const [showTenantSelector, setShowTenantSelector] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      // If tenant is selected, use username@tenant format
      const loginUsername = selectedTenant
        ? `${username}@${selectedTenant}`
        : username;

      const response = await api.post('/auth/login', {
        username: loginUsername,
        password
      });

      // For SuperAdmins without a tenant, default to 'default' tenant for API operations
      const tenantId = response.data.user.tenantId || selectedTenant || 'default';
      login(response.data.user, response.data.accessToken, tenantId);
      navigate('/');
    } catch (err: any) {
      const data = err.response?.data;

      // Handle multiple accounts scenario
      if (data?.code === 'MULTIPLE_ACCOUNTS' && data?.tenants) {
        setAvailableTenants(data.tenants);
        setShowTenantSelector(true);
        setError('Multiple accounts found. Please select your organization.');
      } else {
        setError(data?.message || 'Invalid username or password');
        // Reset tenant selector if there was an error
        setShowTenantSelector(false);
        setAvailableTenants([]);
        setSelectedTenant('');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleTenantSelect = (tenantIdentifier: string) => {
    setSelectedTenant(tenantIdentifier);
    setError('');
  };

  const handleBackToUsername = () => {
    setShowTenantSelector(false);
    setAvailableTenants([]);
    setSelectedTenant('');
    setError('');
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-100 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-sm w-full">
        {/* Logo / Brand */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-primary-600 mb-4">
            <svg className="w-6 h-6 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
            </svg>
          </div>
          <h1 className="text-2xl font-semibold text-gray-900">Identity Admin</h1>
          <p className="mt-1 text-sm text-gray-500">
            {showTenantSelector ? 'Select your organization' : 'Sign in to your account'}
          </p>
        </div>

        {/* Login Card */}
        <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
          <form onSubmit={handleSubmit} className="space-y-5">
            {error && (
              <div className={showTenantSelector ? "alert-info" : "alert-error"}>
                {error}
              </div>
            )}

            {!showTenantSelector ? (
              // Normal login form
              <div className="space-y-4">
                <Input
                  label="Username or Email"
                  type="text"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  required
                  autoComplete="username"
                  placeholder="admin@localhost"
                />
                <Input
                  label="Password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  autoComplete="current-password"
                  placeholder="••••••••"
                />
              </div>
            ) : (
              // Tenant selector
              <div className="space-y-4">
                <div className="text-sm text-gray-600 mb-2">
                  Signing in as: <span className="font-medium">{username}</span>
                </div>

                <div className="space-y-2">
                  {availableTenants.map((tenant) => (
                    <label
                      key={tenant.identifier}
                      className={`
                        flex items-center p-3 border rounded-lg cursor-pointer transition-colors
                        ${selectedTenant === tenant.identifier
                          ? 'border-primary-500 bg-primary-50'
                          : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'
                        }
                      `}
                    >
                      <input
                        type="radio"
                        name="tenant"
                        value={tenant.identifier}
                        checked={selectedTenant === tenant.identifier}
                        onChange={() => handleTenantSelect(tenant.identifier)}
                        className="h-4 w-4 text-primary-600 focus:ring-primary-500"
                      />
                      <div className="ml-3">
                        <div className="text-sm font-medium text-gray-900">
                          {tenant.displayName || tenant.identifier}
                        </div>
                        {tenant.displayName && (
                          <div className="text-xs text-gray-500">
                            {tenant.identifier}
                          </div>
                        )}
                      </div>
                    </label>
                  ))}
                </div>

                <button
                  type="button"
                  onClick={handleBackToUsername}
                  className="text-sm text-primary-600 hover:text-primary-700"
                >
                  ← Use a different account
                </button>
              </div>
            )}

            <Button
              type="submit"
              className="w-full"
              loading={loading}
              disabled={showTenantSelector && !selectedTenant}
            >
              {showTenantSelector ? 'Continue' : 'Sign in'}
            </Button>
          </form>
        </div>

        {/* Footer */}
        <p className="mt-6 text-center text-xs text-gray-500">
          Identity Server Administration Console
        </p>

        {/* Hint for username@tenant format */}
        {!showTenantSelector && (
          <p className="mt-2 text-center text-xs text-gray-400">
            Tip: Use username@tenant to sign in directly
          </p>
        )}
      </div>
    </div>
  );
}

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { formatDistanceToNow } from 'date-fns';
import { LinkIcon, TrashIcon, UserCircleIcon } from '@heroicons/react/24/outline';
import { apiClient } from '../services/api';

interface ConnectedApp {
  clientId: string;
  clientName: string;
  clientUri?: string;
  logoUri?: string;
  description?: string;
  firstConnectedAt: string;
  lastUsedAt: string;
  scopes: string[];
  hasActiveTokens: boolean;
}

interface ConnectedAppList {
  apps: ConnectedApp[];
  totalCount: number;
}

interface ExternalLogin {
  provider: string;
  providerKey: string;
  displayName: string;
}

export function ConnectedAppsPage() {
  const queryClient = useQueryClient();
  const [revoking, setRevoking] = useState<string | null>(null);
  const [unlinkingProvider, setUnlinkingProvider] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ['connected-apps'],
    queryFn: async () => {
      const response = await apiClient.get<ConnectedAppList>('/api/account/connected-apps');
      return response.data;
    },
  });

  const { data: externalLogins, isLoading: loadingExternalLogins } = useQuery({
    queryKey: ['external-logins'],
    queryFn: async () => {
      const response = await apiClient.get<ExternalLogin[]>('/api/account/security/external-logins');
      return response.data;
    },
  });

  const revokeMutation = useMutation({
    mutationFn: async (clientId: string) => {
      await apiClient.delete(`/api/account/connected-apps/${clientId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connected-apps'] });
      toast.success('Access revoked');
      setRevoking(null);
    },
    onError: () => {
      toast.error('Failed to revoke access');
      setRevoking(null);
    },
  });

  const unlinkMutation = useMutation({
    mutationFn: async ({ provider, providerKey }: { provider: string; providerKey: string }) => {
      await apiClient.delete(`/api/account/security/external-logins/${provider}?providerKey=${encodeURIComponent(providerKey)}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['external-logins'] });
      toast.success('Account unlinked');
      setUnlinkingProvider(null);
    },
    onError: (error: any) => {
      const message = error.response?.data?.error || 'Failed to unlink account';
      toast.error(message);
      setUnlinkingProvider(null);
    },
  });

  const handleRevoke = (clientId: string, clientName: string) => {
    if (window.confirm(`Are you sure you want to revoke access for "${clientName}"?`)) {
      setRevoking(clientId);
      revokeMutation.mutate(clientId);
    }
  };

  const handleUnlink = (login: ExternalLogin) => {
    if (window.confirm(`Are you sure you want to unlink your ${login.displayName} account?`)) {
      setUnlinkingProvider(login.provider);
      unlinkMutation.mutate({ provider: login.provider, providerKey: login.providerKey });
    }
  };

  if (isLoading && loadingExternalLogins) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  const apps = data?.apps || [];

  return (
    <div className="max-w-3xl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Connected Apps</h1>
        <p className="mt-1 text-sm text-gray-500">
          Applications that have access to your account. You can revoke access at any time.
        </p>
      </div>

      {/* External Logins Section */}
      {externalLogins && externalLogins.length > 0 && (
        <div className="mb-8">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Linked Accounts</h2>
          <p className="text-sm text-gray-500 mb-4">
            Sign in with these external identity providers.
          </p>
          <div className="space-y-3">
            {externalLogins.map((login) => (
              <div
                key={`${login.provider}-${login.providerKey}`}
                className="bg-white shadow rounded-lg p-4 flex items-center justify-between"
              >
                <div className="flex items-center gap-3">
                  <div className="h-10 w-10 rounded-full bg-gray-100 flex items-center justify-center">
                    <UserCircleIcon className="h-6 w-6 text-gray-400" />
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-900">{login.displayName}</p>
                    <p className="text-xs text-gray-500">{login.provider}</p>
                  </div>
                </div>
                <button
                  onClick={() => handleUnlink(login)}
                  disabled={unlinkingProvider === login.provider}
                  className="rounded-md bg-white px-3 py-1.5 text-sm font-semibold text-red-600 shadow-sm ring-1 ring-inset ring-red-300 hover:bg-red-50 disabled:opacity-50"
                >
                  {unlinkingProvider === login.provider ? 'Unlinking...' : 'Unlink'}
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Connected OAuth Apps Section */}
      <div className="mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Connected Applications</h2>
        <p className="text-sm text-gray-500 mt-1">
          Third-party applications you've authorized.
        </p>
      </div>

      <div className="space-y-4">
        {apps.map((app) => (
          <div key={app.clientId} className="bg-white shadow rounded-lg p-6">
            <div className="flex items-start gap-x-4">
              <div className="flex-shrink-0">
                {app.logoUri ? (
                  <img
                    src={app.logoUri}
                    alt=""
                    className="h-12 w-12 rounded-lg object-cover"
                  />
                ) : (
                  <div className="h-12 w-12 rounded-lg bg-gray-100 flex items-center justify-center">
                    <LinkIcon className="h-6 w-6 text-gray-400" />
                  </div>
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-x-2">
                  <h3 className="text-sm font-semibold text-gray-900">{app.clientName}</h3>
                  {app.hasActiveTokens && (
                    <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                      Active
                    </span>
                  )}
                </div>
                {app.description && (
                  <p className="mt-1 text-sm text-gray-500">{app.description}</p>
                )}
                {app.clientUri && (
                  <a
                    href={app.clientUri}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="mt-1 text-sm text-indigo-600 hover:text-indigo-500"
                  >
                    {new URL(app.clientUri).hostname}
                  </a>
                )}
                <div className="mt-2 flex items-center gap-x-4 text-xs text-gray-500">
                  <span>
                    Connected{' '}
                    {formatDistanceToNow(new Date(app.firstConnectedAt), { addSuffix: true })}
                  </span>
                  <span>
                    Last used{' '}
                    {formatDistanceToNow(new Date(app.lastUsedAt), { addSuffix: true })}
                  </span>
                </div>
                {app.scopes.length > 0 && (
                  <div className="mt-3">
                    <p className="text-xs font-medium text-gray-500 mb-1">Access granted:</p>
                    <div className="flex flex-wrap gap-1">
                      {app.scopes.map((scope) => (
                        <span
                          key={scope}
                          className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-xs text-gray-600"
                        >
                          {scope}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
              </div>
              <button
                onClick={() => handleRevoke(app.clientId, app.clientName)}
                disabled={revoking === app.clientId}
                className="rounded-md bg-white px-3 py-1.5 text-sm font-semibold text-red-600 shadow-sm ring-1 ring-inset ring-red-300 hover:bg-red-50 disabled:opacity-50"
              >
                {revoking === app.clientId ? (
                  'Revoking...'
                ) : (
                  <>
                    <TrashIcon className="h-4 w-4 inline-block mr-1" />
                    Revoke
                  </>
                )}
              </button>
            </div>
          </div>
        ))}

        {apps.length === 0 && (
          <div className="text-center py-12 bg-white rounded-lg shadow">
            <LinkIcon className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-semibold text-gray-900">No connected apps</h3>
            <p className="mt-1 text-sm text-gray-500">
              Applications you authorize will appear here.
            </p>
          </div>
        )}
      </div>
    </div>
  );
}

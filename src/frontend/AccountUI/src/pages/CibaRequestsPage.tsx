import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { formatDistanceToNow } from 'date-fns';
import { ShieldCheckIcon, XCircleIcon, ClockIcon } from '@heroicons/react/24/outline';
import { apiClient } from '../services/api';

interface CibaRequest {
  authReqId: string;
  clientId: string;
  clientName?: string;
  clientLogoUri?: string;
  bindingMessage?: string;
  requestedScopes: string[];
  status: string;
  createdAt: string;
  expiresAt: string;
}

interface CibaRequestList {
  requests: CibaRequest[];
  totalCount: number;
}

// Scope descriptions for common OIDC scopes
const scopeDescriptions: Record<string, string> = {
  openid: 'Verify your identity',
  profile: 'Access your profile information (name, picture)',
  email: 'Access your email address',
  address: 'Access your address',
  phone: 'Access your phone number',
  offline_access: 'Maintain access when you\'re not using the app',
};

function getScopeDescription(scope: string): string {
  return scopeDescriptions[scope] || scope;
}

export function CibaRequestsPage() {
  const queryClient = useQueryClient();

  const { data, isLoading, error } = useQuery({
    queryKey: ['ciba-requests'],
    queryFn: async () => {
      const response = await apiClient.get<CibaRequestList>('/api/account/ciba/pending');
      return response.data;
    },
    refetchInterval: 10000, // Refresh every 10 seconds for pending requests
  });

  const approveMutation = useMutation({
    mutationFn: async (authReqId: string) => {
      await apiClient.post(`/api/account/ciba/${authReqId}/approve`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ciba-requests'] });
      toast.success('Request approved');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to approve request');
    },
  });

  const denyMutation = useMutation({
    mutationFn: async (authReqId: string) => {
      await apiClient.post(`/api/account/ciba/${authReqId}/deny`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ciba-requests'] });
      toast.success('Request denied');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to deny request');
    },
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="max-w-3xl">
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <p className="text-red-800">Failed to load authentication requests.</p>
        </div>
      </div>
    );
  }

  const requests = data?.requests || [];

  return (
    <div className="max-w-3xl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Authentication Requests</h1>
        <p className="mt-1 text-sm text-gray-500">
          Review and respond to pending sign-in requests from applications.
        </p>
      </div>

      {requests.length === 0 ? (
        <div className="text-center py-12 bg-white rounded-lg shadow">
          <ShieldCheckIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">No pending requests</h3>
          <p className="mt-1 text-sm text-gray-500">
            You have no pending authentication requests at this time.
          </p>
        </div>
      ) : (
        <div className="space-y-4">
          {requests.map((request) => (
            <div
              key={request.authReqId}
              className="bg-white shadow rounded-lg overflow-hidden"
            >
              <div className="p-6">
                <div className="flex items-start gap-x-4">
                  {request.clientLogoUri ? (
                    <img
                      src={request.clientLogoUri}
                      alt={request.clientName || request.clientId}
                      className="h-12 w-12 rounded-lg object-contain bg-gray-100"
                    />
                  ) : (
                    <div className="h-12 w-12 rounded-lg bg-indigo-100 flex items-center justify-center">
                      <span className="text-indigo-600 text-lg font-semibold">
                        {(request.clientName || request.clientId).charAt(0).toUpperCase()}
                      </span>
                    </div>
                  )}
                  <div className="flex-1 min-w-0">
                    <h3 className="text-lg font-semibold text-gray-900">
                      {request.clientName || request.clientId}
                    </h3>
                    <p className="text-sm text-gray-500">
                      wants to sign you in
                    </p>
                  </div>
                  <div className="flex items-center text-sm text-gray-500">
                    <ClockIcon className="h-4 w-4 mr-1" />
                    Expires {formatDistanceToNow(new Date(request.expiresAt), { addSuffix: true })}
                  </div>
                </div>

                {request.bindingMessage && (
                  <div className="mt-4 p-3 bg-blue-50 border border-blue-200 rounded-md">
                    <p className="text-sm font-medium text-blue-800">
                      Verification code: <span className="font-mono">{request.bindingMessage}</span>
                    </p>
                    <p className="text-xs text-blue-600 mt-1">
                      Make sure this code matches what you see on the requesting device.
                    </p>
                  </div>
                )}

                <div className="mt-4">
                  <p className="text-sm font-medium text-gray-700 mb-2">
                    This application is requesting access to:
                  </p>
                  <ul className="space-y-1">
                    {request.requestedScopes.map((scope) => (
                      <li key={scope} className="flex items-center text-sm text-gray-600">
                        <ShieldCheckIcon className="h-4 w-4 mr-2 text-green-500" />
                        {getScopeDescription(scope)}
                      </li>
                    ))}
                  </ul>
                </div>
              </div>

              <div className="bg-gray-50 px-6 py-4 flex justify-end gap-3">
                <button
                  onClick={() => denyMutation.mutate(request.authReqId)}
                  disabled={denyMutation.isPending || approveMutation.isPending}
                  className="inline-flex items-center rounded-md bg-white px-4 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:opacity-50"
                >
                  <XCircleIcon className="h-4 w-4 mr-1.5 text-red-500" />
                  Deny
                </button>
                <button
                  onClick={() => approveMutation.mutate(request.authReqId)}
                  disabled={approveMutation.isPending || denyMutation.isPending}
                  className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  <ShieldCheckIcon className="h-4 w-4 mr-1.5" />
                  {approveMutation.isPending ? 'Approving...' : 'Approve'}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

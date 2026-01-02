import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  FingerPrintIcon,
  TrashIcon,
  DevicePhoneMobileIcon,
  KeyIcon,
} from '@heroicons/react/24/outline';
import { fido2Api } from '../api/fido2Api';
import type { DetailPageTabProps, UserDetailData } from '@oluso/ui-core';

export function UserPasskeysTab({ data, onRefresh }: DetailPageTabProps<UserDetailData>) {
  const queryClient = useQueryClient();
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);

  const { data: credentials, isLoading, error } = useQuery({
    queryKey: ['user-credentials', data.id],
    queryFn: () => fido2Api.getUserCredentials(data.id),
  });

  const deleteMutation = useMutation({
    mutationFn: (credentialId: string) => fido2Api.deleteUserCredential(data.id, credentialId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user-credentials', data.id] });
      setDeleteConfirm(null);
      onRefresh?.();
    },
  });

  const getAuthenticatorIcon = (type: string) => {
    if (type?.toLowerCase().includes('platform')) {
      return <DevicePhoneMobileIcon className="h-5 w-5 text-gray-400" />;
    }
    if (type?.toLowerCase().includes('cross-platform') || type?.toLowerCase().includes('security-key')) {
      return <KeyIcon className="h-5 w-5 text-gray-400" />;
    }
    return <FingerPrintIcon className="h-5 w-5 text-gray-400" />;
  };

  if (isLoading) {
    return (
      <div className="bg-white shadow rounded-lg p-6">
        <div className="animate-pulse space-y-4">
          <div className="h-4 bg-gray-200 rounded w-1/4"></div>
          <div className="h-10 bg-gray-200 rounded"></div>
          <div className="h-10 bg-gray-200 rounded"></div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-white shadow rounded-lg p-6">
        <div className="text-red-600">Failed to load passkeys</div>
      </div>
    );
  }

  return (
    <div className="bg-white shadow rounded-lg">
      <div className="px-4 py-5 sm:p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-medium leading-6 text-gray-900">
            Passkeys / FIDO2 Credentials
          </h3>
          <span className="text-sm text-gray-500">
            {credentials?.length || 0} registered
          </span>
        </div>

        {credentials && credentials.length > 0 ? (
          <div className="space-y-3">
            {credentials.map((credential) => (
              <div
                key={credential.id}
                className="flex items-center justify-between p-4 bg-gray-50 rounded-lg"
              >
                <div className="flex items-center gap-4">
                  <div className="flex-shrink-0">
                    {getAuthenticatorIcon(credential.authenticatorType)}
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-900">
                      {credential.displayName || 'Unnamed Passkey'}
                    </p>
                    <div className="flex items-center gap-3 mt-1">
                      <span className="text-xs text-gray-500">
                        Created: {new Date(credential.createdAt).toLocaleDateString()}
                      </span>
                      {credential.lastUsedAt && (
                        <span className="text-xs text-gray-500">
                          Last used: {new Date(credential.lastUsedAt).toLocaleDateString()}
                        </span>
                      )}
                      <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                        credential.isActive
                          ? 'bg-green-100 text-green-800'
                          : 'bg-gray-100 text-gray-800'
                      }`}>
                        {credential.isActive ? 'Active' : 'Inactive'}
                      </span>
                      {credential.isBackedUp && (
                        <span className="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-800">
                          Backed up
                        </span>
                      )}
                    </div>
                    <p className="text-xs text-gray-400 mt-1 font-mono">
                      {credential.authenticatorType}
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  {deleteConfirm === credential.id ? (
                    <>
                      <button
                        onClick={() => deleteMutation.mutate(credential.id)}
                        disabled={deleteMutation.isPending}
                        className="px-3 py-1.5 text-xs font-medium text-white bg-red-600 rounded hover:bg-red-700 disabled:opacity-50"
                      >
                        {deleteMutation.isPending ? 'Deleting...' : 'Confirm'}
                      </button>
                      <button
                        onClick={() => setDeleteConfirm(null)}
                        className="px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded hover:bg-gray-50"
                      >
                        Cancel
                      </button>
                    </>
                  ) : (
                    <button
                      onClick={() => setDeleteConfirm(credential.id)}
                      className="p-2 text-gray-400 hover:text-red-600 rounded hover:bg-gray-100"
                      title="Delete passkey"
                    >
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        ) : (
          <div className="text-center py-8">
            <FingerPrintIcon className="mx-auto h-12 w-12 text-gray-300" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">No passkeys</h3>
            <p className="mt-1 text-sm text-gray-500">
              This user has not registered any FIDO2 passkeys.
            </p>
          </div>
        )}
      </div>
    </div>
  );
}

export default UserPasskeysTab;

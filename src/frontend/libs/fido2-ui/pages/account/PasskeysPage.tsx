import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import {
  KeyIcon,
  PlusIcon,
  TrashIcon,
  FingerPrintIcon,
  PencilIcon,
} from '@heroicons/react/24/outline';
import { fido2AccountApi, AccountPasskey, WebAuthnRegistrationOptions } from '../../api/fido2Api';

// Helper to convert base64url to ArrayBuffer
function base64UrlToArrayBuffer(base64url: string): ArrayBuffer {
  const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
  const padding = '='.repeat((4 - (base64.length % 4)) % 4);
  const binary = atob(base64 + padding);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

// Helper to convert ArrayBuffer to base64url
function arrayBufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}

function getDeviceName(): string {
  const ua = navigator.userAgent;
  if (/iPhone|iPad|iPod/.test(ua)) return 'iPhone/iPad';
  if (/Android/.test(ua)) return 'Android device';
  if (/Mac/.test(ua)) return 'Mac';
  if (/Windows/.test(ua)) return 'Windows PC';
  if (/Linux/.test(ua)) return 'Linux device';
  return 'Unknown device';
}

function convertOptionsForWebAuthn(options: WebAuthnRegistrationOptions): PublicKeyCredentialCreationOptions {
  return {
    challenge: base64UrlToArrayBuffer(options.challenge),
    rp: options.rp,
    user: {
      ...options.user,
      id: base64UrlToArrayBuffer(options.user.id),
    },
    pubKeyCredParams: options.pubKeyCredParams?.map(p => ({
      type: p.type as PublicKeyCredentialType,
      alg: p.alg,
    })) || [
      { type: 'public-key', alg: -7 },  // ES256
      { type: 'public-key', alg: -257 }, // RS256
    ],
    timeout: options.timeout || 60000,
    attestation: (options.attestation as AttestationConveyancePreference) || 'none',
    authenticatorSelection: options.authenticatorSelection as AuthenticatorSelectionCriteria,
    excludeCredentials: options.excludeCredentials?.map(cred => ({
      type: cred.type as PublicKeyCredentialType,
      id: base64UrlToArrayBuffer(cred.id),
      transports: cred.transports as AuthenticatorTransport[],
    })),
  };
}

export function PasskeysPage() {
  const queryClient = useQueryClient();
  const [isRegistering, setIsRegistering] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [editingPasskey, setEditingPasskey] = useState<AccountPasskey | null>(null);
  const [editName, setEditName] = useState('');
  const [showNamePrompt, setShowNamePrompt] = useState(false);
  const [pendingRegistration, setPendingRegistration] = useState<{
    registrationId: string;
    attestationResponse: string;
  } | null>(null);
  const [newPasskeyName, setNewPasskeyName] = useState('');

  const { data: passkeyList, isLoading } = useQuery({
    queryKey: ['account-passkeys'],
    queryFn: () => fido2AccountApi.getMyPasskeys(),
  });

  const registerPasskeyMutation = useMutation({
    mutationFn: async (name: string) => {
      if (!pendingRegistration) {
        throw new Error('No pending registration');
      }
      await fido2AccountApi.completeRegistration(
        pendingRegistration.registrationId,
        pendingRegistration.attestationResponse,
        name || getDeviceName()
      );
    },
    onSuccess: () => {
      toast.success('Passkey registered successfully');
      queryClient.invalidateQueries({ queryKey: ['account-passkeys'] });
      setIsRegistering(false);
      setShowNamePrompt(false);
      setPendingRegistration(null);
      setNewPasskeyName('');
    },
    onError: (error: any) => {
      console.error('Passkey registration failed:', error);
      toast.error(error.response?.data?.error || error.message || 'Failed to register passkey');
      setShowNamePrompt(false);
      setPendingRegistration(null);
      setNewPasskeyName('');
    },
  });

  const updatePasskeyMutation = useMutation({
    mutationFn: ({ passkeyId, name }: { passkeyId: string; name: string }) =>
      fido2AccountApi.updatePasskey(passkeyId, name),
    onSuccess: () => {
      toast.success('Passkey renamed');
      queryClient.invalidateQueries({ queryKey: ['account-passkeys'] });
      setEditingPasskey(null);
      setEditName('');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to rename passkey');
    },
  });

  const deletePasskeyMutation = useMutation({
    mutationFn: (passkeyId: string) => fido2AccountApi.deletePasskey(passkeyId),
    onSuccess: () => {
      toast.success('Passkey deleted');
      queryClient.invalidateQueries({ queryKey: ['account-passkeys'] });
      setDeletingId(null);
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to delete passkey');
      setDeletingId(null);
    },
  });

  const handleRegister = async () => {
    if (!window.PublicKeyCredential) {
      toast.error('WebAuthn is not supported in this browser');
      return;
    }
    setIsRegistering(true);

    try {
      // Step 1: Get registration options from server
      const { registrationId, options } = await fido2AccountApi.startRegistration({
        requireDiscoverableCredential: true,
      });

      // Step 2: Convert options for WebAuthn API
      const publicKeyOptions = convertOptionsForWebAuthn(options);

      // Step 3: Create credential using browser API
      const credential = await navigator.credentials.create({
        publicKey: publicKeyOptions,
      }) as PublicKeyCredential;

      if (!credential) {
        throw new Error('Failed to create credential');
      }

      const response = credential.response as AuthenticatorAttestationResponse;

      // Step 4: Build attestation response
      const attestationResponse = JSON.stringify({
        id: credential.id,
        rawId: arrayBufferToBase64Url(credential.rawId),
        type: credential.type,
        response: {
          clientDataJSON: arrayBufferToBase64Url(response.clientDataJSON),
          attestationObject: arrayBufferToBase64Url(response.attestationObject),
          transports: response.getTransports?.() || [],
        },
      });

      // Step 5: Show name prompt before completing registration
      setPendingRegistration({ registrationId, attestationResponse });
      setNewPasskeyName(getDeviceName());
      setShowNamePrompt(true);
    } catch (error: any) {
      console.error('Passkey registration failed:', error);
      if (error.name === 'NotAllowedError') {
        toast.error('Registration was cancelled or not allowed');
      } else if (error.name === 'SecurityError') {
        toast.error('Security error - please ensure you are using HTTPS');
      } else {
        toast.error(error.response?.data?.error || error.message || 'Failed to register passkey');
      }
      setIsRegistering(false);
    }
  };

  const handleCompleteRegistration = () => {
    registerPasskeyMutation.mutate(newPasskeyName);
  };

  const handleCancelRegistration = () => {
    setShowNamePrompt(false);
    setPendingRegistration(null);
    setNewPasskeyName('');
    setIsRegistering(false);
  };

  const handleEdit = (passkey: AccountPasskey) => {
    setEditingPasskey(passkey);
    setEditName(passkey.name);
  };

  const handleSaveEdit = () => {
    if (editingPasskey && editName.trim()) {
      updatePasskeyMutation.mutate({ passkeyId: editingPasskey.id, name: editName.trim() });
    }
  };

  const handleDelete = (passkey: AccountPasskey) => {
    if (passkeyList?.passkeys.length === 1) {
      if (!confirm('This is your last passkey. Are you sure you want to delete it?')) {
        return;
      }
    }
    setDeletingId(passkey.id);
    deletePasskeyMutation.mutate(passkey.id);
  };

  const getPasskeyIcon = (passkey: AccountPasskey) => {
    switch (passkey.authenticatorType?.toLowerCase()) {
      case 'platform':
        return <FingerPrintIcon className="h-8 w-8 text-indigo-600" />;
      case 'cross-platform':
        return <KeyIcon className="h-8 w-8 text-amber-600" />;
      default:
        return <FingerPrintIcon className="h-8 w-8 text-gray-400" />;
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  if (!passkeyList?.isEnabled) {
    return (
      <div className="max-w-2xl">
        <div className="mb-8">
          <h1 className="text-2xl font-bold text-gray-900">Passkeys</h1>
        </div>
        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <p className="text-sm text-yellow-800">
            {passkeyList?.message || 'Passkeys are not enabled for this server.'}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-2xl">
      <div className="mb-8">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Passkeys</h1>
            <p className="mt-1 text-sm text-gray-500">
              Sign in without a password using biometrics or security keys.
            </p>
          </div>
          <button
            onClick={handleRegister}
            disabled={isRegistering}
            className="inline-flex items-center gap-x-2 rounded-md bg-indigo-600 px-3.5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
          >
            <PlusIcon className="h-5 w-5" />
            {isRegistering ? 'Registering...' : 'Add passkey'}
          </button>
        </div>
      </div>

      {/* Name prompt modal */}
      {showNamePrompt && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-md mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">Name your passkey</h3>
            <p className="text-sm text-gray-500 mb-4">
              Give your passkey a name to help you identify it later.
            </p>
            <input
              type="text"
              value={newPasskeyName}
              onChange={(e) => setNewPasskeyName(e.target.value)}
              placeholder="e.g., MacBook Pro, iPhone"
              className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:ring-indigo-500 focus:border-indigo-500"
              autoFocus
            />
            <div className="mt-4 flex justify-end gap-3">
              <button
                onClick={handleCancelRegistration}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleCompleteRegistration}
                disabled={registerPasskeyMutation.isPending}
                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-500 disabled:opacity-50"
              >
                {registerPasskeyMutation.isPending ? 'Saving...' : 'Save passkey'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit name modal */}
      {editingPasskey && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-md mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">Rename passkey</h3>
            <input
              type="text"
              value={editName}
              onChange={(e) => setEditName(e.target.value)}
              placeholder="Passkey name"
              className="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:ring-indigo-500 focus:border-indigo-500"
              autoFocus
            />
            <div className="mt-4 flex justify-end gap-3">
              <button
                onClick={() => setEditingPasskey(null)}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveEdit}
                disabled={updatePasskeyMutation.isPending || !editName.trim()}
                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-500 disabled:opacity-50"
              >
                {updatePasskeyMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Browser support check */}
      {typeof window !== 'undefined' && !window.PublicKeyCredential && (
        <div className="mb-6 bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <p className="text-sm text-yellow-800">
            Your browser does not support passkeys. Please use a modern browser like Chrome, Safari, Firefox, or Edge.
          </p>
        </div>
      )}

      {/* Passkeys list */}
      <div className="bg-white shadow rounded-lg divide-y divide-gray-200">
        {passkeyList.passkeys.length === 0 ? (
          <div className="px-6 py-12 text-center">
            <FingerPrintIcon className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-4 text-sm font-semibold text-gray-900">No passkeys</h3>
            <p className="mt-2 text-sm text-gray-500">
              Get started by registering a passkey for faster, more secure sign-in.
            </p>
          </div>
        ) : (
          passkeyList.passkeys.map((passkey) => (
            <div key={passkey.id} className="px-6 py-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-x-4">
                  {getPasskeyIcon(passkey)}
                  <div>
                    <p className="text-sm font-medium text-gray-900">{passkey.name}</p>
                    <div className="mt-1 flex items-center gap-x-3 text-xs text-gray-500">
                      <span>
                        Added {new Date(passkey.createdAt).toLocaleDateString()}
                      </span>
                      {passkey.lastUsedAt && (
                        <>
                          <span>·</span>
                          <span>
                            Last used {new Date(passkey.lastUsedAt).toLocaleDateString()}
                          </span>
                        </>
                      )}
                    </div>
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => handleEdit(passkey)}
                    className="p-2 text-gray-400 hover:text-indigo-600 rounded hover:bg-gray-100"
                    title="Rename passkey"
                  >
                    <PencilIcon className="h-4 w-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(passkey)}
                    disabled={deletingId === passkey.id}
                    className="p-2 text-gray-400 hover:text-red-500 disabled:opacity-50 rounded hover:bg-gray-100"
                    title="Delete passkey"
                  >
                    {deletingId === passkey.id ? (
                      <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-red-500" />
                    ) : (
                      <TrashIcon className="h-4 w-4" />
                    )}
                  </button>
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      {/* Info section */}
      <div className="mt-8 bg-blue-50 border border-blue-200 rounded-lg p-4">
        <h3 className="text-sm font-semibold text-blue-900 mb-2">About Passkeys</h3>
        <ul className="text-sm text-blue-800 space-y-1">
          <li>• Passkeys are a more secure alternative to passwords</li>
          <li>• They use biometrics (fingerprint, face) or a security key</li>
          <li>• Your biometric data never leaves your device</li>
          <li>• Passkeys are resistant to phishing attacks</li>
        </ul>
      </div>
    </div>
  );
}

export default PasskeysPage;

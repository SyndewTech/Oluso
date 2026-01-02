import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader } from '../components/common/Card';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import SecretInput from '../components/common/SecretInput';
import { identityProviderService } from '../services/identityProviderService';
import type {
  CreateIdentityProviderRequest,
  IdentityProviderEdit,
  ExternalProviderType,
} from '../types/identityProviders';
import { ProviderTypes } from '../types/identityProviders';
import {
  ArrowLeftIcon,
  CheckCircleIcon,
  XCircleIcon,
  PlayIcon,
  InformationCircleIcon,
  ArrowTopRightOnSquareIcon,
  ExclamationCircleIcon,
} from '@heroicons/react/24/outline';

export default function IdentityProviderDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);

  const isNew = id === 'new';

  // Form state type: providerType is optional until user selects one
  type FormState = Omit<CreateIdentityProviderRequest, 'providerType'> & { providerType?: ExternalProviderType };

  const [formData, setFormData] = useState<FormState>({
    scheme: '',
    displayName: '',
    enabled: true,
    configuration: {},
  });

  // Derive selectedType from formData - single source of truth
  const selectedType = formData.providerType ?? null;

  // Use getForEdit to get full configuration with unmasked secrets for editing
  const { data: provider, isLoading } = useQuery<IdentityProviderEdit>({
    queryKey: ['identity-provider-edit', id],
    queryFn: () => identityProviderService.getForEdit(Number(id)),
    enabled: !!id && !isNew,
  });

  const { data: providerTypes } = useQuery({
    queryKey: ['provider-types'],
    queryFn: () => identityProviderService.getProviderTypes(),
  });

  const { data: serverInfo } = useQuery({
    queryKey: ['identity-provider-server-info'],
    queryFn: () => identityProviderService.getServerInfo(),
  });

  // Debounced scheme for availability check
  const [debouncedScheme, setDebouncedScheme] = useState('');

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedScheme(formData.scheme);
    }, 300);
    return () => clearTimeout(timer);
  }, [formData.scheme]);

  // Check scheme availability (only for new providers)
  const { data: schemeAvailability, isFetching: isCheckingScheme } = useQuery({
    queryKey: ['scheme-availability', debouncedScheme],
    queryFn: () => identityProviderService.checkSchemeAvailability(debouncedScheme),
    enabled: isNew && debouncedScheme.length >= 2,
    staleTime: 5000,
  });

  useEffect(() => {
    if (provider) {
      setFormData({
        scheme: provider.scheme,
        displayName: provider.displayName,
        enabled: provider.enabled,
        providerType: provider.providerType,
        iconUrl: provider.iconUrl,
        displayOrder: provider.displayOrder,
        allowedClientIds: provider.allowedClientIds,
        configuration: provider.configuration || {},
      });
    }
  }, [provider]);

  const createMutation = useMutation({
    mutationFn: (data: CreateIdentityProviderRequest) => identityProviderService.create(data),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ['identity-providers'] });
      navigate(`/identity-providers/${created.id}`);
    },
  });

  const updateMutation = useMutation({
    mutationFn: (data: CreateIdentityProviderRequest) =>
      identityProviderService.update(Number(id), data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['identity-providers'] });
      queryClient.invalidateQueries({ queryKey: ['identity-provider-edit', id] });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => identityProviderService.delete(Number(id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['identity-providers'] });
      navigate('/identity-providers');
    },
  });

  const testMutation = useMutation({
    mutationFn: () => identityProviderService.test(Number(id)),
    onSuccess: (result) => {
      setTestResult(result);
    },
  });

  const handleSave = () => {
    if (isNew) {
      // providerType is guaranteed to be defined here (button is disabled otherwise)
      createMutation.mutate(formData as CreateIdentityProviderRequest);
    } else {
      updateMutation.mutate(formData as CreateIdentityProviderRequest);
    }
  };

  const updateConfig = (key: string, value: unknown) => {
    setFormData({
      ...formData,
      configuration: {
        ...formData.configuration,
        [key]: value,
      },
    });
  };

  // Find the provider type info matching the selected type (both are numbers)
  const selectedTypeInfo = providerTypes?.find((t) =>
    selectedType !== null && t.type === selectedType
  );

  // Helper to get provider type name from the API-provided list
  const getProviderTypeName = (type: number): string => {
    return providerTypes?.find(t => t.type === type)?.name ?? `Type ${type}`;
  };

  // Helper to reveal secrets from the server
  const revealSecret = useCallback(
    (secretKey: string) => async (): Promise<string | null> => {
      if (isNew || !id) return null;
      return identityProviderService.getSecret(Number(id), secretKey);
    },
    [id, isNew]
  );

  // Get the callback URL for this IdP using server-provided info
  const getCallbackUrl = () => {
    const baseUrl = serverInfo?.issuerUri || window.location.origin;
    const scheme = formData.scheme || '{scheme}';
    const callbackPath = serverInfo?.callbackPathTemplate?.replace('{scheme}', scheme) || `/signin-${scheme}`;
    return `${baseUrl}${callbackPath}`;
  };

  // Get discovery URL for OIDC providers
  const getDiscoveryUrl = () => {
    const authority = (formData.configuration?.authority as string) || '';
    if (!authority) return null;
    const baseUrl = authority.replace(/\/$/, '');
    return `${baseUrl}/.well-known/openid-configuration`;
  };

  // Open discovery URL in new tab
  const openDiscoveryUrl = () => {
    const url = getDiscoveryUrl();
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  };

  // Render proxy mode settings (common to all providers)
  const renderProxyModeSettings = () => {
    const config = formData.configuration || {};
    return (
      <>
        <div className="flex items-center">
          <input
            type="checkbox"
            id="proxyMode"
            checked={(config.proxyMode as boolean) || false}
            onChange={(e) => updateConfig('proxyMode', e.target.checked)}
            className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
          />
          <label htmlFor="proxyMode" className="ml-2 text-sm text-gray-700">
            Enable Proxy/Pass-through Mode
          </label>
        </div>
        <p className="text-xs text-gray-500 ml-6 -mt-2">
          Authenticate via external IdP without storing user details locally
        </p>

        {(config.proxyMode as boolean) && (
          <div className="mt-4 pl-4 border-l-2 border-blue-200 space-y-4">
            <div className="flex items-center">
              <input
                type="checkbox"
                id="storeUserLocally"
                checked={(config.storeUserLocally as boolean) ?? true}
                onChange={(e) => updateConfig('storeUserLocally', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="storeUserLocally" className="ml-2 text-sm text-gray-700">
                Store user details locally
              </label>
            </div>

            <div className="flex items-center">
              <input
                type="checkbox"
                id="cacheExternalTokens"
                checked={(config.cacheExternalTokens as boolean) || false}
                onChange={(e) => updateConfig('cacheExternalTokens', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="cacheExternalTokens" className="ml-2 text-sm text-gray-700">
                Cache external tokens for userinfo proxy
              </label>
            </div>

            {(config.cacheExternalTokens as boolean) && (
              <div className="ml-6">
                <label className="block text-sm font-medium text-gray-700">Token Cache Duration (seconds)</label>
                <input
                  type="number"
                  value={(config.tokenCacheDurationSeconds as number) || 3600}
                  onChange={(e) => updateConfig('tokenCacheDurationSeconds', Number(e.target.value))}
                  className="mt-1 block w-32 rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  min={60}
                  max={86400}
                />
              </div>
            )}

            <div className="flex items-center">
              <input
                type="checkbox"
                id="enableUserInfoProxy"
                checked={(config.enableUserInfoProxy as boolean) || false}
                onChange={(e) => updateConfig('enableUserInfoProxy', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="enableUserInfoProxy" className="ml-2 text-sm text-gray-700">
                Enable UserInfo proxy endpoint
              </label>
            </div>
            <p className="text-xs text-gray-500 ml-6 -mt-2">
              Allows clients to fetch user details from external IdP via /userinfo-proxy/{'{scheme}'}
            </p>

            <h5 className="font-medium text-gray-800 text-sm mt-4">Token Pass-through</h5>
            <div className="flex items-center">
              <input
                type="checkbox"
                id="includeExternalAccessToken"
                checked={(config.includeExternalAccessToken as boolean) || false}
                onChange={(e) => updateConfig('includeExternalAccessToken', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="includeExternalAccessToken" className="ml-2 text-sm text-gray-700">
                Include external access token in issued tokens
              </label>
            </div>
            <div className="flex items-center">
              <input
                type="checkbox"
                id="includeExternalIdToken"
                checked={(config.includeExternalIdToken as boolean) || false}
                onChange={(e) => updateConfig('includeExternalIdToken', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="includeExternalIdToken" className="ml-2 text-sm text-gray-700">
                Include external ID token in issued tokens
              </label>
            </div>
            <p className="text-xs text-orange-600 ml-6 -mt-2">
              Warning: Exposes external tokens to clients. Use with caution.
            </p>

            <h5 className="font-medium text-gray-800 text-sm mt-4">Claim Filtering</h5>
            <div>
              <label className="block text-sm font-medium text-gray-700">Include Claims (comma-separated)</label>
              <input
                type="text"
                value={((config.proxyIncludeClaims as string[]) || []).join(', ')}
                onChange={(e) => updateConfig('proxyIncludeClaims', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="Leave empty to include all claims"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Exclude Claims (comma-separated)</label>
              <input
                type="text"
                value={((config.proxyExcludeClaims as string[]) || []).join(', ')}
                onChange={(e) => updateConfig('proxyExcludeClaims', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="e.g., nonce, at_hash, c_hash"
              />
            </div>
          </div>
        )}
      </>
    );
  };

  // Render provider-specific setup instructions
  const renderSetupHelp = () => {
    const callbackUrl = getCallbackUrl();

    switch (selectedType) {
      case ProviderTypes.Google:
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">Google Cloud Console Setup</h4>
            <ol className="list-decimal list-inside space-y-2 text-sm text-gray-600">
              <li>Go to <a href="https://console.cloud.google.com/apis/credentials" target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:underline">Google Cloud Console → APIs & Services → Credentials</a></li>
              <li>Create OAuth 2.0 Client ID (Web application type)</li>
              <li>Add the following Authorized redirect URI:</li>
            </ol>
            <div className="bg-gray-100 p-2 rounded font-mono text-xs break-all">{callbackUrl}</div>
          </div>
        );

      case ProviderTypes.Microsoft:
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">Azure/Entra ID Setup</h4>
            <ol className="list-decimal list-inside space-y-2 text-sm text-gray-600">
              <li>Go to <a href="https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade" target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:underline">Azure Portal → App Registrations</a></li>
              <li>Create new registration or select existing app</li>
              <li>Under Authentication → Add platform → Web</li>
              <li>Add the following redirect URI:</li>
            </ol>
            <div className="bg-gray-100 p-2 rounded font-mono text-xs break-all">{callbackUrl}</div>
          </div>
        );

      case ProviderTypes.GitHub:
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">GitHub OAuth App Setup</h4>
            <ol className="list-decimal list-inside space-y-2 text-sm text-gray-600">
              <li>Go to <a href="https://github.com/settings/developers" target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:underline">GitHub → Settings → Developer Settings → OAuth Apps</a></li>
              <li>Create new OAuth App</li>
              <li>Set the Authorization callback URL to:</li>
            </ol>
            <div className="bg-gray-100 p-2 rounded font-mono text-xs break-all">{callbackUrl}</div>
          </div>
        );

      case ProviderTypes.Oidc:
        const discoveryUrl = getDiscoveryUrl();
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">OpenID Connect Setup</h4>
            <p className="text-sm text-gray-600">Register this application with your OIDC provider using the callback URL below:</p>
            <div className="bg-gray-100 p-2 rounded font-mono text-xs break-all">{callbackUrl}</div>
            {discoveryUrl && (
              <div className="pt-2">
                <p className="text-sm text-gray-600 mb-2">Discovery document:</p>
                <button
                  onClick={openDiscoveryUrl}
                  className="inline-flex items-center gap-1 text-blue-600 hover:underline text-sm"
                >
                  <span className="font-mono text-xs">{discoveryUrl}</span>
                  <ArrowTopRightOnSquareIcon className="h-4 w-4" />
                </button>
              </div>
            )}
          </div>
        );

      case ProviderTypes.Saml2:
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">SAML 2.0 Service Provider Metadata</h4>
            <p className="text-sm text-gray-600">Configure your Identity Provider with these values:</p>
            <dl className="space-y-2 text-sm">
              <div>
                <dt className="font-medium text-gray-700">ACS URL (Assertion Consumer Service):</dt>
                <dd className="bg-gray-100 p-2 rounded font-mono text-xs break-all mt-1">{callbackUrl}</dd>
              </div>
              <div>
                <dt className="font-medium text-gray-700">Entity ID / Audience:</dt>
                <dd className="bg-gray-100 p-2 rounded font-mono text-xs break-all mt-1">{serverInfo?.issuerUri || window.location.origin}</dd>
              </div>
            </dl>
          </div>
        );

      case ProviderTypes.Ldap:
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">LDAP/Active Directory Setup</h4>
            <p className="text-sm text-gray-600">
              Ensure the LDAP server is reachable from this server. Common configurations:
            </p>
            <ul className="list-disc list-inside space-y-1 text-sm text-gray-600">
              <li><strong>Active Directory:</strong> Port 389 (LDAP) or 636 (LDAPS), filter: <code className="bg-gray-100 px-1">(sAMAccountName={'{0}'})</code></li>
              <li><strong>OpenLDAP:</strong> Port 389 (LDAP) or 636 (LDAPS), filter: <code className="bg-gray-100 px-1">(uid={'{0}'})</code></li>
              <li><strong>389 Directory:</strong> Similar to OpenLDAP configuration</li>
            </ul>
          </div>
        );

      default:
        return (
          <div className="space-y-3">
            <h4 className="font-medium text-gray-900">Callback URL</h4>
            <p className="text-sm text-gray-600">Configure your identity provider to redirect to:</p>
            <div className="bg-gray-100 p-2 rounded font-mono text-xs break-all">{callbackUrl}</div>
          </div>
        );
    }
  };

  // Render configuration fields based on provider type
  const renderConfigFields = () => {
    const config = formData.configuration || {};

    switch (selectedType) {
      case ProviderTypes.Google:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="xxx.apps.googleusercontent.com"
              />
            </div>
            <SecretInput
              label="Client Secret *"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">
                Hosted Domain (optional)
              </label>
              <input
                type="text"
                value={(config.hostedDomain as string) || ''}
                onChange={(e) => updateConfig('hostedDomain', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="example.com"
              />
              <p className="mt-1 text-xs text-gray-500">
                Restrict to Google Workspace domain
              </p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Access Type</label>
              <select
                value={(config.accessType as string) || 'online'}
                onChange={(e) => updateConfig('accessType', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              >
                <option value="online">Online (default)</option>
                <option value="offline">Offline (get refresh token)</option>
              </select>
              <p className="mt-1 text-xs text-gray-500">
                Use offline to receive a refresh token for long-lived access
              </p>
            </div>
          </>
        );

      case ProviderTypes.Microsoft:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>
            <SecretInput
              label="Client Secret *"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Tenant ID</label>
              <select
                value={(config.tenantId as string) || 'common'}
                onChange={(e) => updateConfig('tenantId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              >
                <option value="common">Common (any account)</option>
                <option value="organizations">Organizations only</option>
                <option value="consumers">Personal accounts only</option>
              </select>
              <p className="mt-1 text-xs text-gray-500">
                Or enter a specific tenant ID/domain
              </p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">
                Domain Hint (optional)
              </label>
              <input
                type="text"
                value={(config.domainHint as string) || ''}
                onChange={(e) => updateConfig('domainHint', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="contoso.com"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Instance (optional)</label>
              <input
                type="text"
                value={(config.instance as string) || ''}
                onChange={(e) => updateConfig('instance', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://login.microsoftonline.com"
              />
              <p className="mt-1 text-xs text-gray-500">
                Override Azure AD instance URL (e.g., for national clouds)
              </p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Prompt</label>
              <select
                value={(config.prompt as string) || ''}
                onChange={(e) => updateConfig('prompt', e.target.value || undefined)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              >
                <option value="">Default</option>
                <option value="login">Force login</option>
                <option value="consent">Force consent</option>
                <option value="select_account">Select account</option>
                <option value="none">None (silent)</option>
              </select>
            </div>
          </>
        );

      case ProviderTypes.GitHub:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>
            <SecretInput
              label="Client Secret *"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">
                Enterprise URL (optional)
              </label>
              <input
                type="url"
                value={(config.enterpriseUrl as string) || ''}
                onChange={(e) => updateConfig('enterpriseUrl', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://github.example.com"
              />
              <p className="mt-1 text-xs text-gray-500">For GitHub Enterprise Server</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">
                Allowed Organizations (optional)
              </label>
              <input
                type="text"
                value={((config.allowedOrganizations as string[]) || []).join(', ')}
                onChange={(e) => updateConfig('allowedOrganizations', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="org1, org2"
              />
              <p className="mt-1 text-xs text-gray-500">
                Restrict login to members of these GitHub organizations (comma-separated)
              </p>
            </div>
            <div className="flex items-center">
              <input
                type="checkbox"
                id="requestEmail"
                checked={(config.requestEmail as boolean) ?? true}
                onChange={(e) => updateConfig('requestEmail', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="requestEmail" className="ml-2 text-sm text-gray-700">
                Request email address
              </label>
            </div>
          </>
        );

      case ProviderTypes.Facebook:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">App ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="Your Facebook App ID"
              />
            </div>
            <SecretInput
              label="App Secret *"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Fields</label>
              <input
                type="text"
                value={((config.fields as string[]) || ['id', 'name', 'email', 'picture']).join(',')}
                onChange={(e) => updateConfig('fields', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="id,name,email,picture"
              />
              <p className="mt-1 text-xs text-gray-500">Comma-separated Graph API fields</p>
            </div>
          </>
        );

      case ProviderTypes.Apple:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Services ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="com.example.app.auth"
              />
              <p className="mt-1 text-xs text-gray-500">Your Apple Services ID (not App ID)</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Team ID *</label>
              <input
                type="text"
                value={(config.teamId as string) || ''}
                onChange={(e) => updateConfig('teamId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="XXXXXXXXXX"
              />
              <p className="mt-1 text-xs text-gray-500">10-character Team ID from Apple Developer</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Key ID *</label>
              <input
                type="text"
                value={(config.keyId as string) || ''}
                onChange={(e) => updateConfig('keyId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="XXXXXXXXXX"
              />
              <p className="mt-1 text-xs text-gray-500">Key ID for your Sign in with Apple private key</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Private Key (P8) *</label>
              <textarea
                value={(config.privateKey as string) || ''}
                onChange={(e) => updateConfig('privateKey', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm font-mono text-xs"
                rows={4}
                placeholder="-----BEGIN PRIVATE KEY-----&#10;...&#10;-----END PRIVATE KEY-----"
              />
              <p className="mt-1 text-xs text-gray-500">Contents of the .p8 file from Apple Developer</p>
            </div>
          </>
        );

      case ProviderTypes.LinkedIn:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>
            <SecretInput
              label="Client Secret *"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Scopes</label>
              <input
                type="text"
                value={((config.scopes as string[]) || ['openid', 'profile', 'email']).join(' ')}
                onChange={(e) => updateConfig('scopes', e.target.value.split(' ').filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="openid profile email"
              />
              <p className="mt-1 text-xs text-gray-500">LinkedIn uses OpenID Connect</p>
            </div>
          </>
        );

      case ProviderTypes.Twitter:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="OAuth 2.0 Client ID"
              />
              <p className="mt-1 text-xs text-gray-500">From Twitter/X Developer Portal</p>
            </div>
            <SecretInput
              label="Client Secret *"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Scopes</label>
              <input
                type="text"
                value={((config.scopes as string[]) || ['tweet.read', 'users.read']).join(' ')}
                onChange={(e) => updateConfig('scopes', e.target.value.split(' ').filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="tweet.read users.read"
              />
              <p className="mt-1 text-xs text-gray-500">Twitter OAuth 2.0 scopes</p>
            </div>
          </>
        );

      case ProviderTypes.Oidc:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Authority URL *</label>
              <input
                type="url"
                value={(config.authority as string) || ''}
                onChange={(e) => updateConfig('authority', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://provider.example.com"
              />
              <p className="mt-1 text-xs text-gray-500">
                Base URL for OIDC discovery (.well-known/openid-configuration)
              </p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>
            <SecretInput
              label="Client Secret"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
              helperText="Required for confidential clients"
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Scopes</label>
              <input
                type="text"
                value={((config.scopes as string[]) || ['openid', 'profile', 'email']).join(' ')}
                onChange={(e) => updateConfig('scopes', e.target.value.split(' ').filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="openid profile email"
              />
            </div>
            <div className="flex items-center">
              <input
                type="checkbox"
                id="usePkce"
                checked={(config.usePkce as boolean) ?? true}
                onChange={(e) => updateConfig('usePkce', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="usePkce" className="ml-2 text-sm text-gray-700">
                Use PKCE (recommended)
              </label>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Endpoint Overrides (optional)</h4>
            <p className="text-xs text-gray-500 mb-2">
              Override discovered endpoints. Leave blank to use values from OIDC discovery.
            </p>
            <div className="flex items-center mb-3">
              <input
                type="checkbox"
                id="disableDiscovery"
                checked={(config.disableDiscovery as boolean) || false}
                onChange={(e) => updateConfig('disableDiscovery', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="disableDiscovery" className="ml-2 text-sm text-gray-700">
                Disable OIDC discovery (manual configuration)
              </label>
            </div>
            <div className="grid grid-cols-1 gap-3">
              <div>
                <label className="block text-sm font-medium text-gray-700">Authorization Endpoint</label>
                <input
                  type="url"
                  value={(config.authorizationEndpoint as string) || ''}
                  onChange={(e) => updateConfig('authorizationEndpoint', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  placeholder="https://provider.example.com/authorize"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">Token Endpoint</label>
                <input
                  type="url"
                  value={(config.tokenEndpoint as string) || ''}
                  onChange={(e) => updateConfig('tokenEndpoint', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  placeholder="https://provider.example.com/token"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">UserInfo Endpoint</label>
                <input
                  type="url"
                  value={(config.userInfoEndpoint as string) || ''}
                  onChange={(e) => updateConfig('userInfoEndpoint', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  placeholder="https://provider.example.com/userinfo"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">JWKS URI</label>
                <input
                  type="url"
                  value={(config.jwksUri as string) || ''}
                  onChange={(e) => updateConfig('jwksUri', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  placeholder="https://provider.example.com/.well-known/jwks.json"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">End Session Endpoint</label>
                <input
                  type="url"
                  value={(config.endSessionEndpoint as string) || ''}
                  onChange={(e) => updateConfig('endSessionEndpoint', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  placeholder="https://provider.example.com/logout"
                />
              </div>
            </div>
          </>
        );

      case ProviderTypes.OAuth2:
        return (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">
                Authorization Endpoint *
              </label>
              <input
                type="url"
                value={(config.authorizationEndpoint as string) || ''}
                onChange={(e) => updateConfig('authorizationEndpoint', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://provider.example.com/oauth/authorize"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Token Endpoint *</label>
              <input
                type="url"
                value={(config.tokenEndpoint as string) || ''}
                onChange={(e) => updateConfig('tokenEndpoint', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://provider.example.com/oauth/token"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">
                User Info Endpoint
              </label>
              <input
                type="url"
                value={(config.userInfoEndpoint as string) || ''}
                onChange={(e) => updateConfig('userInfoEndpoint', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://provider.example.com/userinfo"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Client ID *</label>
              <input
                type="text"
                value={(config.clientId as string) || ''}
                onChange={(e) => updateConfig('clientId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>
            <SecretInput
              label="Client Secret"
              value={(config.clientSecret as string) || ''}
              onChange={(v) => updateConfig('clientSecret', v)}
              onReveal={!isNew ? revealSecret('clientSecret') : undefined}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Scopes</label>
              <input
                type="text"
                value={((config.scopes as string[]) || []).join(' ')}
                onChange={(e) => updateConfig('scopes', e.target.value.split(' ').filter(Boolean))}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="openid profile email"
              />
            </div>
            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Claim Mappings</h4>
            <p className="text-xs text-gray-500 mb-2">
              JSON path to extract user properties from the userinfo response
            </p>
            <div>
              <label className="block text-sm font-medium text-gray-700">User ID Claim Path</label>
              <input
                type="text"
                value={(config.userIdClaimPath as string) || 'sub'}
                onChange={(e) => updateConfig('userIdClaimPath', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="sub"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Email Claim Path</label>
              <input
                type="text"
                value={(config.emailClaimPath as string) || 'email'}
                onChange={(e) => updateConfig('emailClaimPath', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="email"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Name Claim Path</label>
              <input
                type="text"
                value={(config.nameClaimPath as string) || 'name'}
                onChange={(e) => updateConfig('nameClaimPath', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="name"
              />
            </div>
          </>
        );

      case ProviderTypes.Ldap:
        return (
          <>
            <h4 className="font-medium text-gray-900 border-b pb-2">Connection Settings</h4>
            <div>
              <label className="block text-sm font-medium text-gray-700">Server *</label>
              <input
                type="text"
                value={(config.server as string) || ''}
                onChange={(e) => updateConfig('server', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="ldap.example.com"
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700">Port</label>
                <input
                  type="number"
                  value={(config.port as number) || 389}
                  onChange={(e) => updateConfig('port', Number(e.target.value))}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                />
              </div>
              <div className="flex items-end gap-4">
                <div className="flex items-center">
                  <input
                    type="checkbox"
                    id="useSsl"
                    checked={(config.useSsl as boolean) || false}
                    onChange={(e) => updateConfig('useSsl', e.target.checked)}
                    className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                  />
                  <label htmlFor="useSsl" className="ml-2 text-sm text-gray-700">
                    Use SSL (LDAPS)
                  </label>
                </div>
              </div>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Bind Credentials</h4>
            <div>
              <label className="block text-sm font-medium text-gray-700">Bind DN</label>
              <input
                type="text"
                value={(config.bindDn as string) || ''}
                onChange={(e) => updateConfig('bindDn', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="cn=admin,dc=example,dc=com"
              />
              <p className="mt-1 text-xs text-gray-500">Service account DN for LDAP queries</p>
            </div>
            <SecretInput
              label="Bind Password"
              value={(config.bindPassword as string) || ''}
              onChange={(v) => updateConfig('bindPassword', v)}
              onReveal={!isNew ? revealSecret('bindPassword') : undefined}
            />

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Search Settings</h4>
            <div>
              <label className="block text-sm font-medium text-gray-700">Base DN *</label>
              <input
                type="text"
                value={(config.baseDn as string) || ''}
                onChange={(e) => updateConfig('baseDn', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="dc=example,dc=com"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">User Search Filter</label>
              <input
                type="text"
                value={(config.userSearchFilter as string) || '(&(objectClass=person)(uid={0}))'}
                onChange={(e) => updateConfig('userSearchFilter', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="(&(objectClass=person)(uid={0}))"
              />
              <p className="mt-1 text-xs text-gray-500">{'{0}'} is replaced with the username</p>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Attribute Mappings</h4>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700">UID Attribute</label>
                <input
                  type="text"
                  value={(config.uidAttribute as string) || 'uid'}
                  onChange={(e) => updateConfig('uidAttribute', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">Email Attribute</label>
                <input
                  type="text"
                  value={(config.emailAttribute as string) || 'mail'}
                  onChange={(e) => updateConfig('emailAttribute', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">First Name</label>
                <input
                  type="text"
                  value={(config.firstNameAttribute as string) || 'givenName'}
                  onChange={(e) => updateConfig('firstNameAttribute', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">Last Name</label>
                <input
                  type="text"
                  value={(config.lastNameAttribute as string) || 'sn'}
                  onChange={(e) => updateConfig('lastNameAttribute', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                />
              </div>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Options</h4>
            <div className="flex items-center gap-6">
              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="autoProvision"
                  checked={(config.autoProvisionUsers as boolean) ?? true}
                  onChange={(e) => updateConfig('autoProvisionUsers', e.target.checked)}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <label htmlFor="autoProvision" className="ml-2 text-sm text-gray-700">
                  Auto-provision users
                </label>
              </div>
              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="syncGroups"
                  checked={(config.syncGroupsToRoles as boolean) ?? true}
                  onChange={(e) => updateConfig('syncGroupsToRoles', e.target.checked)}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <label htmlFor="syncGroups" className="ml-2 text-sm text-gray-700">
                  Sync groups to roles
                </label>
              </div>
            </div>
          </>
        );

      case ProviderTypes.Saml2:
        return (
          <>
            <h4 className="font-medium text-gray-900 border-b pb-2">Identity Provider</h4>
            <div>
              <label className="block text-sm font-medium text-gray-700">Entity ID *</label>
              <input
                type="text"
                value={(config.entityId as string) || ''}
                onChange={(e) => updateConfig('entityId', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://idp.example.com/saml2"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">
                Metadata URL (recommended)
              </label>
              <input
                type="url"
                value={(config.metadataUrl as string) || ''}
                onChange={(e) => updateConfig('metadataUrl', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://idp.example.com/saml2/metadata"
              />
              <p className="mt-1 text-xs text-gray-500">
                Auto-discover SSO endpoints and certificates from metadata
              </p>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">
              Manual Configuration (if no metadata)
            </h4>
            <div>
              <label className="block text-sm font-medium text-gray-700">SSO Service URL</label>
              <input
                type="url"
                value={(config.singleSignOnServiceUrl as string) || ''}
                onChange={(e) => updateConfig('singleSignOnServiceUrl', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="https://idp.example.com/saml2/sso"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">SSO Binding</label>
              <select
                value={(config.singleSignOnBinding as string) || 'Redirect'}
                onChange={(e) => updateConfig('singleSignOnBinding', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              >
                <option value="Redirect">HTTP-Redirect</option>
                <option value="POST">HTTP-POST</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">
                IdP Signing Certificate (PEM)
              </label>
              <textarea
                value={(config.signingCertificate as string) || ''}
                onChange={(e) => updateConfig('signingCertificate', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm font-mono text-xs"
                rows={4}
                placeholder="-----BEGIN CERTIFICATE-----&#10;...&#10;-----END CERTIFICATE-----"
              />
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Name ID Format</h4>
            <div>
              <select
                value={(config.nameIdFormat as string) || 'unspecified'}
                onChange={(e) => updateConfig('nameIdFormat', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              >
                <option value="unspecified">Unspecified</option>
                <option value="emailAddress">Email Address</option>
                <option value="persistent">Persistent</option>
                <option value="transient">Transient</option>
              </select>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Security</h4>
            <div className="space-y-2">
              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="signRequests"
                  checked={(config.signAuthenticationRequests as boolean) ?? false}
                  onChange={(e) => updateConfig('signAuthenticationRequests', e.target.checked)}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <label htmlFor="signRequests" className="ml-2 text-sm text-gray-700">
                  Sign authentication requests
                </label>
              </div>
              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="wantSigned"
                  checked={(config.wantAssertionsSigned as boolean) ?? true}
                  onChange={(e) => updateConfig('wantAssertionsSigned', e.target.checked)}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <label htmlFor="wantSigned" className="ml-2 text-sm text-gray-700">
                  Require signed assertions
                </label>
              </div>
            </div>

            <h4 className="font-medium text-gray-900 border-b pb-2 mt-4">Options</h4>
            <div className="flex items-center">
              <input
                type="checkbox"
                id="autoProvisionSaml"
                checked={(config.autoProvisionUsers as boolean) ?? true}
                onChange={(e) => updateConfig('autoProvisionUsers', e.target.checked)}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="autoProvisionSaml" className="ml-2 text-sm text-gray-700">
                Auto-provision users on first login
              </label>
            </div>
          </>
        );

      default:
        return (
          <div className="text-sm text-gray-500">
            Select a provider type to configure its settings.
          </div>
        );
    }
  };

  if (isNew) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/identity-providers')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <h1 className="text-2xl font-bold text-gray-900">Add Identity Provider</h1>
        </div>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <Card>
            <CardHeader title="Basic Information" />
            <div className="mt-4 space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700">Provider Type *</label>
                <select
                  value={formData.providerType !== undefined ? String(formData.providerType) : ''}
                  onChange={(e) => {
                    const value = e.target.value;
                    if (value === '') {
                      setFormData({ ...formData, providerType: undefined, configuration: {} });
                    } else {
                      // API returns enum as number, parse it directly
                      const numericValue = parseInt(value, 10) as ExternalProviderType;
                      setFormData({ ...formData, providerType: numericValue, configuration: {} });
                    }
                  }}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                >
                  <option value="">Select a provider type...</option>
                  {providerTypes?.map((type) => (
                    <option key={type.type} value={type.type}>
                      {type.name}
                    </option>
                  ))}
                </select>
                {selectedTypeInfo && (
                  <p className="mt-1 text-xs text-gray-500">{selectedTypeInfo.description}</p>
                )}
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700">Scheme Name *</label>
                <div className="relative">
                  <input
                    type="text"
                    value={formData.scheme}
                    onChange={(e) =>
                      setFormData({ ...formData, scheme: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, '-') })
                    }
                    className={`mt-1 block w-full rounded-md shadow-sm focus:ring-blue-500 sm:text-sm pr-10 ${
                      formData.scheme.length >= 2 && schemeAvailability
                        ? schemeAvailability.available
                          ? 'border-green-300 focus:border-green-500'
                          : 'border-red-300 focus:border-red-500'
                        : 'border-gray-300 focus:border-blue-500'
                    }`}
                    placeholder="e.g., google, corporate-sso"
                  />
                  {formData.scheme.length >= 2 && (
                    <div className="absolute inset-y-0 right-0 flex items-center pr-3 mt-1">
                      {isCheckingScheme ? (
                        <svg className="animate-spin h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                        </svg>
                      ) : schemeAvailability?.available ? (
                        <CheckCircleIcon className="h-5 w-5 text-green-500" />
                      ) : schemeAvailability ? (
                        <ExclamationCircleIcon className="h-5 w-5 text-red-500" />
                      ) : null}
                    </div>
                  )}
                </div>
                {formData.scheme.length >= 2 && schemeAvailability && !schemeAvailability.available ? (
                  <p className="mt-1 text-xs text-red-600">{schemeAvailability.message || 'This scheme name is unavailable'}</p>
                ) : (
                  <p className="mt-1 text-xs text-gray-500">
                    Unique identifier (lowercase, hyphens allowed)
                  </p>
                )}
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700">Display Name</label>
                <input
                  type="text"
                  value={formData.displayName || ''}
                  onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                  placeholder="e.g., Sign in with Google"
                />
              </div>

              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="enabled"
                  checked={formData.enabled ?? true}
                  onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <label htmlFor="enabled" className="ml-2 text-sm text-gray-700">
                  Enabled
                </label>
              </div>
            </div>
          </Card>

          <Card>
            <CardHeader title="Provider Configuration" />
            <div className="mt-4 space-y-4">{renderConfigFields()}</div>
          </Card>
        </div>

        {selectedType !== null && selectedType !== ProviderTypes.Ldap && (
          <Card>
            <CardHeader title="Proxy Mode (Federation Broker)" />
            <div className="mt-4 space-y-4">{renderProxyModeSettings()}</div>
          </Card>
        )}

        {selectedType !== null && (
          <Card>
            <CardHeader
              title={
                <span className="flex items-center gap-2">
                  <InformationCircleIcon className="h-5 w-5 text-blue-500" />
                  Setup Instructions
                </span>
              }
            />
            <div className="mt-4">
              {renderSetupHelp()}
            </div>
          </Card>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={() => navigate('/identity-providers')}>
            Cancel
          </Button>
          <Button
            onClick={handleSave}
            disabled={
              !formData.scheme ||
              selectedType === null ||
              createMutation.isPending ||
              (schemeAvailability && !schemeAvailability.available)
            }
          >
            {createMutation.isPending ? 'Creating...' : 'Create Provider'}
          </Button>
        </div>
      </div>
    );
  }

  if (isLoading) {
    return <div className="flex items-center justify-center h-64">Loading...</div>;
  }

  if (!provider) {
    return <div className="text-center py-8 text-gray-500">Provider not found</div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/identity-providers')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {provider.displayName || provider.scheme}
            </h1>
            <p className="text-sm text-gray-500">
              {getProviderTypeName(provider.providerType)} &middot; {provider.scheme}
            </p>
          </div>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => testMutation.mutate()}>
            <PlayIcon className="h-4 w-4 mr-2" />
            Test Connection
          </Button>
          <Button
            variant="danger"
            onClick={() => setShowDeleteModal(true)}
            disabled={provider.nonEditable}
            title={provider.nonEditable ? 'This provider cannot be deleted' : undefined}
          >
            Delete
          </Button>
        </div>
      </div>

      {provider.nonEditable && (
        <div className="flex items-center gap-2 p-4 rounded-md bg-yellow-50 text-yellow-800">
          <ExclamationCircleIcon className="h-5 w-5" />
          This provider is marked as non-editable and cannot be modified.
        </div>
      )}

      {testResult && (
        <div
          className={`flex items-center gap-2 p-4 rounded-md ${
            testResult.success ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'
          }`}
        >
          {testResult.success ? (
            <CheckCircleIcon className="h-5 w-5" />
          ) : (
            <XCircleIcon className="h-5 w-5" />
          )}
          {testResult.message}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader title="Basic Information" />
          <div className="mt-4 space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Display Name</label>
              <input
                type="text"
                value={formData.displayName || ''}
                onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>

            <div className="flex items-center">
              <input
                type="checkbox"
                id="enabled"
                checked={formData.enabled ?? true}
                onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <label htmlFor="enabled" className="ml-2 text-sm text-gray-700">
                Enabled
              </label>
            </div>

            <div>
              <dt className="text-sm font-medium text-gray-500">Created</dt>
              <dd className="mt-1 text-sm text-gray-900">
                {new Date(provider.created).toLocaleString()}
              </dd>
            </div>
            {provider.lastAccessed && (
              <div>
                <dt className="text-sm font-medium text-gray-500">Last Used</dt>
                <dd className="mt-1 text-sm text-gray-900">
                  {new Date(provider.lastAccessed).toLocaleString()}
                </dd>
              </div>
            )}
          </div>
        </Card>

        <Card>
          <CardHeader title="Provider Configuration" />
          <div className="mt-4 space-y-4">{renderConfigFields()}</div>
        </Card>
      </div>

      {provider.providerType !== ProviderTypes.Ldap && (
        <Card>
          <CardHeader title="Proxy Mode (Federation Broker)" />
          <div className="mt-4 space-y-4">{renderProxyModeSettings()}</div>
        </Card>
      )}

      <Card>
        <CardHeader
          title={
            <span className="flex items-center gap-2">
              <InformationCircleIcon className="h-5 w-5 text-blue-500" />
              Setup Instructions
            </span>
          }
        />
        <div className="mt-4">
          {renderSetupHelp()}
        </div>
      </Card>

      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={() => navigate('/identity-providers')}>
          Cancel
        </Button>
        <Button
          onClick={handleSave}
          disabled={updateMutation.isPending || provider.nonEditable}
          title={provider.nonEditable ? 'This provider cannot be modified' : undefined}
        >
          {updateMutation.isPending ? 'Saving...' : 'Save Changes'}
        </Button>
      </div>

      <Modal
        isOpen={showDeleteModal}
        onClose={() => setShowDeleteModal(false)}
        title="Delete Identity Provider"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Are you sure you want to delete <strong>{provider.displayName || provider.scheme}</strong>?
            Users will no longer be able to sign in using this provider.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setShowDeleteModal(false)}>
              Cancel
            </Button>
            <Button variant="danger" onClick={() => deleteMutation.mutate()}>
              {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

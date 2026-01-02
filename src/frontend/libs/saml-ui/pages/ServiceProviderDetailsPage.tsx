import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  ArrowLeftIcon,
  CheckCircleIcon,
  XCircleIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline';
import {
  samlApi,
  type CreateSamlServiceProviderRequest,
  type UpdateSamlServiceProviderRequest,
} from '../api/samlApi';

const NAME_ID_FORMATS = [
  { value: '', label: 'Default (emailAddress)' },
  { value: 'urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress', label: 'Email Address' },
  { value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:persistent', label: 'Persistent' },
  { value: 'urn:oasis:names:tc:SAML:2.0:nameid-format:transient', label: 'Transient' },
  { value: 'urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified', label: 'Unspecified' },
];

const SSO_BINDINGS = [
  { value: 'POST', label: 'HTTP-POST' },
  { value: 'Redirect', label: 'HTTP-Redirect' },
];

interface FormData {
  entityId: string;
  displayName: string;
  description: string;
  enabled: boolean;
  metadataUrl: string;
  assertionConsumerServiceUrl: string;
  singleLogoutServiceUrl: string;
  signingCertificate: string;
  encryptionCertificate: string;
  encryptAssertions: boolean;
  nameIdFormat: string;
  allowedClaims: string;
  claimMappings: string;
  ssoBinding: string;
  signResponses: boolean;
  signAssertions: boolean;
  requireSignedAuthnRequests: boolean;
  defaultRelayState: string;
}

const defaultFormData: FormData = {
  entityId: '',
  displayName: '',
  description: '',
  enabled: true,
  metadataUrl: '',
  assertionConsumerServiceUrl: '',
  singleLogoutServiceUrl: '',
  signingCertificate: '',
  encryptionCertificate: '',
  encryptAssertions: false,
  nameIdFormat: '',
  allowedClaims: '',
  claimMappings: '',
  ssoBinding: 'POST',
  signResponses: true,
  signAssertions: true,
  requireSignedAuthnRequests: false,
  defaultRelayState: '',
};

export default function ServiceProviderDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isNew = !id || id === 'new';

  const [formData, setFormData] = useState<FormData>(defaultFormData);
  const [loading, setLoading] = useState(!isNew);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [entityIdError, setEntityIdError] = useState<string | null>(null);
  const [checkingEntityId, setCheckingEntityId] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [nonEditable, setNonEditable] = useState(false);

  // Load existing SP data
  useEffect(() => {
    if (!isNew && id) {
      loadServiceProvider(parseInt(id));
    }
  }, [id, isNew]);

  const loadServiceProvider = async (spId: number) => {
    try {
      setLoading(true);
      const sp = await samlApi.getForEdit(spId);
      setFormData({
        entityId: sp.entityId,
        displayName: sp.displayName || '',
        description: sp.description || '',
        enabled: sp.enabled,
        metadataUrl: sp.metadataUrl || '',
        assertionConsumerServiceUrl: sp.assertionConsumerServiceUrl || '',
        singleLogoutServiceUrl: sp.singleLogoutServiceUrl || '',
        signingCertificate: sp.signingCertificate || '',
        encryptionCertificate: sp.encryptionCertificate || '',
        encryptAssertions: sp.encryptAssertions,
        nameIdFormat: sp.nameIdFormat || '',
        allowedClaims: sp.allowedClaims?.join(', ') || '',
        claimMappings: sp.claimMappings
          ? Object.entries(sp.claimMappings)
              .map(([k, v]) => `${k}=${v}`)
              .join('\n')
          : '',
        ssoBinding: sp.ssoBinding,
        signResponses: sp.signResponses,
        signAssertions: sp.signAssertions,
        requireSignedAuthnRequests: sp.requireSignedAuthnRequests,
        defaultRelayState: sp.defaultRelayState || '',
      });
      setNonEditable(sp.nonEditable);
      setError(null);
    } catch (err) {
      setError('Failed to load service provider');
      console.error('Error loading service provider:', err);
    } finally {
      setLoading(false);
    }
  };

  // Check Entity ID availability with debounce
  const checkEntityIdAvailability = useCallback(
    async (entityId: string) => {
      if (!entityId || (!isNew && !id)) return;

      setCheckingEntityId(true);
      try {
        const result = await samlApi.checkEntityIdAvailability(
          entityId,
          isNew ? undefined : parseInt(id!)
        );
        if (!result.available) {
          setEntityIdError(result.message || 'Entity ID is already in use');
        } else {
          setEntityIdError(null);
        }
      } catch (err) {
        console.error('Error checking entity ID:', err);
      } finally {
        setCheckingEntityId(false);
      }
    },
    [isNew, id]
  );

  useEffect(() => {
    if (isNew && formData.entityId) {
      const timer = setTimeout(() => {
        checkEntityIdAvailability(formData.entityId);
      }, 500);
      return () => clearTimeout(timer);
    }
  }, [formData.entityId, isNew, checkEntityIdAvailability]);

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>
  ) => {
    const { name, value, type } = e.target;
    const checked = (e.target as HTMLInputElement).checked;

    setFormData((prev) => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value,
    }));
  };

  const parseAllowedClaims = (value: string): string[] | undefined => {
    if (!value.trim()) return undefined;
    return value
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s);
  };

  const parseClaimMappings = (value: string): Record<string, string> | undefined => {
    if (!value.trim()) return undefined;
    const mappings: Record<string, string> = {};
    for (const line of value.split('\n')) {
      const [key, val] = line.split('=').map((s) => s.trim());
      if (key && val) {
        mappings[key] = val;
      }
    }
    return Object.keys(mappings).length > 0 ? mappings : undefined;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (entityIdError) {
      return;
    }

    try {
      setSaving(true);
      setError(null);

      if (isNew) {
        const request: CreateSamlServiceProviderRequest = {
          entityId: formData.entityId,
          displayName: formData.displayName || undefined,
          description: formData.description || undefined,
          enabled: formData.enabled,
          metadataUrl: formData.metadataUrl || undefined,
          assertionConsumerServiceUrl: formData.assertionConsumerServiceUrl || undefined,
          singleLogoutServiceUrl: formData.singleLogoutServiceUrl || undefined,
          signingCertificate: formData.signingCertificate || undefined,
          encryptionCertificate: formData.encryptionCertificate || undefined,
          encryptAssertions: formData.encryptAssertions,
          nameIdFormat: formData.nameIdFormat || undefined,
          allowedClaims: parseAllowedClaims(formData.allowedClaims),
          claimMappings: parseClaimMappings(formData.claimMappings),
          ssoBinding: formData.ssoBinding,
          signResponses: formData.signResponses,
          signAssertions: formData.signAssertions,
          requireSignedAuthnRequests: formData.requireSignedAuthnRequests,
          defaultRelayState: formData.defaultRelayState || undefined,
        };

        const created = await samlApi.create(request);
        navigate(`/saml/service-providers/${created.id}`);
      } else {
        const request: UpdateSamlServiceProviderRequest = {
          displayName: formData.displayName || undefined,
          description: formData.description || undefined,
          enabled: formData.enabled,
          metadataUrl: formData.metadataUrl || undefined,
          assertionConsumerServiceUrl: formData.assertionConsumerServiceUrl || undefined,
          singleLogoutServiceUrl: formData.singleLogoutServiceUrl || undefined,
          signingCertificate: formData.signingCertificate || undefined,
          encryptionCertificate: formData.encryptionCertificate || undefined,
          encryptAssertions: formData.encryptAssertions,
          nameIdFormat: formData.nameIdFormat || undefined,
          allowedClaims: parseAllowedClaims(formData.allowedClaims),
          claimMappings: parseClaimMappings(formData.claimMappings),
          ssoBinding: formData.ssoBinding,
          signResponses: formData.signResponses,
          signAssertions: formData.signAssertions,
          requireSignedAuthnRequests: formData.requireSignedAuthnRequests,
          defaultRelayState: formData.defaultRelayState || undefined,
        };

        await samlApi.update(parseInt(id!), request);
      }

      navigate('/saml/service-providers');
    } catch (err: any) {
      setError(err.response?.data?.error || 'Failed to save service provider');
      console.error('Error saving service provider:', err);
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    if (isNew || !id) return;

    try {
      setTestResult(null);
      const result = await samlApi.test(parseInt(id));
      setTestResult(result);
    } catch (err) {
      setTestResult({ success: false, message: 'Failed to run test' });
    }
  };

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="h-8 w-48 bg-gray-200 rounded animate-pulse" />
        <div className="bg-white shadow rounded-lg p-6 space-y-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="h-12 bg-gray-200 rounded animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center space-x-4">
        <Link
          to="/saml/service-providers"
          className="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded"
        >
          <ArrowLeftIcon className="h-5 w-5" />
        </Link>
        <h1 className="text-2xl font-bold text-gray-900">
          {isNew ? 'Add Service Provider' : 'Edit Service Provider'}
        </h1>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <div className="flex items-center space-x-2">
            <XCircleIcon className="h-5 w-5 text-red-600" />
            <span className="text-red-700">{error}</span>
          </div>
        </div>
      )}

      {nonEditable && (
        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <div className="flex items-center space-x-2">
            <ExclamationTriangleIcon className="h-5 w-5 text-yellow-600" />
            <span className="text-yellow-700">
              This service provider is system-managed and cannot be modified.
            </span>
          </div>
        </div>
      )}

      {testResult && (
        <div
          className={`rounded-lg p-4 ${
            testResult.success
              ? 'bg-green-50 border border-green-200'
              : 'bg-red-50 border border-red-200'
          }`}
        >
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              {testResult.success ? (
                <CheckCircleIcon className="h-5 w-5 text-green-600" />
              ) : (
                <XCircleIcon className="h-5 w-5 text-red-600" />
              )}
              <span className={testResult.success ? 'text-green-700' : 'text-red-700'}>
                {testResult.message}
              </span>
            </div>
            <button onClick={() => setTestResult(null)} className="text-gray-400 hover:text-gray-600">
              &times;
            </button>
          </div>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-6">
        {/* Basic Information */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Basic Information</h2>
          </div>
          <div className="px-6 py-4 space-y-4">
            <div>
              <label htmlFor="entityId" className="block text-sm font-medium text-gray-700">
                Entity ID <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                id="entityId"
                name="entityId"
                value={formData.entityId}
                onChange={handleChange}
                disabled={!isNew || nonEditable}
                required
                className={`mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 ${
                  entityIdError ? 'border-red-500' : ''
                } ${!isNew ? 'bg-gray-100' : ''}`}
                placeholder="https://sp.example.com/saml"
              />
              {checkingEntityId && (
                <p className="mt-1 text-sm text-gray-500">Checking availability...</p>
              )}
              {entityIdError && <p className="mt-1 text-sm text-red-600">{entityIdError}</p>}
              <p className="mt-1 text-xs text-gray-500">
                The unique SAML Entity ID for this Service Provider
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label htmlFor="displayName" className="block text-sm font-medium text-gray-700">
                  Display Name
                </label>
                <input
                  type="text"
                  id="displayName"
                  name="displayName"
                  value={formData.displayName}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="My Application"
                />
              </div>

              <div>
                <label htmlFor="enabled" className="block text-sm font-medium text-gray-700">
                  Status
                </label>
                <div className="mt-2">
                  <label className="inline-flex items-center">
                    <input
                      type="checkbox"
                      id="enabled"
                      name="enabled"
                      checked={formData.enabled}
                      onChange={handleChange}
                      disabled={nonEditable}
                      className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                    />
                    <span className="ml-2 text-sm text-gray-700">Enabled</span>
                  </label>
                </div>
              </div>
            </div>

            <div>
              <label htmlFor="description" className="block text-sm font-medium text-gray-700">
                Description
              </label>
              <textarea
                id="description"
                name="description"
                value={formData.description}
                onChange={handleChange}
                disabled={nonEditable}
                rows={2}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                placeholder="Optional description of this service provider"
              />
            </div>
          </div>
        </div>

        {/* Endpoints */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Endpoints</h2>
            <p className="mt-1 text-sm text-gray-500">
              You can provide a metadata URL to auto-configure, or manually specify endpoints
            </p>
          </div>
          <div className="px-6 py-4 space-y-4">
            <div>
              <label htmlFor="metadataUrl" className="block text-sm font-medium text-gray-700">
                Metadata URL
              </label>
              <input
                type="url"
                id="metadataUrl"
                name="metadataUrl"
                value={formData.metadataUrl}
                onChange={handleChange}
                disabled={nonEditable}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                placeholder="https://sp.example.com/saml/metadata"
              />
              <p className="mt-1 text-xs text-gray-500">
                If provided, endpoints and certificates will be fetched from metadata
              </p>
            </div>

            <div>
              <label
                htmlFor="assertionConsumerServiceUrl"
                className="block text-sm font-medium text-gray-700"
              >
                Assertion Consumer Service (ACS) URL
              </label>
              <input
                type="url"
                id="assertionConsumerServiceUrl"
                name="assertionConsumerServiceUrl"
                value={formData.assertionConsumerServiceUrl}
                onChange={handleChange}
                disabled={nonEditable}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                placeholder="https://sp.example.com/saml/acs"
              />
              <p className="mt-1 text-xs text-gray-500">
                Where SAML responses should be sent (required if no metadata URL)
              </p>
            </div>

            <div>
              <label
                htmlFor="singleLogoutServiceUrl"
                className="block text-sm font-medium text-gray-700"
              >
                Single Logout Service URL
              </label>
              <input
                type="url"
                id="singleLogoutServiceUrl"
                name="singleLogoutServiceUrl"
                value={formData.singleLogoutServiceUrl}
                onChange={handleChange}
                disabled={nonEditable}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                placeholder="https://sp.example.com/saml/slo"
              />
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label htmlFor="ssoBinding" className="block text-sm font-medium text-gray-700">
                  SSO Binding
                </label>
                <select
                  id="ssoBinding"
                  name="ssoBinding"
                  value={formData.ssoBinding}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                >
                  {SSO_BINDINGS.map((b) => (
                    <option key={b.value} value={b.value}>
                      {b.label}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label htmlFor="nameIdFormat" className="block text-sm font-medium text-gray-700">
                  Name ID Format
                </label>
                <select
                  id="nameIdFormat"
                  name="nameIdFormat"
                  value={formData.nameIdFormat}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                >
                  {NAME_ID_FORMATS.map((f) => (
                    <option key={f.value} value={f.value}>
                      {f.label}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          </div>
        </div>

        {/* Security */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Security</h2>
          </div>
          <div className="px-6 py-4 space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <label className="flex items-center">
                <input
                  type="checkbox"
                  name="signResponses"
                  checked={formData.signResponses}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
                <span className="ml-2 text-sm text-gray-700">Sign SAML Responses</span>
              </label>

              <label className="flex items-center">
                <input
                  type="checkbox"
                  name="signAssertions"
                  checked={formData.signAssertions}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
                <span className="ml-2 text-sm text-gray-700">Sign SAML Assertions</span>
              </label>

              <label className="flex items-center">
                <input
                  type="checkbox"
                  name="requireSignedAuthnRequests"
                  checked={formData.requireSignedAuthnRequests}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
                <span className="ml-2 text-sm text-gray-700">Require Signed AuthnRequests</span>
              </label>

              <label className="flex items-center">
                <input
                  type="checkbox"
                  name="encryptAssertions"
                  checked={formData.encryptAssertions}
                  onChange={handleChange}
                  disabled={nonEditable}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
                <span className="ml-2 text-sm text-gray-700">Encrypt Assertions</span>
              </label>
            </div>

            <div>
              <label htmlFor="signingCertificate" className="block text-sm font-medium text-gray-700">
                SP Signing Certificate (Base64)
              </label>
              <textarea
                id="signingCertificate"
                name="signingCertificate"
                value={formData.signingCertificate}
                onChange={handleChange}
                disabled={nonEditable}
                rows={3}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 font-mono text-xs"
                placeholder="MIIDx..."
              />
              <p className="mt-1 text-xs text-gray-500">
                Public certificate for verifying signed AuthnRequests from this SP
              </p>
            </div>

            <div>
              <label
                htmlFor="encryptionCertificate"
                className="block text-sm font-medium text-gray-700"
              >
                SP Encryption Certificate (Base64)
              </label>
              <textarea
                id="encryptionCertificate"
                name="encryptionCertificate"
                value={formData.encryptionCertificate}
                onChange={handleChange}
                disabled={nonEditable}
                rows={3}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 font-mono text-xs"
                placeholder="MIIDx..."
              />
              <p className="mt-1 text-xs text-gray-500">
                Public certificate for encrypting assertions sent to this SP
              </p>
            </div>
          </div>
        </div>

        {/* Claims */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Claims Configuration</h2>
          </div>
          <div className="px-6 py-4 space-y-4">
            <div>
              <label htmlFor="allowedClaims" className="block text-sm font-medium text-gray-700">
                Allowed Claims
              </label>
              <input
                type="text"
                id="allowedClaims"
                name="allowedClaims"
                value={formData.allowedClaims}
                onChange={handleChange}
                disabled={nonEditable}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                placeholder="email, name, given_name, family_name"
              />
              <p className="mt-1 text-xs text-gray-500">
                Comma-separated list of claim types to include in assertions (empty = all claims)
              </p>
            </div>

            <div>
              <label htmlFor="claimMappings" className="block text-sm font-medium text-gray-700">
                Claim Mappings
              </label>
              <textarea
                id="claimMappings"
                name="claimMappings"
                value={formData.claimMappings}
                onChange={handleChange}
                disabled={nonEditable}
                rows={4}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 font-mono text-sm"
                placeholder="http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress=email&#10;http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name=name"
              />
              <p className="mt-1 text-xs text-gray-500">
                Map SAML attribute names to claim types (one per line: samlAttribute=claimType)
              </p>
            </div>

            <div>
              <label htmlFor="defaultRelayState" className="block text-sm font-medium text-gray-700">
                Default RelayState
              </label>
              <input
                type="text"
                id="defaultRelayState"
                name="defaultRelayState"
                value={formData.defaultRelayState}
                onChange={handleChange}
                disabled={nonEditable}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                placeholder="https://sp.example.com/dashboard"
              />
              <p className="mt-1 text-xs text-gray-500">
                Default redirect URL if the SP doesn't provide a RelayState
              </p>
            </div>
          </div>
        </div>

        {/* Actions */}
        <div className="flex items-center justify-between">
          <div>
            {!isNew && (
              <button
                type="button"
                onClick={handleTest}
                className="inline-flex items-center px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
              >
                Test Configuration
              </button>
            )}
          </div>
          <div className="flex items-center space-x-4">
            <Link
              to="/saml/service-providers"
              className="px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
            >
              Cancel
            </Link>
            <button
              type="submit"
              disabled={saving || nonEditable || !!entityIdError}
              className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {saving ? 'Saving...' : isNew ? 'Create Service Provider' : 'Save Changes'}
            </button>
          </div>
        </div>
      </form>
    </div>
  );
}

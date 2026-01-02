import { useEffect, useState, useRef } from 'react';
import {
  CheckCircleIcon,
  ExclamationCircleIcon,
  InformationCircleIcon,
  ArrowPathIcon,
  ArrowUpTrayIcon,
  ShieldCheckIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline';
import type { SettingsTabProps } from '@oluso/ui-core';
import { samlApi, type SamlIdpConfiguration, type IdpInfo, type SamlCertificateInfo } from '../api/samlApi';

export default function SamlIdpSettingsTab({
  onSave,
  onHasChanges,
  isActive,
}: SettingsTabProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [hasChanges, setHasChanges] = useState(false);

  const [config, setConfig] = useState<SamlIdpConfiguration>({
    enabled: false,
    loginJourneyName: undefined,
  });
  const [idpInfo, setIdpInfo] = useState<IdpInfo | null>(null);
  const [signingCert, setSigningCert] = useState<SamlCertificateInfo | null>(null);
  const [encryptionCert, setEncryptionCert] = useState<SamlCertificateInfo | null>(null);
  const [certLoading, setCertLoading] = useState<'signing' | 'encryption' | null>(null);

  const loadCertificates = async () => {
    try {
      const [signing, encryption] = await Promise.all([
        samlApi.getSigningCertificate().catch(() => null),
        samlApi.getEncryptionCertificate().catch(() => null),
      ]);
      setSigningCert(signing);
      setEncryptionCert(encryption);
    } catch (err) {
      console.error('Error loading certificates:', err);
    }
  };

  useEffect(() => {
    if (!isActive) return;

    async function loadData() {
      try {
        setLoading(true);
        const [configData, info] = await Promise.all([
          samlApi.getIdpConfiguration(),
          samlApi.getIdpInfo().catch(() => null),
        ]);
        setConfig(configData);
        setIdpInfo(info);
        await loadCertificates();
        setError(null);
      } catch (err) {
        setError('Failed to load SAML IdP configuration');
        console.error('Error loading SAML IdP config:', err);
      } finally {
        setLoading(false);
      }
    }
    loadData();
  }, [isActive]);

  const handleChange = (key: keyof SamlIdpConfiguration, value: boolean | string | undefined) => {
    setConfig((prev) => ({ ...prev, [key]: value }));
    setHasChanges(true);
    setSuccess(null);
    onHasChanges?.(true);
  };

  const handleSave = async () => {
    try {
      setSaving(true);
      setError(null);
      await samlApi.updateIdpConfiguration(config);
      setSuccess('SAML IdP settings saved successfully');
      setHasChanges(false);
      onHasChanges?.(false);
      onSave?.();
    } catch (err) {
      setError('Failed to save SAML IdP configuration');
      console.error('Error saving SAML IdP config:', err);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="h-8 w-64 bg-gray-200 rounded animate-pulse" />
        <div className="h-48 bg-gray-200 rounded-lg animate-pulse" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ExclamationCircleIcon className="h-5 w-5 text-red-500" />
          <p className="text-red-700">{error}</p>
        </div>
      )}

      {success && (
        <div className="bg-green-50 border border-green-200 rounded-lg p-4 flex items-center space-x-2">
          <CheckCircleIcon className="h-5 w-5 text-green-500" />
          <p className="text-green-700">{success}</p>
        </div>
      )}

      {/* SAML IdP Settings */}
      <div className="space-y-6">
        <div>
          <h3 className="text-lg font-medium text-gray-900">SAML IdP Configuration</h3>
          <p className="mt-1 text-sm text-gray-500">
            Enable and configure SAML Identity Provider functionality for this tenant
          </p>
        </div>

        {/* Enable Toggle */}
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-gray-700">Enable SAML IdP</p>
            <p className="text-sm text-gray-500">
              Allow external Service Providers to authenticate users via SAML
            </p>
          </div>
          <button
            type="button"
            onClick={() => handleChange('enabled', !config.enabled)}
            className={`relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 ${
              config.enabled ? 'bg-primary-600' : 'bg-gray-200'
            }`}
            role="switch"
            aria-checked={config.enabled}
          >
            <span
              className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out ${
                config.enabled ? 'translate-x-5' : 'translate-x-0'
              }`}
            />
          </button>
        </div>

        {/* Login Journey Name */}
        <div>
          <label htmlFor="loginJourneyName" className="block text-sm font-medium text-gray-700">
            Login Journey Name
          </label>
          <input
            type="text"
            id="loginJourneyName"
            value={config.loginJourneyName || ''}
            onChange={(e) => handleChange('loginJourneyName', e.target.value || undefined)}
            placeholder="default-login"
            disabled={!config.enabled}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500"
          />
          <p className="mt-2 text-sm text-gray-500">
            Journey to use for SAML SSO authentication. Leave empty to use the standalone login page at{' '}
            <code className="text-xs bg-gray-100 px-1 py-0.5 rounded">/account/login</code>.
          </p>
        </div>

        {/* Info Box */}
        <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div className="flex">
            <InformationCircleIcon className="h-5 w-5 text-blue-500 flex-shrink-0" />
            <div className="ml-3">
              <h4 className="text-sm font-medium text-blue-800">How SAML IdP Authentication Works</h4>
              <p className="mt-1 text-sm text-blue-700">
                When a Service Provider sends an authentication request, users will be redirected to
                the configured login journey (or the standalone login page if not specified). After
                authentication, users are returned to the Service Provider with a SAML assertion.
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* IdP Endpoints Info - Only show when enabled */}
      {config.enabled && idpInfo && (
        <div className="space-y-4">
          <div>
            <h3 className="text-lg font-medium text-gray-900">IdP Endpoints</h3>
            <p className="mt-1 text-sm text-gray-500">
              Share these URLs with Service Providers to configure SSO
            </p>
          </div>
          <div className="space-y-3">
            <EndpointRow label="Metadata URL" value={idpInfo.metadataUrl} />
            <EndpointRow label="SSO Endpoint" value={idpInfo.singleSignOnServiceUrl} />
            <EndpointRow label="SLO Endpoint" value={idpInfo.singleLogoutServiceUrl} />
            <EndpointRow label="Entity ID" value={idpInfo.entityId} />
          </div>
        </div>
      )}

      {/* Endpoints Preview - Show when enabled but IdP info not available */}
      {config.enabled && !idpInfo && (
        <div className="space-y-4">
          <div>
            <h3 className="text-lg font-medium text-gray-900">IdP Endpoints</h3>
            <p className="mt-1 text-sm text-gray-500">
              These endpoints will be available once SAML IdP is enabled
            </p>
          </div>
          <div className="space-y-3">
            <EndpointRow label="Metadata" value="/saml/idp/metadata" />
            <EndpointRow label="SSO Endpoint" value="/saml/idp/sso" />
            <EndpointRow label="SLO Endpoint" value="/saml/idp/slo" />
          </div>
        </div>
      )}

      {/* IdP Certificates - Show when enabled */}
      {config.enabled && (
        <div className="space-y-4">
          <div>
            <h3 className="text-lg font-medium text-gray-900">IdP Certificates</h3>
            <p className="mt-1 text-sm text-gray-500">
              Configure certificates for signing SAML assertions and decrypting requests
            </p>
          </div>

          {/* Signing Certificate */}
          <CertificateCard
            title="Signing Certificate"
            description="Used to sign SAML assertions sent to Service Providers"
            certInfo={signingCert}
            loading={certLoading === 'signing'}
            onGenerate={async () => {
              setCertLoading('signing');
              try {
                const cert = await samlApi.generateSigningCertificate();
                setSigningCert(cert);
                setSuccess('Signing certificate generated successfully');
              } catch (err) {
                setError('Failed to generate signing certificate');
              } finally {
                setCertLoading(null);
              }
            }}
            onUpload={async (base64Pfx: string, password?: string) => {
              setCertLoading('signing');
              try {
                const cert = await samlApi.uploadSigningCertificate({ base64Pfx, password });
                setSigningCert(cert);
                setSuccess('Signing certificate uploaded successfully');
              } catch (err) {
                setError('Failed to upload signing certificate');
              } finally {
                setCertLoading(null);
              }
            }}
            onReset={async () => {
              setCertLoading('signing');
              try {
                await samlApi.resetSigningCertificate();
                await loadCertificates();
                setSuccess('Signing certificate reset to global');
              } catch (err) {
                setError('Failed to reset signing certificate');
              } finally {
                setCertLoading(null);
              }
            }}
          />

          {/* Encryption Certificate */}
          <CertificateCard
            title="Encryption Certificate"
            description="Used to decrypt incoming encrypted SAML requests (optional)"
            certInfo={encryptionCert}
            loading={certLoading === 'encryption'}
            onGenerate={async () => {
              setCertLoading('encryption');
              try {
                const cert = await samlApi.generateEncryptionCertificate();
                setEncryptionCert(cert);
                setSuccess('Encryption certificate generated successfully');
              } catch (err) {
                setError('Failed to generate encryption certificate');
              } finally {
                setCertLoading(null);
              }
            }}
            onUpload={async (base64Pfx: string, password?: string) => {
              setCertLoading('encryption');
              try {
                const cert = await samlApi.uploadEncryptionCertificate({ base64Pfx, password });
                setEncryptionCert(cert);
                setSuccess('Encryption certificate uploaded successfully');
              } catch (err) {
                setError('Failed to upload encryption certificate');
              } finally {
                setCertLoading(null);
              }
            }}
            onReset={async () => {
              setCertLoading('encryption');
              try {
                await samlApi.resetEncryptionCertificate();
                await loadCertificates();
                setSuccess('Encryption certificate reset to global');
              } catch (err) {
                setError('Failed to reset encryption certificate');
              } finally {
                setCertLoading(null);
              }
            }}
          />
        </div>
      )}

      {/* Save Button */}
      <div className="flex justify-end items-center gap-4 pt-4 border-t border-gray-200">
        {hasChanges && (
          <span className="text-sm text-gray-500">You have unsaved changes</span>
        )}
        <button
          onClick={handleSave}
          disabled={!hasChanges || saving}
          className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {saving ? 'Saving...' : 'Save Settings'}
        </button>
      </div>
    </div>
  );
}

interface EndpointRowProps {
  label: string;
  value: string;
}

function EndpointRow({ label, value }: EndpointRowProps) {
  const [copied, setCopied] = useState(false);

  const copyToClipboard = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  return (
    <div className="flex items-center justify-between">
      <div className="flex-1 min-w-0">
        <span className="text-sm font-medium text-gray-500">{label}:</span>
        <code className="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">
          {value}
        </code>
      </div>
      <button
        onClick={copyToClipboard}
        className="ml-4 text-sm text-primary-600 hover:text-primary-700"
      >
        {copied ? 'Copied!' : 'Copy'}
      </button>
    </div>
  );
}

interface CertificateCardProps {
  title: string;
  description: string;
  certInfo: SamlCertificateInfo | null;
  loading: boolean;
  onGenerate: () => Promise<void>;
  onUpload: (base64Pfx: string, password?: string) => Promise<void>;
  onReset: () => Promise<void>;
}

function CertificateCard({
  title,
  description,
  certInfo,
  loading,
  onGenerate,
  onUpload,
  onReset,
}: CertificateCardProps) {
  const [showUpload, setShowUpload] = useState(false);
  const [password, setPassword] = useState('');
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFileSelect = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    try {
      setUploading(true);
      const buffer = await file.arrayBuffer();
      const base64 = btoa(String.fromCharCode(...new Uint8Array(buffer)));
      await onUpload(base64, password || undefined);
      setShowUpload(false);
      setPassword('');
    } catch (err) {
      console.error('Upload failed:', err);
    } finally {
      setUploading(false);
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    }
  };

  const formatDate = (dateStr?: string) => {
    if (!dateStr) return 'N/A';
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  };

  const getSourceBadge = (source: string) => {
    switch (source) {
      case 'Global':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-800">
            Global
          </span>
        );
      case 'Auto':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
            Auto-generated
          </span>
        );
      case 'Uploaded':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800">
            Uploaded
          </span>
        );
      default:
        return null;
    }
  };

  return (
    <div className="bg-gray-50 rounded-lg p-4 space-y-3">
      <div className="flex items-start justify-between">
        <div>
          <div className="flex items-center gap-2">
            <ShieldCheckIcon className="h-5 w-5 text-gray-400" />
            <h4 className="text-sm font-medium text-gray-900">{title}</h4>
            {certInfo && getSourceBadge(certInfo.source)}
          </div>
          <p className="text-sm text-gray-500 mt-1">{description}</p>
        </div>
      </div>

      {certInfo?.hasCertificate && (
        <div className="bg-white rounded border border-gray-200 p-3 space-y-2">
          <div className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
            <div>
              <span className="text-gray-500">Subject:</span>
              <span className="ml-2 text-gray-900 font-mono text-xs">{certInfo.subject || 'N/A'}</span>
            </div>
            <div>
              <span className="text-gray-500">Thumbprint:</span>
              <span className="ml-2 text-gray-900 font-mono text-xs">{certInfo.thumbprint?.substring(0, 16)}...</span>
            </div>
            <div>
              <span className="text-gray-500">Valid From:</span>
              <span className="ml-2 text-gray-900">{formatDate(certInfo.notBefore)}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-gray-500">Valid Until:</span>
              <span className={`ml-2 ${certInfo.isExpired ? 'text-red-600' : certInfo.isExpiringSoon ? 'text-yellow-600' : 'text-gray-900'}`}>
                {formatDate(certInfo.notAfter)}
              </span>
              {certInfo.isExpired && (
                <ExclamationTriangleIcon className="h-4 w-4 text-red-500" title="Certificate expired" />
              )}
              {certInfo.isExpiringSoon && !certInfo.isExpired && (
                <ExclamationTriangleIcon className="h-4 w-4 text-yellow-500" title="Certificate expiring soon" />
              )}
            </div>
          </div>
        </div>
      )}

      {!certInfo?.hasCertificate && (
        <div className="text-sm text-gray-500 italic">
          No certificate configured. Using global certificate.
        </div>
      )}

      {/* Action buttons */}
      <div className="flex items-center gap-2 pt-2">
        <button
          type="button"
          onClick={onGenerate}
          disabled={loading}
          className="inline-flex items-center px-3 py-1.5 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
        >
          {loading ? (
            <ArrowPathIcon className="h-4 w-4 mr-1.5 animate-spin" />
          ) : (
            <ArrowPathIcon className="h-4 w-4 mr-1.5" />
          )}
          Generate New
        </button>

        <button
          type="button"
          onClick={() => setShowUpload(!showUpload)}
          disabled={loading}
          className="inline-flex items-center px-3 py-1.5 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
        >
          <ArrowUpTrayIcon className="h-4 w-4 mr-1.5" />
          Upload PFX
        </button>

        {certInfo?.source !== 'Global' && (
          <button
            type="button"
            onClick={onReset}
            disabled={loading}
            className="inline-flex items-center px-3 py-1.5 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
          >
            Reset to Global
          </button>
        )}
      </div>

      {/* Upload form */}
      {showUpload && (
        <div className="bg-white rounded border border-gray-200 p-3 space-y-3">
          <div>
            <label className="block text-sm font-medium text-gray-700">
              PFX Password (optional)
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Enter password if PFX is protected"
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">
              Select PFX File
            </label>
            <input
              ref={fileInputRef}
              type="file"
              accept=".pfx,.p12"
              onChange={handleFileSelect}
              disabled={uploading}
              className="mt-1 block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-medium file:bg-primary-50 file:text-primary-700 hover:file:bg-primary-100"
            />
          </div>
          <button
            type="button"
            onClick={() => {
              setShowUpload(false);
              setPassword('');
            }}
            className="text-sm text-gray-500 hover:text-gray-700"
          >
            Cancel
          </button>
        </div>
      )}
    </div>
  );
}

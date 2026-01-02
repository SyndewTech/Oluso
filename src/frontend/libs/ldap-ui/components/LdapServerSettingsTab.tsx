import { useEffect, useState, useRef } from 'react';
import {
  CheckCircleIcon,
  ExclamationCircleIcon,
  InformationCircleIcon,
  PlayIcon,
  ArrowPathIcon,
  ArrowUpTrayIcon,
  ShieldCheckIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline';
import type { SettingsTabProps } from '@oluso/ui-core';
import { ldapApi, type LdapServerSettings, type LdapServerInfo, type LdapCertificateInfo } from '../api/ldapApi';

export default function LdapServerSettingsTab({
  onSave,
  onHasChanges,
  isActive,
}: SettingsTabProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [hasChanges, setHasChanges] = useState(false);

  const [settings, setSettings] = useState<LdapServerSettings>({
    enabled: false,
    allowAnonymousBind: false,
  });
  const [serverInfo, setServerInfo] = useState<LdapServerInfo | null>(null);
  const [tlsCert, setTlsCert] = useState<LdapCertificateInfo | null>(null);
  const [certLoading, setCertLoading] = useState(false);

  const loadCertificate = async () => {
    try {
      const cert = await ldapApi.getTlsCertificate().catch(() => null);
      setTlsCert(cert);
    } catch (err) {
      console.error('Error loading certificate:', err);
    }
  };

  useEffect(() => {
    if (!isActive) return;

    async function loadData() {
      try {
        setLoading(true);
        const [settingsData, info] = await Promise.all([
          ldapApi.getSettings(),
          ldapApi.getServerInfo().catch(() => null),
        ]);
        setSettings(settingsData);
        setServerInfo(info);
        await loadCertificate();
        setError(null);
      } catch (err) {
        setError('Failed to load LDAP Server configuration');
        console.error('Error loading LDAP config:', err);
      } finally {
        setLoading(false);
      }
    }
    loadData();
  }, [isActive]);

  const handleChange = <K extends keyof LdapServerSettings>(
    key: K,
    value: LdapServerSettings[K]
  ) => {
    setSettings((prev) => ({ ...prev, [key]: value }));
    setHasChanges(true);
    setSuccess(null);
    onHasChanges?.(true);
  };

  const handleSave = async () => {
    try {
      setSaving(true);
      setError(null);
      await ldapApi.updateSettings(settings);
      // Refresh server info after save
      const info = await ldapApi.getServerInfo().catch(() => null);
      setServerInfo(info);
      setSuccess('LDAP Server settings saved successfully');
      setHasChanges(false);
      onHasChanges?.(false);
      onSave?.();
    } catch (err) {
      setError('Failed to save LDAP Server configuration');
      console.error('Error saving LDAP config:', err);
    } finally {
      setSaving(false);
    }
  };

  const handleTestConnection = async () => {
    try {
      setTesting(true);
      setError(null);
      const result = await ldapApi.testConnection();
      if (result.success) {
        setSuccess(result.message);
      } else {
        setError(result.message);
      }
    } catch (err) {
      setError('Failed to test LDAP connection');
      console.error('Error testing connection:', err);
    } finally {
      setTesting(false);
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

      {/* LDAP Server Settings */}
      <div className="space-y-6">
        <div>
          <h3 className="text-lg font-medium text-gray-900">LDAP Server Configuration</h3>
          <p className="mt-1 text-sm text-gray-500">
            Enable and configure LDAP Server functionality for this tenant
          </p>
        </div>

        {/* Enable Toggle */}
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-gray-700">Enable LDAP Server</p>
            <p className="text-sm text-gray-500">
              Allow external applications to authenticate users via LDAP
            </p>
          </div>
          <button
            type="button"
            onClick={() => handleChange('enabled', !settings.enabled)}
            className={`relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 ${
              settings.enabled ? 'bg-primary-600' : 'bg-gray-200'
            }`}
            role="switch"
            aria-checked={settings.enabled}
          >
            <span
              className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out ${
                settings.enabled ? 'translate-x-5' : 'translate-x-0'
              }`}
            />
          </button>
        </div>

        {/* Base DN */}
        <div>
          <label htmlFor="baseDn" className="block text-sm font-medium text-gray-700">
            Base DN (Optional)
          </label>
          <input
            type="text"
            id="baseDn"
            value={settings.baseDn || ''}
            onChange={(e) => handleChange('baseDn', e.target.value || undefined)}
            placeholder={serverInfo?.baseDn || 'dc=oluso,dc=local'}
            disabled={!settings.enabled}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500"
          />
          <p className="mt-2 text-sm text-gray-500">
            Custom Base DN for this tenant. Leave empty to use the global setting with tenant isolation.
          </p>
        </div>

        {/* Organization */}
        <div>
          <label htmlFor="organization" className="block text-sm font-medium text-gray-700">
            Organization Name (Optional)
          </label>
          <input
            type="text"
            id="organization"
            value={settings.organization || ''}
            onChange={(e) => handleChange('organization', e.target.value || undefined)}
            placeholder={serverInfo?.organization || 'Oluso'}
            disabled={!settings.enabled}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500"
          />
        </div>

        {/* Allow Anonymous Bind */}
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-gray-700">Allow Anonymous Bind</p>
            <p className="text-sm text-gray-500">
              Allow read-only access without authentication (not recommended)
            </p>
          </div>
          <button
            type="button"
            onClick={() => handleChange('allowAnonymousBind', !settings.allowAnonymousBind)}
            disabled={!settings.enabled}
            className={`relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed ${
              settings.allowAnonymousBind ? 'bg-primary-600' : 'bg-gray-200'
            }`}
            role="switch"
            aria-checked={settings.allowAnonymousBind}
          >
            <span
              className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out ${
                settings.allowAnonymousBind ? 'translate-x-5' : 'translate-x-0'
              }`}
            />
          </button>
        </div>

        {/* Max Search Results */}
        <div>
          <label htmlFor="maxSearchResults" className="block text-sm font-medium text-gray-700">
            Max Search Results (Optional)
          </label>
          <input
            type="number"
            id="maxSearchResults"
            value={settings.maxSearchResults || ''}
            onChange={(e) => handleChange('maxSearchResults', e.target.value ? parseInt(e.target.value) : undefined)}
            placeholder={String(serverInfo?.maxSearchResults || 1000)}
            min="1"
            max="10000"
            disabled={!settings.enabled}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500"
          />
        </div>

        {/* Admin DN */}
        <div>
          <label htmlFor="adminDn" className="block text-sm font-medium text-gray-700">
            Admin DN (Optional)
          </label>
          <input
            type="text"
            id="adminDn"
            value={settings.adminDn || ''}
            onChange={(e) => handleChange('adminDn', e.target.value || undefined)}
            placeholder={serverInfo?.adminDn || 'cn=admin,...'}
            disabled={!settings.enabled}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500"
          />
          <p className="mt-2 text-sm text-gray-500">
            Service account DN for administrative operations
          </p>
        </div>

        {/* Info Box */}
        <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div className="flex">
            <InformationCircleIcon className="h-5 w-5 text-blue-500 flex-shrink-0" />
            <div className="ml-3">
              <h4 className="text-sm font-medium text-blue-800">How LDAP Server Works</h4>
              <p className="mt-1 text-sm text-blue-700">
                The LDAP Server exposes your tenant's users and groups via the LDAP protocol,
                allowing legacy applications to authenticate against Oluso. Users are organized
                under the configured Base DN with tenant isolation enabled.
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Server Info - Only show when enabled */}
      {settings.enabled && serverInfo && (
        <div className="space-y-4">
          <div>
            <h3 className="text-lg font-medium text-gray-900">Connection Details</h3>
            <p className="mt-1 text-sm text-gray-500">
              Use these settings to configure LDAP clients
            </p>
          </div>
          <div className="bg-gray-50 rounded-lg p-4 space-y-3">
            <ConnectionRow label="LDAP Port" value={String(serverInfo.port)} />
            {serverInfo.enableSsl && (
              <ConnectionRow label="LDAPS Port" value={String(serverInfo.sslPort)} />
            )}
            {serverInfo.enableStartTls && (
              <ConnectionRow label="STARTTLS" value="Supported on plain port" />
            )}
            <ConnectionRow label="Base DN" value={serverInfo.baseDn} />
            <ConnectionRow label="User OU" value={`ou=${serverInfo.userOu},${serverInfo.baseDn}`} />
            <ConnectionRow label="Group OU" value={`ou=${serverInfo.groupOu},${serverInfo.baseDn}`} />
            <ConnectionRow label="Admin DN" value={serverInfo.adminDn} />
          </div>

          {/* Test Connection */}
          <div className="flex items-center gap-4">
            <button
              type="button"
              onClick={handleTestConnection}
              disabled={testing}
              className="inline-flex items-center px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500 disabled:opacity-50"
            >
              {testing ? (
                <>
                  <PlayIcon className="h-4 w-4 mr-2 animate-spin" />
                  Testing...
                </>
              ) : (
                <>
                  <PlayIcon className="h-4 w-4 mr-2" />
                  Test Connection
                </>
              )}
            </button>
          </div>
        </div>
      )}

      {/* TLS Certificate - Show when SSL/TLS is enabled */}
      {settings.enabled && (serverInfo?.enableSsl || serverInfo?.enableStartTls) && (
        <div className="space-y-4">
          <div>
            <h3 className="text-lg font-medium text-gray-900">TLS Certificate</h3>
            <p className="mt-1 text-sm text-gray-500">
              Configure the SSL/TLS certificate for secure LDAP connections
            </p>
          </div>

          <CertificateCard
            certInfo={tlsCert}
            loading={certLoading}
            onGenerate={async () => {
              setCertLoading(true);
              try {
                const cert = await ldapApi.generateTlsCertificate();
                setTlsCert(cert);
                setSuccess('TLS certificate generated successfully');
              } catch (err) {
                setError('Failed to generate TLS certificate');
              } finally {
                setCertLoading(false);
              }
            }}
            onUpload={async (base64Pfx: string, password?: string) => {
              setCertLoading(true);
              try {
                const cert = await ldapApi.uploadTlsCertificate({ base64Pfx, password });
                setTlsCert(cert);
                setSuccess('TLS certificate uploaded successfully');
              } catch (err) {
                setError('Failed to upload TLS certificate');
              } finally {
                setCertLoading(false);
              }
            }}
            onReset={async () => {
              setCertLoading(true);
              try {
                await ldapApi.resetTlsCertificate();
                await loadCertificate();
                setSuccess('TLS certificate reset to global');
              } catch (err) {
                setError('Failed to reset TLS certificate');
              } finally {
                setCertLoading(false);
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

interface ConnectionRowProps {
  label: string;
  value: string;
}

function ConnectionRow({ label, value }: ConnectionRowProps) {
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
  certInfo: LdapCertificateInfo | null;
  loading: boolean;
  onGenerate: () => Promise<void>;
  onUpload: (base64Pfx: string, password?: string) => Promise<void>;
  onReset: () => Promise<void>;
}

function CertificateCard({
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
      case 'NotConfigured':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-yellow-100 text-yellow-800">
            Not Configured
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
            <h4 className="text-sm font-medium text-gray-900">TLS Certificate</h4>
            {certInfo && getSourceBadge(certInfo.source)}
          </div>
          <p className="text-sm text-gray-500 mt-1">
            Used to encrypt LDAP connections (LDAPS and STARTTLS)
          </p>
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
          No certificate configured. {certInfo?.source === 'NotConfigured'
            ? 'Certificate service is not available.'
            : 'Using global certificate if available.'}
        </div>
      )}

      {/* Action buttons */}
      {certInfo?.source !== 'NotConfigured' && (
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

          {certInfo?.source !== 'Global' && certInfo?.hasCertificate && (
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
      )}

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

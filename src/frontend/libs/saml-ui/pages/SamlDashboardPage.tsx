import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  BuildingOfficeIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClipboardDocumentIcon,
} from '@heroicons/react/24/outline';
import { samlApi, type IdpInfo, type SamlServiceProvider } from '../api/samlApi';

export default function SamlDashboardPage() {
  const [idpInfo, setIdpInfo] = useState<IdpInfo | null>(null);
  const [serviceProviders, setServiceProviders] = useState<SamlServiceProvider[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  useEffect(() => {
    async function loadData() {
      try {
        setLoading(true);
        const [info, sps] = await Promise.all([
          samlApi.getIdpInfo(),
          samlApi.getAll(),
        ]);
        setIdpInfo(info);
        setServiceProviders(sps);
        setError(null);
      } catch (err) {
        setError('Failed to load SAML configuration');
        console.error('Error loading SAML data:', err);
      } finally {
        setLoading(false);
      }
    }
    loadData();
  }, []);

  const copyToClipboard = async (value: string, label: string) => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(label);
      setTimeout(() => setCopied(null), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="h-8 w-48 bg-gray-200 rounded animate-pulse" />
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          {[1, 2].map((i) => (
            <div key={i} className="h-48 bg-gray-200 rounded-lg animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-red-50 border border-red-200 rounded-lg p-4">
        <p className="text-red-700">{error}</p>
      </div>
    );
  }

  const enabledSps = serviceProviders.filter(sp => sp.enabled).length;
  const disabledSps = serviceProviders.length - enabledSps;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">SAML Identity Provider</h1>
        <Link
          to="/saml/service-providers"
          className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700"
        >
          Manage Service Providers
        </Link>
      </div>

      {/* IdP Status */}
      <div className={`rounded-lg p-4 ${idpInfo?.enabled ? 'bg-green-50 border border-green-200' : 'bg-yellow-50 border border-yellow-200'}`}>
        <div className="flex items-center space-x-2">
          {idpInfo?.enabled ? (
            <>
              <CheckCircleIcon className="h-5 w-5 text-green-600" />
              <span className="text-green-700 font-medium">SAML IdP is enabled</span>
            </>
          ) : (
            <>
              <XCircleIcon className="h-5 w-5 text-yellow-600" />
              <span className="text-yellow-700 font-medium">SAML IdP is disabled</span>
            </>
          )}
        </div>
      </div>

      {/* IdP Configuration Info */}
      {idpInfo && (
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">IdP Configuration</h2>
            <p className="mt-1 text-sm text-gray-500">
              Share these URLs with Service Providers to configure SSO
            </p>
          </div>
          <div className="px-6 py-4 space-y-4">
            <InfoRow
              label="Entity ID (Issuer)"
              value={idpInfo.entityId}
              onCopy={() => copyToClipboard(idpInfo.entityId, 'entityId')}
              copied={copied === 'entityId'}
            />
            <InfoRow
              label="SSO URL"
              value={idpInfo.singleSignOnServiceUrl}
              onCopy={() => copyToClipboard(idpInfo.singleSignOnServiceUrl, 'ssoUrl')}
              copied={copied === 'ssoUrl'}
            />
            <InfoRow
              label="SLO URL"
              value={idpInfo.singleLogoutServiceUrl}
              onCopy={() => copyToClipboard(idpInfo.singleLogoutServiceUrl, 'sloUrl')}
              copied={copied === 'sloUrl'}
            />
            <InfoRow
              label="Metadata URL"
              value={idpInfo.metadataUrl}
              onCopy={() => copyToClipboard(idpInfo.metadataUrl, 'metadataUrl')}
              copied={copied === 'metadataUrl'}
              isLink
            />
            <div className="pt-2">
              <span className="text-sm font-medium text-gray-500">Supported Name ID Formats:</span>
              <ul className="mt-1 text-sm text-gray-700 list-disc list-inside">
                {idpInfo.supportedNameIdFormats.map((format, idx) => (
                  <li key={idx}>{format.split(':').pop()}</li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      )}

      {/* Service Provider Stats */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <StatCard
          title="Total Service Providers"
          value={serviceProviders.length}
          icon={BuildingOfficeIcon}
          href="/saml/service-providers"
        />
        <StatCard
          title="Enabled"
          value={enabledSps}
          icon={CheckCircleIcon}
          iconColor="text-green-500"
        />
        <StatCard
          title="Disabled"
          value={disabledSps}
          icon={XCircleIcon}
          iconColor="text-gray-400"
        />
      </div>

      {/* Recent Service Providers */}
      {serviceProviders.length > 0 && (
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Service Providers</h2>
          </div>
          <ul className="divide-y divide-gray-200">
            {serviceProviders.slice(0, 5).map((sp) => (
              <li key={sp.id} className="px-6 py-4 hover:bg-gray-50">
                <Link to={`/saml/service-providers/${sp.id}`} className="flex items-center justify-between">
                  <div className="flex items-center space-x-3">
                    <BuildingOfficeIcon className="h-6 w-6 text-gray-400" />
                    <div>
                      <p className="font-medium text-gray-900">
                        {sp.displayName || sp.entityId}
                      </p>
                      <p className="text-sm text-gray-500 truncate max-w-md">{sp.entityId}</p>
                    </div>
                  </div>
                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                    sp.enabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800'
                  }`}>
                    {sp.enabled ? 'Enabled' : 'Disabled'}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
          {serviceProviders.length > 5 && (
            <div className="px-6 py-3 border-t border-gray-200 bg-gray-50">
              <Link
                to="/saml/service-providers"
                className="text-sm text-primary-600 hover:text-primary-700"
              >
                View all {serviceProviders.length} service providers
              </Link>
            </div>
          )}
        </div>
      )}

      {serviceProviders.length === 0 && (
        <div className="text-center py-12 bg-white rounded-lg shadow">
          <BuildingOfficeIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No Service Providers</h3>
          <p className="mt-1 text-sm text-gray-500">
            Get started by adding a SAML Service Provider.
          </p>
          <div className="mt-6">
            <Link
              to="/saml/service-providers/new"
              className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700"
            >
              Add Service Provider
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}

interface InfoRowProps {
  label: string;
  value: string;
  onCopy: () => void;
  copied: boolean;
  isLink?: boolean;
}

function InfoRow({ label, value, onCopy, copied, isLink }: InfoRowProps) {
  return (
    <div className="flex items-center justify-between">
      <div className="flex-1 min-w-0">
        <span className="text-sm font-medium text-gray-500">{label}:</span>
        <div className="flex items-center space-x-2 mt-1">
          {isLink ? (
            <a
              href={value}
              target="_blank"
              rel="noopener noreferrer"
              className="text-sm text-primary-600 hover:text-primary-700 truncate"
            >
              {value}
            </a>
          ) : (
            <code className="text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded truncate">
              {value}
            </code>
          )}
        </div>
      </div>
      <button
        onClick={onCopy}
        className="ml-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded"
        title="Copy to clipboard"
      >
        {copied ? (
          <CheckCircleIcon className="h-5 w-5 text-green-500" />
        ) : (
          <ClipboardDocumentIcon className="h-5 w-5" />
        )}
      </button>
    </div>
  );
}

interface StatCardProps {
  title: string;
  value: number;
  icon: React.ComponentType<{ className?: string }>;
  iconColor?: string;
  href?: string;
}

function StatCard({ title, value, icon: Icon, iconColor = 'text-gray-400', href }: StatCardProps) {
  const content = (
    <div className="bg-white shadow rounded-lg p-6">
      <div className="flex items-center">
        <div className="flex-shrink-0">
          <Icon className={`h-8 w-8 ${iconColor}`} />
        </div>
        <div className="ml-4">
          <p className="text-sm font-medium text-gray-500">{title}</p>
          <p className="text-2xl font-semibold text-gray-900">{value}</p>
        </div>
      </div>
    </div>
  );

  if (href) {
    return <Link to={href}>{content}</Link>;
  }

  return content;
}

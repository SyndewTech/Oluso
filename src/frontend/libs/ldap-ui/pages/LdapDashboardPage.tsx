import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  ServerIcon,
  CheckCircleIcon,
  XCircleIcon,
  ExclamationCircleIcon,
  UsersIcon,
  KeyIcon,
  ArrowRightIcon,
} from '@heroicons/react/24/outline';
import { ldapApi, type LdapServerInfo } from '../api/ldapApi';

export default function LdapDashboardPage() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [serverInfo, setServerInfo] = useState<LdapServerInfo | null>(null);

  useEffect(() => {
    async function loadData() {
      try {
        setLoading(true);
        const info = await ldapApi.getServerInfo();
        setServerInfo(info);
        setError(null);
      } catch (err) {
        setError('Failed to load LDAP Server information');
        console.error('Error loading LDAP info:', err);
      } finally {
        setLoading(false);
      }
    }
    loadData();
  }, []);

  if (loading) {
    return (
      <div className="p-6 space-y-6">
        <div className="h-8 w-64 bg-gray-200 rounded animate-pulse" />
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-32 bg-gray-200 rounded-lg animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-6">
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ExclamationCircleIcon className="h-5 w-5 text-red-500" />
          <p className="text-red-700">{error}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">LDAP Server</h1>
        <p className="mt-1 text-sm text-gray-500">
          Expose users via LDAP protocol for legacy application integration
        </p>
      </div>

      {/* Status Cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Server Status */}
        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex items-center">
            <div className={`p-3 rounded-full ${serverInfo?.enabled ? 'bg-green-100' : 'bg-gray-100'}`}>
              <ServerIcon className={`h-6 w-6 ${serverInfo?.enabled ? 'text-green-600' : 'text-gray-400'}`} />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Server Status</p>
              <div className="flex items-center mt-1">
                {serverInfo?.enabled ? (
                  <>
                    <CheckCircleIcon className="h-5 w-5 text-green-500 mr-1" />
                    <span className="text-lg font-semibold text-green-600">Enabled</span>
                  </>
                ) : (
                  <>
                    <XCircleIcon className="h-5 w-5 text-gray-400 mr-1" />
                    <span className="text-lg font-semibold text-gray-500">Disabled</span>
                  </>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Port Info */}
        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex items-center">
            <div className="p-3 rounded-full bg-blue-100">
              <ServerIcon className="h-6 w-6 text-blue-600" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Listening Port</p>
              <p className="text-lg font-semibold text-gray-900">
                {serverInfo?.port || 389}
                {serverInfo?.enableSsl && ` / ${serverInfo.sslPort} (SSL)`}
              </p>
            </div>
          </div>
        </div>

        {/* Directory Info */}
        <div className="bg-white rounded-lg shadow p-6">
          <div className="flex items-center">
            <div className="p-3 rounded-full bg-purple-100">
              <UsersIcon className="h-6 w-6 text-purple-600" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500">Base DN</p>
              <p className="text-sm font-mono text-gray-900 truncate max-w-xs" title={serverInfo?.baseDn}>
                {serverInfo?.baseDn || 'Not configured'}
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Management Section */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {/* Service Accounts */}
        <Link
          to="/ldap/service-accounts"
          className="bg-white rounded-lg shadow p-6 hover:shadow-md transition-shadow group"
        >
          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <div className="p-3 rounded-full bg-indigo-100">
                <KeyIcon className="h-6 w-6 text-indigo-600" />
              </div>
              <div className="ml-4">
                <p className="text-lg font-medium text-gray-900">Service Accounts</p>
                <p className="text-sm text-gray-500">
                  Manage LDAP service accounts for programmatic access
                </p>
              </div>
            </div>
            <ArrowRightIcon className="h-5 w-5 text-gray-400 group-hover:text-indigo-600 transition-colors" />
          </div>
        </Link>

        {/* Placeholder for future feature */}
        <div className="bg-white rounded-lg shadow p-6 opacity-50">
          <div className="flex items-center">
            <div className="p-3 rounded-full bg-gray-100">
              <UsersIcon className="h-6 w-6 text-gray-400" />
            </div>
            <div className="ml-4">
              <p className="text-lg font-medium text-gray-500">User Directory</p>
              <p className="text-sm text-gray-400">
                Browse and search LDAP directory (coming soon)
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Connection Details */}
      {serverInfo?.enabled && (
        <div className="bg-white rounded-lg shadow">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Connection Details</h2>
          </div>
          <div className="p-6 space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <InfoRow label="Base DN" value={serverInfo.baseDn} />
              <InfoRow label="Organization" value={serverInfo.organization} />
              <InfoRow label="User OU" value={`ou=${serverInfo.userOu},${serverInfo.baseDn}`} />
              <InfoRow label="Group OU" value={`ou=${serverInfo.groupOu},${serverInfo.baseDn}`} />
              <InfoRow label="Admin DN" value={serverInfo.adminDn} />
              <InfoRow label="Max Results" value={String(serverInfo.maxSearchResults)} />
            </div>

            {/* Sample LDAP URL */}
            <div className="mt-4 p-4 bg-gray-50 rounded-lg">
              <p className="text-sm font-medium text-gray-700 mb-2">Sample LDAP URL:</p>
              <code className="text-sm text-gray-900 bg-white px-3 py-2 rounded border block overflow-x-auto">
                ldap://your-server:{serverInfo.port}/{serverInfo.baseDn}
              </code>
            </div>

            {/* Sample User DN */}
            <div className="p-4 bg-gray-50 rounded-lg">
              <p className="text-sm font-medium text-gray-700 mb-2">Sample User DN:</p>
              <code className="text-sm text-gray-900 bg-white px-3 py-2 rounded border block overflow-x-auto">
                uid=username,ou={serverInfo.userOu},{serverInfo.baseDn}
              </code>
            </div>
          </div>
        </div>
      )}

      {/* Quick Start Guide */}
      <div className="bg-white rounded-lg shadow">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-medium text-gray-900">Quick Start</h2>
        </div>
        <div className="p-6">
          <ol className="list-decimal list-inside space-y-3 text-sm text-gray-600">
            <li>
              <span className="font-medium text-gray-900">Enable LDAP Server</span> - Go to Settings → LDAP Server to enable
            </li>
            <li>
              <span className="font-medium text-gray-900">Configure Base DN</span> - Set your organization's Base DN
            </li>
            <li>
              <span className="font-medium text-gray-900">Connect Applications</span> - Use the connection details above to configure your LDAP clients
            </li>
            <li>
              <span className="font-medium text-gray-900">Test Connection</span> - Use an LDAP browser or ldapsearch to verify connectivity
            </li>
          </ol>

          {/* Sample ldapsearch command */}
          <div className="mt-6 p-4 bg-gray-900 rounded-lg">
            <p className="text-xs text-gray-400 mb-2">Test with ldapsearch:</p>
            <code className="text-sm text-green-400 block overflow-x-auto">
              ldapsearch -x -H ldap://localhost:{serverInfo?.port || 389} -b "{serverInfo?.baseDn || 'dc=example,dc=com'}" "(objectClass=*)"
            </code>
          </div>
        </div>
      </div>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-sm font-medium text-gray-500">{label}</dt>
      <dd className="mt-1 text-sm text-gray-900 font-mono">{value}</dd>
    </div>
  );
}

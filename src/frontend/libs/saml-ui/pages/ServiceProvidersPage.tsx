import { useEffect, useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import {
  PlusIcon,
  BuildingOfficeIcon,
  PencilIcon,
  TrashIcon,
  PlayIcon,
  PauseIcon,
  BeakerIcon,
  CheckCircleIcon,
  XCircleIcon,
  MagnifyingGlassIcon,
} from '@heroicons/react/24/outline';
import { samlApi, type SamlServiceProvider } from '../api/samlApi';

export default function ServiceProvidersPage() {
  const [serviceProviders, setServiceProviders] = useState<SamlServiceProvider[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [showDisabled, setShowDisabled] = useState(true);
  const [actionLoading, setActionLoading] = useState<number | null>(null);
  const [testResult, setTestResult] = useState<{ id: number; success: boolean; message: string } | null>(null);

  const loadServiceProviders = useCallback(async () => {
    try {
      setLoading(true);
      const data = await samlApi.getAll(showDisabled);
      setServiceProviders(data);
      setError(null);
    } catch (err) {
      setError('Failed to load Service Providers');
      console.error('Error loading service providers:', err);
    } finally {
      setLoading(false);
    }
  }, [showDisabled]);

  useEffect(() => {
    loadServiceProviders();
  }, [loadServiceProviders]);

  const handleToggle = async (sp: SamlServiceProvider) => {
    try {
      setActionLoading(sp.id);
      await samlApi.toggle(sp.id);
      await loadServiceProviders();
    } catch (err) {
      console.error('Error toggling service provider:', err);
      alert('Failed to toggle service provider');
    } finally {
      setActionLoading(null);
    }
  };

  const handleDelete = async (sp: SamlServiceProvider) => {
    if (!confirm(`Are you sure you want to delete "${sp.displayName || sp.entityId}"?`)) {
      return;
    }

    try {
      setActionLoading(sp.id);
      await samlApi.delete(sp.id);
      await loadServiceProviders();
    } catch (err) {
      console.error('Error deleting service provider:', err);
      alert('Failed to delete service provider');
    } finally {
      setActionLoading(null);
    }
  };

  const handleTest = async (sp: SamlServiceProvider) => {
    try {
      setActionLoading(sp.id);
      setTestResult(null);
      const result = await samlApi.test(sp.id);
      setTestResult({ id: sp.id, ...result });
    } catch (err) {
      console.error('Error testing service provider:', err);
      setTestResult({ id: sp.id, success: false, message: 'Failed to run test' });
    } finally {
      setActionLoading(null);
    }
  };

  const filteredProviders = serviceProviders.filter((sp) => {
    if (!search) return true;
    const searchLower = search.toLowerCase();
    return (
      sp.entityId.toLowerCase().includes(searchLower) ||
      sp.displayName?.toLowerCase().includes(searchLower) ||
      sp.description?.toLowerCase().includes(searchLower)
    );
  });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">SAML Service Providers</h1>
          <p className="mt-1 text-sm text-gray-500">
            Applications that use this system as their SAML Identity Provider
          </p>
        </div>
        <Link
          to="/saml/service-providers/new"
          className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700"
        >
          <PlusIcon className="-ml-1 mr-2 h-5 w-5" />
          Add Service Provider
        </Link>
      </div>

      {/* Filters */}
      <div className="flex items-center space-x-4">
        <div className="flex-1 max-w-md">
          <div className="relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              placeholder="Search by Entity ID, name, or description..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-md focus:ring-primary-500 focus:border-primary-500"
            />
          </div>
        </div>
        <label className="flex items-center space-x-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={showDisabled}
            onChange={(e) => setShowDisabled(e.target.checked)}
            className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
          />
          <span>Show disabled</span>
        </label>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <p className="text-red-700">{error}</p>
        </div>
      )}

      {/* Test Result Toast */}
      {testResult && (
        <div className={`rounded-lg p-4 ${testResult.success ? 'bg-green-50 border border-green-200' : 'bg-red-50 border border-red-200'}`}>
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
            <button
              onClick={() => setTestResult(null)}
              className="text-gray-400 hover:text-gray-600"
            >
              &times;
            </button>
          </div>
        </div>
      )}

      {loading ? (
        <div className="space-y-4">
          {[1, 2, 3].map((i) => (
            <div key={i} className="bg-gray-200 h-24 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : filteredProviders.length === 0 ? (
        <div className="text-center py-12 bg-white rounded-lg shadow">
          <BuildingOfficeIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No Service Providers</h3>
          <p className="mt-1 text-sm text-gray-500">
            {search
              ? 'No service providers match your search.'
              : 'Get started by adding a SAML Service Provider.'}
          </p>
          {!search && (
            <div className="mt-6">
              <Link
                to="/saml/service-providers/new"
                className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700"
              >
                <PlusIcon className="-ml-1 mr-2 h-5 w-5" />
                Add Service Provider
              </Link>
            </div>
          )}
        </div>
      ) : (
        <div className="bg-white shadow overflow-hidden rounded-lg">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Service Provider
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Configuration
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Last Accessed
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredProviders.map((sp) => (
                <tr key={sp.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4">
                    <div className="flex items-center">
                      <BuildingOfficeIcon className="h-8 w-8 text-gray-400 flex-shrink-0" />
                      <div className="ml-4">
                        <div className="text-sm font-medium text-gray-900">
                          {sp.displayName || sp.entityId}
                        </div>
                        <div className="text-sm text-gray-500 max-w-xs truncate" title={sp.entityId}>
                          {sp.entityId}
                        </div>
                        {sp.description && (
                          <div className="text-xs text-gray-400 max-w-xs truncate">
                            {sp.description}
                          </div>
                        )}
                      </div>
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <span
                      className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                        sp.enabled
                          ? 'bg-green-100 text-green-800'
                          : 'bg-gray-100 text-gray-800'
                      }`}
                    >
                      {sp.enabled ? 'Enabled' : 'Disabled'}
                    </span>
                  </td>
                  <td className="px-6 py-4">
                    <div className="flex items-center space-x-2 text-xs text-gray-500">
                      {sp.metadataUrl ? (
                        <span className="bg-blue-100 text-blue-800 px-2 py-0.5 rounded">Metadata</span>
                      ) : (
                        <span className="bg-gray-100 text-gray-600 px-2 py-0.5 rounded">Manual</span>
                      )}
                      {sp.encryptAssertions && (
                        <span className="bg-purple-100 text-purple-800 px-2 py-0.5 rounded">Encrypted</span>
                      )}
                      {sp.requireSignedAuthnRequests && (
                        <span className="bg-yellow-100 text-yellow-800 px-2 py-0.5 rounded">Signed Requests</span>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {sp.lastAccessed
                      ? new Date(sp.lastAccessed).toLocaleDateString()
                      : 'Never'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <div className="flex items-center justify-end space-x-2">
                      <button
                        onClick={() => handleTest(sp)}
                        disabled={actionLoading === sp.id}
                        className="p-1.5 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded"
                        title="Test configuration"
                      >
                        <BeakerIcon className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleToggle(sp)}
                        disabled={actionLoading === sp.id || sp.nonEditable}
                        className={`p-1.5 rounded ${
                          sp.enabled
                            ? 'text-gray-400 hover:text-yellow-600 hover:bg-yellow-50'
                            : 'text-gray-400 hover:text-green-600 hover:bg-green-50'
                        } ${sp.nonEditable ? 'opacity-50 cursor-not-allowed' : ''}`}
                        title={sp.enabled ? 'Disable' : 'Enable'}
                      >
                        {sp.enabled ? (
                          <PauseIcon className="h-4 w-4" />
                        ) : (
                          <PlayIcon className="h-4 w-4" />
                        )}
                      </button>
                      <Link
                        to={`/saml/service-providers/${sp.id}`}
                        className="p-1.5 text-gray-400 hover:text-primary-600 hover:bg-primary-50 rounded"
                        title="Edit"
                      >
                        <PencilIcon className="h-4 w-4" />
                      </Link>
                      <button
                        onClick={() => handleDelete(sp)}
                        disabled={actionLoading === sp.id || sp.nonEditable}
                        className={`p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded ${
                          sp.nonEditable ? 'opacity-50 cursor-not-allowed' : ''
                        }`}
                        title="Delete"
                      >
                        <TrashIcon className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  ArrowPathIcon,
  PlusIcon,
  TrashIcon,
  PencilIcon,
  ArrowLeftIcon,
  ArrowsRightLeftIcon,
  ArrowRightIcon,
  ArrowLeftIcon as ArrowInIcon,
  CheckCircleIcon,
  XCircleIcon,
  SparklesIcon,
} from '@heroicons/react/24/outline';
import {
  scimApi,
  type ScimAttributeMapping,
  type ScimClient,
  type CreateScimAttributeMappingRequest,
} from '../api/scimApi';

interface MappingFormData {
  scimAttribute: string;
  internalProperty: string;
  direction: 'inbound' | 'outbound' | 'both';
  isRequired: boolean;
  defaultValue: string;
  transformation: string;
  priority: number;
  isEnabled: boolean;
}

const initialFormData: MappingFormData = {
  scimAttribute: '',
  internalProperty: '',
  direction: 'inbound',
  isRequired: false,
  defaultValue: '',
  transformation: 'none',
  priority: 0,
  isEnabled: true,
};

export default function ScimMappingsPage() {
  const { clientId } = useParams<{ clientId: string }>();

  const [client, setClient] = useState<ScimClient | null>(null);
  const [mappings, setMappings] = useState<ScimAttributeMapping[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [editingMapping, setEditingMapping] = useState<ScimAttributeMapping | null>(null);
  const [formData, setFormData] = useState<MappingFormData>(initialFormData);
  const [saving, setSaving] = useState(false);
  const [showDefaultsModal, setShowDefaultsModal] = useState(false);
  const [selectedProvider, setSelectedProvider] = useState<string>('standard');

  const scimSuggestions = scimApi.getScimAttributeSuggestions();
  const internalProperties = scimApi.getInternalPropertyOptions();
  const transformations = scimApi.getTransformationOptions();

  const loadData = useCallback(async () => {
    if (!clientId) return;

    try {
      setLoading(true);
      setError(null);
      const [clientData, mappingsData] = await Promise.all([
        scimApi.getClient(clientId),
        scimApi.getMappings(clientId),
      ]);
      setClient(clientData);
      setMappings(mappingsData);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
    }
  }, [clientId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!clientId) return;

    try {
      setSaving(true);
      setError(null);

      const request: CreateScimAttributeMappingRequest = {
        scimAttribute: formData.scimAttribute,
        internalProperty: formData.internalProperty,
        direction: formData.direction,
        isRequired: formData.isRequired,
        defaultValue: formData.defaultValue || undefined,
        transformation: formData.transformation === 'none' ? undefined : formData.transformation,
        priority: formData.priority,
        isEnabled: formData.isEnabled,
      };

      if (editingMapping) {
        await scimApi.updateMapping(clientId, editingMapping.id, request);
      } else {
        await scimApi.createMapping(clientId, request);
      }

      setShowForm(false);
      setEditingMapping(null);
      setFormData(initialFormData);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save mapping');
    } finally {
      setSaving(false);
    }
  };

  const handleEdit = (mapping: ScimAttributeMapping) => {
    setEditingMapping(mapping);
    setFormData({
      scimAttribute: mapping.scimAttribute,
      internalProperty: mapping.internalProperty,
      direction: mapping.direction,
      isRequired: mapping.isRequired,
      defaultValue: mapping.defaultValue || '',
      transformation: mapping.transformation || 'none',
      priority: mapping.priority,
      isEnabled: mapping.isEnabled,
    });
    setShowForm(true);
  };

  const handleDelete = async (mappingId: string) => {
    if (!clientId || !confirm('Are you sure you want to delete this mapping?')) return;

    try {
      setError(null);
      await scimApi.deleteMapping(clientId, mappingId);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete mapping');
    }
  };

  const handleApplyDefaults = async () => {
    if (!clientId) return;

    try {
      setSaving(true);
      setError(null);
      await scimApi.applyDefaultMappings(clientId, selectedProvider);
      setShowDefaultsModal(false);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to apply default mappings');
    } finally {
      setSaving(false);
    }
  };

  const handleToggleEnabled = async (mapping: ScimAttributeMapping) => {
    if (!clientId) return;

    try {
      await scimApi.updateMapping(clientId, mapping.id, {
        isEnabled: !mapping.isEnabled,
      });
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update mapping');
    }
  };

  const getDirectionIcon = (direction: string) => {
    switch (direction) {
      case 'inbound':
        return <ArrowRightIcon className="h-4 w-4 text-blue-600" title="Inbound (SCIM to Internal)" />;
      case 'outbound':
        return <ArrowInIcon className="h-4 w-4 text-green-600" title="Outbound (Internal to SCIM)" />;
      case 'both':
        return <ArrowsRightLeftIcon className="h-4 w-4 text-purple-600" title="Bidirectional" />;
      default:
        return null;
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <ArrowPathIcon className="h-8 w-8 animate-spin text-gray-400" />
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          <Link
            to={`/scim/clients/${clientId}`}
            className="text-gray-500 hover:text-gray-700"
          >
            <ArrowLeftIcon className="h-5 w-5" />
          </Link>
          <div>
            <h1 className="text-2xl font-semibold text-gray-900">Attribute Mappings</h1>
            {client && (
              <p className="text-sm text-gray-500">
                Configure how SCIM attributes map to internal user properties for{' '}
                <span className="font-medium text-gray-700">{client.name}</span>
              </p>
            )}
          </div>
        </div>
        <div className="flex items-center space-x-3">
          <button
            onClick={() => setShowDefaultsModal(true)}
            className="inline-flex items-center px-3 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
          >
            <SparklesIcon className="h-4 w-4 mr-2" />
            Apply Defaults
          </button>
          <button
            onClick={() => {
              setEditingMapping(null);
              setFormData(initialFormData);
              setShowForm(true);
            }}
            className="inline-flex items-center px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg"
          >
            <PlusIcon className="h-4 w-4 mr-2" />
            Add Mapping
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
          {error}
        </div>
      )}

      {/* Mappings Table */}
      <div className="bg-white rounded-lg border border-gray-200 overflow-x-auto shadow-sm">
        {mappings.length === 0 ? (
          <div className="p-8 text-center">
            <ArrowsRightLeftIcon className="h-12 w-12 text-gray-400 mx-auto mb-4" />
            <h3 className="text-lg font-medium text-gray-900 mb-2">No Mappings Configured</h3>
            <p className="text-gray-500 mb-4">
              Configure attribute mappings to define how SCIM attributes map to internal user properties.
            </p>
            <button
              onClick={() => setShowDefaultsModal(true)}
              className="inline-flex items-center px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg"
            >
              <SparklesIcon className="h-4 w-4 mr-2" />
              Apply Default Mappings
            </button>
          </div>
        ) : (
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  SCIM Attribute
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Direction
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Internal Property
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Transform
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Required
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {mappings.map((mapping) => (
                <tr key={mapping.id} className={!mapping.isEnabled ? 'opacity-50 bg-gray-50' : ''}>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <button
                      onClick={() => handleToggleEnabled(mapping)}
                      className="focus:outline-none"
                      title={mapping.isEnabled ? 'Disable mapping' : 'Enable mapping'}
                    >
                      {mapping.isEnabled ? (
                        <CheckCircleIcon className="h-5 w-5 text-green-500" />
                      ) : (
                        <XCircleIcon className="h-5 w-5 text-gray-400" />
                      )}
                    </button>
                  </td>
                  <td className="px-6 py-4">
                    <code className="text-sm text-blue-700 bg-blue-50 px-2 py-1 rounded">
                      {mapping.scimAttribute}
                    </code>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div className="flex items-center space-x-2">
                      {getDirectionIcon(mapping.direction)}
                      <span className="text-sm text-gray-700 capitalize">{mapping.direction}</span>
                    </div>
                  </td>
                  <td className="px-6 py-4">
                    <code className="text-sm text-green-700 bg-green-50 px-2 py-1 rounded">
                      {mapping.internalProperty}
                    </code>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {mapping.transformation || '-'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    {mapping.isRequired ? (
                      <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-orange-100 text-orange-700">
                        Required
                      </span>
                    ) : (
                      <span className="text-gray-400 text-sm">Optional</span>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button
                      onClick={() => handleEdit(mapping)}
                      className="text-blue-600 hover:text-blue-800 mr-3"
                    >
                      <PencilIcon className="h-4 w-4" />
                    </button>
                    <button
                      onClick={() => handleDelete(mapping.id)}
                      className="text-red-600 hover:text-red-800"
                    >
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Mapping Form Modal */}
      {showForm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 max-w-2xl w-full mx-4 max-h-[90vh] overflow-y-auto">
            <h2 className="text-xl font-semibold text-gray-900 mb-6">
              {editingMapping ? 'Edit Mapping' : 'Add Mapping'}
            </h2>

            <form onSubmit={handleSubmit} className="space-y-6">
              <div className="grid grid-cols-2 gap-6">
                {/* SCIM Attribute */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">
                    SCIM Attribute
                  </label>
                  <input
                    type="text"
                    list="scim-suggestions"
                    value={formData.scimAttribute}
                    onChange={(e) => setFormData({ ...formData, scimAttribute: e.target.value })}
                    className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    placeholder="e.g., userName, emails[primary eq true].value"
                    required
                  />
                  <datalist id="scim-suggestions">
                    {scimSuggestions.map((s) => (
                      <option key={s.attribute} value={s.attribute}>
                        {s.description}
                      </option>
                    ))}
                  </datalist>
                  <p className="mt-1 text-xs text-gray-500">
                    The SCIM attribute path from the incoming request
                  </p>
                </div>

                {/* Internal Property */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">
                    Internal Property
                  </label>
                  <select
                    value={formData.internalProperty}
                    onChange={(e) => setFormData({ ...formData, internalProperty: e.target.value })}
                    className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    required
                  >
                    <option value="">Select property...</option>
                    {internalProperties.map((p) => (
                      <option key={p.name} value={p.name}>
                        {p.displayName}
                      </option>
                    ))}
                  </select>
                  <p className="mt-1 text-xs text-gray-500">
                    The internal user property to map to
                  </p>
                </div>

                {/* Direction */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">
                    Direction
                  </label>
                  <select
                    value={formData.direction}
                    onChange={(e) =>
                      setFormData({
                        ...formData,
                        direction: e.target.value as 'inbound' | 'outbound' | 'both',
                      })
                    }
                    className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="inbound">Inbound (SCIM to Internal)</option>
                    <option value="outbound">Outbound (Internal to SCIM)</option>
                    <option value="both">Bidirectional</option>
                  </select>
                </div>

                {/* Transformation */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">
                    Transformation
                  </label>
                  <select
                    value={formData.transformation}
                    onChange={(e) => setFormData({ ...formData, transformation: e.target.value })}
                    className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    {transformations.map((t) => (
                      <option key={t.name} value={t.name}>
                        {t.description}
                      </option>
                    ))}
                  </select>
                </div>

                {/* Default Value */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">
                    Default Value
                  </label>
                  <input
                    type="text"
                    value={formData.defaultValue}
                    onChange={(e) => setFormData({ ...formData, defaultValue: e.target.value })}
                    className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    placeholder="Value if SCIM attribute is empty"
                  />
                </div>

                {/* Priority */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">
                    Priority
                  </label>
                  <input
                    type="number"
                    value={formData.priority}
                    onChange={(e) => setFormData({ ...formData, priority: parseInt(e.target.value) || 0 })}
                    className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    min="0"
                    max="100"
                  />
                  <p className="mt-1 text-xs text-gray-500">
                    Higher priority mappings are processed first
                  </p>
                </div>
              </div>

              {/* Checkboxes */}
              <div className="flex items-center space-x-6">
                <label className="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    checked={formData.isRequired}
                    onChange={(e) => setFormData({ ...formData, isRequired: e.target.checked })}
                    className="h-4 w-4 border-gray-300 rounded text-blue-600 focus:ring-blue-500"
                  />
                  <span className="text-sm text-gray-700">Required for user creation</span>
                </label>
                <label className="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    checked={formData.isEnabled}
                    onChange={(e) => setFormData({ ...formData, isEnabled: e.target.checked })}
                    className="h-4 w-4 border-gray-300 rounded text-blue-600 focus:ring-blue-500"
                  />
                  <span className="text-sm text-gray-700">Enabled</span>
                </label>
              </div>

              {/* Form Actions */}
              <div className="flex justify-end space-x-3 pt-4 border-t border-gray-200">
                <button
                  type="button"
                  onClick={() => {
                    setShowForm(false);
                    setEditingMapping(null);
                    setFormData(initialFormData);
                  }}
                  className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-lg"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={saving}
                  className="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg disabled:opacity-50"
                >
                  {saving ? 'Saving...' : editingMapping ? 'Update Mapping' : 'Create Mapping'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Apply Defaults Modal */}
      {showDefaultsModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 max-w-md w-full mx-4">
            <h2 className="text-xl font-semibold text-gray-900 mb-4">Apply Default Mappings</h2>
            <p className="text-gray-600 mb-6">
              Choose a provider to apply pre-configured attribute mappings. This will add default
              mappings without removing existing ones.
            </p>

            <div className="mb-6">
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Provider
              </label>
              <select
                value={selectedProvider}
                onChange={(e) => setSelectedProvider(e.target.value)}
                className="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              >
                <option value="standard">Standard SCIM 2.0</option>
                <option value="azure">Azure AD</option>
                <option value="okta">Okta</option>
                <option value="google">Google Workspace</option>
              </select>
            </div>

            <div className="flex justify-end space-x-3">
              <button
                onClick={() => setShowDefaultsModal(false)}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={handleApplyDefaults}
                disabled={saving}
                className="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg disabled:opacity-50"
              >
                {saving ? 'Applying...' : 'Apply Defaults'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

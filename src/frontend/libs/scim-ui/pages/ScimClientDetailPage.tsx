import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  ArrowLeftIcon,
  ArrowPathIcon,
  CheckCircleIcon,
  XCircleIcon,
} from '@heroicons/react/24/outline';
import { scimApi, type ScimClient, type ScimProvisioningLog } from '../api/scimApi';

export default function ScimClientDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [client, setClient] = useState<ScimClient | null>(null);
  const [logs, setLogs] = useState<ScimProvisioningLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    description: '',
    isEnabled: true,
    allowedIpRanges: '',
    rateLimitPerMinute: 60,
    canCreateUsers: true,
    canUpdateUsers: true,
    canDeleteUsers: true,
    canManageGroups: true,
  });

  useEffect(() => {
    if (id) {
      loadClient(id);
      loadLogs(id);
    }
  }, [id]);

  const loadClient = async (clientId: string) => {
    try {
      const data = await scimApi.getClient(clientId);
      setClient(data);
      setFormData({
        name: data.name,
        description: data.description || '',
        isEnabled: data.isEnabled,
        allowedIpRanges: data.allowedIpRanges || '',
        rateLimitPerMinute: data.rateLimitPerMinute,
        canCreateUsers: data.canCreateUsers,
        canUpdateUsers: data.canUpdateUsers,
        canDeleteUsers: data.canDeleteUsers,
        canManageGroups: data.canManageGroups,
      });
    } catch (err) {
      console.error('Failed to load client:', err);
    } finally {
      setLoading(false);
    }
  };

  const loadLogs = async (clientId: string) => {
    try {
      const response = await scimApi.getLogs(clientId, 0, 20);
      setLogs(response.items);
    } catch (err) {
      console.error('Failed to load logs:', err);
    }
  };

  const handleSave = async () => {
    if (!id) return;
    try {
      setSaving(true);
      const updated = await scimApi.updateClient(id, formData);
      setClient(updated);
    } catch (err) {
      console.error('Failed to save:', err);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <div className="p-6">Loading...</div>;
  }

  if (!client) {
    return <div className="p-6">Client not found</div>;
  }

  return (
    <div className="p-6 max-w-4xl">
      <button
        onClick={() => navigate('/scim/clients')}
        className="inline-flex items-center text-gray-600 hover:text-gray-900 mb-4"
      >
        <ArrowLeftIcon className="h-4 w-4 mr-2" />
        Back to SCIM Clients
      </button>

      <h1 className="text-2xl font-semibold text-gray-900 mb-6">{client.name}</h1>

      {/* Quick links */}
      <div className="bg-white shadow rounded-lg p-4 mb-6">
        <div className="flex items-center gap-4">
          <button
            onClick={() => navigate(`/scim/clients/${id}/mappings`)}
            className="inline-flex items-center px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
          >
            Configure Attribute Mappings
          </button>
        </div>
      </div>

      {/* Settings form */}
      <div className="bg-white shadow rounded-lg p-6 mb-6">
        <h2 className="text-lg font-medium mb-4">Settings</h2>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Name
            </label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className="w-full border rounded-md px-3 py-2"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Description
            </label>
            <input
              type="text"
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              className="w-full border rounded-md px-3 py-2"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Allowed IP Ranges
            </label>
            <input
              type="text"
              value={formData.allowedIpRanges}
              onChange={(e) => setFormData({ ...formData, allowedIpRanges: e.target.value })}
              className="w-full border rounded-md px-3 py-2"
              placeholder="Leave empty to allow all"
            />
            <p className="text-xs text-gray-500 mt-1">
              Comma-separated IPs or ranges (e.g., 192.168.1.*, 10.0.0.1)
            </p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Rate Limit (requests/min)
            </label>
            <input
              type="number"
              value={formData.rateLimitPerMinute}
              onChange={(e) => setFormData({ ...formData, rateLimitPerMinute: parseInt(e.target.value) })}
              className="w-full border rounded-md px-3 py-2"
              min={0}
            />
          </div>
        </div>

        {/* Permissions */}
        <h3 className="text-sm font-medium text-gray-700 mt-6 mb-3">Permissions</h3>
        <div className="grid grid-cols-2 gap-3">
          <label className="flex items-center">
            <input
              type="checkbox"
              checked={formData.isEnabled}
              onChange={(e) => setFormData({ ...formData, isEnabled: e.target.checked })}
              className="rounded border-gray-300 text-blue-600 mr-2"
            />
            <span className="text-sm">Enabled</span>
          </label>
          <label className="flex items-center">
            <input
              type="checkbox"
              checked={formData.canCreateUsers}
              onChange={(e) => setFormData({ ...formData, canCreateUsers: e.target.checked })}
              className="rounded border-gray-300 text-blue-600 mr-2"
            />
            <span className="text-sm">Can Create Users</span>
          </label>
          <label className="flex items-center">
            <input
              type="checkbox"
              checked={formData.canUpdateUsers}
              onChange={(e) => setFormData({ ...formData, canUpdateUsers: e.target.checked })}
              className="rounded border-gray-300 text-blue-600 mr-2"
            />
            <span className="text-sm">Can Update Users</span>
          </label>
          <label className="flex items-center">
            <input
              type="checkbox"
              checked={formData.canDeleteUsers}
              onChange={(e) => setFormData({ ...formData, canDeleteUsers: e.target.checked })}
              className="rounded border-gray-300 text-blue-600 mr-2"
            />
            <span className="text-sm">Can Delete Users</span>
          </label>
          <label className="flex items-center">
            <input
              type="checkbox"
              checked={formData.canManageGroups}
              onChange={(e) => setFormData({ ...formData, canManageGroups: e.target.checked })}
              className="rounded border-gray-300 text-blue-600 mr-2"
            />
            <span className="text-sm">Can Manage Groups</span>
          </label>
        </div>

        <div className="mt-6 flex justify-end">
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
          >
            {saving ? 'Saving...' : 'Save Changes'}
          </button>
        </div>
      </div>

      {/* Statistics */}
      <div className="bg-white shadow rounded-lg p-6 mb-6">
        <h2 className="text-lg font-medium mb-4">Statistics</h2>
        <div className="grid grid-cols-4 gap-4">
          <div className="text-center">
            <div className="text-2xl font-bold text-gray-900">{client.successCount}</div>
            <div className="text-sm text-gray-500">Successful Operations</div>
          </div>
          <div className="text-center">
            <div className="text-2xl font-bold text-red-600">{client.errorCount}</div>
            <div className="text-sm text-gray-500">Failed Operations</div>
          </div>
          <div className="text-center">
            <div className="text-sm font-medium text-gray-900">
              {client.tokenCreatedAt ? new Date(client.tokenCreatedAt).toLocaleDateString() : '-'}
            </div>
            <div className="text-sm text-gray-500">Token Created</div>
          </div>
          <div className="text-center">
            <div className="text-sm font-medium text-gray-900">
              {client.lastActivityAt ? new Date(client.lastActivityAt).toLocaleString() : 'Never'}
            </div>
            <div className="text-sm text-gray-500">Last Activity</div>
          </div>
        </div>
      </div>

      {/* Recent logs */}
      <div className="bg-white shadow rounded-lg p-6">
        <div className="flex justify-between items-center mb-4">
          <h2 className="text-lg font-medium">Recent Activity</h2>
          <button
            onClick={() => id && loadLogs(id)}
            className="text-gray-600 hover:text-gray-900"
          >
            <ArrowPathIcon className="h-5 w-5" />
          </button>
        </div>

        {logs.length === 0 ? (
          <p className="text-gray-500 text-center py-4">No activity recorded yet</p>
        ) : (
          <div className="space-y-2">
            {logs.map((log) => (
              <div
                key={log.id}
                className="flex items-center justify-between py-2 border-b last:border-0"
              >
                <div className="flex items-center">
                  {log.success ? (
                    <CheckCircleIcon className="h-5 w-5 text-green-500 mr-3" />
                  ) : (
                    <XCircleIcon className="h-5 w-5 text-red-500 mr-3" />
                  )}
                  <div>
                    <span className="font-mono text-sm">
                      {log.method} {log.path}
                    </span>
                    {log.errorMessage && (
                      <p className="text-sm text-red-600">{log.errorMessage}</p>
                    )}
                  </div>
                </div>
                <div className="text-right text-sm text-gray-500">
                  <div>{log.statusCode} - {log.durationMs}ms</div>
                  <div>{new Date(log.timestamp).toLocaleString()}</div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

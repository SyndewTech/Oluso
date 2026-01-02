import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import {
  PlusIcon,
  ArrowPathIcon,
  TrashIcon,
  ClipboardIcon,
  CheckIcon,
} from '@heroicons/react/24/outline';
import { scimApi, getScimEndpointUrl, type ScimClient } from '../api/scimApi';

export default function ScimClientsPage() {
  const [clients, setClients] = useState<ScimClient[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [newToken, setNewToken] = useState<string | null>(null);
  const [copiedToken, setCopiedToken] = useState(false);

  useEffect(() => {
    loadClients();
  }, []);

  const loadClients = async () => {
    try {
      setLoading(true);
      const data = await scimApi.getClients();
      setClients(data);
    } catch (err) {
      setError('Failed to load SCIM clients');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = async (data: { name: string; description?: string }) => {
    try {
      const response = await scimApi.createClient(data);
      setNewToken(response.token);
      setClients([...clients, response.client]);
      setShowCreateModal(false);
    } catch (err) {
      console.error('Failed to create client:', err);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Are you sure you want to delete this SCIM client?')) return;
    try {
      await scimApi.deleteClient(id);
      setClients(clients.filter(c => c.id !== id));
    } catch (err) {
      console.error('Failed to delete client:', err);
    }
  };

  const handleRotateToken = async (id: string) => {
    if (!confirm('Rotate the API token? The old token will stop working immediately.')) return;
    try {
      const response = await scimApi.rotateToken(id);
      setNewToken(response.token);
    } catch (err) {
      console.error('Failed to rotate token:', err);
    }
  };

  const copyToken = () => {
    if (newToken) {
      navigator.clipboard.writeText(newToken);
      setCopiedToken(true);
      setTimeout(() => setCopiedToken(false), 2000);
    }
  };

  if (loading) {
    return <div className="p-6">Loading...</div>;
  }

  return (
    <div className="p-6">
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">SCIM Provisioning</h1>
          <p className="text-gray-500 mt-1">
            Manage SCIM clients for automated user provisioning from external identity providers
          </p>
        </div>
        <button
          onClick={() => setShowCreateModal(true)}
          className="inline-flex items-center px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
        >
          <PlusIcon className="h-5 w-5 mr-2" />
          Add SCIM Client
        </button>
      </div>

      {/* Token display modal */}
      {newToken && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 max-w-lg w-full mx-4">
            <h3 className="text-lg font-semibold mb-4">SCIM API Token</h3>
            <p className="text-sm text-gray-600 mb-4">
              Copy this token now. You won't be able to see it again.
            </p>
            <div className="bg-gray-100 p-4 rounded-md font-mono text-sm break-all mb-4">
              {newToken}
            </div>
            <div className="flex justify-end gap-2">
              <button
                onClick={copyToken}
                className="inline-flex items-center px-4 py-2 bg-gray-100 rounded-md hover:bg-gray-200"
              >
                {copiedToken ? (
                  <>
                    <CheckIcon className="h-5 w-5 mr-2 text-green-600" />
                    Copied!
                  </>
                ) : (
                  <>
                    <ClipboardIcon className="h-5 w-5 mr-2" />
                    Copy
                  </>
                )}
              </button>
              <button
                onClick={() => setNewToken(null)}
                className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create modal */}
      {showCreateModal && (
        <CreateClientModal
          onClose={() => setShowCreateModal(false)}
          onCreate={handleCreate}
        />
      )}

      {error && (
        <div className="bg-red-50 text-red-700 p-4 rounded-md mb-6">{error}</div>
      )}

      {/* SCIM endpoint info */}
      <div className="bg-blue-50 border border-blue-200 rounded-md p-4 mb-6">
        <h3 className="font-medium text-blue-900 mb-2">SCIM Endpoint URL</h3>
        <code className="text-sm text-blue-700">
          {getScimEndpointUrl()}
        </code>
        <p className="text-sm text-blue-600 mt-2">
          Use this URL when configuring your identity provider (Azure AD, Okta, etc.)
        </p>
      </div>

      {/* Clients table */}
      <div className="bg-white shadow rounded-lg overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Name
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Status
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Last Activity
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Stats
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                Actions
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {clients.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-6 py-8 text-center text-gray-500">
                  No SCIM clients configured. Create one to enable provisioning.
                </td>
              </tr>
            ) : (
              clients.map((client) => (
                <tr key={client.id}>
                  <td className="px-6 py-4">
                    <Link
                      to={`/scim/clients/${client.id}`}
                      className="text-blue-600 hover:underline font-medium"
                    >
                      {client.name}
                    </Link>
                    {client.description && (
                      <p className="text-sm text-gray-500">{client.description}</p>
                    )}
                  </td>
                  <td className="px-6 py-4">
                    <span
                      className={`inline-flex px-2 py-1 text-xs rounded-full ${
                        client.isEnabled
                          ? 'bg-green-100 text-green-800'
                          : 'bg-gray-100 text-gray-800'
                      }`}
                    >
                      {client.isEnabled ? 'Enabled' : 'Disabled'}
                    </span>
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-500">
                    {client.lastActivityAt
                      ? new Date(client.lastActivityAt).toLocaleString()
                      : 'Never'}
                  </td>
                  <td className="px-6 py-4 text-sm">
                    <span className="text-green-600">{client.successCount} ok</span>
                    {' / '}
                    <span className="text-red-600">{client.errorCount} errors</span>
                  </td>
                  <td className="px-6 py-4 text-right">
                    <button
                      onClick={() => handleRotateToken(client.id)}
                      className="text-gray-600 hover:text-gray-900 p-1"
                      title="Rotate Token"
                    >
                      <ArrowPathIcon className="h-5 w-5" />
                    </button>
                    <button
                      onClick={() => handleDelete(client.id)}
                      className="text-red-600 hover:text-red-900 p-1 ml-2"
                      title="Delete"
                    >
                      <TrashIcon className="h-5 w-5" />
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function CreateClientModal({
  onClose,
  onCreate,
}: {
  onClose: () => void;
  onCreate: (data: { name: string; description?: string }) => void;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onCreate({ name, description: description || undefined });
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 max-w-md w-full mx-4">
        <h3 className="text-lg font-semibold mb-4">Create SCIM Client</h3>
        <form onSubmit={handleSubmit}>
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Name
            </label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="w-full border rounded-md px-3 py-2"
              placeholder="e.g., Azure AD, Okta"
              required
            />
          </div>
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Description (optional)
            </label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="w-full border rounded-md px-3 py-2"
              placeholder="Optional description"
            />
          </div>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-md"
            >
              Cancel
            </button>
            <button
              type="submit"
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
            >
              Create
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

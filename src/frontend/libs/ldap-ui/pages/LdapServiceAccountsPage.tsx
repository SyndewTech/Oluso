import { useEffect, useState } from 'react';
import {
  PlusIcon,
  TrashIcon,
  PencilIcon,
  KeyIcon,
  CheckCircleIcon,
  XCircleIcon,
  ExclamationTriangleIcon,
  ClipboardDocumentIcon,
  EyeIcon,
  EyeSlashIcon,
} from '@heroicons/react/24/outline';
import {
  ldapApi,
  type ServiceAccount,
  type CreateServiceAccountRequest,
  type UpdateServiceAccountRequest,
  type ServiceAccountPermission,
} from '../api/ldapApi';

export default function LdapServiceAccountsPage() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<ServiceAccount[]>([]);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [selectedAccount, setSelectedAccount] = useState<ServiceAccount | null>(null);
  const [actionLoading, setActionLoading] = useState(false);

  useEffect(() => {
    loadAccounts();
  }, []);

  async function loadAccounts() {
    try {
      setLoading(true);
      const data = await ldapApi.getServiceAccounts();
      setAccounts(data);
      setError(null);
    } catch (err) {
      setError('Failed to load service accounts');
      console.error('Error loading service accounts:', err);
    } finally {
      setLoading(false);
    }
  }

  async function handleCreate(data: CreateServiceAccountRequest) {
    try {
      setActionLoading(true);
      await ldapApi.createServiceAccount(data);
      await loadAccounts();
      setShowCreateModal(false);
    } catch (err) {
      console.error('Error creating service account:', err);
      throw err;
    } finally {
      setActionLoading(false);
    }
  }

  async function handleUpdate(id: string, data: UpdateServiceAccountRequest) {
    try {
      setActionLoading(true);
      await ldapApi.updateServiceAccount(id, data);
      await loadAccounts();
      setShowEditModal(false);
      setSelectedAccount(null);
    } catch (err) {
      console.error('Error updating service account:', err);
      throw err;
    } finally {
      setActionLoading(false);
    }
  }

  async function handleResetPassword(id: string, newPassword: string) {
    try {
      setActionLoading(true);
      await ldapApi.resetServiceAccountPassword(id, { newPassword });
      setShowPasswordModal(false);
      setSelectedAccount(null);
    } catch (err) {
      console.error('Error resetting password:', err);
      throw err;
    } finally {
      setActionLoading(false);
    }
  }

  async function handleDelete(id: string) {
    try {
      setActionLoading(true);
      await ldapApi.deleteServiceAccount(id);
      await loadAccounts();
      setShowDeleteConfirm(false);
      setSelectedAccount(null);
    } catch (err) {
      console.error('Error deleting service account:', err);
    } finally {
      setActionLoading(false);
    }
  }

  if (loading) {
    return (
      <div className="p-6 space-y-6">
        <div className="h-8 w-64 bg-gray-200 rounded animate-pulse" />
        <div className="space-y-4">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-20 bg-gray-200 rounded-lg animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Service Accounts</h1>
          <p className="mt-1 text-sm text-gray-500">
            Create service accounts for applications to bind to the LDAP server
          </p>
        </div>
        <button
          onClick={() => setShowCreateModal(true)}
          className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
        >
          <PlusIcon className="h-5 w-5 mr-2" />
          Create Service Account
        </button>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ExclamationTriangleIcon className="h-5 w-5 text-red-500" />
          <p className="text-red-700">{error}</p>
        </div>
      )}

      {accounts.length === 0 ? (
        <div className="bg-white rounded-lg shadow p-12 text-center">
          <KeyIcon className="h-12 w-12 text-gray-400 mx-auto mb-4" />
          <h3 className="text-lg font-medium text-gray-900 mb-2">No Service Accounts</h3>
          <p className="text-gray-500 mb-6">
            Create a service account to allow applications to authenticate with the LDAP server.
          </p>
          <button
            onClick={() => setShowCreateModal(true)}
            className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md text-blue-700 bg-blue-100 hover:bg-blue-200"
          >
            <PlusIcon className="h-5 w-5 mr-2" />
            Create First Service Account
          </button>
        </div>
      ) : (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Name
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Bind DN
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Permission
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Last Used
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {accounts.map((account) => (
                <tr key={account.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div>
                      <div className="text-sm font-medium text-gray-900">{account.name}</div>
                      {account.description && (
                        <div className="text-sm text-gray-500">{account.description}</div>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4">
                    <div className="flex items-center">
                      <code className="text-xs bg-gray-100 px-2 py-1 rounded font-mono text-gray-700 max-w-xs truncate">
                        {account.bindDn}
                      </code>
                      <button
                        onClick={() => navigator.clipboard.writeText(account.bindDn)}
                        className="ml-2 text-gray-400 hover:text-gray-600"
                        title="Copy Bind DN"
                      >
                        <ClipboardDocumentIcon className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                      account.permission === 'FullRead'
                        ? 'bg-purple-100 text-purple-800'
                        : account.permission === 'SearchOnly'
                        ? 'bg-blue-100 text-blue-800'
                        : 'bg-gray-100 text-gray-800'
                    }`}>
                      {account.permission}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    {account.isExpired ? (
                      <span className="inline-flex items-center text-red-600">
                        <XCircleIcon className="h-4 w-4 mr-1" />
                        Expired
                      </span>
                    ) : account.isEnabled ? (
                      <span className="inline-flex items-center text-green-600">
                        <CheckCircleIcon className="h-4 w-4 mr-1" />
                        Active
                      </span>
                    ) : (
                      <span className="inline-flex items-center text-gray-500">
                        <XCircleIcon className="h-4 w-4 mr-1" />
                        Disabled
                      </span>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {account.lastUsedAt
                      ? new Date(account.lastUsedAt).toLocaleDateString()
                      : 'Never'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button
                      onClick={() => {
                        setSelectedAccount(account);
                        setShowEditModal(true);
                      }}
                      className="text-blue-600 hover:text-blue-900 mr-3"
                      title="Edit"
                    >
                      <PencilIcon className="h-5 w-5" />
                    </button>
                    <button
                      onClick={() => {
                        setSelectedAccount(account);
                        setShowPasswordModal(true);
                      }}
                      className="text-yellow-600 hover:text-yellow-900 mr-3"
                      title="Reset Password"
                    >
                      <KeyIcon className="h-5 w-5" />
                    </button>
                    <button
                      onClick={() => {
                        setSelectedAccount(account);
                        setShowDeleteConfirm(true);
                      }}
                      className="text-red-600 hover:text-red-900"
                      title="Delete"
                    >
                      <TrashIcon className="h-5 w-5" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Create Modal */}
      {showCreateModal && (
        <CreateServiceAccountModal
          onClose={() => setShowCreateModal(false)}
          onSave={handleCreate}
          loading={actionLoading}
        />
      )}

      {/* Edit Modal */}
      {showEditModal && selectedAccount && (
        <EditServiceAccountModal
          account={selectedAccount}
          onClose={() => {
            setShowEditModal(false);
            setSelectedAccount(null);
          }}
          onSave={(data) => handleUpdate(selectedAccount.id, data)}
          loading={actionLoading}
        />
      )}

      {/* Password Reset Modal */}
      {showPasswordModal && selectedAccount && (
        <PasswordResetModal
          account={selectedAccount}
          onClose={() => {
            setShowPasswordModal(false);
            setSelectedAccount(null);
          }}
          onSave={(password) => handleResetPassword(selectedAccount.id, password)}
          loading={actionLoading}
        />
      )}

      {/* Delete Confirmation */}
      {showDeleteConfirm && selectedAccount && (
        <DeleteConfirmModal
          account={selectedAccount}
          onClose={() => {
            setShowDeleteConfirm(false);
            setSelectedAccount(null);
          }}
          onConfirm={() => handleDelete(selectedAccount.id)}
          loading={actionLoading}
        />
      )}
    </div>
  );
}

interface CreateServiceAccountModalProps {
  onClose: () => void;
  onSave: (data: CreateServiceAccountRequest) => Promise<void>;
  loading: boolean;
}

function CreateServiceAccountModal({ onClose, onSave, loading }: CreateServiceAccountModalProps) {
  const [formData, setFormData] = useState({
    name: '',
    description: '',
    password: '',
    confirmPassword: '',
    permission: 'ReadOnly' as ServiceAccountPermission,
    allowedOus: '',
    allowedIpRanges: '',
    maxSearchResults: '',
    rateLimitPerMinute: '',
    expiresAt: '',
  });
  const [error, setError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (formData.password !== formData.confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    if (formData.password.length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    try {
      await onSave({
        name: formData.name,
        description: formData.description || undefined,
        password: formData.password,
        permission: formData.permission,
        allowedOus: formData.allowedOus ? formData.allowedOus.split('\n').filter(Boolean) : undefined,
        allowedIpRanges: formData.allowedIpRanges ? formData.allowedIpRanges.split('\n').filter(Boolean) : undefined,
        maxSearchResults: formData.maxSearchResults ? parseInt(formData.maxSearchResults) : undefined,
        rateLimitPerMinute: formData.rateLimitPerMinute ? parseInt(formData.rateLimitPerMinute) : undefined,
        expiresAt: formData.expiresAt || undefined,
      });
    } catch (err) {
      setError('Failed to create service account');
    }
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-medium text-gray-900">Create Service Account</h2>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-6">
          {error && (
            <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">
              {error}
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                required
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                placeholder="my-app-service"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Permission</label>
              <select
                value={formData.permission}
                onChange={(e) => setFormData({ ...formData, permission: e.target.value as ServiceAccountPermission })}
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
              >
                <option value="ReadOnly">Read Only</option>
                <option value="SearchOnly">Search Only</option>
                <option value="FullRead">Full Read</option>
              </select>
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <input
              type="text"
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
              placeholder="Service account for HR application"
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Password *</label>
              <div className="relative">
                <input
                  type={showPassword ? 'text' : 'password'}
                  value={formData.password}
                  onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                  required
                  minLength={8}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 pr-10 focus:ring-blue-500 focus:border-blue-500"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                >
                  {showPassword ? <EyeSlashIcon className="h-5 w-5" /> : <EyeIcon className="h-5 w-5" />}
                </button>
              </div>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Confirm Password *</label>
              <input
                type={showPassword ? 'text' : 'password'}
                value={formData.confirmPassword}
                onChange={(e) => setFormData({ ...formData, confirmPassword: e.target.value })}
                required
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
              />
            </div>
          </div>

          <div className="border-t border-gray-200 pt-6">
            <h3 className="text-sm font-medium text-gray-900 mb-4">Restrictions (Optional)</h3>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Allowed OUs (one per line)
                </label>
                <textarea
                  value={formData.allowedOus}
                  onChange={(e) => setFormData({ ...formData, allowedOus: e.target.value })}
                  rows={3}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"
                  placeholder="ou=users&#10;ou=groups"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Allowed IP Ranges (one per line)
                </label>
                <textarea
                  value={formData.allowedIpRanges}
                  onChange={(e) => setFormData({ ...formData, allowedIpRanges: e.target.value })}
                  rows={3}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"
                  placeholder="10.0.0.0/8&#10;192.168.1.100"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mt-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Max Search Results</label>
                <input
                  type="number"
                  value={formData.maxSearchResults}
                  onChange={(e) => setFormData({ ...formData, maxSearchResults: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                  placeholder="1000"
                  min="1"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Rate Limit (binds/min)</label>
                <input
                  type="number"
                  value={formData.rateLimitPerMinute}
                  onChange={(e) => setFormData({ ...formData, rateLimitPerMinute: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                  placeholder="60"
                  min="1"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Expires At</label>
                <input
                  type="date"
                  value={formData.expiresAt}
                  onChange={(e) => setFormData({ ...formData, expiresAt: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                />
              </div>
            </div>
          </div>

          <div className="flex justify-end space-x-3 pt-4 border-t border-gray-200">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading}
              className="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {loading ? 'Creating...' : 'Create Account'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

interface EditServiceAccountModalProps {
  account: ServiceAccount;
  onClose: () => void;
  onSave: (data: UpdateServiceAccountRequest) => Promise<void>;
  loading: boolean;
}

function EditServiceAccountModal({ account, onClose, onSave, loading }: EditServiceAccountModalProps) {
  const [formData, setFormData] = useState({
    name: account.name,
    description: account.description ?? '',
    permission: account.permission,
    isEnabled: account.isEnabled,
    allowedOus: account.allowedOus?.join('\n') ?? '',
    allowedIpRanges: account.allowedIpRanges?.join('\n') ?? '',
    maxSearchResults: account.maxSearchResults?.toString() ?? '',
    rateLimitPerMinute: account.rateLimitPerMinute?.toString() ?? '',
    expiresAt: account.expiresAt ? account.expiresAt.split('T')[0] : '',
  });
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    try {
      await onSave({
        name: formData.name,
        description: formData.description || undefined,
        isEnabled: formData.isEnabled,
        permission: formData.permission,
        allowedOus: formData.allowedOus ? formData.allowedOus.split('\n').filter(Boolean) : undefined,
        allowedIpRanges: formData.allowedIpRanges ? formData.allowedIpRanges.split('\n').filter(Boolean) : undefined,
        maxSearchResults: formData.maxSearchResults ? parseInt(formData.maxSearchResults) : undefined,
        rateLimitPerMinute: formData.rateLimitPerMinute ? parseInt(formData.rateLimitPerMinute) : undefined,
        expiresAt: formData.expiresAt || undefined,
      });
    } catch (err) {
      setError('Failed to update service account');
    }
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-medium text-gray-900">Edit Service Account</h2>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-6">
          {error && (
            <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">
              {error}
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                required
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                placeholder="my-app-service"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Permission</label>
              <select
                value={formData.permission}
                onChange={(e) => setFormData({ ...formData, permission: e.target.value as ServiceAccountPermission })}
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
              >
                <option value="ReadOnly">Read Only</option>
                <option value="SearchOnly">Search Only</option>
                <option value="FullRead">Full Read</option>
              </select>
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <input
              type="text"
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
              placeholder="Service account for HR application"
            />
          </div>

          <div className="flex items-center">
            <input
              type="checkbox"
              id="isEnabled"
              checked={formData.isEnabled}
              onChange={(e) => setFormData({ ...formData, isEnabled: e.target.checked })}
              className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <label htmlFor="isEnabled" className="ml-2 block text-sm text-gray-900">
              Enabled
            </label>
          </div>

          <div className="border-t border-gray-200 pt-6">
            <h3 className="text-sm font-medium text-gray-900 mb-4">Restrictions (Optional)</h3>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Allowed OUs (one per line)
                </label>
                <textarea
                  value={formData.allowedOus}
                  onChange={(e) => setFormData({ ...formData, allowedOus: e.target.value })}
                  rows={3}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"
                  placeholder="ou=users&#10;ou=groups"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Allowed IP Ranges (one per line)
                </label>
                <textarea
                  value={formData.allowedIpRanges}
                  onChange={(e) => setFormData({ ...formData, allowedIpRanges: e.target.value })}
                  rows={3}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"
                  placeholder="10.0.0.0/8&#10;192.168.1.100"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mt-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Max Search Results</label>
                <input
                  type="number"
                  value={formData.maxSearchResults}
                  onChange={(e) => setFormData({ ...formData, maxSearchResults: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                  placeholder="1000"
                  min="1"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Rate Limit (binds/min)</label>
                <input
                  type="number"
                  value={formData.rateLimitPerMinute}
                  onChange={(e) => setFormData({ ...formData, rateLimitPerMinute: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                  placeholder="60"
                  min="1"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Expires At</label>
                <input
                  type="date"
                  value={formData.expiresAt}
                  onChange={(e) => setFormData({ ...formData, expiresAt: e.target.value })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                />
              </div>
            </div>
          </div>

          <div className="flex justify-end space-x-3 pt-4 border-t border-gray-200">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading}
              className="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {loading ? 'Saving...' : 'Save Changes'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

interface PasswordResetModalProps {
  account: ServiceAccount;
  onClose: () => void;
  onSave: (password: string) => Promise<void>;
  loading: boolean;
}

function PasswordResetModal({ account, onClose, onSave, loading }: PasswordResetModalProps) {
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (password !== confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    if (password.length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    try {
      await onSave(password);
    } catch (err) {
      setError('Failed to reset password');
    }
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-medium text-gray-900">Reset Password</h2>
          <p className="text-sm text-gray-500 mt-1">
            Reset password for <span className="font-medium">{account.name}</span>
          </p>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {error && (
            <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">
              {error}
            </div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">New Password</label>
            <div className="relative">
              <input
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={8}
                className="w-full border border-gray-300 rounded-md px-3 py-2 pr-10 focus:ring-blue-500 focus:border-blue-500"
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
              >
                {showPassword ? <EyeSlashIcon className="h-5 w-5" /> : <EyeIcon className="h-5 w-5" />}
              </button>
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Confirm Password</label>
            <input
              type={showPassword ? 'text' : 'password'}
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              required
              className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
            />
          </div>

          <div className="flex justify-end space-x-3 pt-4">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading}
              className="px-4 py-2 text-sm font-medium text-white bg-yellow-600 border border-transparent rounded-md hover:bg-yellow-700 disabled:opacity-50"
            >
              {loading ? 'Resetting...' : 'Reset Password'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

interface DeleteConfirmModalProps {
  account: ServiceAccount;
  onClose: () => void;
  onConfirm: () => void;
  loading: boolean;
}

function DeleteConfirmModal({ account, onClose, onConfirm, loading }: DeleteConfirmModalProps) {
  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-medium text-gray-900">Delete Service Account</h2>
        </div>

        <div className="p-6">
          <div className="flex items-start">
            <div className="flex-shrink-0">
              <ExclamationTriangleIcon className="h-6 w-6 text-red-600" />
            </div>
            <div className="ml-3">
              <p className="text-sm text-gray-700">
                Are you sure you want to delete the service account{' '}
                <span className="font-medium">{account.name}</span>? This action cannot be undone.
              </p>
              <p className="text-sm text-gray-500 mt-2">
                Applications using this account will no longer be able to authenticate.
              </p>
            </div>
          </div>
        </div>

        <div className="px-6 py-4 bg-gray-50 rounded-b-lg flex justify-end space-x-3">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={loading}
            className="px-4 py-2 text-sm font-medium text-white bg-red-600 border border-transparent rounded-md hover:bg-red-700 disabled:opacity-50"
          >
            {loading ? 'Deleting...' : 'Delete Account'}
          </button>
        </div>
      </div>
    </div>
  );
}

import React, { useState, useEffect } from 'react';
import signingKeyService, { SigningKey, KeyRotationConfig, GenerateKeyRequest } from '../services/signingKeyService';

const SigningKeysPage: React.FC = () => {
  const [keys, setKeys] = useState<SigningKey[]>([]);
  const [rotationConfig, setRotationConfig] = useState<KeyRotationConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showGenerateModal, setShowGenerateModal] = useState(false);
  const [showConfigModal, setShowConfigModal] = useState(false);
  const [clientFilter] = useState<string>('');

  useEffect(() => {
    loadData();
  }, [clientFilter]);

  const loadData = async () => {
    try {
      setLoading(true);
      setError(null);
      const [keysData, configData] = await Promise.all([
        signingKeyService.getKeys(clientFilter || undefined),
        signingKeyService.getRotationConfig(clientFilter || undefined)
      ]);
      setKeys(keysData);
      setRotationConfig(configData);
    } catch (err: any) {
      setError(err.message || 'Failed to load signing keys');
    } finally {
      setLoading(false);
    }
  };

  const handleRotate = async () => {
    if (!confirm('This will generate a new key and demote existing keys. Continue?')) {
      return;
    }
    try {
      await signingKeyService.rotateKeys(clientFilter || undefined);
      await loadData();
    } catch (err: any) {
      setError(err.message || 'Failed to rotate keys');
    }
  };

  const handleRevoke = async (key: SigningKey) => {
    const reason = prompt('Enter revocation reason:');
    if (!reason) return;

    try {
      await signingKeyService.revokeKey(key.id, reason);
      await loadData();
    } catch (err: any) {
      setError(err.message || 'Failed to revoke key');
    }
  };

  const handleDelete = async (key: SigningKey) => {
    if (!confirm(`Delete key ${key.name}? This cannot be undone.`)) {
      return;
    }
    try {
      await signingKeyService.deleteKey(key.id);
      await loadData();
    } catch (err: any) {
      setError(err.message || 'Failed to delete key');
    }
  };

  const getStatusBadge = (key: SigningKey) => {
    const statusColors: Record<string, string> = {
      Active: 'bg-green-100 text-green-800',
      Pending: 'bg-yellow-100 text-yellow-800',
      Expired: 'bg-orange-100 text-orange-800',
      Revoked: 'bg-red-100 text-red-800',
      Archived: 'bg-gray-100 text-gray-800'
    };

    return (
      <span className={`px-2 py-1 text-xs font-medium rounded-full ${statusColors[key.status] || 'bg-gray-100'}`}>
        {key.status}
        {key.isExpiringSoon && key.status === 'Active' && (
          <span className="ml-1 text-orange-600">⚠</span>
        )}
      </span>
    );
  };

  const formatDate = (date?: string) => {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Signing Keys</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage cryptographic keys used for signing tokens
          </p>
        </div>
        <div className="flex space-x-3">
          <button
            onClick={() => setShowConfigModal(true)}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Rotation Settings
          </button>
          <button
            onClick={handleRotate}
            className="px-4 py-2 text-sm font-medium text-white bg-yellow-600 rounded-md hover:bg-yellow-700"
          >
            Rotate Keys
          </button>
          <button
            onClick={() => setShowGenerateModal(true)}
            className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700"
          >
            Generate Key
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-md">
          {error}
        </div>
      )}

      {/* Key Expiration Warnings */}
      {keys.some(k => k.isExpiringSoon) && (
        <div className="bg-yellow-50 border border-yellow-200 text-yellow-800 px-4 py-3 rounded-md">
          <strong>Warning:</strong> Some keys are expiring soon. Consider rotating keys to ensure uninterrupted service.
        </div>
      )}

      {/* Rotation Config Summary */}
      {rotationConfig && (
        <div className="bg-white rounded-lg shadow p-4">
          <h3 className="text-sm font-medium text-gray-700 mb-2">Automatic Rotation</h3>
          <div className="flex items-center space-x-6 text-sm text-gray-600">
            <span className={rotationConfig.enabled ? 'text-green-600' : 'text-red-600'}>
              {rotationConfig.enabled ? '● Enabled' : '○ Disabled'}
            </span>
            <span>Algorithm: {rotationConfig.algorithm}</span>
            <span>Key Size: {rotationConfig.keySize} bits</span>
            <span>Lifetime: {rotationConfig.keyLifetimeDays} days</span>
            {rotationConfig.nextRotationAt && (
              <span>Next Rotation: {formatDate(rotationConfig.nextRotationAt)}</span>
            )}
          </div>
        </div>
      )}

      {/* Keys Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Key
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Type / Algorithm
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Status
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Expires
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Usage
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                Actions
              </th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {keys.map((key) => (
              <tr key={key.id} className={key.isExpired ? 'bg-gray-50' : ''}>
                <td className="px-6 py-4 whitespace-nowrap">
                  <div className="flex items-center">
                    <div>
                      <div className="text-sm font-medium text-gray-900">{key.name}</div>
                      <div className="text-xs text-gray-500 font-mono">{key.keyId}</div>
                      {key.clientId && (
                        <div className="text-xs text-blue-600">Client: {key.clientId}</div>
                      )}
                    </div>
                  </div>
                </td>
                <td className="px-6 py-4 whitespace-nowrap">
                  <div className="text-sm text-gray-900">{key.keyType}</div>
                  <div className="text-xs text-gray-500">{key.algorithm} ({key.keySize} bits)</div>
                </td>
                <td className="px-6 py-4 whitespace-nowrap">
                  {getStatusBadge(key)}
                  <div className="mt-1">
                    {key.canSign && (
                      <span className="text-xs text-green-600">Can Sign</span>
                    )}
                    {key.canVerify && !key.canSign && (
                      <span className="text-xs text-yellow-600">Verify Only</span>
                    )}
                  </div>
                </td>
                <td className="px-6 py-4 whitespace-nowrap">
                  <div className="text-sm text-gray-900">
                    {key.expiresAt ? formatDate(key.expiresAt) : 'Never'}
                  </div>
                  {key.isExpiringSoon && (
                    <div className="text-xs text-orange-600">Expiring Soon</div>
                  )}
                </td>
                <td className="px-6 py-4 whitespace-nowrap">
                  <div className="text-sm text-gray-900">
                    {key.signatureCount.toLocaleString()} signatures
                  </div>
                  {key.lastUsedAt && (
                    <div className="text-xs text-gray-500">
                      Last: {formatDate(key.lastUsedAt)}
                    </div>
                  )}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                  <div className="flex justify-end space-x-2">
                    {key.status === 'Active' && (
                      <button
                        onClick={() => handleRevoke(key)}
                        className="text-red-600 hover:text-red-900"
                      >
                        Revoke
                      </button>
                    )}
                    {key.status !== 'Active' && (
                      <button
                        onClick={() => handleDelete(key)}
                        className="text-red-600 hover:text-red-900"
                      >
                        Delete
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {keys.length === 0 && (
              <tr>
                <td colSpan={6} className="px-6 py-8 text-center text-gray-500">
                  No signing keys found. Generate a new key to get started.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Generate Key Modal */}
      {showGenerateModal && (
        <GenerateKeyModal
          onClose={() => setShowGenerateModal(false)}
          onGenerate={async (request) => {
            await signingKeyService.generateKey(request);
            await loadData();
            setShowGenerateModal(false);
          }}
        />
      )}

      {/* Rotation Config Modal */}
      {showConfigModal && rotationConfig && (
        <RotationConfigModal
          config={rotationConfig}
          onClose={() => setShowConfigModal(false)}
          onSave={async (config) => {
            await signingKeyService.updateRotationConfig(config);
            await loadData();
            setShowConfigModal(false);
          }}
        />
      )}
    </div>
  );
};

// Generate Key Modal Component
interface GenerateKeyModalProps {
  onClose: () => void;
  onGenerate: (request: GenerateKeyRequest) => Promise<void>;
}

const GenerateKeyModal: React.FC<GenerateKeyModalProps> = ({ onClose, onGenerate }) => {
  const [request, setRequest] = useState<GenerateKeyRequest>({
    keyType: 'RSA',
    algorithm: 'RS256',
    keySize: 2048,
    lifetimeDays: 90,
    activateImmediately: true
  });
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await onGenerate(request);
    } finally {
      setSubmitting(false);
    }
  };

  const algorithmOptions: Record<string, string[]> = {
    RSA: ['RS256', 'RS384', 'RS512', 'PS256', 'PS384', 'PS512'],
    EC: ['ES256', 'ES384', 'ES512'],
    Symmetric: ['HS256', 'HS384', 'HS512']
  };

  const keySizeOptions: Record<string, number[]> = {
    RSA: [2048, 3072, 4096],
    EC: [256, 384, 521],
    Symmetric: [256, 384, 512]
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full p-6">
        <h2 className="text-lg font-semibold mb-4">Generate New Signing Key</h2>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Name</label>
            <input
              type="text"
              value={request.name || ''}
              onChange={(e) => setRequest({ ...request, name: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
              placeholder="e.g., Production Key 2024"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Key Type</label>
            <select
              value={request.keyType}
              onChange={(e) => setRequest({
                ...request,
                keyType: e.target.value,
                algorithm: algorithmOptions[e.target.value][0],
                keySize: keySizeOptions[e.target.value][0]
              })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
            >
              <option value="RSA">RSA</option>
              <option value="EC">Elliptic Curve (EC)</option>
              <option value="Symmetric">Symmetric (HMAC)</option>
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Algorithm</label>
            <select
              value={request.algorithm}
              onChange={(e) => setRequest({ ...request, algorithm: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
            >
              {algorithmOptions[request.keyType || 'RSA'].map((alg) => (
                <option key={alg} value={alg}>{alg}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Key Size</label>
            <select
              value={request.keySize}
              onChange={(e) => setRequest({ ...request, keySize: parseInt(e.target.value) })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
            >
              {keySizeOptions[request.keyType || 'RSA'].map((size) => (
                <option key={size} value={size}>{size} bits</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Lifetime (days)</label>
            <input
              type="number"
              value={request.lifetimeDays || 90}
              onChange={(e) => setRequest({ ...request, lifetimeDays: parseInt(e.target.value) })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
              min={1}
              max={3650}
            />
          </div>

          <div className="flex items-center">
            <input
              type="checkbox"
              id="activateImmediately"
              checked={request.activateImmediately}
              onChange={(e) => setRequest({ ...request, activateImmediately: e.target.checked })}
              className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <label htmlFor="activateImmediately" className="ml-2 block text-sm text-gray-700">
              Activate immediately
            </label>
          </div>

          <div className="flex justify-end space-x-3 pt-4">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 rounded-md hover:bg-gray-200"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {submitting ? 'Generating...' : 'Generate Key'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

// Rotation Config Modal Component
interface RotationConfigModalProps {
  config: KeyRotationConfig;
  onClose: () => void;
  onSave: (config: KeyRotationConfig) => Promise<void>;
}

const RotationConfigModal: React.FC<RotationConfigModalProps> = ({ config, onClose, onSave }) => {
  const [editConfig, setEditConfig] = useState<KeyRotationConfig>(config);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await onSave(editConfig);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full p-6">
        <h2 className="text-lg font-semibold mb-4">Key Rotation Settings</h2>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="flex items-center">
            <input
              type="checkbox"
              id="enabled"
              checked={editConfig.enabled}
              onChange={(e) => setEditConfig({ ...editConfig, enabled: e.target.checked })}
              className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <label htmlFor="enabled" className="ml-2 block text-sm text-gray-700">
              Enable automatic key rotation
            </label>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Key Lifetime (days)</label>
            <input
              type="number"
              value={editConfig.keyLifetimeDays || 90}
              onChange={(e) => setEditConfig({ ...editConfig, keyLifetimeDays: parseInt(e.target.value) })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
              min={7}
              max={3650}
            />
            <p className="mt-1 text-xs text-gray-500">How long keys are valid before expiring</p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Rotation Lead Time (days)</label>
            <input
              type="number"
              value={editConfig.rotationLeadDays || 14}
              onChange={(e) => setEditConfig({ ...editConfig, rotationLeadDays: parseInt(e.target.value) })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
              min={1}
              max={90}
            />
            <p className="mt-1 text-xs text-gray-500">Days before expiration to generate new key</p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Grace Period (days)</label>
            <input
              type="number"
              value={editConfig.gracePeriodDays || 30}
              onChange={(e) => setEditConfig({ ...editConfig, gracePeriodDays: parseInt(e.target.value) })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
              min={1}
              max={365}
            />
            <p className="mt-1 text-xs text-gray-500">How long expired keys remain for verification</p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Maximum Keys</label>
            <input
              type="number"
              value={editConfig.maxKeys || 5}
              onChange={(e) => setEditConfig({ ...editConfig, maxKeys: parseInt(e.target.value) })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500"
              min={2}
              max={20}
            />
            <p className="mt-1 text-xs text-gray-500">Maximum number of keys to keep (including expired)</p>
          </div>

          <div className="flex justify-end space-x-3 pt-4">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 rounded-md hover:bg-gray-200"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {submitting ? 'Saving...' : 'Save Settings'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default SigningKeysPage;

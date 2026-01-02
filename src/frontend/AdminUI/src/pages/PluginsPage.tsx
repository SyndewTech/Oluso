import { useState, useEffect, useRef } from 'react';
import { pluginService } from '../services/pluginService';
import type { PluginListItem, Plugin } from '../types/plugin';
import {Card} from '../components/common/Card';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import Input from '../components/common/Input';
import {
  PuzzlePieceIcon,
  CloudArrowUpIcon,
  TrashIcon,
  ArrowPathIcon,
  InformationCircleIcon,
  CheckCircleIcon,
  XCircleIcon,
  GlobeAltIcon,
  BuildingOfficeIcon,
} from '@heroicons/react/24/outline';

export default function PluginsPage() {
  const [plugins, setPlugins] = useState<PluginListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedPlugin, setSelectedPlugin] = useState<Plugin | null>(null);
  const [showDetailsModal, setShowDetailsModal] = useState(false);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [pluginToDelete, setPluginToDelete] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [reloading, setReloading] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Upload form state
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadName, setUploadName] = useState('');
  const [uploadDisplayName, setUploadDisplayName] = useState('');
  const [uploadDescription, setUploadDescription] = useState('');
  const [uploadVersion, setUploadVersion] = useState('1.0.0');
  const [uploadAuthor, setUploadAuthor] = useState('');

  useEffect(() => {
    loadPlugins();
  }, []);

  const loadPlugins = async () => {
    try {
      setLoading(true);
      const data = await pluginService.getPlugins();
      setPlugins(data);
      setError(null);
    } catch (err) {
      setError('Failed to load plugins');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const handleViewDetails = async (pluginName: string) => {
    try {
      const plugin = await pluginService.getPlugin(pluginName);
      setSelectedPlugin(plugin);
      setShowDetailsModal(true);
    } catch (err) {
      setError('Failed to load plugin details');
      console.error(err);
    }
  };

  const handleReload = async (pluginName: string) => {
    try {
      setReloading(pluginName);
      await pluginService.reloadPlugin(pluginName);
      await loadPlugins();
    } catch (err) {
      setError('Failed to reload plugin');
      console.error(err);
    } finally {
      setReloading(null);
    }
  };

  const handleDelete = async () => {
    if (!pluginToDelete) return;

    try {
      await pluginService.deletePlugin(pluginToDelete);
      setShowDeleteModal(false);
      setPluginToDelete(null);
      await loadPlugins();
    } catch (err) {
      setError('Failed to delete plugin');
      console.error(err);
    }
  };

  const handleUpload = async () => {
    if (!uploadFile) return;

    try {
      setUploading(true);
      await pluginService.uploadPlugin(uploadFile, {
        name: uploadName || undefined,
        displayName: uploadDisplayName || undefined,
        description: uploadDescription || undefined,
        version: uploadVersion || undefined,
        author: uploadAuthor || undefined,
      });
      setShowUploadModal(false);
      resetUploadForm();
      await loadPlugins();
    } catch (err) {
      setError('Failed to upload plugin');
      console.error(err);
    } finally {
      setUploading(false);
    }
  };

  const resetUploadForm = () => {
    setUploadFile(null);
    setUploadName('');
    setUploadDisplayName('');
    setUploadDescription('');
    setUploadVersion('1.0.0');
    setUploadAuthor('');
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const formatBytes = (bytes: number): string => {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const formatDate = (dateString: string): string => {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  if (loading) {
    return (
      <div className="flex justify-center items-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">WASM Plugins</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage custom WASM plugins for user journey steps
          </p>
        </div>
        <Button onClick={() => setShowUploadModal(true)}>
          <CloudArrowUpIcon className="h-5 w-5 mr-2" />
          Upload Plugin
        </Button>
      </div>

      {error && (
        <div className="rounded-md bg-red-50 p-4">
          <div className="flex">
            <XCircleIcon className="h-5 w-5 text-red-400" />
            <div className="ml-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          </div>
        </div>
      )}

      <Card>
        {plugins.length === 0 ? (
          <div className="text-center py-12">
            <PuzzlePieceIcon className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">No plugins</h3>
            <p className="mt-1 text-sm text-gray-500">
              Get started by uploading a WASM plugin.
            </p>
            <div className="mt-6">
              <Button onClick={() => setShowUploadModal(true)}>
                <CloudArrowUpIcon className="h-5 w-5 mr-2" />
                Upload Plugin
              </Button>
            </div>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Plugin
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Scope
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Version
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Size
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Updated
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {plugins.map((plugin) => (
                  <tr key={plugin.name} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <PuzzlePieceIcon className="h-5 w-5 text-primary-500 flex-shrink-0" />
                        <div className="ml-3">
                          <div className="text-sm font-medium text-gray-900">
                            {plugin.displayName}
                          </div>
                          <div className="text-sm text-gray-500">{plugin.name}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span
                        className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                          plugin.isGlobal
                            ? 'bg-purple-100 text-purple-800'
                            : 'bg-blue-100 text-blue-800'
                        }`}
                      >
                        {plugin.isGlobal ? (
                          <>
                            <GlobeAltIcon className="h-3 w-3 mr-1" />
                            Global
                          </>
                        ) : (
                          <>
                            <BuildingOfficeIcon className="h-3 w-3 mr-1" />
                            Tenant
                          </>
                        )}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {plugin.version || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {formatBytes(plugin.sizeBytes)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {plugin.isLoaded ? (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                          <CheckCircleIcon className="h-3 w-3 mr-1" />
                          Loaded
                        </span>
                      ) : (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                          <XCircleIcon className="h-3 w-3 mr-1" />
                          Not Loaded
                        </span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {plugin.updatedAt ? formatDate(plugin.updatedAt) : formatDate(plugin.createdAt)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                      <div className="flex justify-end space-x-2">
                        <button
                          onClick={() => handleViewDetails(plugin.name)}
                          className="text-primary-600 hover:text-primary-900"
                          title="View Details"
                        >
                          <InformationCircleIcon className="h-5 w-5" />
                        </button>
                        <button
                          onClick={() => handleReload(plugin.name)}
                          className="text-blue-600 hover:text-blue-900"
                          disabled={reloading === plugin.name}
                          title="Reload Plugin"
                        >
                          <ArrowPathIcon
                            className={`h-5 w-5 ${reloading === plugin.name ? 'animate-spin' : ''}`}
                          />
                        </button>
                        {!plugin.isGlobal && (
                          <button
                            onClick={() => {
                              setPluginToDelete(plugin.name);
                              setShowDeleteModal(true);
                            }}
                            className="text-red-600 hover:text-red-900"
                            title="Delete Plugin"
                          >
                            <TrashIcon className="h-5 w-5" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Upload Modal */}
      <Modal
        isOpen={showUploadModal}
        onClose={() => {
          setShowUploadModal(false);
          resetUploadForm();
        }}
        title="Upload WASM Plugin"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              WASM File *
            </label>
            <input
              type="file"
              ref={fileInputRef}
              accept=".wasm"
              onChange={(e) => setUploadFile(e.target.files?.[0] || null)}
              className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-primary-50 file:text-primary-700 hover:file:bg-primary-100"
            />
            <p className="mt-1 text-xs text-gray-500">Max file size: 10MB</p>
          </div>

          <Input
            label="Plugin Name"
            value={uploadName}
            onChange={(e) => setUploadName(e.target.value)}
            placeholder="my-custom-plugin"
            helperText="Leave empty to use filename"
          />

          <Input
            label="Display Name"
            value={uploadDisplayName}
            onChange={(e) => setUploadDisplayName(e.target.value)}
            placeholder="My Custom Plugin"
          />

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Description
            </label>
            <textarea
              value={uploadDescription}
              onChange={(e) => setUploadDescription(e.target.value)}
              rows={3}
              className="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              placeholder="Describe what this plugin does..."
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <Input
              label="Version"
              value={uploadVersion}
              onChange={(e) => setUploadVersion(e.target.value)}
              placeholder="1.0.0"
            />
            <Input
              label="Author"
              value={uploadAuthor}
              onChange={(e) => setUploadAuthor(e.target.value)}
              placeholder="Your name"
            />
          </div>

          <div className="flex justify-end space-x-3 pt-4">
            <Button
              variant="secondary"
              onClick={() => {
                setShowUploadModal(false);
                resetUploadForm();
              }}
            >
              Cancel
            </Button>
            <Button onClick={handleUpload} disabled={!uploadFile || uploading}>
              {uploading ? (
                <>
                  <ArrowPathIcon className="h-4 w-4 mr-2 animate-spin" />
                  Uploading...
                </>
              ) : (
                <>
                  <CloudArrowUpIcon className="h-4 w-4 mr-2" />
                  Upload
                </>
              )}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Details Modal */}
      <Modal
        isOpen={showDetailsModal}
        onClose={() => setShowDetailsModal(false)}
        title="Plugin Details"
      >
        {selectedPlugin && (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Name</label>
                <p className="text-sm text-gray-900">{selectedPlugin.name}</p>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Display Name</label>
                <p className="text-sm text-gray-900">{selectedPlugin.displayName}</p>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Version</label>
                <p className="text-sm text-gray-900">{selectedPlugin.version || '-'}</p>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Author</label>
                <p className="text-sm text-gray-900">{selectedPlugin.author || '-'}</p>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Scope</label>
                <p className="text-sm text-gray-900">{selectedPlugin.scope}</p>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Size</label>
                <p className="text-sm text-gray-900">{formatBytes(selectedPlugin.sizeBytes)}</p>
              </div>
            </div>

            {selectedPlugin.description && (
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Description</label>
                <p className="text-sm text-gray-900">{selectedPlugin.description}</p>
              </div>
            )}

            {selectedPlugin.requiredClaims && selectedPlugin.requiredClaims.length > 0 && (
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Required Claims</label>
                <div className="flex flex-wrap gap-1 mt-1">
                  {selectedPlugin.requiredClaims.map((claim) => (
                    <span
                      key={claim}
                      className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800"
                    >
                      {claim}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {selectedPlugin.outputClaims && selectedPlugin.outputClaims.length > 0 && (
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Output Claims</label>
                <div className="flex flex-wrap gap-1 mt-1">
                  {selectedPlugin.outputClaims.map((claim) => (
                    <span
                      key={claim}
                      className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800"
                    >
                      {claim}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {selectedPlugin.configSchema && (
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Config Schema</label>
                <pre className="mt-1 p-2 bg-gray-50 rounded text-xs overflow-auto max-h-40">
                  {JSON.stringify(selectedPlugin.configSchema, null, 2)}
                </pre>
              </div>
            )}

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-xs font-medium text-gray-500 uppercase">Created</label>
                <p className="text-sm text-gray-900">{formatDate(selectedPlugin.createdAt)}</p>
              </div>
              {selectedPlugin.updatedAt && (
                <div>
                  <label className="text-xs font-medium text-gray-500 uppercase">Updated</label>
                  <p className="text-sm text-gray-900">{formatDate(selectedPlugin.updatedAt)}</p>
                </div>
              )}
            </div>

            <div className="flex justify-end pt-4">
              <Button variant="secondary" onClick={() => setShowDetailsModal(false)}>
                Close
              </Button>
            </div>
          </div>
        )}
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={showDeleteModal}
        onClose={() => {
          setShowDeleteModal(false);
          setPluginToDelete(null);
        }}
        title="Delete Plugin"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-500">
            Are you sure you want to delete the plugin <strong>{pluginToDelete}</strong>? This action
            cannot be undone.
          </p>
          <div className="flex justify-end space-x-3">
            <Button
              variant="secondary"
              onClick={() => {
                setShowDeleteModal(false);
                setPluginToDelete(null);
              }}
            >
              Cancel
            </Button>
            <Button variant="danger" onClick={handleDelete}>
              <TrashIcon className="h-4 w-4 mr-2" />
              Delete
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

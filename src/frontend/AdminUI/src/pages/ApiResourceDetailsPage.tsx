import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import Button from '../components/common/Button';
import { Table } from '../components/common/Table';
import Modal from '../components/common/Modal';
import { apiResourceService } from '../services/resourceService';
import type { CreateApiResourceRequest } from '../types/resources';
import { ArrowLeftIcon, PlusIcon, TrashIcon, PencilIcon } from '@heroicons/react/24/outline';
import type { UpdateApiResourceRequest } from '../types/resources';

export default function ApiResourceDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [showScopeModal, setShowScopeModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showClaimsModal, setShowClaimsModal] = useState(false);
  const [newClaim, setNewClaim] = useState('');
  const [editFormData, setEditFormData] = useState<UpdateApiResourceRequest>({});
  const [formData, setFormData] = useState<CreateApiResourceRequest>({
    name: '',
    displayName: '',
    description: '',
    enabled: true,
    showInDiscoveryDocument: true,
  });

  const isNew = id === 'new';
  const numericId = isNew ? 0 : parseInt(id!, 10);

  const { data: resource, isLoading } = useQuery({
    queryKey: ['api-resource', numericId],
    queryFn: () => apiResourceService.getById(numericId),
    enabled: !isNew && !isNaN(numericId),
  });

  const { data: availableScopes = [] } = useQuery({
    queryKey: ['available-scopes'],
    queryFn: () => apiResourceService.getAvailableScopes(),
    enabled: !isNew,
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateApiResourceRequest) => apiResourceService.create(data),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ['api-resources'] });
      navigate(`/api-resources/${created.id}`);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (resourceId: number) => apiResourceService.delete(resourceId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-resources'] });
      navigate('/api-resources');
    },
  });

  const addScopeMutation = useMutation({
    mutationFn: (scopeName: string) => apiResourceService.addScope(numericId, scopeName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-resource', numericId] });
      queryClient.invalidateQueries({ queryKey: ['available-scopes'] });
      setShowScopeModal(false);
    },
    onError: (error) => {
      console.error('Failed to add scope:', error);
      alert('Failed to add scope. Check console for details.');
    },
  });

  const removeScopeMutation = useMutation({
    mutationFn: (scopeName: string) => apiResourceService.removeScope(numericId, scopeName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-resource', numericId] });
    },
  });

  const updateMutation = useMutation({
    mutationFn: (data: UpdateApiResourceRequest) => apiResourceService.update(numericId, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-resource', numericId] });
      queryClient.invalidateQueries({ queryKey: ['api-resources'] });
      setShowEditModal(false);
    },
  });

  const updateClaimsMutation = useMutation({
    mutationFn: (claims: string[]) => apiResourceService.update(numericId, { userClaims: claims }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-resource', numericId] });
      setShowClaimsModal(false);
      setNewClaim('');
    },
  });

  const handleAddClaim = () => {
    if (newClaim && resource) {
      const currentClaims = resource.userClaims || [];
      if (!currentClaims.includes(newClaim)) {
        updateClaimsMutation.mutate([...currentClaims, newClaim]);
      }
    }
  };

  const handleRemoveClaim = (claimToRemove: string) => {
    if (resource) {
      const currentClaims = resource.userClaims || [];
      updateClaimsMutation.mutate(currentClaims.filter(c => c !== claimToRemove));
    }
  };

  const openEditModal = () => {
    if (resource) {
      setEditFormData({
        displayName: resource.displayName || '',
        description: resource.description || '',
        enabled: resource.enabled,
        showInDiscoveryDocument: resource.showInDiscoveryDocument,
        requireResourceIndicator: resource.requireResourceIndicator,
        allowedAccessTokenSigningAlgorithms: resource.allowedAccessTokenSigningAlgorithms || '',
      });
      setShowEditModal(true);
    }
  };

  const handleUpdate = () => {
    updateMutation.mutate(editFormData);
  };

  const handleCreate = () => {
    if (formData.name) {
      createMutation.mutate(formData);
    }
  };

  const handleDelete = () => {
    if (resource) {
      deleteMutation.mutate(resource.id);
    }
  };

  // Get scopes not already assigned to this resource
  const unassignedScopes = availableScopes?.filter(
    (scope) => !resource?.scopes?.includes(scope.name)
  ) || [];

  if (isNew) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/api-resources')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <h1 className="text-2xl font-bold text-gray-900">Create API Resource</h1>
        </div>

        <Card>
          <CardContent className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Name *</label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="e.g., my-api"
              />
              <p className="mt-1 text-xs text-gray-500">Unique identifier for this API resource</p>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Display Name</label>
              <input
                type="text"
                value={formData.displayName || ''}
                onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="e.g., My API"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Description</label>
              <textarea
                value={formData.description || ''}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                rows={3}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              />
            </div>

            <div className="flex items-center gap-4">
              <label className="flex items-center">
                <input
                  type="checkbox"
                  checked={formData.enabled ?? true}
                  onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <span className="ml-2 text-sm text-gray-700">Enabled</span>
              </label>

              <label className="flex items-center">
                <input
                  type="checkbox"
                  checked={formData.showInDiscoveryDocument ?? true}
                  onChange={(e) => setFormData({ ...formData, showInDiscoveryDocument: e.target.checked })}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <span className="ml-2 text-sm text-gray-700">Show in Discovery Document</span>
              </label>
            </div>

            <div className="flex justify-end gap-2 pt-4">
              <Button variant="secondary" onClick={() => navigate('/api-resources')}>
                Cancel
              </Button>
              <Button onClick={handleCreate} disabled={!formData.name || createMutation.isPending}>
                {createMutation.isPending ? 'Creating...' : 'Create API Resource'}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (isLoading) {
    return <div className="flex items-center justify-center h-64">Loading...</div>;
  }

  if (!resource) {
    return <div className="text-center py-8 text-gray-500">API Resource not found</div>;
  }

  const scopeColumns = [
    { key: 'name', header: 'Scope Name' },
    {
      key: 'actions',
      header: '',
      render: (item: {name: string}) => (  // (scope: string) => (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => removeScopeMutation.mutate(item.name)}
          disabled={removeScopeMutation.isPending}
        >
          <TrashIcon className="h-4 w-4 text-red-500" />
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/api-resources')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">{resource.displayName || resource.name}</h1>
            <p className="text-sm text-gray-500">{resource.name}</p>
          </div>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={openEditModal}>
            <PencilIcon className="h-4 w-4 mr-2" />
            Edit
          </Button>
          <Button variant="danger" onClick={() => setShowDeleteModal(true)}>
            Delete
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader title="Basic Information" />
          <CardContent>
            <dl className="space-y-4">
              <div>
                <dt className="text-sm font-medium text-gray-500">Name</dt>
                <dd className="mt-1 text-sm text-gray-900">{resource.name}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Display Name</dt>
                <dd className="mt-1 text-sm text-gray-900">{resource.displayName || '-'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Description</dt>
                <dd className="mt-1 text-sm text-gray-900">{resource.description || '-'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Status</dt>
                <dd className="mt-1">
                  <span
                    className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
                      resource.enabled ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
                    }`}
                  >
                    {resource.enabled ? 'Enabled' : 'Disabled'}
                  </span>
                </dd>
              </div>
            </dl>
          </CardContent>
        </Card>

        <Card>
          <CardHeader title="Settings" />
          <CardContent>
            <dl className="space-y-4">
              <div>
                <dt className="text-sm font-medium text-gray-500">Show in Discovery Document</dt>
                <dd className="mt-1 text-sm text-gray-900">{resource.showInDiscoveryDocument ? 'Yes' : 'No'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Require Resource Indicator</dt>
                <dd className="mt-1 text-sm text-gray-900">{resource.requireResourceIndicator ? 'Yes' : 'No'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Allowed Signing Algorithms</dt>
                <dd className="mt-1 text-sm text-gray-900">{resource.allowedAccessTokenSigningAlgorithms || 'Default'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Created</dt>
                <dd className="mt-1 text-sm text-gray-900">{new Date(resource.created).toLocaleString()}</dd>
              </div>
            </dl>
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader
            title="Scopes"
            action={
              <Button size="sm" onClick={() => setShowScopeModal(true)}>
                <PlusIcon className="h-4 w-4 mr-2" />
                Add Scope
              </Button>
            }
          />
          <CardContent>
            <p className="text-sm text-gray-500 mb-4">
              Scopes define what permissions are available for this API resource. Clients can request these scopes
              in their access tokens.
            </p>
            {resource.scopes && resource.scopes.length > 0 ? (
              <Table
                columns={scopeColumns}
                data={resource.scopes.map((s) => ({ name: s }))}
                keyExtractor={(item) => item.name}
              />
            ) : (
              <div className="text-center py-8 text-gray-500">
                No scopes configured. Add scopes to allow clients to request access to this API.
              </div>
            )}
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader
            title="User Claims"
            action={
              <Button size="sm" onClick={() => setShowClaimsModal(true)}>
                <PencilIcon className="h-4 w-4 mr-2" />
                Edit Claims
              </Button>
            }
          />
          <CardContent>
            <p className="text-sm text-gray-500 mb-4">
              Claims that will be included in access tokens for this resource.
            </p>
            <div className="flex flex-wrap gap-2">
              {resource.userClaims && resource.userClaims.length > 0 ? (
                resource.userClaims.map((claim) => (
                  <span
                    key={claim}
                    className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-800"
                  >
                    {claim}
                  </span>
                ))
              ) : (
                <span className="text-sm text-gray-500">No claims configured</span>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Add Scope Modal */}
      <Modal
        isOpen={showScopeModal}
        onClose={() => setShowScopeModal(false)}
        title="Add Scope to Resource"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-500">
            Select a scope to add to this API resource. Clients can then request this scope to access the resource.
          </p>
          {unassignedScopes.length > 0 ? (
            <div className="max-h-64 overflow-y-auto space-y-2">
              {unassignedScopes.map((scope) => (
                <button
                  key={scope.name}
                  onClick={() => addScopeMutation.mutate(scope.name)}
                  disabled={addScopeMutation.isPending}
                  className="w-full text-left p-3 rounded-md border border-gray-200 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500"
                >
                  <div className="font-medium text-gray-900">{scope.name}</div>
                  {scope.displayName && (
                    <div className="text-sm text-gray-500">{scope.displayName}</div>
                  )}
                  {scope.description && (
                    <div className="text-xs text-gray-400 mt-1">{scope.description}</div>
                  )}
                </button>
              ))}
            </div>
          ) : (
            <div className="text-center py-8 text-gray-500">
              All available scopes are already assigned to this resource.
            </div>
          )}
        </div>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={showDeleteModal}
        onClose={() => setShowDeleteModal(false)}
        title="Delete API Resource"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Are you sure you want to delete the API resource <strong>{resource.name}</strong>?
            This action cannot be undone.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setShowDeleteModal(false)}>
              Cancel
            </Button>
            <Button variant="danger" onClick={handleDelete} disabled={deleteMutation.isPending}>
              {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Edit Modal */}
      <Modal
        isOpen={showEditModal}
        onClose={() => setShowEditModal(false)}
        title="Edit API Resource"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Display Name</label>
            <input
              type="text"
              value={editFormData.displayName || ''}
              onChange={(e) => setEditFormData({ ...editFormData, displayName: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Description</label>
            <textarea
              value={editFormData.description || ''}
              onChange={(e) => setEditFormData({ ...editFormData, description: e.target.value })}
              rows={3}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
            />
          </div>

          <div className="flex flex-wrap gap-4">
            <label className="flex items-center">
              <input
                type="checkbox"
                checked={editFormData.enabled ?? true}
                onChange={(e) => setEditFormData({ ...editFormData, enabled: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Enabled</span>
            </label>

            <label className="flex items-center">
              <input
                type="checkbox"
                checked={editFormData.showInDiscoveryDocument ?? true}
                onChange={(e) => setEditFormData({ ...editFormData, showInDiscoveryDocument: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Show in Discovery</span>
            </label>

            <label className="flex items-center">
              <input
                type="checkbox"
                checked={editFormData.requireResourceIndicator ?? false}
                onChange={(e) => setEditFormData({ ...editFormData, requireResourceIndicator: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Require Resource Indicator</span>
            </label>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Allowed Signing Algorithms</label>
            <input
              type="text"
              value={editFormData.allowedAccessTokenSigningAlgorithms || ''}
              onChange={(e) => setEditFormData({ ...editFormData, allowedAccessTokenSigningAlgorithms: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              placeholder="e.g., RS256, RS384"
            />
            <p className="mt-1 text-xs text-gray-500">Leave empty for default algorithms</p>
          </div>

          <div className="flex justify-end gap-2 pt-4">
            <Button variant="secondary" onClick={() => setShowEditModal(false)}>
              Cancel
            </Button>
            <Button onClick={handleUpdate} disabled={updateMutation.isPending}>
              {updateMutation.isPending ? 'Saving...' : 'Save Changes'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Edit Claims Modal */}
      <Modal
        isOpen={showClaimsModal}
        onClose={() => {
          setShowClaimsModal(false);
          setNewClaim('');
        }}
        title="Edit User Claims"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-500">
            Add or remove claims that will be included in access tokens for this resource.
          </p>

          {/* Add new claim */}
          <div className="flex gap-2">
            <input
              type="text"
              value={newClaim}
              onChange={(e) => setNewClaim(e.target.value)}
              placeholder="Enter claim type (e.g., sub, email, role)"
              className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  handleAddClaim();
                }
              }}
            />
            <Button
              onClick={handleAddClaim}
              disabled={!newClaim || updateClaimsMutation.isPending}
            >
              <PlusIcon className="h-4 w-4 mr-1" />
              Add
            </Button>
          </div>

          {/* Current claims */}
          <div className="border rounded-md divide-y max-h-64 overflow-y-auto">
            {resource.userClaims && resource.userClaims.length > 0 ? (
              resource.userClaims.map((claim) => (
                <div key={claim} className="flex items-center justify-between p-3">
                  <span className="text-sm text-gray-900">{claim}</span>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => handleRemoveClaim(claim)}
                    disabled={updateClaimsMutation.isPending}
                  >
                    <TrashIcon className="h-4 w-4 text-red-500" />
                  </Button>
                </div>
              ))
            ) : (
              <div className="p-4 text-center text-sm text-gray-500">
                No claims configured yet
              </div>
            )}
          </div>

          <div className="text-xs text-gray-500">
            <p className="font-medium mb-1">Common claims:</p>
            <div className="flex flex-wrap gap-1">
              {['sub', 'name', 'email', 'role', 'groups', 'tenant_id'].map((claim) => (
                <button
                  key={claim}
                  onClick={() => setNewClaim(claim)}
                  className="px-2 py-0.5 bg-gray-100 rounded text-gray-600 hover:bg-gray-200"
                >
                  {claim}
                </button>
              ))}
            </div>
          </div>

          <div className="flex justify-end pt-2">
            <Button
              variant="secondary"
              onClick={() => {
                setShowClaimsModal(false);
                setNewClaim('');
              }}
            >
              Done
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { apiScopeService } from '../services/resourceService';
import type { CreateApiScopeRequest, UpdateApiScopeRequest } from '../types/resources';
import { ArrowLeftIcon, PlusIcon, XMarkIcon, PencilIcon, TrashIcon } from '@heroicons/react/24/outline';

export default function ApiScopeDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [showResourceModal, setShowResourceModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showClaimsModal, setShowClaimsModal] = useState(false);
  const [newClaim, setNewClaim] = useState('');
  const [editFormData, setEditFormData] = useState<UpdateApiScopeRequest>({});
  const [formData, setFormData] = useState<CreateApiScopeRequest>({
    name: '',
    displayName: '',
    description: '',
    enabled: true,
    required: false,
    emphasize: false,
    showInDiscoveryDocument: true,
  });

  const isNew = id === 'new';
  const numericId = isNew ? 0 : parseInt(id!, 10);

  const { data: scope, isLoading } = useQuery({
    queryKey: ['api-scope', numericId],
    queryFn: () => apiScopeService.getById(numericId),
    enabled: !isNew && !isNaN(numericId),
  });

  const { data: availableResources = [] } = useQuery({
    queryKey: ['available-resources'],
    queryFn: () => apiScopeService.getAvailableResources(),
    enabled: !isNew,
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateApiScopeRequest) => apiScopeService.create(data),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ['api-scopes'] });
      navigate(`/api-scopes/${created.id}`);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (scopeId: number) => apiScopeService.delete(scopeId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-scopes'] });
      navigate('/api-scopes');
    },
  });

  const updateResourcesMutation = useMutation({
    mutationFn: (resourceNames: string[]) =>
      apiScopeService.update(numericId, { apiResourceNames: resourceNames }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-scope', numericId] });
      queryClient.invalidateQueries({ queryKey: ['available-resources'] });
      setShowResourceModal(false);
    },
    onError: (error) => {
      console.error('Failed to update resources:', error);
      alert('Failed to update resources. Check console for details.');
    },
  });

  const handleCreate = () => {
    if (formData.name) {
      createMutation.mutate(formData);
    }
  };

  const handleDelete = () => {
    if (scope) {
      deleteMutation.mutate(scope.id);
    }
  };

  const handleAddResource = (resourceName: string) => {
    const currentResources = scope?.apiResourceNames || [];
    if (!currentResources.includes(resourceName)) {
      updateResourcesMutation.mutate([...currentResources, resourceName]);
    }
  };

  const handleRemoveResource = (resourceName: string) => {
    const currentResources = scope?.apiResourceNames || [];
    updateResourcesMutation.mutate(currentResources.filter((r) => r !== resourceName));
  };

  const updateMutation = useMutation({
    mutationFn: (data: UpdateApiScopeRequest) => apiScopeService.update(numericId, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-scope', numericId] });
      queryClient.invalidateQueries({ queryKey: ['api-scopes'] });
      setShowEditModal(false);
    },
  });

  const updateClaimsMutation = useMutation({
    mutationFn: (claims: string[]) => apiScopeService.update(numericId, { userClaims: claims }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-scope', numericId] });
      setShowClaimsModal(false);
      setNewClaim('');
    },
  });

  const handleAddClaim = () => {
    if (newClaim && scope) {
      const currentClaims = scope.userClaims || [];
      if (!currentClaims.includes(newClaim)) {
        updateClaimsMutation.mutate([...currentClaims, newClaim]);
      }
    }
  };

  const handleRemoveClaim = (claimToRemove: string) => {
    if (scope) {
      const currentClaims = scope.userClaims || [];
      updateClaimsMutation.mutate(currentClaims.filter(c => c !== claimToRemove));
    }
  };

  const openEditModal = () => {
    if (scope) {
      setEditFormData({
        displayName: scope.displayName || '',
        description: scope.description || '',
        enabled: scope.enabled,
        required: scope.required,
        emphasize: scope.emphasize,
        showInDiscoveryDocument: scope.showInDiscoveryDocument,
      });
      setShowEditModal(true);
    }
  };

  const handleUpdate = () => {
    updateMutation.mutate(editFormData);
  };

  // Get resources not already associated with this scope
  const unassociatedResources = availableResources?.filter(
    (resource) => !scope?.apiResourceNames?.includes(resource.name)
  ) || [];

  if (isNew) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/api-scopes')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <h1 className="text-2xl font-bold text-gray-900">Create API Scope</h1>
        </div>

        <Card>
          <CardContent className="space-y-4">
            <div>
              <label className="form-label">Name *</label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="e.g., read:orders"
              />
              <p className="mt-1 text-xs text-gray-500">Unique identifier for this scope (permission)</p>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Display Name</label>
              <input
                type="text"
                value={formData.displayName || ''}
                onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="e.g., Read Orders"
              />
              <p className="mt-1 text-xs text-gray-500">Shown to users on consent screen</p>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Description</label>
              <textarea
                value={formData.description || ''}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                rows={3}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
                placeholder="Describe what this scope allows"
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
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
                  checked={formData.required ?? false}
                  onChange={(e) => setFormData({ ...formData, required: e.target.checked })}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <span className="ml-2 text-sm text-gray-700">Required</span>
              </label>

              <label className="flex items-center">
                <input
                  type="checkbox"
                  checked={formData.emphasize ?? false}
                  onChange={(e) => setFormData({ ...formData, emphasize: e.target.checked })}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <span className="ml-2 text-sm text-gray-700">Emphasize on consent</span>
              </label>

              <label className="flex items-center">
                <input
                  type="checkbox"
                  checked={formData.showInDiscoveryDocument ?? true}
                  onChange={(e) => setFormData({ ...formData, showInDiscoveryDocument: e.target.checked })}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <span className="ml-2 text-sm text-gray-700">Show in Discovery</span>
              </label>
            </div>

            <div className="flex justify-end gap-2 pt-4">
              <Button variant="secondary" onClick={() => navigate('/api-scopes')}>
                Cancel
              </Button>
              <Button onClick={handleCreate} disabled={!formData.name || createMutation.isPending}>
                {createMutation.isPending ? 'Creating...' : 'Create API Scope'}
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

  if (!scope) {
    return <div className="text-center py-8 text-gray-500">API Scope not found</div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/api-scopes')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">{scope.displayName || scope.name}</h1>
            <p className="text-sm text-gray-500">{scope.name}</p>
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
              <dd className="mt-1 text-sm text-gray-900">{scope.name}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Display Name</dt>
              <dd className="mt-1 text-sm text-gray-900">{scope.displayName || '-'}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Description</dt>
              <dd className="mt-1 text-sm text-gray-900">{scope.description || '-'}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Status</dt>
              <dd className="mt-1">
                <span
                  className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
                    scope.enabled ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
                  }`}
                >
                  {scope.enabled ? 'Enabled' : 'Disabled'}
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
              <dt className="text-sm font-medium text-gray-500">Required</dt>
              <dd className="mt-1 text-sm text-gray-900">{scope.required ? 'Yes' : 'No'}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Emphasize on Consent</dt>
              <dd className="mt-1 text-sm text-gray-900">{scope.emphasize ? 'Yes' : 'No'}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Show in Discovery Document</dt>
              <dd className="mt-1 text-sm text-gray-900">{scope.showInDiscoveryDocument ? 'Yes' : 'No'}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Created</dt>
              <dd className="mt-1 text-sm text-gray-900">{new Date(scope.created).toLocaleString()}</dd>
            </div>
          </dl>
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader
            title="API Resources"
            action={
              <Button size="sm" onClick={() => setShowResourceModal(true)}>
                <PlusIcon className="h-4 w-4 mr-2" />
                Add to Resource
              </Button>
            }
          />
          <CardContent>
          <p className="text-sm text-gray-500 mb-4">
            API Resources this scope belongs to. A scope can belong to multiple resources.
            Clients requesting this scope will receive access tokens for these resources.
          </p>
          {scope.apiResourceNames && scope.apiResourceNames.length > 0 ? (
            <div className="flex flex-wrap gap-2">
              {scope.apiResourceNames.map((resourceName) => (
                <span
                  key={resourceName}
                  className="inline-flex items-center gap-1 rounded-full bg-blue-100 px-3 py-1 text-sm font-medium text-blue-800"
                >
                  {resourceName}
                  <button
                    onClick={() => handleRemoveResource(resourceName)}
                    className="ml-1 hover:text-blue-600"
                    disabled={updateResourcesMutation.isPending}
                  >
                    <XMarkIcon className="h-4 w-4" />
                  </button>
                </span>
              ))}
            </div>
          ) : (
            <div className="text-center py-8 text-gray-500">
              This scope is not associated with any API resource. Add it to a resource
              so clients can request it.
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
            Claims that will be included in access tokens when this scope is requested.
          </p>
          <div className="flex flex-wrap gap-2">
            {scope.userClaims && scope.userClaims.length > 0 ? (
              scope.userClaims.map((claim) => (
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

      {/* Add to Resource Modal */}
      <Modal
        isOpen={showResourceModal}
        onClose={() => setShowResourceModal(false)}
        title="Add Scope to API Resource"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-500">
            Select an API resource to add this scope to. This allows clients to request
            this scope when accessing that resource.
          </p>
          {unassociatedResources.length > 0 ? (
            <div className="max-h-64 overflow-y-auto space-y-2">
              {unassociatedResources.map((resource) => (
                <button
                  key={resource.name}
                  onClick={() => handleAddResource(resource.name)}
                  disabled={updateResourcesMutation.isPending}
                  className="w-full text-left p-3 rounded-md border border-gray-200 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500"
                >
                  <div className="font-medium text-gray-900">{resource.name}</div>
                  {resource.displayName && (
                    <div className="text-sm text-gray-500">{resource.displayName}</div>
                  )}
                  <div className="text-xs text-gray-400 mt-1">
                    {resource.scopeCount} scope{resource.scopeCount !== 1 ? 's' : ''}
                  </div>
                </button>
              ))}
            </div>
          ) : (
            <div className="text-center py-8 text-gray-500">
              This scope is already associated with all available resources.
            </div>
          )}
        </div>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={showDeleteModal}
        onClose={() => setShowDeleteModal(false)}
        title="Delete API Scope"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Are you sure you want to delete the API scope <strong>{scope.name}</strong>?
            This will remove the scope from all associated resources. This action cannot be undone.
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
        title="Edit API Scope"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Display Name</label>
            <input
              type="text"
              value={editFormData.displayName || ''}
              onChange={(e) => setEditFormData({ ...editFormData, displayName: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm"
              placeholder="e.g., Read Orders"
            />
            <p className="mt-1 text-xs text-gray-500">Shown to users on consent screen</p>
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

          <div className="grid grid-cols-2 gap-4">
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
                checked={editFormData.required ?? false}
                onChange={(e) => setEditFormData({ ...editFormData, required: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Required</span>
            </label>

            <label className="flex items-center">
              <input
                type="checkbox"
                checked={editFormData.emphasize ?? false}
                onChange={(e) => setEditFormData({ ...editFormData, emphasize: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              <span className="ml-2 text-sm text-gray-700">Emphasize on consent</span>
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
            Add or remove claims that will be included in access tokens when this scope is requested.
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
            {scope.userClaims && scope.userClaims.length > 0 ? (
              scope.userClaims.map((claim) => (
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

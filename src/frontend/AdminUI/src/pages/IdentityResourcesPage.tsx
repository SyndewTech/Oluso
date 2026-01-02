import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { identityResourceService } from '../services/resourceService';
import type { IdentityResource, CreateIdentityResourceRequest } from '../types/resources';
import { PlusIcon, PencilIcon, TrashIcon } from '@heroicons/react/24/outline';

interface ResourceFormData {
  name: string;
  displayName: string;
  description: string;
  enabled: boolean;
  required: boolean;
  emphasize: boolean;
  showInDiscoveryDocument: boolean;
  userClaims: string[];
}

export default function IdentityResourcesPage() {
  const queryClient = useQueryClient();
  const [page] = useState(1);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [selectedResource, setSelectedResource] = useState<IdentityResource | null>(null);
  const [newClaim, setNewClaim] = useState('');
  const [formData, setFormData] = useState<ResourceFormData>({
    name: '',
    displayName: '',
    description: '',
    enabled: true,
    required: false,
    emphasize: false,
    showInDiscoveryDocument: true,
    userClaims: [],
  });

  const { data, isLoading } = useQuery({
    queryKey: ['identity-resources', page],
    queryFn: () => identityResourceService.getAll(page),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateIdentityResourceRequest) => identityResourceService.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['identity-resources'] });
      closeModal();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: number; data: Partial<CreateIdentityResourceRequest> }) =>
      identityResourceService.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['identity-resources'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => identityResourceService.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['identity-resources'] });
      setIsDeleteModalOpen(false);
      setSelectedResource(null);
    },
  });

  const openCreateModal = () => {
    setSelectedResource(null);
    setFormData({
      name: '',
      displayName: '',
      description: '',
      enabled: true,
      required: false,
      emphasize: false,
      showInDiscoveryDocument: true,
      userClaims: [],
    });
    setIsModalOpen(true);
  };

  const openEditModal = (resource: IdentityResource) => {
    setSelectedResource(resource);
    setFormData({
      name: resource.name,
      displayName: resource.displayName || '',
      description: resource.description || '',
      enabled: resource.enabled,
      required: resource.required || false,
      emphasize: resource.emphasize || false,
      showInDiscoveryDocument: resource.showInDiscoveryDocument ?? true,
      userClaims: resource.userClaims || [],
    });
    setIsModalOpen(true);
  };

  const openDeleteModal = (resource: IdentityResource) => {
    setSelectedResource(resource);
    setIsDeleteModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedResource(null);
    setNewClaim('');
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedResource) {
      updateMutation.mutate({
        id: selectedResource.id,
        data: {
          displayName: formData.displayName || undefined,
          description: formData.description || undefined,
          enabled: formData.enabled,
          required: formData.required,
          emphasize: formData.emphasize,
          showInDiscoveryDocument: formData.showInDiscoveryDocument,
          userClaims: formData.userClaims,
        },
      });
    } else {
      createMutation.mutate({
        name: formData.name,
        displayName: formData.displayName || undefined,
        description: formData.description || undefined,
        enabled: formData.enabled,
        required: formData.required,
        emphasize: formData.emphasize,
        showInDiscoveryDocument: formData.showInDiscoveryDocument,
        userClaims: formData.userClaims,
      });
    }
  };

  const addClaim = () => {
    if (newClaim && !formData.userClaims.includes(newClaim)) {
      setFormData((prev) => ({
        ...prev,
        userClaims: [...prev.userClaims, newClaim],
      }));
      setNewClaim('');
    }
  };

  const removeClaim = (claim: string) => {
    setFormData((prev) => ({
      ...prev,
      userClaims: prev.userClaims.filter((c) => c !== claim),
    }));
  };

  const columns = [
    { key: 'name', header: 'Name' },
    { key: 'displayName', header: 'Display Name' },
    {
      key: 'enabled',
      header: 'Status',
      render: (resource: IdentityResource) => (
        <span
          className={`badge ${resource.enabled ? 'badge-success' : 'badge-danger'}`}
        >
          {resource.enabled ? 'Enabled' : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'required',
      header: 'Required',
      render: (resource: IdentityResource) => (resource.required ? 'Yes' : 'No'),
    },
    {
      key: 'userClaims',
      header: 'Claims',
      render: (resource: IdentityResource) => (
        <span className="text-xs text-gray-500">{resource.userClaims?.length || 0} claims</span>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (resource: IdentityResource) => (
        <div className="flex items-center justify-end gap-2">
          <button
            onClick={() => openEditModal(resource)}
            className="p-1 text-gray-400 hover:text-blue-600"
            title="Edit"
          >
            <PencilIcon className="h-4 w-4" />
          </button>
          <button
            onClick={() => openDeleteModal(resource)}
            className="p-1 text-gray-400 hover:text-red-600"
            title="Delete"
          >
            <TrashIcon className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Identity Resources</h1>
          <p className="mt-1 text-sm text-gray-500">Manage identity resources (user claims)</p>
        </div>
        <Button onClick={openCreateModal}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Identity Resource
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(resource) => resource.id}
          loading={isLoading}
          emptyMessage="No identity resources configured yet"
        />
      </Card>

      {/* Create/Edit Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={closeModal}
        title={selectedResource ? 'Edit Identity Resource' : 'Create Identity Resource'}
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="form-label">Name</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              required
              disabled={!!selectedResource}
              placeholder="e.g., profile, email, address"
            />
            {!selectedResource && (
              <p className="form-helper">Unique identifier (cannot be changed later)</p>
            )}
          </div>

          <div>
            <label className="form-label">Display Name</label>
            <input
              type="text"
              value={formData.displayName}
              onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
              placeholder="Human-readable name"
            />
          </div>

          <div>
            <label className="form-label">Description</label>
            <textarea
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              rows={2}
            />
          </div>

          <div className="flex flex-wrap gap-4">
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={formData.enabled}
                onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
              />
              <span className="text-sm">Enabled</span>
            </label>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={formData.required}
                onChange={(e) => setFormData({ ...formData, required: e.target.checked })}
              />
              <span className="text-sm">Required</span>
            </label>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={formData.emphasize}
                onChange={(e) => setFormData({ ...formData, emphasize: e.target.checked })}
              />
              <span className="text-sm">Emphasize</span>
            </label>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={formData.showInDiscoveryDocument}
                onChange={(e) => setFormData({ ...formData, showInDiscoveryDocument: e.target.checked })}
              />
              <span className="text-sm">Show in Discovery</span>
            </label>
          </div>

          <div>
            <label className="form-label">User Claims</label>
            <div className="flex flex-wrap gap-2 mb-2">
              {formData.userClaims.map((claim) => (
                <span
                  key={claim}
                  className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                  onClick={() => removeClaim(claim)}
                  title="Click to remove"
                >
                  {claim} ×
                </span>
              ))}
            </div>
            <div className="flex gap-2">
              <input
                type="text"
                value={newClaim}
                onChange={(e) => setNewClaim(e.target.value)}
                placeholder="e.g., name, email, picture"
                onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addClaim())}
              />
              <Button type="button" variant="secondary" onClick={addClaim}>
                Add
              </Button>
            </div>
          </div>

          <div className="flex justify-end gap-3 pt-4">
            <Button type="button" variant="secondary" onClick={closeModal}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={createMutation.isPending || updateMutation.isPending}
            >
              {createMutation.isPending || updateMutation.isPending
                ? 'Saving...'
                : selectedResource
                  ? 'Update'
                  : 'Create'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => setIsDeleteModalOpen(false)}
        title="Delete Identity Resource"
      >
        <p className="text-sm text-gray-500">
          Are you sure you want to delete "{selectedResource?.displayName || selectedResource?.name}"?
          This action cannot be undone.
        </p>
        <div className="mt-4 flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setIsDeleteModalOpen(false)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => selectedResource && deleteMutation.mutate(selectedResource.id)}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
          </Button>
        </div>
      </Modal>
    </div>
  );
}

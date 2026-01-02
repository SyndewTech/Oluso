import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { tenantService } from '../services/tenantService';
import type { Tenant, CreateTenantRequest } from '../types/tenant';
import {
  PlusIcon,
  PencilIcon,
  TrashIcon,
  Cog6ToothIcon,
} from '@heroicons/react/24/outline';

interface TenantFormData {
  name: string;
  displayName: string;
  identifier: string;
  description: string;
  customDomain: string;
}

export default function TenantsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [selectedTenant, setSelectedTenant] = useState<Tenant | null>(null);
  const [formData, setFormData] = useState<TenantFormData>({
    name: '',
    displayName: '',
    identifier: '',
    description: '',
    customDomain: '',
  });

  const { data: tenants, isLoading } = useQuery({
    queryKey: ['tenants'],
    queryFn: () => tenantService.getAll(),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateTenantRequest) => tenantService.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tenants'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => tenantService.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tenants'] });
      setIsDeleteModalOpen(false);
      setSelectedTenant(null);
    },
  });

  const openCreateModal = () => {
    setSelectedTenant(null);
    setFormData({
      name: '',
      displayName: '',
      identifier: '',
      description: '',
      customDomain: '',
    });
    setIsModalOpen(true);
  };

  const openDeleteModal = (tenant: Tenant) => {
    setSelectedTenant(tenant);
    setIsDeleteModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedTenant(null);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      name: formData.name,
      displayName: formData.displayName || undefined,
      identifier: formData.identifier,
      description: formData.description || undefined,
      customDomain: formData.customDomain || undefined,
    });
  };

  const columns = [
    {
      key: 'name',
      header: 'Name',
      render: (tenant: Tenant) => (
        <div>
          <div className="font-medium text-gray-900">{tenant.displayName || tenant.name}</div>
          {tenant.displayName && (
            <div className="text-xs text-gray-500">{tenant.name}</div>
          )}
        </div>
      ),
    },
    {
      key: 'identifier',
      header: 'Identifier',
      render: (tenant: Tenant) => (
        <code className="text-sm bg-gray-100 px-2 py-0.5 rounded">{tenant.identifier}</code>
      ),
    },
    {
      key: 'customDomain',
      header: 'Custom Domain',
      render: (tenant: Tenant) => tenant.customDomain || '-',
    },
    {
      key: 'enabled',
      header: 'Status',
      render: (tenant: Tenant) => (
        <span
          className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
            tenant.enabled
              ? 'bg-green-100 text-green-800'
              : 'bg-red-100 text-red-800'
          }`}
        >
          {tenant.enabled ? 'Enabled' : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (tenant: Tenant) => new Date(tenant.createdAt).toLocaleDateString(),
    },
    {
      key: 'actions',
      header: '',
      render: (tenant: Tenant) => (
        <div className="flex items-center justify-end gap-2">
          <button
            onClick={() => navigate(`/tenants/${tenant.id}/settings`)}
            className="p-1 text-gray-400 hover:text-blue-600"
            title="Settings"
          >
            <Cog6ToothIcon className="h-4 w-4" />
          </button>
          <button
            onClick={() => navigate(`/tenants/${tenant.id}`)}
            className="p-1 text-gray-400 hover:text-blue-600"
            title="Edit tenant"
          >
            <PencilIcon className="h-4 w-4" />
          </button>
          <button
            onClick={() => openDeleteModal(tenant)}
            className="p-1 text-gray-400 hover:text-red-600"
            title="Delete tenant"
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
          <h1 className="text-2xl font-bold text-gray-900">Tenants</h1>
          <p className="mt-1 text-sm text-gray-500">Manage multi-tenant organizations</p>
        </div>
        <Button onClick={openCreateModal}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Tenant
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={tenants || []}
          keyExtractor={(tenant) => tenant.id}
          loading={isLoading}
          emptyMessage="No tenants configured yet"
        />
      </Card>

      {/* Create Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={closeModal}
        title="Create Tenant"
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Name</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              required
            />
            <p className="mt-1 text-xs text-gray-500">Internal name for the tenant</p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Display Name</label>
            <input
              type="text"
              value={formData.displayName}
              onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              placeholder="Human-readable name"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Identifier</label>
            <input
              type="text"
              value={formData.identifier}
              onChange={(e) => setFormData({ ...formData, identifier: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, '') })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              placeholder="my-tenant"
              required
            />
            <p className="mt-1 text-xs text-gray-500">
              URL-safe identifier used for tenant resolution (lowercase, alphanumeric and hyphens only)
            </p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Description</label>
            <textarea
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              rows={2}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Custom Domain</label>
            <input
              type="text"
              value={formData.customDomain}
              onChange={(e) => setFormData({ ...formData, customDomain: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              placeholder="auth.example.com"
            />
            <p className="mt-1 text-xs text-gray-500">Optional custom domain for this tenant</p>
          </div>

          <div className="flex justify-end gap-3 pt-4">
            <Button type="button" variant="secondary" onClick={closeModal}>
              Cancel
            </Button>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Creating...' : 'Create Tenant'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => setIsDeleteModalOpen(false)}
        title="Delete Tenant"
      >
        <p className="text-sm text-gray-500">
          Are you sure you want to delete the tenant "{selectedTenant?.displayName || selectedTenant?.name}"?
          This will permanently delete all associated data including users, roles, and configurations.
          This action cannot be undone.
        </p>
        <div className="mt-4 flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setIsDeleteModalOpen(false)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => selectedTenant && deleteMutation.mutate(selectedTenant.id)}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
          </Button>
        </div>
      </Modal>
    </div>
  );
}

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { roleService } from '../services/userService';
import type { Role, CreateRoleRequest, UpdateRoleRequest, RoleClaim } from '../types/user';
import {
  PlusIcon,
  PencilIcon,
  TrashIcon,
  UsersIcon,
  ShieldCheckIcon,
} from '@heroicons/react/24/outline';

interface RoleFormData {
  name: string;
  displayName: string;
  description: string;
  permissions: string[];
  claims: RoleClaim[];
}

const AVAILABLE_PERMISSIONS = [
  'users.read',
  'users.write',
  'users.delete',
  'roles.read',
  'roles.write',
  'roles.delete',
  'clients.read',
  'clients.write',
  'clients.delete',
  'scopes.read',
  'scopes.write',
  'scopes.delete',
  'settings.read',
  'settings.write',
  'audit.read',
  'journeys.read',
  'journeys.write',
  'journeys.delete',
];

export default function RolesPage() {
  const queryClient = useQueryClient();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isUsersModalOpen, setIsUsersModalOpen] = useState(false);
  const [selectedRole, setSelectedRole] = useState<Role | null>(null);
  const [formData, setFormData] = useState<RoleFormData>({
    name: '',
    displayName: '',
    description: '',
    permissions: [],
    claims: [],
  });
  const [newClaimType, setNewClaimType] = useState('');
  const [newClaimValue, setNewClaimValue] = useState('');

  const { data: roles, isLoading } = useQuery({
    queryKey: ['roles'],
    queryFn: () => roleService.getAll(),
  });

  const { data: usersInRole, isLoading: isLoadingUsers } = useQuery({
    queryKey: ['role-users', selectedRole?.id],
    queryFn: () => roleService.getUsersInRole(selectedRole!.id),
    enabled: isUsersModalOpen && !!selectedRole?.id,
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateRoleRequest) => roleService.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['roles'] });
      closeModal();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateRoleRequest }) =>
      roleService.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['roles'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => roleService.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['roles'] });
      setIsDeleteModalOpen(false);
      setSelectedRole(null);
    },
  });

  const openCreateModal = () => {
    setSelectedRole(null);
    setFormData({
      name: '',
      displayName: '',
      description: '',
      permissions: [],
      claims: [],
    });
    setIsModalOpen(true);
  };

  const openEditModal = (role: Role) => {
    setSelectedRole(role);
    setFormData({
      name: role.name,
      displayName: role.displayName || '',
      description: role.description || '',
      permissions: role.permissions || [],
      claims: role.claims || [],
    });
    setIsModalOpen(true);
  };

  const openDeleteModal = (role: Role) => {
    setSelectedRole(role);
    setIsDeleteModalOpen(true);
  };

  const openUsersModal = (role: Role) => {
    setSelectedRole(role);
    setIsUsersModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedRole(null);
    setNewClaimType('');
    setNewClaimValue('');
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedRole) {
      updateMutation.mutate({
        id: selectedRole.id,
        data: {
          name: formData.name,
          displayName: formData.displayName || undefined,
          description: formData.description || undefined,
          permissions: formData.permissions,
          claims: formData.claims,
        },
      });
    } else {
      createMutation.mutate({
        name: formData.name,
        displayName: formData.displayName || undefined,
        description: formData.description || undefined,
        permissions: formData.permissions,
        claims: formData.claims,
      });
    }
  };

  const togglePermission = (permission: string) => {
    setFormData((prev) => ({
      ...prev,
      permissions: prev.permissions.includes(permission)
        ? prev.permissions.filter((p) => p !== permission)
        : [...prev.permissions, permission],
    }));
  };

  const addClaim = () => {
    if (newClaimType && newClaimValue) {
      setFormData((prev) => ({
        ...prev,
        claims: [...prev.claims, { type: newClaimType, value: newClaimValue }],
      }));
      setNewClaimType('');
      setNewClaimValue('');
    }
  };

  const removeClaim = (index: number) => {
    setFormData((prev) => ({
      ...prev,
      claims: prev.claims.filter((_, i) => i !== index),
    }));
  };

  const columns = [
    {
      key: 'name',
      header: 'Name',
      render: (role: Role) => (
        <div>
          <div className="font-medium text-gray-900">{role.displayName || role.name}</div>
          {role.displayName && (
            <div className="text-xs text-gray-500">{role.name}</div>
          )}
        </div>
      ),
    },
    { key: 'description', header: 'Description' },
    {
      key: 'isSystemRole',
      header: 'Type',
      render: (role: Role) => (
        <span
          className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
            role.isSystemRole
              ? 'bg-purple-100 text-purple-800'
              : role.isGlobal
                ? 'bg-blue-100 text-blue-800'
                : 'bg-gray-100 text-gray-800'
          }`}
        >
          {role.isSystemRole ? 'System' : role.isGlobal ? 'Global' : 'Tenant'}
        </span>
      ),
    },
    {
      key: 'permissions',
      header: 'Permissions',
      render: (role: Role) => (
        <span className="text-xs text-gray-500">
          {role.permissions?.length || 0} permissions
        </span>
      ),
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (role: Role) => new Date(role.createdAt).toLocaleDateString(),
    },
    {
      key: 'actions',
      header: '',
      render: (role: Role) => (
        <div className="flex items-center justify-end gap-2">
          <button
            onClick={() => openUsersModal(role)}
            className="p-1 text-gray-400 hover:text-gray-600"
            title="View users"
          >
            <UsersIcon className="h-4 w-4" />
          </button>
          {!role.isSystemRole && (
            <>
              <button
                onClick={() => openEditModal(role)}
                className="p-1 text-gray-400 hover:text-blue-600"
                title="Edit role"
              >
                <PencilIcon className="h-4 w-4" />
              </button>
              <button
                onClick={() => openDeleteModal(role)}
                className="p-1 text-gray-400 hover:text-red-600"
                title="Delete role"
              >
                <TrashIcon className="h-4 w-4" />
              </button>
            </>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Roles</h1>
          <p className="mt-1 text-sm text-gray-500">Manage user roles and permissions</p>
        </div>
        <Button onClick={openCreateModal}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Role
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={roles || []}
          keyExtractor={(role) => role.id}
          loading={isLoading}
          emptyMessage="No roles configured yet"
        />
      </Card>

      {/* Create/Edit Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={closeModal}
        title={selectedRole ? 'Edit Role' : 'Create Role'}
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Name</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              required
              disabled={!!selectedRole}
            />
            {!selectedRole && (
              <p className="mt-1 text-xs text-gray-500">
                Unique identifier for the role (cannot be changed later)
              </p>
            )}
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Display Name</label>
            <input
              type="text"
              value={formData.displayName}
              onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              placeholder="Human-readable name"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Description</label>
            <textarea
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              rows={2}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              <ShieldCheckIcon className="h-4 w-4 inline mr-1" />
              Permissions
            </label>
            <div className="grid grid-cols-2 gap-2 max-h-48 overflow-y-auto border rounded-md p-2">
              {AVAILABLE_PERMISSIONS.map((permission) => (
                <label key={permission} className="flex items-center space-x-2 text-sm">
                  <input
                    type="checkbox"
                    checked={formData.permissions.includes(permission)}
                    onChange={() => togglePermission(permission)}
                    className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                  />
                  <span>{permission}</span>
                </label>
              ))}
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Claims</label>
            <div className="space-y-2">
              {formData.claims.map((claim, index) => (
                <div key={index} className="flex items-center gap-2 text-sm">
                  <span className="font-medium">{claim.type}:</span>
                  <span className="text-gray-600">{claim.value}</span>
                  <button
                    type="button"
                    onClick={() => removeClaim(index)}
                    className="text-red-500 hover:text-red-700"
                  >
                    <TrashIcon className="h-3 w-3" />
                  </button>
                </div>
              ))}
              <div className="flex gap-2">
                <input
                  type="text"
                  placeholder="Type"
                  value={newClaimType}
                  onChange={(e) => setNewClaimType(e.target.value)}
                  className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
                <input
                  type="text"
                  placeholder="Value"
                  value={newClaimValue}
                  onChange={(e) => setNewClaimValue(e.target.value)}
                  className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
                <Button type="button" variant="secondary" onClick={addClaim}>
                  Add
                </Button>
              </div>
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
                : selectedRole
                  ? 'Update Role'
                  : 'Create Role'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => setIsDeleteModalOpen(false)}
        title="Delete Role"
      >
        <p className="text-sm text-gray-500">
          Are you sure you want to delete the role "{selectedRole?.displayName || selectedRole?.name}"?
          This action cannot be undone.
        </p>
        <div className="mt-4 flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setIsDeleteModalOpen(false)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => selectedRole && deleteMutation.mutate(selectedRole.id)}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
          </Button>
        </div>
      </Modal>

      {/* Users in Role Modal */}
      <Modal
        isOpen={isUsersModalOpen}
        onClose={() => setIsUsersModalOpen(false)}
        title={`Users in "${selectedRole?.displayName || selectedRole?.name}"`}
      >
        {isLoadingUsers ? (
          <p className="text-sm text-gray-500">Loading users...</p>
        ) : usersInRole && usersInRole.length > 0 ? (
          <ul className="divide-y divide-gray-200">
            {usersInRole.map((user) => (
              <li key={user.id} className="py-3 flex items-center justify-between">
                <div>
                  <p className="font-medium text-gray-900">
                    {user.displayName || user.userName}
                  </p>
                  <p className="text-sm text-gray-500">{user.email}</p>
                </div>
                <span
                  className={`inline-flex rounded-full px-2 text-xs font-semibold ${
                    user.isActive ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
                  }`}
                >
                  {user.isActive ? 'Active' : 'Inactive'}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-gray-500">No users have this role.</p>
        )}
        <div className="mt-4 flex justify-end">
          <Button variant="secondary" onClick={() => setIsUsersModalOpen(false)}>
            Close
          </Button>
        </div>
      </Modal>
    </div>
  );
}

import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardContent } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { userService, roleService } from '../services/userService';
import type { User, CreateUserRequest, UpdateUserRequest } from '../types/user';
import {
  PlusIcon,
  PencilIcon,
  TrashIcon,
  MagnifyingGlassIcon,
  KeyIcon,
  LockOpenIcon,
  ShieldCheckIcon,
} from '@heroicons/react/24/outline';

interface UserFormData {
  email: string;
  userName: string;
  password: string;
  firstName: string;
  lastName: string;
  displayName: string;
  phoneNumber: string;
  isActive: boolean;
  emailConfirmed: boolean;
  roles: string[];
}

export default function UsersPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [roleFilter, setRoleFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('');

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isRolesModalOpen, setIsRolesModalOpen] = useState(false);
  const [isPasswordModalOpen, setIsPasswordModalOpen] = useState(false);
  const [selectedUser, setSelectedUser] = useState<User | null>(null);
  const [newPassword, setNewPassword] = useState('');
  const [formData, setFormData] = useState<UserFormData>({
    email: '',
    userName: '',
    password: '',
    firstName: '',
    lastName: '',
    displayName: '',
    phoneNumber: '',
    isActive: true,
    emailConfirmed: false,
    roles: [],
  });

  const { data, isLoading } = useQuery({
    queryKey: ['users', page, search, roleFilter, statusFilter],
    queryFn: () =>
      userService.getAll({
        page,
        pageSize: 10,
      }
        )
      // userService.getAll({
      //   page,
      //   pageSize: 10,
      //   //search: search || undefined,
      //   //role: roleFilter || undefined,
      //   //isActive: statusFilter === '' ? undefined : statusFilter === 'active',
      // }),
  });

  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: () => roleService.getAll(),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateUserRequest) => userService.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      closeModal();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateUserRequest }) =>
      userService.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => userService.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      setIsDeleteModalOpen(false);
      setSelectedUser(null);
    },
  });

  const setRolesMutation = useMutation({
    mutationFn: ({ userId, roles }: { userId: string; roles: string[] }) =>
      userService.setRoles(userId, roles),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      setIsRolesModalOpen(false);
    },
  });

  const resetPasswordMutation = useMutation({
    mutationFn: ({ userId, password }: { userId: string; password: string }) =>
      userService.resetPassword(userId, password),
    onSuccess: () => {
      setIsPasswordModalOpen(false);
      setNewPassword('');
    },
  });

  const unlockMutation = useMutation({
    mutationFn: (userId: string) => userService.unlock(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
    },
  });

  const openCreateModal = () => {
    setSelectedUser(null);
    setFormData({
      email: '',
      userName: '',
      password: '',
      firstName: '',
      lastName: '',
      displayName: '',
      phoneNumber: '',
      isActive: true,
      emailConfirmed: false,
      roles: [],
    });
    setIsModalOpen(true);
  };

  const openEditModal = (user: User) => {
    setSelectedUser(user);
    setFormData({
      email: user.email,
      userName: user.userName,
      password: '',
      firstName: user.firstName || '',
      lastName: user.lastName || '',
      displayName: user.displayName || '',
      phoneNumber: user.phoneNumber || '',
      isActive: user.isActive,
      emailConfirmed: user.emailConfirmed,
      roles: user.roles || [],
    });
    setIsModalOpen(true);
  };

  const openDeleteModal = (user: User) => {
    setSelectedUser(user);
    setIsDeleteModalOpen(true);
  };

  const openRolesModal = (user: User) => {
    setSelectedUser(user);
    setFormData((prev) => ({ ...prev, roles: user.roles || [] }));
    setIsRolesModalOpen(true);
  };

  const openPasswordModal = (user: User) => {
    setSelectedUser(user);
    setNewPassword('');
    setIsPasswordModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedUser(null);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedUser) {
      updateMutation.mutate({
        id: selectedUser.id,
        data: {
          firstName: formData.firstName || undefined,
          lastName: formData.lastName || undefined,
          displayName: formData.displayName || undefined,
          phoneNumber: formData.phoneNumber || undefined,
          isActive: formData.isActive,
          emailConfirmed: formData.emailConfirmed,
        },
      });
    } else {
      createMutation.mutate({
        email: formData.email,
        userName: formData.userName || undefined,
        password: formData.password || undefined,
        firstName: formData.firstName || undefined,
        lastName: formData.lastName || undefined,
        displayName: formData.displayName || undefined,
        phoneNumber: formData.phoneNumber || undefined,
        isActive: formData.isActive,
        emailConfirmed: formData.emailConfirmed,
        roles: formData.roles.length > 0 ? formData.roles : undefined,
      });
    }
  };

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setSearch(searchInput);
    setPage(1);
  };

  const toggleRole = (roleName: string) => {
    setFormData((prev) => ({
      ...prev,
      roles: prev.roles.includes(roleName)
        ? prev.roles.filter((r) => r !== roleName)
        : [...prev.roles, roleName],
    }));
  };

  const handleSaveRoles = () => {
    if (selectedUser) {
      setRolesMutation.mutate({ userId: selectedUser.id, roles: formData.roles });
    }
  };

  const columns = [
    {
      key: 'userName',
      header: 'Username',
      render: (user: User) => (
        <div>
          <button
            onClick={() => navigate(`/users/${user.id}`)}
            className="font-medium text-blue-600 hover:text-blue-800 hover:underline text-left"
          >
            {user.userName}
          </button>
          <div className="text-xs text-gray-500">{user.email}</div>
        </div>
      ),
    },
    {
      key: 'displayName',
      header: 'Name',
      render: (user: User) =>
        user.displayName || `${user.firstName || ''} ${user.lastName || ''}`.trim() || '-',
    },
    {
      key: 'isActive',
      header: 'Status',
      render: (user: User) => (
        <div className="flex flex-col gap-1">
          <span
            className={`inline-flex w-fit rounded-full px-2 text-xs font-semibold leading-5 ${
              user.isActive ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
            }`}
          >
            {user.isActive ? 'Active' : 'Inactive'}
          </span>
          {user.lockoutEnd && new Date(user.lockoutEnd) > new Date() && (
            <span className="inline-flex w-fit rounded-full bg-yellow-100 px-2 text-xs font-semibold leading-5 text-yellow-800">
              Locked
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'roles',
      header: 'Roles',
      render: (user: User) => (
        <div className="flex flex-wrap gap-1">
          {user.roles?.length > 0 ? (
            user.roles.map((role) => (
              <span
                key={role}
                className="inline-flex items-center rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-800"
              >
                {role}
              </span>
            ))
          ) : (
            <span className="text-gray-400">-</span>
          )}
        </div>
      ),
    },
    {
      key: 'lastLoginAt',
      header: 'Last Login',
      render: (user: User) =>
        user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleDateString() : 'Never',
    },
    {
      key: 'actions',
      header: '',
      render: (user: User) => (
        <div className="flex items-center justify-end gap-2">
          <button
            onClick={() => openRolesModal(user)}
            className="p-1 text-gray-400 hover:text-indigo-600"
            title="Manage roles"
          >
            <ShieldCheckIcon className="h-4 w-4" />
          </button>
          <button
            onClick={() => openPasswordModal(user)}
            className="p-1 text-gray-400 hover:text-gray-600"
            title="Reset password"
          >
            <KeyIcon className="h-4 w-4" />
          </button>
          {user.lockoutEnd && new Date(user.lockoutEnd) > new Date() && (
            <button
              onClick={() => unlockMutation.mutate(user.id)}
              className="p-1 text-gray-400 hover:text-green-600"
              title="Unlock account"
              disabled={unlockMutation.isPending}
            >
              <LockOpenIcon className="h-4 w-4" />
            </button>
          )}
          <button
            onClick={() => openEditModal(user)}
            className="p-1 text-gray-400 hover:text-blue-600"
            title="Edit user"
          >
            <PencilIcon className="h-4 w-4" />
          </button>
          <button
            onClick={() => openDeleteModal(user)}
            className="p-1 text-gray-400 hover:text-red-600"
            title="Delete user"
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
          <h1 className="text-2xl font-bold text-gray-900">Users</h1>
          <p className="mt-1 text-sm text-gray-500">Manage user accounts and roles</p>
        </div>
        <Button onClick={openCreateModal}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add User
        </Button>
      </div>

      {/* Search and Filter */}
      <Card>
        <CardContent className="flex flex-wrap gap-4">
          <form onSubmit={handleSearch} className="flex-1 min-w-[200px]">
            <div className="relative">
              <input
                type="text"
                placeholder="Search by name or email..."
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                className="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm pl-10"
              />
              <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
            </div>
          </form>
          <select
            value={roleFilter}
            onChange={(e) => {
              setRoleFilter(e.target.value);
              setPage(1);
            }}
            className="rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
          >
            <option value="">All Roles</option>
            {roles?.map((role) => (
              <option key={role.id} value={role.name}>
                {role.displayName || role.name}
              </option>
            ))}
          </select>
          <select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value);
              setPage(1);
            }}
            className="rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
          >
            <option value="">All Status</option>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
          </select>
        </CardContent>
      </Card>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(user) => user.id}
          loading={isLoading}
          emptyMessage="No users found"
        />
        {data && data.totalPages > 1 && (
          <div className="flex items-center justify-between border-t border-gray-200 px-4 py-3 sm:px-6">
            <div className="text-sm text-gray-700">
              Page {data.page} of {data.totalPages} ({data.totalCount} total users)
            </div>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page <= 1}
              >
                Previous
              </Button>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setPage((p) => p + 1)}
                disabled={page >= data.totalPages}
              >
                Next
              </Button>
            </div>
          </div>
        )}
      </Card>

      {/* Create/Edit Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={closeModal}
        title={selectedUser ? 'Edit User' : 'Create User'}
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="col-span-2">
              <label className="block text-sm font-medium text-gray-700">Email</label>
              <input
                type="email"
                value={formData.email}
                onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                required
                disabled={!!selectedUser}
              />
            </div>

            {!selectedUser && (
              <>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Username</label>
                  <input
                    type="text"
                    value={formData.userName}
                    onChange={(e) => setFormData({ ...formData, userName: e.target.value })}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                    placeholder="Leave empty to use email"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Password</label>
                  <input
                    type="password"
                    value={formData.password}
                    onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                    placeholder="Leave empty to auto-generate"
                  />
                </div>
              </>
            )}

            <div>
              <label className="block text-sm font-medium text-gray-700">First Name</label>
              <input
                type="text"
                value={formData.firstName}
                onChange={(e) => setFormData({ ...formData, firstName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Last Name</label>
              <input
                type="text"
                value={formData.lastName}
                onChange={(e) => setFormData({ ...formData, lastName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Display Name</label>
              <input
                type="text"
                value={formData.displayName}
                onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Phone Number</label>
              <input
                type="tel"
                value={formData.phoneNumber}
                onChange={(e) => setFormData({ ...formData, phoneNumber: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              />
            </div>
          </div>

          <div className="flex gap-6">
            <label className="flex items-center space-x-2">
              <input
                type="checkbox"
                checked={formData.isActive}
                onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
              />
              <span className="text-sm text-gray-700">Active</span>
            </label>
            <label className="flex items-center space-x-2">
              <input
                type="checkbox"
                checked={formData.emailConfirmed}
                onChange={(e) => setFormData({ ...formData, emailConfirmed: e.target.checked })}
                className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
              />
              <span className="text-sm text-gray-700">Email Confirmed</span>
            </label>
          </div>

          {!selectedUser && roles && roles.length > 0 && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Roles</label>
              <div className="grid grid-cols-2 gap-2 max-h-32 overflow-y-auto border rounded-md p-2">
                {roles.map((role) => (
                  <label key={role.id} className="flex items-center space-x-2 text-sm">
                    <input
                      type="checkbox"
                      checked={formData.roles.includes(role.name)}
                      onChange={() => toggleRole(role.name)}
                      className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                    />
                    <span>{role.displayName || role.name}</span>
                  </label>
                ))}
              </div>
            </div>
          )}

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
                : selectedUser
                  ? 'Update User'
                  : 'Create User'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => setIsDeleteModalOpen(false)}
        title="Delete User"
      >
        <p className="text-sm text-gray-500">
          Are you sure you want to delete the user "{selectedUser?.displayName || selectedUser?.userName}"?
          This action cannot be undone.
        </p>
        <div className="mt-4 flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setIsDeleteModalOpen(false)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => selectedUser && deleteMutation.mutate(selectedUser.id)}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
          </Button>
        </div>
      </Modal>

      {/* Manage Roles Modal */}
      <Modal
        isOpen={isRolesModalOpen}
        onClose={() => setIsRolesModalOpen(false)}
        title={`Manage Roles - ${selectedUser?.displayName || selectedUser?.userName}`}
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-500">
            Select the roles to assign to this user.
          </p>
          <div className="grid grid-cols-2 gap-2 max-h-64 overflow-y-auto border rounded-md p-3">
            {roles?.map((role) => (
              <label key={role.id} className="flex items-center space-x-2 text-sm">
                <input
                  type="checkbox"
                  checked={formData.roles.includes(role.name)}
                  onChange={() => toggleRole(role.name)}
                  className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                />
                <div>
                  <span className="font-medium">{role.displayName || role.name}</span>
                  {role.description && (
                    <p className="text-xs text-gray-500">{role.description}</p>
                  )}
                </div>
              </label>
            ))}
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="secondary" onClick={() => setIsRolesModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSaveRoles} disabled={setRolesMutation.isPending}>
              {setRolesMutation.isPending ? 'Saving...' : 'Save Roles'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Reset Password Modal */}
      <Modal
        isOpen={isPasswordModalOpen}
        onClose={() => setIsPasswordModalOpen(false)}
        title={`Reset Password - ${selectedUser?.displayName || selectedUser?.userName}`}
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">New Password</label>
            <input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              placeholder="Enter new password"
              required
            />
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="secondary" onClick={() => setIsPasswordModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={() =>
                selectedUser &&
                resetPasswordMutation.mutate({ userId: selectedUser.id, password: newPassword })
              }
              disabled={resetPasswordMutation.isPending || !newPassword}
            >
              {resetPasswordMutation.isPending ? 'Resetting...' : 'Reset Password'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

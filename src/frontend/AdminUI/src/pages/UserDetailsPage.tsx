import { useState, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { userService, roleService } from '../services/userService';
import { useUserDetailTabs, type UserDetailData } from '../contexts/PluginContext';
import { useAuthStore } from '../store/slices/authSlice';
import { useTenantFeatures } from '../contexts/TenantFeaturesContext';
import type { User, UpdateUserRequest } from '../types/user';
import {
  ArrowLeftIcon,
  UserCircleIcon,
  ShieldCheckIcon,
  KeyIcon,
  ClockIcon,
  FingerPrintIcon,
  DocumentTextIcon,
  LinkIcon,
  PencilIcon,
  TrashIcon,
  LockOpenIcon,
  CheckCircleIcon,
  XCircleIcon,
} from '@heroicons/react/24/outline';

// Built-in tabs with large order increments (100, 200, 300...)
// Plugin tabs can insert between these using intermediate values

interface TabDefinition {
  id: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  order: number;
}

const BUILT_IN_TABS: TabDefinition[] = [
  { id: 'profile', label: 'Profile', icon: UserCircleIcon, order: 100 },
  { id: 'security', label: 'Security', icon: ShieldCheckIcon, order: 200 },
  { id: 'roles', label: 'Roles & Permissions', icon: KeyIcon, order: 300 },
  { id: 'sessions', label: 'Sessions', icon: ClockIcon, order: 400 },
  { id: 'mfa', label: 'MFA', icon: FingerPrintIcon, order: 500 },
  { id: 'claims', label: 'Claims', icon: DocumentTextIcon, order: 600 },
  { id: 'external-logins', label: 'External Logins', icon: LinkIcon, order: 700 },
];

export default function UserDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState('profile');
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isPasswordModalOpen, setIsPasswordModalOpen] = useState(false);
  const [isRolesModalOpen, setIsRolesModalOpen] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);

  const currentUser = useAuthStore((state) => state.user);
  const isSuperAdmin = currentUser?.roles?.includes('SuperAdmin') || false;
  const { hasFeature } = useTenantFeatures();

  // Get plugin tabs
  const pluginTabs = useUserDetailTabs({ isSuperAdmin, hasFeature });

  // Merge built-in tabs with plugin tabs and sort
  const allTabs = useMemo(() => {
    const merged = [
      ...BUILT_IN_TABS.map(tab => ({ ...tab, isBuiltIn: true as const })),
      ...pluginTabs.map(tab => ({
        id: tab.id,
        label: tab.label,
        icon: tab.icon || DocumentTextIcon,
        order: tab.order ?? 1000,
        isBuiltIn: false as const,
        component: tab.component,
        badge: tab.badge,
      })),
    ];
    return merged.sort((a, b) => a.order - b.order);
  }, [pluginTabs]);

  const { data: user, isLoading, refetch } = useQuery<User>({
    queryKey: ['user', id],
    queryFn: () => userService.getById(id!),
    enabled: !!id,
  });

  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: () => roleService.getAll(),
  });

  const [editFormData, setEditFormData] = useState<Partial<UpdateUserRequest>>({});

  // Set edit form data when user loads
  useMemo(() => {
    if (user) {
      setEditFormData({
        firstName: user.firstName || '',
        lastName: user.lastName || '',
        displayName: user.displayName || '',
        phoneNumber: user.phoneNumber || '',
        isActive: user.isActive,
        emailConfirmed: user.emailConfirmed,
        lockoutEnabled: user.lockoutEnabled,
      });
      setSelectedRoles(user.roles || []);
    }
  }, [user]);

  const updateMutation = useMutation({
    mutationFn: (data: UpdateUserRequest) => userService.update(id!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', id] });
      setIsEditModalOpen(false);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => userService.delete(id!),
    onSuccess: () => {
      navigate('/users');
    },
  });

  const resetPasswordMutation = useMutation({
    mutationFn: (password: string) => userService.resetPassword(id!, password),
    onSuccess: () => {
      setIsPasswordModalOpen(false);
      setNewPassword('');
    },
  });

  const setRolesMutation = useMutation({
    mutationFn: (roles: string[]) => userService.setRoles(id!, roles),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', id] });
      setIsRolesModalOpen(false);
    },
  });

  const unlockMutation = useMutation({
    mutationFn: () => userService.unlock(id!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', id] });
    },
  });

  const handleEditSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    updateMutation.mutate(editFormData);
  };

  const toggleRole = (roleName: string) => {
    setSelectedRoles(prev =>
      prev.includes(roleName)
        ? prev.filter(r => r !== roleName)
        : [...prev, roleName]
    );
  };

  // Convert User to UserDetailData for plugin tabs
  const userDetailData: UserDetailData | undefined = user
    ? {
        ...user,
        claims: user.claims,
        externalLogins: [], // TODO: Load from API
      }
    : undefined;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-gray-500">Loading user...</div>
      </div>
    );
  }

  if (!user) {
    return (
      <div className="text-center py-12">
        <h2 className="text-lg font-medium text-gray-900">User not found</h2>
        <Button variant="secondary" onClick={() => navigate('/users')} className="mt-4">
          Back to Users
        </Button>
      </div>
    );
  }

  const isLocked = user.lockoutEnd && new Date(user.lockoutEnd) > new Date();

  // Compute badge for a tab
  const getTabBadge = (tab: typeof allTabs[0]) => {
    if (!('badge' in tab) || !tab.badge || !userDetailData) return undefined;
    if (typeof tab.badge === 'function') {
      return tab.badge(userDetailData);
    }
    return tab.badge;
  };

  // Render the active tab content
  const renderTabContent = () => {
    const activeTabDef = allTabs.find(t => t.id === activeTab);

    // Plugin tab - render its component
    if (activeTabDef && !activeTabDef.isBuiltIn && 'component' in activeTabDef && userDetailData) {
      const TabComponent = activeTabDef.component;
      return (
        <TabComponent
          data={userDetailData}
          onRefresh={() => refetch()}
          isActive={true}
        />
      );
    }

    // Built-in tabs
    switch (activeTab) {
      case 'profile':
        return <ProfileTab user={user} />;
      case 'security':
        return (
          <SecurityTab
            user={user}
            isLocked={!!isLocked}
            onResetPassword={() => setIsPasswordModalOpen(true)}
            onUnlock={() => unlockMutation.mutate()}
            unlocking={unlockMutation.isPending}
          />
        );
      case 'roles':
        return (
          <RolesTab
            user={user}
            onManageRoles={() => {
              setSelectedRoles(user.roles || []);
              setIsRolesModalOpen(true);
            }}
          />
        );
      case 'sessions':
        return <SessionsTab userId={user.id} />;
      case 'mfa':
        return <MfaTab user={user} />;
      case 'claims':
        return <ClaimsTab user={user} onRefresh={() => refetch()} />;
      case 'external-logins':
        return <ExternalLoginsTab user={user} />;
      default:
        return <div className="text-gray-500">Select a tab</div>;
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/users')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div className="flex items-center gap-4">
            <div className="h-16 w-16 rounded-full bg-primary-600 flex items-center justify-center">
              {user.profilePictureUrl ? (
                <img
                  src={user.profilePictureUrl}
                  alt=""
                  className="h-16 w-16 rounded-full object-cover"
                />
              ) : (
                <span className="text-2xl font-medium text-white">
                  {user.displayName?.[0] || user.userName?.[0] || 'U'}
                </span>
              )}
            </div>
            <div>
              <h1 className="text-2xl font-bold text-gray-900">
                {user.displayName || user.userName}
              </h1>
              <p className="text-sm text-gray-500">{user.email}</p>
              <div className="flex items-center gap-2 mt-1">
                <span
                  className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                    user.isActive
                      ? 'bg-green-100 text-green-800'
                      : 'bg-red-100 text-red-800'
                  }`}
                >
                  {user.isActive ? 'Active' : 'Inactive'}
                </span>
                {isLocked && (
                  <span className="inline-flex items-center rounded-full bg-yellow-100 px-2 py-0.5 text-xs font-medium text-yellow-800">
                    Locked
                  </span>
                )}
                {user.emailConfirmed && (
                  <span className="inline-flex items-center gap-1 text-xs text-green-600">
                    <CheckCircleIcon className="h-3 w-3" />
                    Email verified
                  </span>
                )}
              </div>
            </div>
          </div>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => setIsEditModalOpen(true)}>
            <PencilIcon className="h-4 w-4 mr-2" />
            Edit
          </Button>
          <Button variant="danger" onClick={() => setIsDeleteModalOpen(true)}>
            <TrashIcon className="h-4 w-4 mr-2" />
            Delete
          </Button>
        </div>
      </div>

      {/* Tabs */}
      <div className="border-b border-gray-200">
        <nav className="-mb-px flex space-x-8 overflow-x-auto" aria-label="Tabs">
          {allTabs.map((tab) => {
            const Icon = tab.icon;
            const badge = getTabBadge(tab);
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={`
                  flex items-center gap-2 whitespace-nowrap border-b-2 py-4 px-1 text-sm font-medium
                  ${
                    activeTab === tab.id
                      ? 'border-primary-500 text-primary-600'
                      : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                  }
                `}
              >
                <Icon className="h-5 w-5" />
                {tab.label}
                {badge !== undefined && (
                  <span className="ml-1 rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">
                    {badge}
                  </span>
                )}
              </button>
            );
          })}
        </nav>
      </div>

      {/* Tab Content */}
      <div>{renderTabContent()}</div>

      {/* Edit Modal */}
      <Modal
        isOpen={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        title="Edit User"
      >
        <form onSubmit={handleEditSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">First Name</label>
              <input
                type="text"
                value={editFormData.firstName || ''}
                onChange={(e) => setEditFormData({ ...editFormData, firstName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Last Name</label>
              <input
                type="text"
                value={editFormData.lastName || ''}
                onChange={(e) => setEditFormData({ ...editFormData, lastName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-sm font-medium text-gray-700">Display Name</label>
              <input
                type="text"
                value={editFormData.displayName || ''}
                onChange={(e) => setEditFormData({ ...editFormData, displayName: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-sm font-medium text-gray-700">Phone Number</label>
              <input
                type="tel"
                value={editFormData.phoneNumber || ''}
                onChange={(e) => setEditFormData({ ...editFormData, phoneNumber: e.target.value })}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
            </div>
          </div>
          <div className="flex gap-6">
            <label className="flex items-center space-x-2">
              <input
                type="checkbox"
                checked={editFormData.isActive ?? true}
                onChange={(e) => setEditFormData({ ...editFormData, isActive: e.target.checked })}
                className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700">Active</span>
            </label>
            <label className="flex items-center space-x-2">
              <input
                type="checkbox"
                checked={editFormData.emailConfirmed ?? false}
                onChange={(e) => setEditFormData({ ...editFormData, emailConfirmed: e.target.checked })}
                className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700">Email Confirmed</span>
            </label>
          </div>
          <div className="flex justify-end gap-3 pt-4">
            <Button type="button" variant="secondary" onClick={() => setIsEditModalOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={updateMutation.isPending}>
              {updateMutation.isPending ? 'Saving...' : 'Save Changes'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => setIsDeleteModalOpen(false)}
        title="Delete User"
      >
        <p className="text-sm text-gray-500">
          Are you sure you want to delete "{user.displayName || user.userName}"?
          This action cannot be undone.
        </p>
        <div className="mt-4 flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setIsDeleteModalOpen(false)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => deleteMutation.mutate()}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
          </Button>
        </div>
      </Modal>

      {/* Reset Password Modal */}
      <Modal
        isOpen={isPasswordModalOpen}
        onClose={() => setIsPasswordModalOpen(false)}
        title="Reset Password"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">New Password</label>
            <input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              placeholder="Enter new password"
            />
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="secondary" onClick={() => setIsPasswordModalOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={() => resetPasswordMutation.mutate(newPassword)}
              disabled={resetPasswordMutation.isPending || !newPassword}
            >
              {resetPasswordMutation.isPending ? 'Resetting...' : 'Reset Password'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Manage Roles Modal */}
      <Modal
        isOpen={isRolesModalOpen}
        onClose={() => setIsRolesModalOpen(false)}
        title="Manage Roles"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-500">Select roles to assign to this user.</p>
          <div className="grid grid-cols-2 gap-2 max-h-64 overflow-y-auto border rounded-md p-3">
            {roles?.map((role) => (
              <label key={role.id} className="flex items-center space-x-2 text-sm">
                <input
                  type="checkbox"
                  checked={selectedRoles.includes(role.name)}
                  onChange={() => toggleRole(role.name)}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
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
            <Button
              onClick={() => setRolesMutation.mutate(selectedRoles)}
              disabled={setRolesMutation.isPending}
            >
              {setRolesMutation.isPending ? 'Saving...' : 'Save Roles'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

// Built-in Tab Components

function ProfileTab({ user }: { user: User }) {
  return (
    <Card>
      <div className="px-4 py-5 sm:p-6">
        <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">Profile Information</h3>
        <dl className="grid grid-cols-1 gap-x-4 gap-y-6 sm:grid-cols-2">
          <div>
            <dt className="text-sm font-medium text-gray-500">Username</dt>
            <dd className="mt-1 text-sm text-gray-900">{user.userName}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Email</dt>
            <dd className="mt-1 text-sm text-gray-900 flex items-center gap-2">
              {user.email}
              {user.emailConfirmed ? (
                <CheckCircleIcon className="h-4 w-4 text-green-500" />
              ) : (
                <XCircleIcon className="h-4 w-4 text-gray-400" />
              )}
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">First Name</dt>
            <dd className="mt-1 text-sm text-gray-900">{user.firstName || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Last Name</dt>
            <dd className="mt-1 text-sm text-gray-900">{user.lastName || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Display Name</dt>
            <dd className="mt-1 text-sm text-gray-900">{user.displayName || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Phone Number</dt>
            <dd className="mt-1 text-sm text-gray-900 flex items-center gap-2">
              {user.phoneNumber || '-'}
              {user.phoneNumber && (
                user.phoneNumberConfirmed ? (
                  <CheckCircleIcon className="h-4 w-4 text-green-500" />
                ) : (
                  <XCircleIcon className="h-4 w-4 text-gray-400" />
                )
              )}
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Created</dt>
            <dd className="mt-1 text-sm text-gray-900">
              {new Date(user.createdAt).toLocaleString()}
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Last Updated</dt>
            <dd className="mt-1 text-sm text-gray-900">
              {user.updatedAt ? new Date(user.updatedAt).toLocaleString() : '-'}
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-gray-500">Last Login</dt>
            <dd className="mt-1 text-sm text-gray-900">
              {user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString() : 'Never'}
            </dd>
          </div>
        </dl>
      </div>
    </Card>
  );
}

function SecurityTab({
  user,
  isLocked,
  onResetPassword,
  onUnlock,
  unlocking,
}: {
  user: User;
  isLocked: boolean;
  onResetPassword: () => void;
  onUnlock: () => void;
  unlocking: boolean;
}) {
  return (
    <div className="space-y-6">
      <Card>
        <div className="px-4 py-5 sm:p-6">
          <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">Account Security</h3>
          <dl className="grid grid-cols-1 gap-x-4 gap-y-6 sm:grid-cols-2">
            <div>
              <dt className="text-sm font-medium text-gray-500">Account Status</dt>
              <dd className="mt-1">
                <span
                  className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
                    user.isActive ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
                  }`}
                >
                  {user.isActive ? 'Active' : 'Inactive'}
                </span>
              </dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Lockout Status</dt>
              <dd className="mt-1 flex items-center gap-2">
                {isLocked ? (
                  <>
                    <span className="inline-flex rounded-full bg-yellow-100 px-2 text-xs font-semibold leading-5 text-yellow-800">
                      Locked until {new Date(user.lockoutEnd!).toLocaleString()}
                    </span>
                    <Button size="sm" variant="secondary" onClick={onUnlock} disabled={unlocking}>
                      <LockOpenIcon className="h-4 w-4 mr-1" />
                      {unlocking ? 'Unlocking...' : 'Unlock'}
                    </Button>
                  </>
                ) : (
                  <span className="text-sm text-gray-900">Not locked</span>
                )}
              </dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Failed Login Attempts</dt>
              <dd className="mt-1 text-sm text-gray-900">{user.accessFailedCount || 0}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500">Lockout Enabled</dt>
              <dd className="mt-1 text-sm text-gray-900">{user.lockoutEnabled ? 'Yes' : 'No'}</dd>
            </div>
          </dl>
        </div>
      </Card>

      <Card>
        <div className="px-4 py-5 sm:p-6">
          <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">Password</h3>
          <p className="text-sm text-gray-500 mb-4">
            Reset the user's password. They will need to use the new password on their next login.
          </p>
          <Button variant="secondary" onClick={onResetPassword}>
            <KeyIcon className="h-4 w-4 mr-2" />
            Reset Password
          </Button>
        </div>
      </Card>
    </div>
  );
}

function RolesTab({ user, onManageRoles }: { user: User; onManageRoles: () => void }) {
  return (
    <Card>
      <div className="px-4 py-5 sm:p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-medium leading-6 text-gray-900">Assigned Roles</h3>
          <Button size="sm" variant="secondary" onClick={onManageRoles}>
            <ShieldCheckIcon className="h-4 w-4 mr-2" />
            Manage Roles
          </Button>
        </div>
        {user.roles && user.roles.length > 0 ? (
          <div className="flex flex-wrap gap-2">
            {user.roles.map((role) => (
              <span
                key={role}
                className="inline-flex items-center rounded-full bg-primary-100 px-3 py-1 text-sm font-medium text-primary-800"
              >
                {role}
              </span>
            ))}
          </div>
        ) : (
          <p className="text-sm text-gray-500">No roles assigned</p>
        )}
      </div>
    </Card>
  );
}

function SessionsTab({ userId }: { userId: string }) {
  const queryClient = useQueryClient();

  const { data: sessions, isLoading } = useQuery({
    queryKey: ['user', userId, 'sessions'],
    queryFn: () => userService.getSessions(userId),
  });

  const revokeSessionMutation = useMutation({
    mutationFn: (sessionId: string) => userService.revokeSession(userId, sessionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', userId, 'sessions'] });
    },
  });

  const revokeAllMutation = useMutation({
    mutationFn: () => userService.revokeAllSessions(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', userId, 'sessions'] });
    },
  });

  const formatUserAgent = (userAgent?: string) => {
    if (!userAgent) return 'Unknown';
    // Simple parsing to extract browser/OS info
    if (userAgent.includes('Chrome')) return 'Chrome';
    if (userAgent.includes('Firefox')) return 'Firefox';
    if (userAgent.includes('Safari')) return 'Safari';
    if (userAgent.includes('Edge')) return 'Edge';
    return userAgent.substring(0, 30) + (userAgent.length > 30 ? '...' : '');
  };

  if (isLoading) {
    return (
      <Card>
        <div className="px-4 py-5 sm:p-6">
          <p className="text-sm text-gray-500">Loading sessions...</p>
        </div>
      </Card>
    );
  }

  return (
    <Card>
      <div className="px-4 py-5 sm:p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-medium leading-6 text-gray-900">Active Sessions</h3>
          {sessions && sessions.length > 0 && (
            <Button
              variant="secondary"
              size="sm"
              onClick={() => {
                if (window.confirm('Are you sure you want to revoke all sessions for this user?')) {
                  revokeAllMutation.mutate();
                }
              }}
              disabled={revokeAllMutation.isPending}
            >
              Revoke All
            </Button>
          )}
        </div>
        {sessions && sessions.length > 0 ? (
          <div className="space-y-3">
            {sessions.map((session) => (
              <div
                key={session.sessionId}
                className="flex items-center justify-between p-3 bg-gray-50 rounded-lg"
              >
                <div className="flex items-center gap-3">
                  <div className="h-10 w-10 rounded-full bg-gray-200 flex items-center justify-center">
                    <ClockIcon className="h-5 w-5 text-gray-500" />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <p className="text-sm font-medium text-gray-900">
                        {session.clientName || session.clientId || 'Direct Session'}
                      </p>
                      {session.isCurrent && (
                        <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                          Current
                        </span>
                      )}
                    </div>
                    <p className="text-xs text-gray-500">
                      {session.ipAddress || 'Unknown IP'} • {formatUserAgent(session.userAgent)}
                    </p>
                    <p className="text-xs text-gray-400">
                      Created: {new Date(session.created).toLocaleString()}
                      {session.expires && ` • Expires: ${new Date(session.expires).toLocaleString()}`}
                    </p>
                  </div>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    if (window.confirm('Are you sure you want to revoke this session?')) {
                      revokeSessionMutation.mutate(session.sessionId);
                    }
                  }}
                  disabled={revokeSessionMutation.isPending}
                >
                  <TrashIcon className="h-4 w-4 text-red-500" />
                </Button>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-sm text-gray-500">No active sessions</p>
        )}
      </div>
    </Card>
  );
}

function MfaTab({ user }: { user: User }) {
  return (
    <Card>
      <div className="px-4 py-5 sm:p-6">
        <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">
          Multi-Factor Authentication
        </h3>
        <dl className="grid grid-cols-1 gap-x-4 gap-y-6 sm:grid-cols-2">
          <div>
            <dt className="text-sm font-medium text-gray-500">MFA Status</dt>
            <dd className="mt-1">
              <span
                className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
                  user.twoFactorEnabled
                    ? 'bg-green-100 text-green-800'
                    : 'bg-gray-100 text-gray-800'
                }`}
              >
                {user.twoFactorEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </dd>
          </div>
        </dl>
        {user.twoFactorEnabled && (
          <div className="mt-4">
            <Button variant="secondary" size="sm">
              Reset MFA
            </Button>
          </div>
        )}
      </div>
    </Card>
  );
}

function ClaimsTab({ user, onRefresh }: { user: User; onRefresh: () => void }) {
  const [newClaim, setNewClaim] = useState({ type: '', value: '' });

  const addClaimMutation = useMutation({
    mutationFn: (claim: { type: string; value: string }) =>
      userService.addClaim(user.id, claim),
    onSuccess: () => {
      onRefresh();
      setNewClaim({ type: '', value: '' });
    },
  });

  const deleteClaimMutation = useMutation({
    mutationFn: ({ type, value }: { type: string; value: string }) =>
      userService.deleteClaim(user.id, type, value),
    onSuccess: () => {
      onRefresh();
    },
  });

  return (
    <Card>
      <div className="px-4 py-5 sm:p-6">
        <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">User Claims</h3>

        {/* Existing claims */}
        {user.claims && user.claims.length > 0 ? (
          <div className="mb-6">
            <table className="min-w-full divide-y divide-gray-200">
              <thead>
                <tr>
                  <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                    Type
                  </th>
                  <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                    Value
                  </th>
                  <th className="px-3 py-2"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {user.claims.map((claim, idx) => (
                  <tr key={`${claim.type}-${claim.value}-${idx}`}>
                    <td className="px-3 py-2 text-sm text-gray-900 font-mono">{claim.type}</td>
                    <td className="px-3 py-2 text-sm text-gray-900">{claim.value}</td>
                    <td className="px-3 py-2 text-right">
                      <button
                        onClick={() => deleteClaimMutation.mutate(claim)}
                        className="text-red-600 hover:text-red-800 text-sm"
                        disabled={deleteClaimMutation.isPending}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-sm text-gray-500 mb-6">No custom claims</p>
        )}

        {/* Add new claim */}
        <div className="border-t pt-4">
          <h4 className="text-sm font-medium text-gray-700 mb-2">Add Claim</h4>
          <div className="flex gap-2">
            <input
              type="text"
              placeholder="Claim type"
              value={newClaim.type}
              onChange={(e) => setNewClaim({ ...newClaim, type: e.target.value })}
              className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            />
            <input
              type="text"
              placeholder="Claim value"
              value={newClaim.value}
              onChange={(e) => setNewClaim({ ...newClaim, value: e.target.value })}
              className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            />
            <Button
              onClick={() => addClaimMutation.mutate(newClaim)}
              disabled={!newClaim.type || !newClaim.value || addClaimMutation.isPending}
            >
              Add
            </Button>
          </div>
        </div>
      </div>
    </Card>
  );
}

function ExternalLoginsTab({ user }: { user: User }) {
  const queryClient = useQueryClient();

  const { data: externalLogins, isLoading } = useQuery({
    queryKey: ['user', user.id, 'external-logins'],
    queryFn: () => userService.getExternalLogins(user.id),
  });

  const removeLoginMutation = useMutation({
    mutationFn: ({ provider, providerKey }: { provider: string; providerKey: string }) =>
      userService.removeExternalLogin(user.id, provider, providerKey),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', user.id, 'external-logins'] });
    },
  });

  if (isLoading) {
    return (
      <Card>
        <div className="px-4 py-5 sm:p-6">
          <p className="text-sm text-gray-500">Loading external logins...</p>
        </div>
      </Card>
    );
  }

  return (
    <Card>
      <div className="px-4 py-5 sm:p-6">
        <h3 className="text-lg font-medium leading-6 text-gray-900 mb-4">External Logins</h3>
        {externalLogins && externalLogins.length > 0 ? (
          <div className="space-y-3">
            {externalLogins.map((login) => (
              <div
                key={`${login.loginProvider}-${login.providerKey}`}
                className="flex items-center justify-between p-3 bg-gray-50 rounded-lg"
              >
                <div className="flex items-center gap-3">
                  <div className="h-10 w-10 rounded-full bg-gray-200 flex items-center justify-center">
                    <LinkIcon className="h-5 w-5 text-gray-500" />
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-900">{login.providerDisplayName}</p>
                    <p className="text-xs text-gray-500 font-mono">
                      {login.providerKey.length > 20
                        ? `${login.providerKey.substring(0, 20)}...`
                        : login.providerKey}
                    </p>
                  </div>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() =>
                    removeLoginMutation.mutate({
                      provider: login.loginProvider,
                      providerKey: login.providerKey,
                    })
                  }
                  disabled={removeLoginMutation.isPending}
                >
                  <TrashIcon className="h-4 w-4 text-red-500" />
                </Button>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-sm text-gray-500">No external logins linked</p>
        )}
      </div>
    </Card>
  );
}

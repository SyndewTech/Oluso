import api from './api';
import type {
  User,
  Role,
  CreateUserRequest,
  UpdateUserRequest,
  CreateRoleRequest,
  UpdateRoleRequest,
  PagedResult,
  ExternalLogin,
  UserSession,
} from '../types/user';

export interface UserSearchParams {
  page?: number;
  pageSize?: number;
  search?: string;
  role?: string;
  isActive?: boolean;
}

export const userService = {
  getAll: async (params: UserSearchParams = {}): Promise<PagedResult<User>> => {
    const { page = 1, pageSize = 10, search, role, isActive } = params;
    const response = await api.get('/users', {
      params: { page, pageSize, search, role, isActive },
    });
    return response.data;
  },

  getById: async (id: string): Promise<User> => {
    const response = await api.get(`/users/${id}`);
    return response.data;
  },

  create: async (data: CreateUserRequest): Promise<User> => {
    const response = await api.post('/users', data);
    return response.data;
  },

  update: async (id: string, data: UpdateUserRequest): Promise<User> => {
    const response = await api.put(`/users/${id}`, data);
    return response.data;
  },

  delete: async (id: string): Promise<void> => {
    await api.delete(`/users/${id}`);
  },

  resetPassword: async (id: string, newPassword: string): Promise<void> => {
    await api.post(`/users/${id}/reset-password`, { newPassword });
  },

  unlock: async (id: string): Promise<void> => {
    await api.post(`/users/${id}/unlock`);
  },

  // Role assignment
  getRoles: async (userId: string): Promise<string[]> => {
    const response = await api.get(`/users/${userId}/roles`);
    return response.data;
  },

  setRoles: async (userId: string, roles: string[]): Promise<void> => {
    await api.put(`/users/${userId}/roles`, { roles });
  },

  addRole: async (userId: string, roleName: string): Promise<void> => {
    await api.post(`/users/${userId}/roles/${encodeURIComponent(roleName)}`);
  },

  removeRole: async (userId: string, roleName: string): Promise<void> => {
    await api.delete(`/users/${userId}/roles/${encodeURIComponent(roleName)}`);
  },

  // Claims management
  addClaim: async (
    userId: string,
    claim: { type: string; value: string }
  ): Promise<void> => {
    await api.post(`/users/${userId}/claims`, claim);
  },

  deleteClaim: async (userId: string, claimType: string, claimValue: string): Promise<void> => {
    await api.delete(`/users/${userId}/claims`, {
      params: { type: claimType, value: claimValue },
    });
  },

  // External logins
  getExternalLogins: async (userId: string): Promise<ExternalLogin[]> => {
    const response = await api.get(`/users/${userId}/external-logins`);
    return response.data;
  },

  removeExternalLogin: async (userId: string, provider: string, providerKey: string): Promise<void> => {
    await api.delete(`/users/${userId}/external-logins/${encodeURIComponent(provider)}`, {
      params: { providerKey },
    });
  },

  // Sessions
  getSessions: async (userId: string): Promise<UserSession[]> => {
    const response = await api.get(`/users/${userId}/sessions`);
    return response.data;
  },

  revokeSession: async (userId: string, sessionId: string): Promise<void> => {
    await api.delete(`/users/${userId}/sessions/${encodeURIComponent(sessionId)}`);
  },

  revokeAllSessions: async (userId: string): Promise<void> => {
    await api.delete(`/users/${userId}/sessions`);
  },
};

export const roleService = {
  getAll: async (): Promise<Role[]> => {
    const response = await api.get('/roles');
    return response.data;
  },

  getById: async (id: string): Promise<Role> => {
    const response = await api.get(`/roles/${id}`);
    return response.data;
  },

  create: async (data: CreateRoleRequest): Promise<Role> => {
    const response = await api.post('/roles', data);
    return response.data;
  },

  update: async (id: string, data: UpdateRoleRequest): Promise<Role> => {
    const response = await api.put(`/roles/${id}`, data);
    return response.data;
  },

  delete: async (id: string): Promise<void> => {
    await api.delete(`/roles/${id}`);
  },

  getUsersInRole: async (roleId: string): Promise<User[]> => {
    const response = await api.get(`/roles/${roleId}/users`);
    return response.data;
  },
};

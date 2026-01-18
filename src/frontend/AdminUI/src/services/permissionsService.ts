import api from './api';
import type {
  PermissionsResponse,
  MyPermissionsResponse,
  PermissionCheckResult,
} from '../types/permissions';

export const permissionsService = {
  /**
   * Get all available permissions grouped by category.
   * SuperAdmin users will see all permissions, others see filtered list.
   */
  getAll: async (): Promise<PermissionsResponse> => {
    const response = await api.get('/permissions');
    return response.data;
  },

  /**
   * Get a flat list of all permission names.
   */
  getList: async (): Promise<string[]> => {
    const response = await api.get('/permissions/list');
    return response.data;
  },

  /**
   * Get permissions assigned to a specific role.
   */
  getRolePermissions: async (roleId: string): Promise<string[]> => {
    const response = await api.get(`/permissions/role/${roleId}`);
    return response.data;
  },

  /**
   * Check if the current user has a specific permission.
   */
  checkPermission: async (permission: string): Promise<PermissionCheckResult> => {
    const response = await api.get(`/permissions/check/${encodeURIComponent(permission)}`);
    return response.data;
  },

  /**
   * Get the current user's effective permissions.
   */
  getMyPermissions: async (): Promise<MyPermissionsResponse> => {
    const response = await api.get('/permissions/my');
    return response.data;
  },
};

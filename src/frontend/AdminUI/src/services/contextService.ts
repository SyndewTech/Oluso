import api from './api';
import type {
  AdminContext,
  TenantSwitch,
  OrganizationSwitch,
  TenantDetail,
} from '../types/context';

export const contextService = {
  /**
   * Get the current admin context including user info, current tenant, and available resources.
   * This is the first call the admin dashboard should make to understand the user's access.
   */
  getContext: async (): Promise<AdminContext> => {
    const response = await api.get('/context');
    return response.data;
  },

  /**
   * Get list of tenants the current user can switch to.
   * SuperAdmins see all tenants, OrgAdmins see tenants in their organizations.
   */
  getAvailableTenants: async (organizationId?: string): Promise<TenantSwitch[]> => {
    const params = organizationId ? { organizationId } : {};
    const response = await api.get('/context/tenants', { params });
    return response.data;
  },

  /**
   * Get list of organizations the current user belongs to.
   */
  getAvailableOrganizations: async (): Promise<OrganizationSwitch[]> => {
    const response = await api.get('/context/organizations');
    return response.data;
  },

  /**
   * Get details about a specific tenant before switching to it.
   * Validates that the user has access to the tenant.
   */
  getTenantDetails: async (tenantId: string): Promise<TenantDetail> => {
    const response = await api.get(`/context/tenants/${tenantId}`);
    return response.data;
  },
};

import api from './api';
import type {
  Tenant,
  PasswordPolicy,
  CreateTenantRequest,
  UpdateTenantRequest,
  UpdatePasswordPolicyRequest,
  ProtocolConfiguration,
  UpdateProtocolConfigurationRequest,
} from '../types/tenant';

export const tenantService = {
  getAll: async (): Promise<Tenant[]> => {
    const response = await api.get('/tenants');
    return response.data;
  },

  getById: async (tenantId: string): Promise<Tenant> => {
    const response = await api.get(`/tenants/${tenantId}`);
    return response.data;
  },

  create: async (data: CreateTenantRequest): Promise<Tenant> => {
    const response = await api.post('/tenants', data);
    return response.data;
  },

  update: async (tenantId: string, data: UpdateTenantRequest): Promise<Tenant> => {
    const response = await api.put(`/tenants/${tenantId}`, data);
    return response.data;
  },

  delete: async (tenantId: string): Promise<void> => {
    await api.delete(`/tenants/${tenantId}`);
  },

  // Password Policy
  getPasswordPolicy: async (tenantId: string): Promise<PasswordPolicy> => {
    const response = await api.get(`/tenants/${tenantId}/password-policy`);
    return response.data;
  },

  updatePasswordPolicy: async (
    tenantId: string,
    data: UpdatePasswordPolicyRequest
  ): Promise<PasswordPolicy> => {
    const response = await api.put(`/tenants/${tenantId}/password-policy`, data);
    return response.data;
  },

  resetPasswordPolicy: async (tenantId: string): Promise<void> => {
    await api.delete(`/tenants/${tenantId}/password-policy`);
  },

  // Protocol Configuration
  getProtocolConfiguration: async (tenantId: string): Promise<ProtocolConfiguration> => {
    const response = await api.get(`/tenants/${tenantId}/protocol-configuration`);
    return response.data;
  },

  updateProtocolConfiguration: async (
    tenantId: string,
    data: UpdateProtocolConfigurationRequest
  ): Promise<ProtocolConfiguration> => {
    const response = await api.put(`/tenants/${tenantId}/protocol-configuration`, data);
    return response.data;
  },

  resetProtocolConfiguration: async (tenantId: string): Promise<void> => {
    await api.delete(`/tenants/${tenantId}/protocol-configuration`);
  },
};

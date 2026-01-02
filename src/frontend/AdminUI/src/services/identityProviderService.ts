import api from './api';
import type {
  IdentityProvider,
  IdentityProviderEdit,
  CreateIdentityProviderRequest,
  UpdateIdentityProviderRequest,
  ProviderTypeInfo,
  TestResult,
} from '../types/identityProviders';

export const identityProviderService = {
  getAll: async (): Promise<IdentityProvider[]> => {
    const response = await api.get('/identity-providers');
    return response.data;
  },

  getById: async (id: number): Promise<IdentityProvider> => {
    const response = await api.get(`/identity-providers/${id}`);
    return response.data;
  },

  /** Get provider for editing - returns full configuration with unmasked secrets */
  getForEdit: async (id: number): Promise<IdentityProviderEdit> => {
    const response = await api.get(`/identity-providers/${id}/edit`);
    return response.data;
  },

  getByScheme: async (scheme: string): Promise<IdentityProvider> => {
    const response = await api.get(`/identity-providers/by-scheme/${encodeURIComponent(scheme)}`);
    return response.data;
  },

  getProviderTypes: async (): Promise<ProviderTypeInfo[]> => {
    const response = await api.get('/identity-providers/types');
    return response.data;
  },

  create: async (data: CreateIdentityProviderRequest): Promise<IdentityProvider> => {
    const response = await api.post('/identity-providers', data);
    return response.data;
  },

  update: async (id: number, data: UpdateIdentityProviderRequest): Promise<IdentityProvider> => {
    const response = await api.put(`/identity-providers/${id}`, data);
    return response.data;
  },

  delete: async (id: number): Promise<void> => {
    await api.delete(`/identity-providers/${id}`);
  },

  toggle: async (id: number): Promise<IdentityProvider> => {
    const response = await api.post(`/identity-providers/${id}/toggle`);
    return response.data;
  },

  test: async (id: number): Promise<TestResult> => {
    const response = await api.post(`/identity-providers/${id}/test`);
    return response.data;
  },

  getSecret: async (id: number, secretKey: string): Promise<string | null> => {
    try {
      const response = await api.get(`/identity-providers/${id}/secret/${encodeURIComponent(secretKey)}`);
      return response.data.value;
    } catch {
      return null;
    }
  },

  getServerInfo: async (): Promise<{ issuerUri: string; callbackPathTemplate: string }> => {
    const response = await api.get('/identity-providers/server-info');
    return response.data;
  },

  checkSchemeAvailability: async (scheme: string): Promise<{ available: boolean; message?: string }> => {
    const response = await api.get(`/identity-providers/check-scheme/${encodeURIComponent(scheme)}`);
    return response.data;
  },
};

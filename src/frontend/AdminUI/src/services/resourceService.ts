import api from './api';
import type {
  ApiResource,
  ApiScope,
  IdentityResource,
  CreateApiResourceRequest,
  UpdateApiResourceRequest,
  CreateApiScopeRequest,
  UpdateApiScopeRequest,
  CreateIdentityResourceRequest,
  ApiScopeSummary,
  ApiResourceSummary,
} from '../types/resources';
import type { PaginatedResult } from '../types/audit';

// Helper to wrap array responses in pagination structure
function wrapInPagination<T>(data: T[] | PaginatedResult<T>): PaginatedResult<T> {
  if (Array.isArray(data)) {
    return {
      items: data,
      totalCount: data.length,
      pageNumber: 1,
      pageSize: data.length,
      totalPages: 1,
    };
  }
  return data;
}

export const apiResourceService = {
  getAll: async (page = 1, pageSize = 10): Promise<PaginatedResult<ApiResource>> => {
    const response = await api.get('/api-resources', {
      params: { pageNumber: page, pageSize },
    });
    return wrapInPagination(response.data);
  },

  getById: async (id: number): Promise<ApiResource> => {
    const response = await api.get(`/api-resources/${id}`);
    return response.data;
  },

  getByName: async (name: string): Promise<ApiResource> => {
    const response = await api.get(`/api-resources/by-name/${encodeURIComponent(name)}`);
    return response.data;
  },

  create: async (data: CreateApiResourceRequest): Promise<ApiResource> => {
    const response = await api.post('/api-resources', data);
    return response.data;
  },

  update: async (id: number, data: UpdateApiResourceRequest): Promise<ApiResource> => {
    const response = await api.put(`/api-resources/${id}`, data);
    return response.data;
  },

  delete: async (id: number): Promise<void> => {
    await api.delete(`/api-resources/${id}`);
  },

  // Scope management for a resource
  addScope: async (id: number, scopeName: string): Promise<ApiResource> => {
    const response = await api.post(`/api-resources/${id}/scopes`, {
      scopeName,
    });
    return response.data;
  },

  removeScope: async (id: number, scopeName: string): Promise<ApiResource> => {
    const response = await api.delete(
      `/api-resources/${id}/scopes/${encodeURIComponent(scopeName)}`
    );
    return response.data;
  },

  getAvailableScopes: async (): Promise<ApiScopeSummary[]> => {
    const response = await api.get('/api-resources/available-scopes');
    return response.data;
  },
};

export const apiScopeService = {
  getAll: async (page = 1, pageSize = 10): Promise<PaginatedResult<ApiScope>> => {
    const response = await api.get('/api-scopes', {
      params: { pageNumber: page, pageSize },
    });
    return wrapInPagination(response.data);
  },

  getById: async (id: number): Promise<ApiScope> => {
    const response = await api.get(`/api-scopes/${id}`);
    return response.data;
  },

  getByName: async (name: string): Promise<ApiScope> => {
    const response = await api.get(`/api-scopes/by-name/${encodeURIComponent(name)}`);
    return response.data;
  },

  create: async (data: CreateApiScopeRequest): Promise<ApiScope> => {
    const response = await api.post('/api-scopes', data);
    return response.data;
  },

  update: async (id: number, data: UpdateApiScopeRequest): Promise<ApiScope> => {
    const response = await api.put(`/api-scopes/${id}`, data);
    return response.data;
  },

  delete: async (id: number): Promise<void> => {
    await api.delete(`/api-scopes/${id}`);
  },

  getAvailableResources: async (): Promise<ApiResourceSummary[]> => {
    const response = await api.get('/api-scopes/available-resources');
    return response.data;
  },
};

export const identityResourceService = {
  getAll: async (page = 1, pageSize = 10): Promise<PaginatedResult<IdentityResource>> => {
    const response = await api.get('/identity-resources', {
      params: { pageNumber: page, pageSize },
    });
    return wrapInPagination(response.data);
  },

  getById: async (id: number): Promise<IdentityResource> => {
    const response = await api.get(`/identity-resources/${id}`);
    return response.data;
  },

  getByName: async (name: string): Promise<IdentityResource> => {
    const response = await api.get(`/identity-resources/by-name/${encodeURIComponent(name)}`);
    return response.data;
  },

  create: async (data: CreateIdentityResourceRequest): Promise<IdentityResource> => {
    const response = await api.post('/identity-resources', data);
    return response.data;
  },

  update: async (id: number, data: Partial<IdentityResource>): Promise<IdentityResource> => {
    const response = await api.put(`/identity-resources/${id}`, data);
    return response.data;
  },

  delete: async (id: number): Promise<void> => {
    await api.delete(`/identity-resources/${id}`);
  },
};

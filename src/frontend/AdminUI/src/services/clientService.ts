import api from './api';
import type { Client, CreateClientRequest, UpdateClientRequest } from '../types/client';

// Helper to wrap array responses in pagination structure
interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

function wrapInPagination<T>(data: T[]): PaginatedResult<T> {
  return {
    items: data,
    totalCount: data.length,
    pageNumber: 1,
    pageSize: data.length,
    totalPages: 1,
  };
}

export const clientService = {
  getAll: async (): Promise<PaginatedResult<Client>> => {
    const response = await api.get('/clients');
    // Backend returns array, wrap in pagination structure
    return wrapInPagination(response.data);
  },

  getByClientId: async (clientId: string): Promise<Client> => {
    const response = await api.get(`/clients/${encodeURIComponent(clientId)}`);
    return response.data;
  },

  create: async (data: CreateClientRequest): Promise<Client> => {
    const response = await api.post('/clients', data);
    return response.data;
  },

  update: async (clientId: string, data: UpdateClientRequest): Promise<Client> => {
    const response = await api.put(`/clients/${encodeURIComponent(clientId)}`, data);
    return response.data;
  },

  delete: async (clientId: string): Promise<void> => {
    await api.delete(`/clients/${encodeURIComponent(clientId)}`);
  },

  regenerateSecret: async (clientId: string): Promise<{ clientId: string; clientSecret: string }> => {
    const response = await api.post(`/clients/${encodeURIComponent(clientId)}/regenerate-secret`);
    return response.data;
  },
};

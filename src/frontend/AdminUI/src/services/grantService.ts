import api from './api';
import type { PersistedGrant, DeviceFlowCode, ServerSideSession, GrantFilter } from '../types/grants';
import type { PaginatedResult } from '../types/audit';

export const grantService = {
  getPersistedGrants: async (
    filter: GrantFilter,
    page = 1,
    pageSize = 10
  ): Promise<PaginatedResult<PersistedGrant>> => {
    const response = await api.get('/grants', {
      params: { ...filter, pageNumber: page, pageSize },
    });
    return response.data;
  },

  deletePersistedGrant: async (key: string): Promise<void> => {
    await api.delete(`/grants/${encodeURIComponent(key)}`);
  },

  deleteAllPersistedGrants: async (filter: GrantFilter): Promise<void> => {
    await api.delete('/grants', { params: filter });
  },

  getDeviceFlowCodes: async (
    page = 1,
    pageSize = 10
  ): Promise<PaginatedResult<DeviceFlowCode>> => {
    const response = await api.get('/grants/device-codes', {
      params: { pageNumber: page, pageSize },
    });
    return response.data;
  },

  deleteDeviceFlowCode: async (deviceCode: string): Promise<void> => {
    await api.delete(`/grants/device-codes/${encodeURIComponent(deviceCode)}`);
  },

  getSessions: async (
    subjectId?: string,
    page = 1,
    pageSize = 10
  ): Promise<PaginatedResult<ServerSideSession>> => {
    const response = await api.get('/grants/sessions', {
      params: { subjectId, pageNumber: page, pageSize },
    });
    return response.data;
  },

  deleteSession: async (key: string): Promise<void> => {
    await api.delete(`/grants/sessions/${encodeURIComponent(key)}`);
  },

  deleteUserSessions: async (subjectId: string): Promise<void> => {
    await api.delete(`/grants/sessions/by-subject/${encodeURIComponent(subjectId)}`);
  },
};

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaginatedResponse } from '@syndew/oluso-ui-core';
import { PersistedGrant, DeviceFlowCode, ServerSideSession, GrantFilter } from '../models/grant.model';

@Injectable({
  providedIn: 'root',
})
export class GrantService {
  private readonly api = inject(ApiService);

  getPersistedGrants(
    filter: GrantFilter = {},
    page = 1,
    pageSize = 10
  ): Observable<PaginatedResponse<PersistedGrant>> {
    return this.api.get<PaginatedResponse<PersistedGrant>>('/grants', {
      ...filter,
      pageNumber: page,
      pageSize,
    });
  }

  deletePersistedGrant(key: string): Observable<void> {
    return this.api.delete<void>(`/grants/${encodeURIComponent(key)}`);
  }

  deleteAllPersistedGrants(filter: GrantFilter): Observable<void> {
    const params = { ...filter } as Record<string, string | number | boolean>;
    return this.api.delete<void>('/grants', params);
  }

  getDeviceFlowCodes(page = 1, pageSize = 10): Observable<PaginatedResponse<DeviceFlowCode>> {
    return this.api.get<PaginatedResponse<DeviceFlowCode>>('/grants/device-codes', {
      pageNumber: page,
      pageSize,
    });
  }

  deleteDeviceFlowCode(deviceCode: string): Observable<void> {
    return this.api.delete<void>(`/grants/device-codes/${encodeURIComponent(deviceCode)}`);
  }

  getSessions(subjectId?: string, page = 1, pageSize = 10): Observable<PaginatedResponse<ServerSideSession>> {
    return this.api.get<PaginatedResponse<ServerSideSession>>('/grants/sessions', {
      ...(subjectId && { subjectId }),
      pageNumber: page,
      pageSize,
    });
  }

  deleteSession(key: string): Observable<void> {
    return this.api.delete<void>(`/grants/sessions/${encodeURIComponent(key)}`);
  }

  deleteUserSessions(subjectId: string): Observable<void> {
    return this.api.delete<void>(`/grants/sessions/by-subject/${encodeURIComponent(subjectId)}`);
  }
}

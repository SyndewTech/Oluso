import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  PlatformRole,
  PlatformRoleCandidate,
  PlatformRoleHolder,
  PlatformRoleTenantOption,
} from '../models/platform.model';

/** Platform-wide roles and who holds them in every organization. SuperAdmin only. */
@Injectable({ providedIn: 'root' })
export class PlatformRoleService {
  private readonly api = inject(ApiService);
  private readonly basePath = '/platform/roles';

  getAll(): Observable<PlatformRole[]> {
    return this.api.get<PlatformRole[]>(this.basePath);
  }

  getHolders(roleId: string): Observable<PlatformRoleHolder[]> {
    return this.api.get<PlatformRoleHolder[]>(`${this.basePath}/${roleId}/holders`);
  }

  assign(roleId: string, userId: string, tenantId: string): Observable<void> {
    return this.api.post<void>(`${this.basePath}/${roleId}/holders`, { userId, tenantId });
  }

  remove(roleId: string, holder: PlatformRoleHolder): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/${roleId}/holders/${holder.userId}`, { tenantId: holder.tenantId });
  }

  searchTenants(search: string): Observable<PlatformRoleTenantOption[]> {
    return this.api.get<PlatformRoleTenantOption[]>(`${this.basePath}/tenant-options`, search ? { search } : undefined);
  }

  searchCandidates(tenantId: string, search: string): Observable<PlatformRoleCandidate[]> {
    return this.api.get<PlatformRoleCandidate[]>(
      `${this.basePath}/candidates`,
      search ? { tenantId, search } : { tenantId }
    );
  }
}

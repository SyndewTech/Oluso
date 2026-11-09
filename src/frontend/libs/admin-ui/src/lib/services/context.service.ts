import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap, shareReplay } from 'rxjs';
import { ApiService } from './api.service';
import {
  AdminContext,
  TenantSwitch,
  OrganizationSwitch,
  TenantDetail,
} from '../models/context.model';

@Injectable({
  providedIn: 'root',
})
export class ContextService {
  private readonly api = inject(ApiService);

  readonly tenants = signal<TenantSwitch[]>([]);
  readonly organizations = signal<OrganizationSwitch[]>([]);
  readonly adminContext = signal<AdminContext | null>(null);
  private tenants$: Observable<TenantSwitch[]> | null = null;
  private organizations$: Observable<OrganizationSwitch[]> | null = null;

  getContext(): Observable<AdminContext> {
    return this.api.get<AdminContext>('/context').pipe(
      tap(ctx => this.adminContext.set(ctx))
    );
  }

  getAvailableTenants(organizationId?: string): Observable<TenantSwitch[]> {
    if (organizationId) {
      return this.api.get<TenantSwitch[]>('/context/tenants', { organizationId });
    }
    if (!this.tenants$) {
      this.tenants$ = this.api.get<TenantSwitch[]>('/context/tenants').pipe(
        tap((data) => this.tenants.set(data)),
        shareReplay(1)
      );
    }
    return this.tenants$;
  }

  getAvailableOrganizations(): Observable<OrganizationSwitch[]> {
    if (!this.organizations$) {
      this.organizations$ = this.api.get<OrganizationSwitch[]>('/context/organizations').pipe(
        tap((data) => this.organizations.set(data)),
        shareReplay(1)
      );
    }
    return this.organizations$;
  }

  getTenantDetails(tenantId: string): Observable<TenantDetail> {
    return this.api.get<TenantDetail>(`/context/tenants/${tenantId}`);
  }

  invalidateCache(): void {
    this.tenants$ = null;
    this.organizations$ = null;
  }
}

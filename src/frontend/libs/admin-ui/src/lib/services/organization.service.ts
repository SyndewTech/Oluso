import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  Organization,
  CreateOrganizationRequest,
  UpdateOrganizationRequest,
  OrganizationMember,
  OrganizationTenant,
  OrganizationFeaturesResponse,
  SetOrganizationFeaturesRequest,
  ProvisionOrganizationRequest,
  ProvisionOrganizationResponse,
  MemberTenantAccess,
  OrganizationInvitation,
  CreateOrganizationInvitationRequest,
  InvitationStatus,
  OrganizationUser,
} from '../models/organization.model';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse } from '@oluso/ui-core';
import { CreateTenantRequest } from '../models/tenant.model';

@Injectable({
  providedIn: 'root',
})
export class OrganizationService {
  private readonly api = inject(ApiService);

  getAll(
    pagination: PaginationParams,
    filters?: { search?: string; includeDisabled?: boolean }
  ): Observable<PaginatedResponse<Organization>> {
    return this.api.getPaginated<Organization>(
      '/organizations',
      pagination,
      filters as Record<string, string | number | boolean>
    );
  }

  getById(id: string): Observable<Organization> {
    return this.api.get<Organization>(`/organizations/${id}`);
  }

  create(request: CreateOrganizationRequest): Observable<Organization> {
    return this.api.post<Organization>('/organizations', request);
  }

  update(id: string, request: UpdateOrganizationRequest): Observable<Organization> {
    return this.api.put<Organization>(`/organizations/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/organizations/${id}`);
  }

  enable(id: string): Observable<void> {
    return this.api.post<void>(`/organizations/${id}/enable`, {});
  }

  disable(id: string): Observable<void> {
    return this.api.post<void>(`/organizations/${id}/disable`, {});
  }

  // Tenants
  getTenants(
    orgId: string,
    pagination: PaginationParams,
    filters?: { search?: string }
  ): Observable<PaginatedResponse<OrganizationTenant>> {
    return this.api.getPaginated<OrganizationTenant>(
      `/organizations/${orgId}/tenants`,
      pagination,
      filters as Record<string, string | number | boolean>
    );
  }

  createTenant(orgId: string, request: CreateTenantRequest): Observable<OrganizationTenant> {
    return this.api.post<OrganizationTenant>(`/organizations/${orgId}/tenants`, request);
  }

  // Members
  getMembers(
    orgId: string,
    pagination: PaginationParams,
    filters?: { search?: string }
  ): Observable<PaginatedResponse<OrganizationMember>> {
    return this.api.getPaginated<OrganizationMember>(
      `/organizations/${orgId}/members`,
      pagination,
      filters as Record<string, string | number | boolean>
    );
  }

  addMember(orgId: string, request: { userId: string; role: string; allowedTenantIds?: string[] }): Observable<OrganizationMember> {
    return this.api.post<OrganizationMember>(`/organizations/${orgId}/members`, request);
  }

  updateMember(orgId: string, memberId: string, request: { role?: string; allowedTenantIds?: string[] }): Observable<OrganizationMember> {
    return this.api.put<OrganizationMember>(`/organizations/${orgId}/members/${memberId}`, request);
  }

  removeMember(orgId: string, memberId: string): Observable<void> {
    return this.api.delete<void>(`/organizations/${orgId}/members/${memberId}`);
  }

  // Member tenant access
  getMemberTenants(orgId: string, userId: string): Observable<MemberTenantAccess[]> {
    return this.api.get<MemberTenantAccess[]>(`/organizations/${orgId}/members/${userId}/tenants`);
  }

  grantMemberTenant(orgId: string, userId: string, tenantId: string): Observable<void> {
    return this.api.post<void>(`/organizations/${orgId}/members/${userId}/tenants/${tenantId}`, {});
  }

  revokeMemberTenant(orgId: string, userId: string, tenantId: string): Observable<void> {
    return this.api.delete<void>(`/organizations/${orgId}/members/${userId}/tenants/${tenantId}`);
  }

  updateMemberTenantRole(orgId: string, userId: string, tenantId: string, roleName: string): Observable<void> {
    return this.api.put<void>(`/organizations/${orgId}/members/${userId}/tenants/${tenantId}/role`, { roleName });
  }

  // Features
  getFeatures(orgId: string): Observable<OrganizationFeaturesResponse> {
    return this.api.get<OrganizationFeaturesResponse>(`/organizations/${orgId}/features`);
  }

  setFeatures(orgId: string, request: SetOrganizationFeaturesRequest): Observable<void> {
    return this.api.put<void>(`/organizations/${orgId}/features`, request);
  }

  enableFeature(orgId: string, featureKey: string, expiresAt?: string): Observable<void> {
    return this.api.post<void>(`/organizations/${orgId}/features/${featureKey}`, { expiresAt });
  }

  disableFeature(orgId: string, featureKey: string): Observable<void> {
    return this.api.delete<void>(`/organizations/${orgId}/features/${featureKey}`);
  }

  /**
   * Users across every tenant in the organization. The admin user list is
   * tenant-scoped, so it can't find someone whose home tenant is a sibling.
   */
  searchOrganizationUsers(
    orgId: string,
    pagination: PaginationParams,
    search?: string
  ): Observable<PaginatedResponse<OrganizationUser>> {
    return this.api.getPaginated<OrganizationUser>(
      `/organizations/${orgId}/users`,
      pagination,
      search ? { search } : undefined
    );
  }

  // Invitations
  getInvitations(orgId: string, status?: InvitationStatus): Observable<OrganizationInvitation[]> {
    return this.api.get<OrganizationInvitation[]>(
      `/organizations/${orgId}/invitations`,
      status ? { status } : undefined
    );
  }

  createInvitation(
    orgId: string,
    request: CreateOrganizationInvitationRequest
  ): Observable<OrganizationInvitation> {
    return this.api.post<OrganizationInvitation>(`/organizations/${orgId}/invitations`, request);
  }

  resendInvitation(orgId: string, invitationId: string): Observable<{ emailSent: boolean; expiresAt: string }> {
    return this.api.post<{ emailSent: boolean; expiresAt: string }>(
      `/organizations/${orgId}/invitations/${invitationId}/resend`,
      {}
    );
  }

  revokeInvitation(orgId: string, invitationId: string): Observable<void> {
    return this.api.post<void>(`/organizations/${orgId}/invitations/${invitationId}/revoke`, {});
  }

  deleteInvitation(orgId: string, invitationId: string): Observable<void> {
    return this.api.delete<void>(`/organizations/${orgId}/invitations/${invitationId}`);
  }

  // Provisioning
  provision(request: ProvisionOrganizationRequest): Observable<ProvisionOrganizationResponse> {
    return this.api.post<ProvisionOrganizationResponse>('/organizations/provision', request);
  }
}

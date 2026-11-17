import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse } from '@syndew/oluso-ui-core';
import { RoleDto, CreateRoleRequest, UserInRoleDto, RoleImportResult } from '../models/role.model';

@Injectable({
  providedIn: 'root',
})
export class RoleService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/roles';

  /**
   * Get all roles with pagination
   */
  getRoles(pagination: PaginationParams): Observable<PaginatedResponse<RoleDto>> {
    return this.api.getPaginated<RoleDto>(this.endpoint, pagination);
  }

  /**
   * Get all roles without pagination (for dropdowns and list)
   */
  getAllRoles(): Observable<RoleDto[]> {
    return this.api.get<RoleDto[]>(this.endpoint);
  }

  /**
   * Get a single role by ID
   */
  getRole(id: string): Observable<RoleDto> {
    return this.api.get<RoleDto>(`${this.endpoint}/${id}`);
  }

  /**
   * Create a new role
   */
  createRole(request: CreateRoleRequest): Observable<RoleDto> {
    return this.api.post<RoleDto>(this.endpoint, request);
  }

  /**
   * Update an existing role
   */
  updateRole(id: string, role: RoleDto): Observable<RoleDto> {
    return this.api.put<RoleDto>(`${this.endpoint}/${id}`, role);
  }

  /**
   * Delete a role
   */
  deleteRole(id: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  /**
   * Get users assigned to a role
   */
  getUsersInRole(roleId: string): Observable<UserInRoleDto[]> {
    return this.api.get<UserInRoleDto[]>(`${this.endpoint}/${roleId}/users`);
  }

  /** The roles as an export document (plain JSON), to save as a file. */
  exportRoles(roleIds: string[]): Observable<unknown> {
    return this.api.post<unknown>(`${this.endpoint}/export`, { roleIds });
  }

  /** What importing the file would do here. Writes nothing. */
  previewImport(content: string, replaceExisting: boolean): Observable<RoleImportResult> {
    return this.api.post<RoleImportResult>(`${this.endpoint}/import/preview`, { content, replaceExisting });
  }

  importRoles(content: string, replaceExisting: boolean): Observable<RoleImportResult> {
    return this.api.post<RoleImportResult>(`${this.endpoint}/import`, { content, replaceExisting });
  }
}

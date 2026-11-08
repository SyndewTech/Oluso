import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse } from '@oluso/ui-core';
import { ResourceDto, CreateResourceRequest } from '../models/resource.model';
import { ApiScopeSummaryDto } from '../models/api-scope.model';

@Injectable({
  providedIn: 'root',
})
export class ResourceService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/resources';

  /**
   * Get all resources with pagination
   */
  getAll(pagination: PaginationParams): Observable<PaginatedResponse<ResourceDto>> {
    return this.api.getPaginated<ResourceDto>(this.endpoint, pagination);
  }

  /**
   * Get a single resource by ID
   */
  getById(id: number): Observable<ResourceDto> {
    return this.api.get<ResourceDto>(`${this.endpoint}/${id}`);
  }

  /**
   * Create a new resource
   */
  create(request: CreateResourceRequest): Observable<ResourceDto> {
    return this.api.post<ResourceDto>(this.endpoint, request);
  }

  /**
   * Update an existing resource
   */
  update(id: number, resource: ResourceDto): Observable<ResourceDto> {
    return this.api.put<ResourceDto>(`${this.endpoint}/${id}`, resource);
  }

  /**
   * Delete a resource
   */
  delete(id: number): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  /**
   * Get available scopes (for adding to resource)
   */
  getAvailableScopes(): Observable<ApiScopeSummaryDto[]> {
    return this.api.get<ApiScopeSummaryDto[]>(`${this.endpoint}/available-scopes`);
  }

  /**
   * Add a scope to a resource
   */
  addScope(resourceId: number, scopeName: string): Observable<void> {
    return this.api.post<void>(`${this.endpoint}/${resourceId}/scopes`, { scopeName });
  }

  /**
   * Remove a scope from a resource
   */
  removeScope(resourceId: number, scopeName: string): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${resourceId}/scopes/${encodeURIComponent(scopeName)}`);
  }
}

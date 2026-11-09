import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, expand, map, reduce } from 'rxjs';
import { ApiService } from './api.service';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse } from '@syndew/oluso-ui-core';
import { ApiScopeDto, CreateApiScopeRequest } from '../models/api-scope.model';
import { ServiceApiCatalogDto } from '../models/service-api-scope.model';

@Injectable({
  providedIn: 'root',
})
export class ApiScopeService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/api-scopes';

  /**
   * Get all API scopes with pagination
   */
  getAll(pagination: PaginationParams): Observable<PaginatedResponse<ApiScopeDto>> {
    return this.api.getPaginated<ApiScopeDto>(this.endpoint, pagination);
  }

  /** Every API scope (for pickers): the list endpoint is paged, so this reads each page in turn. */
  getAllScopes(): Observable<ApiScopeDto[]> {
    const pageSize = 200;
    return this.getAll({ page: 1, pageSize }).pipe(
      expand((res) => (res.page < res.totalPages ? this.getAll({ page: res.page + 1, pageSize }) : EMPTY)),
      map((res) => res.items),
      reduce((all, items) => [...all, ...items], [] as ApiScopeDto[]),
    );
  }

  /**
   * Get a single API scope by ID
   */
  getById(id: number): Observable<ApiScopeDto> {
    return this.api.get<ApiScopeDto>(`${this.endpoint}/${id}`);
  }

  /**
   * Create a new API scope
   */
  create(request: CreateApiScopeRequest): Observable<ApiScopeDto> {
    return this.api.post<ApiScopeDto>(this.endpoint, request);
  }

  /**
   * Update an existing API scope
   */
  update(id: number, scope: ApiScopeDto): Observable<ApiScopeDto> {
    return this.api.put<ApiScopeDto>(`${this.endpoint}/${id}`, scope);
  }

  /**
   * Delete an API scope
   */
  delete(id: number): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  /**
   * Get the Service API permission catalog (machine-to-machine clients).
   * The scopes it lists are provisioned on demand when a client is saved.
   */
  getServiceApiCatalog(): Observable<ServiceApiCatalogDto> {
    return this.api.get<ServiceApiCatalogDto>('/service-api/scopes');
  }
}

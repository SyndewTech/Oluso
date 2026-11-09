import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  IdentityResourceDto,
  CreateIdentityResourceRequest,
} from '../models/identity-resource.model';

@Injectable({
  providedIn: 'root',
})
export class IdentityResourceService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/identity-resources';

  /**
   * Get all identity resources (standard OIDC scopes + custom).
   * The endpoint returns a flat list, not paginated.
   */
  getAll(): Observable<IdentityResourceDto[]> {
    return this.api.get<IdentityResourceDto[]>(this.endpoint);
  }

  /**
   * Get a single identity resource by ID
   */
  getById(id: number): Observable<IdentityResourceDto> {
    return this.api.get<IdentityResourceDto>(`${this.endpoint}/${id}`);
  }

  /**
   * Create a new identity resource
   */
  create(request: CreateIdentityResourceRequest): Observable<IdentityResourceDto> {
    return this.api.post<IdentityResourceDto>(this.endpoint, request);
  }

  /**
   * Update an existing identity resource
   */
  update(id: number, resource: IdentityResourceDto): Observable<IdentityResourceDto> {
    return this.api.put<IdentityResourceDto>(`${this.endpoint}/${id}`, resource);
  }

  /**
   * Delete an identity resource (standard OIDC scopes cannot be deleted)
   */
  delete(id: number): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }
}

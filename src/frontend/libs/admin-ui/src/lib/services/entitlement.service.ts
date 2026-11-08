import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  EntitlementDto,
  CreateEntitlementRequest,
  UpdateEntitlementRequest,
} from '../models/entitlement.model';

@Injectable({
  providedIn: 'root',
})
export class EntitlementService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/entitlements';

  getAll(): Observable<EntitlementDto[]> {
    return this.api.get<EntitlementDto[]>(this.endpoint);
  }

  getById(id: string): Observable<EntitlementDto> {
    return this.api.get<EntitlementDto>(`${this.endpoint}/${id}`);
  }

  create(request: CreateEntitlementRequest): Observable<EntitlementDto> {
    return this.api.post<EntitlementDto>(this.endpoint, request);
  }

  update(id: string, request: UpdateEntitlementRequest): Observable<EntitlementDto> {
    return this.api.put<EntitlementDto>(`${this.endpoint}/${id}`, request);
  }

  /** Deletes the entitlement and strips it from every role holding it. */
  delete(id: string): Observable<{ removedFromRoles: number }> {
    return this.api.delete<{ removedFromRoles: number }>(`${this.endpoint}/${id}`);
  }
}

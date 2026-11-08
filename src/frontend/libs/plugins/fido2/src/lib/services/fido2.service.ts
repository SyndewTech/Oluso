import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '@oluso/admin-ui';
import {
  Fido2Stats,
  Fido2UsersResponse,
  Fido2Credential,
} from '../models/fido2.model';

@Injectable({
  providedIn: 'root',
})
export class Fido2Service {
  private readonly api = inject(ApiService);
  private readonly basePath = '/fido2';

  /**
   * Get FIDO2 statistics
   */
  getStats(): Observable<Fido2Stats> {
    return this.api.get<Fido2Stats>(`${this.basePath}/stats`);
  }

  /**
   * Get users with FIDO2 credentials
   */
  getUsersWithCredentials(params?: {
    search?: string;
    page?: number;
    pageSize?: number;
  }): Observable<Fido2UsersResponse> {
    const queryParams: Record<string, string | number | boolean> = {};
    if (params?.search) {
      queryParams['search'] = params.search;
    }
    if (params?.page) {
      queryParams['page'] = params.page;
    }
    if (params?.pageSize) {
      queryParams['pageSize'] = params.pageSize;
    }
    return this.api.get<Fido2UsersResponse>(`${this.basePath}/users`, queryParams);
  }

  /**
   * Get credentials for a specific user
   */
  getUserCredentials(userId: string): Observable<Fido2Credential[]> {
    return this.api.get<Fido2Credential[]>(`${this.basePath}/users/${userId}/credentials`);
  }

  /**
   * Delete a user's credential
   */
  deleteUserCredential(userId: string, credentialId: string): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/users/${userId}/credentials/${credentialId}`);
  }
}

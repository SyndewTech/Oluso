import { Injectable, inject } from '@angular/core';
import { Observable, tap, catchError, of } from 'rxjs';
import { ApiService } from './api.service';
import { PermissionsResponse } from '../models/permission.model';
import { PermissionsService, MyPermissionsResponse } from '@syndew/oluso-ui-core';

@Injectable({
  providedIn: 'root',
})
export class PermissionService {
  private readonly api = inject(ApiService);
  private readonly permissionsService = inject(PermissionsService);

  private readonly endpoint = '/permissions';

  /**
   * Get all permissions grouped by category
   */
  getAll(): Observable<PermissionsResponse> {
    return this.api.get<PermissionsResponse>(this.endpoint);
  }

  /**
   * Get the current user's effective permissions.
   * Calls GET /permissions/my and updates the PermissionsService.
   *
   * When the API is unreachable, the current identity's cache is restored;
   * without one, `fallback` (if given) supplies permissions instead.
   */
  getMyPermissions(fallback?: () => void): Observable<MyPermissionsResponse> {
    this.permissionsService.setLoading(true);
    return this.api.get<MyPermissionsResponse>(`${this.endpoint}/my`).pipe(
      tap((response) => {
        this.permissionsService.setMyPermissions(response);
        this.permissionsService.setOffline(false); // API is reachable
        this.permissionsService.setLoading(false);
      }),
      catchError((error) => {
        // On failure, restore this user's cached permissions (if any)
        if (!this.permissionsService.loadFromCache()) {
          fallback?.();
        }
        this.permissionsService.setOffline(true); // Mark as offline
        this.permissionsService.setLoading(false);
        console.error('Failed to fetch permissions, using cache:', error);
        // Return empty response to not break the chain
        return of({
          effectivePermissions: [],
          rolePermissions: {},
          isSuperAdmin: false,
          explicitGrantPermissions: [],
        } as MyPermissionsResponse);
      })
    );
  }
}

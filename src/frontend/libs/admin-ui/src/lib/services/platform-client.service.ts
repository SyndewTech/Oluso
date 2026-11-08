import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { Client } from '../models/client.model';
import { ClientRequest } from '../models/client-request.model';
import { ClientConnection } from '../models/client-connection.model';

/**
 * Service for managing platform-level OAuth clients.
 * Platform clients are accessible across all tenants but invisible to tenant admins.
 * SuperAdmin access only.
 *
 * Uses the same Client/ClientRequest models as ClientService since the backend
 * now returns the same DTOs for platform clients.
 */
@Injectable({
  providedIn: 'root',
})
export class PlatformClientService {
  private readonly api = inject(ApiService);
  private readonly basePath = '/platform/clients';

  getAll(): Observable<Client[]> {
    return this.api.get<Client[]>(this.basePath);
  }

  getById(id: number): Observable<Client> {
    return this.api.get<Client>(`${this.basePath}/${id}`);
  }

  /**
   * Integration / connection details — see ClientService.getClientConnection
   * for the description; same shape, different endpoint scope.
   */
  getClientConnection(id: number): Observable<ClientConnection> {
    return this.api.get<ClientConnection>(`${this.basePath}/${id}/connection`);
  }

  create(client: ClientRequest): Observable<Client> {
    return this.api.post<Client>(this.basePath, client);
  }

  update(id: number, client: Partial<ClientRequest>): Observable<Client> {
    return this.api.put<Client>(`${this.basePath}/${id}`, client);
  }

  delete(id: number): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/${id}`);
  }

  regenerateSecret(id: number): Observable<{ clientId: string; clientSecret: string; secretId: number }> {
    return this.api.post<{ clientId: string; clientSecret: string; secretId: number }>(
      `${this.basePath}/${id}/regenerate-secret`,
      {}
    );
  }

  removeClientSecret(id: number, secretId: number): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/${id}/secrets/${secretId}`);
  }
}

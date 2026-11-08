import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { Client } from '../models/client.model';
import { ClientRequest } from '../models/client-request.model';
import { ClientConnection } from '../models/client-connection.model';

@Injectable({
  providedIn: 'root',
})
export class ClientService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/clients';

  /**
   * Get all clients (returns array, no server-side pagination)
   */
  getClients(): Observable<Client[]> {
    return this.api.get<Client[]>(this.endpoint);
  }

  /**
   * Get a single client by database ID
   */
  getClient(id: number): Observable<Client> {
    return this.api.get<Client>(`${this.endpoint}/${id}`);
  }

  /**
   * Get the integration / connection details for a client — everything an
   * integrator needs to copy into their app (issuer, OIDC endpoints, redirect
   * URIs, scopes). Issuer is resolved per-tenant on the server, so any
   * CustomDomain or TokenSettings.IssuerUri override is reflected here.
   */
  getClientConnection(id: number): Observable<ClientConnection> {
    return this.api.get<ClientConnection>(`${this.endpoint}/${id}/connection`);
  }

  /**
   * Create a new client
   */
  createClient(client: ClientRequest): Observable<Client> {
    return this.api.post<Client>(this.endpoint, client);
  }

  /**
   * Update an existing client by database ID
   */
  updateClient(id: number, client: Partial<ClientRequest>): Observable<Client> {
    return this.api.put<Client>(`${this.endpoint}/${id}`, client);
  }

  /**
   * Delete a client by database ID
   */
  deleteClient(id: number): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}`);
  }

  /**
   * Regenerate (add new) secret for a client
   * Returns the new secret value (only time it's visible)
   */
  regenerateSecret(id: number): Observable<{ clientId: string; clientSecret: string; secretId: number }> {
    return this.api.post<{ clientId: string; clientSecret: string; secretId: number }>(
      `${this.endpoint}/${id}/regenerate-secret`,
      {}
    );
  }

  /**
   * Remove a secret from a client
   */
  removeClientSecret(id: number, secretId: number): Observable<void> {
    return this.api.delete<void>(`${this.endpoint}/${id}/secrets/${secretId}`);
  }
}

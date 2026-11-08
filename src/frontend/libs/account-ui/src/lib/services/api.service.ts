import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class ApiService {
  private readonly http = inject(HttpClient);

  private _baseUrl = '/api/account';
  private _serverUrl = '';

  /**
   * Set the API base URL (e.g., 'http://localhost:5050/api/account')
   */
  setBaseUrl(url: string): void {
    this._baseUrl = url;
    try {
      const parsed = new URL(url, window.location.origin);
      this._serverUrl = parsed.origin;
    } catch {
      this._serverUrl = window.location.origin;
    }
  }

  /**
   * Set the server URL (e.g., 'http://localhost:5050')
   */
  setServerUrl(url: string): void {
    this._serverUrl = url;
  }

  get baseUrl(): string {
    return this._baseUrl;
  }

  get serverUrl(): string {
    return this._serverUrl || window.location.origin;
  }

  /**
   * Build an absolute URL that bypasses the /api/account base path.
   * Use for anonymous platform endpoints like /api/tenant/resolve.
   */
  absoluteUrl(path: string): string {
    return `${this.serverUrl}${path}`;
  }

  /**
   * GET an endpoint at the server root (e.g. /api/tenant/resolve) rather than
   * under /api/account. Calling {@link get} would double-prefix and 404.
   */
  getAbsolute<T>(path: string, params?: Record<string, string | number | boolean>): Observable<T> {
    const httpParams = this.buildParams(params);
    return this.http.get<T>(this.absoluteUrl(path), { params: httpParams });
  }

  /** POST counterpart of {@link getAbsolute} — bypasses /api/account base. */
  postAbsolute<T>(path: string, body: unknown): Observable<T> {
    return this.http.post<T>(this.absoluteUrl(path), body);
  }

  get<T>(endpoint: string, params?: Record<string, string | number | boolean>): Observable<T> {
    const httpParams = this.buildParams(params);
    return this.http.get<T>(`${this._baseUrl}${endpoint}`, { params: httpParams });
  }

  post<T>(endpoint: string, body: unknown): Observable<T> {
    return this.http.post<T>(`${this._baseUrl}${endpoint}`, body);
  }

  put<T>(endpoint: string, body: unknown): Observable<T> {
    return this.http.put<T>(`${this._baseUrl}${endpoint}`, body);
  }

  patch<T>(endpoint: string, body: unknown): Observable<T> {
    return this.http.patch<T>(`${this._baseUrl}${endpoint}`, body);
  }

  delete<T>(endpoint: string, params?: Record<string, string | number | boolean>): Observable<T> {
    const httpParams = this.buildParams(params);
    return this.http.delete<T>(`${this._baseUrl}${endpoint}`, { params: httpParams });
  }

  getBlob(endpoint: string, params?: HttpParams): Observable<Blob> {
    return this.http.get(`${this._baseUrl}${endpoint}`, { params, responseType: 'blob' });
  }

  private buildParams(params?: Record<string, string | number | boolean>): HttpParams {
    let httpParams = new HttpParams();

    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        if (value !== undefined && value !== null) {
          httpParams = httpParams.set(key, String(value));
        }
      });
    }

    return httpParams;
  }
}

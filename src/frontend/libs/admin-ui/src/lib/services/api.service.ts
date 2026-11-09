import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaginationParams } from '../models/pagination-params.model';
import { PaginatedResponse } from '@syndew/oluso-ui-core';
import { apiUrl, serverUrl } from '../config';

@Injectable({
  providedIn: 'root',
})
export class ApiService {
  private readonly http = inject(HttpClient);

  private _baseUrl = apiUrl;
  private _serverUrl = serverUrl;

  /**
   * Set the API base URL (e.g., 'http://localhost:5050/api/admin')
   */
  setBaseUrl(url: string): void {
    this._baseUrl = url;
    // Update origin when base URL changes
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

  /**
   * Get an absolute URL that bypasses the API baseURL.
   * Use this for endpoints that are NOT under /api/admin.
   * @param path The absolute path (e.g., '/api/tenant/features')
   */
  absoluteUrl(path: string): string {
    return `${this._serverUrl}${path}`;
  }

  /**
   * GET an endpoint that lives at the server root (e.g. /api/tenant/resolve)
   * rather than under /api/admin. Use this for anonymous platform endpoints
   * like tenant resolution that aren't part of the admin API surface — calling
   * the regular {@link get} would double-prefix and 404.
   */
  getAbsolute<T>(path: string, params?: Record<string, string | number | boolean>): Observable<T> {
    const httpParams = this.buildParams(params);
    return this.http.get<T>(this.absoluteUrl(path), { params: httpParams });
  }

  /** POST counterpart of {@link getAbsolute} — bypasses /api/admin baseUrl. */
  postAbsolute<T>(path: string, body: unknown): Observable<T> {
    return this.http.post<T>(this.absoluteUrl(path), body);
  }

  get<T>(endpoint: string, params?: Record<string, string | number | boolean>): Observable<T> {
    const httpParams = this.buildParams(params);
    return this.http.get<T>(`${this._baseUrl}${endpoint}`, { params: httpParams });
  }

  getPaginated<T>(
    endpoint: string,
    pagination: PaginationParams,
    filters?: Record<string, string | number | boolean>
  ): Observable<PaginatedResponse<T>> {
    const params = {
      ...filters,
      page: pagination.page ?? 1,
      pageSize: pagination.pageSize ?? 10,
      ...(pagination.sortBy && { sortBy: pagination.sortBy }),
      ...(pagination.sortOrder && { sortOrder: pagination.sortOrder }),
    };

    return this.get<PaginatedResponse<T>>(endpoint, params);
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

  getWithParams<T>(endpoint: string, params: HttpParams): Observable<T> {
    return this.http.get<T>(`${this._baseUrl}${endpoint}`, { params });
  }

  getBlob(endpoint: string, params: HttpParams): Observable<Blob> {
    return this.http.get(`${this._baseUrl}${endpoint}`, { params, responseType: 'blob' });
  }

  getBaseUrl(): string {
    return this._baseUrl;
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

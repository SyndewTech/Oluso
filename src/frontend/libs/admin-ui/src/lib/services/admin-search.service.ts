import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

export type AdminSearchBadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export interface AdminSearchBadge {
  label: string;
  value: string;
  tone: AdminSearchBadgeTone;
}

export interface AdminSearchResultItem {
  id: string;
  title: string;
  subtitle?: string;
  description?: string;
  /** Admin route; empty when the record lives in the workspace app. */
  route: string;
  /** Workspace route for records without an admin page. */
  workspaceRoute?: string;
  icon?: string;
  imageUrl?: string;
  badges: AdminSearchBadge[];
  score: number;
}

export interface AdminSearchGroup {
  providerId: string;
  /** Module feature key; 'platform' for core records. */
  module: string;
  category: string;
  displayName: string;
  icon: string;
  totalCount: number;
  seeAllRoute?: string;
  items: AdminSearchResultItem[];
}

export interface AdminSearchResponse {
  query: string;
  totalCount: number;
  groups: AdminSearchGroup[];
}

export type AdminSearchFilterType =
  | 'text' | 'select' | 'multiselect' | 'boolean' | 'date' | 'daterange' | 'number' | 'numberrange';

export interface AdminSearchFilterOption {
  value: string;
  label: string;
}

export interface AdminSearchFilterDefinition {
  key: string;
  label: string;
  type: AdminSearchFilterType;
  placeholder?: string;
  options?: AdminSearchFilterOption[];
}

/** A record kind the caller may search, with the filters it accepts. */
export interface AdminSearchProvider {
  providerId: string;
  module: string;
  category: string;
  displayName: string;
  icon: string;
  filters: AdminSearchFilterDefinition[];
}

export interface AdminSearchOptions {
  module?: string;
  providerId?: string;
  filters?: Record<string, string>;
  limit?: number;
}

export const PLATFORM_MODULE = 'platform';
export const MIN_SEARCH_LENGTH = 2;

@Injectable({ providedIn: 'root' })
export class AdminSearchService {
  private readonly api = inject(ApiService);

  /** Providers the current caller may search: tenant feature and read permission already applied by the server. */
  getProviders(): Observable<AdminSearchProvider[]> {
    return this.api.get<AdminSearchProvider[]>('/search/providers');
  }

  search(query: string, options: AdminSearchOptions = {}): Observable<AdminSearchResponse> {
    const params: Record<string, string | number> = { q: query };
    if (options.module) params['module'] = options.module;
    if (options.providerId) params['provider'] = options.providerId;
    if (options.limit) params['limit'] = options.limit;
    for (const [key, value] of Object.entries(options.filters ?? {})) {
      if (value !== '' && value != null) params[`filters[${key}]`] = value;
    }
    return this.api.get<AdminSearchResponse>('/search', params);
  }
}

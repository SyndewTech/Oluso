import { Injectable } from '@angular/core';

export interface SavedFilter {
  id: string;
  name: string;
  filters: Record<string, unknown>;
  createdAt: string;
}

const KEY_PREFIX = 'oluso:savedFilters:';
const MAX_FILTERS_PER_PAGE = 10;

/**
 * Stores named filter presets per list page in localStorage.
 *
 * Each list page gets its own bucket keyed by
 * `oluso:savedFilters:{pageKey}`. Users can save the current filter state under
 * a name, recall it later, rename it, or delete it. A maximum of
 * {@link MAX_FILTERS_PER_PAGE} filters is enforced per page — older ones are
 * dropped when the limit is exceeded.
 *
 * The service is pure data access; UI concerns live in
 * {@link SavedFiltersComponent}.
 */
@Injectable({ providedIn: 'root' })
export class SavedFiltersService {
  /** Returns all saved filters for the given page, newest first. */
  getFilters(pageKey: string): SavedFilter[] {
    try {
      const raw = localStorage.getItem(this.storageKey(pageKey));
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed.filter(this.isValidFilter).slice(0, MAX_FILTERS_PER_PAGE);
    } catch {
      return [];
    }
  }

  /**
   * Persists a new filter preset. If the page already has the maximum number
   * of filters, the oldest one is dropped to make room.
   */
  saveFilter(
    pageKey: string,
    name: string,
    filters: Record<string, unknown>,
  ): SavedFilter {
    const trimmed = name.trim();
    if (!trimmed) {
      throw new Error('Filter name is required.');
    }
    const entry: SavedFilter = {
      id: this.generateId(),
      name: trimmed,
      filters: { ...filters },
      createdAt: new Date().toISOString(),
    };
    const list = this.getFilters(pageKey);
    list.unshift(entry);
    this.persist(pageKey, list.slice(0, MAX_FILTERS_PER_PAGE));
    return entry;
  }

  /** Removes the filter with the given id. No-op if the id is unknown. */
  deleteFilter(pageKey: string, id: string): void {
    const list = this.getFilters(pageKey).filter((f) => f.id !== id);
    this.persist(pageKey, list);
  }

  /**
   * Replaces the name and filter payload of an existing preset. The original
   * id and createdAt timestamp are preserved. No-op if the id is unknown.
   */
  updateFilter(
    pageKey: string,
    id: string,
    name: string,
    filters: Record<string, unknown>,
  ): void {
    const trimmed = name.trim();
    if (!trimmed) {
      throw new Error('Filter name is required.');
    }
    const list = this.getFilters(pageKey).map((f) =>
      f.id === id ? { ...f, name: trimmed, filters: { ...filters } } : f,
    );
    this.persist(pageKey, list);
  }

  private storageKey(pageKey: string): string {
    return `${KEY_PREFIX}${pageKey}`;
  }

  private persist(pageKey: string, list: SavedFilter[]): void {
    try {
      localStorage.setItem(this.storageKey(pageKey), JSON.stringify(list));
    } catch {
      // Storage may be full or disabled (private mode) — fail silently.
    }
  }

  private generateId(): string {
    if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
      return crypto.randomUUID();
    }
    return `sf_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 10)}`;
  }

  private isValidFilter(value: unknown): value is SavedFilter {
    if (!value || typeof value !== 'object') return false;
    const f = value as Partial<SavedFilter>;
    return (
      typeof f.id === 'string' &&
      typeof f.name === 'string' &&
      typeof f.createdAt === 'string' &&
      !!f.filters &&
      typeof f.filters === 'object'
    );
  }
}

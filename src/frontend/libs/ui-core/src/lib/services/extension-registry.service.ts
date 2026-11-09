import { Injectable, Type, signal, computed } from '@angular/core';

/**
 * Field renderer definition for custom form fields
 */
export interface FieldRenderer {
  type: string;
  component: Type<unknown>;
  label?: string;
}

/**
 * Cell renderer definition for custom table cells
 */
export interface CellRenderer {
  type: string;
  component: Type<unknown>;
}

/**
 * Form schema field definition
 */
export interface SchemaField {
  name: string;
  type: string;
  label?: string;
  placeholder?: string;
  required?: boolean;
  disabled?: boolean;
  options?: { label: string; value: unknown }[];
  validation?: Record<string, unknown>;
  [key: string]: unknown;
}

/**
 * Extension Registry Service
 *
 * Central service for managing custom renderers and extensions.
 * This mirrors the ExtensionRegistry from the React ui-core package.
 */
@Injectable({
  providedIn: 'root',
})
export class ExtensionRegistryService {
  private readonly _fieldRenderers = signal<Map<string, FieldRenderer>>(new Map());
  private readonly _cellRenderers = signal<Map<string, CellRenderer>>(new Map());

  /** Read-only computed list of field renderers */
  readonly fieldRenderers = computed(() => Array.from(this._fieldRenderers().values()));

  /** Read-only computed list of cell renderers */
  readonly cellRenderers = computed(() => Array.from(this._cellRenderers().values()));

  /**
   * Register a custom field renderer
   */
  registerFieldRenderer(renderer: FieldRenderer): void {
    this._fieldRenderers.update((map) => {
      const newMap = new Map(map);
      newMap.set(renderer.type, renderer);
      return newMap;
    });
  }

  /**
   * Register multiple field renderers at once
   */
  registerFieldRenderers(renderers: FieldRenderer[]): void {
    renderers.forEach((renderer) => this.registerFieldRenderer(renderer));
  }

  /**
   * Get a field renderer by type
   */
  getFieldRenderer(type: string): FieldRenderer | undefined {
    return this._fieldRenderers().get(type);
  }

  /**
   * Check if a field renderer is registered
   */
  hasFieldRenderer(type: string): boolean {
    return this._fieldRenderers().has(type);
  }

  /**
   * Unregister a field renderer
   */
  unregisterFieldRenderer(type: string): void {
    this._fieldRenderers.update((map) => {
      const newMap = new Map(map);
      newMap.delete(type);
      return newMap;
    });
  }

  /**
   * Register a custom cell renderer
   */
  registerCellRenderer(renderer: CellRenderer): void {
    this._cellRenderers.update((map) => {
      const newMap = new Map(map);
      newMap.set(renderer.type, renderer);
      return newMap;
    });
  }

  /**
   * Register multiple cell renderers at once
   */
  registerCellRenderers(renderers: CellRenderer[]): void {
    renderers.forEach((renderer) => this.registerCellRenderer(renderer));
  }

  /**
   * Get a cell renderer by type
   */
  getCellRenderer(type: string): CellRenderer | undefined {
    return this._cellRenderers().get(type);
  }

  /**
   * Check if a cell renderer is registered
   */
  hasCellRenderer(type: string): boolean {
    return this._cellRenderers().has(type);
  }

  /**
   * Unregister a cell renderer
   */
  unregisterCellRenderer(type: string): void {
    this._cellRenderers.update((map) => {
      const newMap = new Map(map);
      newMap.delete(type);
      return newMap;
    });
  }

  /**
   * Clear all registered renderers
   */
  clearAll(): void {
    this._fieldRenderers.set(new Map());
    this._cellRenderers.set(new Map());
  }
}

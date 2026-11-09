import { Type } from '@angular/core';

export interface TabDefinition {
  id: string;
  label: string;
  icon?: string;
  component: Type<unknown>;
  order?: number;
  permission?: string;
  feature?: string;
}

/**
 * Context passed to dynamic tab components
 */
export interface TabContext {
  /** Entity ID (e.g., tenant ID, user ID) */
  entityId: string;
  /** Entity type (e.g., 'tenant', 'user') */
  entityType: string;
  /** Additional data specific to the entity */
  data?: Record<string, unknown>;
}

import type { ComponentType } from 'react';
import type { FieldError } from 'react-hook-form';

/**
 * Field renderer props passed to custom field components
 */
export interface FieldRendererProps<T = unknown> {
  /** Field name */
  name: string;
  /** Field label */
  label?: string;
  /** Current value */
  value: T;
  /** Change handler */
  onChange: (value: T) => void;
  /** Blur handler */
  onBlur?: () => void;
  /** Field is disabled */
  disabled?: boolean;
  /** Field is read-only */
  readOnly?: boolean;
  /** Field is required */
  required?: boolean;
  /** Placeholder text */
  placeholder?: string;
  /** Help text */
  helpText?: string;
  /** Validation error */
  error?: FieldError;
  /** Additional field-specific options */
  options?: Record<string, unknown>;
  /** CSS class name */
  className?: string;
}

/**
 * Field renderer registration
 */
export interface FieldRenderer {
  /** Field type identifier */
  type: string;
  /** Display name for field type picker */
  displayName: string;
  /** Description */
  description?: string;
  /** Icon for field type */
  icon?: ComponentType<{ className?: string }>;
  /** The field component */
  component: ComponentType<FieldRendererProps>;
  /** Default options for this field type */
  defaultOptions?: Record<string, unknown>;
  /** Schema for options (JSON Schema) */
  optionsSchema?: Record<string, unknown>;
}

/**
 * Cell renderer props for table columns
 */
export interface CellRendererProps<T = unknown> {
  /** Cell value */
  value: T;
  /** Full row data */
  row: Record<string, unknown>;
  /** Column ID */
  columnId: string;
  /** Row index */
  rowIndex: number;
}

/**
 * Cell renderer registration
 */
export interface CellRenderer {
  /** Cell type identifier */
  type: string;
  /** Display name */
  displayName: string;
  /** The cell component */
  component: ComponentType<CellRendererProps>;
}

/**
 * Action button for tables/pages
 */
export interface ActionButton {
  id: string;
  label: string;
  icon?: ComponentType<{ className?: string }>;
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost';
  /** Handler receives selected items for bulk actions */
  onClick: (selectedItems?: unknown[]) => void | Promise<void>;
  /** Show only when items are selected */
  requiresSelection?: boolean;
  /** Minimum items required */
  minSelection?: number;
  /** Feature flag */
  feature?: string;
  /** Confirmation message */
  confirm?: string | { title: string; message: string };
}

/**
 * Detail panel section for entity detail pages
 */
export interface DetailSection {
  id: string;
  title: string;
  /** Entity types this section applies to */
  entityTypes: string[];
  /** Component to render */
  component: ComponentType<DetailSectionProps>;
  /** Sort order */
  order?: number;
  /** Feature flag */
  feature?: string;
  /** Collapsible */
  collapsible?: boolean;
  /** Default collapsed state */
  defaultCollapsed?: boolean;
}

export interface DetailSectionProps {
  /** Entity type */
  entityType: string;
  /** Entity ID */
  entityId: string;
  /** Entity data */
  data: Record<string, unknown>;
  /** Refresh entity data */
  onRefresh?: () => void;
}

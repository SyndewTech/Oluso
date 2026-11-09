/**
 * Represents a column in the Kanban board
 */
export interface KanbanColumn<T = unknown> {
  /** Unique identifier for the column */
  id: string;
  /** Display name for the column header */
  name: string;
  /** Optional color for the column header (tailwind color name or hex) */
  color?: string;
  /** Optional icon name */
  icon?: string;
  /** Items in this column */
  items: T[];
  /** Optional metadata */
  metadata?: Record<string, unknown>;
}

/**
 * Event emitted when an item is moved between columns
 */
export interface KanbanItemMoveEvent<T = unknown> {
  /** The item that was moved */
  item: T;
  /** Previous column ID */
  fromColumnId: string;
  /** New column ID */
  toColumnId: string;
  /** Previous index in the column */
  fromIndex: number;
  /** New index in the column */
  toIndex: number;
}

/**
 * Configuration for the Kanban board
 */
export interface KanbanConfig {
  /** Whether to allow dragging items between columns */
  allowDrag?: boolean;
  /** Per-item override when dragging is allowed: return false to pin an item in place. */
  canDrag?(item: unknown): boolean;
  /** Whether to show column item counts */
  showCounts?: boolean;
  /** Whether to show column totals (requires totalField or columnTotal) */
  showTotals?: boolean;
  /** Numeric field on items to sum for column totals */
  totalField?: string;
  /**
   * The text of a column's total, in place of summing totalField. Money needs this: items can be in
   * different currencies and amounts in different minor units, which a plain sum can't respect.
   */
  columnTotal?(column: KanbanColumn<unknown>): string;
  /** Empty state message when a column has no items */
  emptyMessage?: string;
  /** Maximum items to display per column before showing "load more" */
  maxItemsPerColumn?: number;
}

/**
 * Summary data for a column
 */
export interface KanbanColumnSummary {
  id: string;
  name: string;
  count: number;
  total?: number;
}

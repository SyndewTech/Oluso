import {
  Component,
  input,
  output,
  computed,
  signal,
  contentChild,
  TemplateRef,
  effect,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  CdkDragDrop,
  CdkDrag,
  CdkDragPlaceholder,
  CdkDropList,
  CdkDropListGroup,
  moveItemInArray,
  transferArrayItem,
} from '@angular/cdk/drag-drop';
import { IconComponent } from '../icon/icon.component';
import { LocaleService } from '../../services/locale.service';
import type { KanbanColumn, KanbanItemMoveEvent, KanbanConfig } from './kanban.types';

const DEFAULT_CONFIG: KanbanConfig = {
  allowDrag: true,
  showCounts: true,
  showTotals: false,
  emptyMessage: 'No items',
};

const COLUMN_COLORS: Record<string, { bg: string; border: string; text: string }> = {
  gray: { bg: 'bg-gray-100', border: 'border-gray-300', text: 'text-gray-700' },
  blue: { bg: 'bg-blue-100', border: 'border-blue-300', text: 'text-blue-700' },
  green: { bg: 'bg-green-100', border: 'border-green-300', text: 'text-green-700' },
  yellow: { bg: 'bg-yellow-100', border: 'border-yellow-300', text: 'text-yellow-700' },
  red: { bg: 'bg-red-100', border: 'border-red-300', text: 'text-red-700' },
  purple: { bg: 'bg-purple-100', border: 'border-purple-300', text: 'text-purple-700' },
  pink: { bg: 'bg-pink-100', border: 'border-pink-300', text: 'text-pink-700' },
  indigo: { bg: 'bg-indigo-100', border: 'border-indigo-300', text: 'text-indigo-700' },
  teal: { bg: 'bg-teal-100', border: 'border-teal-300', text: 'text-teal-700' },
  orange: { bg: 'bg-orange-100', border: 'border-orange-300', text: 'text-orange-700' },
  cyan: { bg: 'bg-cyan-100', border: 'border-cyan-300', text: 'text-cyan-700' },
};

@Component({
  selector: 'ui-kanban',
  standalone: true,
  imports: [CommonModule, IconComponent, CdkDropListGroup, CdkDropList, CdkDrag, CdkDragPlaceholder],
  template: `
    <div class="kanban-board flex flex-nowrap gap-4 overflow-x-auto pb-4 min-h-[500px]" cdkDropListGroup>
      @for (column of internalColumns(); track column.id) {
        <div class="kanban-column flex flex-col w-[300px] min-w-[280px] flex-shrink-0 bg-gray-50 rounded-lg">
          <!-- Column Header -->
          <div
            class="kanban-column-header px-4 py-3 rounded-t-lg border-b-2"
            [class]="getColumnHeaderClasses(column)"
          >
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                @if (column.icon) {
                  <ui-icon [name]="column.icon" class="h-4 w-4" />
                }
                <h3 class="font-semibold text-sm">{{ column.name }}</h3>
              </div>
              @if (mergedConfig().showCounts) {
                <span class="text-xs font-medium px-2 py-0.5 rounded-full bg-white/50">
                  {{ column.items.length }}
                </span>
              }
            </div>
            @if (mergedConfig().showTotals && (mergedConfig().columnTotal || mergedConfig().totalField)) {
              <div class="mt-1 text-xs opacity-75">
                {{ columnTotalText(column) }}
              </div>
            }
          </div>

          <!-- Column Body (Droppable) -->
          <div
            class="kanban-column-body flex-1 p-2 space-y-2 overflow-y-auto max-h-[calc(100vh-300px)]"
            cdkDropList
            [cdkDropListData]="column.items"
            [id]="'column-' + column.id"
            (cdkDropListDropped)="onDrop($event, column.id)"
            [cdkDropListDisabled]="!mergedConfig().allowDrag"
          >
            @if (column.items.length === 0) {
              <div class="text-center py-8 text-gray-400 text-sm">
                {{ mergedConfig().emptyMessage }}
              </div>
            } @else {
              @for (item of column.items; track trackByFn(item); let i = $index) {
                <div
                  class="kanban-item bg-white rounded-lg shadow-sm border border-gray-200 p-3 cursor-grab hover:shadow-md transition-shadow"
                  [class.cursor-not-allowed]="!isDraggable(item)"
                  cdkDrag
                  [cdkDragDisabled]="!isDraggable(item)"
                  [cdkDragData]="item"
                >
                  <!-- Drag placeholder -->
                  <div class="kanban-item-placeholder bg-gray-200 rounded-lg border-2 border-dashed border-gray-300" *cdkDragPlaceholder></div>

                  <!-- Item content via template or default -->
                  @if (itemTemplate()) {
                    <ng-container
                      [ngTemplateOutlet]="itemTemplate()!"
                      [ngTemplateOutletContext]="{ $implicit: item, column: column, index: i }"
                    ></ng-container>
                  } @else {
                    <div class="text-sm text-gray-700">
                      {{ getItemLabel(item) }}
                    </div>
                  }
                </div>
              }
            }
          </div>

          <!-- Column Footer (optional) -->
          @if (showColumnFooter()) {
            <div class="kanban-column-footer px-4 py-2 border-t border-gray-200">
              <ng-content select="[kanban-column-footer]"></ng-content>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .kanban-item-placeholder {
      min-height: 60px;
    }

    .cdk-drag-preview {
      box-shadow: 0 10px 25px rgba(0, 0, 0, 0.15);
      border-radius: 0.5rem;
      background: white;
    }

    .cdk-drag-animating {
      transition: transform 250ms cubic-bezier(0, 0, 0.2, 1);
    }

    .cdk-drop-list-dragging .kanban-item:not(.cdk-drag-placeholder) {
      transition: transform 250ms cubic-bezier(0, 0, 0.2, 1);
    }
  `],
})
export class KanbanComponent<T = unknown> {
  private readonly locale = inject(LocaleService);

  /** Columns to display */
  readonly columns = input.required<KanbanColumn<T>[]>();

  /** Configuration options */
  readonly config = input<Partial<KanbanConfig>>({});

  /** Field to use as item identifier for tracking */
  readonly itemIdField = input<string>('id');

  /** Field to use as item label (used in default template) */
  readonly itemLabelField = input<string>('name');

  /** Event emitted when an item is moved */
  readonly itemMoved = output<KanbanItemMoveEvent<T>>();

  /** Event emitted when a column is clicked */
  readonly columnClicked = output<KanbanColumn<T>>();

  /** Event emitted when an item is clicked */
  readonly itemClicked = output<{ item: T; column: KanbanColumn<T> }>();

  /** Template for rendering items */
  readonly itemTemplate = contentChild<TemplateRef<{ $implicit: T; column: KanbanColumn<T>; index: number }>>('kanbanItem');

  /** Whether to show column footer */
  readonly showColumnFooter = input<boolean>(false);

  /** Internal mutable copy of columns for drag-and-drop */
  protected readonly internalColumns = signal<KanbanColumn<T>[]>([]);

  /** Merged configuration with defaults */
  protected readonly mergedConfig = computed(() => ({
    ...DEFAULT_CONFIG,
    ...this.config(),
  }));

  constructor() {
    // Sync input columns to internal state
    effect(() => {
      const cols = this.columns();
      this.internalColumns.set(cols.map(col => ({ ...col, items: [...col.items] })));
    });
  }

  /** Get classes for column header based on color */
  getColumnHeaderClasses(column: KanbanColumn<T>): string {
    const color = column.color || 'gray';
    const colorConfig = COLUMN_COLORS[color] || COLUMN_COLORS['gray'];
    return `${colorConfig.bg} ${colorConfig.border} ${colorConfig.text}`;
  }

  /** Calculate total for a column */
  getColumnTotal(column: KanbanColumn<T>): number {
    const field = this.mergedConfig().totalField;
    if (!field) return 0;

    return column.items.reduce((sum, item) => {
      const value = (item as Record<string, unknown>)[field];
      return sum + (typeof value === 'number' ? value : 0);
    }, 0);
  }

  /** A column's total: the configured text, or the sum of totalField as a number */
  columnTotalText(column: KanbanColumn<T>): string {
    const config = this.mergedConfig();
    if (config.columnTotal) return config.columnTotal(column);
    return new Intl.NumberFormat(this.locale.locale()).format(this.getColumnTotal(column));
  }

  /** Get label for an item */
  getItemLabel(item: T): string {
    const field = this.itemLabelField();
    const value = (item as Record<string, unknown>)[field];
    return typeof value === 'string' ? value : String(value ?? '');
  }

  /** Track function for items */
  trackByFn(item: T): string {
    const field = this.itemIdField();
    const id = (item as Record<string, unknown>)[field];
    return typeof id === 'string' ? id : String(id ?? '');
  }

  /** Handle drop event */
  /** Whether an item can be picked up: dragging is on and the item isn't pinned. */
  isDraggable(item: T): boolean {
    const config = this.mergedConfig();
    return !!config.allowDrag && (config.canDrag ? config.canDrag(item) : true);
  }

  onDrop(event: CdkDragDrop<T[]>, targetColumnId: string): void {
    const sourceColumnId = event.previousContainer.id.replace('column-', '');

    if (event.previousContainer === event.container) {
      // Reorder within same column
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
    } else {
      // Move between columns
      transferArrayItem(
        event.previousContainer.data,
        event.container.data,
        event.previousIndex,
        event.currentIndex
      );
    }

    // Update internal state
    const updatedColumns = this.internalColumns().map(col => {
      if (col.id === sourceColumnId || col.id === targetColumnId) {
        return { ...col };
      }
      return col;
    });
    this.internalColumns.set(updatedColumns);

    // Emit move event
    this.itemMoved.emit({
      item: event.item.data,
      fromColumnId: sourceColumnId,
      toColumnId: targetColumnId,
      fromIndex: event.previousIndex,
      toIndex: event.currentIndex,
    });
  }
}

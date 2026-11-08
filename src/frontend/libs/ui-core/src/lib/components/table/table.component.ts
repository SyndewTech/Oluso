import { Component, input, output, TemplateRef, ContentChild } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface TableColumn<T> {
  key: string;
  header: string;
  render?: (item: T) => string;
}

@Component({
  selector: 'ui-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './table.component.html',
})
export class TableComponent<T = unknown> {
  readonly columns = input.required<TableColumn<T>[]>();
  readonly data = input<T[]>([]);
  readonly loading = input<boolean>(false);
  readonly emptyMessage = input<string>('No data available');
  readonly clickable = input<boolean>(true);

  readonly rowClick = output<T>();

  @ContentChild('cellTemplate') cellTemplate?: TemplateRef<{ $implicit: T; column: TableColumn<T> }>;

  getColumnValue(item: T, column: TableColumn<T>): string {
    if (column.render) {
      return column.render(item);
    }
    const value = (item as Record<string, unknown>)[column.key];
    return value !== undefined && value !== null ? String(value) : '';
  }

  onRowClick(item: T): void {
    if (this.clickable()) {
      this.rowClick.emit(item);
    }
  }

  trackByIndex(index: number): number {
    return index;
  }
}

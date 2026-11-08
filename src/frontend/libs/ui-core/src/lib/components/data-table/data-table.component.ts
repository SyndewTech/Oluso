import { Component, effect, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';
import { PaginationComponent } from '../pagination/pagination.component';

@Component({
  selector: 'ui-data-table',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, PaginationComponent],
  templateUrl: './data-table.component.html',
})
export class DataTableComponent {
  // Data state
  readonly loading = input<boolean>(false);
  readonly empty = input<boolean>(false);
  readonly emptyMessage = input<string>('No data available');
  readonly emptyIcon = input<string>('table');

  // Search
  readonly searchable = input<boolean>(false);
  readonly searchPlaceholder = input<string>('Search...');
  /** Term the table starts with, e.g. from a search query parameter. */
  readonly initialSearch = input<string>('');

  // Pagination
  readonly paginated = input<boolean>(false);
  readonly page = input<number>(1);
  readonly pageSize = input<number>(25);
  readonly totalCount = input<number>(0);
  readonly totalPages = input<number>(0);
  readonly pageSizeOptions = input<number[]>([10, 25, 50, 100]);
  readonly showPageSize = input<boolean>(true);

  // Events
  readonly searchChange = output<string>();
  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  // Internal search state
  readonly searchTerm = signal('');

  constructor() {
    effect(() => {
      const initial = this.initialSearch();
      if (initial) this.searchTerm.set(initial);
    });
  }

  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchTerm.set(value);
  }

  onSearchSubmit(): void {
    this.searchChange.emit(this.searchTerm());
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      this.onSearchSubmit();
    }
  }

  onClearSearch(): void {
    this.searchTerm.set('');
    this.searchChange.emit('');
  }
}

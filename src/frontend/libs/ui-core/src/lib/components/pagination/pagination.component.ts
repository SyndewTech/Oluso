import { Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'ui-pagination',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 px-1 py-3 text-[13px] text-gray-500">
      <div class="flex items-center gap-4">
        @if (totalCount() > 0) {
          <span class="tabular-nums whitespace-nowrap">
            <span class="text-gray-700 font-medium">{{ startItem() }}&#8209;{{ endItem() }}</span>
            <span class="text-gray-400 mx-1.5">of</span>
            <span class="text-gray-700 font-medium">{{ totalCount() }}</span>
          </span>
        } @else {
          <span class="text-gray-400">No results</span>
        }
        @if (showPageSize() && pageSizeOptions().length > 0) {
          <select
            class="text-[13px] border border-gray-200 bg-white text-gray-700 focus:border-gray-400 focus:ring-0 focus:outline-none px-2 py-1 rounded-md cursor-pointer hover:border-gray-300 transition-colors"
            [ngModel]="pageSize()"
            (ngModelChange)="onPageSizeChange($event)"
            [attr.aria-label]="'Items per page'"
          >
            @for (size of pageSizeOptions(); track size) {
              <option [ngValue]="size">{{ size }} / page</option>
            }
          </select>
        }
      </div>

      @if (totalPages() > 1) {
        <nav class="flex items-center gap-1" aria-label="Pagination">
          @if (page() > 1) {
            <button
              type="button"
              (click)="goPrev()"
              aria-label="Previous page"
              class="inline-flex items-center justify-center h-7 w-7 rounded-md text-gray-500 hover:text-gray-900 hover:bg-gray-100 transition-colors"
            >
              <ui-icon name="chevron-left" class="h-4 w-4" />
            </button>
          }
          @for (token of pageWindow(); track $index) {
            @if (token === -1) {
              <span class="inline-flex items-center justify-center h-7 w-7 text-gray-300">…</span>
            } @else {
              <button
                type="button"
                (click)="goPage(token)"
                [attr.aria-current]="token === page() ? 'page' : null"
                [class]="token === page()
                  ? 'inline-flex items-center justify-center h-7 min-w-7 px-2 rounded-md text-[13px] font-medium bg-gray-900 text-white tabular-nums'
                  : 'inline-flex items-center justify-center h-7 min-w-7 px-2 rounded-md text-[13px] text-gray-600 hover:text-gray-900 hover:bg-gray-100 transition-colors tabular-nums'"
              >
                {{ token }}
              </button>
            }
          }
          @if (page() < totalPages()) {
            <button
              type="button"
              (click)="goNext()"
              aria-label="Next page"
              class="inline-flex items-center justify-center h-7 w-7 rounded-md text-gray-500 hover:text-gray-900 hover:bg-gray-100 transition-colors"
            >
              <ui-icon name="chevron-right" class="h-4 w-4" />
            </button>
          }
        </nav>
      }
    </div>
  `,
})
export class PaginationComponent {
  readonly page = input<number>(1);
  readonly pageSize = input<number>(25);
  readonly totalCount = input<number>(0);
  readonly totalPages = input<number>(0);
  readonly pageSizeOptions = input<number[]>([10, 25, 50, 100]);
  readonly showPageSize = input<boolean>(true);

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  readonly startItem = computed(() => (this.page() - 1) * this.pageSize() + 1);
  readonly endItem = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()),
  );

  readonly pageWindow = computed(() => {
    const total = this.totalPages();
    const current = this.page();
    if (total <= 1) return [];
    if (total <= 7) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }
    const tokens: number[] = [1];
    const start = Math.max(2, current - 1);
    const end = Math.min(total - 1, current + 1);
    if (start > 2) tokens.push(-1);
    for (let i = start; i <= end; i++) tokens.push(i);
    if (end < total - 1) tokens.push(-1);
    tokens.push(total);
    return tokens;
  });

  goPage(page: number): void {
    if (page === this.page() || page < 1 || page > this.totalPages()) return;
    this.pageChange.emit(page);
  }

  goPrev(): void {
    if (this.page() > 1) this.pageChange.emit(this.page() - 1);
  }

  goNext(): void {
    if (this.page() < this.totalPages()) this.pageChange.emit(this.page() + 1);
  }

  onPageSizeChange(size: number): void {
    if (size !== this.pageSize()) {
      this.pageSizeChange.emit(size);
    }
  }
}

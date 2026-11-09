import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent } from '@syndew/oluso-ui-core';
import { FeedbackAdminService } from '../../services/feedback-admin.service';
import {
  FeedbackDto,
  FeedbackFilter,
  FEEDBACK_CATEGORIES,
  FEEDBACK_STATUSES,
} from '../../models/feedback.model';

@Component({
  selector: 'admin-feedback-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent],
  templateUrl: './feedback-page.component.html',
})
export class FeedbackPageComponent {
  private readonly feedbackService = inject(FeedbackAdminService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly items = signal<FeedbackDto[]>([]);
  readonly totalCount = signal(0);
  readonly totalPages = signal(0);

  readonly categories = FEEDBACK_CATEGORIES;
  readonly statuses = FEEDBACK_STATUSES;

  filter: FeedbackFilter = {
    pageNumber: 1,
    pageSize: 25,
  };

  constructor() {
    this.loadFeedback();
  }

  loadFeedback(): void {
    this.loading.set(true);
    this.feedbackService.getAll(this.filter).subscribe({
      next: (response) => {
        this.items.set(response.items);
        this.totalCount.set(response.totalCount);
        this.totalPages.set(response.totalPages);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load feedback:', error);
        this.loading.set(false);
      },
    });
  }

  onRowClick(item: FeedbackDto): void {
    this.router.navigate(['/feedback', item.id]);
  }

  onCategoryChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.filter = { ...this.filter, category: value || undefined, pageNumber: 1 };
    this.loadFeedback();
  }

  onStatusChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.filter = { ...this.filter, status: value || undefined, pageNumber: 1 };
    this.loadFeedback();
  }

  clearFilters(): void {
    this.filter = { pageNumber: 1, pageSize: 25 };
    this.loadFeedback();
  }

  onRefresh(): void {
    this.loadFeedback();
  }

  onPreviousPage(): void {
    if (this.filter.pageNumber > 1) {
      this.filter = { ...this.filter, pageNumber: this.filter.pageNumber - 1 };
      this.loadFeedback();
    }
  }

  onNextPage(): void {
    if (this.filter.pageNumber < this.totalPages()) {
      this.filter = { ...this.filter, pageNumber: this.filter.pageNumber + 1 };
      this.loadFeedback();
    }
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  getStartIndex(): number {
    return (this.filter.pageNumber - 1) * this.filter.pageSize + 1;
  }

  getEndIndex(): number {
    return Math.min(this.filter.pageNumber * this.filter.pageSize, this.totalCount());
  }

  getCategoryVariant(category: string): 'danger' | 'info' | 'warning' {
    switch (category) {
      case 'bug':
        return 'danger';
      case 'feature':
        return 'info';
      default:
        return 'warning';
    }
  }

  getStatusVariant(status: string): 'warning' | 'info' | 'success' | 'danger' {
    switch (status) {
      case 'new':
        return 'warning';
      case 'reviewed':
        return 'info';
      case 'resolved':
        return 'success';
      case 'dismissed':
        return 'danger';
      default:
        return 'warning';
    }
  }

  getCategoryLabel(category: string): string {
    switch (category) {
      case 'bug':
        return 'Bug';
      case 'feature':
        return 'Feature';
      case 'note':
        return 'Note';
      default:
        return category;
    }
  }

  getPortalLabel(portal: string): string {
    return portal === 'admin' ? 'Admin' : 'Workspace';
  }
}

import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  IconComponent,
  ModalComponent,
  AuthFilePipe,
} from '@syndew/oluso-ui-core';
import { FeedbackAdminService } from '../../services/feedback-admin.service';
import { FeedbackDto, FEEDBACK_STATUSES } from '../../models/feedback.model';

@Component({
  selector: 'admin-feedback-detail-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent, ModalComponent, AuthFilePipe],
  templateUrl: './feedback-detail-page.component.html',
})
export class FeedbackDetailPageComponent implements OnInit {
  private readonly feedbackService = inject(FeedbackAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly updating = signal(false);
  readonly feedback = signal<FeedbackDto | null>(null);
  readonly showStatusModal = signal(false);
  readonly showScreenshotModal = signal(false);
  readonly selectedStatus = signal('');
  readonly statuses = FEEDBACK_STATUSES;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadFeedback(id);
    }
  }

  loadFeedback(id: string): void {
    this.loading.set(true);
    this.feedbackService.getById(id).subscribe({
      next: (feedback) => {
        this.feedback.set(feedback);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load feedback:', error);
        this.loading.set(false);
      },
    });
  }

  goBack(): void {
    this.router.navigate(['/feedback']);
  }

  openStatusModal(): void {
    this.selectedStatus.set(this.feedback()?.status || 'new');
    this.showStatusModal.set(true);
  }

  confirmStatusUpdate(): void {
    const fb = this.feedback();
    if (!fb) return;

    this.updating.set(true);
    this.feedbackService.updateStatus(fb.id, this.selectedStatus()).subscribe({
      next: () => {
        this.feedback.set({ ...fb, status: this.selectedStatus() });
        this.showStatusModal.set(false);
        this.updating.set(false);
      },
      error: (error) => {
        console.error('Failed to update status:', error);
        this.updating.set(false);
      },
    });
  }

  selectStatus(status: string): void {
    this.selectedStatus.set(status);
  }

  getScreenshotUrl(): string {
    const fb = this.feedback();
    if (!fb) return '';
    return this.feedbackService.getScreenshotUrl(fb.id);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
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
        return 'Bug Report';
      case 'feature':
        return 'Feature Request';
      case 'note':
        return 'Note';
      default:
        return category;
    }
  }

  getPortalLabel(portal: string): string {
    return portal === 'admin' ? 'Admin Portal' : 'Workspace Portal';
  }
}

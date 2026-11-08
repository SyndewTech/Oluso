import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LocaleDatePipe, PaginationComponent, ToastService, extractErrorMessage } from '@oluso/ui-core';
import { VerificationService } from '../../services/verification.service';
import {
  BusinessReviewQueueItem,
  BusinessVerificationStatus,
  REASON_LABELS,
  RELATIONSHIP_LABELS,
  STATUS_CLASSES,
  STATUS_LABELS,
} from '../../models/verification.model';

@Component({
  selector: 'verification-review-queue-page',
  standalone: true,
  imports: [CommonModule, RouterLink, LocaleDatePipe, PaginationComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-semibold text-gray-900">Verification review queue</h1>
        <p class="text-sm text-gray-500">People waiting on a decision to act for a business: documents to check, a provider that couldn't answer, or a hosted check still open. Oldest first.</p>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center py-12">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else if (items().length === 0) {
        <p class="text-sm text-gray-500 py-12 text-center">Nobody is waiting on a decision.</p>
      } @else {
        <div class="overflow-x-auto rounded-lg border border-gray-200">
          <table class="min-w-full divide-y divide-gray-200 text-sm">
            <thead class="bg-gray-50 text-xs uppercase text-gray-500">
              <tr>
                <th class="px-4 py-2 text-left">Waiting since</th>
                <th class="px-4 py-2 text-left">Business</th>
                <th class="px-4 py-2 text-left">Applicant</th>
                <th class="px-4 py-2 text-left">Grounds</th>
                <th class="px-4 py-2 text-left">Documents</th>
                <th class="px-4 py-2 text-left">Status</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-100 bg-white">
              @for (item of items(); track item.applicant.id) {
                <tr>
                  <td class="px-4 py-2 text-gray-600">{{ item.applicant.updatedAt | localeDate: 'medium' }}</td>
                  <td class="px-4 py-2">
                    <a class="font-medium text-primary-700 hover:underline" [routerLink]="['/verification/businesses', item.business.id]">{{ item.business.registeredName || item.business.registrationNumber }}</a>
                    <div class="text-xs text-gray-500 font-mono">{{ item.business.country }} · {{ item.business.registrationNumber }}</div>
                  </td>
                  <td class="px-4 py-2">{{ item.applicant.subjectName || (item.applicant.subjectId ? item.applicant.subjectId : 'Before sign-up') }}</td>
                  <td class="px-4 py-2">{{ relationship(item.applicant.relationship) }}</td>
                  <td class="px-4 py-2">{{ item.applicant.documents.length }}</td>
                  <td class="px-4 py-2">
                    <span class="px-2 py-0.5 rounded-full text-xs font-medium" [ngClass]="badge(item.applicant.status)">{{ statusLabel(item.applicant.status) }}</span>
                    @if (item.applicant.statusReason) { <span class="ml-1 text-xs text-gray-500">{{ reason(item.applicant.statusReason) }}</span> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <ui-pagination [page]="page()" [pageSize]="pageSize()" [totalCount]="total()" [totalPages]="totalPages()"
                       (pageChange)="goTo($event)" (pageSizeChange)="resize($event)" />
      }
    </div>
  `,
})
export class ReviewQueuePageComponent implements OnInit {
  private readonly service = inject(VerificationService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly items = signal<BusinessReviewQueueItem[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  ngOnInit(): void {
    this.load();
  }

  statusLabel(status: BusinessVerificationStatus): string {
    return STATUS_LABELS[status] ?? status;
  }

  badge(status: BusinessVerificationStatus): string {
    return STATUS_CLASSES[status] ?? 'bg-gray-100 text-gray-700';
  }

  reason(reason: string): string {
    return REASON_LABELS[reason] ?? reason;
  }

  relationship(relationship: string): string {
    return RELATIONSHIP_LABELS[relationship] ?? relationship;
  }

  goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  resize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.service.reviewQueue(this.page(), this.pageSize()).subscribe({
      next: (queue) => {
        this.items.set(queue.items);
        this.total.set(queue.totalCount);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.error(extractErrorMessage(err, 'Failed to load the review queue'));
      },
    });
  }
}

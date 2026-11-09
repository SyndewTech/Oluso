import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LocaleDatePipe, PaginationComponent, ToastService, extractErrorMessage } from '@syndew/oluso-ui-core';
import { VerificationService } from '../../services/verification.service';
import {
  BusinessVerificationStatus,
  REASON_LABELS,
  STATUS_CLASSES,
  STATUS_LABELS,
  VerifiedBusinessSummary,
} from '../../models/verification.model';

@Component({
  selector: 'verification-businesses-page',
  standalone: true,
  imports: [CommonModule, FormsModule, LocaleDatePipe, PaginationComponent],
  template: `
    <div class="space-y-6">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">Verified businesses</h1>
          <p class="text-sm text-gray-500">Businesses checked by the Business Verification journey step, one per registration number, with the people acting for them.</p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <input type="search" [(ngModel)]="search" (keyup.enter)="reload()" name="search" placeholder="Name or registration no." class="w-56" aria-label="Search businesses" />
          <label for="status-filter" class="text-sm text-gray-600">Status</label>
          <select id="status-filter" [(ngModel)]="status" (ngModelChange)="reload()" name="status">
            <option value="">All</option>
            @for (s of statuses; track s) {
              <option [value]="s">{{ statusLabel(s) }}</option>
            }
          </select>
        </div>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center py-12">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else if (items().length === 0) {
        <p class="text-sm text-gray-500 py-12 text-center">No businesses{{ status || search ? ' match' : ' yet' }}.</p>
      } @else {
        <div class="overflow-x-auto rounded-lg border border-gray-200">
          <table class="min-w-full divide-y divide-gray-200 text-sm">
            <thead class="bg-gray-50 text-xs uppercase text-gray-500">
              <tr>
                <th class="px-4 py-2 text-left">Business</th>
                <th class="px-4 py-2 text-left">Registration no.</th>
                <th class="px-4 py-2 text-left">Country</th>
                <th class="px-4 py-2 text-left">Applicants</th>
                <th class="px-4 py-2 text-left">Registry checked</th>
                <th class="px-4 py-2 text-left">Status</th>
                <th class="px-4 py-2 text-left">Updated</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-100 bg-white">
              @for (b of items(); track b.id) {
                <tr class="cursor-pointer hover:bg-gray-50" (click)="open(b)">
                  <td class="px-4 py-2 font-medium text-gray-900">{{ b.registeredName || '—' }}</td>
                  <td class="px-4 py-2 font-mono text-xs">{{ b.registrationNumber }}</td>
                  <td class="px-4 py-2">{{ b.country }}</td>
                  <td class="px-4 py-2">{{ b.applicantCount }}</td>
                  <td class="px-4 py-2 text-gray-600">{{ b.registryCheckedAt ? (b.registryCheckedAt | localeDate: 'medium') : '—' }}</td>
                  <td class="px-4 py-2">
                    <span class="px-2 py-0.5 rounded-full text-xs font-medium" [ngClass]="badge(b.status)">{{ statusLabel(b.status) }}</span>
                    @if (b.statusReason) { <span class="ml-1 text-xs text-gray-500">{{ reason(b.statusReason) }}</span> }
                  </td>
                  <td class="px-4 py-2 text-gray-600">{{ b.updatedAt | localeDate: 'medium' }}</td>
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
export class BusinessesPageComponent implements OnInit {
  private readonly service = inject(VerificationService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly statuses = Object.keys(STATUS_LABELS) as BusinessVerificationStatus[];
  readonly loading = signal(true);
  readonly items = signal<VerifiedBusinessSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  status = '';
  search = '';

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

  reload(): void {
    this.page.set(1);
    this.load();
  }

  goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  resize(size: number): void {
    this.pageSize.set(size);
    this.reload();
  }

  open(business: VerifiedBusinessSummary): void {
    this.router.navigate(['/verification/businesses', business.id]);
  }

  private load(): void {
    this.loading.set(true);
    this.service.listBusinesses(this.status, this.search.trim(), this.page(), this.pageSize()).subscribe({
      next: (list) => {
        this.items.set(list.items);
        this.total.set(list.totalCount);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.error(extractErrorMessage(err, 'Failed to load businesses'));
      },
    });
  }
}

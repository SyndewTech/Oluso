import { Component, inject, signal, computed, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent } from '@syndew/oluso-ui-core';
import { SubmissionService } from '../../services/submission.service';
import { ApiService } from '../../services/api.service';
import {
  JourneySubmission,
  DataCollectionPolicy,
  SubmissionStatus,
  SubmissionFilterOptions,
  SUBMISSION_STATUS_OPTIONS,
  SUBMISSION_STATUS_COLORS,
} from '../../models/submission.model';

@Component({
  selector: 'admin-submissions-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent],
  templateUrl: './submissions-page.component.html',
})
export class SubmissionsPageComponent implements OnDestroy {
  private readonly submissionService = inject(SubmissionService);
  private readonly api = inject(ApiService);
  private readonly destroy$ = new Subject<void>();

  // State
  readonly policiesLoading = signal(true);
  readonly submissionsLoading = signal(false);
  readonly exporting = signal(false);
  readonly policies = signal<DataCollectionPolicy[]>([]);
  readonly submissions = signal<JourneySubmission[]>([]);
  readonly totalSubmissions = signal(0);
  readonly selectedPolicyId = signal<string | null>(null);
  /** Tenant-resolved share URL for the currently selected policy. Empty
   *  while the share-url fetch is in flight. */
  readonly selectedPolicyShareUrl = signal<string>('');
  readonly selectedSubmission = signal<JourneySubmission | null>(null);
  readonly showDetailsModal = signal(false);
  readonly copiedUrl = signal(false);

  // Tags management
  readonly availableTags = signal<string[]>([]);
  readonly savingTags = signal(false);
  newTagInput = '';
  editingNotes = false;
  notesInput = '';

  // Bulk selection
  readonly selectedIds = signal<Set<string>>(new Set());
  readonly bulkUpdating = signal(false);

  // Pagination
  readonly page = signal(0);
  readonly pageSize = signal(25);

  // Filters
  filters: SubmissionFilterOptions = {};
  searchTerm = '';

  // Status options for dropdowns
  readonly statusOptions = SUBMISSION_STATUS_OPTIONS;
  readonly statusColors = SUBMISSION_STATUS_COLORS;

  // Computed
  readonly selectedPolicy = computed(() => {
    const policyId = this.selectedPolicyId();
    return this.policies().find((p) => p.id === policyId) || null;
  });

  readonly allSelected = computed(() => {
    const subs = this.submissions();
    const selected = this.selectedIds();
    return subs.length > 0 && subs.every((s) => selected.has(s.id));
  });

  readonly someSelected = computed(() => {
    const selected = this.selectedIds();
    return selected.size > 0;
  });

  readonly totalPages = computed(() => {
    return Math.ceil(this.totalSubmissions() / this.pageSize());
  });

  constructor() {
    this.loadPolicies();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadPolicies(): void {
    this.policiesLoading.set(true);
    this.submissionService
      .getDataCollectionPolicies()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (policies) => {
          this.policies.set(policies);
          this.policiesLoading.set(false);
        },
        error: (err) => {
          console.error('Failed to load data collection policies:', err);
          this.policiesLoading.set(false);
        },
      });
  }

  selectPolicy(policyId: string): void {
    this.selectedPolicyId.set(policyId);
    this.selectedPolicyShareUrl.set('');
    this.page.set(0);
    this.selectedIds.set(new Set());
    this.filters = {};
    this.searchTerm = '';
    this.loadSubmissions();
    this.loadAvailableTags(policyId);
    this.loadShareUrl(policyId);
  }

  /** Fetch the policy's tenant-resolved share URL once when selected, store
   *  in selectedPolicyShareUrl for both display and copy-to-clipboard. */
  private loadShareUrl(policyId: string): void {
    this.api.get<{ url: string }>(`/journeys/${policyId}/share-url`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (r) => this.selectedPolicyShareUrl.set(r.url),
        error: (err) => console.error('Failed to load share URL', err),
      });
  }

  loadAvailableTags(policyId: string): void {
    this.submissionService
      .getAvailableTags(policyId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (tags) => this.availableTags.set(tags),
        error: (err) => console.error('Failed to load tags:', err),
      });
  }

  loadSubmissions(): void {
    const policyId = this.selectedPolicyId();
    if (!policyId) return;

    this.submissionsLoading.set(true);
    this.submissionService
      .getSubmissions(
        policyId,
        this.page() * this.pageSize(),
        this.pageSize(),
        this.filters
      )
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.submissions.set(response.items);
          this.totalSubmissions.set(response.totalCount);
          this.submissionsLoading.set(false);
        },
        error: (err) => {
          console.error('Failed to load submissions:', err);
          this.submissionsLoading.set(false);
        },
      });
  }

  onSearch(): void {
    this.filters = {
      ...this.filters,
      search: this.searchTerm || undefined,
    };
    this.page.set(0);
    this.loadSubmissions();
  }

  onStatusFilterChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as SubmissionStatus | 'all';
    this.filters = {
      ...this.filters,
      status: value === 'all' ? undefined : value,
    };
    this.page.set(0);
    this.loadSubmissions();
  }

  onDateFromChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.filters = {
      ...this.filters,
      dateFrom: value || undefined,
    };
    this.page.set(0);
    this.loadSubmissions();
  }

  onDateToChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.filters = {
      ...this.filters,
      dateTo: value || undefined,
    };
    this.page.set(0);
    this.loadSubmissions();
  }

  clearFilters(): void {
    this.filters = {};
    this.searchTerm = '';
    this.page.set(0);
    this.loadSubmissions();
  }

  // Selection
  toggleSelectAll(): void {
    if (this.allSelected()) {
      this.selectedIds.set(new Set());
    } else {
      this.selectedIds.set(new Set(this.submissions().map((s) => s.id)));
    }
  }

  toggleSelect(id: string): void {
    const newSet = new Set(this.selectedIds());
    if (newSet.has(id)) {
      newSet.delete(id);
    } else {
      newSet.add(id);
    }
    this.selectedIds.set(newSet);
  }

  isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  // Status updates
  updateStatus(submission: JourneySubmission, status: SubmissionStatus): void {
    this.submissionService
      .updateStatus(submission.id, status)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          // Update in list
          this.submissions.update((subs) =>
            subs.map((s) => (s.id === updated.id ? updated : s))
          );
          // Update selected submission if viewing details
          if (this.selectedSubmission()?.id === updated.id) {
            this.selectedSubmission.set(updated);
          }
        },
        error: (err) => console.error('Failed to update status:', err),
      });
  }

  bulkUpdateStatus(status: SubmissionStatus): void {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;

    this.bulkUpdating.set(true);
    this.submissionService
      .bulkUpdateSubmissions({ submissionIds: ids, status })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.selectedIds.set(new Set());
          this.bulkUpdating.set(false);
          this.loadSubmissions();
          this.loadPolicies(); // Refresh counts
        },
        error: (err) => {
          console.error('Bulk update failed:', err);
          this.bulkUpdating.set(false);
        },
      });
  }

  // Delete
  deleteSubmission(submission: JourneySubmission): void {
    if (!confirm('Are you sure you want to delete this submission?')) return;

    this.submissionService
      .deleteSubmission(submission.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.loadSubmissions();
          this.loadPolicies();
        },
        error: (err) => console.error('Failed to delete submission:', err),
      });
  }

  bulkDelete(): void {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;
    if (!confirm(`Are you sure you want to delete ${ids.length} submissions?`)) return;

    this.bulkUpdating.set(true);
    this.submissionService
      .bulkDeleteSubmissions(ids)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.selectedIds.set(new Set());
          this.bulkUpdating.set(false);
          this.loadSubmissions();
          this.loadPolicies();
        },
        error: (err) => {
          console.error('Bulk delete failed:', err);
          this.bulkUpdating.set(false);
        },
      });
  }

  // View details
  viewDetails(submission: JourneySubmission): void {
    this.selectedSubmission.set(submission);
    this.notesInput = submission.notes || '';
    this.editingNotes = false;
    this.newTagInput = '';
    this.showDetailsModal.set(true);
  }

  closeDetailsModal(): void {
    this.showDetailsModal.set(false);
    this.selectedSubmission.set(null);
    this.editingNotes = false;
    this.newTagInput = '';
  }

  // Tag management
  addTag(submission: JourneySubmission): void {
    const tag = this.newTagInput.trim();
    if (!tag) return;

    const currentTags = submission.tags || [];
    if (currentTags.includes(tag)) {
      this.newTagInput = '';
      return;
    }

    const newTags = [...currentTags, tag];
    this.savingTags.set(true);
    this.submissionService
      .updateSubmission(submission.id, { tags: newTags })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.updateSubmissionInList(updated);
          this.selectedSubmission.set(updated);
          this.newTagInput = '';
          this.savingTags.set(false);
          // Add to available tags if new
          if (!this.availableTags().includes(tag)) {
            this.availableTags.update((tags) => [...tags, tag].sort());
          }
        },
        error: (err) => {
          console.error('Failed to add tag:', err);
          this.savingTags.set(false);
        },
      });
  }

  removeTag(submission: JourneySubmission, tagToRemove: string): void {
    const currentTags = submission.tags || [];
    const newTags = currentTags.filter((t) => t !== tagToRemove);

    this.savingTags.set(true);
    this.submissionService
      .updateSubmission(submission.id, { tags: newTags })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.updateSubmissionInList(updated);
          this.selectedSubmission.set(updated);
          this.savingTags.set(false);
        },
        error: (err) => {
          console.error('Failed to remove tag:', err);
          this.savingTags.set(false);
        },
      });
  }

  // Notes management
  startEditingNotes(): void {
    this.notesInput = this.selectedSubmission()?.notes || '';
    this.editingNotes = true;
  }

  cancelEditingNotes(): void {
    this.editingNotes = false;
    this.notesInput = this.selectedSubmission()?.notes || '';
  }

  saveNotes(submission: JourneySubmission): void {
    this.submissionService
      .updateSubmission(submission.id, { notes: this.notesInput })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.updateSubmissionInList(updated);
          this.selectedSubmission.set(updated);
          this.editingNotes = false;
        },
        error: (err) => console.error('Failed to save notes:', err),
      });
  }

  // Helper to update submission in list
  private updateSubmissionInList(updated: JourneySubmission): void {
    this.submissions.update((subs) =>
      subs.map((s) => (s.id === updated.id ? updated : s))
    );
  }

  // Get suggested tags (available tags not already on this submission)
  getSuggestedTags(submission: JourneySubmission): string[] {
    const currentTags = submission.tags || [];
    return this.availableTags().filter((t) => !currentTags.includes(t));
  }

  selectSuggestedTag(tag: string): void {
    this.newTagInput = tag;
  }

  // Export
  exportSubmissions(format: 'csv' | 'json'): void {
    const policyId = this.selectedPolicyId();
    if (!policyId) return;

    this.exporting.set(true);
    this.submissionService
      .exportSubmissions(policyId, { format, ...this.filters })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `${this.selectedPolicy()?.name || 'submissions'}-export.${format}`;
          a.click();
          URL.revokeObjectURL(url);
          this.exporting.set(false);
        },
        error: (err) => {
          console.error('Export failed:', err);
          this.exporting.set(false);
        },
      });
  }

  // Pagination
  previousPage(): void {
    if (this.page() > 0) {
      this.page.update((p) => p - 1);
      this.loadSubmissions();
    }
  }

  nextPage(): void {
    if (this.page() < this.totalPages() - 1) {
      this.page.update((p) => p + 1);
      this.loadSubmissions();
    }
  }

  // The displayed URL and the copy-to-clipboard URL share the same value
  // (selectedPolicyShareUrl), fetched from the backend so it goes through
  // IIssuerResolver — reflects the policy's tenant host (auto-subdomain or
  // CustomDomain), not the platform URL. Concatenating the platform
  // serverUrl previously generated links that landed on _platform tenant
  // context and failed the policy lookup at /Journey/Start.
  async copyUrl(): Promise<void> {
    const url = this.selectedPolicyShareUrl();
    if (!url) return;
    try {
      await navigator.clipboard.writeText(url);
      this.copiedUrl.set(true);
      setTimeout(() => this.copiedUrl.set(false), 2000);
    } catch (err) {
      console.error('Failed to copy URL:', err);
    }
  }

  // Formatters
  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  formatRelativeDate(dateString: string): string {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;
    return date.toLocaleDateString();
  }

  getStatusColor(status: SubmissionStatus): string {
    const colors = this.statusColors[status];
    return `${colors.bg} ${colors.text}`;
  }

  getDataPreview(data: Record<string, unknown>): { key: string; value: string }[] {
    return Object.entries(data)
      .slice(0, 2)
      .map(([key, value]) => ({
        key,
        value: String(value),
      }));
  }

  getRemainingFieldCount(data: Record<string, unknown>): number {
    return Math.max(0, Object.keys(data).length - 2);
  }

  getStartIndex(): number {
    return this.page() * this.pageSize() + 1;
  }

  getEndIndex(): number {
    return Math.min((this.page() + 1) * this.pageSize(), this.totalSubmissions());
  }
}

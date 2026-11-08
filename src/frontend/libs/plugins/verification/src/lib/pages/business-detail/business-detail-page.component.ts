import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ButtonComponent,
  LocaleDatePipe,
  PermissionsService,
  PromptDialogComponent,
  ToastService,
  extractErrorMessage,
  openFetchedFile,
} from '@oluso/ui-core';
import { VerificationService } from '../../services/verification.service';
import {
  BusinessApplicant,
  BusinessCheck,
  BusinessDocument,
  BusinessVerificationStatus,
  REASON_LABELS,
  RELATIONSHIP_LABELS,
  ReviewDecision,
  STATUS_CLASSES,
  STATUS_LABELS,
  VerifiedBusinessDetail,
} from '../../models/verification.model';

interface PendingReview {
  target: 'business' | 'applicant';
  id: string;
  decision: ReviewDecision;
}

const CHECK_KIND_LABELS: Record<BusinessCheck['kind'], string> = {
  lookup: 'Registry lookup',
  reuse: 'Reused registry result',
  hosted_session: 'Hosted verification',
  review: 'Review',
};

@Component({
  selector: 'verification-business-detail-page',
  standalone: true,
  imports: [CommonModule, RouterLink, ButtonComponent, LocaleDatePipe, PromptDialogComponent],
  template: `
    <div class="space-y-6">
      <a routerLink="/verification/businesses" class="text-sm text-primary-700 hover:underline">← Verified businesses</a>

      @if (loading()) {
        <div class="flex items-center justify-center py-12">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else if (business(); as b) {
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 class="text-2xl font-semibold text-gray-900">{{ b.registeredName || b.registrationNumber }}</h1>
            <p class="text-sm text-gray-500 font-mono">{{ b.country }} · {{ b.registrationNumber }}</p>
          </div>
          <div class="flex items-center gap-2">
            <span class="px-2 py-0.5 rounded-full text-xs font-medium" [ngClass]="badge(b.status)">{{ statusLabel(b.status) }}</span>
            @if (b.statusReason) { <span class="text-xs text-gray-500">{{ reason(b.statusReason) }}</span> }
          </div>
        </div>

        @if (b.statusMessage) {
          <div class="bg-gray-50 border border-gray-200 rounded p-3 text-xs text-gray-700">{{ b.statusMessage }}</div>
        }

        @if (canReview() && (b.status === 'pending' || b.status === 'in_review')) {
          <div class="rounded-lg border border-amber-200 bg-amber-50 p-4 flex flex-wrap items-center justify-between gap-3">
            <p class="text-sm text-amber-900">This business has no registry decision yet. You can decide it yourself; people waiting on it follow your decision.</p>
            <div class="flex gap-2">
              <ui-button size="sm" (click)="ask('business', b.id, 'approve')">Verify business</ui-button>
              <ui-button size="sm" variant="danger" (click)="ask('business', b.id, 'reject')">Reject business</ui-button>
            </div>
          </div>
        }

        <section class="rounded-lg border border-gray-200 p-4">
          <h2 class="text-sm font-semibold text-gray-900 mb-3">Registry record</h2>
          <dl class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-6 gap-y-3 text-sm">
            <div><dt class="text-xs text-gray-500">Registered name</dt><dd>{{ b.registeredName || '—' }}</dd></div>
            <div><dt class="text-xs text-gray-500">Registration type</dt><dd>{{ b.registrationType || '—' }}</dd></div>
            <div><dt class="text-xs text-gray-500">Entity type</dt><dd>{{ b.entityType || '—' }}</dd></div>
            <div><dt class="text-xs text-gray-500">Registered on</dt><dd>{{ b.registrationDate || '—' }}</dd></div>
            <div><dt class="text-xs text-gray-500">Registry status</dt><dd>{{ b.rawStatus || 'Not reported' }} ({{ b.activityStatus }})</dd></div>
            <div><dt class="text-xs text-gray-500">Provider</dt><dd>{{ b.registryProviderId || '—' }}{{ b.registryEnvironment ? ' · ' + b.registryEnvironment : '' }}</dd></div>
            <div class="sm:col-span-2 lg:col-span-3"><dt class="text-xs text-gray-500">Address</dt><dd>{{ b.registeredAddress || '—' }}</dd></div>
            <div><dt class="text-xs text-gray-500">Snapshot taken</dt><dd>{{ b.registryCheckedAt ? (b.registryCheckedAt | localeDate: 'medium') : 'No snapshot yet' }}</dd></div>
            <div><dt class="text-xs text-gray-500">Decided</dt><dd>{{ b.decidedAt ? (b.decidedAt | localeDate: 'medium') : '—' }}</dd></div>
            @if (b.reviewedAt) {
              <div><dt class="text-xs text-gray-500">Reviewed</dt><dd>{{ b.reviewedAt | localeDate: 'medium' }}{{ b.reviewNote ? ' — ' + b.reviewNote : '' }}</dd></div>
            }
          </dl>
          <div class="mt-4">
            <p class="text-xs font-medium text-gray-500 mb-1">Directors and other listed people</p>
            @if (b.people.length === 0) {
              <p class="text-xs text-gray-500">None returned.</p>
            } @else {
              <ul class="text-sm divide-y divide-gray-100 border border-gray-200 rounded">
                @for (p of b.people; track $index) {
                  <li class="px-3 py-1.5 flex justify-between gap-3"><span>{{ p.name }}</span><span class="text-gray-500">{{ p.role || '—' }}</span></li>
                }
              </ul>
            }
          </div>
        </section>

        <section class="rounded-lg border border-gray-200 p-4 space-y-4">
          <h2 class="text-sm font-semibold text-gray-900">People acting for this business</h2>
          @for (a of b.applicants; track a.id) {
            <div class="rounded-md border border-gray-100 p-3 space-y-2">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <p class="text-sm font-medium text-gray-900">{{ a.subjectName || a.subjectId || 'Before sign-up' }}</p>
                  <p class="text-xs text-gray-500">{{ relationship(a.relationship) }} · evidence required: {{ a.evidenceRequirement }}</p>
                </div>
                <div class="flex items-center gap-2">
                  <span class="px-2 py-0.5 rounded-full text-xs font-medium" [ngClass]="badge(a.status)">{{ statusLabel(a.status) }}</span>
                  @if (a.statusReason) { <span class="text-xs text-gray-500">{{ reason(a.statusReason) }}</span> }
                </div>
              </div>
              <dl class="grid grid-cols-1 sm:grid-cols-3 gap-x-6 gap-y-2 text-xs">
                <div><dt class="text-gray-500">Name entered</dt><dd class="text-gray-900">{{ a.providedBusinessName || '—' }}</dd></div>
                <div><dt class="text-gray-500">Name match</dt><dd class="text-gray-900">{{ match(a.nameMatched) }}</dd></div>
                <div><dt class="text-gray-500">Director match</dt><dd class="text-gray-900">{{ match(a.directorMatched) }}{{ a.matchedPersonName ? ' (' + a.matchedPersonName + (a.matchedPersonRole ? ', ' + a.matchedPersonRole : '') + ')' : '' }}</dd></div>
                <div><dt class="text-gray-500">Submitted</dt><dd class="text-gray-900">{{ a.createdAt | localeDate: 'medium' }}</dd></div>
                <div><dt class="text-gray-500">Decided</dt><dd class="text-gray-900">{{ a.decidedAt ? (a.decidedAt | localeDate: 'medium') : '—' }}</dd></div>
                <div><dt class="text-gray-500">Outcome email</dt><dd class="text-gray-900">{{ a.outcomeEmailRequested ? 'Requested' : '—' }}</dd></div>
                @if (a.reviewedAt) {
                  <div class="sm:col-span-3"><dt class="text-gray-500">Reviewed</dt><dd class="text-gray-900">{{ a.reviewedAt | localeDate: 'medium' }}{{ a.reviewNote ? ' — ' + a.reviewNote : '' }}</dd></div>
                }
              </dl>
              @if (a.documents.length > 0) {
                <div>
                  <p class="text-xs font-medium text-gray-500 mb-1">Documents</p>
                  <ul class="space-y-1">
                    @for (d of a.documents; track d.id) {
                      <li class="flex items-center justify-between gap-2 text-sm">
                        <span class="truncate">{{ d.fileName }} <span class="text-xs text-gray-500">{{ size(d.sizeBytes) }} · {{ d.createdAt | localeDate: 'medium' }}</span></span>
                        <ui-button size="xs" variant="secondary" (click)="openDocument(a, d)" [disabled]="downloading() === d.id">Open</ui-button>
                      </li>
                    }
                  </ul>
                </div>
              }
              @if (canReview() && (a.status === 'pending' || a.status === 'in_review')) {
                <div class="flex flex-wrap gap-2 pt-1">
                  @if (b.status === 'verified') {
                    <ui-button size="sm" (click)="ask('applicant', a.id, 'approve')">Approve applicant</ui-button>
                  }
                  <ui-button size="sm" variant="danger" (click)="ask('applicant', a.id, 'reject')">Reject applicant</ui-button>
                  @if (a.statusReason === 'awaiting_provider') {
                    <ui-button size="sm" variant="secondary" (click)="refresh(a)" [disabled]="refreshing() === a.id">Ask the provider again</ui-button>
                  }
                </div>
              }
            </div>
          } @empty {
            <p class="text-xs text-gray-500">Nobody yet.</p>
          }
        </section>

        <section class="rounded-lg border border-gray-200 p-4">
          <h2 class="text-sm font-semibold text-gray-900 mb-3">Checks</h2>
          <div class="overflow-x-auto">
            <table class="min-w-full divide-y divide-gray-200 text-xs">
              <thead class="bg-gray-50 uppercase text-gray-500">
                <tr>
                  <th class="px-3 py-2 text-left">When</th>
                  <th class="px-3 py-2 text-left">Check</th>
                  <th class="px-3 py-2 text-left">Outcome</th>
                  <th class="px-3 py-2 text-left">Provider</th>
                  <th class="px-3 py-2 text-left">Reference</th>
                  <th class="px-3 py-2 text-left">By</th>
                  <th class="px-3 py-2 text-left">Detail</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-gray-100">
                @for (c of b.checks; track c.id) {
                  <tr>
                    <td class="px-3 py-2 whitespace-nowrap">{{ c.createdAt | localeDate: 'medium' }}</td>
                    <td class="px-3 py-2">{{ kind(c.kind) }}</td>
                    <td class="px-3 py-2">{{ c.outcome }}</td>
                    <td class="px-3 py-2">{{ c.providerId || '—' }}{{ c.environment ? ' · ' + c.environment : '' }}</td>
                    <td class="px-3 py-2 font-mono break-all">{{ c.sessionReference || c.providerReference || '—' }}</td>
                    <td class="px-3 py-2">{{ c.originKind || '—' }}{{ c.originName ? ': ' + c.originName : '' }}</td>
                    <td class="px-3 py-2 text-gray-600">{{ c.message || '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>

    <ui-prompt-dialog
      [open]="!!pending()"
      [title]="dialogTitle()"
      [message]="dialogMessage()"
      label="Note (optional)"
      inputType="textarea"
      [maxLength]="1000"
      [confirmText]="pending()?.decision === 'approve' ? 'Approve' : 'Reject'"
      [loading]="reviewing()"
      (confirmed)="review($event)"
      (cancelled)="pending.set(null)" />
  `,
})
export class BusinessDetailPageComponent implements OnInit {
  private readonly service = inject(VerificationService);
  private readonly toast = inject(ToastService);
  private readonly permissions = inject(PermissionsService);
  private readonly route = inject(ActivatedRoute);

  readonly loading = signal(true);
  readonly business = signal<VerifiedBusinessDetail | null>(null);
  readonly pending = signal<PendingReview | null>(null);
  readonly reviewing = signal(false);
  readonly refreshing = signal<string | null>(null);
  readonly downloading = signal<string | null>(null);
  readonly canReview = computed(() => this.permissions.hasPermission('business_verification.review'));

  readonly dialogTitle = computed(() => {
    const p = this.pending();
    if (!p) return '';
    const what = p.target === 'business' ? 'business' : 'applicant';
    return p.decision === 'approve' ? `Approve ${what}` : `Reject ${what}`;
  });

  readonly dialogMessage = computed(() => {
    const p = this.pending();
    if (!p) return '';
    if (p.target === 'business') {
      return p.decision === 'approve'
        ? 'Mark the business as verified. People waiting on it are verified when the step asked for nothing more, otherwise they move to review.'
        : 'Reject the business. Everyone waiting on it is rejected; webhooks and workflows receive the decision.';
    }
    return p.decision === 'approve'
      ? 'Let this person act for the business. Webhooks and workflows receive the decision and the applicant is emailed if they asked.'
      : 'Refuse this person. Webhooks and workflows receive the decision and the applicant is emailed if they asked.';
  });

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

  kind(kind: BusinessCheck['kind']): string {
    return CHECK_KIND_LABELS[kind] ?? kind;
  }

  match(value: boolean | null | undefined): string {
    if (value === true) return 'Matched';
    if (value === false) return 'Did not match';
    return 'Not checked';
  }

  size(bytes: number): string {
    return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  ask(target: 'business' | 'applicant', id: string, decision: ReviewDecision): void {
    this.pending.set({ target, id, decision });
  }

  review(note: string): void {
    const p = this.pending();
    if (!p) return;
    this.reviewing.set(true);
    const done = (message: string) => {
      this.reviewing.set(false);
      this.pending.set(null);
      this.toast.success(message);
      this.load();
    };
    const failed = (err: unknown) => {
      this.reviewing.set(false);
      this.pending.set(null);
      this.toast.error(extractErrorMessage(err, 'Failed to record the review'));
      if (err instanceof HttpErrorResponse && err.status === 409) this.load();
    };
    const text = note.trim() || undefined;
    if (p.target === 'business') {
      this.service.reviewBusiness(p.id, p.decision, text).subscribe({
        next: () => done(p.decision === 'approve' ? 'Business verified' : 'Business rejected'),
        error: failed,
      });
    } else {
      this.service.reviewApplicant(p.id, p.decision, text).subscribe({
        next: () => done(p.decision === 'approve' ? 'Applicant approved' : 'Applicant rejected'),
        error: failed,
      });
    }
  }

  refresh(applicant: BusinessApplicant): void {
    this.refreshing.set(applicant.id);
    this.service.refreshApplicant(applicant.id).subscribe({
      next: (updated) => {
        this.refreshing.set(null);
        this.toast.success(updated.status === applicant.status ? 'The provider has no decision yet' : 'Updated from the provider');
        this.load();
      },
      error: (err) => {
        this.refreshing.set(null);
        this.toast.error(extractErrorMessage(err, 'Failed to ask the provider'));
      },
    });
  }

  openDocument(applicant: BusinessApplicant, document: BusinessDocument): void {
    this.downloading.set(document.id);
    this.service.downloadDocument(applicant.id, document.id).subscribe({
      next: (blob) => {
        this.downloading.set(null);
        openFetchedFile(blob, document.fileName);
      },
      error: (err) => {
        this.downloading.set(null);
        this.toast.error(extractErrorMessage(err, 'Failed to open the document'));
      },
    });
  }

  private load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) return;
    this.loading.set(this.business() === null);
    this.service.getBusiness(id).subscribe({
      next: (business) => {
        this.business.set(business);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.error(extractErrorMessage(err, 'Failed to load the business'));
      },
    });
  }
}

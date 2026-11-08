import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { LocaleDatePipe } from '../../pipes/locale-date.pipe';

export interface TimelineEntry {
  id: string;
  entityType: string;
  entityId: string;
  fromStatus: string | null;
  toStatus: string;
  description: string | null;
  reason: string | null;
  actorUserId: string;
  actorName: string | null;
  actorRole: string | null;
  occurredAt: string;
  metadata: string | null;
}

/**
 * Reusable timeline visualiser that shows the lifecycle (status history) of any entity.
 *
 * Usage:
 *   <ui-entity-timeline entityType="Item" [entityId]="id()" baseUrl="/api/admin/<module>/timeline" />
 *
 * Renders a vertical timeline with each status transition as a node,
 * showing actor, time, status badges, and optional reason/description.
 */
@Component({
  selector: 'ui-entity-timeline',
  standalone: true,
  imports: [CommonModule, LocaleDatePipe],
  template: `
    <div class="rounded-lg border border-slate-200 bg-white">
      <div class="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <h3 class="text-sm font-semibold text-slate-900">
          {{ title() }}
          @if (entries().length > 0) {
            <span class="ml-2 text-xs font-normal text-slate-500">({{ entries().length }} {{ entries().length === 1 ? 'event' : 'events' }})</span>
          }
        </h3>
        @if (loading()) {
          <span class="text-xs text-slate-500">Loading…</span>
        } @else if (!collapsed) {
          <button class="text-xs text-slate-500 hover:text-slate-700" (click)="reload()">Refresh</button>
        }
      </div>

      @if (error()) {
        <div class="p-4 text-sm text-red-600">{{ error() }}</div>
      } @else if (loading()) {
        <div class="p-6 flex items-center justify-center">
          <div class="h-5 w-5 animate-spin rounded-full border-2 border-slate-200 border-t-slate-500"></div>
        </div>
      } @else if (entries().length === 0) {
        <div class="p-6 text-center text-sm text-slate-500">
          No status history yet.
        </div>
      } @else {
        <ol class="p-4 space-y-0">
          @for (entry of entries(); track entry.id; let i = $index, isLast = $last) {
            <li class="relative flex gap-3 pb-4 last:pb-0">
              <!-- Vertical connector line -->
              @if (!isLast) {
                <span class="absolute left-[11px] top-6 -bottom-0 w-0.5 bg-slate-200" aria-hidden="true"></span>
              }

              <!-- Status dot -->
              <div class="relative z-10 flex-shrink-0">
                <div
                  class="flex h-6 w-6 items-center justify-center rounded-full ring-4 ring-white"
                  [class]="dotColorClass(entry.toStatus)"
                >
                  <svg class="h-3 w-3 text-white" fill="currentColor" viewBox="0 0 20 20">
                    <path d="M10 2a8 8 0 100 16 8 8 0 000-16z" />
                  </svg>
                </div>
              </div>

              <!-- Entry content -->
              <div class="flex-1 min-w-0 -mt-0.5">
                <div class="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                  @if (entry.fromStatus) {
                    <span class="text-xs text-slate-400">{{ formatStatus(entry.fromStatus) }}</span>
                    <span class="text-slate-300" aria-hidden="true">→</span>
                  }
                  <span
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                    [class]="badgeColorClass(entry.toStatus)"
                  >
                    {{ formatStatus(entry.toStatus) }}
                  </span>
                </div>

                @if (entry.description) {
                  <p class="mt-1 text-sm text-slate-700">{{ entry.description }}</p>
                }

                @if (entry.reason) {
                  <p class="mt-1 text-xs italic text-slate-500">"{{ entry.reason }}"</p>
                }

                <div class="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-slate-500">
                  <span>{{ entry.occurredAt | localeDate: 'medium' }}</span>
                  @if (entry.actorName) {
                    <span class="text-slate-300">·</span>
                    <span>by {{ entry.actorName }}</span>
                  } @else if (entry.actorUserId === 'system') {
                    <span class="text-slate-300">·</span>
                    <span class="italic">system</span>
                  }
                  @if (entry.actorRole) {
                    <span class="text-slate-300">·</span>
                    <span>{{ entry.actorRole }}</span>
                  }
                </div>
              </div>
            </li>
          }
        </ol>
      }
    </div>
  `,
})
export class EntityTimelineComponent implements OnChanges {
  @Input({ required: true }) entityType = '';
  @Input({ required: true }) entityId = '';
  /**
   * Base URL for the timeline endpoint — the component appends `/{entityType}/{entityId}`.
   * Each consumer supplies its own module route, e.g. `/api/admin/<module>/timeline`.
   */
  @Input({ required: true }) baseUrl = '';
  /** Override the default title ("Status Timeline"). */
  @Input() titleOverride: string | null = null;
  /** When true, hides the refresh button. */
  @Input() collapsed = false;

  private readonly http = inject(HttpClient);

  readonly entries = signal<TimelineEntry[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  title = () => this.titleOverride ?? 'Status Timeline';

  ngOnChanges(changes: SimpleChanges): void {
    if (
      (changes['entityId'] || changes['entityType'] || changes['baseUrl']) &&
      this.entityId &&
      this.entityType
    ) {
      this.reload();
    }
  }

  reload(): void {
    if (!this.entityType || !this.entityId) return;
    this.loading.set(true);
    this.error.set(null);
    this.http.get<TimelineEntry[]>(`${this.baseUrl}/${this.entityType}/${this.entityId}`).subscribe({
      next: (data) => {
        this.entries.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'Failed to load timeline');
        this.loading.set(false);
      },
    });
  }

  /** Convert PascalCase status to "Title Case" for display (e.g., "PendingApproval" → "Pending Approval"). */
  formatStatus(status: string): string {
    return status.replace(/([A-Z])/g, ' $1').trim();
  }

  /** Tailwind colour for the timeline dot, by status semantics. */
  dotColorClass(status: string): string {
    const s = status.toLowerCase();
    // Terminal good states
    if (s === 'completed' || s === 'paid' || s === 'active' || s === 'signed' || s === 'approved' || s === 'accepted')
      return 'bg-emerald-500';
    // Terminal bad/cancelled states
    if (s === 'cancelled' || s === 'denied' || s === 'declined' || s === 'noshow' || s === 'expired' || s === 'revoked' || s === 'voided')
      return 'bg-red-500';
    if (s === 'writtenoff' || s === 'rejected') return 'bg-slate-500';
    // Pending / in-progress
    if (s === 'pendingapproval' || s === 'submitted' || s === 'inprogress' || s === 'appealed') return 'bg-amber-500';
    if (s === 'checkedin' || s === 'confirmed' || s === 'collected' || s === 'sent') return 'bg-blue-500';
    if (s === 'partiallypaid' || s === 'dispensed' || s === 'rescheduled') return 'bg-violet-500';
    // Initial / draft
    return 'bg-slate-400';
  }

  /** Tailwind classes for the status badge pill. */
  badgeColorClass(status: string): string {
    const s = status.toLowerCase();
    if (s === 'completed' || s === 'paid' || s === 'active' || s === 'signed' || s === 'approved' || s === 'accepted')
      return 'bg-emerald-100 text-emerald-800';
    if (s === 'cancelled' || s === 'denied' || s === 'declined' || s === 'noshow' || s === 'expired' || s === 'revoked' || s === 'voided')
      return 'bg-red-100 text-red-800';
    if (s === 'writtenoff' || s === 'rejected') return 'bg-slate-100 text-slate-800';
    if (s === 'pendingapproval' || s === 'submitted' || s === 'inprogress' || s === 'appealed') return 'bg-amber-100 text-amber-800';
    if (s === 'checkedin' || s === 'confirmed' || s === 'collected' || s === 'sent') return 'bg-blue-100 text-blue-800';
    if (s === 'partiallypaid' || s === 'dispensed' || s === 'rescheduled') return 'bg-violet-100 text-violet-800';
    return 'bg-slate-100 text-slate-700';
  }
}

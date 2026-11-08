import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

/**
 * Slim, framework-agnostic shape any module can map its history payload to.
 * Any module's history DTO can be projected onto this without copying the
 * full DTO type into ui-core (which must not depend on backend module schemas).
 */
export interface ApprovalHistoryRow {
  action: string;
  performedByUserId?: string;
  performedByName?: string;
  /** Step the row pertains to (omit for instance-level events like Submitted/Recalled). */
  stepOrder?: number | null;
  stepName?: string | null;
  comments?: string | null;
  occurredAt: string;
}

/**
 * Purely presentational timeline of approval activity (submitted, approved,
 * rejected, skipped, recalled, …). Has no HTTP dependency — callers fetch
 * the rows however they like (workspace API, module self-service API, admin API)
 * and pass them in via <c>[entries]</c>. That keeps ui-core free of any
 * approval-module coupling and lets every consumer reuse the same visual
 * treatment.
 *
 * Companion to <c>ui-approval-flow</c>: that one shows *current* step state,
 * this one shows the *audit trail* of decisions taken so far.
 */
@Component({
  selector: 'ui-approval-history',
  standalone: true,
  imports: [CommonModule, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!entries || entries.length === 0) {
      <div class="py-4 text-center text-xs text-gray-400">No approval activity recorded yet.</div>
    } @else {
      <ol class="relative ml-3 border-l border-gray-200 space-y-4">
        @for (entry of entries; track $index) {
          <li class="ml-4">
            <span
              class="absolute -left-2 flex h-4 w-4 items-center justify-center rounded-full ring-4 ring-white"
              [class]="dotClass(entry.action)"
            >
              <ui-icon [name]="iconFor(entry.action)" class="h-2.5 w-2.5 text-white" />
            </span>
            <div class="flex flex-wrap items-baseline gap-x-2">
              <span class="text-sm font-medium text-gray-900">{{ actionLabel(entry.action) }}</span>
              @if (entry.stepName) {
                <span class="text-xs text-gray-600">on step <span class="font-medium">{{ entry.stepName }}</span></span>
              }
              @if (entry.performedByName || entry.performedByUserId) {
                <span class="text-xs text-gray-500">by {{ entry.performedByName || entry.performedByUserId }}</span>
              }
              <span class="text-xs text-gray-400">· {{ formatDate(entry.occurredAt) }}</span>
            </div>
            @if (entry.comments) {
              <p class="mt-1 text-xs text-gray-600 italic">"{{ entry.comments }}"</p>
            }
          </li>
        }
      </ol>
    }
  `,
})
export class ApprovalHistoryComponent {
  @Input() entries: ApprovalHistoryRow[] | null | undefined = [];

  actionLabel(action: string): string {
    switch ((action || '').toLowerCase()) {
      case 'submitted': return 'Submitted';
      case 'approved': return 'Approved';
      case 'rejected': return 'Rejected';
      case 'recalled': return 'Recalled';
      case 'reassigned': return 'Reassigned';
      case 'skipped': return 'Skipped';
      case 'completed': return 'Completed';
      case 'commented': return 'Commented';
      default: return action;
    }
  }

  iconFor(action: string): string {
    switch ((action || '').toLowerCase()) {
      case 'approved':
      case 'completed':
        return 'check';
      case 'rejected':
      case 'recalled':
        return 'x-circle';
      case 'skipped':
        return 'arrow-path-rounded-square';
      case 'reassigned':
        return 'users';
      default:
        return 'document-text';
    }
  }

  dotClass(action: string): string {
    switch ((action || '').toLowerCase()) {
      case 'approved':
      case 'completed':
        return 'bg-green-500';
      case 'rejected':
      case 'recalled':
        return 'bg-red-500';
      case 'skipped':
        return 'bg-amber-500';
      default:
        return 'bg-gray-400';
    }
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleString();
  }
}

import { Component, Input, OnChanges, SimpleChanges, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { LocaleDatePipe } from '../../pipes/locale-date.pipe';

export interface ApprovalFlowTask {
  id: string;
  assignedToUserId: string;
  status: string;
  decision: string | null;
  comments: string | null;
  decisionAt: string | null;
  dueDate: string | null;
}

export interface ApprovalFlowStep {
  stepOrder: number;
  name: string;
  approverType: string;
  condition: string | null;
  status: string;
  isCurrent: boolean;
  tasks: ApprovalFlowTask[];
}

export interface ApprovalFlow {
  instanceId: string;
  workflowName: string;
  workflowDescription: string | null;
  entityType: string;
  entityId: string;
  requesterId: string;
  status: string;
  currentStepOrder: number;
  totalSteps: number;
  startedAt: string;
  completedAt: string | null;
  finalComments: string | null;
  steps: ApprovalFlowStep[];
}

/**
 * Visualises the approval workflow chain for an entity in approval.
 * Renders steps as a horizontal pipeline with progress indicators, current step highlight,
 * approver assignments, decisions, and due dates.
 *
 * Usage:
 *   <ui-approval-flow entityType="Item" [entityId]="id()" baseUrl="/api/admin/<module>/approval-flow" />
 *
 * Renders nothing if the entity has no approval workflow (returns 404 quietly).
 */
@Component({
  selector: 'ui-approval-flow',
  standalone: true,
  imports: [CommonModule, LocaleDatePipe],
  template: `
    @if (loading()) {
      <div class="rounded-lg border border-slate-200 bg-white p-4">
        <div class="flex items-center gap-2 text-sm text-slate-500">
          <div class="h-4 w-4 animate-spin rounded-full border-2 border-slate-200 border-t-slate-500"></div>
          Loading approval flow…
        </div>
      </div>
    } @else if (flow()) {
      <div class="rounded-lg border border-slate-200 bg-white">
        <!-- Header -->
        <div class="border-b border-slate-200 px-4 py-3">
          <div class="flex items-start justify-between gap-4">
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2">
                <svg class="h-4 w-4 flex-shrink-0 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                </svg>
                <h3 class="text-sm font-semibold text-slate-900">{{ flow()!.workflowName }}</h3>
                <span
                  class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                  [class]="instanceStatusBadgeClass()"
                >{{ formatStatus(flow()!.status) }}</span>
              </div>
              @if (flow()!.workflowDescription) {
                <p class="mt-1 text-xs text-slate-500">{{ flow()!.workflowDescription }}</p>
              }
            </div>
            <div class="text-right text-xs text-slate-500 flex-shrink-0">
              <div>Step {{ flow()!.currentStepOrder }} of {{ flow()!.totalSteps }}</div>
              <div class="mt-0.5">Started {{ flow()!.startedAt | localeDate: 'mediumDate' }}</div>
            </div>
          </div>
        </div>

        <!-- Progress Bar -->
        <div class="px-4 pt-4">
          <div class="relative h-1.5 w-full rounded-full bg-slate-100">
            <div
              class="absolute left-0 top-0 h-full rounded-full transition-all"
              [class]="progressBarClass()"
              [style.width.%]="progressPercent()"
            ></div>
          </div>
        </div>

        <!-- Step Pipeline -->
        <div class="px-4 py-4">
          <ol class="flex flex-col gap-3 md:flex-row md:items-stretch">
            @for (step of flow()!.steps; track step.stepOrder; let isLast = $last) {
              <li class="flex flex-1 items-stretch">
                <!-- Step Card -->
                <div
                  class="flex-1 rounded-lg border p-3 text-sm"
                  [class]="stepCardClass(step)"
                >
                  <!-- Step header -->
                  <div class="flex items-center gap-2">
                    <div
                      class="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full text-xs font-semibold"
                      [class]="stepNumberClass(step)"
                    >
                      @if (step.status === 'Approved') {
                        <svg class="h-3.5 w-3.5" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/></svg>
                      } @else if (step.status === 'Rejected') {
                        <svg class="h-3.5 w-3.5" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd"/></svg>
                      } @else if (step.status === 'Skipped') {
                        <svg class="h-3.5 w-3.5" fill="currentColor" viewBox="0 0 20 20"><path d="M10 12a2 2 0 100-4 2 2 0 000 4z"/></svg>
                      } @else {
                        {{ step.stepOrder }}
                      }
                    </div>
                    <div class="min-w-0 flex-1">
                      <div class="font-medium text-slate-900 truncate">{{ step.name }}</div>
                      <div class="text-xs text-slate-500">{{ formatApproverType(step.approverType) }}</div>
                    </div>
                  </div>

                  <!-- Status badge -->
                  <div class="mt-2">
                    <span
                      class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                      [class]="stepStatusBadgeClass(step.status)"
                    >
                      {{ formatStatus(step.status) }}
                      @if (step.isCurrent) {
                        <span class="ml-1 inline-flex h-1.5 w-1.5 animate-pulse rounded-full bg-amber-500"></span>
                      }
                    </span>
                  </div>

                  <!-- Tasks -->
                  @if (step.tasks.length > 0) {
                    <div class="mt-2 space-y-1.5">
                      @for (task of step.tasks; track task.id) {
                        <div class="rounded bg-white/60 p-1.5 text-xs">
                          <div class="flex items-center justify-between gap-1">
                            <span class="text-slate-700 truncate">{{ task.assignedToUserId }}</span>
                            @if (task.decision) {
                              <span [class]="decisionTextClass(task.decision)">{{ task.decision }}</span>
                            } @else if (task.dueDate) {
                              <span class="text-slate-500" title="Due">{{ task.dueDate | localeDate: 'shortDate' }}</span>
                            }
                          </div>
                          @if (task.comments) {
                            <div class="mt-0.5 italic text-slate-500 line-clamp-2">"{{ task.comments }}"</div>
                          }
                          @if (task.decisionAt) {
                            <div class="mt-0.5 text-slate-400">{{ task.decisionAt | localeDate: 'short' }}</div>
                          }
                        </div>
                      }
                    </div>
                  }

                  <!-- Condition (if any) -->
                  @if (step.condition) {
                    <div class="mt-2 text-xs text-slate-400 truncate" title="{{ step.condition }}">
                      <span class="italic">when</span> {{ step.condition }}
                    </div>
                  }
                </div>

                <!-- Connector arrow (between steps, hidden on last) -->
                @if (!isLast) {
                  <div class="hidden md:flex items-center px-1 text-slate-300">
                    <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5m0 0l-5 5m5-5H6" />
                    </svg>
                  </div>
                }
              </li>
            }
          </ol>
        </div>

        @if (flow()!.finalComments) {
          <div class="border-t border-slate-200 bg-slate-50 px-4 py-2 text-xs italic text-slate-600">
            "{{ flow()!.finalComments }}"
          </div>
        }
      </div>
    }
    <!-- Renders nothing when no flow exists (entity not in approval) -->
  `,
})
export class ApprovalFlowComponent implements OnChanges {
  @Input({ required: true }) entityType = '';
  @Input({ required: true }) entityId = '';
  /**
   * Base URL for the approval-flow endpoint — the component appends `/{entityType}/{entityId}`.
   * Each consumer supplies its own module route, e.g. `/api/admin/<module>/approval-flow`.
   */
  @Input({ required: true }) baseUrl = '';

  private readonly http = inject(HttpClient);

  readonly flow = signal<ApprovalFlow | null>(null);
  readonly loading = signal(false);

  readonly progressPercent = computed(() => {
    const f = this.flow();
    if (!f || f.totalSteps === 0) return 0;
    if (f.status === 'Approved') return 100;
    if (f.status === 'Rejected') {
      // Show progress up to the rejected step
      return Math.min((f.currentStepOrder / f.totalSteps) * 100, 100);
    }
    // Pending: half-completed for the current step
    return Math.min(((f.currentStepOrder - 0.5) / f.totalSteps) * 100, 100);
  });

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['entityId'] || changes['entityType'] || changes['baseUrl']) && this.entityId && this.entityType) {
      this.reload();
    }
  }

  reload(): void {
    if (!this.entityType || !this.entityId) return;
    this.loading.set(true);
    this.http.get<ApprovalFlow>(`${this.baseUrl}/${this.entityType}/${this.entityId}`).subscribe({
      next: (data) => {
        this.flow.set(data);
        this.loading.set(false);
      },
      error: () => {
        // 404 = no approval flow for this entity → render nothing
        this.flow.set(null);
        this.loading.set(false);
      },
    });
  }

  formatStatus(status: string): string {
    return status.replace(/([A-Z])/g, ' $1').trim();
  }

  formatApproverType(type: string): string {
    return type.replace(/([A-Z])/g, ' $1').trim();
  }

  // ─── Styling Helpers ─────────────────────────────────────

  instanceStatusBadgeClass = computed(() => {
    const s = this.flow()?.status?.toLowerCase() ?? '';
    if (s === 'approved') return 'bg-emerald-100 text-emerald-800';
    if (s === 'rejected' || s === 'cancelled') return 'bg-red-100 text-red-800';
    return 'bg-amber-100 text-amber-800';
  });

  progressBarClass = computed(() => {
    const s = this.flow()?.status?.toLowerCase() ?? '';
    if (s === 'approved') return 'bg-emerald-500';
    if (s === 'rejected' || s === 'cancelled') return 'bg-red-500';
    return 'bg-amber-500';
  });

  stepCardClass(step: ApprovalFlowStep): string {
    const s = step.status.toLowerCase();
    if (s === 'approved') return 'border-emerald-200 bg-emerald-50';
    if (s === 'rejected') return 'border-red-200 bg-red-50';
    if (s === 'skipped') return 'border-slate-200 bg-slate-50 opacity-70';
    if (step.isCurrent || s === 'pending') return 'border-amber-300 bg-amber-50 ring-2 ring-amber-200';
    return 'border-slate-200 bg-white';
  }

  stepNumberClass(step: ApprovalFlowStep): string {
    const s = step.status.toLowerCase();
    if (s === 'approved') return 'bg-emerald-500 text-white';
    if (s === 'rejected') return 'bg-red-500 text-white';
    if (s === 'skipped') return 'bg-slate-400 text-white';
    if (step.isCurrent || s === 'pending') return 'bg-amber-500 text-white';
    return 'bg-slate-200 text-slate-600';
  }

  stepStatusBadgeClass(status: string): string {
    const s = status.toLowerCase();
    if (s === 'approved') return 'bg-emerald-100 text-emerald-800';
    if (s === 'rejected') return 'bg-red-100 text-red-800';
    if (s === 'skipped') return 'bg-slate-100 text-slate-600';
    if (s === 'pending') return 'bg-amber-100 text-amber-800';
    if (s === 'waiting') return 'bg-slate-100 text-slate-500';
    return 'bg-slate-100 text-slate-700';
  }

  decisionTextClass(decision: string): string {
    const d = decision.toLowerCase();
    if (d === 'approved') return 'font-medium text-emerald-700';
    if (d === 'rejected') return 'font-medium text-red-700';
    return 'font-medium text-slate-700';
  }
}

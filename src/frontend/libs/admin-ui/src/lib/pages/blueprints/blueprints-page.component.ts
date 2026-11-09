import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LocaleDatePipe } from '@syndew/oluso-ui-core';
import {
  BlueprintsService,
  BlueprintListEntry,
  BlueprintApplyResult,
} from '../../services/blueprints.service';

/**
 * Blueprints — on-demand starter packs. A tenant administrator applies a
 * module's operational starter configuration (note templates, order sets,
 * draft automation workflows, …) with one click, any time — idempotent, so
 * re-applying is always safe. Seeded workflows land disabled: enable them
 * in Workflows after setting recipients.
 */
@Component({
  selector: 'admin-blueprints-page',
  standalone: true,
  imports: [CommonModule, FormsModule, LocaleDatePipe],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Blueprints</h1>
        <p class="mt-1 text-sm text-gray-500">
          Ready-made starter packs for your licensed modules — apply one to set up
          templates, order sets and draft automations in one click. Applying again
          is safe: existing items are skipped unless you choose overwrite.
        </p>
      </div>

      @if (error()) {
        <div class="rounded-md bg-red-50 p-4"><p class="text-sm text-red-600">{{ error() }}</p></div>
      }

      @if (loading()) {
        <div class="text-center py-12 text-gray-500">Loading…</div>
      } @else if (blueprints().length === 0) {
        <div class="text-center py-12 text-gray-500">
          No blueprints are available for your licensed modules yet.
        </div>
      } @else {
        <div class="grid gap-4 md:grid-cols-2">
          @for (bp of blueprints(); track bp.key) {
            <div class="rounded-lg border border-gray-200 bg-white p-5 flex flex-col">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h2 class="text-base font-semibold text-gray-900">{{ bp.title }}</h2>
                  <span class="inline-block mt-1 rounded bg-gray-100 px-2 py-0.5 text-xs font-medium uppercase tracking-wide text-gray-500">
                    {{ bp.moduleKey }}
                  </span>
                </div>
                @if (bp.appliedAt) {
                  <span class="shrink-0 rounded-full bg-green-50 px-2.5 py-0.5 text-xs font-medium text-green-700">
                    Applied {{ bp.appliedAt | localeDate: 'mediumDate' }}
                  </span>
                }
              </div>

              <p class="mt-2 text-sm text-gray-600 flex-1">{{ bp.description }}</p>

              <div class="mt-3 flex flex-wrap gap-2">
                @for (kind of itemKinds(bp); track kind) {
                  <span class="rounded border border-gray-200 bg-gray-50 px-2 py-0.5 text-xs text-gray-600">
                    {{ bp.itemCounts[kind] }} {{ kindLabel(kind) }}
                  </span>
                }
              </div>

              <div class="mt-4 flex items-center gap-3">
                <button
                  (click)="apply(bp, false)"
                  [disabled]="applying() === bp.key"
                  class="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
                  {{ applying() === bp.key ? 'Applying…' : bp.appliedAt ? 'Re-apply' : 'Apply' }}
                </button>
                @if (bp.appliedAt) {
                  <button
                    (click)="applyOverwrite(bp)"
                    [disabled]="applying() === bp.key"
                    class="rounded-md border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                    title="Replace existing items with the pack's version. Overwritten automations return to draft.">
                    Re-apply with overwrite
                  </button>
                }
              </div>

              @if (lastResult()?.key === bp.key) {
                <div class="mt-3 rounded-md bg-blue-50 px-3 py-2 text-xs text-blue-800">
                  <span class="font-medium">{{ lastResult()!.created.length }} created,
                  {{ lastResult()!.skipped.length }} skipped.</span>
                  @if (createdWorkflows() > 0) {
                    <span class="block mt-0.5">
                      {{ createdWorkflows() }} automation workflow(s) were added as drafts —
                      set recipients and enable them in Workflows.
                    </span>
                  }
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class BlueprintsPageComponent implements OnInit {
  private readonly service = inject(BlueprintsService);

  readonly blueprints = signal<BlueprintListEntry[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly applying = signal<string | null>(null);
  readonly lastResult = signal<BlueprintApplyResult | null>(null);

  ngOnInit(): void {
    this.reload();
  }

  itemKinds(bp: BlueprintListEntry): string[] {
    return Object.keys(bp.itemCounts ?? {});
  }

  kindLabel(kind: string): string {
    // "noteTemplate" -> "note templates"
    const words = kind.replace(/([A-Z])/g, ' $1').toLowerCase().trim();
    return `${words}s`;
  }

  createdWorkflows(): number {
    return (this.lastResult()?.created ?? []).filter((i) => i.kind === 'workflow').length;
  }

  apply(bp: BlueprintListEntry, overwrite: boolean): void {
    this.error.set(null);
    this.lastResult.set(null);
    this.applying.set(bp.key);
    this.service.apply(bp.key, overwrite).subscribe({
      next: (result) => {
        this.applying.set(null);
        this.lastResult.set(result);
        this.reload(false);
      },
      error: (err) => {
        this.applying.set(null);
        this.error.set(err?.error?.message ?? 'Failed to apply the blueprint.');
      },
    });
  }

  applyOverwrite(bp: BlueprintListEntry): void {
    // Overwrite replaces tenant edits with the pack's version — confirm first.
    if (!confirm(`Replace existing "${bp.title}" items with the pack's versions? ` +
        `Your edits to those items will be lost, and overwritten automations return to draft.`)) {
      return;
    }
    this.apply(bp, true);
  }

  private reload(showSpinner = true): void {
    if (showSpinner) this.loading.set(true);
    this.service.list().subscribe({
      next: (list) => {
        this.blueprints.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load blueprints.');
        this.loading.set(false);
      },
    });
  }
}

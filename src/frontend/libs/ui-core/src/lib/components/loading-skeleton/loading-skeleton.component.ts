import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

/**
 * Content-shaped loading placeholder that replaces bare spinners.
 *
 * Provides different skeleton types that match the shape of the actual
 * content being loaded, giving users a better sense of what to expect.
 *
 * Usage:
 *   <ui-loading-skeleton type="table" [rows]="8" />
 *   <ui-loading-skeleton type="dashboard" />
 *   <ui-loading-skeleton type="form" />
 */
@Component({
  selector: 'ui-loading-skeleton',
  standalone: true,
  imports: [CommonModule],
  template: `
    <!-- Table skeleton -->
    <div *ngIf="type === 'table'" class="rounded-lg border border-slate-200 bg-white p-4">
      <!-- Header row -->
      <div class="mb-4 flex gap-4 border-b border-slate-100 pb-3">
        <div class="h-3 w-24 animate-pulse rounded bg-slate-200"></div>
        <div class="h-3 w-32 animate-pulse rounded bg-slate-200"></div>
        <div class="h-3 w-20 animate-pulse rounded bg-slate-200"></div>
        <div class="h-3 w-28 animate-pulse rounded bg-slate-200"></div>
        <div class="h-3 w-16 animate-pulse rounded bg-slate-200"></div>
      </div>
      <!-- Data rows -->
      <div *ngFor="let row of rowArray" class="flex gap-4 py-3 border-b border-slate-50 last:border-0">
        <div class="h-3 animate-pulse rounded bg-slate-100" [ngClass]="row % 2 === 0 ? 'w-28' : 'w-20'"></div>
        <div class="h-3 animate-pulse rounded bg-slate-100" [ngClass]="row % 2 === 0 ? 'w-36' : 'w-24'"></div>
        <div class="h-3 w-16 animate-pulse rounded bg-slate-100"></div>
        <div class="h-3 animate-pulse rounded bg-slate-100" [ngClass]="row % 2 === 0 ? 'w-24' : 'w-32'"></div>
        <div class="h-3 w-12 animate-pulse rounded bg-slate-100"></div>
      </div>
    </div>

    <!-- Card skeleton -->
    <div *ngIf="type === 'card'" class="grid grid-cols-2 gap-4">
      <div *ngFor="let card of cardArray" class="rounded-lg border border-slate-200 bg-white p-5">
        <div class="mb-3 h-4 w-2/3 animate-pulse rounded bg-slate-200"></div>
        <div class="mb-2 h-3 w-full animate-pulse rounded bg-slate-100"></div>
        <div class="mb-2 h-3 w-5/6 animate-pulse rounded bg-slate-100"></div>
        <div class="mt-4 h-3 w-1/3 animate-pulse rounded bg-slate-200"></div>
      </div>
    </div>

    <!-- Detail skeleton -->
    <div *ngIf="type === 'detail'" class="space-y-4">
      <!-- Header bar -->
      <div class="flex items-center gap-4">
        <div class="h-6 w-48 animate-pulse rounded bg-slate-200"></div>
        <div class="h-6 w-20 animate-pulse rounded-full bg-slate-100"></div>
      </div>
      <!-- Detail card -->
      <div class="rounded-lg border border-slate-200 bg-white p-6">
        <div class="grid grid-cols-2 gap-x-8 gap-y-5">
          <div *ngFor="let pair of detailPairs" class="space-y-1.5">
            <div class="h-3 animate-pulse rounded bg-slate-200" [ngClass]="pair % 2 === 0 ? 'w-20' : 'w-24'"></div>
            <div class="h-4 animate-pulse rounded bg-slate-100" [ngClass]="pair % 2 === 0 ? 'w-40' : 'w-32'"></div>
          </div>
        </div>
      </div>
    </div>

    <!-- Dashboard skeleton -->
    <div *ngIf="type === 'dashboard'" class="space-y-4">
      <!-- Metric cards row -->
      <div class="grid grid-cols-4 gap-4">
        <div *ngFor="let m of metricCards" class="rounded-lg border border-slate-200 bg-white p-4">
          <div class="mb-2 h-7 w-16 animate-pulse rounded bg-slate-200"></div>
          <div class="h-3 w-20 animate-pulse rounded bg-slate-100"></div>
        </div>
      </div>
      <!-- Larger content cards -->
      <div class="grid grid-cols-2 gap-4">
        <div *ngFor="let c of dashboardCards" class="rounded-lg border border-slate-200 bg-white p-5">
          <div class="mb-4 h-4 w-1/3 animate-pulse rounded bg-slate-200"></div>
          <div class="mb-2 h-3 w-full animate-pulse rounded bg-slate-100"></div>
          <div class="mb-2 h-3 w-4/5 animate-pulse rounded bg-slate-100"></div>
          <div class="mb-2 h-3 w-2/3 animate-pulse rounded bg-slate-100"></div>
          <div class="mt-4 h-20 w-full animate-pulse rounded bg-slate-50"></div>
        </div>
      </div>
    </div>

    <!-- Form skeleton -->
    <div *ngIf="type === 'form'" class="rounded-lg border border-slate-200 bg-white p-6">
      <div class="mb-6 h-5 w-40 animate-pulse rounded bg-slate-200"></div>
      <div *ngFor="let field of formFields" class="mb-5">
        <div class="mb-1.5 h-3 animate-pulse rounded bg-slate-200" [ngClass]="field % 2 === 0 ? 'w-24' : 'w-32'"></div>
        <div class="h-9 w-full animate-pulse rounded-md bg-slate-100"></div>
      </div>
      <div class="mt-6 flex justify-end gap-3">
        <div class="h-9 w-20 animate-pulse rounded-md bg-slate-100"></div>
        <div class="h-9 w-24 animate-pulse rounded-md bg-slate-200"></div>
      </div>
    </div>
  `,
})
export class LoadingSkeletonComponent {
  /** Determines the skeleton shape to render. */
  @Input() type: 'table' | 'card' | 'detail' | 'dashboard' | 'form' = 'table';

  /** Number of rows to display for the table skeleton. */
  @Input() rows = 5;

  /** Number of cards to display for the card/dashboard skeleton. */
  @Input() cards = 4;

  get rowArray(): number[] {
    return Array.from({ length: this.rows }, (_, i) => i);
  }

  get cardArray(): number[] {
    return Array.from({ length: this.cards }, (_, i) => i);
  }

  /** Six label/value pairs for the detail skeleton. */
  get detailPairs(): number[] {
    return [0, 1, 2, 3, 4, 5];
  }

  /** Four metric cards for the dashboard skeleton top row. */
  get metricCards(): number[] {
    return [0, 1, 2, 3];
  }

  /** Two larger cards for the dashboard skeleton bottom section. */
  get dashboardCards(): number[] {
    return [0, 1];
  }

  /** Six form fields for the form skeleton. */
  get formFields(): number[] {
    return [0, 1, 2, 3, 4, 5];
  }
}

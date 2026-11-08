import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '@oluso/ui-core';
import {
  TerminologyService,
  TenantTerminologyProfile,
} from '../../services/terminology.service';

/**
 * Industry / tenant-type tab on the core platform Settings page. The tenant's
 * industry is a first-class platform setting (backed by the Core
 * `ITenantProfileService` via `/api/admin/tenant-profile`) and is the single
 * source of truth the terminology layer consumes to pick a vocabulary pack.
 * Renaming individual labels still happens in the Terminology Center; this tab
 * only chooses the vertical.
 */
@Component({
  selector: 'admin-industry-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6 max-w-2xl">
      <div class="flex items-center justify-between">
        <div>
          <h2 class="text-lg font-semibold text-gray-900">Industry / Tenant type</h2>
          <p class="mt-1 text-sm text-gray-500">
            Sets the default vocabulary across every module — e.g. Hospitality shows
            “Wing / Room / Guest”, Education shows “Building / Room / Student”.
            Fine-tune individual words in the Terminology Center.
          </p>
        </div>
        <button
          (click)="save()"
          [disabled]="saving() || !changed()"
          class="px-4 py-2 bg-blue-600 text-white rounded-md text-sm font-medium disabled:opacity-50">
          @if (saving()) { Saving… } @else { Save }
        </button>
      </div>

      @if (error()) {
        <div class="rounded-md bg-red-50 p-3"><p class="text-sm text-red-600">{{ error() }}</p></div>
      }
      @if (saved()) {
        <div class="rounded-md bg-green-50 p-3 flex items-center gap-2">
          <ui-icon name="check-circle" class="h-4 w-4 text-green-600" />
          <p class="text-sm text-green-700">Industry updated. Labels refresh on next load.</p>
        </div>
      }

      @if (loading()) {
        <div class="text-sm text-gray-500">Loading…</div>
      } @else {
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Industry</label>
          <select
            [ngModel]="selected()"
            (ngModelChange)="onSelect($event)"
            class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm">
            <option [ngValue]="null">Generic (no industry)</option>
            @for (opt of profile()?.availableIndustries ?? []; track opt.value) {
              <option [ngValue]="opt.value">{{ opt.label }}</option>
            }
          </select>
        </div>
      }
    </div>
  `,
})
export class IndustrySettingsTabComponent implements OnInit {
  private readonly terminology = inject(TerminologyService);

  loading = signal(true);
  saving = signal(false);
  saved = signal(false);
  error = signal<string | null>(null);

  profile = signal<TenantTerminologyProfile | null>(null);
  selected = signal<string | null>(null);

  changed = computed(() => (this.profile()?.industry ?? null) !== this.selected());

  ngOnInit(): void {
    this.terminology.getProfile().subscribe({
      next: (p) => {
        this.profile.set(p);
        this.selected.set(p.industry ?? null);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load industry.');
        this.loading.set(false);
      },
    });
  }

  onSelect(value: string | null): void {
    this.selected.set(value);
    this.saved.set(false);
  }

  save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.terminology.setProfile(this.selected()).subscribe({
      next: () => {
        this.profile.update((p) => (p ? { ...p, industry: this.selected() } : p));
        this.saving.set(false);
        this.saved.set(true);
      },
      error: () => {
        this.error.set('Failed to save industry.');
        this.saving.set(false);
      },
    });
  }
}

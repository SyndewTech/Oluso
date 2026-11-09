import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { IconComponent, LocaleService } from '@syndew/oluso-ui-core';
import {
  TerminologyService,
  ResolvedTerminologyKey,
  TenantTerminologyProfile,
} from '../../services/terminology.service';

interface TerminologyGroup {
  moduleKey: string;
  group: string;
  keys: ResolvedTerminologyKey[];
}

/**
 * Terminology Center — the one central place
 * a tenant administrator renames domain vocabulary
 * across every module they're licensed for, without a developer or redeploy.
 *
 * The tenant's industry/type — which drives the inherited vocabulary — is a
 * central platform setting edited in Settings → Industry; it is shown here
 * read-only for context, with a link across to change it.
 */
@Component({
  selector: 'admin-terminology-center-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, IconComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Terminology Center</h1>
        <p class="mt-1 text-sm text-gray-500">
          Rename the words your team sees — without changing how the system works.
        </p>
      </div>

      @if (error()) {
        <div class="rounded-md bg-red-50 p-4"><p class="text-sm text-red-600">{{ error() }}</p></div>
      }

      <!-- Industry shown read-only (§2.6): the inherited vocabulary derives
           from it, but it is edited in platform Settings → Industry, not here. -->
      <div class="rounded-lg border border-gray-200 bg-gray-50 px-4 py-3 flex items-center justify-between">
        <div>
          <span class="text-sm text-gray-500">Industry:</span>
          <span class="ml-1 text-sm font-medium text-gray-900">{{ industryLabel() }}</span>
          <span class="ml-2 text-xs text-gray-400">— inherited vocabulary derives from this</span>
        </div>
        <a routerLink="/settings" [queryParams]="{ tab: 'industry' }" class="text-xs text-blue-600 hover:text-blue-800">
          Change in Settings →
        </a>
      </div>

      <!-- Search -->
      <div class="flex items-center gap-3">
        <div class="relative flex-1 max-w-md">
          <ui-icon name="magnifying-glass" class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
          <input
            type="text"
            [ngModel]="search()"
            (ngModelChange)="search.set($event)"
            placeholder="Search terms…"
            class="w-full rounded-md border border-gray-300 pl-9 pr-3 py-2 text-sm" />
        </div>
        <span class="text-xs text-gray-400">Language: {{ locale.locale() }}</span>
      </div>

      @if (loading()) {
        <div class="text-center py-12 text-gray-500">Loading…</div>
      } @else if (filteredGroups().length === 0) {
        <div class="text-center py-12 text-gray-500">No terms match your search.</div>
      } @else {
        @for (grp of filteredGroups(); track grp.moduleKey + grp.group) {
          <div class="rounded-lg border border-gray-200 overflow-hidden">
            <div class="bg-gray-50 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-gray-500">
              {{ grp.moduleKey }} › {{ grp.group }}
            </div>
            <table class="w-full text-sm">
              <thead>
                <tr class="text-left text-xs text-gray-400 border-b border-gray-100">
                  <th class="px-4 py-2 font-medium">Term</th>
                  <th class="px-4 py-2 font-medium">Inherited</th>
                  <th class="px-4 py-2 font-medium">Your label</th>
                  <th class="px-4 py-2 font-medium w-24"></th>
                </tr>
              </thead>
              <tbody>
                @for (k of grp.keys; track k.key) {
                  <tr class="border-b border-gray-50 last:border-0">
                    <td class="px-4 py-2">
                      <div class="font-medium text-gray-900">{{ k.key }}</div>
                      @if (k.description) { <div class="text-xs text-gray-400">{{ k.description }}</div> }
                    </td>
                    <td class="px-4 py-2 text-gray-500">{{ k.inheritedValue }}</td>
                    <td class="px-4 py-2">
                      <input
                        type="text"
                        [ngModel]="draft()[k.key] ?? k.overrideValue ?? ''"
                        (ngModelChange)="setDraft(k.key, $event)"
                        [disabled]="!k.isOverridable || savingKey() === k.key"
                        [placeholder]="k.isOverridable ? '(inherited)' : 'locked'"
                        class="w-full rounded-md border border-gray-200 px-2 py-1 text-sm disabled:bg-gray-50 disabled:text-gray-400" />
                    </td>
                    <td class="px-4 py-2 text-right whitespace-nowrap">
                      @if (isDirty(k)) {
                        <button (click)="save(k)" [disabled]="savingKey() === k.key"
                          class="text-xs text-blue-600 hover:text-blue-800 font-medium">Save</button>
                      } @else if (k.overrideValue) {
                        <button (click)="reset(k)" [disabled]="savingKey() === k.key"
                          class="text-xs text-gray-500 hover:text-gray-700">↩ Reset</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
})
export class TerminologyCenterPageComponent implements OnInit, OnDestroy {
  private readonly terminology = inject(TerminologyService);
  private readonly destroy$ = new Subject<void>();
  protected readonly locale = inject(LocaleService);

  loading = signal(true);
  error = signal<string | null>(null);
  savingKey = signal<string | null>(null);
  search = signal('');

  // Industry is read-only here — edited in platform Settings → Industry.
  profile = signal<TenantTerminologyProfile | null>(null);

  private keys = signal<ResolvedTerminologyKey[]>([]);
  draft = signal<Record<string, string>>({});

  industryLabel = computed(() => {
    const p = this.profile();
    if (!p?.industry) return 'Generic';
    return p.availableIndustries.find((o) => o.value === p.industry)?.label ?? p.industry;
  });

  private groups = computed<TerminologyGroup[]>(() => {
    const byGroup = new Map<string, TerminologyGroup>();
    for (const k of this.keys()) {
      const id = `${k.moduleKey}|${k.group}`;
      let g = byGroup.get(id);
      if (!g) {
        g = { moduleKey: k.moduleKey, group: k.group, keys: [] };
        byGroup.set(id, g);
      }
      g.keys.push(k);
    }
    return [...byGroup.values()];
  });

  filteredGroups = computed<TerminologyGroup[]>(() => {
    const term = this.search().trim().toLowerCase();
    if (!term) return this.groups();
    return this.groups()
      .map((g) => ({
        ...g,
        keys: g.keys.filter(
          (k) =>
            k.key.toLowerCase().includes(term) ||
            k.inheritedValue.toLowerCase().includes(term) ||
            (k.description?.toLowerCase().includes(term) ?? false),
        ),
      }))
      .filter((g) => g.keys.length > 0);
  });

  ngOnInit(): void {
    // Industry is read-only context here; the editor lives in Settings.
    this.terminology.getProfile().pipe(takeUntil(this.destroy$)).subscribe({
      next: (p) => this.profile.set(p),
      error: () => { /* non-fatal; keys still load */ },
    });
    this.loadKeys();
  }

  private loadKeys(): void {
    this.loading.set(true);
    this.error.set(null);
    this.terminology.getKeys(this.locale.locale()).pipe(takeUntil(this.destroy$)).subscribe({
      next: (keys) => {
        this.keys.set(keys);
        this.draft.set({});
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load terminology.');
        this.loading.set(false);
      },
    });
  }

  setDraft(key: string, value: string): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  isDirty(k: ResolvedTerminologyKey): boolean {
    const current = this.draft()[k.key];
    if (current === undefined) return false;
    const trimmed = current.trim();
    return trimmed.length > 0 && trimmed !== (k.overrideValue ?? '');
  }

  save(k: ResolvedTerminologyKey): void {
    const value = (this.draft()[k.key] ?? '').trim();
    if (!value) return;
    this.savingKey.set(k.key);
    this.terminology.setOverride(k.key, value, this.locale.locale()).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => { this.applyLocal(k.key, value); this.savingKey.set(null); },
      error: () => { this.error.set('Failed to save override.'); this.savingKey.set(null); },
    });
  }

  reset(k: ResolvedTerminologyKey): void {
    this.savingKey.set(k.key);
    this.terminology.resetOverride(k.key, this.locale.locale()).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => { this.applyLocal(k.key, undefined); this.savingKey.set(null); },
      error: () => { this.error.set('Failed to reset override.'); this.savingKey.set(null); },
    });
  }

  private applyLocal(key: string, overrideValue: string | undefined): void {
    this.keys.update((list) =>
      list.map((k) => (k.key === key ? { ...k, overrideValue: overrideValue ?? null } : k)),
    );
    this.draft.update((d) => {
      const next = { ...d };
      delete next[key];
      return next;
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}

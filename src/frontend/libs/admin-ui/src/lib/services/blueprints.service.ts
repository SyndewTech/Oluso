import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

/** One row of the tenant's blueprint list (matches backend BlueprintListEntry). */
export interface BlueprintListEntry {
  key: string;
  moduleKey: string;
  title: string;
  description: string;
  /** Item count per kind, e.g. { noteTemplate: 6, orderSet: 5, workflow: 4 }. */
  itemCounts: Record<string, number>;
  /** When this blueprint was last applied for the tenant (null = never). */
  appliedAt?: string | null;
  appliedBy?: string | null;
}

/** One item a blueprint touched (or deliberately did not). */
export interface BlueprintItemOutcome {
  kind: string;
  name: string;
}

/** The outcome of applying one blueprint (matches backend BlueprintApplyResult). */
export interface BlueprintApplyResult {
  key: string;
  created: BlueprintItemOutcome[];
  skipped: BlueprintItemOutcome[];
}

/**
 * Client for the platform blueprint surface (`/api/admin/blueprints`).
 * Module-agnostic — the backend aggregates every licensed module's starter
 * packs (contributor pattern), so this service has no per-module knowledge.
 */
@Injectable({ providedIn: 'root' })
export class BlueprintsService {
  private readonly api = inject(ApiService);
  private readonly endpoint = '/blueprints';

  /** The starter packs available to this tenant, with applied-state receipts. */
  list(): Observable<BlueprintListEntry[]> {
    return this.api.get<BlueprintListEntry[]>(this.endpoint);
  }

  /**
   * Apply one blueprint. Idempotent: existing same-named items are skipped
   * unless `overwrite` is set (overwritten automations return to draft).
   */
  apply(key: string, overwrite = false): Observable<BlueprintApplyResult> {
    return this.api.post<BlueprintApplyResult>(
      `${this.endpoint}/${encodeURIComponent(key)}/apply`,
      { overwrite },
    );
  }
}

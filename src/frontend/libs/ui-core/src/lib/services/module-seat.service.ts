import { Injectable, signal, computed } from '@angular/core';

/**
 * Module Seat Service
 *
 * Holds the current user's per-module access seats. Mirrors the
 * <see cref="FeatureService"/> shape but for "this user is allowed to
 * use module X" rather than "the tenant has module X licensed".
 *
 * <para>Workspace-ui's <c>WorkspaceAuthService</c> populates this on
 * profile load. Plugin slot / app / nav filters consult it via
 * <see cref="hasSeat"/> so module visibility flows from a single
 * source — the same seat list the backend's token claim and
 * <see cref="RequireModuleSeat"/> attribute use.</para>
 */
@Injectable({
  providedIn: 'root',
})
export class ModuleSeatService {
  private readonly _seats = signal<Set<string>>(new Set());
  private readonly _isLoaded = signal<boolean>(false);

  /** Read-only seat list, lowercase-normalised. */
  readonly seats = computed(() => Array.from(this._seats()));

  /** True once setSeats has been called at least once. */
  readonly isLoaded = this._isLoaded.asReadonly();

  /** Replace the current seat set. Called after profile load. */
  setSeats(featureKeys: string[]): void {
    const normalised = new Set(featureKeys.map((k) => k.toLowerCase()));
    this._seats.set(normalised);
    this._isLoaded.set(true);
  }

  /** Drop all seats — on logout. */
  clear(): void {
    this._seats.set(new Set());
    this._isLoaded.set(false);
  }

  /** True when the user holds a seat for the given module. */
  hasSeat(featureKey: string): boolean {
    return this._seats().has(featureKey.toLowerCase());
  }
}

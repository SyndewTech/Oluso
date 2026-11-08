import { Injectable, signal, computed } from '@angular/core';

/**
 * Feature Service
 *
 * Manages feature flags for the application.
 * Supports tenant-specific features and global features.
 *
 * This mirrors the featureService from the React codebase.
 */
@Injectable({
  providedIn: 'root',
})
export class FeatureService {
  private readonly _features = signal<Map<string, boolean>>(new Map());
  private readonly _tenantFeatures = signal<Map<string, boolean>>(new Map());
  private readonly _isLoading = signal<boolean>(false);
  private readonly _isLoaded = signal<boolean>(false);

  /** Read-only access to features */
  readonly features = computed(() => {
    const result: Record<string, boolean> = {};
    this._features().forEach((value, key) => {
      result[key] = value;
    });
    return result;
  });

  readonly tenantFeatures = computed(() => {
    const result: Record<string, boolean> = {};
    this._tenantFeatures().forEach((value, key) => {
      result[key] = value;
    });
    return result;
  });

  readonly isLoading = this._isLoading.asReadonly();
  readonly isLoaded = this._isLoaded.asReadonly();

  /**
   * Set global features from API response
   */
  setFeatures(features: Record<string, boolean>): void {
    const featureMap = new Map(Object.entries(features));
    this._features.set(featureMap);
    this._isLoaded.set(true);
    this._isLoading.set(false);
  }

  /**
   * Set tenant-specific features
   */
  setTenantFeatures(features: Record<string, boolean>): void {
    const featureMap = new Map(Object.entries(features));
    this._tenantFeatures.set(featureMap);
  }

  /**
   * Enable a specific feature
   */
  enableFeature(feature: string): void {
    this._features.update((map) => {
      const newMap = new Map(map);
      newMap.set(feature, true);
      return newMap;
    });
  }

  /**
   * Disable a specific feature
   */
  disableFeature(feature: string): void {
    this._features.update((map) => {
      const newMap = new Map(map);
      newMap.set(feature, false);
      return newMap;
    });
  }

  /**
   * Check if a feature is enabled
   * Tenant features take precedence over global features
   */
  hasFeature(feature: string): boolean {
    // Check tenant features first
    if (this._tenantFeatures().has(feature)) {
      return this._tenantFeatures().get(feature) === true;
    }

    // Fall back to global features
    return this._features().get(feature) === true;
  }

  /**
   * Check if any of the specified features is enabled
   */
  hasAnyFeature(features: string[]): boolean {
    return features.some((feature) => this.hasFeature(feature));
  }

  /**
   * Check if all of the specified features are enabled
   */
  hasAllFeatures(features: string[]): boolean {
    return features.every((feature) => this.hasFeature(feature));
  }

  /**
   * Set loading state
   */
  setLoading(isLoading: boolean): void {
    this._isLoading.set(isLoading);
  }

  /**
   * Clear all features (on logout)
   */
  clear(): void {
    this._features.set(new Map());
    this._tenantFeatures.set(new Map());
    this._isLoaded.set(false);
  }
}

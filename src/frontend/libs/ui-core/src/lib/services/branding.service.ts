import { Injectable } from '@angular/core';

export interface AppBranding {
  name: string;
  description?: string;
}

export interface BrandingConfig {
  productName?: string;
  companyName?: string;
  logoUrl?: string;
  apps?: Record<string, AppBranding>;
}

/**
 * Reads branding overrides from the runtime config (window.__OLUSO_*_CONFIG__.branding).
 * Any consumer can override module names and descriptions via config.js.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly branding: BrandingConfig;

  constructor() {
    const win = typeof window !== 'undefined' ? window : undefined;
    const config =
      (win as any)?.__OLUSO_WORKSPACE_CONFIG__?.branding ??
      (win as any)?.__OLUSO_CONFIG__?.branding ??
      {};
    this.branding = config;
  }

  get productName(): string {
    return this.branding.productName || 'Oluso';
  }

  get companyName(): string {
    return this.branding.companyName || 'Oluso';
  }

  get logoUrl(): string | undefined {
    return this.branding.logoUrl;
  }

  getAppName(appId: string, fallback: string): string {
    return this.branding.apps?.[appId]?.name || fallback;
  }

  getAppDescription(appId: string, fallback: string): string {
    return this.branding.apps?.[appId]?.description || fallback;
  }
}

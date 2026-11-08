import { InjectionToken } from '@angular/core';

/**
 * Display name shown in the admin sidebar logo and the mobile header.
 * Host apps override at bootstrap to surface their own brand
 * (e.g. "Acme" / "Acme Admin"). Defaults to
 * "Oluso" / "Oluso Admin" for the Oluso reference admin app.
 */
export interface AdminBrandConfig {
  /** Sidebar logo wordmark. */
  name: string;
  /** Mobile header label, typically `${name} Admin`. */
  shortLabel: string;
}

export const ADMIN_BRAND = new InjectionToken<AdminBrandConfig>('ADMIN_BRAND', {
  providedIn: 'root',
  factory: () => ({ name: 'Oluso', shortLabel: 'Oluso Admin' }),
});

import { InjectionToken } from '@angular/core';

/**
 * One filterable mode in the admin sidebar's mode-switch tabs. Selecting a
 * mode hides nav items that don't match its filter, so platform admins and
 * suite admins each see only the half of the nav that's relevant to them.
 */
export interface AdminNavMode {
  /** Stable id used for localStorage persistence. */
  id: string;
  /** Tab label shown above the nav. */
  label: string;
  /**
   * Heroicon name shown left of the label in the tab. Pick something that
   * telegraphs the mode at a glance — `cog` for platform admin, `squares-2x2`
   * for a modules collective, etc.
   */
  icon?: string;
  /**
   * Raw inline SVG markup to render in the tab — overrides `icon` when set.
   * Used by host apps with their own brand mark. Trusted by the layout
   * via DomSanitizer.bypassSecurityTrustHtml — only set this from your own
   * app config, never from user input.
   */
  iconHtml?: string;
  /**
   * Which items show in this mode:
   * - `'all'`: every visible item (legacy, single-mode behaviour)
   * - `'grouped'`: only items with a `group` set (the brand modules)
   * - `'ungrouped'`: only items without a `group` (platform-level admin)
   */
  filter: 'all' | 'grouped' | 'ungrouped';
  /**
   * Optional master section header rendered above the nav when this mode is
   * active - e.g. "Suite" above all the brand-grouped items.
   */
  header?: string;
}

export interface AdminNavModesConfig {
  modes: AdminNavMode[];
  /** Mode id used on first load when nothing is in localStorage. */
  defaultMode?: string;
  /** localStorage key for persistence. Defaults to `oluso.admin.navMode`. */
  storageKey?: string;
}

/**
 * Default Platform/Modules dual-nav. Tabs only render when at least one
 * registered nav item carries a `group` (i.e. a plugin opted into the
 * suite mode); otherwise the sidebar collapses to a single combined nav.
 *
 * Host apps with their own brand override this token wholesale (e.g.
 * 'Platform' / 'Suite'). Reference admins get the generic split for
 * free as soon as they tag plugins with `navGroup: 'Modules'`.
 */
export const ADMIN_NAV_MODES = new InjectionToken<AdminNavModesConfig | null>(
  'ADMIN_NAV_MODES',
  {
    providedIn: 'root',
    factory: () => ({
      modes: [
        { id: 'platform', label: 'Platform', filter: 'ungrouped', icon: 'cog' },
        { id: 'modules', label: 'Modules', filter: 'grouped', header: 'Modules', icon: 'squares-2x2' },
      ],
      defaultMode: 'platform',
    }),
  },
);

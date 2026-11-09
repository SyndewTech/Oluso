export interface NavItem {
  /** Unique identifier for this nav item (used for merging plugin items) */
  id?: string;
  label: string;
  icon?: string;
  path: string;
  children?: NavItem[];
  permission?: string;
  feature?: string;
  /**
   * Account portal only: a "{module}.{flag}" path into the account profile's
   * extensions (e.g. "emr.isPatient"). The item is shown only when that flag is
   * true for the signed-in user, so module self-service pages appear just for
   * the people they apply to. Combine with `feature` for the tenant-level gate.
   */
  requires?: string;
  badge?: string;
  order?: number;
  superAdminOnly?: boolean;
  /** Only visible to org admins (non-super-admins) */
  orgAdminOnly?: boolean;
  /**
   * If specified, this nav item's children will be merged into the existing
   * nav item with matching path. The parent item itself won't be shown.
   * Use this for plugins that want to add items under existing menus.
   */
  mergeIntoPath?: string;
  /**
   * Visual group label rendered as a section header above this item. Items
   * sharing the same group value are clustered under the same header. Lets
   * host apps surface their own brand language (e.g. grouping related items
   * under a custom section header) without nesting items.
   */
  group?: string;
}

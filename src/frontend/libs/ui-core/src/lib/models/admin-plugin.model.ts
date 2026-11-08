import { Route } from '@angular/router';
import { BasePlugin } from './base-plugin.model';
import { NavItem } from './nav-item.model';
import { DashboardWidget } from './dashboard-widget.model';
import { PageSlot } from './page-slot.model';
import { TabDefinition } from './tab-definition.model';
import { ActionButton } from './action-button.model';
import { DetailSection } from './detail-section.model';

export interface AdminPlugin extends BasePlugin {
  routes?: Route[];
  navItems?: NavItem[];
  dashboardWidgets?: DashboardWidget[];
  pageSlots?: PageSlot[];
  userDetailTabs?: TabDefinition[];
  tenantSettingsTabs?: TabDefinition[];
  settingsTabs?: TabDefinition[];
  actionButtons?: ActionButton[];
  detailSections?: DetailSection[];
}

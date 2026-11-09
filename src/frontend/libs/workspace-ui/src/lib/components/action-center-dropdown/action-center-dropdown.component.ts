import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { IconComponent } from '@syndew/oluso-ui-core';
import { ActionCenterService } from '../../services/action-center.service';
import { AppSwitcherService } from '../../services/app-switcher.service';
import { ActionItem } from '../../models';

@Component({
  selector: 'workspace-action-center-dropdown',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="absolute right-0 mt-2 w-96 max-h-[32rem] bg-white rounded-xl shadow-lg border border-gray-200 z-50 flex flex-col overflow-hidden">
      <!-- Header -->
      <div class="px-4 py-3 border-b border-gray-100 flex items-center justify-between shrink-0">
        <h3 class="text-sm font-semibold text-gray-900">Action Center</h3>
        @if (actionCenter.count() > 0) {
          <span class="text-xs font-medium text-gray-500">{{ actionCenter.count() }} pending</span>
        }
      </div>

      <!-- Content -->
      <div class="overflow-y-auto flex-1">
        @if (actionCenter.loading()) {
          <div class="flex items-center justify-center py-8">
            <div class="h-5 w-5 border-2 border-gray-300 border-t-primary-500 rounded-full animate-spin"></div>
          </div>
        } @else if (actionCenter.items(); as result) {
          @if (result.groups.length === 0) {
            <div class="flex flex-col items-center justify-center py-8 px-4 text-center">
              <ui-icon name="check-circle" class="h-8 w-8 text-green-400 mb-2" />
              <p class="text-sm font-medium text-gray-700">All caught up!</p>
              <p class="text-xs text-gray-500 mt-1">No items need your attention</p>
            </div>
          } @else {
            @for (group of result.groups; track group.providerId) {
              <div class="border-b border-gray-50 last:border-b-0">
                <!-- Group Header -->
                <div class="px-4 py-2 bg-gray-50/80 flex items-center justify-between">
                  <div class="flex items-center gap-2">
                    <ui-icon [name]="group.icon" class="h-4 w-4 text-gray-500" />
                    <span class="text-xs font-semibold text-gray-600 uppercase tracking-wide">{{ group.displayName }}</span>
                  </div>
                  <span class="text-xs font-medium text-gray-400">{{ group.count }}</span>
                </div>

                <!-- Group Items -->
                @for (item of group.items.slice(0, 5); track item.id) {
                  <button
                    type="button"
                    class="w-full text-left px-4 py-2.5 hover:bg-gray-50 transition-colors flex items-start gap-3"
                    (click)="navigateTo(item)"
                  >
                    <!-- Priority indicator -->
                    <div
                      class="mt-1 h-2 w-2 rounded-full shrink-0"
                      [class.bg-red-500]="item.priority === 'Urgent' || item.status === 'Overdue'"
                      [class.bg-orange-400]="item.priority === 'High' && item.status !== 'Overdue'"
                      [class.bg-blue-400]="item.priority === 'Normal' && item.status !== 'Overdue'"
                      [class.bg-gray-300]="item.priority === 'Low' && item.status !== 'Overdue'"
                    ></div>
                    <div class="min-w-0 flex-1">
                      <p class="text-sm font-medium text-gray-900 truncate">{{ item.title }}</p>
                      @if (item.subtitle) {
                        <p class="text-xs text-gray-500 truncate mt-0.5">{{ item.subtitle }}</p>
                      }
                      @if (item.scope === 'AdminOnly') {
                        <p class="text-[10px] text-indigo-500 mt-0.5">Opens in admin area →</p>
                      }
                    </div>
                    <span class="text-[10px] text-gray-400 shrink-0 mt-0.5">{{ getTimeAgo(item.createdAt) }}</span>
                  </button>
                }

                <!-- View all link when more than 5 -->
                @if (group.items.length > 5) {
                  <button
                    type="button"
                    class="w-full text-center px-4 py-2 text-xs font-medium text-primary-600 hover:text-primary-700 hover:bg-gray-50 transition-colors"
                    (click)="navigateToGroup(group.workspaceActionUrl)"
                  >
                    View all {{ group.count }} {{ group.displayName | lowercase }}
                  </button>
                }
              </div>
            }
          }
        }
      </div>
    </div>
  `,
})
export class ActionCenterDropdownComponent {
  readonly actionCenter = inject(ActionCenterService);
  private readonly router = inject(Router);
  private readonly appSwitcher = inject(AppSwitcherService);

  navigateTo(item: ActionItem): void {
    this.actionCenter.close();
    if (item.scope === 'AdminOnly') {
      const url = item.adminActionUrl ?? item.actionUrl;
      if (url) window.location.href = this.appSwitcher.getAdminUrl() + url;
    } else if (item.workspaceActionUrl) {
      this.router.navigateByUrl(item.workspaceActionUrl);
    } else if (item.actionUrl) {
      this.router.navigateByUrl(item.actionUrl);
    }
  }

  navigateToGroup(workspaceActionUrl?: string): void {
    this.actionCenter.close();
    if (workspaceActionUrl) {
      this.router.navigateByUrl(workspaceActionUrl);
    }
  }

  getTimeAgo(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60_000);
    const diffHours = Math.floor(diffMs / 3_600_000);
    const diffDays = Math.floor(diffMs / 86_400_000);

    if (diffMins < 1) return 'now';
    if (diffMins < 60) return `${diffMins}m`;
    if (diffHours < 24) return `${diffHours}h`;
    if (diffDays < 7) return `${diffDays}d`;
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }
}

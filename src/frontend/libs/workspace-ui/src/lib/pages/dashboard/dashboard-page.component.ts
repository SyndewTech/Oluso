import { Component, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { IconComponent, PageSlotHostComponent } from '@syndew/oluso-ui-core';
import { WorkspaceAuthService } from '../../services/workspace-auth.service';
import { WorkspacePluginService } from '../../services/workspace-plugin.service';
@Component({
  selector: 'workspace-dashboard-page',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent, PageSlotHostComponent],
  template: `
    <div class="space-y-6">
      <!-- Welcome Section -->
      <div class="bg-white rounded-lg shadow p-6">
        <div class="flex items-center justify-between">
          <div>
            <h1 class="text-2xl font-bold text-gray-900">
              Welcome back, {{ authService.user()?.firstName || authService.displayName() }}!
            </h1>
            <p class="text-gray-600 mt-1">
              @if (authService.isManager()) {
                Here's what's happening with your team today.
              } @else {
                Here's your workspace overview.
              }
            </p>
          </div>
          @if (authService.user()?.profilePictureUrl) {
            <img
              [src]="authService.user()?.profilePictureUrl"
              alt="Profile"
              class="h-16 w-16 rounded-full object-cover"
            />
          } @else {
            <div class="h-16 w-16 rounded-full bg-primary-100 flex items-center justify-center">
              <span class="text-2xl font-semibold text-primary-700">
                {{ getInitials() }}
              </span>
            </div>
          }
        </div>
      </div>

      <!-- Module-contributed dashboard cards. Each module registers a
           PageSlot for pageId="workspace-dashboard" / slotId="cards" with
           its own feature + requiresSeat — users without access never
           mount the component, so their endpoints never get hit. HR's
           contribution renders the stat tiles + recent activity panel
           that used to live here. -->
      <ui-page-slot-host pageId="workspace-dashboard" slotId="cards" />

      <!-- Quick Actions -->
      <div class="bg-white rounded-lg shadow p-6">
        <h2 class="text-lg font-semibold text-gray-900 mb-4">Quick Actions</h2>
        <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
          @for (action of quickActions(); track action.id) {
            <a
              [routerLink]="action.path"
              class="flex flex-col items-center p-4 rounded-lg border border-gray-200 hover:border-primary-300 hover:bg-primary-50 transition-colors"
            >
              <div class="p-2 bg-gray-100 rounded-lg mb-2">
                <ui-icon [name]="action.icon" class="h-6 w-6 text-gray-600" />
              </div>
              <span class="text-sm font-medium text-gray-700 text-center">{{ action.label }}</span>
            </a>
          }
        </div>
      </div>

      <!-- Plugin Widgets -->
      @for (widget of pluginService.getWidgetsBySlot('content'); track widget.id) {
        <ng-container *ngComponentOutlet="widget.component"></ng-container>
      }
    </div>
  `,
})
export class DashboardPageComponent {
  readonly authService = inject(WorkspaceAuthService);
  readonly pluginService = inject(WorkspacePluginService);

  // Quick actions from plugins (limited to 6)
  readonly quickActions = computed(() => this.pluginService.quickActions().slice(0, 6));

  getInitials(): string {
    const user = this.authService.user();
    if (!user) return '?';
    const first = user.firstName?.[0] || user.displayName?.[0] || '';
    const last = user.lastName?.[0] || '';
    return (first + last).toUpperCase() || '?';
  }
}

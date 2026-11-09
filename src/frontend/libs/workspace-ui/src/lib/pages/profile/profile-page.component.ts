import { Component, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WorkspaceAuthService } from '../../services/workspace-auth.service';
import { CardComponent, PageSlotHostComponent } from '@syndew/oluso-ui-core';

@Component({
  selector: 'workspace-profile-page',
  standalone: true,
  imports: [CommonModule, CardComponent, PageSlotHostComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">My Profile</h1>
        <p class="text-gray-600 mt-1">Your account information</p>
      </div>

      @if (authService.isLoading()) {
        <div class="flex items-center justify-center py-12">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else if (user()) {
        <!-- Profile Header -->
        <div class="bg-white rounded-lg shadow overflow-hidden">
          <div class="bg-gradient-to-r from-primary-600 to-primary-700 px-6 py-8">
            <div class="flex items-center gap-6">
              @if (user()?.profilePictureUrl) {
                <img
                  [src]="user()?.profilePictureUrl"
                  alt="Profile"
                  class="h-24 w-24 rounded-full object-cover border-4 border-white"
                />
              } @else {
                <div class="h-24 w-24 rounded-full bg-white flex items-center justify-center border-4 border-white">
                  <span class="text-3xl font-semibold text-primary-700">
                    {{ getInitials() }}
                  </span>
                </div>
              }
              <div class="text-white">
                <h2 class="text-2xl font-bold">{{ user()?.displayName }}</h2>
                <p class="text-primary-100">{{ user()?.email }}</p>
              </div>
            </div>
          </div>
        </div>

        <!-- Profile Details -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
          <!-- Account Information -->
          <ui-card title="Account Information">
            <div class="space-y-4">
              <div>
                <label class="text-sm font-medium text-gray-500">Display Name</label>
                <p class="text-gray-900">{{ user()?.displayName }}</p>
              </div>
              @if (user()?.firstName || user()?.lastName) {
                <div>
                  <label class="text-sm font-medium text-gray-500">Full Name</label>
                  <p class="text-gray-900">{{ user()?.firstName }} {{ user()?.lastName }}</p>
                </div>
              }
              <div>
                <label class="text-sm font-medium text-gray-500">Username</label>
                <p class="text-gray-900">{{ user()?.userName }}</p>
              </div>
              <div>
                <label class="text-sm font-medium text-gray-500">Email</label>
                <p class="text-gray-900">{{ user()?.email }}</p>
              </div>
            </div>
          </ui-card>

          <!-- Roles -->
          <ui-card title="Roles">
            <div class="space-y-4">
              @if (user()?.isManager) {
                <div class="mb-2">
                  <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-purple-100 text-purple-800">
                    Manager
                  </span>
                </div>
              }
              @if (user()?.roles && user()!.roles.length > 0) {
                <div class="flex flex-wrap gap-2">
                  @for (role of user()?.roles; track role) {
                    <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-primary-100 text-primary-800">
                      {{ role }}
                    </span>
                  }
                </div>
              } @else {
                <p class="text-sm text-gray-500">No roles assigned</p>
              }
            </div>
          </ui-card>

          <!-- Plugin-provided profile cards -->
          <ui-page-slot-host
            pageId="workspace-profile"
            slotId="cards"
            [data]="slotData()"
          />
        </div>
      } @else {
        <div class="text-center py-12">
          <p class="text-gray-500">Unable to load profile information</p>
        </div>
      }
    </div>
  `,
})
export class ProfilePageComponent {
  readonly authService = inject(WorkspaceAuthService);
  readonly user = this.authService.user;

  readonly slotData = computed<Record<string, unknown>>(() => ({
    user: this.user(),
    extensions: this.user()?.extensions ?? {},
  }));

  getInitials(): string {
    const u = this.user();
    if (!u) return '?';
    const first = u.firstName?.[0] || u.displayName?.[0] || '';
    const last = u.lastName?.[0] || '';
    return (first + last).toUpperCase() || '?';
  }
}

import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { IconComponent } from '@oluso/ui-core';
import { Fido2Service } from '../../services/fido2.service';
import { Fido2Stats } from '../../models/fido2.model';

@Component({
  selector: 'fido2-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-bold text-gray-900">FIDO2 / WebAuthn Dashboard</h1>
      </div>

      @if (loading()) {
        <div class="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="bg-gray-200 h-24 rounded-lg animate-pulse"></div>
          }
        </div>
      } @else if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4">
          <p class="text-red-700">{{ error() }}</p>
        </div>
      } @else {
        <!-- Stats Cards -->
        <div class="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
          <!-- Total Credentials -->
          <div class="bg-white overflow-hidden shadow rounded-lg">
            <div class="p-5">
              <div class="flex items-center">
                <div class="flex-shrink-0">
                  <ui-icon name="fingerprint" class="h-8 w-8 text-primary-600" />
                </div>
                <div class="ml-4 flex-1">
                  <p class="text-sm font-medium text-gray-500">Total Credentials</p>
                  <p class="text-2xl font-semibold text-gray-900">{{ stats()?.totalCredentials ?? 0 }}</p>
                </div>
              </div>
            </div>
          </div>

          <!-- Users with Passkeys -->
          <div class="bg-white overflow-hidden shadow rounded-lg">
            <div class="p-5">
              <div class="flex items-center">
                <div class="flex-shrink-0">
                  <ui-icon name="users" class="h-8 w-8 text-primary-600" />
                </div>
                <div class="ml-4 flex-1">
                  <p class="text-sm font-medium text-gray-500">Users with Passkeys</p>
                  <p class="text-2xl font-semibold text-gray-900">{{ stats()?.totalUsersWithCredentials ?? 0 }}</p>
                </div>
              </div>
            </div>
          </div>

          <!-- Platform Authenticators -->
          <div class="bg-white overflow-hidden shadow rounded-lg">
            <div class="p-5">
              <div class="flex items-center">
                <div class="flex-shrink-0">
                  <ui-icon name="device-phone-mobile" class="h-8 w-8 text-primary-600" />
                </div>
                <div class="ml-4 flex-1">
                  <p class="text-sm font-medium text-gray-500">Platform Authenticators</p>
                  <p class="text-2xl font-semibold text-gray-900">{{ stats()?.platformCredentials ?? 0 }}</p>
                  <p class="mt-1 text-sm text-gray-500">Built-in (Touch ID, Windows Hello)</p>
                </div>
              </div>
            </div>
          </div>

          <!-- Security Keys -->
          <div class="bg-white overflow-hidden shadow rounded-lg">
            <div class="p-5">
              <div class="flex items-center">
                <div class="flex-shrink-0">
                  <ui-icon name="key" class="h-8 w-8 text-primary-600" />
                </div>
                <div class="ml-4 flex-1">
                  <p class="text-sm font-medium text-gray-500">Security Keys</p>
                  <p class="text-2xl font-semibold text-gray-900">{{ stats()?.crossPlatformCredentials ?? 0 }}</p>
                  <p class="mt-1 text-sm text-gray-500">External (YubiKey, etc.)</p>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Bottom Section -->
        <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <!-- Recent Activity -->
          <div class="bg-white overflow-hidden shadow rounded-lg">
            <div class="px-4 py-5 sm:p-6">
              <h3 class="text-lg font-medium text-gray-900">Recent Activity</h3>
              <div class="mt-4">
                <dl class="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div>
                    <dt class="text-sm font-medium text-gray-500">Registered (Last 30 Days)</dt>
                    <dd class="mt-1 text-2xl font-semibold text-gray-900">
                      {{ stats()?.credentialsRegisteredLast30Days ?? 0 }}
                    </dd>
                  </div>
                  <div>
                    <dt class="text-sm font-medium text-gray-500">Used (Last 30 Days)</dt>
                    <dd class="mt-1 text-2xl font-semibold text-gray-900">
                      {{ stats()?.credentialsUsedLast30Days ?? 0 }}
                    </dd>
                  </div>
                </dl>
              </div>
            </div>
          </div>

          <!-- Quick Actions -->
          <div class="bg-white overflow-hidden shadow rounded-lg">
            <div class="px-4 py-5 sm:p-6">
              <h3 class="text-lg font-medium text-gray-900">Quick Actions</h3>
              <div class="mt-4 space-y-3">
                <a
                  routerLink="/fido2/credentials"
                  class="block w-full rounded-md border border-gray-300 bg-white px-4 py-2 text-center text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50"
                >
                  View All Credentials
                </a>
                <a
                  routerLink="/users"
                  class="block w-full rounded-md border border-gray-300 bg-white px-4 py-2 text-center text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50"
                >
                  Manage Users
                </a>
              </div>
            </div>
          </div>
        </div>
      }
    </div>
  `,
})
export class Fido2DashboardComponent implements OnInit {
  private readonly fido2Service = inject(Fido2Service);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly stats = signal<Fido2Stats | null>(null);

  ngOnInit(): void {
    this.loadStats();
  }

  loadStats(): void {
    this.loading.set(true);
    this.fido2Service.getStats().subscribe({
      next: (data) => {
        this.stats.set(data);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading FIDO2 stats:', err);
        this.error.set('Failed to load FIDO2 statistics');
        this.loading.set(false);
      },
    });
  }
}

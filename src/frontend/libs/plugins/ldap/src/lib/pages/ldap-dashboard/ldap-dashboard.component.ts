import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { IconComponent } from '@oluso/ui-core';
import { LdapService } from '../../services/ldap.service';
import { LdapServerInfo } from '../../models/ldap.model';

@Component({
  selector: 'ldap-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent],
  template: `
    <div class="p-6 space-y-6">
      <!-- Header -->
      <div>
        <h1 class="text-2xl font-bold text-gray-900">LDAP Server</h1>
        <p class="mt-1 text-sm text-gray-500">
          Expose users via LDAP protocol for legacy application integration
        </p>
      </div>

      <!-- Loading State -->
      @if (loading()) {
        <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 bg-gray-200 rounded-lg animate-pulse"></div>
          }
        </div>
      }

      <!-- Error State -->
      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ui-icon name="exclamation-circle" class="h-5 w-5 text-red-500" />
          <p class="text-red-700">{{ error() }}</p>
        </div>
      }

      <!-- Content -->
      @if (!loading() && !error() && serverInfo()) {
        <!-- Status Cards -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
          <!-- Server Status -->
          <div class="bg-white rounded-lg shadow p-6">
            <div class="flex items-center">
              <div class="p-3 rounded-full" [class]="serverInfo()?.enabled ? 'bg-green-100' : 'bg-gray-100'">
                <ui-icon name="server" [class]="'h-6 w-6 ' + (serverInfo()?.enabled ? 'text-green-600' : 'text-gray-400')" />
              </div>
              <div class="ml-4">
                <p class="text-sm font-medium text-gray-500">Server Status</p>
                <div class="flex items-center mt-1">
                  @if (serverInfo()?.enabled) {
                    <ui-icon name="check-circle" class="h-5 w-5 text-green-500 mr-1" />
                    <span class="text-lg font-semibold text-green-600">Enabled</span>
                  } @else {
                    <ui-icon name="x-circle" class="h-5 w-5 text-gray-400 mr-1" />
                    <span class="text-lg font-semibold text-gray-500">Disabled</span>
                  }
                </div>
              </div>
            </div>
          </div>

          <!-- Port Info -->
          <div class="bg-white rounded-lg shadow p-6">
            <div class="flex items-center">
              <div class="p-3 rounded-full bg-blue-100">
                <ui-icon name="server" class="h-6 w-6 text-blue-600" />
              </div>
              <div class="ml-4">
                <p class="text-sm font-medium text-gray-500">Listening Port</p>
                <p class="text-lg font-semibold text-gray-900">
                  {{ serverInfo()?.port || 389 }}
                  @if (serverInfo()?.enableSsl) {
                    <span> / {{ serverInfo()?.sslPort }} (SSL)</span>
                  }
                </p>
              </div>
            </div>
          </div>

          <!-- Directory Info -->
          <div class="bg-white rounded-lg shadow p-6">
            <div class="flex items-center">
              <div class="p-3 rounded-full bg-purple-100">
                <ui-icon name="users" class="h-6 w-6 text-purple-600" />
              </div>
              <div class="ml-4">
                <p class="text-sm font-medium text-gray-500">Base DN</p>
                <p class="text-sm font-mono text-gray-900 truncate max-w-xs" [title]="serverInfo()?.baseDn">
                  {{ serverInfo()?.baseDn || 'Not configured' }}
                </p>
              </div>
            </div>
          </div>
        </div>

        <!-- Management Section -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
          <!-- Service Accounts -->
          <a routerLink="/ldap/service-accounts"
             class="bg-white rounded-lg shadow p-6 hover:shadow-md transition-shadow group">
            <div class="flex items-center justify-between">
              <div class="flex items-center">
                <div class="p-3 rounded-full bg-indigo-100">
                  <ui-icon name="key" class="h-6 w-6 text-indigo-600" />
                </div>
                <div class="ml-4">
                  <p class="text-lg font-medium text-gray-900">Service Accounts</p>
                  <p class="text-sm text-gray-500">
                    Manage LDAP service accounts for programmatic access
                  </p>
                </div>
              </div>
              <ui-icon name="chevron-right" class="h-5 w-5 text-gray-400 group-hover:text-indigo-600 transition-colors" />
            </div>
          </a>

          <!-- Placeholder for future feature -->
          <div class="bg-white rounded-lg shadow p-6 opacity-50">
            <div class="flex items-center">
              <div class="p-3 rounded-full bg-gray-100">
                <ui-icon name="users" class="h-6 w-6 text-gray-400" />
              </div>
              <div class="ml-4">
                <p class="text-lg font-medium text-gray-500">User Directory</p>
                <p class="text-sm text-gray-400">
                  Browse and search LDAP directory (coming soon)
                </p>
              </div>
            </div>
          </div>
        </div>

        <!-- Connection Details -->
        @if (serverInfo()?.enabled) {
          <div class="bg-white rounded-lg shadow">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">Connection Details</h2>
            </div>
            <div class="p-6 space-y-4">
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <dt class="text-sm font-medium text-gray-500">Base DN</dt>
                  <dd class="mt-1 text-sm text-gray-900 font-mono">{{ serverInfo()?.baseDn }}</dd>
                </div>
                <div>
                  <dt class="text-sm font-medium text-gray-500">Organization</dt>
                  <dd class="mt-1 text-sm text-gray-900 font-mono">{{ serverInfo()?.organization }}</dd>
                </div>
                <div>
                  <dt class="text-sm font-medium text-gray-500">User OU</dt>
                  <dd class="mt-1 text-sm text-gray-900 font-mono">ou={{ serverInfo()?.userOu }},{{ serverInfo()?.baseDn }}</dd>
                </div>
                <div>
                  <dt class="text-sm font-medium text-gray-500">Group OU</dt>
                  <dd class="mt-1 text-sm text-gray-900 font-mono">ou={{ serverInfo()?.groupOu }},{{ serverInfo()?.baseDn }}</dd>
                </div>
                <div>
                  <dt class="text-sm font-medium text-gray-500">Admin DN</dt>
                  <dd class="mt-1 text-sm text-gray-900 font-mono">{{ serverInfo()?.adminDn }}</dd>
                </div>
                <div>
                  <dt class="text-sm font-medium text-gray-500">Max Results</dt>
                  <dd class="mt-1 text-sm text-gray-900 font-mono">{{ serverInfo()?.maxSearchResults }}</dd>
                </div>
              </div>

              <!-- Sample LDAP URL -->
              <div class="mt-4 p-4 bg-gray-50 rounded-lg">
                <p class="text-sm font-medium text-gray-700 mb-2">Sample LDAP URL:</p>
                <code class="text-sm text-gray-900 bg-white px-3 py-2 rounded border block overflow-x-auto">
                  ldap://your-server:{{ serverInfo()?.port }}/{{ serverInfo()?.baseDn }}
                </code>
              </div>

              <!-- Sample User DN -->
              <div class="p-4 bg-gray-50 rounded-lg">
                <p class="text-sm font-medium text-gray-700 mb-2">Sample User DN:</p>
                <code class="text-sm text-gray-900 bg-white px-3 py-2 rounded border block overflow-x-auto">
                  uid=username,ou={{ serverInfo()?.userOu }},{{ serverInfo()?.baseDn }}
                </code>
              </div>
            </div>
          </div>
        }

        <!-- Quick Start Guide -->
        <div class="bg-white rounded-lg shadow">
          <div class="px-6 py-4 border-b border-gray-200">
            <h2 class="text-lg font-medium text-gray-900">Quick Start</h2>
          </div>
          <div class="p-6">
            <ol class="list-decimal list-inside space-y-3 text-sm text-gray-600">
              <li>
                <span class="font-medium text-gray-900">Enable LDAP Server</span> - Go to Settings &rarr; LDAP Server to enable
              </li>
              <li>
                <span class="font-medium text-gray-900">Configure Base DN</span> - Set your organization's Base DN
              </li>
              <li>
                <span class="font-medium text-gray-900">Connect Applications</span> - Use the connection details above to configure your LDAP clients
              </li>
              <li>
                <span class="font-medium text-gray-900">Test Connection</span> - Use an LDAP browser or ldapsearch to verify connectivity
              </li>
            </ol>

            <!-- Sample ldapsearch command -->
            <div class="mt-6 p-4 bg-gray-900 rounded-lg">
              <p class="text-xs text-gray-400 mb-2">Test with ldapsearch:</p>
              <code class="text-sm text-green-400 block overflow-x-auto">
                ldapsearch -x -H ldap://localhost:{{ serverInfo()?.port || 389 }} -b "{{ serverInfo()?.baseDn || 'dc=example,dc=com' }}" "(objectClass=*)"
              </code>
            </div>
          </div>
        </div>
      }
    </div>
  `,
})
export class LdapDashboardComponent implements OnInit {
  private readonly ldapService = inject(LdapService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly serverInfo = signal<LdapServerInfo | null>(null);

  ngOnInit(): void {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);
    this.ldapService.getServerInfo().subscribe({
      next: (info) => {
        this.serverInfo.set(info);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading LDAP info:', err);
        this.error.set('Failed to load LDAP Server information');
        this.loading.set(false);
      },
    });
  }
}

import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { ButtonComponent, IconComponent, PermissionsService } from '@oluso/ui-core';
import { AuthStore, clearTenantSso } from '@oluso/auth';

@Component({
  selector: 'admin-logout-page',
  standalone: true,
  imports: [CommonModule, ButtonComponent, IconComponent],
  template: `
    <div class="min-h-screen bg-gray-50 flex flex-col items-center justify-center px-4">
      <div class="max-w-md w-full text-center">
        <div class="mb-8">
          <div class="mx-auto h-16 w-16 bg-green-100 rounded-full flex items-center justify-center">
            <ui-icon name="check" class="h-8 w-8 text-green-600" />
          </div>
          <h1 class="mt-4 text-2xl font-bold text-gray-900">Signed Out</h1>
          <p class="mt-2 text-gray-600">
            You have been successfully signed out of the Admin Portal.
          </p>
        </div>

        <div class="space-y-4">
          <ui-button class="w-full" (clicked)="login()">
            Sign In Again
          </ui-button>

          <p class="text-sm text-gray-500">
            You can close this window or sign in again to continue working.
          </p>
        </div>
      </div>
    </div>
  `,
})
export class LogoutPageComponent implements OnInit {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly authStore = inject(AuthStore);
  private readonly permissionsService = inject(PermissionsService);

  ngOnInit(): void {
    this.authStore.logout();
    this.permissionsService.clear();
    clearTenantSso();
  }

  login(): void {
    this.oidcService.authorize();
  }
}

import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';

@Component({
  selector: 'app-unauthorized-page',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-50">
      <div class="max-w-md w-full text-center p-8">
        <div class="mx-auto h-16 w-16 rounded-full bg-red-100 flex items-center justify-center mb-6">
          <svg class="h-8 w-8 text-red-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" d="M18.364 18.364A9 9 0 0 0 5.636 5.636m12.728 12.728A9 9 0 0 1 5.636 5.636m12.728 12.728L5.636 5.636" />
          </svg>
        </div>
        <h1 class="text-2xl font-bold text-gray-900 mb-2">Access Denied</h1>
        <p class="text-gray-600 mb-6">You don't have permission to access this page. Contact your administrator if you believe this is an error, or sign out and try with a different account.</p>
        <div class="flex items-center justify-center gap-3">
          <a routerLink="/dashboard" class="inline-flex items-center px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700">
            Go to Dashboard
          </a>
          <button
            type="button"
            (click)="logout()"
            class="inline-flex items-center px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Sign out
          </button>
        </div>
      </div>
    </div>
  `,
})
export class UnauthorizedPageComponent {
  private readonly oidcService = inject(OidcSecurityService);

  logout(): void {
    this.oidcService.logoff().subscribe();
  }
}

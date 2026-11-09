import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { IconComponent } from '@syndew/oluso-ui-core';

@Component({
  selector: 'workspace-default-landing-page',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="min-h-screen bg-gradient-to-br from-primary-600 to-primary-800 flex flex-col items-center justify-center px-4">
      <div class="max-w-md w-full text-center">
        <div class="mb-8">
          <div class="mx-auto h-20 w-20 bg-white rounded-full flex items-center justify-center mb-6">
            <ui-icon name="briefcase" class="h-10 w-10 text-primary-600" />
          </div>
          <h1 class="text-3xl font-bold text-white">Workspace Portal</h1>
          <p class="mt-3 text-primary-100">
            Access your self-service portal to manage your tasks and information.
          </p>
        </div>

        <div class="space-y-4">
          <button
            type="button"
            class="w-full flex items-center justify-center px-6 py-3 border border-transparent text-base font-medium rounded-lg text-primary-700 bg-white hover:bg-primary-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-primary-700 focus:ring-white transition-colors"
            (click)="login()"
          >
            Sign In
          </button>
        </div>

        <div class="mt-8 text-sm text-primary-200">
          <p>Sign in with your company credentials to access the portal.</p>
        </div>
      </div>

      <div class="absolute bottom-4 text-center text-primary-300 text-sm">
        <p>Powered by Oluso Identity Platform</p>
      </div>
    </div>
  `,
})
export class DefaultLandingPageComponent implements OnInit {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.oidcService.isAuthenticated$.subscribe(({ isAuthenticated }) => {
      if (isAuthenticated) {
        this.router.navigate(['/dashboard']);
      }
    });
  }

  login(): void {
    this.oidcService.authorize();
  }
}

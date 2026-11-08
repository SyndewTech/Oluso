import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { ButtonComponent, IconComponent } from '@oluso/ui-core';
import { AccountAuthService } from '../../services/account-auth.service';

@Component({
  selector: 'account-default-landing-page',
  standalone: true,
  imports: [CommonModule, ButtonComponent, IconComponent],
  template: `
    <div class="min-h-screen bg-gray-50 flex flex-col items-center justify-center px-4">
      <div class="max-w-md w-full text-center">
        <div class="mb-8">
          <ui-icon name="user" class="mx-auto h-16 w-16 text-primary-600" />
          <h1 class="mt-4 text-3xl font-bold text-gray-900">My Account</h1>
          <p class="mt-2 text-gray-600">
            Manage your profile, security settings, and connected applications
          </p>
        </div>

        <div class="space-y-4">
          <ui-button class="w-full" (clicked)="login()">
            Sign In
          </ui-button>
        </div>

        <div class="mt-8 text-sm text-gray-500">
          <p>Sign in to access your account settings</p>
        </div>
      </div>
    </div>
  `,
})
export class DefaultLandingPageComponent implements OnInit {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly router = inject(Router);
  private readonly auth = inject(AccountAuthService);

  ngOnInit(): void {
    this.oidcService.isAuthenticated$.subscribe(({ isAuthenticated }) => {
      if (isAuthenticated) {
        this.router.navigateByUrl(this.auth.homePath());
      }
    });
  }

  login(): void {
    this.oidcService.authorize();
  }
}

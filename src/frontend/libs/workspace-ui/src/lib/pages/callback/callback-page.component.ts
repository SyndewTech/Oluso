import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { readAndClearReturnUrl } from '@oluso/auth';
import { WorkspaceAuthService } from '../../services/workspace-auth.service';

@Component({
  selector: 'workspace-callback-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    @if (errorMessage()) {
      <div class="min-h-screen flex items-center justify-center bg-gray-50">
        <div class="max-w-md w-full text-center p-8">
          <div class="mx-auto h-16 w-16 rounded-full bg-red-100 flex items-center justify-center mb-6">
            <svg class="h-8 w-8 text-red-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
            </svg>
          </div>
          <h1 class="text-2xl font-bold text-gray-900 mb-2">Sign-in failed</h1>
          <p class="text-gray-600 mb-2">{{ errorMessage() }}</p>
          @if (errorCode()) {
            <p class="text-xs text-gray-400 mb-6 font-mono">Error code: {{ errorCode() }}</p>
          } @else {
            <p class="mb-6"></p>
          }
          <a routerLink="/" class="inline-flex items-center px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700">
            Back to sign in
          </a>
        </div>
      </div>
    } @else {
      <div class="min-h-screen bg-gray-50 flex flex-col items-center justify-center">
        <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mb-4"></div>
        <p class="text-gray-600">Completing sign in...</p>
      </div>
    }
  `,
})
export class CallbackPageComponent implements OnInit {
  private readonly authService = inject(WorkspaceAuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly errorCode = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    // Surface OIDC server-side rejections (invalid_scope, access_denied, …)
    // rather than silently bouncing the user to the landing page.
    const params = this.route.snapshot.queryParamMap;
    const err = params.get('error');
    if (err) {
      this.errorCode.set(err);
      this.errorMessage.set(
        params.get('error_description') ?? 'Authentication was rejected by the identity provider.'
      );
      return;
    }

    // checkAuth() already ran in APP_INITIALIZER and processed the auth code.
    // Back to the page the user was on when their session ended, else the dashboard.
    if (this.authService.isAuthenticated()) {
      this.router.navigateByUrl(readAndClearReturnUrl() ?? '/dashboard');
    } else {
      this.errorCode.set('not_authenticated');
      this.errorMessage.set('Could not establish a session. Please try signing in again.');
    }
  }
}

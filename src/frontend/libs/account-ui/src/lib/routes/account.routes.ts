import { Routes } from '@angular/router';
import { accountSignInGuard } from '../guards/account-sign-in.guard';

export const ACCOUNT_ROUTES: Routes = [
  // Public landing page
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('../pages/landing/landing-page.component').then(
        (m) => m.LandingPageComponent
      ),
  },
  // Public logout page
  {
    path: 'logout',
    title: 'Signed out',
    loadComponent: () =>
      import('../pages/logout/logout-page.component').then(
        (m) => m.LogoutPageComponent
      ),
  },
  // OIDC callback
  {
    path: 'callback',
    title: 'Signing in',
    loadComponent: () =>
      import('../pages/callback/callback-page.component').then(
        (m) => m.CallbackPageComponent
      ),
  },
  {
    path: 'unauthorized',
    title: 'Access denied',
    loadComponent: () =>
      import('../pages/unauthorized/unauthorized-page.component').then(
        (m) => m.UnauthorizedPageComponent
      ),
  },
  // Protected routes under account layout
  {
    path: '',
    loadComponent: () =>
      import('../layouts/account-layout/account-layout.component').then(
        (m) => m.AccountLayoutComponent
      ),
    canActivate: [accountSignInGuard],
    children: [
      {
        path: 'profile',
        title: 'Profile',
        loadComponent: () =>
          import('../pages/profile/profile-page.component').then(
            (m) => m.ProfilePageComponent
          ),
      },
      {
        path: 'security',
        title: 'Security',
        loadComponent: () =>
          import('../pages/security/security-page.component').then(
            (m) => m.SecurityPageComponent
          ),
      },
      {
        path: 'security/two-factor',
        title: 'Two-factor authentication',
        loadComponent: () =>
          import('../pages/security/two-factor-page.component').then(
            (m) => m.TwoFactorPageComponent
          ),
      },
      {
        path: 'sessions',
        title: 'Sessions',
        loadComponent: () =>
          import('../pages/sessions/sessions-page.component').then(
            (m) => m.SessionsPageComponent
          ),
      },
      {
        path: 'connected-apps',
        title: 'Connected apps',
        loadComponent: () =>
          import('../pages/connected-apps/connected-apps-page.component').then(
            (m) => m.ConnectedAppsPageComponent
          ),
      },
      {
        path: 'authentication-requests',
        title: 'Sign-in requests',
        loadComponent: () =>
          import('../pages/ciba-requests/ciba-requests-page.component').then(
            (m) => m.CibaRequestsPageComponent
          ),
      },
      {
        path: 'verify',
        title: 'Verify',
        loadComponent: () =>
          import('../pages/verify/verify-page.component').then(
            (m) => m.VerifyPageComponent
          ),
      },
    ],
  },
];

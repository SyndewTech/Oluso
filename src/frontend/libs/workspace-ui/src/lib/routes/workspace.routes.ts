import { Routes } from '@angular/router';
import { AutoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';

/**
 * Core workspace routes.
 * Only Dashboard and Profile are included here.
 * All other routes are provided by plugins.
 */
export const WORKSPACE_ROUTES: Routes = [
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
  // Unauthorized / access denied
  {
    path: 'unauthorized',
    title: 'Access denied',
    loadComponent: () =>
      import('../pages/unauthorized/unauthorized-page.component').then(
        (m) => m.UnauthorizedPageComponent
      ),
  },
  // Generic anonymous magic-link landing. The token comes from the URL
  // fragment (#tk=…) so it stays client-side and is never logged. The page
  // dispatches via the public /api/public/links endpoints; any module that
  // registers an IActionTokenHandler is automatically supported.
  {
    path: 'm',
    title: 'Link',
    loadComponent: () =>
      import('../pages/magic-link/magic-link-page.component').then(
        (m) => m.MagicLinkPageComponent
      ),
  },
  // Anonymous hosted document (invoice, quote). Same token-in-fragment
  // scheme as /m, but renders the document the handler puts in preview.data.
  {
    path: 'd',
    title: 'Document',
    loadComponent: () =>
      import('../pages/hosted-document/hosted-document-page.component').then(
        (m) => m.HostedDocumentPageComponent
      ),
  },
  // Protected routes under workspace layout
  {
    path: '',
    loadComponent: () =>
      import('../layouts/workspace-layout/workspace-layout.component').then(
        (m) => m.WorkspaceLayoutComponent
      ),
    canActivate: [AutoLoginPartialRoutesGuard],
    children: [
      {
        path: 'dashboard',
        title: 'Dashboard',
        loadComponent: () =>
          import('../pages/dashboard/dashboard-page.component').then(
            (m) => m.DashboardPageComponent
          ),
        data: { helpContext: 'workspace.dashboard' },
      },
      {
        path: 'profile',
        title: 'Profile',
        loadComponent: () =>
          import('../pages/profile/profile-page.component').then(
            (m) => m.ProfilePageComponent
          ),
        data: { helpContext: 'workspace.profile' },
      },
      {
        path: 'my-files',
        title: 'My files',
        loadComponent: () =>
          import('../pages/my-files/my-files-page.component').then(
            (m) => m.MyFilesPageComponent
          ),
        data: { helpContext: 'workspace.my-files' },
      },
      {
        path: 'notifications',
        title: 'Notifications',
        loadComponent: () =>
          import('../pages/notifications/notification-preferences-page.component').then(
            (m) => m.NotificationPreferencesPageComponent
          ),
        data: { helpContext: 'workspace.notifications' },
      },
      // Plugin routes will be added dynamically via WorkspacePluginService
    ],
  },
];

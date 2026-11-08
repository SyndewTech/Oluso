import { Routes } from '@angular/router';
import { AutoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { superAdminGuard, orgAdminGuard, permissionGuard } from '@oluso/auth';

/**
 * Admin UI Routes
 *
 * These routes define the admin dashboard navigation structure.
 * Public routes: /landing, /callback, /logout
 * Protected routes use AutoLoginPartialRoutesGuard (OIDC redirect).
 */
export const ADMIN_ROUTES: Routes = [
  // Public landing page (root)
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
  // Protected routes under main layout
  {
    path: '',
    loadComponent: () =>
      import('../layouts/main-layout/main-layout.component').then(
        (m) => m.MainLayoutComponent
      ),
    canActivate: [AutoLoginPartialRoutesGuard],
    children: [
      {
        path: 'dashboard',
        title: 'Dashboard',
        data: { helpContext: 'admin.dashboard' },
        loadComponent: () =>
          import('../pages/dashboard/dashboard.component').then(
            (m) => m.DashboardComponent
          ),
      },
      {
        path: 'clients',
        title: 'Clients',
        data: { helpContext: 'admin.clients' },
        loadComponent: () =>
          import('../pages/clients/clients-page.component').then(
            (m) => m.ClientsPageComponent
          ),
      },
      {
        path: 'clients/new',
        title: 'New client',
        data: { helpContext: 'admin.clients.create' },
        loadComponent: () =>
          import('../pages/clients/client-detail-page.component').then(
            (m) => m.ClientDetailPageComponent
          ),
      },
      {
        path: 'clients/:id',
        title: 'Client',
        data: { helpContext: 'admin.clients.detail' },
        loadComponent: () =>
          import('../pages/clients/client-detail-page.component').then(
            (m) => m.ClientDetailPageComponent
          ),
      },
      {
        path: 'search',
        title: 'Search',
        loadComponent: () =>
          import('../pages/search/search-results-page.component').then(
            (m) => m.SearchResultsPageComponent
          ),
      },
      {
        path: 'users',
        title: 'Users',
        data: { helpContext: 'admin.users' },
        loadComponent: () =>
          import('../pages/users/users-page.component').then(
            (m) => m.UsersPageComponent
          ),
      },
      {
        path: 'users/:id',
        title: 'User',
        data: { helpContext: 'admin.users.detail' },
        loadComponent: () =>
          import('../pages/users/user-detail-page.component').then(
            (m) => m.UserDetailPageComponent
          ),
      },
      {
        path: 'settings',
        title: 'Settings',
        data: { helpContext: 'admin.settings' },
        loadComponent: () =>
          import('../pages/settings/settings-page.component').then(
            (m) => m.SettingsPageComponent
          ),
      },
      {
        path: 'terminology',
        title: 'Terminology',
        data: { helpContext: 'admin.terminology' },
        loadComponent: () =>
          import('../pages/terminology/terminology-center-page.component').then(
            (m) => m.TerminologyCenterPageComponent
          ),
      },
      {
        path: 'blueprints',
        title: 'Blueprints',
        data: { helpContext: 'admin.blueprints' },
        loadComponent: () =>
          import('../pages/blueprints/blueprints-page.component').then(
            (m) => m.BlueprintsPageComponent
          ),
      },
      {
        path: 'organizations',
        title: 'Organizations',
        data: { helpContext: 'admin.organizations' },
        loadComponent: () =>
          import('../pages/organizations/organizations-page.component').then(
            (m) => m.OrganizationsPageComponent
          ),
        canActivate: [superAdminGuard],
      },
      {
        path: 'organizations/:id',
        title: 'Organization',
        data: { helpContext: 'admin.organizations.detail' },
        loadComponent: () =>
          import('../pages/organizations/organization-detail-page.component').then(
            (m) => m.OrganizationDetailPageComponent
          ),
        canActivate: [superAdminGuard],
      },
      {
        path: 'teams',
        title: 'Teams',
        data: { helpContext: 'admin.teams' },
        loadComponent: () =>
          import('../pages/teams/teams-page.component').then(
            (m) => m.TeamsPageComponent
          ),
      },
      {
        path: 'teams/:id',
        title: 'Team',
        data: { helpContext: 'admin.teams.detail' },
        loadComponent: () =>
          import('../pages/teams/team-detail-page.component').then(
            (m) => m.TeamDetailPageComponent
          ),
      },
      {
        path: 'platform/clients',
        title: 'Platform clients',
        data: { helpContext: 'admin.platform.clients' },
        loadComponent: () =>
          import('../pages/platform-clients/platform-clients-page.component').then(
            (m) => m.PlatformClientsPageComponent
          ),
        canActivate: [superAdminGuard],
      },
      {
        path: 'platform/clients/new',
        title: 'New platform client',
        data: { platformMode: true, helpContext: 'admin.platform.clients.create' },
        loadComponent: () =>
          import('../pages/clients/client-detail-page.component').then(
            (m) => m.ClientDetailPageComponent
          ),
        canActivate: [superAdminGuard],
      },
      {
        path: 'platform/clients/:id',
        title: 'Platform client',
        data: { platformMode: true, helpContext: 'admin.platform.clients.detail' },
        loadComponent: () =>
          import('../pages/clients/client-detail-page.component').then(
            (m) => m.ClientDetailPageComponent
          ),
        canActivate: [superAdminGuard],
      },
      {
        path: 'identity-providers',
        title: 'Identity providers',
        data: { helpContext: 'admin.identity-providers' },
        loadComponent: () =>
          import('../pages/identity-providers/identity-providers-page.component').then(
            (m) => m.IdentityProvidersPageComponent
          ),
      },
      {
        path: 'identity-providers/:id',
        title: 'Identity provider',
        data: { helpContext: 'admin.identity-providers.detail' },
        loadComponent: () =>
          import('../pages/identity-providers/identity-provider-detail-page.component').then(
            (m) => m.IdentityProviderDetailPageComponent
          ),
      },
      {
        path: 'api-scopes',
        title: 'API scopes',
        data: { helpContext: 'admin.api-scopes' },
        loadComponent: () =>
          import('../pages/api-scopes/api-scopes-page.component').then(
            (m) => m.ApiScopesPageComponent
          ),
      },
      {
        path: 'api-scopes/:id',
        title: 'API scope',
        data: { helpContext: 'admin.api-scopes.detail' },
        loadComponent: () =>
          import('../pages/api-scopes/api-scope-detail-page.component').then(
            (m) => m.ApiScopeDetailPageComponent
          ),
      },
      {
        path: 'identity-resources',
        title: 'Identity resources',
        data: { helpContext: 'admin.identity-resources' },
        loadComponent: () =>
          import('../pages/identity-resources/identity-resources-page.component').then(
            (m) => m.IdentityResourcesPageComponent
          ),
      },
      {
        path: 'identity-resources/:id',
        title: 'Identity resource',
        data: { helpContext: 'admin.identity-resources.detail' },
        loadComponent: () =>
          import('../pages/identity-resources/identity-resource-detail-page.component').then(
            (m) => m.IdentityResourceDetailPageComponent
          ),
      },
      {
        path: 'resources',
        title: 'API resources',
        data: { helpContext: 'admin.resources' },
        loadComponent: () =>
          import('../pages/resources/resources-page.component').then(
            (m) => m.ResourcesPageComponent
          ),
      },
      {
        path: 'resources/:id',
        title: 'API resource',
        data: { helpContext: 'admin.resources.detail' },
        loadComponent: () =>
          import('../pages/resources/resource-detail-page.component').then(
            (m) => m.ResourceDetailPageComponent
          ),
      },
      {
        path: 'entitlements',
        title: 'Entitlements',
        data: { helpContext: 'admin.entitlements' },
        loadComponent: () =>
          import('../pages/entitlements/entitlements-page.component').then(
            (m) => m.EntitlementsPageComponent
          ),
      },
      {
        path: 'roles',
        title: 'Roles',
        data: { helpContext: 'admin.roles' },
        loadComponent: () =>
          import('../pages/roles/roles-page.component').then(
            (m) => m.RolesPageComponent
          ),
      },
      {
        path: 'module-seats',
        title: 'Module seats',
        data: { helpContext: 'admin.module-seats' },
        loadComponent: () =>
          import('../pages/module-seats/module-seats-page.component').then(
            (m) => m.ModuleSeatsPageComponent
          ),
      },
      {
        path: 'audit-logs',
        title: 'Audit logs',
        data: { helpContext: 'admin.audit-logs' },
        loadComponent: () =>
          import('../pages/audit-logs/audit-logs-page.component').then(
            (m) => m.AuditLogsPageComponent
          ),
      },
      {
        path: 'grants',
        title: 'Grants',
        data: { helpContext: 'admin.grants' },
        loadComponent: () =>
          import('../pages/grants/grants-page.component').then(
            (m) => m.GrantsPageComponent
          ),
      },
      {
        path: 'webhooks',
        title: 'Webhooks',
        data: { helpContext: 'admin.webhooks' },
        loadComponent: () =>
          import('../pages/webhooks/webhooks-page.component').then(
            (m) => m.WebhooksPageComponent
          ),
      },
      {
        path: 'webhooks/new',
        title: 'New webhook',
        data: { helpContext: 'admin.webhooks.detail' },
        loadComponent: () =>
          import('../pages/webhooks/webhook-detail-page.component').then(
            (m) => m.WebhookDetailPageComponent
          ),
      },
      {
        path: 'webhooks/:id',
        title: 'Webhook',
        data: { helpContext: 'admin.webhooks.detail' },
        loadComponent: () =>
          import('../pages/webhooks/webhook-detail-page.component').then(
            (m) => m.WebhookDetailPageComponent
          ),
      },
      {
        path: 'signing-keys',
        title: 'Signing keys',
        data: { helpContext: 'admin.signing-keys' },
        loadComponent: () =>
          import('../pages/signing-keys/signing-keys-page.component').then(
            (m) => m.SigningKeysPageComponent
          ),
      },
      {
        path: 'settings/license',
        title: 'License',
        data: { helpContext: 'admin.settings.license' },
        loadComponent: () =>
          import('../pages/settings/license-status-page.component').then(
            (m) => m.LicenseStatusPageComponent
          ),
        canActivate: [superAdminGuard],
      },
      {
        path: 'tenants',
        title: 'Tenants',
        data: { helpContext: 'admin.tenants' },
        loadComponent: () =>
          import('../pages/tenants/tenants-page.component').then(
            (m) => m.TenantsPageComponent
          ),
        canActivate: [orgAdminGuard],
      },
      {
        path: 'tenants/:id',
        title: 'Tenant',
        data: { helpContext: 'admin.tenants.detail' },
        loadComponent: () =>
          import('../pages/tenants/tenant-settings-page.component').then(
            (m) => m.TenantSettingsPageComponent
          ),
        canActivate: [orgAdminGuard],
      },
      // Feedback
      {
        path: 'feedback',
        title: 'Feedback',
        data: { helpContext: 'admin.feedback' },
        loadComponent: () =>
          import('../pages/feedback/feedback-page.component').then(
            (m) => m.FeedbackPageComponent
          ),
      },
      {
        path: 'feedback/:id',
        title: 'Feedback item',
        data: { helpContext: 'admin.feedback.detail' },
        loadComponent: () =>
          import('../pages/feedback/feedback-detail-page.component').then(
            (m) => m.FeedbackDetailPageComponent
          ),
      },
      // Experience section
      {
        path: 'experience/journeys',
        title: 'Journeys',
        data: { helpContext: 'admin.experience.journeys' },
        loadComponent: () =>
          import('../pages/journeys/journeys-page.component').then(
            (m) => m.JourneysPageComponent
          ),
      },
      {
        path: 'experience/journeys/:policyId',
        title: 'Journey',
        data: { helpContext: 'admin.experience.journeys.builder' },
        loadComponent: () =>
          import('../pages/journeys/journey-builder-page.component').then(
            (m) => m.JourneyBuilderPageComponent
          ),
      },
      {
        path: 'experience/submissions',
        title: 'Submissions',
        canActivate: [permissionGuard],
        data: { helpContext: 'admin.experience.submissions', permission: 'submissions.read' },
        loadComponent: () =>
          import('../pages/submissions/submissions-page.component').then(
            (m) => m.SubmissionsPageComponent
          ),
      },
      {
        path: 'experience/plugins',
        title: 'Plugins',
        data: { helpContext: 'admin.experience.plugins' },
        loadComponent: () =>
          import('../pages/plugins/plugins-page.component').then(
            (m) => m.PluginsPageComponent
          ),
      },
    ],
  },
];

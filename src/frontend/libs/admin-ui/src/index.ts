// Config
export * from './lib/config';

// Auth
export { adminOidcConfig } from './lib/auth/oidc.config';
export { OidcAuthProvider, provideAdminAuth } from './lib/auth/oidc-auth-provider';
export { ADMIN_BRAND } from './lib/branding/admin-brand.token';
export type { AdminBrandConfig } from './lib/branding/admin-brand.token';
export { ADMIN_NAV_MODES } from './lib/branding/admin-nav-modes.token';
export type { AdminNavMode, AdminNavModesConfig } from './lib/branding/admin-nav-modes.token';

// Models
export * from './lib/models/index';

// Routes
export * from './lib/routes/admin.routes';

// Services
export * from './lib/services/admin-auth.service';
export * from './lib/services/api.service';
export * from './lib/services/client.service';
export * from './lib/services/user.service';
export * from './lib/services/role.service';
export * from './lib/services/settings.service';
export * from './lib/services/terminology.service';
export * from './lib/services/blueprints.service';
export * from './lib/services/identity-provider.service';
export * from './lib/services/api-scope.service';
export * from './lib/services/resource.service';
export * from './lib/services/identity-resource.service';
export * from './lib/services/permission.service';
export * from './lib/services/audit-log.service';
export * from './lib/services/grant.service';
export * from './lib/services/webhook.service';
export * from './lib/services/plugin.service';
export * from './lib/services/signing-key.service';
export * from './lib/services/tenant.service';
export * from './lib/services/organization.service';
export * from './lib/services/journey.service';
export * from './lib/services/submission.service';
export * from './lib/services/context.service';
export * from './lib/services/locale.service';

export * from './lib/services/feedback-admin.service';
export * from './lib/services/module-seats.service';

// Layouts
export * from './lib/layouts/main-layout/main-layout.component';

// Pages
export * from './lib/pages/clients/clients-page.component';
export * from './lib/pages/clients/client-detail-page.component';
export * from './lib/pages/dashboard/dashboard.component';
export * from './lib/pages/landing/landing-page.component';
export * from './lib/pages/landing/default-landing-page.component';
export * from './lib/pages/landing/landing.token';
export * from './lib/pages/callback/callback-page.component';
export * from './lib/pages/logout/logout-page.component';
export * from './lib/pages/users/users-page.component';
export * from './lib/pages/users/user-detail-page.component';
export * from './lib/pages/settings/settings-page.component';
export * from './lib/pages/terminology/terminology-center-page.component';
export * from './lib/pages/blueprints/blueprints-page.component';
export * from './lib/pages/settings/sender-profiles-tab.component';
export * from './lib/pages/settings/whatsapp-settings-tab.component';
export * from './lib/models/sender-profile.model';
export * from './lib/services/sender-profiles.service';
export * from './lib/services/whatsapp.service';
export * from './lib/models/whatsapp.model';
export * from './lib/pages/identity-providers/identity-providers-page.component';
export * from './lib/pages/identity-providers/identity-provider-detail-page.component';
export * from './lib/pages/api-scopes/api-scopes-page.component';
export * from './lib/pages/api-scopes/api-scope-detail-page.component';
export * from './lib/pages/identity-resources/identity-resources-page.component';
export * from './lib/pages/identity-resources/identity-resource-detail-page.component';
export * from './lib/pages/resources/resources-page.component';
export * from './lib/pages/resources/resource-detail-page.component';
export * from './lib/pages/roles/roles-page.component';
export * from './lib/pages/audit-logs/audit-logs-page.component';
export * from './lib/pages/grants/grants-page.component';
export * from './lib/pages/webhooks/webhooks-page.component';
export * from './lib/pages/plugins/plugins-page.component';
export * from './lib/pages/signing-keys/signing-keys-page.component';
export * from './lib/pages/tenants/tenants-page.component';
export * from './lib/pages/tenants/tenant-settings-page.component';
export * from './lib/pages/organizations/organizations-page.component';
export * from './lib/pages/organizations/organization-detail-page.component';
export * from './lib/pages/teams/teams-page.component';
export * from './lib/services/team.service';
export * from './lib/models/team.model';
export * from './lib/pages/journeys/journeys-page.component';
export * from './lib/pages/journeys/journey-builder-page.component';
export * from './lib/pages/submissions/submissions-page.component';
export * from './lib/pages/feedback/feedback-page.component';
export * from './lib/pages/feedback/feedback-detail-page.component';
export * from './lib/pages/module-seats/module-seats-page.component';

// Journey Components
export * from './lib/components/journey/index';

// Tenant Switcher
export * from './lib/components/tenant-switcher/tenant-switcher.component';

// Toast — moved to @oluso/ui-core. Re-exported here so existing
// imports from @oluso/admin-ui keep working.
export { ToastContainerComponent } from '@oluso/ui-core';

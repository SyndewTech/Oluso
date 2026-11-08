// Routes
export * from './lib/routes/workspace.routes';

// Guards
export { managerGuard } from './lib/guards/manager.guard';
export { moduleAccessGuard } from './lib/guards/module-access.guard';

// Config
export * from './lib/auth/oidc.config';

// Auth provider
export { OidcAuthProvider, provideWorkspaceAuth } from './lib/auth/oidc-auth-provider';

// Models
export * from './lib/models';

// Services
export * from './lib/services';

// Layout Components
export { WorkspaceLayoutComponent } from './lib/layouts/workspace-layout/workspace-layout.component';

// UI Components
export { AppSwitcherComponent } from './lib/components/app-switcher/app-switcher.component';
export { ConversationThreadComponent } from './lib/components/conversation-thread/conversation-thread.component';

// Page Components
export { LandingPageComponent } from './lib/pages/landing/landing-page.component';
export { DefaultLandingPageComponent } from './lib/pages/landing/default-landing-page.component';
export { WORKSPACE_LANDING_COMPONENT } from './lib/pages/landing/landing.token';
export { LogoutPageComponent } from './lib/pages/logout/logout-page.component';
export { CallbackPageComponent } from './lib/pages/callback/callback-page.component';
export { DashboardPageComponent } from './lib/pages/dashboard/dashboard-page.component';
export { ProfilePageComponent } from './lib/pages/profile/profile-page.component';
export { PlaceholderPageComponent } from './lib/pages/placeholder/placeholder-page.component';
export { MyFilesPageComponent } from './lib/pages/my-files/my-files-page.component';

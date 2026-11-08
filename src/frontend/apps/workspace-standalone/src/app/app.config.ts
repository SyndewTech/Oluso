import { ApplicationConfig, provideBrowserGlobalErrorListeners, APP_INITIALIZER, Injector, inject } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideAppTitle } from '@oluso/ui-core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor as oidcAuthInterceptor } from 'angular-auth-oidc-client';
import { ngrokInterceptor, authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor } from '@oluso/auth';
import {
  WORKSPACE_ROUTES,
  WorkspaceApiService,
  WorkspaceAuthService,
  WorkspacePluginService,
  ActionCenterService,
  RealtimeService,
  provideWorkspaceAuth,
  apiUrl,
} from '@oluso/workspace-ui';
import {
  API_BASE_URL,
  provideTerminology,
  loadTerminology,
  TerminologyRefreshService,
  LocaleService,
} from '@oluso/ui-core';
import { firstValueFrom } from 'rxjs';

/**
 * Initialize the workspace services with the correct base URLs.
 */
function initializeApp(): () => Promise<void> {
  return async () => {
    // Everything is injected before the first await: an initializer keeps its
    // injection context only for its synchronous prefix.
    const apiService = inject(WorkspaceApiService);
    const authService = inject(WorkspaceAuthService);
    const pluginService = inject(WorkspacePluginService);
    const actionCenterService = inject(ActionCenterService);
    const localeService = inject(LocaleService);
    const realtime = inject(RealtimeService);
    const terminologyRefresh = inject(TerminologyRefreshService);
    const injector = inject(Injector);

    // Configure API service with the resolved URL
    apiService.setBaseUrl(apiUrl);
    actionCenterService.setBaseUrl(apiUrl);

    // Initialize auth service (loads user profile)
    await authService.initialize();

    // Mark plugin service as initialized
    pluginService.markInitialized();

    // Resolve the tenant's locale first so the terminology bundle is fetched
    // for the right language on first paint. Best-effort: on failure we fall
    // back to the default locale rather than blocking boot.
    localeService.setApiBaseUrl(`${apiUrl}/api/locale`);
    try {
      await firstValueFrom(localeService.loadCurrentLocale());
    } catch {
      // Fall back to default locale.
    }

    // Terminology and the realtime hub both require a signed-in user. Before
    // login, a 401 here would trigger the refresh interceptor's own authorize()
    // and race the router's login redirect, so only wire them once authenticated.
    if (authService.isAuthenticated()) {
      // Load the industry/tenant terminology bundle before first paint so
      // boards render "Bed/Ward" etc. rather than generic labels.
      await loadTerminology(injector);

      // Live relabel: when an admin renames a term (or changes the industry),
      // the backend broadcasts on the platform `terminology` channel. Re-fetch
      // the bundle so open screens repaint with the new wording without a reload.
      realtime.onChannel('terminology').subscribe(() => {
        void terminologyRefresh.refresh();
      });
    }
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(WORKSPACE_ROUTES),
    provideAppTitle({ name: 'Workspace Portal', brandingId: 'workspace' }),
    provideHttpClient(
      withInterceptors([ngrokInterceptor, oidcAuthInterceptor(), authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor])
    ),
    provideWorkspaceAuth(),
    provideTerminology({ endpoint: `${apiUrl}/api/terminology` }),
    { provide: API_BASE_URL, useValue: `${apiUrl}/api/workspace` },
    {
      provide: APP_INITIALIZER,
      useFactory: initializeApp,
      multi: true,
    },
  ],
};

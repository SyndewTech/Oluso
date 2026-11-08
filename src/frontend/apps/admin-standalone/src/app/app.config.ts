import { ApplicationConfig, provideBrowserGlobalErrorListeners, APP_INITIALIZER, Injector, inject } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideAppTitle } from '@oluso/ui-core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { tenantInterceptor, authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor } from '@oluso/auth';
import {
  ADMIN_ROUTES,
  ApiService,
  provideAdminAuth,
  AdminAuthService,
  serverUrl,
  apiUrl,
} from '@oluso/admin-ui';
import {
  PluginService,
  API_BASE_URL,
  provideTerminology,
  loadTerminology,
  LocaleService,
} from '@oluso/ui-core';
import { firstValueFrom } from 'rxjs';
import { FIDO2_PLUGIN } from '@oluso/plugins/fido2';
import { LDAP_PLUGIN } from '@oluso/plugins/ldap';
import { SAML_PLUGIN } from '@oluso/plugins/saml';
import { SCIM_PLUGIN } from '@oluso/plugins/scim';
import { VERIFICATION_PLUGIN } from '@oluso/plugins/verification';
import { TELEMETRY_PLUGIN } from '@oluso/plugins/telemetry';

function initializeApp(): () => Promise<void> {
  return async () => {
    // Everything is injected before the first await: an initializer keeps its
    // injection context only for its synchronous prefix.
    const apiService = inject(ApiService);
    const adminAuthService = inject(AdminAuthService);
    const pluginService = inject(PluginService);
    const localeService = inject(LocaleService);
    const injector = inject(Injector);

    // Configure API service with the resolved URL
    apiService.setBaseUrl(apiUrl);
    apiService.setServerUrl(serverUrl);

    // Register plugins
    pluginService.registerAdminPlugin(FIDO2_PLUGIN);
    pluginService.registerAdminPlugin(LDAP_PLUGIN);
    pluginService.registerAdminPlugin(SAML_PLUGIN);
    pluginService.registerAdminPlugin(SCIM_PLUGIN);
    pluginService.registerAdminPlugin(VERIFICATION_PLUGIN);
    pluginService.registerAdminPlugin(TELEMETRY_PLUGIN);
    pluginService.markInitialized();

    // Initialize OIDC auth (checkAuth + load user + load permissions)
    await adminAuthService.initialize();

    // Resolve the tenant's locale first so the terminology bundle is fetched
    // for the right language. Best-effort: fall back to the default locale
    // rather than blocking boot.
    localeService.setApiBaseUrl(`${serverUrl}/api/locale`);
    try {
      await firstValueFrom(localeService.loadCurrentLocale());
    } catch {
      // Fall back to default locale.
    }

    // Load the tenant's terminology bundle so the Terminology Center and any
    // relabelled admin screens render with industry vocabulary from boot.
    // Authenticated only: a 401 before login would trigger the refresh
    // interceptor's authorize() and race the router's login redirect.
    if (adminAuthService.isAuthenticated()) {
      await loadTerminology(injector);
    }
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(ADMIN_ROUTES),
    provideAppTitle({ name: 'Admin Portal', brandingId: 'admin' }),
    provideHttpClient(
      withInterceptors([authRefreshInterceptor, tenantInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor])
    ),
    provideAdminAuth(),
    provideTerminology({ endpoint: `${serverUrl}/api/terminology` }),
    { provide: API_BASE_URL, useValue: apiUrl },
    {
      provide: APP_INITIALIZER,
      useFactory: initializeApp,
      multi: true,
    },
  ],
};

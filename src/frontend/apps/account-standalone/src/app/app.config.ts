import { ApplicationConfig, provideBrowserGlobalErrorListeners, APP_INITIALIZER, inject } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideAppTitle } from '@syndew/oluso-ui-core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor as oidcAuthInterceptor } from 'angular-auth-oidc-client';
import { ngrokInterceptor, authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor } from '@syndew/oluso-auth';
import { ACCOUNT_ROUTES, ApiService, AccountAuthService, provideAccountAuth, apiUrl, serverUrl } from '@syndew/oluso-account-ui';
import { PluginService, API_BASE_URL } from '@syndew/oluso-ui-core';
import { FIDO2_ACCOUNT_PLUGIN, PasskeysAccountService } from '@syndew/oluso-plugin-fido2';

/**
 * Initialize the API services with the correct base URLs.
 */
function initializeApp(): () => Promise<void> {
  return async () => {
    const apiService = inject(ApiService);
    const pluginService = inject(PluginService);
    const passkeysService = inject(PasskeysAccountService);
    const accountAuthService = inject(AccountAuthService);

    // Configure API service with the resolved URL
    apiService.setBaseUrl(apiUrl);
    apiService.setServerUrl(serverUrl);

    // Configure passkeys service with the server URL
    passkeysService.setBaseUrl(serverUrl);

    // Register plugins
    pluginService.registerAccountPlugin(FIDO2_ACCOUNT_PLUGIN);
    pluginService.markInitialized();

    // Load user's account profile (features) for plugin filtering
    await accountAuthService.initialize();
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(ACCOUNT_ROUTES),
    provideAppTitle({ name: 'My Account', brandingId: 'account' }),
    provideHttpClient(
      withInterceptors([ngrokInterceptor, oidcAuthInterceptor(), authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor])
    ),
    provideAccountAuth(),
    { provide: API_BASE_URL, useValue: apiUrl },
    {
      provide: APP_INITIALIZER,
      useFactory: initializeApp,
      multi: true,
    },
  ],
};

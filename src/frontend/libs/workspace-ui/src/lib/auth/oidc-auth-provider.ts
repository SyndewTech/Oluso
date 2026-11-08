import { Injectable, EnvironmentProviders, inject, makeEnvironmentProviders } from '@angular/core';
import { Observable } from 'rxjs';
import { filter, map, share } from 'rxjs/operators';
import { AbstractSecurityStorage, EventTypes, OidcSecurityService, OpenIdConfiguration, PublicEventsService, provideAuth } from 'angular-auth-oidc-client';
import { AuthProvider, AuthRefreshResult, RememberMeSecurityStorage, RenewAwareAuthProvider } from '@oluso/auth';
import { workspaceOidcConfig } from './oidc.config';

@Injectable({ providedIn: 'root' })
export class OidcAuthProvider extends RenewAwareAuthProvider {
  private readonly oidc = inject(OidcSecurityService);

  readonly isAuthenticated$ = this.oidc.isAuthenticated$.pipe(
    map(({ isAuthenticated }) => isAuthenticated)
  );

  constructor() {
    super();
    // Bridge the library's renew lifecycle so a 401-triggered refresh waits for a
    // renew that is already running instead of reading its refusal as "signed out".
    const events = inject(PublicEventsService).registerForEvents().pipe(share());
    this.trackRenew(
      events.pipe(filter((e) => e.type === EventTypes.SilentRenewStarted)),
      events.pipe(
        filter((e) => e.type === EventTypes.NewAuthenticationResult || e.type === EventTypes.SilentRenewFailed),
        map((e) =>
          e.type === EventTypes.NewAuthenticationResult &&
          !!(e.value as { isAuthenticated?: boolean } | undefined)?.isAuthenticated
        )
      )
    );
  }

  getAccessToken(): Observable<string | null> {
    return this.oidc.getAccessToken().pipe(map((t) => t ?? null));
  }

  protected refreshOnce(): Observable<AuthRefreshResult> {
    return this.oidc.forceRefreshSession().pipe(
      map((result) => ({
        isAuthenticated: result?.isAuthenticated ?? false,
        accessToken: result?.accessToken ?? null,
      }))
    );
  }

  authorize(): void {
    this.oidc.authorize();
  }
}

/**
 * Configures OIDC for the workspace portal.
 *
 * Pass a partial OpenIdConfiguration to override any defaults.
 * Overrides replace the base value via spread.
 */
export function provideWorkspaceAuth(overrides?: Partial<OpenIdConfiguration>): EnvironmentProviders {
  const base = workspaceOidcConfig.config as OpenIdConfiguration;
  const merged: OpenIdConfiguration = overrides ? { ...base, ...overrides } : base;
  if (overrides?.customParamsAuthRequest && base.customParamsAuthRequest) {
    merged.customParamsAuthRequest = { ...base.customParamsAuthRequest, ...overrides.customParamsAuthRequest };
  }
  return makeEnvironmentProviders([
    provideAuth({ config: merged }),
    { provide: AuthProvider, useExisting: OidcAuthProvider },
    // Tokens follow the sign-in's "Remember me": local storage when remembered, per tab otherwise.
    { provide: AbstractSecurityStorage, useClass: RememberMeSecurityStorage },
  ]);
}

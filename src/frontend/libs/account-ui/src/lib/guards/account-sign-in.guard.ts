import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map } from 'rxjs';
import { rememberReturnUrl } from '../services/account-return-url';

/**
 * Signs the visitor in before an account page, then brings them back to the page they asked for (an invitation
 * link with its token, say) rather than a fixed one. The callback page reads the remembered address.
 */
export const accountSignInGuard: CanActivateFn = (_route, state) => {
  const oidc = inject(OidcSecurityService);
  return oidc.isAuthenticated().pipe(
    map((authenticated) => {
      if (authenticated) return true;
      rememberReturnUrl(state.url);
      oidc.authorize();
      return false;
    })
  );
};

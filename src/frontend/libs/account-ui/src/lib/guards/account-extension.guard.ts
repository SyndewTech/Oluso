import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AccountAuthService } from '../services/account-auth.service';

/**
 * Route counterpart of a plugin nav item's `feature` + `requires` filter: the
 * tenant must have the feature and the signed-in account must carry the
 * extension flag a module adds to /api/account/me (e.g. `emr.isPatient`).
 */
export const accountExtensionGuard: CanActivateFn = (route) => {
  const auth = inject(AccountAuthService);
  const router = inject(Router);
  const feature = route.data['feature'] as string | undefined;
  const requires = route.data['requires'] as string | undefined;

  if (feature && !auth.hasFeature(feature)) return router.createUrlTree(['/unauthorized']);
  if (requires && !auth.hasExtensionFlag(requires)) return router.createUrlTree(['/unauthorized']);
  return true;
};

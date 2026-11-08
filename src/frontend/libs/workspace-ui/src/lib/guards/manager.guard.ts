import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { WorkspaceAuthService } from '../services/workspace-auth.service';

/**
 * Route guard that checks if the user is a manager (has is_manager claim).
 * Redirects to dashboard if not a manager.
 */
export const managerGuard: CanActivateFn = () => {
  const authService = inject(WorkspaceAuthService);
  const router = inject(Router);

  if (authService.isManager()) {
    return true;
  }

  // Redirect non-managers to dashboard
  return router.createUrlTree(['/dashboard']);
};

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { WorkspaceAuthService } from '../services/workspace-auth.service';

/**
 * Module-level access guard. Each plugin wraps its routes under a parent
 * with this guard and declares <c>feature</c> and/or <c>requiresSeat</c>
 * on <c>data</c> — same keys the app switcher / nav filters check. Keeps
 * one source of truth: a module's app definition tells you both "should
 * the user see the entry point?" and "can they navigate there directly?".
 *
 * <para>Per-route fine-grained permissions stay layered on top via
 * <c>anyPermissionGuard</c> / <c>permissionGuard</c>. This gate just
 * blocks the module wholesale when the tenant doesn't have the feature
 * or the user has no seat — saves every child route from repeating the
 * same module-level check.</para>
 *
 * <para>Usage:</para>
 * <code>
 * const routes: Route[] = [{
 *   path: '',
 *   canActivate: [moduleAccessGuard],
 *   data: { feature: 'hr', requiresSeat: 'hr' },
 *   children: [
 *     { path: 'leave', loadComponent: …, canActivate: [permissionGuard],
 *       data: { permissions: ['hr.leave.view'] } },
 *     // …
 *   ],
 * }];
 * </code>
 */
export const moduleAccessGuard: CanActivateFn = async (route) => {
  const auth = inject(WorkspaceAuthService);
  const router = inject(Router);

  // Wait for the initial auth + profile load before reading features/seats.
  // On a fresh login the OIDC callback can drive this navigation before the
  // profile lands; without this, features() is still empty and the user is
  // wrongly bounced to /unauthorized?reason=feature_unavailable.
  await auth.whenReady();

  const data = route.data ?? {};
  const feature = (data['feature'] as string | undefined) ?? undefined;
  const requiresSeat = (data['requiresSeat'] as string | undefined) ?? undefined;

  // Tenant must have the feature licensed AND the user must hold the
  // module seat. Either failing → redirect to /unauthorized; deep links
  // pasted to non-licensed users don't render a partial page.
  if (feature && !auth.hasFeature(feature)) {
    return router.createUrlTree(['/unauthorized'], {
      queryParams: { reason: 'feature_unavailable', feature },
    });
  }

  if (requiresSeat && !auth.hasModuleSeat(requiresSeat)) {
    return router.createUrlTree(['/unauthorized'], {
      queryParams: { reason: 'seat_required', module: requiresSeat },
    });
  }

  return true;
};

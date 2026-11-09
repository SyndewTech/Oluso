import { Routes } from '@angular/router';

export const VERIFICATION_ROUTES: Routes = [
  {
    path: '',
    redirectTo: 'businesses',
    pathMatch: 'full',
  },
  {
    path: 'businesses',
    loadComponent: () => import('./pages/businesses/businesses-page.component').then((m) => m.BusinessesPageComponent),
    data: { title: 'Verified businesses', helpContext: 'admin.verification.businesses' },
  },
  {
    path: 'businesses/:id',
    loadComponent: () => import('./pages/business-detail/business-detail-page.component').then((m) => m.BusinessDetailPageComponent),
    data: { title: 'Business', helpContext: 'admin.verification.businesses' },
  },
  {
    path: 'review',
    loadComponent: () => import('./pages/review-queue/review-queue-page.component').then((m) => m.ReviewQueuePageComponent),
    data: { title: 'Verification review queue', helpContext: 'admin.verification.businesses' },
  },
];

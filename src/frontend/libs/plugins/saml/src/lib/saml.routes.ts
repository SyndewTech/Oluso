import { Routes } from '@angular/router';
import { SamlDashboardComponent } from './pages/saml-dashboard/saml-dashboard.component';
import { ServiceProvidersPageComponent } from './pages/service-providers/service-providers-page.component';
import { ServiceProviderDetailsPageComponent } from './pages/service-provider-details/service-provider-details-page.component';

export const SAML_ROUTES: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full',
  },
  {
    path: 'dashboard',
    component: SamlDashboardComponent,
  },
  {
    path: 'service-providers',
    component: ServiceProvidersPageComponent,
  },
  {
    path: 'service-providers/new',
    component: ServiceProviderDetailsPageComponent,
  },
  {
    path: 'service-providers/:id',
    component: ServiceProviderDetailsPageComponent,
  },
];

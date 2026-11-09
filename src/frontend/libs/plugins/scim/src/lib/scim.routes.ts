import { Routes } from '@angular/router';

export const SCIM_ROUTES: Routes = [
  {
    path: '',
    redirectTo: 'clients',
    pathMatch: 'full',
  },
  {
    path: 'clients',
    loadComponent: () =>
      import('./pages/scim-clients/scim-clients.component').then((m) => m.ScimClientsComponent),
    data: { title: 'SCIM Clients' },
  },
  {
    path: 'clients/:id',
    loadComponent: () =>
      import('./pages/scim-client-detail/scim-client-detail.component').then(
        (m) => m.ScimClientDetailComponent
      ),
    data: { title: 'SCIM Client Details' },
  },
  {
    path: 'clients/:clientId/mappings',
    loadComponent: () =>
      import('./pages/scim-mappings/scim-mappings.component').then((m) => m.ScimMappingsComponent),
    data: { title: 'Attribute Mappings' },
  },
  {
    path: 'providers',
    loadComponent: () =>
      import('./pages/scim-providers/scim-providers.component').then((m) => m.ScimProvidersComponent),
    data: { title: 'SCIM Providers' },
  },
  {
    path: 'providers/:id',
    loadComponent: () =>
      import('./pages/scim-provider-detail/scim-provider-detail.component').then(
        (m) => m.ScimProviderDetailComponent
      ),
    data: { title: 'SCIM Provider Details' },
  },
];

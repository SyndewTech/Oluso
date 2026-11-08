import { Routes } from '@angular/router';

export const LDAP_ROUTES: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full',
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/ldap-dashboard/ldap-dashboard.component').then((m) => m.LdapDashboardComponent),
    data: { title: 'LDAP Server' },
  },
  {
    path: 'service-accounts',
    loadComponent: () =>
      import('./pages/ldap-service-accounts/ldap-service-accounts.component').then(
        (m) => m.LdapServiceAccountsComponent
      ),
    data: { title: 'Service Accounts' },
  },
];

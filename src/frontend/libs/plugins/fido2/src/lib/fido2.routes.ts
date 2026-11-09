import { Routes } from '@angular/router';
import { Fido2DashboardComponent } from './pages/fido2-dashboard/fido2-dashboard.component';
import { Fido2CredentialsComponent } from './pages/fido2-credentials/fido2-credentials.component';

export const FIDO2_ROUTES: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full',
  },
  {
    path: 'dashboard',
    component: Fido2DashboardComponent,
  },
  {
    path: 'credentials',
    component: Fido2CredentialsComponent,
  },
];

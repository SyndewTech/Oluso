// SCIM Plugin
export { SCIM_PLUGIN, getScimNavItems } from './lib/scim.plugin';
export { SCIM_ROUTES } from './lib/scim.routes';

// Models
export * from './lib/models/scim.model';
export * from './lib/models/scim-provider.model';

// Services
export { ScimService } from './lib/services/scim.service';

// Components
export { ScimClientsComponent } from './lib/pages/scim-clients/scim-clients.component';
export { ScimClientDetailComponent } from './lib/pages/scim-client-detail/scim-client-detail.component';
export { ScimMappingsComponent } from './lib/pages/scim-mappings/scim-mappings.component';
export { ScimProvidersComponent } from './lib/pages/scim-providers/scim-providers.component';
export { ScimProviderDetailComponent } from './lib/pages/scim-provider-detail/scim-provider-detail.component';

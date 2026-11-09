import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { forkJoin, of, catchError, map } from 'rxjs';
import {
  CardComponent,
  ButtonComponent,
  InputComponent,
  BadgeComponent,
  ModalComponent,
  IconComponent,
  PageSlotHostComponent,
  DetailSectionHostComponent,
  LocaleDatePipe,
  ToastService,
  extractErrorMessage,
} from '@syndew/oluso-ui-core';
import { ClientService } from '../../services/client.service';
import { PlatformClientService } from '../../services/platform-client.service';
import { ApiScopeService } from '../../services/api-scope.service';
import { IdentityResourceService } from '../../services/identity-resource.service';
import { ResourceService } from '../../services/resource.service';
import { ResourceDto } from '../../models/resource.model';
import { ApiScopeDto } from '../../models/api-scope.model';
import { IdentityResourceDto } from '../../models/identity-resource.model';
import { ClientConnectionPanelComponent } from '../../components/client-connection-panel/client-connection-panel.component';
import { RoleService } from '../../services/role.service';
import { UserService } from '../../services/user.service';
import { IdentityProviderService } from '../../services/identity-provider.service';
import { Client } from '../../models/client.model';
import { ClientRequest } from '../../models/client-request.model';
import { ClientClaim } from '../../models/client-claim.model';
import { ClientAuthorizeParameter } from '../../models/client-authorize-parameter.model';
import { ClientProperty } from '../../models/client-property.model';
import { ClientSecret } from '../../models/client-secret.model';
import { AllowedUser } from '../../models/allowed-user.model';
import { RoleDto } from '../../models/role.model';
import { UserDto } from '../../models/user.model';
import { IdentityProvider } from '../../models/identity-provider.model';
import { ServiceApiCatalogDto, ServiceApiScopeDto } from '../../models/service-api-scope.model';

type TabId = 'basic' | 'authentication' | 'tokens' | 'uris' | 'scopes' | 'service' | 'logout' | 'access' | 'claims' | 'advanced' | 'connection';

/**
 * Client property that makes scope validation all-or-nothing (server: ScopeValidationContext.StrictScopesProperty).
 * Edited by the switch on the Scopes tab, so it's left out of the generic properties list.
 */
export const STRICT_SCOPES_PROPERTY = 'strict_scopes';

/** Mirrors the server's reading of the property: "true" (any case) or "1". */
function isTrueProperty(value: string | null | undefined): boolean {
  const v = value?.trim() ?? '';
  return v.toLowerCase() === 'true' || v === '1';
}

interface Tab {
  id: TabId;
  label: string;
  editOnly?: boolean;
}

@Component({
  selector: 'admin-client-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CardComponent,
    ButtonComponent,
    InputComponent,
    BadgeComponent,
    ModalComponent,
    IconComponent,
    PageSlotHostComponent,
    DetailSectionHostComponent,
    ClientConnectionPanelComponent,
    LocaleDatePipe,
  ],
  templateUrl: './client-detail-page.component.html',
})
export class ClientDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly clientService = inject(ClientService);
  private readonly platformClientService = inject(PlatformClientService);
  private readonly roleService = inject(RoleService);
  private readonly userService = inject(UserService);
  private readonly identityProviderService = inject(IdentityProviderService);
  private readonly apiScopeService = inject(ApiScopeService);
  private readonly identityResourceService = inject(IdentityResourceService);
  private readonly resourceService = inject(ResourceService);
  private readonly toast = inject(ToastService);

  readonly isNew = signal(false);
  readonly platformMode = signal(false);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly activeTab = signal<TabId>('basic');
  readonly showDeleteModal = signal(false);

  // Secrets management
  readonly showSecretModal = signal(false);
  readonly newSecretValue = signal('');
  readonly showDeleteSecretModal = signal(false);
  readonly secretToDelete = signal<ClientSecret | null>(null);
  readonly regeneratingSecret = signal(false);
  readonly deletingSecretId = signal<number | null>(null);

  readonly client = signal<Client | null>(null);

  // Available roles, users, and identity providers for dropdowns
  readonly availableRoles = signal<RoleDto[]>([]);
  readonly availableUsers = signal<UserDto[]>([]);
  readonly availableIdentityProviders = signal<IdentityProvider[]>([]);

  // Service API (machine-to-machine) permission catalog
  readonly serviceApiCatalog = signal<ServiceApiCatalogDto | null>(null);
  /** Identity scopes from the server: its built-in standard scopes and the organisation's own. */
  readonly identityScopes = signal<IdentityResourceDto[]>([]);
  /** The identity scopes couldn't be loaded. */
  readonly identityScopesLoadFailed = signal(false);
  /** Identity scopes offered on the Scopes tab: the server's built-in standard scopes, then the organisation's own. */
  readonly identityScopeOptions = computed(() =>
    this.identityScopes().map((s) => ({ name: s.name, title: s.displayName || s.name })));
  readonly apiScopes = signal<ApiScopeDto[]>([]);
  /** Registered APIs (resource indicators) the client can be limited to. */
  readonly resources = signal<ResourceDto[]>([]);
  /** API scopes the client may pick here: enabled, and not the Service API's own (managed on its tab). */
  readonly selectableApiScopes = computed(() =>
    this.apiScopes().filter((s) => s.enabled && !this.isManagedServiceScope(s.name)));

  /** Allowed scopes not offered as a chip above: typed-in custom scopes and Service API scopes. */
  readonly otherAllowedScopes = computed(() => {
    const offered = new Set([
      ...this.identityScopeOptions().map((s) => s.name),
      ...this.selectableApiScopes().map((s) => s.name),
      this.offlineAccessScope,
    ]);
    return ((this.formData()['allowedScopes'] as string[] | undefined) ?? []).filter((s) => !offered.has(s));
  });

  /** Whether the client refuses a request outright when any requested scope can't be granted. */
  readonly strictScopes = computed(() =>
    (this.getCurrentValue('properties') ?? []).some(
      (p) => p.key === STRICT_SCOPES_PROPERTY && isTrueProperty(p.value)));

  /** Properties shown in the generic card: everything but the ones with their own control. */
  readonly customProperties = computed(() =>
    (this.getCurrentValue('properties') ?? []).filter((p) => !this.isStrictScopesProperty(p)));

  // Tenant-resolved token endpoint, for the copy-paste example
  readonly tokenEndpoint = signal('/connect/token');

  // Form state for new client
  readonly formData = signal<ClientRequest>({
    clientId: '',
    clientName: '',
    description: '',
    allowedGrantTypes: ['authorization_code'],
    allowedScopes: ['openid', 'profile'],
    // The server's defaults for a new client, set here so the form shows what will be saved:
    // a box left as it is keeps its value, and unticking one sends false.
    enabled: true,
    requireClientSecret: true,
    requirePkce: true,
    allowRememberConsent: true,
    includeJwtId: true,
    frontChannelLogoutSessionRequired: true,
    backChannelLogoutSessionRequired: true,
    enableLocalLogin: true,
  });

  // Edit state tracking
  readonly editedFields = signal<Partial<ClientRequest>>({});

  readonly createTabs: Tab[] = [
    { id: 'basic', label: 'Basic' },
    { id: 'authentication', label: 'Authentication' },
    { id: 'uris', label: 'URIs & CORS' },
    { id: 'scopes', label: 'Scopes' },
    { id: 'access', label: 'Access Control' },
  ];

  readonly editTabs: Tab[] = [
    { id: 'basic', label: 'Basic' },
    { id: 'authentication', label: 'Authentication' },
    { id: 'tokens', label: 'Tokens', editOnly: true },
    { id: 'uris', label: 'URIs & CORS' },
    { id: 'scopes', label: 'Scopes' },
    { id: 'logout', label: 'Logout', editOnly: true },
    { id: 'access', label: 'Access Control' },
    { id: 'claims', label: 'Claims & Properties', editOnly: true },
    { id: 'advanced', label: 'Advanced', editOnly: true },
    { id: 'connection', label: 'Connection', editOnly: true },
  ];

  readonly tabs = computed(() => {
    const base = this.isNew() ? this.createTabs : this.editTabs;
    if (!this.serviceApiEnabled()) {
      return base;
    }
    // Sits directly after Scopes — it's the same subject, split out because
    // these scopes are managed rather than free-text.
    const at = base.findIndex((t) => t.id === 'scopes');
    const serviceTab: Tab = { id: 'service', label: 'Service API' };
    return [...base.slice(0, at + 1), serviceTab, ...base.slice(at + 1)];
  });

  readonly pageTitle = computed(() => {
    const prefix = this.platformMode() ? 'Platform ' : '';
    if (this.isNew()) {
      return `Create ${prefix}Client`;
    }
    return this.client()?.clientName || this.client()?.clientId || `${prefix}Client Details`;
  });

  private readonly listRoute = computed(() => this.platformMode() ? '/platform/clients' : '/clients');

  // Grant types options
  readonly grantTypes = [
    { value: 'authorization_code', label: 'Authorization Code' },
    { value: 'client_credentials', label: 'Client Credentials' },
    { value: 'refresh_token', label: 'Refresh Token' },
    { value: 'implicit', label: 'Implicit (Legacy)' },
    { value: 'password', label: 'Resource Owner Password (Legacy)' },
    { value: 'urn:ietf:params:oauth:grant-type:device_code', label: 'Device Code' },
    { value: 'urn:openid:params:grant-type:ciba', label: 'CIBA' },
  ];

  // Common scopes
  /** Not a registered resource, but a scope clients ask for to get a refresh token. */
  readonly offlineAccessScope = 'offline_access';

  // Input fields for collections
  redirectUriInput = '';
  postLogoutUriInput = '';
  corsOriginInput = '';
  scopeInput = '';

  // Access Control inputs
  selectedRoleInput = '';
  selectedUserInput = '';
  selectedIdpInput = '';

  // Claims & Properties inputs
  newClaimType = '';
  newClaimValue = '';
  newPropertyKey = '';
  newPropertyValue = '';

  // Authorize parameter claim inputs
  newAuthParamName = '';
  newAuthParamClaim = '';
  newAuthParamRequired = false;

  ngOnInit(): void {
    // Detect platform mode from route data
    this.platformMode.set(this.route.snapshot.data['platformMode'] === true);

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam === 'new' || !idParam) {
      this.isNew.set(true);
      this.loading.set(false);
    } else {
      const id = parseInt(idParam, 10);
      if (!isNaN(id)) {
        this.loadClient(id);
      } else {
        this.router.navigate([this.listRoute()]);
      }
    }
    this.loadAvailableRolesAndUsers();
  }

  private loadAvailableRolesAndUsers(): void {
    forkJoin({
      roles: this.roleService.getAllRoles().pipe(catchError(() => of([] as RoleDto[]))),
      users: this.userService.getUsers({ page: 1, pageSize: 100 }).pipe(catchError(() => of({ items: [] as UserDto[], total: 0, page: 1, pageSize: 100, totalPages: 0 }))),
      identityProviders: this.identityProviderService.getAll().pipe(catchError(() => of([] as IdentityProvider[]))),
      serviceApiCatalog: this.apiScopeService.getServiceApiCatalog().pipe(catchError(() => of(null))),
      identityScopes: this.identityResourceService.getAll().pipe(catchError(() => {
        this.identityScopesLoadFailed.set(true);
        return of([] as IdentityResourceDto[]);
      })),
      apiScopes: this.apiScopeService.getAllScopes().pipe(catchError(() => of([] as ApiScopeDto[]))),
      resources: this.resourceService.getAll({ page: 1, pageSize: 200 }).pipe(
        map((page) => page.items),
        catchError(() => of([] as ResourceDto[]))),
    }).subscribe({
      next: ({ roles, users, identityProviders, serviceApiCatalog, identityScopes, apiScopes, resources }) => {
        this.resources.set(resources.filter((r) => r.enabled));
        this.identityScopes.set(identityScopes.filter((s) => s.enabled));
        this.apiScopes.set(apiScopes);
        this.availableRoles.set(roles);
        this.availableUsers.set(users.items);
        this.availableIdentityProviders.set(identityProviders);
        this.serviceApiCatalog.set(serviceApiCatalog);
      },
    });
  }

  loadClient(id: number): void {
    this.loading.set(true);
    const obs = this.platformMode()
      ? this.platformClientService.getById(id)
      : this.clientService.getClient(id);

    obs.subscribe({
      next: (client) => {
        this.client.set(client);
        this.loading.set(false);
        this.loadTokenEndpoint(id);
      },
      error: (error) => {
        console.error('Failed to load client:', error);
        this.loading.set(false);
        this.router.navigate([this.listRoute()]);
      },
    });
  }

  /**
   * Resolves the tenant's real token endpoint (custom domain and issuer
   * overrides applied) for the Service API example.
   */
  private loadTokenEndpoint(id: number): void {
    const obs = this.platformMode()
      ? this.platformClientService.getClientConnection(id)
      : this.clientService.getClientConnection(id);

    obs.pipe(catchError(() => of(null))).subscribe((connection) => {
      if (connection?.tokenEndpoint) {
        this.tokenEndpoint.set(connection.tokenEndpoint);
      }
    });
  }

  setActiveTab(tabId: TabId): void {
    this.activeTab.set(tabId);
  }

  onSave(): void {
    if (this.isNew()) {
      this.createClient();
    } else {
      this.updateClient();
    }
  }

  createClient(): void {
    this.saving.set(true);
    const request = this.formData();

    if (this.platformMode()) {
      this.platformClientService.create(request).subscribe({
        next: (client) => {
          this.saving.set(false);
          this.router.navigate(['/platform/clients', client.id]);
        },
        error: (error) => {
          console.error('Failed to create client:', error);
          this.toast.error(extractErrorMessage(error, 'Failed to create client'));
          this.saving.set(false);
        },
      });
    } else {
      this.clientService.createClient(request).subscribe({
        next: (client) => {
          this.saving.set(false);
          this.router.navigate(['/clients', client.id]);
        },
        error: (error) => {
          console.error('Failed to create client:', error);
          this.toast.error(extractErrorMessage(error, 'Failed to create client'));
          this.saving.set(false);
        },
      });
    }
  }

  updateClient(): void {
    const client = this.client();
    if (!client) return;

    this.saving.set(true);
    const fields = this.editedFields();

    if (this.platformMode()) {
      this.platformClientService.update(client.id, fields).subscribe({
        next: (updatedClient) => {
          this.client.set(updatedClient);
          this.editedFields.set({});
          this.saving.set(false);
        },
        error: (error) => {
          console.error('Failed to update client:', error);
          this.toast.error(extractErrorMessage(error, 'Failed to update client'));
          this.saving.set(false);
        },
      });
    } else {
      this.clientService.updateClient(client.id, fields).subscribe({
        next: (updatedClient) => {
          this.client.set(updatedClient);
          this.editedFields.set({});
          this.saving.set(false);
        },
        error: (error) => {
          console.error('Failed to update client:', error);
          this.toast.error(extractErrorMessage(error, 'Failed to update client'));
          this.saving.set(false);
        },
      });
    }
  }

  onDelete(): void {
    this.showDeleteModal.set(true);
  }

  confirmDelete(): void {
    const client = this.client();
    if (!client) return;

    if (this.platformMode()) {
      this.platformClientService.delete(client.id).subscribe({
        next: () => {
          this.showDeleteModal.set(false);
          this.router.navigate([this.listRoute()]);
        },
        error: (error) => {
          console.error('Failed to delete client:', error);
          this.showDeleteModal.set(false);
        },
      });
    } else {
      this.clientService.deleteClient(client.id).subscribe({
        next: () => {
          this.showDeleteModal.set(false);
          this.router.navigate([this.listRoute()]);
        },
        error: (error) => {
          console.error('Failed to delete client:', error);
          this.showDeleteModal.set(false);
        },
      });
    }
  }

  cancelDelete(): void {
    this.showDeleteModal.set(false);
  }

  onCancel(): void {
    this.router.navigate([this.listRoute()]);
  }

  // Form field updates
  updateFormField<K extends keyof ClientRequest>(field: K, value: ClientRequest[K]): void {
    if (this.isNew()) {
      this.formData.update((data) => ({ ...data, [field]: value }));
    } else {
      this.editedFields.update((fields) => ({ ...fields, [field]: value }));
    }
  }

  getCurrentValue<K extends keyof Client>(field: K): Client[K] | undefined {
    if (this.isNew()) {
      return (this.formData() as unknown as Client)[field];
    }
    const edited = this.editedFields()[field as keyof ClientRequest];
    if (edited !== undefined) {
      return edited as Client[K];
    }
    return this.client()?.[field];
  }

  // Grant type toggles
  isGrantTypeEnabled(grantType: string): boolean {
    const grantTypes = this.getCurrentValue('allowedGrantTypes') || [];
    return grantTypes.includes(grantType);
  }

  isResourceAllowed(uri: string): boolean {
    return (this.getCurrentValue('allowedResources') || []).includes(uri);
  }

  toggleAllowedResource(uri: string): void {
    const current = this.getCurrentValue('allowedResources') || [];
    this.updateFormField('allowedResources', current.includes(uri) ? current.filter((r) => r !== uri) : [...current, uri]);
  }

  isIntrospectionResource(uri: string): boolean {
    return (this.getCurrentValue('introspectionResources') || []).includes(uri);
  }

  toggleIntrospectionResource(uri: string): void {
    const current = this.getCurrentValue('introspectionResources') || [];
    this.updateFormField('introspectionResources', current.includes(uri) ? current.filter((r) => r !== uri) : [...current, uri]);
  }

  toggleGrantType(grantType: string): void {
    const current = this.getCurrentValue('allowedGrantTypes') || [];
    const removing = current.includes(grantType);
    const updated = removing
      ? current.filter((gt) => gt !== grantType)
      : [...current, grantType];
    this.updateFormField('allowedGrantTypes', updated);

    // Removing a grant type the Service API depends on would leave the client in
    // a state the server rejects (service scopes with no way to mint them), so
    // switch Service API off with it rather than failing the save.
    const catalog = this.serviceApiCatalog();
    if (removing && catalog?.requiredGrantTypes.includes(grantType) && this.serviceApiEnabled()) {
      const managed = this.managedServiceScopes();
      const scopes = this.getCurrentValue('allowedScopes') || [];
      this.updateFormField('allowedScopes', scopes.filter((s) => !managed.includes(s)));

      if (this.activeTab() === 'service') {
        this.activeTab.set('authentication');
      }

      this.toast.warning('Service API access turned off — it requires the Client Credentials grant.');
    }
  }

  // Scope management
  isScopeEnabled(scope: string): boolean {
    const scopes = this.getCurrentValue('allowedScopes') || [];
    return scopes.includes(scope);
  }

  toggleScope(scope: string): void {
    const current = this.getCurrentValue('allowedScopes') || [];
    const updated = current.includes(scope)
      ? current.filter((s) => s !== scope)
      : [...current, scope];
    this.updateFormField('allowedScopes', updated);
  }

  // Service API (machine-to-machine)
  readonly serviceApiEnabled = computed(() => {
    const base = this.serviceApiCatalog()?.baseScope;
    if (!base) return false;
    return (this.getCurrentValue('allowedScopes') || []).includes(base);
  });

  readonly serviceApiGroups = computed(() => {
    const scopes = this.serviceApiCatalog()?.scopes || [];
    const groups: { name: string; scopes: ServiceApiScopeDto[] }[] = [];
    for (const scope of scopes) {
      const group = groups.find((g) => g.name === scope.group);
      if (group) {
        group.scopes.push(scope);
      } else {
        groups.push({ name: scope.group, scopes: [scope] });
      }
    }
    return groups;
  });

  /** The scope string an integrator should request: base plus what's granted. */
  readonly serviceApiRequestScopes = computed(() => {
    const catalog = this.serviceApiCatalog();
    if (!catalog) return '';
    const granted = this.getCurrentValue('allowedScopes') || [];
    return [catalog.baseScope, ...catalog.scopes.map((s) => s.name)]
      .filter((s) => granted.includes(s))
      .join(' ');
  });

  /** Every scope name the Service API owns — base plus granular. */
  private managedServiceScopes(): string[] {
    const catalog = this.serviceApiCatalog();
    if (!catalog) return [];
    return [catalog.baseScope, ...catalog.scopes.map((s) => s.name)];
  }

  /** True for scopes the Service API tab owns, so the Scopes tab won't edit them. */
  isManagedServiceScope(scope: string): boolean {
    return this.managedServiceScopes().includes(scope);
  }

  toggleServiceApi(): void {
    const catalog = this.serviceApiCatalog();
    if (!catalog) return;

    const current = this.getCurrentValue('allowedScopes') || [];

    if (this.serviceApiEnabled()) {
      const managed = this.managedServiceScopes();
      this.updateFormField('allowedScopes', current.filter((s) => !managed.includes(s)));
      if (this.activeTab() === 'service') {
        this.activeTab.set('authentication');
      }
      return;
    }

    this.updateFormField('allowedScopes', [...current, catalog.baseScope]);

    // The server rejects service scopes without client_credentials, so turn the
    // grant on with the toggle rather than failing the save.
    const grantTypes = this.getCurrentValue('allowedGrantTypes') || [];
    const missing = catalog.requiredGrantTypes.filter((g) => !grantTypes.includes(g));
    if (missing.length > 0) {
      this.updateFormField('allowedGrantTypes', [...grantTypes, ...missing]);
    }
  }

  addScope(): void {
    if (!this.scopeInput.trim()) return;
    const current = this.getCurrentValue('allowedScopes') || [];
    if (!current.includes(this.scopeInput.trim())) {
      this.updateFormField('allowedScopes', [...current, this.scopeInput.trim()]);
    }
    this.scopeInput = '';
  }

  removeScope(scope: string): void {
    const current = this.getCurrentValue('allowedScopes') || [];
    this.updateFormField('allowedScopes', current.filter((s) => s !== scope));
  }

  // URI management
  addRedirectUri(): void {
    if (!this.redirectUriInput.trim()) return;
    const current = this.getCurrentValue('redirectUris') || [];
    if (!current.includes(this.redirectUriInput.trim())) {
      this.updateFormField('redirectUris', [...current, this.redirectUriInput.trim()]);
    }
    this.redirectUriInput = '';
  }

  removeRedirectUri(uri: string): void {
    const current = this.getCurrentValue('redirectUris') || [];
    this.updateFormField('redirectUris', current.filter((u) => u !== uri));
  }

  addPostLogoutUri(): void {
    if (!this.postLogoutUriInput.trim()) return;
    const current = this.getCurrentValue('postLogoutRedirectUris') || [];
    if (!current.includes(this.postLogoutUriInput.trim())) {
      this.updateFormField('postLogoutRedirectUris', [...current, this.postLogoutUriInput.trim()]);
    }
    this.postLogoutUriInput = '';
  }

  removePostLogoutUri(uri: string): void {
    const current = this.getCurrentValue('postLogoutRedirectUris') || [];
    this.updateFormField('postLogoutRedirectUris', current.filter((u) => u !== uri));
  }

  addCorsOrigin(): void {
    if (!this.corsOriginInput.trim()) return;
    const current = this.getCurrentValue('allowedCorsOrigins') || [];
    if (!current.includes(this.corsOriginInput.trim())) {
      this.updateFormField('allowedCorsOrigins', [...current, this.corsOriginInput.trim()]);
    }
    this.corsOriginInput = '';
  }

  removeCorsOrigin(origin: string): void {
    const current = this.getCurrentValue('allowedCorsOrigins') || [];
    this.updateFormField('allowedCorsOrigins', current.filter((o) => o !== origin));
  }

  // Access Control - Roles
  addRole(): void {
    if (!this.selectedRoleInput) return;
    const current = this.getCurrentValue('allowedRoles') || [];
    if (!current.includes(this.selectedRoleInput)) {
      this.updateFormField('allowedRoles', [...current, this.selectedRoleInput]);
    }
    this.selectedRoleInput = '';
  }

  removeRole(role: string): void {
    const current = this.getCurrentValue('allowedRoles') || [];
    this.updateFormField('allowedRoles', current.filter((r) => r !== role));
  }

  getAvailableRolesForDropdown(): RoleDto[] {
    const current = this.getCurrentValue('allowedRoles') || [];
    return this.availableRoles().filter((r) => !current.includes(r.name));
  }

  // Access Control - Users
  addUser(): void {
    if (!this.selectedUserInput) return;
    const selectedUser = this.availableUsers().find((u) => u.id === this.selectedUserInput);
    if (!selectedUser) return;

    const current = this.getCurrentValue('allowedUsers') || [];
    if (!current.some((u) => u.subjectId === selectedUser.id)) {
      const newUser: AllowedUser = {
        subjectId: selectedUser.id,
        displayName: selectedUser.email || selectedUser.userName || selectedUser.id,
      };
      this.updateFormField('allowedUsers', [...current, newUser]);
    }
    this.selectedUserInput = '';
  }

  removeUser(subjectId: string): void {
    const current = this.getCurrentValue('allowedUsers') || [];
    this.updateFormField('allowedUsers', current.filter((u) => u.subjectId !== subjectId));
  }

  getAvailableUsersForDropdown(): UserDto[] {
    const current = this.getCurrentValue('allowedUsers') || [];
    return this.availableUsers().filter((u) => !current.some((au) => au.subjectId === u.id));
  }

  // Access Control - Identity Provider Restrictions
  addIdpRestriction(): void {
    if (!this.selectedIdpInput) return;
    const current = this.getCurrentValue('identityProviderRestrictions') || [];
    if (!current.includes(this.selectedIdpInput)) {
      this.updateFormField('identityProviderRestrictions', [...current, this.selectedIdpInput]);
    }
    this.selectedIdpInput = '';
  }

  removeIdpRestriction(idp: string): void {
    const current = this.getCurrentValue('identityProviderRestrictions') || [];
    this.updateFormField('identityProviderRestrictions', current.filter((i) => i !== idp));
  }

  getAvailableIdpsForDropdown(): IdentityProvider[] {
    const current = this.getCurrentValue('identityProviderRestrictions') || [];
    return this.availableIdentityProviders().filter((idp) => !current.includes(idp.scheme));
  }

  // Claims & Properties - Claims
  addClaim(): void {
    if (!this.newClaimType.trim() || !this.newClaimValue.trim()) return;
    const current = this.getCurrentValue('claims') || [];
    const newClaim: ClientClaim = {
      type: this.newClaimType.trim(),
      value: this.newClaimValue.trim(),
    };
    this.updateFormField('claims', [...current, newClaim]);
    this.newClaimType = '';
    this.newClaimValue = '';
  }

  removeClaim(index: number): void {
    const current = this.getCurrentValue('claims') || [];
    this.updateFormField('claims', current.filter((_, i) => i !== index));
  }

  // Claims & Properties - Properties
  isStrictScopesProperty(prop: ClientProperty): boolean {
    return prop.key === STRICT_SCOPES_PROPERTY;
  }

  /** On writes strict_scopes=true; off removes the property rather than storing "false". */
  setStrictScopes(on: boolean): void {
    const rest = (this.getCurrentValue('properties') ?? []).filter((p) => !this.isStrictScopesProperty(p));
    this.updateFormField('properties', on ? [...rest, { key: STRICT_SCOPES_PROPERTY, value: 'true' }] : rest);
  }

  addProperty(): void {
    if (!this.newPropertyKey.trim() || !this.newPropertyValue.trim()) return;
    if (this.newPropertyKey.trim() === STRICT_SCOPES_PROPERTY) {
      // Has its own switch on the Scopes tab; keep a single source of truth.
      this.setStrictScopes(isTrueProperty(this.newPropertyValue));
      this.newPropertyKey = '';
      this.newPropertyValue = '';
      return;
    }
    const current = this.getCurrentValue('properties') || [];
    const newProperty: ClientProperty = {
      key: this.newPropertyKey.trim(),
      value: this.newPropertyValue.trim(),
    };
    this.updateFormField('properties', [...current, newProperty]);
    this.newPropertyKey = '';
    this.newPropertyValue = '';
  }

  removeProperty(index: number): void {
    const current = this.getCurrentValue('properties') || [];
    this.updateFormField('properties', current.filter((_, i) => i !== index));
  }

  // Claims & Properties - Authorize parameter claims
  addAuthorizeParameter(): void {
    if (!this.newAuthParamName.trim() || !this.newAuthParamClaim.trim()) return;
    const current = this.getCurrentValue('allowedAuthorizeParameters') || [];
    const newParam: ClientAuthorizeParameter = {
      parameter: this.newAuthParamName.trim(),
      claimType: this.newAuthParamClaim.trim(),
      required: this.newAuthParamRequired,
    };
    this.updateFormField('allowedAuthorizeParameters', [...current, newParam]);
    this.newAuthParamName = '';
    this.newAuthParamClaim = '';
    this.newAuthParamRequired = false;
  }

  removeAuthorizeParameter(index: number): void {
    const current = this.getCurrentValue('allowedAuthorizeParameters') || [];
    this.updateFormField('allowedAuthorizeParameters', current.filter((_, i) => i !== index));
  }

  // Helper to format duration
  formatDuration(seconds: number | null | undefined): string {
    if (!seconds) return '-';
    if (seconds < 60) return `${seconds} seconds`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)} minutes`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)} hours`;
    return `${Math.floor(seconds / 86400)} days`;
  }

  // Helper for the read-only metadata strip on the page header
  formatDate(iso: string | null | undefined): string {
    if (!iso) return 'Never';
    return new Date(iso).toLocaleString();
  }

  // Handle useJourneyFlow change with special handling for "inherit"
  onUseJourneyFlowChange(value: string): void {
    if (value === 'inherit') {
      // Clear the useJourneyFlow field and set the reset flag
      this.editedFields.update((fields) => {
        const updated = { ...fields };
        delete (updated as Record<string, unknown>)['useJourneyFlow'];
        return { ...updated, resetUseJourneyFlowToInherit: true };
      });
    } else {
      // Set useJourneyFlow and clear the reset flag
      this.editedFields.update((fields) => {
        const updated = { ...fields };
        delete (updated as Record<string, unknown>)['resetUseJourneyFlowToInherit'];
        return { ...updated, useJourneyFlow: value === 'journey' };
      });
    }
  }

  // Secrets management
  regenerateSecret(): void {
    const client = this.client();
    if (!client) return;

    this.regeneratingSecret.set(true);
    const obs = this.platformMode()
      ? this.platformClientService.regenerateSecret(client.id)
      : this.clientService.regenerateSecret(client.id);

    obs.subscribe({
      next: (result) => {
        this.newSecretValue.set(result.clientSecret);
        this.showSecretModal.set(true);
        this.regeneratingSecret.set(false);
        this.loadClient(client.id);
      },
      error: (error) => {
        console.error('Failed to regenerate secret:', error);
        this.regeneratingSecret.set(false);
      },
    });
  }

  confirmDeleteSecret(secret: ClientSecret): void {
    this.secretToDelete.set(secret);
    this.showDeleteSecretModal.set(true);
  }

  cancelDeleteSecret(): void {
    this.secretToDelete.set(null);
    this.showDeleteSecretModal.set(false);
  }

  deleteSecret(): void {
    const client = this.client();
    const secret = this.secretToDelete();
    if (!client || !secret) return;

    this.deletingSecretId.set(secret.id);
    const obs = this.platformMode()
      ? this.platformClientService.removeClientSecret(client.id, secret.id)
      : this.clientService.removeClientSecret(client.id, secret.id);

    obs.subscribe({
      next: () => {
        this.deletingSecretId.set(null);
        this.showDeleteSecretModal.set(false);
        this.secretToDelete.set(null);
        this.loadClient(client.id);
      },
      error: (error) => {
        console.error('Failed to delete secret:', error);
        this.deletingSecretId.set(null);
      },
    });
  }

  closeSecretModal(): void {
    this.showSecretModal.set(false);
    this.newSecretValue.set('');
  }

  copyAndCloseSecret(): void {
    navigator.clipboard.writeText(this.newSecretValue());
    this.closeSecretModal();
  }
}

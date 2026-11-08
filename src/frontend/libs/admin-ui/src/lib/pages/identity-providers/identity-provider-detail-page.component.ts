import { Component, inject, signal, OnInit, computed, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  ModalComponent,
  PageSlotHostComponent,
  DetailSectionHostComponent,
  IconComponent,
  LocaleDatePipe,
  ToastService,
  extractErrorMessage,
} from '@oluso/ui-core';
import { IdentityProviderService } from '../../services/identity-provider.service';
import { RoleService } from '../../services/role.service';
import { RoleDto } from '../../models/role.model';
import {
  IdentityProviderEdit,
  CreateIdentityProviderRequest,
  ProviderTypeInfo,
  ProviderTypes,
  ExternalProviderType,
  ServerInfo,
  SchemeAvailability,
  ProviderIconDiscovery,
  ProviderIconCandidate,
} from '../../models/identity-provider.model';

export type AccessControlMode = 'open' | 'emailAllowlist' | 'claimGate';

export interface AccessControlConfig {
  mode: AccessControlMode;
  allowedEmails?: string[];
  claimGate?: { claimPath: string; anyOf: string[] };
}

@Component({
  selector: 'admin-identity-provider-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CardComponent,
    ButtonComponent,
    ModalComponent,
    PageSlotHostComponent,
    DetailSectionHostComponent,
    IconComponent,
    LocaleDatePipe,
  ],
  templateUrl: './identity-provider-detail-page.component.html',
})
export class IdentityProviderDetailPageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly idpService = inject(IdentityProviderService);
  private readonly roleService = inject(RoleService);
  private readonly toast = inject(ToastService);

  readonly ProviderTypes = ProviderTypes;
  readonly tenantRoles = signal<RoleDto[]>([]);

  // Buffer fields for the email and claim-anyOf tag inputs. Bound to
  // local string inputs; pressing Enter / clicking "Add" pushes the
  // trimmed value into the corresponding array on the config.
  newAllowedEmail = '';
  newClaimAnyOfValue = '';
  newDefaultRole = '';
  customClaimPath = '';

  /** True when the admin has switched the Microsoft Tenant ID select to
   * "Specific tenant ID or domain…". Tracked explicitly (rather than derived
   * from the config value) so the text input renders immediately after the
   * user picks the custom option, even before they've typed anything. */
  readonly microsoftCustomTenant = signal(false);

  private syncMicrosoftCustomTenantFromConfig(): void {
    const value = this.getConfig<string>('tenantId', '');
    const presets = ['common', 'organizations', 'consumers', ''];
    this.microsoftCustomTenant.set(!presets.includes(value));
  }

  setMicrosoftTenantPreset(value: string): void {
    if (value === '__custom__') {
      this.microsoftCustomTenant.set(true);
      return;
    }
    this.microsoftCustomTenant.set(false);
    this.updateConfig('tenantId', value);
  }

  readonly commonClaimPaths: { value: string; label: string; hint: string }[] = [
    { value: 'groups', label: 'groups', hint: 'group memberships from the IdP' },
    { value: 'roles', label: 'roles', hint: 'application roles' },
    { value: 'app_roles', label: 'app_roles', hint: 'app-scoped role assignments' },
    { value: 'wids', label: 'wids', hint: 'directory-level role identifiers' },
  ];

  // Tab navigation. Existing cards are unchanged — they're just wrapped
  // in a tab container so the page isn't a 1500-line scroll.
  readonly activeTab = signal<'general' | 'connection' | 'access' | 'advanced'>('general');
  readonly tabs: { id: 'general' | 'connection' | 'access' | 'advanced'; label: string; icon: string }[] = [
    { id: 'general', label: 'General', icon: 'menu' },
    { id: 'connection', label: 'Connection', icon: 'link' },
    { id: 'access', label: 'Access Control', icon: 'shield' },
    { id: 'advanced', label: 'Advanced', icon: 'settings' },
  ];

  setActiveTab(id: 'general' | 'connection' | 'access' | 'advanced'): void {
    this.activeTab.set(id);
  }

  /**
   * Whether a tab has unresolved validation issues — used to render a small
   * "!" badge on the tab so admins notice errors hiding off-screen. Mirrors
   * the same checks the save button uses; expand as more validation lands.
   */
  tabHasErrors(id: 'general' | 'connection' | 'access' | 'advanced'): boolean {
    if (id === 'general') {
      if (this.isNew()) {
        const noScheme = !this.formData.scheme;
        const noType = this.formData.providerType === undefined;
        const availability = this.schemeAvailability();
        const schemeUnavailable = availability !== null && !availability.available;
        return noScheme || noType || schemeUnavailable;
      }
      return false;
    }
    if (id === 'access') {
      const ac = this.getAccessControl();
      if (ac.mode === 'emailAllowlist' && (!ac.allowedEmails || ac.allowedEmails.length === 0)) return true;
      if (ac.mode === 'claimGate') {
        const gate = ac.claimGate;
        if (!gate || !gate.claimPath || !gate.anyOf || gate.anyOf.length === 0) return true;
      }
      return false;
    }
    return false;
  }

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly provider = signal<IdentityProviderEdit | null>(null);
  readonly providerTypes = signal<ProviderTypeInfo[]>([]);
  readonly serverInfo = signal<ServerInfo | null>(null);
  readonly schemeAvailability = signal<SchemeAvailability | null>(null);
  readonly checkingScheme = signal(false);
  readonly testResult = signal<{ success: boolean; message: string } | null>(null);
  readonly revealingSecret = signal<string | null>(null);

  readonly showDeleteModal = signal(false);

  readonly isNew = computed(() => this.route.snapshot.paramMap.get('id') === 'new');

  readonly discoveringIcons = signal(false);
  readonly settingIcon = signal(false);
  readonly iconDiscovery = signal<ProviderIconDiscovery | null>(null);

  discoverIcons(): void {
    const provider = this.provider();
    if (!provider) return;
    this.discoveringIcons.set(true);
    this.iconDiscovery.set(null);
    this.idpService.discoverIcons(provider.id).subscribe({
      next: (found) => {
        this.iconDiscovery.set(found);
        this.discoveringIcons.set(false);
        if (found.candidates.length === 0) {
          this.toast.info('No usable icons were found on the provider site');
        }
      },
      error: (error) => {
        this.discoveringIcons.set(false);
        this.toast.error(extractErrorMessage(error, 'Icon discovery failed'));
      },
    });
  }

  useIcon(candidate: ProviderIconCandidate): void {
    const provider = this.provider();
    if (!provider) return;
    this.settingIcon.set(true);
    this.idpService.setIcon(provider.id, candidate.dataUrl).subscribe({
      next: (updated) => {
        this.formData.iconUrl = updated.iconUrl;
        this.iconDiscovery.set(null);
        this.settingIcon.set(false);
        this.toast.success('Provider icon updated');
      },
      error: (error) => {
        this.settingIcon.set(false);
        this.toast.error(extractErrorMessage(error, 'Failed to set icon'));
      },
    });
  }

  formData: Partial<CreateIdentityProviderRequest> = {
    scheme: '',
    displayName: '',
    enabled: true,
    configuration: {},
  };

  private schemeCheckTimeout?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    this.loadProviderTypes();
    this.loadServerInfo();
    this.loadTenantRoles();

    if (id && id !== 'new') {
      this.loadProvider(parseInt(id, 10));
    } else {
      this.loading.set(false);
    }
  }

  private loadTenantRoles(): void {
    this.roleService.getAllRoles().subscribe({
      next: (roles) => this.tenantRoles.set(roles),
      error: (err) => console.error('Failed to load tenant roles:', err),
    });
  }

  ngOnDestroy(): void {
    if (this.schemeCheckTimeout) {
      clearTimeout(this.schemeCheckTimeout);
    }
  }

  loadProvider(id: number): void {
    this.loading.set(true);
    this.idpService.getForEdit(id).subscribe({
      next: (provider) => {
        this.provider.set(provider);
        this.formData = {
          scheme: provider.scheme,
          displayName: provider.displayName,
          enabled: provider.enabled,
          providerType: provider.providerType,
          iconUrl: provider.iconUrl,
          displayOrder: provider.displayOrder,
          allowedClientIds: provider.allowedClientIds,
          configuration: provider.configuration || {},
        };
        this.syncMicrosoftCustomTenantFromConfig();
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load identity provider:', error);
        this.loading.set(false);
      },
    });
  }

  loadProviderTypes(): void {
    this.idpService.getProviderTypes().subscribe({
      next: (types) => this.providerTypes.set(types),
      error: (error) => console.error('Failed to load provider types:', error),
    });
  }

  loadServerInfo(): void {
    this.idpService.getServerInfo().subscribe({
      next: (info) => this.serverInfo.set(info),
      error: (error) => console.error('Failed to load server info:', error),
    });
  }

  onSchemeChange(): void {
    if (this.schemeCheckTimeout) {
      clearTimeout(this.schemeCheckTimeout);
    }

    const scheme = this.formData.scheme;
    if (!this.isNew() || !scheme || scheme.length < 2) {
      this.schemeAvailability.set(null);
      return;
    }

    this.checkingScheme.set(true);
    this.schemeCheckTimeout = setTimeout(() => {
      this.idpService.checkSchemeAvailability(scheme).subscribe({
        next: (result) => {
          this.schemeAvailability.set(result);
          this.checkingScheme.set(false);
        },
        error: (err) => {
          // 404 → endpoint says "no provider with that scheme exists",
          // which is exactly what "available" means. Same convention as
          // the username-availability check elsewhere. Other status
          // codes are silently swallowed so a transient API error
          // doesn't block the admin from typing.
          if (err?.status === 404) {
            this.schemeAvailability.set({ available: true });
          }
          this.checkingScheme.set(false);
        },
      });
    }, 300);
  }

  onBack(): void {
    this.router.navigate(['/identity-providers']);
  }

  onSave(): void {
    this.saving.set(true);

    const request = this.formData as CreateIdentityProviderRequest;

    if (this.isNew()) {
      this.idpService.create(request).subscribe({
        next: (created) => {
          this.saving.set(false);
          this.toast.success('Identity provider created');
          this.router.navigate(['/identity-providers', created.id]);
        },
        error: (error) => {
          this.saving.set(false);
          this.toast.error(extractErrorMessage(error, 'Failed to create identity provider'));
        },
      });
    } else {
      const provider = this.provider();
      if (!provider) return;

      this.idpService.update(provider.id, request).subscribe({
        next: () => {
          this.loadProvider(provider.id);
          this.saving.set(false);
          this.toast.success('Identity provider saved');
        },
        error: (error) => {
          this.saving.set(false);
          this.toast.error(extractErrorMessage(error, 'Failed to save identity provider'));
        },
      });
    }
  }

  onTest(): void {
    const provider = this.provider();
    if (!provider) return;

    this.idpService.test(provider.id).subscribe({
      next: (result) => this.testResult.set(result),
      error: (error) => {
        console.error('Test failed:', error);
        this.testResult.set({ success: false, message: 'Test failed' });
      },
    });
  }

  openDeleteModal(): void {
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
  }

  confirmDelete(): void {
    const provider = this.provider();
    if (!provider) return;

    this.idpService.delete(provider.id).subscribe({
      next: () => {
        this.router.navigate(['/identity-providers']);
      },
      error: (error) => {
        console.error('Failed to delete provider:', error);
      },
    });
  }

  updateConfig(key: string, value: unknown): void {
    this.formData.configuration = {
      ...this.formData.configuration,
      [key]: value,
    };
  }

  getConfig<T>(key: string, defaultValue?: T): T {
    return (this.formData.configuration?.[key] as T) ?? (defaultValue as T);
  }

  getConfigArray(key: string): string[] {
    const value = this.formData.configuration?.[key];
    return Array.isArray(value) ? value : [];
  }

  setConfigFromCommaSeparated(key: string, value: string): void {
    this.updateConfig(
      key,
      value
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean)
    );
  }

  setConfigFromSpaceSeparated(key: string, value: string): void {
    this.updateConfig(key, value.split(' ').filter(Boolean));
  }

  // ── Default roles ──────────────────────────────────────────────────

  getDefaultRoles(): string[] {
    return this.getConfigArray('defaultRoles');
  }

  getRoleDisplayName(roleId: string): string {
    return this.tenantRoles().find((r) => r.id === roleId)?.name ?? roleId;
  }

  /** Adds the selected role ID if it isn't already present. */
  addDefaultRole(): void {
    const roleId = this.newDefaultRole.trim();
    if (!roleId) return;
    const existing = this.getDefaultRoles();
    if (existing.includes(roleId)) {
      this.newDefaultRole = '';
      return;
    }
    this.updateConfig('defaultRoles', [...existing, roleId]);
    this.newDefaultRole = '';
  }

  removeDefaultRole(roleId: string): void {
    this.updateConfig('defaultRoles', this.getDefaultRoles().filter((r) => r !== roleId));
  }

  // ── Access control ─────────────────────────────────────────────────

  getAccessControl(): AccessControlConfig {
    const ac = this.formData.configuration?.['accessControl'] as AccessControlConfig | undefined;
    return ac ?? { mode: 'open' };
  }

  getAccessControlMode(): AccessControlMode {
    return this.getAccessControl().mode || 'open';
  }

  setAccessControlMode(mode: AccessControlMode): void {
    const current = this.getAccessControl();
    // Carry forward per-mode fields so an admin flipping between modes
    // doesn't lose what they typed. Server validation enforces the
    // required-field rules per mode at save time.
    this.updateConfig('accessControl', {
      mode,
      allowedEmails: current.allowedEmails ?? [],
      claimGate: current.claimGate ?? { claimPath: '', anyOf: [] },
    });
  }

  getAllowedEmails(): string[] {
    return this.getAccessControl().allowedEmails ?? [];
  }

  /** Add the email currently in the buffer. Validates locally; server validates on save too. */
  addAllowedEmail(): void {
    const email = this.newAllowedEmail.trim().toLowerCase();
    if (!email) return;
    if (!this.looksLikeEmail(email)) return;
    const existing = this.getAllowedEmails();
    if (existing.includes(email)) {
      this.newAllowedEmail = '';
      return;
    }
    this.updateConfig('accessControl', {
      ...this.getAccessControl(),
      allowedEmails: [...existing, email],
    });
    this.newAllowedEmail = '';
  }

  removeAllowedEmail(email: string): void {
    this.updateConfig('accessControl', {
      ...this.getAccessControl(),
      allowedEmails: this.getAllowedEmails().filter((e) => e !== email),
    });
  }

  getClaimPath(): string {
    return this.getAccessControl().claimGate?.claimPath ?? '';
  }

  /** Returns true when the current claim path isn't one of the common preset values. */
  isCustomClaimPath(): boolean {
    const path = this.getClaimPath();
    if (!path) return false;
    return !this.commonClaimPaths.some((p) => p.value === path);
  }

  setClaimPath(path: string): void {
    const ac = this.getAccessControl();
    this.updateConfig('accessControl', {
      ...ac,
      claimGate: {
        claimPath: path,
        anyOf: ac.claimGate?.anyOf ?? [],
      },
    });
  }

  getClaimAnyOf(): string[] {
    return this.getAccessControl().claimGate?.anyOf ?? [];
  }

  addClaimAnyOf(): void {
    const v = this.newClaimAnyOfValue.trim();
    if (!v) return;
    const existing = this.getClaimAnyOf();
    if (existing.includes(v)) {
      this.newClaimAnyOfValue = '';
      return;
    }
    const ac = this.getAccessControl();
    this.updateConfig('accessControl', {
      ...ac,
      claimGate: {
        claimPath: ac.claimGate?.claimPath ?? '',
        anyOf: [...existing, v],
      },
    });
    this.newClaimAnyOfValue = '';
  }

  removeClaimAnyOf(v: string): void {
    const ac = this.getAccessControl();
    this.updateConfig('accessControl', {
      ...ac,
      claimGate: {
        claimPath: ac.claimGate?.claimPath ?? '',
        anyOf: this.getClaimAnyOf().filter((x) => x !== v),
      },
    });
  }

  /** Live-rendered "what would happen" sentence next to the claim-gate editor. */
  getClaimGatePreview(): string {
    const path = this.getClaimPath();
    const anyOf = this.getClaimAnyOf();
    if (!path) return 'Pick a claim path to see how the gate behaves.';
    if (anyOf.length === 0) return `Add at least one allowed value for "${path}".`;
    return `User is allowed if their "${path}" claim contains any of: ${anyOf.map((v) => `"${v}"`).join(', ')}. All other users are denied.`;
  }

  private looksLikeEmail(value: string): boolean {
    // Loose RFC-ish check; server enforces strict validation on save.
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
  }

  getCallbackUrl(): string {
    const info = this.serverInfo();
    const baseUrl = info?.issuerUri || window.location.origin;
    const scheme = this.formData.scheme || '{scheme}';
    const callbackPath = info?.callbackPathTemplate?.replace('{scheme}', scheme) || `/signin-${scheme}`;
    return `${baseUrl}${callbackPath}`;
  }

  getDiscoveryUrl(): string | null {
    const authority = this.formData.configuration?.['authority'] as string;
    if (!authority) return null;
    const baseUrl = authority.replace(/\/$/, '');
    return `${baseUrl}/.well-known/openid-configuration`;
  }

  openDiscoveryUrl(): void {
    const url = this.getDiscoveryUrl();
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }

  getIssuerUri(): string {
    return this.serverInfo()?.issuerUri || window.location.origin;
  }

  getProviderTypeName(type: ExternalProviderType): string {
    const typeInfo = this.providerTypes().find((t) => t.type === type);
    return typeInfo?.name ?? `Type ${type}`;
  }

  getSelectedTypeInfo(): ProviderTypeInfo | undefined {
    if (this.formData.providerType === undefined) return undefined;
    return this.providerTypes().find((t) => t.type === this.formData.providerType);
  }

  onProviderTypeSelected(value: ExternalProviderType | undefined): void {
    // Bound via [(ngModel)] / (ngModelChange) on the select so the DOM
    // selection actually reflects formData.providerType — the previous
    // [value] binding on a native <select> didn't reliably mark the
    // matching option as selected (Angular sets the DOM property, but
    // the browser only respects 'selected' on <option>). ngModel + ngValue
    // delegates to DefaultValueAccessor which handles option matching
    // by reference equality, including for primitive-typed values.
    if (value === undefined || value === null) {
      this.formData.providerType = undefined;
    } else {
      this.formData.providerType = value;
    }
    // Provider type switch — drop the old type's config bag so stale
    // fields (e.g. Microsoft tenantId left over after switching to Google)
    // don't bleed into the new type's form.
    this.formData.configuration = {};

    // Seed sensible default scopes per provider type so a freshly created
    // IdP actually includes the OIDC `email` scope. Without this, the form
    // would save with empty scopes and the OIDC handler's built-in default
    // (`openid profile`) would drop `email` — meaning IdPs like Auth0 never
    // return the email claim and users hit the Complete Registration page.
    const defaults = this.getDefaultScopesForType(this.formData.providerType);
    if (defaults.length > 0) {
      this.formData.configuration['scopes'] = defaults;
    }
  }

  private getDefaultScopesForType(type: ExternalProviderType | undefined): string[] {
    switch (type) {
      case ProviderTypes.Oidc:
      case ProviderTypes.LinkedIn:
        return ['openid', 'profile', 'email'];
      case ProviderTypes.Twitter:
        return ['tweet.read', 'users.read'];
      default:
        return [];
    }
  }

  /** @deprecated kept for binary compatibility while the template migrates. */
  onProviderTypeChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.onProviderTypeSelected(
      value === '' ? undefined : (value as ExternalProviderType),
    );
  }

  normalizeScheme(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9-]/g, '-');
  }

  revealSecret(secretKey: string): void {
    const provider = this.provider();
    if (!provider || this.isNew()) return;

    this.revealingSecret.set(secretKey);
    this.idpService.getSecret(provider.id, secretKey).subscribe({
      next: (secret) => {
        if (secret) {
          this.updateConfig(secretKey, secret);
        }
        this.revealingSecret.set(null);
      },
      error: () => {
        this.revealingSecret.set(null);
      },
    });
  }

  isRevealingSecret(secretKey: string): boolean {
    return this.revealingSecret() === secretKey;
  }

  // Check if proxy mode settings should be shown (all except LDAP)
  showProxyModeSettings(): boolean {
    return this.formData.providerType !== undefined && this.formData.providerType !== ProviderTypes.Ldap;
  }
}

import { Component, inject, signal, computed, OnInit, Type, Injector } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  ModalComponent,
  IconComponent,
  PluginService,
  TabDefinition,
  TabContext,
  TAB_CONTEXT,
} from '@oluso/ui-core';
import { AuthStore } from '@oluso/auth';
import { TenantService } from '../../services/tenant.service';
import {
  Tenant,
  PasswordPolicy,
  ProtocolConfiguration,
  UpdateTenantRequest,
  UpdateProtocolConfigurationRequest,
  DEFAULT_PASSWORD_POLICY,
  DEFAULT_PROTOCOL_CONFIGURATION,
} from '../../models/tenant.model';
import { LocaleSettingsTabComponent } from './locale-settings-tab.component';
import { UiStringsSettingsTabComponent } from './ui-strings-settings-tab.component';
import { WorkspaceAppsTabComponent } from './workspace-apps-tab.component';

/** Core tab configuration */
interface CoreTab {
  id: string;
  label: string;
  icon: string;
  order: number;
  isCore: true;
}

/** Plugin tab configuration */
interface PluginTab extends TabDefinition {
  isCore: false;
}

type Tab = CoreTab | PluginTab;

/**
 * Organization Settings Page
 *
 * Allows org admins to manage their own tenant/organization settings.
 * This is separate from the super-admin Tenant Settings page.
 * It uses the currentTenantId from AuthStore instead of a route param.
 */
/** Core tabs that are always available */
const CORE_TABS: Tab[] = [
  { id: 'general', label: 'General', icon: 'cog', order: 0, isCore: true },
  { id: 'locale', label: 'Locale & Currency', icon: 'globe-alt', order: 5,
    isCore: false, component: LocaleSettingsTabComponent } as PluginTab,
  { id: 'ui-strings', label: 'UI Strings', icon: 'language', order: 6,
    isCore: false, component: UiStringsSettingsTabComponent } as PluginTab,
  { id: 'workspace-apps', label: 'Workspace Apps', icon: 'squares', order: 8,
    isCore: false, component: WorkspaceAppsTabComponent } as PluginTab,
  { id: 'protocol', label: 'Protocol', icon: 'server', order: 10, isCore: true },
];

@Component({
  selector: 'admin-organization-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, NgComponentOutlet, CardComponent, ButtonComponent, ModalComponent, IconComponent],
  templateUrl: './organization-settings-page.component.html',
})
export class OrganizationSettingsPageComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);
  private readonly tenantService = inject(TenantService);
  private readonly pluginService = inject(PluginService);
  readonly injector = inject(Injector);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly hasChanges = signal(false);
  readonly error = signal<string | null>(null);

  readonly tenant = signal<Tenant | null>(null);
  readonly passwordPolicy = signal<PasswordPolicy | null>(null);
  readonly protocolConfig = signal<ProtocolConfiguration | null>(null);

  readonly activeTab = signal<string>('general');
  readonly showResetModal = signal(false);

  /** Combined tabs: core + plugin tabs, sorted by order */
  readonly tabs = computed<Tab[]>(() => {
    const pluginTabs = this.pluginService.tenantSettingsTabs().map(
      (tab): PluginTab => ({ ...tab, isCore: false })
    );
    return [...CORE_TABS, ...pluginTabs].sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  });

  /** Get the currently active tab */
  readonly activeTabConfig = computed(() => this.tabs().find((t) => t.id === this.activeTab()));

  /** Check if active tab is a core tab */
  readonly isCoreTa = computed(() => {
    const tab = this.activeTabConfig();
    return tab?.isCore === true;
  });

  /** Get the component for the active plugin tab */
  readonly activePluginComponent = computed<Type<unknown> | null>(() => {
    const tab = this.activeTabConfig();
    if (tab && !tab.isCore) {
      return (tab as PluginTab).component;
    }
    return null;
  });

  /** Context to pass to plugin tab components */
  readonly tabContext = computed<TabContext>(() => ({
    entityId: this.authStore.currentTenantId() || '',
    entityType: 'tenant',
    data: {
      tenant: this.tenant(),
      passwordPolicy: this.passwordPolicy(),
      protocolConfig: this.protocolConfig(),
    },
  }));

  /** Injector with TAB_CONTEXT for plugin components */
  readonly pluginInjector = computed(() =>
    Injector.create({
      providers: [{ provide: TAB_CONTEXT, useValue: this.tabContext() }],
      parent: this.injector,
    })
  );

  // Form data
  tenantData: UpdateTenantRequest = {};
  policyData: PasswordPolicy = { ...DEFAULT_PASSWORD_POLICY };
  protocolData: UpdateProtocolConfigurationRequest = {
    ...DEFAULT_PROTOCOL_CONFIGURATION,
  };

  ngOnInit(): void {
    const tenantId = this.authStore.currentTenantId();
    if (tenantId) {
      this.loadData(tenantId);
    } else {
      this.error.set('No organization context found. Please contact your administrator.');
      this.loading.set(false);
    }
  }

  loadData(tenantId: string): void {
    this.loading.set(true);
    this.error.set(null);

    // Load tenant
    this.tenantService.getById(tenantId).subscribe({
      next: (tenant) => {
        this.tenant.set(tenant);
        this.tenantData = {
          name: tenant.name,
          displayName: tenant.displayName,
          description: tenant.description,
          customDomain: tenant.customDomain,
          enabled: tenant.enabled,
        };
      },
      error: (err) => {
        console.error('Failed to load organization:', err);
        this.error.set('Failed to load organization settings');
        this.loading.set(false);
      },
    });

    // Load password policy
    this.tenantService.getPasswordPolicy(tenantId).subscribe({
      next: (policy) => {
        this.passwordPolicy.set(policy);
        this.policyData = { ...policy };
      },
      error: (err) => {
        console.error('Failed to load password policy:', err);
      },
    });

    // Load protocol configuration
    this.tenantService.getProtocolConfiguration(tenantId).subscribe({
      next: (config) => {
        this.protocolConfig.set(config);
        this.protocolData = {
          enableDynamicClientRegistration: config.enableDynamicClientRegistration,
          allowOpenDynamicRegistration: config.allowOpenDynamicRegistration,
          dynamicRegistrationAllowedScopes: config.dynamicRegistrationAllowedScopes,
          dynamicRegistrationAllowedGrantTypes: config.dynamicRegistrationAllowedGrantTypes,
          dynamicRegistrationRequirePkce: config.dynamicRegistrationRequirePkce,
          dynamicRegistrationMaxRedirectUris: config.dynamicRegistrationMaxRedirectUris,
          dynamicRegistrationRequiredScope: config.dynamicRegistrationRequiredScope,
          dynamicRegistrationRequiredClaim: config.dynamicRegistrationRequiredClaim,
          dynamicRegistrationRequiredClaimValue: config.dynamicRegistrationRequiredClaimValue,
        };
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load protocol configuration:', err);
        this.loading.set(false);
      },
    });
  }

  setActiveTab(tabId: string): void {
    this.activeTab.set(tabId);
  }

  onTenantChange(): void {
    this.hasChanges.set(true);
  }

  onPolicyChange(): void {
    this.hasChanges.set(true);
  }

  onProtocolChange(): void {
    this.hasChanges.set(true);
  }

  onScopesInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.protocolData.dynamicRegistrationAllowedScopes = input.value
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    this.hasChanges.set(true);
  }

  onGrantTypesInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.protocolData.dynamicRegistrationAllowedGrantTypes = input.value
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    this.hasChanges.set(true);
  }

  getScopesString(): string {
    return (this.protocolData.dynamicRegistrationAllowedScopes ?? []).join(', ');
  }

  getGrantTypesString(): string {
    return (this.protocolData.dynamicRegistrationAllowedGrantTypes ?? []).join(', ');
  }

  onSave(): void {
    const tenantId = this.authStore.currentTenantId();
    if (!tenantId) return;

    this.saving.set(true);

    if (this.activeTab() === 'general') {
      // Save tenant and password policy
      Promise.all([
        this.tenantService.update(tenantId, this.tenantData).toPromise(),
        this.tenantService.updatePasswordPolicy(tenantId, this.policyData).toPromise(),
      ])
        .then(() => {
          this.saving.set(false);
          this.hasChanges.set(false);
          this.loadData(tenantId);
        })
        .catch((err) => {
          console.error('Failed to save:', err);
          this.saving.set(false);
        });
    } else if (this.activeTab() === 'protocol') {
      // Save protocol configuration
      this.tenantService.updateProtocolConfiguration(tenantId, this.protocolData).subscribe({
        next: () => {
          this.saving.set(false);
          this.hasChanges.set(false);
          this.loadData(tenantId);
        },
        error: (err) => {
          console.error('Failed to save protocol configuration:', err);
          this.saving.set(false);
        },
      });
    }
  }

  openResetModal(): void {
    this.showResetModal.set(true);
  }

  closeResetModal(): void {
    this.showResetModal.set(false);
  }

  confirmReset(): void {
    const tenantId = this.authStore.currentTenantId();
    if (!tenantId) return;

    this.tenantService.resetPasswordPolicy(tenantId).subscribe({
      next: () => {
        this.policyData = { ...DEFAULT_PASSWORD_POLICY };
        this.closeResetModal();
        this.hasChanges.set(false);
        this.loadData(tenantId);
      },
      error: (err) => {
        console.error('Failed to reset password policy:', err);
      },
    });
  }
}

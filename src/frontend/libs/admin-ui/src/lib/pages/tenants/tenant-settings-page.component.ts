import { Component, inject, signal, computed, OnInit, Type, Injector } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
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
} from '@syndew/oluso-ui-core';
import { TenantService } from '../../services/tenant.service';
import {
  Tenant,
  PasswordPolicy,
  UpdateTenantRequest,
  DEFAULT_PASSWORD_POLICY,
  TenantFeature,
  TenantFeaturesResponse,
} from '../../models/tenant.model';
import { LocaleSettingsTabComponent } from '../organization/locale-settings-tab.component';
import { WorkspaceAppsTabComponent } from '../organization/workspace-apps-tab.component';
import { SenderProfilesTabComponent } from '../settings/sender-profiles-tab.component';
import { DomainsTabComponent } from './domains-tab.component';
import { SsoTabComponent } from './sso-tab.component';

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

/** Core tabs that are always available */
const CORE_TABS: Tab[] = [
  { id: 'general', label: 'General', icon: 'cog', order: 0, isCore: true },
  { id: 'features', label: 'Features', icon: 'puzzle-piece', order: 2, isCore: true },
  { id: 'domains', label: 'Domains', icon: 'globe-alt', order: 3,
    isCore: false, component: DomainsTabComponent } as PluginTab,
  { id: 'sso', label: 'SSO', icon: 'lock', order: 4,
    isCore: false, component: SsoTabComponent } as PluginTab,
  { id: 'locale', label: 'Locale & Currency', icon: 'language', order: 5,
    isCore: false, component: LocaleSettingsTabComponent } as PluginTab,
  { id: 'email', label: 'Email', icon: 'envelope', order: 6,
    isCore: false, component: SenderProfilesTabComponent } as PluginTab,
  { id: 'workspace-apps', label: 'Workspace Apps', icon: 'squares', order: 8,
    isCore: false, component: WorkspaceAppsTabComponent } as PluginTab,
];

@Component({
  selector: 'admin-tenant-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, NgComponentOutlet, CardComponent, ButtonComponent, ModalComponent, IconComponent],
  templateUrl: './tenant-settings-page.component.html',
})
export class TenantSettingsPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tenantService = inject(TenantService);
  private readonly pluginService = inject(PluginService);
  readonly injector = inject(Injector);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly hasChanges = signal(false);

  readonly tenant = signal<Tenant | null>(null);
  readonly passwordPolicy = signal<PasswordPolicy | null>(null);
  readonly featuresResponse = signal<TenantFeaturesResponse | null>(null);
  readonly featuresSaving = signal(false);
  readonly featuresHasChanges = signal(false);

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
    entityId: this.tenantId(),
    entityType: 'tenant',
    data: {
      tenant: this.tenant(),
      passwordPolicy: this.passwordPolicy(),
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
  featuresData: TenantFeature[] = [];

  readonly tenantId = computed(() => this.route.snapshot.paramMap.get('id') || '');

  ngOnInit(): void {
    const id = this.tenantId();
    if (id) {
      this.loadData(id);
    }
  }

  loadData(tenantId: string): void {
    this.loading.set(true);

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
          hideEmbedBranding: tenant.hideEmbedBranding,
        };
      },
      error: (error) => {
        console.error('Failed to load tenant:', error);
        this.loading.set(false);
      },
    });

    // Load password policy
    this.tenantService.getPasswordPolicy(tenantId).subscribe({
      next: (policy) => {
        this.passwordPolicy.set(policy);
        this.policyData = { ...policy };
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load password policy:', error);
        this.loading.set(false);
      },
    });

    // Load features
    this.loadFeatures(tenantId);
  }

  loadFeatures(tenantId: string): void {
    this.tenantService.getFeatures(tenantId).subscribe({
      next: (response) => {
        this.featuresResponse.set(response);
        this.featuresData = response.features.map(f => ({ ...f }));
        this.featuresHasChanges.set(false);
      },
      error: (error) => {
        console.error('Failed to load features:', error);
      },
    });
  }

  setActiveTab(tabId: string): void {
    this.activeTab.set(tabId);
  }

  goBack(): void {
    this.router.navigate(['/tenants']);
  }

  onTenantChange(): void {
    this.hasChanges.set(true);
  }

  onPolicyChange(): void {
    this.hasChanges.set(true);
  }

  onSave(): void {
    const tenantId = this.tenantId();
    if (!tenantId) return;

    this.saving.set(true);

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
      .catch((error) => {
        console.error('Failed to save:', error);
        this.saving.set(false);
      });
  }

  onFeatureToggle(feature: TenantFeature): void {
    feature.enabled = !feature.enabled;
    this.featuresHasChanges.set(true);
  }

  onFeatureMaxSeatsChange(feature: TenantFeature, value: string): void {
    const num = value ? parseInt(value, 10) : null;
    feature.maxSeats = num && num > 0 ? num : null;
    this.featuresHasChanges.set(true);
  }

  saveFeatures(): void {
    const tenantId = this.tenantId();
    if (!tenantId) return;

    this.featuresSaving.set(true);

    const request = {
      features: this.featuresData.map(f => ({
        featureKey: f.key,
        enabled: f.enabled,
        maxSeats: f.maxSeats ?? null,
      })),
    };

    this.tenantService.setFeatures(tenantId, request).subscribe({
      next: () => {
        this.featuresSaving.set(false);
        this.featuresHasChanges.set(false);
        this.loadFeatures(tenantId);
      },
      error: (error) => {
        console.error('Failed to save features:', error);
        this.featuresSaving.set(false);
      },
    });
  }

  openResetModal(): void {
    this.showResetModal.set(true);
  }

  closeResetModal(): void {
    this.showResetModal.set(false);
  }

  confirmReset(): void {
    const tenantId = this.tenantId();
    if (!tenantId) return;

    this.tenantService.resetPasswordPolicy(tenantId).subscribe({
      next: () => {
        this.policyData = { ...DEFAULT_PASSWORD_POLICY };
        this.closeResetModal();
        this.hasChanges.set(false);
        this.loadData(tenantId);
      },
      error: (error) => {
        console.error('Failed to reset password policy:', error);
      },
    });
  }
}

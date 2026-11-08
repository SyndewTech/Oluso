import { Component, inject, signal, computed, OnInit, Type, Injector } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  IconComponent,
  PluginService,
  TabDefinition as PluginTabDefinition,
  TabContext,
  TAB_CONTEXT,
  ToastService,
  extractErrorMessage,
} from '@oluso/ui-core';
import { SettingsService } from '../../services/settings.service';
import {
  ServerSettings,
  TokenSettings,
  SecuritySettings,
  CorsSettings,
  TenantSettings,
  ProtocolSettings,
  DEFAULT_PROTOCOL_SETTINGS,
  MIN_PREFIX_SCOPE_SUFFIX_MAX_LENGTH,
  MAX_PREFIX_SCOPE_SUFFIX_MAX_LENGTH,
  prefixScopeSuffixMaxLengthError,
  AccountEmailSettings,
  DEFAULT_ACCOUNT_EMAIL_SETTINGS,
} from '../../models/server-settings.model';
import { LocaleSettingsTabComponent } from '../organization/locale-settings-tab.component';
import { SenderProfilesTabComponent } from './sender-profiles-tab.component';
import { WhatsAppSettingsTabComponent } from './whatsapp-settings-tab.component';
import { BrandingSettingsTabComponent } from './branding-settings-tab.component';
import { IndustrySettingsTabComponent } from './industry-settings-tab.component';
import { DomainsTabComponent } from '../tenants/domains-tab.component';
import { SsoTabComponent } from '../tenants/sso-tab.component';

/** Core tab configuration */
interface CoreTab {
  id: string;
  label: string;
  icon: string;
  order: number;
  isCore: true;
}

/** Plugin tab configuration */
interface PluginTab extends PluginTabDefinition {
  isCore: false;
}

type Tab = CoreTab | PluginTab;

/** Core tabs that are always available */
const CORE_TABS: Tab[] = [
  { id: 'general', label: 'General', icon: 'cog', order: 0, isCore: true },
  { id: 'domains', label: 'Domains', icon: 'globe-alt', order: 4,
    isCore: false, component: DomainsTabComponent } as PluginTab,
  { id: 'locale', label: 'Locale & Currency', icon: 'globe-alt', order: 5,
    isCore: false, component: LocaleSettingsTabComponent } as PluginTab,
  { id: 'industry', label: 'Industry', icon: 'building-office', order: 6,
    isCore: false, component: IndustrySettingsTabComponent } as PluginTab,
  { id: 'tokens', label: 'Tokens', icon: 'key', order: 10, isCore: true },
  { id: 'security', label: 'Security', icon: 'shield', order: 20, isCore: true },
  { id: 'cors', label: 'CORS', icon: 'globe', order: 30, isCore: true },
  { id: 'email', label: 'Email', icon: 'envelope', order: 35,
    isCore: false, component: SenderProfilesTabComponent } as PluginTab,
  { id: 'whatsapp', label: 'WhatsApp', icon: 'chat-bubble-left', order: 36,
    isCore: false, component: WhatsAppSettingsTabComponent } as PluginTab,
  { id: 'branding', label: 'Branding', icon: 'photo', order: 37,
    isCore: false, component: BrandingSettingsTabComponent } as PluginTab,
  // Same component instance lives in the tenant-settings page too. It reads
  // tabContext.entityType to decide whether to use the route's tenant id or
  // the authenticated tenant.
  { id: 'sso', label: 'SSO', icon: 'lock', order: 38,
    isCore: false, component: SsoTabComponent } as PluginTab,
  { id: 'protocol', label: 'Protocol', icon: 'server', order: 40, isCore: true },
];

@Component({
  selector: 'admin-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, NgComponentOutlet, CardComponent, ButtonComponent, IconComponent],
  templateUrl: './settings-page.component.html',
})
export class SettingsPageComponent implements OnInit {
  private readonly settingsService = inject(SettingsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly pluginService = inject(PluginService);
  private readonly toast = inject(ToastService);
  readonly injector = inject(Injector);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly hasChanges = signal(false);
  readonly activeTab = signal<string>('general');

  /** Combined tabs: core + plugin tabs, sorted by order */
  readonly tabs = computed<Tab[]>(() => {
    const pluginTabs = this.pluginService.settingsTabs().map(
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
    entityId: 'server',
    entityType: 'settings',
    data: {
      settings: this.formData,
    },
  }));

  /** Injector with TAB_CONTEXT for plugin components */
  readonly pluginInjector = computed(() =>
    Injector.create({
      providers: [{ provide: TAB_CONTEXT, useValue: this.tabContext() }],
      parent: this.injector,
    })
  );

  formData: ServerSettings = {
    tokenSettings: {
      defaultAccessTokenLifetime: 3600,
      defaultIdentityTokenLifetime: 300,
      defaultRefreshTokenLifetime: 2592000,
    },
    securitySettings: {
      requireHttps: true,
      emitStaticClaims: false,
      enableBackchannelLogout: true,
    },
    corsSettings: {
      allowAllOrigins: false,
      allowedOrigins: [],
    },
    tenantSettings: {
      useJourneyFlow: true,
      journeyFallbackToStandard: false,
      allowSelfRegistration: true,
      requireTermsAcceptance: false,
      requireEmailVerification: true,
      enableLocalLogin: true,
      enablePasskey: true,
    },
    protocolSettings: { ...DEFAULT_PROTOCOL_SETTINGS },
    accountEmailSettings: { ...DEFAULT_ACCOUNT_EMAIL_SETTINGS },
  };

  // For the CORS origins input
  allowedOriginsInput = '';

  readonly minPrefixScopeSuffixMaxLength = MIN_PREFIX_SCOPE_SUFFIX_MAX_LENGTH;
  readonly maxPrefixScopeSuffixMaxLength = MAX_PREFIX_SCOPE_SUFFIX_MAX_LENGTH;

  /** Why the Protocol tab's default prefix-scope suffix length can't be saved, or null. */
  prefixScopeSuffixMaxLengthError(): string | null {
    return prefixScopeSuffixMaxLengthError(this.formData.protocolSettings?.prefixScopeSuffixMaxLength);
  }

  ngOnInit(): void {
    this.loadSettings();
    // Allow deep-linking to a tab, e.g. /settings?tab=industry — the
    // Terminology Center links here to edit the tenant type.
    const requestedTab = this.route.snapshot.queryParamMap.get('tab');
    if (requestedTab) {
      this.setActiveTab(requestedTab);
    }
  }

  loadSettings(): void {
    this.loading.set(true);
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        this.formData = {
          ...settings,
          accountEmailSettings: settings.accountEmailSettings ?? { ...DEFAULT_ACCOUNT_EMAIL_SETTINGS },
          protocolSettings: { ...DEFAULT_PROTOCOL_SETTINGS, ...settings.protocolSettings },
        };
        this.allowedOriginsInput = settings.corsSettings.allowedOrigins.join(', ');
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load settings:', error);
        this.loading.set(false);
      },
    });
  }

  updateTokenSettings(key: keyof TokenSettings, value: number): void {
    this.formData.tokenSettings[key] = value;
    this.hasChanges.set(true);
  }

  updateAccountEmailSettings(key: keyof AccountEmailSettings, value: number): void {
    this.formData.accountEmailSettings = {
      ...(this.formData.accountEmailSettings ?? DEFAULT_ACCOUNT_EMAIL_SETTINGS),
      [key]: value,
    };
    this.hasChanges.set(true);
  }

  updateSecuritySettings(key: keyof SecuritySettings, value: boolean): void {
    this.formData.securitySettings[key] = value;
    this.hasChanges.set(true);
  }

  updateCorsSettings(key: keyof CorsSettings, value: boolean | string[]): void {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (this.formData.corsSettings as any)[key] = value;
    this.hasChanges.set(true);
  }

  updateTenantSettings(key: keyof TenantSettings, value: boolean | string | undefined): void {
    if (!this.formData.tenantSettings) {
      this.formData.tenantSettings = {
        useJourneyFlow: true,
        journeyFallbackToStandard: false,
        allowSelfRegistration: true,
        requireTermsAcceptance: false,
        requireEmailVerification: true,
        enableLocalLogin: true,
        enablePasskey: true,
      };
    }
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (this.formData.tenantSettings as any)[key] = value;
    this.hasChanges.set(true);
  }

  onAllowedOriginsChange(): void {
    const origins = this.allowedOriginsInput
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    this.updateCorsSettings('allowedOrigins', origins);
  }

  updateProtocolSettings(key: keyof ProtocolSettings, value: boolean | number | string | string[] | undefined): void {
    if (!this.formData.protocolSettings) {
      this.formData.protocolSettings = { ...DEFAULT_PROTOCOL_SETTINGS };
    }
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (this.formData.protocolSettings as any)[key] = value;
    this.hasChanges.set(true);
  }

  getProtocolScopesString(): string {
    return (this.formData.protocolSettings?.dynamicRegistrationAllowedScopes ?? []).join(', ');
  }

  getProtocolGrantTypesString(): string {
    return (this.formData.protocolSettings?.dynamicRegistrationAllowedGrantTypes ?? []).join(', ');
  }

  onProtocolScopesInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const scopes = input.value.split(',').map((s) => s.trim()).filter(Boolean);
    this.updateProtocolSettings('dynamicRegistrationAllowedScopes', scopes.length > 0 ? scopes : undefined);
  }

  onProtocolGrantTypesInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const grantTypes = input.value.split(',').map((s) => s.trim()).filter(Boolean);
    this.updateProtocolSettings('dynamicRegistrationAllowedGrantTypes', grantTypes.length > 0 ? grantTypes : undefined);
  }

  onSave(): void {
    const invalid = this.prefixScopeSuffixMaxLengthError();
    if (invalid) {
      this.setActiveTab('protocol');
      this.toast.error(`Default maximum suffix length for prefix scopes: ${invalid}`);
      return;
    }
    this.saving.set(true);
    this.settingsService.updateSettings(this.formData).subscribe({
      next: (updated) => {
        this.formData = updated;
        this.saving.set(false);
        this.hasChanges.set(false);
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to save settings'));
        this.saving.set(false);
      },
    });
  }

  navigateToSigningKeys(): void {
    this.router.navigate(['/signing-keys']);
  }

  setActiveTab(tabId: string): void {
    this.activeTab.set(tabId);
  }
}

import { useState, useEffect, useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Cog6ToothIcon } from '@heroicons/react/24/outline';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import Button from '../components/common/Button';
import Input from '../components/common/Input';
import { useSettingsTabs, type SettingsTab } from '@oluso/ui-core';
import api from '../services/api';

interface TokenSettings {
  defaultAccessTokenLifetime: number;
  defaultIdentityTokenLifetime: number;
  defaultRefreshTokenLifetime: number;
}

interface SecuritySettings {
  requireHttps: boolean;
  emitStaticClaims: boolean;
  enableBackchannelLogout: boolean;
}

interface CorsSettings {
  allowAllOrigins: boolean;
  allowedOrigins: string[];
}

interface TenantSettings {
  useJourneyFlow: boolean;
  allowSelfRegistration: boolean;
  requireTermsAcceptance: boolean;
  termsOfServiceUrl?: string;
  privacyPolicyUrl?: string;
  requireEmailVerification: boolean;
  allowedEmailDomains?: string;
}

interface ServerSettings {
  tokenSettings: TokenSettings;
  securitySettings: SecuritySettings;
  corsSettings: CorsSettings;
  tenantSettings?: TenantSettings;
}

// Built-in "General" tab definition
const GENERAL_TAB_ID = 'general';

export default function SettingsPage() {
  const queryClient = useQueryClient();
  const [hasChanges, setHasChanges] = useState(false);
  const [activeTab, setActiveTab] = useState(GENERAL_TAB_ID);

  // Get plugin settings tabs
  const pluginTabs = useSettingsTabs();

  // Combine built-in tab with plugin tabs
  const allTabs = useMemo(() => {
    const builtInTab: SettingsTab = {
      id: GENERAL_TAB_ID,
      label: 'General',
      icon: Cog6ToothIcon,
      component: () => null, // We render the built-in content directly
      order: 0,
    };
    return [builtInTab, ...pluginTabs].sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }, [pluginTabs]);

  const { data: settings, isLoading } = useQuery<ServerSettings>({
    queryKey: ['settings'],
    queryFn: async () => {
      const response = await api.get('/settings');
      return response.data;
    },
  });

  const [formData, setFormData] = useState<ServerSettings>({
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
      allowSelfRegistration: true,
      requireTermsAcceptance: false,
      requireEmailVerification: true,
    },
  });

  useEffect(() => {
    if (settings) {
      setFormData(settings);
    }
  }, [settings]);

  const saveMutation = useMutation({
    mutationFn: async (data: ServerSettings) => {
      const response = await api.put('/settings', data);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['settings'] });
      setHasChanges(false);
    },
  });

  const updateTokenSettings = (key: keyof TokenSettings, value: number) => {
    setFormData({
      ...formData,
      tokenSettings: { ...formData.tokenSettings, [key]: value },
    });
    setHasChanges(true);
  };

  const updateSecuritySettings = (key: keyof SecuritySettings, value: boolean) => {
    setFormData({
      ...formData,
      securitySettings: { ...formData.securitySettings, [key]: value },
    });
    setHasChanges(true);
  };

  const updateCorsSettings = (key: keyof CorsSettings, value: boolean | string[]) => {
    setFormData({
      ...formData,
      corsSettings: { ...formData.corsSettings, [key]: value },
    });
    setHasChanges(true);
  };

  const updateTenantSettings = (key: keyof TenantSettings, value: boolean | string | undefined) => {
    setFormData({
      ...formData,
      tenantSettings: { ...formData.tenantSettings!, [key]: value },
    });
    setHasChanges(true);
  };

  const handleSave = () => {
    saveMutation.mutate(formData);
  };

  if (isLoading) {
    return <div className="flex items-center justify-center h-64">Loading...</div>;
  }

  const renderTabContent = () => {
    if (activeTab === GENERAL_TAB_ID) {
      return (
        <>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader title="Token Settings" description="Configure token lifetimes and behavior" />
              <CardContent className="space-y-4">
                <Input
                  label="Default Access Token Lifetime (seconds)"
                  type="number"
                  value={formData.tokenSettings.defaultAccessTokenLifetime}
                  onChange={(e) =>
                    updateTokenSettings('defaultAccessTokenLifetime', parseInt(e.target.value) || 0)
                  }
                />
                <Input
                  label="Default Identity Token Lifetime (seconds)"
                  type="number"
                  value={formData.tokenSettings.defaultIdentityTokenLifetime}
                  onChange={(e) =>
                    updateTokenSettings('defaultIdentityTokenLifetime', parseInt(e.target.value) || 0)
                  }
                />
                <Input
                  label="Default Refresh Token Lifetime (seconds)"
                  type="number"
                  value={formData.tokenSettings.defaultRefreshTokenLifetime}
                  onChange={(e) =>
                    updateTokenSettings('defaultRefreshTokenLifetime', parseInt(e.target.value) || 0)
                  }
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader title="Security Settings" description="Configure security policies" />
              <CardContent className="space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Require HTTPS</p>
                    <p className="text-sm text-gray-500">Enforce HTTPS for all endpoints</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.securitySettings.requireHttps}
                    onChange={(e) => updateSecuritySettings('requireHttps', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Emit Static Claims</p>
                    <p className="text-sm text-gray-500">Include static claims in tokens</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.securitySettings.emitStaticClaims}
                    onChange={(e) => updateSecuritySettings('emitStaticClaims', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Enable Backchannel Logout</p>
                    <p className="text-sm text-gray-500">Allow backchannel logout requests</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.securitySettings.enableBackchannelLogout}
                    onChange={(e) => updateSecuritySettings('enableBackchannelLogout', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader title="Signing Keys" description="Manage token signing credentials" />
              <CardContent>
                <p className="text-sm text-gray-500 mb-4">
                  Current signing key: RS256 (auto-generated)
                </p>
                <div className="space-x-2">
                  <Button variant="secondary" size="sm" onClick={() => window.location.href = '/signing-keys'}>
                    Manage Keys
                  </Button>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader title="CORS Settings" description="Configure cross-origin resource sharing" />
              <CardContent className="space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Allow All Origins</p>
                    <p className="text-sm text-gray-500">Accept requests from any origin</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.corsSettings.allowAllOrigins}
                    onChange={(e) => updateCorsSettings('allowAllOrigins', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
                <Input
                  label="Allowed Origins"
                  value={formData.corsSettings.allowedOrigins.join(', ')}
                  onChange={(e) =>
                    updateCorsSettings(
                      'allowedOrigins',
                      e.target.value.split(',').map((s) => s.trim()).filter(Boolean)
                    )
                  }
                  placeholder="https://example.com, https://app.example.com"
                  helperText="Comma-separated list of allowed origins"
                  disabled={formData.corsSettings.allowAllOrigins}
                />
              </CardContent>
            </Card>

            <Card className="lg:col-span-2">
              <CardHeader title="Authentication Flow Settings" description="Configure how users authenticate with your tenant" />
              <CardContent className="space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Use Journey-Based Flow</p>
                    <p className="text-sm text-gray-500">Use step-by-step authentication journeys instead of standalone pages</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.tenantSettings?.useJourneyFlow ?? true}
                    onChange={(e) => updateTenantSettings('useJourneyFlow', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
              </CardContent>
            </Card>

            <Card className="lg:col-span-2">
              <CardHeader title="User Registration Settings" description="Configure self-service registration options" />
              <CardContent className="space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Allow Self Registration</p>
                    <p className="text-sm text-gray-500">Allow users to create accounts without admin intervention</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.tenantSettings?.allowSelfRegistration ?? true}
                    onChange={(e) => updateTenantSettings('allowSelfRegistration', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Require Email Verification</p>
                    <p className="text-sm text-gray-500">Users must verify their email before they can sign in</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.tenantSettings?.requireEmailVerification ?? true}
                    onChange={(e) => updateTenantSettings('requireEmailVerification', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-gray-700">Require Terms Acceptance</p>
                    <p className="text-sm text-gray-500">Users must accept terms of service during registration</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={formData.tenantSettings?.requireTermsAcceptance ?? false}
                    onChange={(e) => updateTenantSettings('requireTermsAcceptance', e.target.checked)}
                    className="h-4 w-4 text-primary-600 rounded"
                  />
                </div>
                <Input
                  label="Terms of Service URL"
                  value={formData.tenantSettings?.termsOfServiceUrl || ''}
                  onChange={(e) => updateTenantSettings('termsOfServiceUrl', e.target.value || undefined)}
                  placeholder="https://example.com/terms"
                  helperText="Link to your terms of service document"
                />
                <Input
                  label="Privacy Policy URL"
                  value={formData.tenantSettings?.privacyPolicyUrl || ''}
                  onChange={(e) => updateTenantSettings('privacyPolicyUrl', e.target.value || undefined)}
                  placeholder="https://example.com/privacy"
                  helperText="Link to your privacy policy document"
                />
                <Input
                  label="Allowed Email Domains"
                  value={formData.tenantSettings?.allowedEmailDomains || ''}
                  onChange={(e) => updateTenantSettings('allowedEmailDomains', e.target.value || undefined)}
                  placeholder="company.com, partner.com"
                  helperText="Comma-separated list of allowed email domains for registration (leave empty to allow all)"
                />
              </CardContent>
            </Card>
          </div>

          <div className="flex justify-end gap-2">
            {hasChanges && (
              <span className="text-sm text-gray-500 self-center mr-2">You have unsaved changes</span>
            )}
            <Button
              onClick={handleSave}
              disabled={!hasChanges || saveMutation.isPending}
            >
              {saveMutation.isPending ? 'Saving...' : 'Save Settings'}
            </Button>
          </div>
        </>
      );
    }

    // Render plugin tab content
    const pluginTab = pluginTabs.find(tab => tab.id === activeTab);
    if (pluginTab) {
      const TabComponent = pluginTab.component;
      return (
        <TabComponent
          isActive={true}
          onSave={() => {
            // Plugin tabs manage their own save
          }}
          onHasChanges={() => {
            // Plugin tabs manage their own changes
          }}
        />
      );
    }

    return null;
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Settings</h1>
        <p className="mt-1 text-sm text-gray-500">Configure identity server settings</p>
      </div>

      {/* Tab Navigation - only show if there are plugin tabs */}
      {pluginTabs.length > 0 && (
        <div className="border-b border-gray-200">
          <nav className="-mb-px flex space-x-8" aria-label="Tabs">
            {allTabs.map((tab) => {
              const isActive = activeTab === tab.id;
              const IconComponent = tab.icon;
              return (
                <button
                  key={tab.id}
                  onClick={() => setActiveTab(tab.id)}
                  className={`
                    group inline-flex items-center py-4 px-1 border-b-2 font-medium text-sm
                    ${isActive
                      ? 'border-primary-500 text-primary-600'
                      : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }
                  `}
                >
                  {IconComponent && (
                    <IconComponent
                      className={`-ml-0.5 mr-2 h-5 w-5 ${
                        isActive ? 'text-primary-500' : 'text-gray-400 group-hover:text-gray-500'
                      }`}
                    />
                  )}
                  {tab.label}
                </button>
              );
            })}
          </nav>
        </div>
      )}

      {/* Tab Content */}
      {renderTabContent()}
    </div>
  );
}

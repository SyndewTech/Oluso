import { useState, useEffect, useMemo } from 'react';
import { useParams, useNavigate, useSearchParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import Button from '../components/common/Button';
import Input from '../components/common/Input';
import Modal from '../components/common/Modal';
import { tenantService } from '../services/tenantService';
import type { PasswordPolicy, UpdateTenantRequest, UpdatePasswordPolicyRequest } from '../types/tenant';
import { DEFAULT_PASSWORD_POLICY } from '../types/tenant';
import { useTenantSettingsTabs, type TenantData } from '@oluso/ui-core';
import {
  ArrowLeftIcon,
  ShieldCheckIcon,
  KeyIcon,
  ClockIcon,
  ExclamationTriangleIcon,
  Cog6ToothIcon,
} from '@heroicons/react/24/outline';

export default function TenantSettingsPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const queryClient = useQueryClient();
  const [hasChanges, setHasChanges] = useState(false);
  const [isResetModalOpen, setIsResetModalOpen] = useState(false);

  // Get plugin-provided tabs
  const pluginTabs = useTenantSettingsTabs();

  // All tabs: built-in general tab + plugin tabs
  const allTabs = useMemo(() => {
    const builtInTabs = [
      { id: 'general', label: 'General', icon: Cog6ToothIcon, order: 0 },
    ];
    return [...builtInTabs, ...pluginTabs].sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }, [pluginTabs]);

  // Active tab management
  const activeTab = searchParams.get('tab') || 'general';
  const setActiveTab = (tabId: string) => {
    setSearchParams({ tab: tabId });
  };

  const { data: tenant, isLoading: isLoadingTenant } = useQuery({
    queryKey: ['tenant', tenantId],
    queryFn: () => tenantService.getById(tenantId!),
    enabled: !!tenantId,
  });

  const { data: passwordPolicy, isLoading: isLoadingPolicy } = useQuery({
    queryKey: ['tenant-password-policy', tenantId],
    queryFn: () => tenantService.getPasswordPolicy(tenantId!),
    enabled: !!tenantId,
  });

  const [tenantData, setTenantData] = useState<UpdateTenantRequest>({});
  const [policyData, setPolicyData] = useState<PasswordPolicy>(DEFAULT_PASSWORD_POLICY);

  useEffect(() => {
    if (tenant) {
      setTenantData({
        name: tenant.name,
        displayName: tenant.displayName,
        description: tenant.description,
        customDomain: tenant.customDomain,
        enabled: tenant.enabled,
      });
    }
  }, [tenant]);

  useEffect(() => {
    if (passwordPolicy) {
      setPolicyData(passwordPolicy);
    }
  }, [passwordPolicy]);

  const updateTenantMutation = useMutation({
    mutationFn: (data: UpdateTenantRequest) => tenantService.update(tenantId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tenant', tenantId] });
      queryClient.invalidateQueries({ queryKey: ['tenants'] });
      setHasChanges(false);
    },
  });

  const updatePolicyMutation = useMutation({
    mutationFn: (data: UpdatePasswordPolicyRequest) => tenantService.updatePasswordPolicy(tenantId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tenant-password-policy', tenantId] });
      setHasChanges(false);
    },
  });

  const resetPolicyMutation = useMutation({
    mutationFn: () => tenantService.resetPasswordPolicy(tenantId!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tenant-password-policy', tenantId] });
      setPolicyData(DEFAULT_PASSWORD_POLICY);
      setIsResetModalOpen(false);
      setHasChanges(false);
    },
  });

  const handleTenantChange = (key: keyof UpdateTenantRequest, value: string | boolean | undefined) => {
    setTenantData((prev) => ({ ...prev, [key]: value }));
    setHasChanges(true);
  };

  const handlePolicyChange = (key: keyof PasswordPolicy, value: number | boolean | string | undefined) => {
    setPolicyData((prev) => ({ ...prev, [key]: value }));
    setHasChanges(true);
  };

  const handleSave = async () => {
    await Promise.all([
      updateTenantMutation.mutateAsync(tenantData),
      updatePolicyMutation.mutateAsync(policyData),
    ]);
  };

  if (isLoadingTenant || isLoadingPolicy) {
    return <div className="flex items-center justify-center h-64">Loading...</div>;
  }

  if (!tenant) {
    return (
      <div className="text-center py-12">
        <p className="text-gray-500">Tenant not found</p>
        <Button variant="secondary" onClick={() => navigate('/tenants')} className="mt-4">
          Back to Tenants
        </Button>
      </div>
    );
  }

  const isSaving = updateTenantMutation.isPending || updatePolicyMutation.isPending;

  // Convert tenant to TenantData format for plugin tabs
  const tenantData2: TenantData = {
    id: tenant.id,
    name: tenant.name,
    displayName: tenant.displayName,
    identifier: tenant.identifier,
    description: tenant.description,
    customDomain: tenant.customDomain,
    enabled: tenant.enabled,
    createdAt: tenant.createdAt,
    updatedAt: tenant.updatedAt,
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <button
          onClick={() => navigate('/tenants')}
          className="p-2 text-gray-400 hover:text-gray-600 rounded-md hover:bg-gray-100"
        >
          <ArrowLeftIcon className="h-5 w-5" />
        </button>
        <div>
          <h1 className="text-2xl font-bold text-gray-900">
            {tenant.displayName || tenant.name} Settings
          </h1>
          <p className="mt-1 text-sm text-gray-500">
            Configure tenant settings and policies
          </p>
        </div>
      </div>

      {/* Tabs Navigation */}
      {allTabs.length > 1 && (
        <div className="border-b border-gray-200">
          <nav className="-mb-px flex space-x-8" aria-label="Tabs">
            {allTabs.map((tab) => {
              const Icon = tab.icon;
              const isActive = activeTab === tab.id;
              return (
                <button
                  key={tab.id}
                  onClick={() => setActiveTab(tab.id)}
                  className={`
                    flex items-center gap-2 whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm
                    ${isActive
                      ? 'border-primary-500 text-primary-600'
                      : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }
                  `}
                >
                  {Icon && <Icon className="h-5 w-5" />}
                  {tab.label}
                </button>
              );
            })}
          </nav>
        </div>
      )}

      {/* Tab Content */}
      {activeTab === 'general' ? (
        <>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Tenant Details */}
        <Card>
          <CardHeader title="Tenant Details" description="Basic tenant information" />
          <CardContent className="space-y-4">
            <Input
              label="Name"
              value={tenantData.name || ''}
              onChange={(e) => handleTenantChange('name', e.target.value)}
            />
            <Input
              label="Display Name"
              value={tenantData.displayName || ''}
              onChange={(e) => handleTenantChange('displayName', e.target.value || undefined)}
            />
            <div>
              <label className="block text-sm font-medium text-gray-700">Description</label>
              <textarea
                value={tenantData.description || ''}
                onChange={(e) => handleTenantChange('description', e.target.value || undefined)}
                rows={3}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
            </div>
            <Input
              label="Custom Domain"
              value={tenantData.customDomain || ''}
              onChange={(e) => handleTenantChange('customDomain', e.target.value || undefined)}
              placeholder="auth.example.com"
            />
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-gray-700">Enabled</p>
                <p className="text-sm text-gray-500">Allow users to authenticate with this tenant</p>
              </div>
              <input
                type="checkbox"
                checked={tenantData.enabled ?? true}
                onChange={(e) => handleTenantChange('enabled', e.target.checked)}
                className="h-4 w-4 text-primary-600 rounded"
              />
            </div>
          </CardContent>
        </Card>

        {/* Password Length & Complexity */}
        <Card>
          <CardHeader
            title="Password Requirements"
            description="Configure password length and complexity"
            icon={<KeyIcon className="h-5 w-5 text-gray-400" />}
          />
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <Input
                label="Minimum Length"
                type="number"
                min={1}
                max={128}
                value={policyData.minimumLength}
                onChange={(e) => handlePolicyChange('minimumLength', parseInt(e.target.value) || 8)}
              />
              <Input
                label="Maximum Length"
                type="number"
                min={0}
                max={256}
                value={policyData.maximumLength}
                onChange={(e) => handlePolicyChange('maximumLength', parseInt(e.target.value) || 128)}
                helperText="0 = no limit"
              />
            </div>
            <Input
              label="Required Unique Characters"
              type="number"
              min={0}
              max={50}
              value={policyData.requiredUniqueChars}
              onChange={(e) => handlePolicyChange('requiredUniqueChars', parseInt(e.target.value) || 0)}
              helperText="Minimum distinct characters in password"
            />
            <div className="space-y-3 pt-2">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-gray-700">Require Digit</p>
                  <p className="text-xs text-gray-500">At least one number (0-9)</p>
                </div>
                <input
                  type="checkbox"
                  checked={policyData.requireDigit}
                  onChange={(e) => handlePolicyChange('requireDigit', e.target.checked)}
                  className="h-4 w-4 text-primary-600 rounded"
                />
              </div>
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-gray-700">Require Lowercase</p>
                  <p className="text-xs text-gray-500">At least one lowercase letter (a-z)</p>
                </div>
                <input
                  type="checkbox"
                  checked={policyData.requireLowercase}
                  onChange={(e) => handlePolicyChange('requireLowercase', e.target.checked)}
                  className="h-4 w-4 text-primary-600 rounded"
                />
              </div>
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-gray-700">Require Uppercase</p>
                  <p className="text-xs text-gray-500">At least one uppercase letter (A-Z)</p>
                </div>
                <input
                  type="checkbox"
                  checked={policyData.requireUppercase}
                  onChange={(e) => handlePolicyChange('requireUppercase', e.target.checked)}
                  className="h-4 w-4 text-primary-600 rounded"
                />
              </div>
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-gray-700">Require Special Character</p>
                  <p className="text-xs text-gray-500">At least one non-alphanumeric (!@#$%^&* etc.)</p>
                </div>
                <input
                  type="checkbox"
                  checked={policyData.requireNonAlphanumeric}
                  onChange={(e) => handlePolicyChange('requireNonAlphanumeric', e.target.checked)}
                  className="h-4 w-4 text-primary-600 rounded"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Password History & Expiration */}
        <Card>
          <CardHeader
            title="Password Lifecycle"
            description="Configure password history and expiration"
            icon={<ClockIcon className="h-5 w-5 text-gray-400" />}
          />
          <CardContent className="space-y-4">
            <Input
              label="Password History Count"
              type="number"
              min={0}
              max={24}
              value={policyData.passwordHistoryCount}
              onChange={(e) => handlePolicyChange('passwordHistoryCount', parseInt(e.target.value) || 0)}
              helperText="Number of previous passwords to prevent reuse (0 = disabled)"
            />
            <Input
              label="Password Expiration (days)"
              type="number"
              min={0}
              max={365}
              value={policyData.passwordExpirationDays}
              onChange={(e) => handlePolicyChange('passwordExpirationDays', parseInt(e.target.value) || 0)}
              helperText="Days until password expires (0 = never expires)"
            />
          </CardContent>
        </Card>

        {/* Lockout Settings */}
        <Card>
          <CardHeader
            title="Account Lockout"
            description="Configure account lockout behavior"
            icon={<ShieldCheckIcon className="h-5 w-5 text-gray-400" />}
          />
          <CardContent className="space-y-4">
            <Input
              label="Max Failed Attempts"
              type="number"
              min={1}
              max={20}
              value={policyData.maxFailedAttempts}
              onChange={(e) => handlePolicyChange('maxFailedAttempts', parseInt(e.target.value) || 5)}
              helperText="Failed login attempts before lockout"
            />
            <Input
              label="Lockout Duration (minutes)"
              type="number"
              min={1}
              max={1440}
              value={policyData.lockoutDurationMinutes}
              onChange={(e) => handlePolicyChange('lockoutDurationMinutes', parseInt(e.target.value) || 15)}
              helperText="How long the account stays locked"
            />
            <div className="flex items-center justify-between pt-2">
              <div>
                <p className="text-sm font-medium text-gray-700">Block Common Passwords</p>
                <p className="text-xs text-gray-500">Reject frequently used weak passwords</p>
              </div>
              <input
                type="checkbox"
                checked={policyData.blockCommonPasswords}
                onChange={(e) => handlePolicyChange('blockCommonPasswords', e.target.checked)}
                className="h-4 w-4 text-primary-600 rounded"
              />
            </div>
          </CardContent>
        </Card>

        {/* Custom Validation */}
        <Card className="lg:col-span-2">
          <CardHeader
            title="Custom Validation"
            description="Add custom regex pattern for password validation (advanced)"
            icon={<ExclamationTriangleIcon className="h-5 w-5 text-yellow-500" />}
          />
          <CardContent className="space-y-4">
            <Input
              label="Custom Regex Pattern"
              value={policyData.customRegexPattern || ''}
              onChange={(e) => handlePolicyChange('customRegexPattern', e.target.value || undefined)}
              placeholder="^(?=.*[!@#$%^&*])(?=.*\d{2,}).*$"
              helperText="Regular expression pattern passwords must match (leave empty to disable)"
            />
            <Input
              label="Custom Error Message"
              value={policyData.customRegexErrorMessage || ''}
              onChange={(e) => handlePolicyChange('customRegexErrorMessage', e.target.value || undefined)}
              placeholder="Password must contain at least 2 numbers and 1 special character"
              helperText="Error message shown when password doesn't match the custom pattern"
            />
          </CardContent>
        </Card>
          </div>

          <div className="flex justify-between items-center">
            <Button
              variant="secondary"
              onClick={() => setIsResetModalOpen(true)}
            >
              Reset to Defaults
            </Button>
            <div className="flex items-center gap-2">
              {hasChanges && (
                <span className="text-sm text-gray-500">You have unsaved changes</span>
              )}
              <Button
                onClick={handleSave}
                disabled={!hasChanges || isSaving}
              >
                {isSaving ? 'Saving...' : 'Save Settings'}
              </Button>
            </div>
          </div>

          {/* Reset Confirmation Modal */}
          <Modal
            isOpen={isResetModalOpen}
            onClose={() => setIsResetModalOpen(false)}
            title="Reset Password Policy"
          >
            <p className="text-sm text-gray-500">
              Are you sure you want to reset the password policy to system defaults?
              This will remove all custom password requirements for this tenant.
            </p>
            <div className="mt-4 flex justify-end gap-3">
              <Button variant="secondary" onClick={() => setIsResetModalOpen(false)}>
                Cancel
              </Button>
              <Button
                variant="danger"
                onClick={() => resetPolicyMutation.mutate()}
                disabled={resetPolicyMutation.isPending}
              >
                {resetPolicyMutation.isPending ? 'Resetting...' : 'Reset to Defaults'}
              </Button>
            </div>
          </Modal>
        </>
      ) : (
        // Render plugin tabs
        pluginTabs
          .filter((tab) => tab.id === activeTab)
          .map((tab) => {
            const TabComponent = tab.component;
            return (
              <div key={tab.id} className="bg-white shadow rounded-lg p-6">
                <TabComponent
                  tenantId={tenantId!}
                  tenant={tenantData2}
                  onSave={() => queryClient.invalidateQueries({ queryKey: ['tenant', tenantId] })}
                  onHasChanges={setHasChanges}
                  isActive={activeTab === tab.id}
                />
              </div>
            );
          })
      )}
    </div>
  );
}

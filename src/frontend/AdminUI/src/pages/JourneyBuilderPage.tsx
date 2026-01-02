import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import Button from '../components/common/Button';
import { SchemaFormRenderer } from '../components/common/SchemaFormRenderer';
import { journeyService, JourneyStep, StepTypeInfo, JourneyPolicy } from '../services/journeyService';
import { SimpleConditionBuilder, MappingBuilder } from '../components/journey';
import type { StepCondition, TransformMapping } from '../components/journey';
import {
  ArrowLeftIcon,
  PlusIcon,
  TrashIcon,
  ChevronUpIcon,
  ChevronDownIcon,
  Cog6ToothIcon,
  ArrowPathIcon,
  ExclamationCircleIcon,
  AdjustmentsHorizontalIcon
} from '@heroicons/react/24/outline';

const stepCategoryIcons: Record<string, string> = {
  Authentication: '🔐',
  'User Interaction': '👤',
  'User Management': '👥',
  'Flow Control': '⚡',
  Logic: '⚡',
  'Account Management': '⚙️',
  Billing: '💳',
  Plugins: '🧩',
  Custom: '🔧',
};

const stepCategoryColors: Record<string, { bg: string; border: string; text: string }> = {
  Authentication: { bg: 'bg-blue-50', border: 'border-blue-200', text: 'text-blue-700' },
  'User Interaction': { bg: 'bg-purple-50', border: 'border-purple-200', text: 'text-purple-700' },
  'User Management': { bg: 'bg-green-50', border: 'border-green-200', text: 'text-green-700' },
  'Flow Control': { bg: 'bg-amber-50', border: 'border-amber-200', text: 'text-amber-700' },
  Logic: { bg: 'bg-amber-50', border: 'border-amber-200', text: 'text-amber-700' },
  'Account Management': { bg: 'bg-slate-50', border: 'border-slate-200', text: 'text-slate-700' },
  Billing: { bg: 'bg-emerald-50', border: 'border-emerald-200', text: 'text-emerald-700' },
  Plugins: { bg: 'bg-pink-50', border: 'border-pink-200', text: 'text-pink-700' },
  Custom: { bg: 'bg-gray-50', border: 'border-gray-200', text: 'text-gray-700' },
};

const defaultSteps: Partial<Record<string, Partial<JourneyStep>>> = {
  LocalLogin: { displayName: 'Sign In', configuration: { allowRememberMe: true } },
  local_login: { displayName: 'Sign In', configuration: { allowRememberMe: true } },
  external_login: { displayName: 'Social Login', configuration: { autoProvision: true, autoRedirect: false } },
  // Billing step handlers
  subscription: { displayName: 'Subscription Selection', configuration: { allowSkip: true, filterByTenant: true } },
  'billing:subscription-check': { displayName: 'Subscription Check', configuration: { failureAction: 'block' } },
  CompositeLogin: {
    displayName: 'Combined Login',
    configuration: {
      enableLocalLogin: true,
      enableExternalLogin: true,
      enablePasskey: true,
      allowRememberMe: true,
      autoProvision: true
    }
  },
  Mfa: { displayName: 'MFA', configuration: { required: false, methods: ['totp', 'phone', 'email'] } },
  Consent: { displayName: 'Consent', configuration: { allowRemember: true } },
  ClaimsCollection: {
    displayName: 'Collect User Info',
    configuration: {
      title: 'Additional Information',
      fields: []
    }
  },
  Condition: { displayName: 'Condition', configuration: { conditions: [], combineWith: 'and' } },
  Transform: { displayName: 'Transform Claims', configuration: { mappings: [] } },
  ApiCall: { displayName: 'API Call', configuration: { method: 'GET', timeout: 30 } },
  TermsAcceptance: { displayName: 'Terms & Conditions', configuration: { requireCheckbox: true } },
  CaptchaVerification: { displayName: 'CAPTCHA', configuration: { provider: 'recaptcha' } },
  CreateUser: { displayName: 'Create User', configuration: { requireEmailVerification: true } },
  PasswordReset: { displayName: 'Password Reset', configuration: { tokenLifetimeMinutes: 60 } },
};

export default function JourneyBuilderPage() {
  const { policyId } = useParams<{ policyId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const isNew = policyId === 'new';

  const [policy, setPolicy] = useState<Partial<JourneyPolicy>>({
    name: '',
    description: '',
    type: 'SignIn',
    enabled: true,
    priority: 100,
    steps: [],
    conditions: [],
    session: {
      sessionLifetimeMinutes: 60,
      maxSessionLifetimeMinutes: 480,
      slidingExpiration: true,
      persistentSession: true
    },
    ui: {
      theme: '',
      logoUrl: '',
      backgroundColor: '',
      primaryColor: '',
      customCss: '',
      loginTemplate: ''
    },
    // Data collection defaults
    requiresAuthentication: true,
    persistSubmissions: false,
    allowDuplicates: true,
    maxSubmissions: 0
  });

  const [selectedStepIndex, setSelectedStepIndex] = useState<number | null>(null);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  const [isDirty, setIsDirty] = useState(false);

  // Fetch existing policy
  const { data: existingPolicy, isLoading } = useQuery({
    queryKey: ['journey', policyId],
    queryFn: () => journeyService.getPolicy(policyId!),
    enabled: !isNew && !!policyId,
  });

  // Fetch step types
  const { data: stepTypes } = useQuery({
    queryKey: ['stepTypes'],
    queryFn: () => journeyService.getStepTypes(),
  });

  useEffect(() => {
    if (existingPolicy) {
      setPolicy(existingPolicy);
    }
  }, [existingPolicy]);

  // Save mutation
  const saveMutation = useMutation({
    mutationFn: async () => {
      if (isNew) {
        return journeyService.createPolicy({
          name: policy.name!,
          description: policy.description,
          type: policy.type!,
          enabled: policy.enabled,
          priority: policy.priority,
          steps: policy.steps as JourneyStep[],
          conditions: policy.conditions,
          session: policy.session,
          ui: policy.ui,
          // Data collection settings
          persistSubmissions: policy.persistSubmissions,
          requiresAuthentication: policy.requiresAuthentication,
          allowDuplicates: policy.allowDuplicates,
          duplicateCheckFields: policy.duplicateCheckFields,
          maxSubmissions: policy.maxSubmissions,
          submissionCollection: policy.submissionCollection,
          successRedirectUrl: policy.successRedirectUrl,
          successMessage: policy.successMessage
        });
      } else {
        return journeyService.updatePolicy(policyId!, {
          name: policy.name,
          description: policy.description,
          enabled: policy.enabled,
          priority: policy.priority,
          steps: policy.steps as JourneyStep[],
          conditions: policy.conditions,
          session: policy.session,
          ui: policy.ui,
          // Data collection settings
          persistSubmissions: policy.persistSubmissions,
          requiresAuthentication: policy.requiresAuthentication,
          allowDuplicates: policy.allowDuplicates,
          duplicateCheckFields: policy.duplicateCheckFields,
          maxSubmissions: policy.maxSubmissions,
          submissionCollection: policy.submissionCollection,
          successRedirectUrl: policy.successRedirectUrl,
          successMessage: policy.successMessage
        });
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['journeys'] });
      setIsDirty(false);
      if (isNew) {
        navigate('/journeys');
      }
    },
  });

  // Validate mutation
  const validateMutation = useMutation({
    mutationFn: () => journeyService.validatePolicy(policy.steps as JourneyStep[]),
    onSuccess: (result) => {
      setValidationErrors(result.errors);
    },
  });

  const updatePolicy = useCallback((updates: Partial<JourneyPolicy>) => {
    setPolicy(prev => ({ ...prev, ...updates }));
    setIsDirty(true);
  }, []);

  const addStep = useCallback((stepType: StepTypeInfo) => {
    const newStep: JourneyStep = {
      id: `step_${Date.now()}`,
      type: stepType.type,
      displayName: defaultSteps[stepType.type]?.displayName || stepType.displayName,
      configuration: defaultSteps[stepType.type]?.configuration || {},
    };

    setPolicy(prev => ({
      ...prev,
      steps: [...(prev.steps || []), newStep]
    }));
    setIsDirty(true);
    setSelectedStepIndex((policy.steps?.length || 0));
  }, [policy.steps]);

  const updateStep = useCallback((index: number, updates: Partial<JourneyStep>) => {
    setPolicy(prev => ({
      ...prev,
      steps: prev.steps?.map((s, i) => i === index ? { ...s, ...updates } : s)
    }));
    setIsDirty(true);
  }, []);

  const removeStep = useCallback((index: number) => {
    setPolicy(prev => ({
      ...prev,
      steps: prev.steps?.filter((_, i) => i !== index)
    }));
    setIsDirty(true);
    setSelectedStepIndex(null);
  }, []);

  const moveStep = useCallback((index: number, direction: 'up' | 'down') => {
    const newIndex = direction === 'up' ? index - 1 : index + 1;
    if (newIndex < 0 || newIndex >= (policy.steps?.length || 0)) return;

    setPolicy(prev => {
      const steps = [...(prev.steps || [])];
      [steps[index], steps[newIndex]] = [steps[newIndex], steps[index]];
      return { ...prev, steps };
    });
    setIsDirty(true);
    setSelectedStepIndex(newIndex);
  }, [policy.steps?.length]);

  const groupedStepTypes = stepTypes?.reduce((acc, type) => {
    if (!acc[type.category]) acc[type.category] = [];
    acc[type.category].push(type);
    return acc;
  }, {} as Record<string, StepTypeInfo[]>) || {};

  if (isLoading) {
    return <div className="animate-pulse">Loading...</div>;
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          <button onClick={() => navigate('/journeys')} className="text-gray-500 hover:text-gray-700">
            <ArrowLeftIcon className="h-5 w-5" />
          </button>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {isNew ? 'Create User Journey' : 'Edit User Journey'}
            </h1>
            <p className="mt-1 text-sm text-gray-500">
              Define the steps users will go through during authentication
            </p>
          </div>
        </div>
        <div className="flex items-center space-x-3">
          <Button
            variant="secondary"
            onClick={() => validateMutation.mutate()}
            disabled={validateMutation.isPending}
          >
            {validateMutation.isPending ? (
              <ArrowPathIcon className="h-4 w-4 animate-spin mr-2" />
            ) : null}
            Validate
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending || !isDirty}
          >
            {saveMutation.isPending ? 'Saving...' : 'Save'}
          </Button>
        </div>
      </div>

      {/* Validation Errors */}
      {validationErrors.length > 0 && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <div className="flex items-center">
            <ExclamationCircleIcon className="h-5 w-5 text-red-400 mr-2" />
            <h3 className="text-sm font-medium text-red-800">Validation Errors</h3>
          </div>
          <ul className="mt-2 list-disc list-inside text-sm text-red-700">
            {validationErrors.map((error, i) => (
              <li key={i}>{error}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="grid grid-cols-12 gap-6">
        {/* Left Panel - Step Palette */}
        <div className="col-span-3">
          <Card>
            <CardHeader title="Add Steps" />
            <CardContent className="space-y-5">
              {Object.entries(groupedStepTypes).map(([category, types]) => {
                const categoryColor = stepCategoryColors[category] || stepCategoryColors.Custom;
                return (
                  <div key={category}>
                    <h4 className={`text-xs font-semibold uppercase tracking-wider mb-2 flex items-center gap-1.5 ${categoryColor.text}`}>
                      <span>{stepCategoryIcons[category]}</span>
                      <span>{category}</span>
                    </h4>
                    <div className="space-y-1">
                      {types.map((type) => (
                        <button
                          key={type.type}
                          onClick={() => addStep(type)}
                          disabled={!type.isAvailable}
                          className={`w-full text-left px-3 py-2 rounded-md text-sm transition-all group ${
                            type.isAvailable
                              ? `hover:${categoryColor.bg} ${categoryColor.text.replace('700', '600')} hover:shadow-sm border border-transparent hover:${categoryColor.border}`
                              : 'opacity-50 cursor-not-allowed text-gray-400'
                          }`}
                          title={type.description}
                        >
                          <div className="flex items-center justify-between">
                            <span className="font-medium">{type.displayName}</span>
                            <PlusIcon className="h-4 w-4 opacity-0 group-hover:opacity-100 transition-opacity" />
                          </div>
                        </button>
                      ))}
                    </div>
                  </div>
                );
              })}
            </CardContent>
          </Card>
        </div>

        {/* Center Panel - Journey Flow */}
        <div className="col-span-5">
          <Card>
            <CardHeader title="Journey Flow" />
            <CardContent>
              {/* Policy Info */}
              <div className="mb-6 space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">Name</label>
                  <input
                    type="text"
                    value={policy.name || ''}
                    onChange={(e) => updatePolicy({ name: e.target.value })}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    placeholder="My Sign-In Journey"
                  />
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700">Type</label>
                    <select
                      value={policy.type || 'SignIn'}
                      onChange={(e) => updatePolicy({ type: e.target.value })}
                      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    >
                      <optgroup label="Authentication">
                        <option value="SignIn">Sign In</option>
                        <option value="SignUp">Sign Up</option>
                        <option value="SignInSignUp">Sign In / Sign Up</option>
                        <option value="PasswordReset">Password Reset</option>
                        <option value="ProfileEdit">Profile Edit</option>
                        <option value="LinkAccount">Link Account</option>
                        <option value="Consent">Consent</option>
                      </optgroup>
                      <optgroup label="Data Collection (No Auth)">
                        <option value="Waitlist">Waitlist</option>
                        <option value="ContactForm">Contact Form</option>
                        <option value="Survey">Survey</option>
                        <option value="Feedback">Feedback</option>
                        <option value="DataCollection">Data Collection</option>
                      </optgroup>
                      <optgroup label="Other">
                        <option value="Custom">Custom</option>
                      </optgroup>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700">Priority</label>
                    <input
                      type="number"
                      value={policy.priority || 100}
                      onChange={(e) => updatePolicy({ priority: parseInt(e.target.value) })}
                      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    />
                  </div>
                </div>
              </div>

              {/* UI Configuration */}
              <div className="border-t pt-4">
                <h4 className="text-sm font-medium text-gray-700 mb-3">UI Customization</h4>
                <div className="space-y-3">
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Theme</label>
                      <select
                        value={policy.ui?.theme || ''}
                        onChange={(e) => updatePolicy({ ui: { ...policy.ui, theme: e.target.value || undefined } })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      >
                        <option value="">Default</option>
                        <option value="light">Light</option>
                        <option value="dark">Dark</option>
                        <option value="minimal">Minimal</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Login Template</label>
                      <select
                        value={policy.ui?.loginTemplate || ''}
                        onChange={(e) => updatePolicy({ ui: { ...policy.ui, loginTemplate: e.target.value || undefined } })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      >
                        <option value="">Standard</option>
                        <option value="social-first">Social First</option>
                        <option value="passkey-first">Passkey First</option>
                        <option value="combined">Combined (Tabs)</option>
                      </select>
                      <p className="mt-1 text-xs text-gray-500">Layout for Combined Login step</p>
                    </div>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Logo URL</label>
                      <input
                        type="text"
                        value={policy.ui?.logoUrl || ''}
                        onChange={(e) => updatePolicy({ ui: { ...policy.ui, logoUrl: e.target.value || undefined } })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        placeholder="https://..."
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Page Title</label>
                      <input
                        type="text"
                        value={policy.ui?.title || ''}
                        onChange={(e) => updatePolicy({ ui: { ...policy.ui, title: e.target.value || undefined } })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        placeholder="Sign in to continue"
                      />
                    </div>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Primary Color</label>
                      <div className="mt-1 flex items-center space-x-2">
                        <input
                          type="color"
                          value={policy.ui?.primaryColor || '#3b82f6'}
                          onChange={(e) => updatePolicy({ ui: { ...policy.ui, primaryColor: e.target.value } })}
                          className="h-8 w-8 rounded border border-gray-300 cursor-pointer"
                        />
                        <input
                          type="text"
                          value={policy.ui?.primaryColor || ''}
                          onChange={(e) => updatePolicy({ ui: { ...policy.ui, primaryColor: e.target.value || undefined } })}
                          className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                          placeholder="#3b82f6"
                        />
                      </div>
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Background Color</label>
                      <div className="mt-1 flex items-center space-x-2">
                        <input
                          type="color"
                          value={policy.ui?.backgroundColor || '#ffffff'}
                          onChange={(e) => updatePolicy({ ui: { ...policy.ui, backgroundColor: e.target.value } })}
                          className="h-8 w-8 rounded border border-gray-300 cursor-pointer"
                        />
                        <input
                          type="text"
                          value={policy.ui?.backgroundColor || ''}
                          onChange={(e) => updatePolicy({ ui: { ...policy.ui, backgroundColor: e.target.value || undefined } })}
                          className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                          placeholder="#ffffff"
                        />
                      </div>
                    </div>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-600">Custom CSS</label>
                    <textarea
                      value={policy.ui?.customCss || ''}
                      onChange={(e) => updatePolicy({ ui: { ...policy.ui, customCss: e.target.value || undefined } })}
                      rows={3}
                      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm font-mono text-xs"
                      placeholder=".journey-container { border-radius: 12px; }"
                    />
                    <p className="mt-1 text-xs text-gray-500">Custom CSS applied to this journey's pages</p>
                  </div>
                </div>
              </div>

              {/* Data Collection Settings - only show for data collection types */}
              {['Waitlist', 'ContactForm', 'Survey', 'Feedback', 'DataCollection'].includes(policy.type || '') && (
                <div className="border-t pt-4">
                  <h4 className="text-sm font-medium text-gray-700 mb-3">Data Collection Settings</h4>
                  <div className="space-y-3 bg-pink-50 border border-pink-200 rounded-lg p-3">
                    <div className="flex items-center">
                      <input
                        type="checkbox"
                        id="persistSubmissions"
                        checked={policy.persistSubmissions ?? true}
                        onChange={(e) => updatePolicy({ persistSubmissions: e.target.checked })}
                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                      <label htmlFor="persistSubmissions" className="ml-2 text-sm text-gray-700">
                        Save form submissions
                      </label>
                    </div>

                    <div className="flex items-center">
                      <input
                        type="checkbox"
                        id="requiresAuth"
                        checked={policy.requiresAuthentication ?? false}
                        onChange={(e) => updatePolicy({ requiresAuthentication: e.target.checked })}
                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                      <label htmlFor="requiresAuth" className="ml-2 text-sm text-gray-700">
                        Require authentication
                      </label>
                    </div>

                    <div className="flex items-center">
                      <input
                        type="checkbox"
                        id="allowDuplicates"
                        checked={policy.allowDuplicates ?? false}
                        onChange={(e) => updatePolicy({ allowDuplicates: e.target.checked })}
                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                      <label htmlFor="allowDuplicates" className="ml-2 text-sm text-gray-700">
                        Allow duplicate submissions
                      </label>
                    </div>

                    {!policy.allowDuplicates && (
                      <div>
                        <label className="block text-xs font-medium text-gray-600">Duplicate check fields</label>
                        <input
                          type="text"
                          value={(policy.duplicateCheckFields || []).join(', ')}
                          onChange={(e) => updatePolicy({
                            duplicateCheckFields: e.target.value.split(',').map(s => s.trim()).filter(Boolean)
                          })}
                          placeholder="email, phone"
                          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        />
                        <p className="mt-1 text-xs text-gray-500">Fields to check for duplicates (comma-separated)</p>
                      </div>
                    )}

                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className="block text-xs font-medium text-gray-600">Max submissions</label>
                        <input
                          type="number"
                          value={policy.maxSubmissions ?? 0}
                          onChange={(e) => updatePolicy({ maxSubmissions: parseInt(e.target.value) || 0 })}
                          min="0"
                          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        />
                        <p className="mt-1 text-xs text-gray-500">0 = unlimited</p>
                      </div>
                      <div>
                        <label className="block text-xs font-medium text-gray-600">Collection name</label>
                        <input
                          type="text"
                          value={policy.submissionCollection || ''}
                          onChange={(e) => updatePolicy({ submissionCollection: e.target.value || undefined })}
                          placeholder="Defaults to policy ID"
                          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        />
                      </div>
                    </div>

                    <div>
                      <label className="block text-xs font-medium text-gray-600">Success redirect URL</label>
                      <input
                        type="text"
                        value={policy.successRedirectUrl || ''}
                        onChange={(e) => updatePolicy({ successRedirectUrl: e.target.value || undefined })}
                        placeholder="https://yoursite.com/thank-you"
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      />
                    </div>

                    <div>
                      <label className="block text-xs font-medium text-gray-600">Success message</label>
                      <textarea
                        value={policy.successMessage || ''}
                        onChange={(e) => updatePolicy({ successMessage: e.target.value || undefined })}
                        rows={2}
                        placeholder="Thank you for your submission!"
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      />
                    </div>
                  </div>
                </div>
              )}

              {/* Steps */}
              <div className="border-t pt-4">
                <h4 className="text-sm font-medium text-gray-700 mb-3">Steps</h4>
                <div className="space-y-2">
                  {policy.steps?.map((step, index) => {
                    const stepType = stepTypes?.find(t => t.type === step.type);
                    const category = stepType?.category || 'Custom';
                    const categoryColor = stepCategoryColors[category] || stepCategoryColors.Custom;

                    return (
                      <div
                        key={step.id}
                        onClick={() => setSelectedStepIndex(index)}
                        className={`flex items-center justify-between p-3 rounded-lg border-2 cursor-pointer transition-all ${
                          selectedStepIndex === index
                            ? 'border-primary-500 bg-primary-50 shadow-sm'
                            : `${categoryColor.border} ${categoryColor.bg} hover:shadow-sm`
                        }`}
                      >
                        <div className="flex items-center">
                          <span className={`w-7 h-7 rounded-full flex items-center justify-center mr-3 text-sm font-semibold ${
                            selectedStepIndex === index
                              ? 'bg-primary-600 text-white'
                              : `${categoryColor.text} bg-white border ${categoryColor.border}`
                          }`}>
                            {index + 1}
                          </span>
                          <div>
                            <div className="font-medium text-gray-900 text-sm flex items-center gap-2">
                              {step.displayName || step.type}
                              {step.optional && (
                                <span className="text-xs px-1.5 py-0.5 bg-gray-100 text-gray-500 rounded">optional</span>
                              )}
                            </div>
                            <div className="text-xs text-gray-500 flex items-center gap-1.5">
                              <span className={`inline-block w-2 h-2 rounded-full ${categoryColor.text.replace('text-', 'bg-')}`}></span>
                              {stepType?.description?.slice(0, 40) || step.type}
                              {(stepType?.description?.length || 0) > 40 && '...'}
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center space-x-1">
                          <button
                            onClick={(e) => { e.stopPropagation(); moveStep(index, 'up'); }}
                            disabled={index === 0}
                            className="p-1.5 text-gray-400 hover:text-gray-600 hover:bg-white/50 rounded disabled:opacity-30"
                          >
                            <ChevronUpIcon className="h-4 w-4" />
                          </button>
                          <button
                            onClick={(e) => { e.stopPropagation(); moveStep(index, 'down'); }}
                            disabled={index === (policy.steps?.length || 0) - 1}
                            className="p-1.5 text-gray-400 hover:text-gray-600 hover:bg-white/50 rounded disabled:opacity-30"
                          >
                            <ChevronDownIcon className="h-4 w-4" />
                          </button>
                          <button
                            onClick={(e) => { e.stopPropagation(); removeStep(index); }}
                            className="p-1.5 text-red-400 hover:text-red-600 hover:bg-red-50 rounded"
                          >
                            <TrashIcon className="h-4 w-4" />
                          </button>
                        </div>
                      </div>
                    );
                  })}

                  {(!policy.steps || policy.steps.length === 0) && (
                    <div className="text-center py-8 text-gray-500">
                      <p className="text-sm">No steps added yet</p>
                      <p className="text-xs mt-1">Add steps from the palette on the left</p>
                    </div>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Right Panel - Step Configuration */}
        <div className="col-span-4">
          <Card>
            <CardHeader
              title={selectedStepIndex !== null ? 'Step Configuration' : 'Select a Step'}
              action={
                selectedStepIndex !== null && (
                  <Cog6ToothIcon className="h-5 w-5 text-gray-400" />
                )
              }
            />
            <CardContent>
              {selectedStepIndex !== null && policy.steps?.[selectedStepIndex] ? (
                <StepConfigEditor
                  step={policy.steps[selectedStepIndex]}
                  stepTypes={stepTypes || []}
                  allSteps={policy.steps}
                  onChange={(updates) => updateStep(selectedStepIndex, updates)}
                />
              ) : (
                <div className="text-center py-8 text-gray-500">
                  <p className="text-sm">Select a step to configure</p>
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}

// Step Configuration Editor Component
function StepConfigEditor({
  step,
  stepTypes,
  allSteps,
  onChange
}: {
  step: JourneyStep;
  stepTypes: StepTypeInfo[];
  allSteps: JourneyStep[];
  onChange: (updates: Partial<JourneyStep>) => void;
}) {
  const stepType = stepTypes.find(t => t.type === step.type);
  const configSchema = stepType?.configurationSchema || {};

  return (
    <div className="space-y-4">
      <div>
        <label className="block text-sm font-medium text-gray-700">Step ID</label>
        <input
          type="text"
          value={step.id}
          onChange={(e) => onChange({ id: e.target.value })}
          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700">Display Name</label>
        <input
          type="text"
          value={step.displayName || ''}
          onChange={(e) => onChange({ displayName: e.target.value })}
          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
        />
      </div>

      <div className="flex items-center">
        <input
          type="checkbox"
          id="optional"
          checked={step.optional || false}
          onChange={(e) => onChange({ optional: e.target.checked })}
          className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
        />
        <label htmlFor="optional" className="ml-2 text-sm text-gray-700">
          Optional (continue on failure)
        </label>
      </div>

      {/* Specialized editor for ClaimsCollection - supports both PascalCase and snake_case */}
      {(step.type === 'ClaimsCollection' || step.type === 'claims_collection') && (
        <ClaimsCollectionEditor
          configuration={step.configuration || {}}
          onChange={(config) => onChange({ configuration: config })}
        />
      )}

      {/* Specialized editor for ApiCall - supports both PascalCase and snake_case */}
      {(step.type === 'ApiCall' || step.type === 'api_call') && (
        <ApiCallStepEditor
          configuration={step.configuration || {}}
          onChange={(config) => onChange({ configuration: config })}
        />
      )}

      {/* Generic config editor for other step types - uses SchemaFormRenderer for x-enumSource support */}
      {!['ClaimsCollection', 'claims_collection', 'ApiCall', 'api_call'].includes(step.type) && Object.keys(configSchema).length > 0 && (
        <div className="border-t pt-4">
          <h4 className="text-sm font-medium text-gray-700 mb-3">Configuration</h4>
          <SchemaFormRenderer
            schema={{ type: 'object', properties: configSchema }}
            value={(step.configuration as Record<string, unknown>) || {}}
            onChange={(config) => onChange({ configuration: config })}
          />
        </div>
      )}

      {/* Condition Builder - for Condition step type */}
      {(step.type === 'Condition' || step.type === 'condition') && (
        <div className="border-t pt-4">
          <SimpleConditionBuilder
            conditions={(step.configuration?.conditions as StepCondition[]) || []}
            onChange={(conditions) => onChange({
              configuration: { ...step.configuration, conditions }
            })}
          />
          <div className="mt-3">
            <label className="block text-sm font-medium text-gray-700 mb-2">Combine conditions with</label>
            <select
              value={(step.configuration?.combineWith as string) || 'and'}
              onChange={(e) => onChange({
                configuration: { ...step.configuration, combineWith: e.target.value }
              })}
              className="rounded-md border-gray-300 text-sm"
            >
              <option value="and">AND (all must match)</option>
              <option value="or">OR (any must match)</option>
            </select>
          </div>
        </div>
      )}

      {/* Transform Mapping Builder - for Transform step type */}
      {(step.type === 'Transform' || step.type === 'transform') && (
        <div className="border-t pt-4">
          <MappingBuilder
            mappings={(step.configuration?.mappings as TransformMapping[]) || []}
            onChange={(mappings) => onChange({
              configuration: { ...step.configuration, mappings }
            })}
            mode="transform"
            title="Data Transformations"
            description="Transform and map data between journey steps"
          />
        </div>
      )}

      {/* Step Execution Conditions */}
      <div className="border-t pt-4">
        <details className="group">
          <summary className="flex items-center justify-between cursor-pointer text-sm font-medium text-gray-700 hover:text-gray-900">
            <span className="flex items-center gap-2">
              <AdjustmentsHorizontalIcon className="h-4 w-4" />
              Execution Conditions
            </span>
            <span className="text-xs text-gray-500 group-open:hidden">
              {step.conditions?.length || 0} condition(s)
            </span>
          </summary>
          <div className="mt-3">
            <p className="text-xs text-gray-500 mb-3">
              Define when this step should execute. If conditions are not met, the step will be skipped.
            </p>
            <SimpleConditionBuilder
              conditions={(step.conditions as StepCondition[]) || []}
              onChange={(conditions) => onChange({ conditions })}
            />
          </div>
        </details>
      </div>

      {/* Flow Control */}
      <div className="border-t pt-4">
        <h4 className="text-sm font-medium text-gray-700 mb-3">Flow Control</h4>
        <div className="space-y-3">
          <div>
            <label className="block text-sm font-medium text-gray-700">On Success</label>
            <select
              value={step.onSuccess || ''}
              onChange={(e) => onChange({ onSuccess: e.target.value || undefined })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            >
              <option value="">Next step (default)</option>
              {allSteps.filter(s => s.id !== step.id).map(s => (
                <option key={s.id} value={s.id}>{s.displayName || s.id}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">On Failure</label>
            <select
              value={step.onFailure || ''}
              onChange={(e) => onChange({ onFailure: e.target.value || undefined })}
              className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            >
              <option value="">Fail journey (default)</option>
              {allSteps.filter(s => s.id !== step.id).map(s => (
                <option key={s.id} value={s.id}>{s.displayName || s.id}</option>
              ))}
            </select>
          </div>

          {/* Branch navigation */}
          <div>
            <details className="group">
              <summary className="flex items-center justify-between cursor-pointer text-sm font-medium text-gray-700 hover:text-gray-900">
                <span>Branches</span>
                <span className="text-xs text-gray-500 group-open:hidden">
                  {Object.keys(step.branches || {}).length} branch(es)
                </span>
              </summary>
              <div className="mt-2 space-y-2">
                <p className="text-xs text-gray-500">
                  Define named branches for conditional navigation (e.g., "mfa_required" → MFA step)
                </p>
                {Object.entries(step.branches || {}).map(([branchName, targetStepId]) => (
                  <div key={branchName} className="flex items-center gap-2">
                    <input
                      type="text"
                      value={branchName}
                      onChange={(e) => {
                        const newBranches = { ...step.branches };
                        delete newBranches[branchName];
                        newBranches[e.target.value] = targetStepId;
                        onChange({ branches: newBranches });
                      }}
                      placeholder="Branch name"
                      className="flex-1 rounded-md border-gray-300 text-sm"
                    />
                    <span className="text-gray-400">→</span>
                    <select
                      value={targetStepId}
                      onChange={(e) => onChange({
                        branches: { ...step.branches, [branchName]: e.target.value }
                      })}
                      className="flex-1 rounded-md border-gray-300 text-sm"
                    >
                      <option value="">Select step...</option>
                      {allSteps.filter(s => s.id !== step.id).map(s => (
                        <option key={s.id} value={s.id}>{s.displayName || s.id}</option>
                      ))}
                    </select>
                    <button
                      type="button"
                      onClick={() => {
                        const newBranches = { ...step.branches };
                        delete newBranches[branchName];
                        onChange({ branches: newBranches });
                      }}
                      className="text-red-400 hover:text-red-600"
                    >
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  </div>
                ))}
                <button
                  type="button"
                  onClick={() => onChange({
                    branches: { ...step.branches, [`branch_${Date.now()}`]: '' }
                  })}
                  className="text-sm text-primary-600 hover:text-primary-800 flex items-center"
                >
                  <PlusIcon className="h-4 w-4 mr-1" /> Add Branch
                </button>
              </div>
            </details>
          </div>
        </div>
      </div>

      {/* Advanced Options */}
      <div className="border-t pt-4">
        <details className="group">
          <summary className="flex items-center justify-between cursor-pointer text-sm font-medium text-gray-700 hover:text-gray-900">
            <span>Advanced Options</span>
          </summary>
          <div className="mt-3 space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-gray-600">Timeout (seconds)</label>
                <input
                  type="number"
                  value={step.timeoutSeconds || ''}
                  onChange={(e) => onChange({ timeoutSeconds: e.target.value ? parseInt(e.target.value) : undefined })}
                  placeholder="Use policy default"
                  className="mt-1 block w-full rounded-md border-gray-300 text-sm"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600">Max Retries</label>
                <input
                  type="number"
                  value={step.maxRetries || 0}
                  onChange={(e) => onChange({ maxRetries: parseInt(e.target.value) || 0 })}
                  min="0"
                  max="5"
                  className="mt-1 block w-full rounded-md border-gray-300 text-sm"
                />
              </div>
            </div>

            <div className="flex items-center">
              <input
                type="checkbox"
                id="skipIfCompleted"
                checked={step.skipIfCompleted || false}
                onChange={(e) => onChange({ skipIfCompleted: e.target.checked })}
                className="rounded border-gray-300 text-blue-600"
              />
              <label htmlFor="skipIfCompleted" className="ml-2 text-xs text-gray-600">
                Skip if already completed in this session
              </label>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600">Error Message Template</label>
              <input
                type="text"
                value={step.errorMessageTemplate || ''}
                onChange={(e) => onChange({ errorMessageTemplate: e.target.value || undefined })}
                placeholder="Custom error message shown on failure"
                className="mt-1 block w-full rounded-md border-gray-300 text-sm"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600">Required Claims</label>
              <input
                type="text"
                value={(step.requiredClaims || []).join(', ')}
                onChange={(e) => onChange({
                  requiredClaims: e.target.value.split(',').map(s => s.trim()).filter(Boolean)
                })}
                placeholder="email, sub"
                className="mt-1 block w-full rounded-md border-gray-300 text-sm"
              />
              <p className="text-xs text-gray-500 mt-1">Claims that must exist before this step runs</p>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600">Output Claims</label>
              <input
                type="text"
                value={(step.outputClaims || []).join(', ')}
                onChange={(e) => onChange({
                  outputClaims: e.target.value.split(',').map(s => s.trim()).filter(Boolean)
                })}
                placeholder="mfaVerified, authLevel"
                className="mt-1 block w-full rounded-md border-gray-300 text-sm"
              />
              <p className="text-xs text-gray-500 mt-1">Claims this step adds to the journey context</p>
            </div>
          </div>
        </details>
      </div>
    </div>
  );
}

// Specialized editor for ClaimsCollection fields
function ClaimsCollectionEditor({
  configuration,
  onChange
}: {
  configuration: Record<string, any>;
  onChange: (config: Record<string, any>) => void;
}) {
  const fields = configuration.fields || [];
  const [expandedField, setExpandedField] = useState<number | null>(null);

  const fieldTypes = [
    { value: 'text', label: 'Text' },
    { value: 'email', label: 'Email' },
    { value: 'password', label: 'Password' },
    { value: 'number', label: 'Number' },
    { value: 'date', label: 'Date' },
    { value: 'tel', label: 'Phone' },
    { value: 'url', label: 'URL' },
    { value: 'textarea', label: 'Textarea' },
    { value: 'select', label: 'Dropdown' },
    { value: 'radio', label: 'Radio Buttons' },
    { value: 'checkbox', label: 'Checkbox' },
  ];

  const addField = () => {
    const newField = {
      name: `field_${Date.now()}`,
      type: 'text',
      label: 'New Field',
      required: false
    };
    onChange({ ...configuration, fields: [...fields, newField] });
    setExpandedField(fields.length);
  };

  const updateField = (index: number, updates: Record<string, any>) => {
    const newFields = [...fields];
    newFields[index] = { ...newFields[index], ...updates };
    onChange({ ...configuration, fields: newFields });
  };

  const removeField = (index: number) => {
    onChange({ ...configuration, fields: fields.filter((_: any, i: number) => i !== index) });
    setExpandedField(null);
  };

  const moveField = (index: number, direction: 'up' | 'down') => {
    const newIndex = direction === 'up' ? index - 1 : index + 1;
    if (newIndex < 0 || newIndex >= fields.length) return;
    const newFields = [...fields];
    [newFields[index], newFields[newIndex]] = [newFields[newIndex], newFields[index]];
    onChange({ ...configuration, fields: newFields });
    setExpandedField(newIndex);
  };

  return (
    <div className="border-t pt-4 space-y-4">
      <h4 className="text-sm font-medium text-gray-700">Form Settings</h4>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-gray-600">Title</label>
          <input
            type="text"
            value={configuration.title || ''}
            onChange={(e) => onChange({ ...configuration, title: e.target.value })}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            placeholder="Form title"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600">Submit Button</label>
          <input
            type="text"
            value={configuration.submitButtonText || ''}
            onChange={(e) => onChange({ ...configuration, submitButtonText: e.target.value })}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
            placeholder="Continue"
          />
        </div>
      </div>

      <div>
        <label className="block text-xs font-medium text-gray-600">Description</label>
        <textarea
          value={configuration.description || ''}
          onChange={(e) => onChange({ ...configuration, description: e.target.value })}
          rows={2}
          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
          placeholder="Instructions for the user..."
        />
      </div>

      <div className="border-t pt-4">
        <div className="flex items-center justify-between mb-3">
          <h4 className="text-sm font-medium text-gray-700">Form Fields</h4>
          <button
            onClick={addField}
            className="text-sm text-primary-600 hover:text-primary-800 flex items-center"
          >
            <PlusIcon className="h-4 w-4 mr-1" /> Add Field
          </button>
        </div>

        <div className="space-y-2">
          {fields.map((field: any, index: number) => (
            <div key={index} className="border rounded-lg">
              <div
                className="flex items-center justify-between p-3 cursor-pointer hover:bg-gray-50"
                onClick={() => setExpandedField(expandedField === index ? null : index)}
              >
                <div className="flex items-center space-x-3">
                  <span className="text-xs text-gray-400 w-5">{index + 1}</span>
                  <div>
                    <div className="font-medium text-sm">{field.label || field.name}</div>
                    <div className="text-xs text-gray-500">
                      {field.type} {field.required && '• required'}
                    </div>
                  </div>
                </div>
                <div className="flex items-center space-x-1">
                  <button
                    onClick={(e) => { e.stopPropagation(); moveField(index, 'up'); }}
                    disabled={index === 0}
                    className="p-1 text-gray-400 hover:text-gray-600 disabled:opacity-30"
                  >
                    <ChevronUpIcon className="h-4 w-4" />
                  </button>
                  <button
                    onClick={(e) => { e.stopPropagation(); moveField(index, 'down'); }}
                    disabled={index === fields.length - 1}
                    className="p-1 text-gray-400 hover:text-gray-600 disabled:opacity-30"
                  >
                    <ChevronDownIcon className="h-4 w-4" />
                  </button>
                  <button
                    onClick={(e) => { e.stopPropagation(); removeField(index); }}
                    className="p-1 text-red-400 hover:text-red-600"
                  >
                    <TrashIcon className="h-4 w-4" />
                  </button>
                </div>
              </div>

              {expandedField === index && (
                <div className="p-3 border-t bg-gray-50 space-y-3">
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Field Name</label>
                      <input
                        type="text"
                        value={field.name || ''}
                        onChange={(e) => updateField(index, { name: e.target.value })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        placeholder="field_name"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Type</label>
                      <select
                        value={field.type || 'text'}
                        onChange={(e) => updateField(index, { type: e.target.value })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      >
                        {fieldTypes.map(ft => (
                          <option key={ft.value} value={ft.value}>{ft.label}</option>
                        ))}
                      </select>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Label</label>
                      <input
                        type="text"
                        value={field.label || ''}
                        onChange={(e) => updateField(index, { label: e.target.value })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        placeholder="Field Label"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Claim Type</label>
                      <input
                        type="text"
                        value={field.claimType || ''}
                        onChange={(e) => updateField(index, { claimType: e.target.value })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        placeholder="Defaults to field name"
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-xs font-medium text-gray-600">Placeholder</label>
                    <input
                      type="text"
                      value={field.placeholder || ''}
                      onChange={(e) => updateField(index, { placeholder: e.target.value })}
                      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-medium text-gray-600">Help Text</label>
                    <input
                      type="text"
                      value={field.description || ''}
                      onChange={(e) => updateField(index, { description: e.target.value })}
                      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    />
                  </div>

                  {/* Options for select/radio */}
                  {(field.type === 'select' || field.type === 'radio') && (
                    <div>
                      <label className="block text-xs font-medium text-gray-600 mb-1">Options</label>
                      <div className="space-y-1">
                        {(field.options || []).map((opt: any, optIdx: number) => (
                          <div key={optIdx} className="flex items-center space-x-2">
                            <input
                              type="text"
                              value={typeof opt === 'string' ? opt : opt.value}
                              onChange={(e) => {
                                const newOpts = [...(field.options || [])];
                                newOpts[optIdx] = typeof opt === 'string'
                                  ? e.target.value
                                  : { ...opt, value: e.target.value, label: e.target.value };
                                updateField(index, { options: newOpts });
                              }}
                              placeholder="Option value"
                              className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                            />
                            <button
                              onClick={() => {
                                const newOpts = (field.options || []).filter((_: any, i: number) => i !== optIdx);
                                updateField(index, { options: newOpts });
                              }}
                              className="text-red-400 hover:text-red-600"
                            >
                              <TrashIcon className="h-4 w-4" />
                            </button>
                          </div>
                        ))}
                        <button
                          onClick={() => {
                            const newOpts = [...(field.options || []), ''];
                            updateField(index, { options: newOpts });
                          }}
                          className="text-xs text-primary-600 hover:text-primary-800"
                        >
                          + Add option
                        </button>
                      </div>
                    </div>
                  )}

                  <div className="flex items-center space-x-4">
                    <label className="flex items-center">
                      <input
                        type="checkbox"
                        checked={field.required || false}
                        onChange={(e) => updateField(index, { required: e.target.checked })}
                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                      <span className="ml-2 text-xs text-gray-600">Required</span>
                    </label>
                    <label className="flex items-center">
                      <input
                        type="checkbox"
                        checked={field.hidden || false}
                        onChange={(e) => updateField(index, { hidden: e.target.checked })}
                        className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                      <span className="ml-2 text-xs text-gray-600">Hidden</span>
                    </label>
                  </div>

                  {/* Validation */}
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Min Length</label>
                      <input
                        type="number"
                        value={field.minLength || ''}
                        onChange={(e) => updateField(index, { minLength: e.target.value ? parseInt(e.target.value) : undefined })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600">Max Length</label>
                      <input
                        type="number"
                        value={field.maxLength || ''}
                        onChange={(e) => updateField(index, { maxLength: e.target.value ? parseInt(e.target.value) : undefined })}
                        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-xs font-medium text-gray-600">Validation Pattern (Regex)</label>
                    <input
                      type="text"
                      value={field.pattern || ''}
                      onChange={(e) => updateField(index, { pattern: e.target.value })}
                      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                      placeholder="e.g., ^[A-Z]{2}[0-9]{4}$"
                    />
                  </div>
                </div>
              )}
            </div>
          ))}

          {fields.length === 0 && (
            <div className="text-center py-4 text-gray-500 text-sm">
              No fields defined. Click "Add Field" to create one.
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// Specialized editor for ApiCall step
function ApiCallStepEditor({
  configuration,
  onChange
}: {
  configuration: Record<string, any>;
  onChange: (config: Record<string, any>) => void;
}) {
  const [expandedSection, setExpandedSection] = useState<string | null>('basic');

  const headers = configuration.headers || {};
  const authentication = configuration.authentication || {};
  const outputMapping = configuration.outputMapping || {};

  const authTypes = [
    { value: 'none', label: 'None' },
    { value: 'bearer', label: 'Bearer Token' },
    { value: 'basic', label: 'Basic Auth' },
    { value: 'apikey', label: 'API Key' },
  ];

  const httpMethods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

  const updateConfig = (key: string, value: any) => {
    onChange({ ...configuration, [key]: value });
  };

  const updateAuth = (key: string, value: any) => {
    onChange({ ...configuration, authentication: { ...authentication, [key]: value } });
  };

  const addHeader = () => {
    const newHeaders = { ...headers, [`header_${Date.now()}`]: '' };
    updateConfig('headers', newHeaders);
  };

  const updateHeader = (oldKey: string, newKey: string, value: string) => {
    const newHeaders = { ...headers };
    if (oldKey !== newKey) {
      delete newHeaders[oldKey];
    }
    newHeaders[newKey] = value;
    updateConfig('headers', newHeaders);
  };

  const removeHeader = (key: string) => {
    const newHeaders = { ...headers };
    delete newHeaders[key];
    updateConfig('headers', newHeaders);
  };

  const toggleSection = (section: string) => {
    setExpandedSection(expandedSection === section ? null : section);
  };

  return (
    <div className="border-t pt-4 space-y-4">
      {/* Basic Settings */}
      <div className="border rounded-lg">
        <button
          className="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
          onClick={() => toggleSection('basic')}
        >
          <span className="font-medium text-sm">Basic Settings</span>
          <ChevronDownIcon className={`h-4 w-4 transition-transform ${expandedSection === 'basic' ? 'rotate-180' : ''}`} />
        </button>
        {expandedSection === 'basic' && (
          <div className="p-3 border-t bg-gray-50 space-y-3">
            <div>
              <label className="block text-xs font-medium text-gray-600">API URL *</label>
              <input
                type="text"
                value={configuration.url || ''}
                onChange={(e) => updateConfig('url', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                placeholder="https://api.example.com/endpoint/{claim:user_id}"
              />
              <p className="mt-1 text-xs text-gray-500">
                Supports placeholders: {'{claim:name}'}, {'{state:userId}'}, {'{input:field}'}
              </p>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-gray-600">HTTP Method</label>
                <select
                  value={configuration.method || 'GET'}
                  onChange={(e) => updateConfig('method', e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                >
                  {httpMethods.map(method => (
                    <option key={method} value={method}>{method}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600">Timeout (seconds)</label>
                <input
                  type="number"
                  value={configuration.timeout ?? 30}
                  onChange={(e) => updateConfig('timeout', parseInt(e.target.value) || 30)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Headers */}
      <div className="border rounded-lg">
        <button
          className="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
          onClick={() => toggleSection('headers')}
        >
          <span className="font-medium text-sm">Headers ({Object.keys(headers).length})</span>
          <ChevronDownIcon className={`h-4 w-4 transition-transform ${expandedSection === 'headers' ? 'rotate-180' : ''}`} />
        </button>
        {expandedSection === 'headers' && (
          <div className="p-3 border-t bg-gray-50 space-y-2">
            {Object.entries(headers).map(([key, value]: [string, any]) => (
              <div key={key} className="flex items-center space-x-2">
                <input
                  type="text"
                  value={key}
                  onChange={(e) => updateHeader(key, e.target.value, value)}
                  placeholder="Header name"
                  className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
                <input
                  type="text"
                  value={value}
                  onChange={(e) => updateHeader(key, key, e.target.value)}
                  placeholder="Value"
                  className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
                <button
                  onClick={() => removeHeader(key)}
                  className="text-red-400 hover:text-red-600"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              </div>
            ))}
            <button
              onClick={addHeader}
              className="text-sm text-primary-600 hover:text-primary-800 flex items-center"
            >
              <PlusIcon className="h-4 w-4 mr-1" /> Add Header
            </button>
          </div>
        )}
      </div>

      {/* Authentication */}
      <div className="border rounded-lg">
        <button
          className="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
          onClick={() => toggleSection('auth')}
        >
          <span className="font-medium text-sm">Authentication</span>
          <ChevronDownIcon className={`h-4 w-4 transition-transform ${expandedSection === 'auth' ? 'rotate-180' : ''}`} />
        </button>
        {expandedSection === 'auth' && (
          <div className="p-3 border-t bg-gray-50 space-y-3">
            <div>
              <label className="block text-xs font-medium text-gray-600">Type</label>
              <select
                value={authentication.type || 'none'}
                onChange={(e) => updateAuth('type', e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              >
                {authTypes.map(type => (
                  <option key={type.value} value={type.value}>{type.label}</option>
                ))}
              </select>
            </div>

            {authentication.type === 'bearer' && (
              <div>
                <label className="block text-xs font-medium text-gray-600">Bearer Token</label>
                <input
                  type="text"
                  value={authentication.token || ''}
                  onChange={(e) => updateAuth('token', e.target.value)}
                  placeholder="{claim:access_token} or static token"
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
            )}

            {authentication.type === 'basic' && (
              <>
                <div>
                  <label className="block text-xs font-medium text-gray-600">Username</label>
                  <input
                    type="text"
                    value={authentication.username || ''}
                    onChange={(e) => updateAuth('username', e.target.value)}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-gray-600">Password</label>
                  <input
                    type="password"
                    value={authentication.password || ''}
                    onChange={(e) => updateAuth('password', e.target.value)}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                  />
                </div>
              </>
            )}

            {authentication.type === 'apikey' && (
              <>
                <div>
                  <label className="block text-xs font-medium text-gray-600">Header Name</label>
                  <input
                    type="text"
                    value={authentication.headerName || 'X-API-Key'}
                    onChange={(e) => updateAuth('headerName', e.target.value)}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-gray-600">API Key</label>
                  <input
                    type="text"
                    value={authentication.apiKey || ''}
                    onChange={(e) => updateAuth('apiKey', e.target.value)}
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                  />
                </div>
              </>
            )}
          </div>
        )}
      </div>

      {/* Retry & Error Handling */}
      <div className="border rounded-lg">
        <button
          className="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
          onClick={() => toggleSection('retry')}
        >
          <span className="font-medium text-sm">Retry & Error Handling</span>
          <ChevronDownIcon className={`h-4 w-4 transition-transform ${expandedSection === 'retry' ? 'rotate-180' : ''}`} />
        </button>
        {expandedSection === 'retry' && (
          <div className="p-3 border-t bg-gray-50 space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-gray-600">Retry Count</label>
                <input
                  type="number"
                  value={configuration.retryCount ?? 0}
                  onChange={(e) => updateConfig('retryCount', parseInt(e.target.value) || 0)}
                  min="0"
                  max="5"
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600">Retry Delay (ms)</label>
                <input
                  type="number"
                  value={configuration.retryDelay ?? 1000}
                  onChange={(e) => updateConfig('retryDelay', parseInt(e.target.value) || 1000)}
                  min="100"
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
            </div>

            <div className="flex items-center">
              <input
                type="checkbox"
                id="failOnError"
                checked={configuration.failOnError ?? true}
                onChange={(e) => updateConfig('failOnError', e.target.checked)}
                className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
              />
              <label htmlFor="failOnError" className="ml-2 text-xs text-gray-600">
                Fail journey on API error
              </label>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600">Continue on Status Codes</label>
              <input
                type="text"
                value={(configuration.continueOnStatus || []).join(', ')}
                onChange={(e) => updateConfig('continueOnStatus',
                  e.target.value.split(',').map(s => parseInt(s.trim())).filter(n => !isNaN(n))
                )}
                placeholder="404, 409"
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
              <p className="mt-1 text-xs text-gray-500">
                HTTP status codes that should continue even on error
              </p>
            </div>
          </div>
        )}
      </div>

      {/* Request Body */}
      <div className="border rounded-lg">
        <button
          className="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
          onClick={() => toggleSection('body')}
        >
          <span className="font-medium text-sm">Request Body</span>
          <ChevronDownIcon className={`h-4 w-4 transition-transform ${expandedSection === 'body' ? 'rotate-180' : ''}`} />
        </button>
        {expandedSection === 'body' && (
          <div className="p-3 border-t bg-gray-50 space-y-3">
            <div>
              <label className="block text-xs font-medium text-gray-600">Body Template (JSON)</label>
              <textarea
                value={configuration.bodyTemplate || ''}
                onChange={(e) => updateConfig('bodyTemplate', e.target.value)}
                rows={4}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm font-mono text-xs"
                placeholder={'{\n  "userId": "{claim:sub}",\n  "email": "{claim:email}"\n}'}
              />
              <p className="mt-1 text-xs text-gray-500">
                JSON template with placeholders for dynamic values
              </p>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600">Include Claims</label>
              <input
                type="text"
                value={(configuration.bodyFromClaims || []).join(', ')}
                onChange={(e) => updateConfig('bodyFromClaims',
                  e.target.value.split(',').map(s => s.trim()).filter(Boolean)
                )}
                placeholder="sub, email, name"
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
              />
              <p className="mt-1 text-xs text-gray-500">
                Automatically include these claims in the request body
              </p>
            </div>
          </div>
        )}
      </div>

      {/* Output Mapping */}
      <div className="border rounded-lg">
        <button
          className="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
          onClick={() => toggleSection('output')}
        >
          <span className="font-medium text-sm">Output Mapping ({Object.keys(outputMapping).length})</span>
          <ChevronDownIcon className={`h-4 w-4 transition-transform ${expandedSection === 'output' ? 'rotate-180' : ''}`} />
        </button>
        {expandedSection === 'output' && (
          <div className="p-3 border-t bg-gray-50 space-y-2">
            <p className="text-xs text-gray-500 mb-2">
              Map JSON response paths to claims (e.g., user.id → external_id)
            </p>
            {Object.entries(outputMapping).map(([jsonPath, claimType]: [string, any]) => (
              <div key={jsonPath} className="flex items-center space-x-2">
                <input
                  type="text"
                  value={jsonPath}
                  onChange={(e) => {
                    const newMapping = { ...outputMapping };
                    delete newMapping[jsonPath];
                    newMapping[e.target.value] = claimType;
                    updateConfig('outputMapping', newMapping);
                  }}
                  placeholder="JSON path (e.g., user.id)"
                  className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
                <span className="text-gray-400">→</span>
                <input
                  type="text"
                  value={claimType}
                  onChange={(e) => {
                    updateConfig('outputMapping', { ...outputMapping, [jsonPath]: e.target.value });
                  }}
                  placeholder="Claim type"
                  className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
                <button
                  onClick={() => {
                    const newMapping = { ...outputMapping };
                    delete newMapping[jsonPath];
                    updateConfig('outputMapping', newMapping);
                  }}
                  className="text-red-400 hover:text-red-600"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              </div>
            ))}
            <button
              onClick={() => updateConfig('outputMapping', { ...outputMapping, [`path_${Date.now()}`]: '' })}
              className="text-sm text-primary-600 hover:text-primary-800 flex items-center"
            >
              <PlusIcon className="h-4 w-4 mr-1" /> Add Mapping
            </button>

            <div className="pt-2 border-t mt-2">
              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="includeResponseMeta"
                  checked={configuration.includeResponseMeta ?? false}
                  onChange={(e) => updateConfig('includeResponseMeta', e.target.checked)}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
                <label htmlFor="includeResponseMeta" className="ml-2 text-xs text-gray-600">
                  Include response metadata (_api_status, _api_success)
                </label>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

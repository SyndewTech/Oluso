import api from './api';

// Types
export interface JourneyStep {
  id: string;
  type: string;
  displayName?: string;
  configuration?: Record<string, unknown>;
  conditions?: StepCondition[];
  onSuccess?: string;
  onFailure?: string;
  branches?: Record<string, string>;
  pluginName?: string;
  optional?: boolean;
  timeoutSeconds?: number;
  maxRetries?: number;
  skipIfCompleted?: boolean;
  errorMessageTemplate?: string;
  requiredClaims?: string[];
  outputClaims?: string[];
}

export interface StepCondition {
  type: string;   // journeyData, claim, context, path
  field: string;
  operator: string;
  value?: string;
  logicalOperator: string;  // and, or
  negate: boolean;
}

export interface PolicyCondition {
  type: string;
  operator: string;
  value: string;
  customEvaluator?: string;
}

export interface ClaimMapping {
  sourceType: string;
  sourcePath: string;
  targetClaimType: string;
  defaultValue?: string;
}

export interface SessionConfiguration {
  sessionLifetimeMinutes: number;
  maxSessionLifetimeMinutes: number;
  slidingExpiration: boolean;
  persistentSession: boolean;
  requireReauthAfterMinutes?: number;
  ssoMode?: string;
  cookieName?: string;
  cookieSameSite?: string;
}

export interface UiConfiguration {
  theme?: string;
  logoUrl?: string;
  backgroundColor?: string;
  primaryColor?: string;
  customCss?: string;
  localization?: Record<string, string>;
  customPageTemplate?: string;
  loginTemplate?: string;
  title?: string;
}

export interface JourneyPolicy {
  id: string;
  name: string;
  description?: string;
  type: string;
  tenantId?: string;
  enabled: boolean;
  priority: number;
  steps: JourneyStep[];
  conditions: PolicyCondition[];
  outputClaims: ClaimMapping[];
  session: SessionConfiguration;
  ui?: UiConfiguration;
  version: number;
  createdAt: string;
  updatedAt?: string;
  // Data collection properties
  requiresAuthentication: boolean;
  persistSubmissions: boolean;
  submissionCollection?: string;
  maxSubmissions: number;
  allowDuplicates: boolean;
  duplicateCheckFields?: string[];
  successRedirectUrl?: string;
  successMessage?: string;
}

export interface JourneyPolicyListItem {
  id: string;
  name: string;
  description?: string;
  type: string;
  enabled: boolean;
  priority: number;
  isGlobal: boolean;
  stepCount: number;
  version: number;
  createdAt: string;
  updatedAt?: string;
}

export interface StepTypeInfo {
  type: string;
  category: string;
  displayName: string;
  description: string;
  isAvailable: boolean;
  configurationSchema?: Record<string, unknown>;
}

export interface CreatePolicyRequest {
  id?: string;
  name: string;
  description?: string;
  type: string;
  enabled?: boolean;
  priority?: number;
  steps: JourneyStep[];
  conditions?: PolicyCondition[];
  outputClaims?: ClaimMapping[];
  session?: Partial<SessionConfiguration>;
  ui?: UiConfiguration;
  // Data collection properties
  requiresAuthentication?: boolean;
  persistSubmissions?: boolean;
  submissionCollection?: string;
  maxSubmissions?: number;
  allowDuplicates?: boolean;
  duplicateCheckFields?: string[];
  successRedirectUrl?: string;
  successMessage?: string;
}

export interface UpdatePolicyRequest {
  name?: string;
  description?: string;
  enabled?: boolean;
  priority?: number;
  steps?: JourneyStep[];
  conditions?: PolicyCondition[];
  outputClaims?: ClaimMapping[];
  session?: Partial<SessionConfiguration>;
  ui?: UiConfiguration;
  // Data collection properties
  requiresAuthentication?: boolean;
  persistSubmissions?: boolean;
  submissionCollection?: string;
  maxSubmissions?: number;
  allowDuplicates?: boolean;
  duplicateCheckFields?: string[];
  successRedirectUrl?: string;
  successMessage?: string;
}

export interface ValidationResult {
  isValid: boolean;
  errors: string[];
}

export interface AvailablePlugin {
  name: string;
  displayName: string;
  description?: string;
  version?: string;
  author?: string;
  scope: string;
  isGlobal: boolean;
  requiredClaims?: string[];
  outputClaims?: string[];
  configSchema?: Record<string, unknown>;
}

// API Service
export const journeyService = {
  // Get all policies
  async getPolicies(includeGlobal = true): Promise<JourneyPolicyListItem[]> {
    const response = await api.get('/journeys', {
      params: { includeGlobal }
    });
    return response.data;
  },

  // Get single policy
  async getPolicy(policyId: string): Promise<JourneyPolicy> {
    const response = await api.get(`/journeys/${policyId}`);
    return response.data;
  },

  // Create policy
  async createPolicy(request: CreatePolicyRequest): Promise<JourneyPolicy> {
    const response = await api.post('/journeys', request);
    return response.data;
  },

  // Update policy
  async updatePolicy(policyId: string, request: UpdatePolicyRequest): Promise<JourneyPolicy> {
    const response = await api.put(`/journeys/${policyId}`, request);
    return response.data;
  },

  // Delete policy
  async deletePolicy(policyId: string): Promise<void> {
    await api.delete(`/journeys/${policyId}`);
  },

  // Clone policy
  async clonePolicy(policyId: string, newId?: string, newName?: string): Promise<JourneyPolicy> {
    const response = await api.post(`/journeys/${policyId}/clone`, {
      newId,
      newName
    });
    return response.data;
  },

  // Enable/disable policy
  async setStatus(policyId: string, enabled: boolean): Promise<{ enabled: boolean }> {
    const response = await api.patch(`/journeys/${policyId}/status`, { enabled });
    return response.data;
  },

  // Get available step types
  async getStepTypes(): Promise<StepTypeInfo[]> {
    const response = await api.get('/journeys/step-types');
    return response.data;
  },

  // Validate policy
  async validatePolicy(steps: JourneyStep[]): Promise<ValidationResult> {
    const response = await api.post('/journeys/validate', { steps });
    return response.data;
  },

  // Get available plugins for CustomPlugin steps
  async getAvailablePlugins(): Promise<AvailablePlugin[]> {
    const response = await api.get('/journeys/available-plugins');
    return response.data;
  }
};

export default journeyService;

// Types for tenant feature gating

export interface FeatureStatus {
  enabled: boolean;
  value?: string;
  displayName: string;
  description: string;
  category: string;
}

export interface LimitStatus {
  limit: number;
  current: number;
  remaining: number;
  isUnlimited: boolean;
  displayName: string;
  usagePercentage: number;
}

export interface PlanSummary {
  id: string;
  name: string;
  displayName?: string;
  billingInterval?: string;
}

export interface TenantFeaturesResponse {
  tenantId: string;
  billingEnabled: boolean;
  features: Record<string, FeatureStatus>;
  limits: Record<string, LimitStatus>;
  plan?: PlanSummary;
  subscriptionStatus?: string;
  currentPeriodEnd?: string;
  isTrialing: boolean;
  trialEnd?: string;
}

export interface FeatureCheckResponse {
  feature: string;
  isEnabled: boolean;
  reason?: string;
  value?: string;
  upgradeUrl?: string;
}

export interface LimitCheckResponse {
  limitType: string;
  isAllowed: boolean;
  isUnlimited: boolean;
  limit: number;
  current: number;
  remaining: number;
  reason?: string;
  hasOverageCharge: boolean;
  overagePricePerUnit?: number;
  upgradeUrl?: string;
}

export interface FeatureDefinition {
  key: string;
  displayName: string;
  description: string;
  category: string;
}

export interface LimitDefinition {
  key: string;
  displayName: string;
  description: string;
  category: string;
}

export interface FeatureDefinitionsResponse {
  features: FeatureDefinition[];
  limits: LimitDefinition[];
}

// Feature key constants (matching backend PlatformFeatures)
export const FEATURE_KEYS = {
  // Protocol
  SAML: 'saml',
  FIDO2: 'fido2',
  LDAP: 'ldap',
  SCIM: 'scim',
  // Authentication
  MFA: 'mfa',
  SOCIAL_LOGIN: 'social_login',
  ENTERPRISE_SSO: 'enterprise_sso',
  PASSWORDLESS: 'passwordless',
  ADAPTIVE_MFA: 'adaptive_mfa',
  // Branding
  CUSTOM_BRANDING: 'custom_branding',
  CUSTOM_EMAILS: 'custom_emails',
  CUSTOM_DOMAIN: 'custom_domain',
  WHITE_LABEL: 'white_label',
  // Security
  AUDIT_LOGS: 'audit_logs',
  THREAT_DETECTION: 'threat_detection',
  IP_RESTRICTIONS: 'ip_restrictions',
  SESSION_MANAGEMENT: 'session_management',
  DATA_RESIDENCY: 'data_residency',
  // Integration
  WEBHOOKS: 'webhooks',
  CUSTOM_ATTRIBUTES: 'custom_attributes',
  ROLES_PERMISSIONS: 'roles_permissions',
  ADMIN_API: 'admin_api',
  USER_SUBSCRIPTIONS: 'user_subscriptions',
  // Support
  PRIORITY_SUPPORT: 'priority_support',
  DEDICATED_SUPPORT: 'dedicated_support',
  SLA: 'sla',
} as const;

export type FeatureKey = typeof FEATURE_KEYS[keyof typeof FEATURE_KEYS];

// Limit key constants (matching backend PlatformLimits)
export const LIMIT_KEYS = {
  MONTHLY_ACTIVE_USERS: 'mau',
  TOTAL_USERS: 'total_users',
  CLIENTS: 'clients',
  AUTH_REQUESTS: 'auth_requests',
  API_REQUESTS: 'api_requests',
  AUDIT_RETENTION_DAYS: 'audit_retention_days',
  WEBHOOK_ENDPOINTS: 'webhook_endpoints',
  EXTERNAL_PROVIDERS: 'external_providers',
  ROLES: 'roles',
} as const;

export type LimitKey = typeof LIMIT_KEYS[keyof typeof LIMIT_KEYS];

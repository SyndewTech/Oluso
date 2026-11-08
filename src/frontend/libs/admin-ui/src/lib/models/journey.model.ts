// Journey Step
export interface JourneyStep {
  id: string;
  type: string;
  displayName?: string;
  configuration?: Record<string, unknown>;
  conditions?: StepCondition[];
  layout?: string;
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

// Step Condition
export interface StepCondition {
  type: string; // journeyData, claim, context, user, path — see JourneyConditionValues
  field: string;
  operator: string;
  value?: string;
  logicalOperator: string; // and, or
  negate: boolean;
}

// Policy Condition
export interface PolicyCondition {
  type: string;
  operator: string;
  value: string;
  customEvaluator?: string;
}

// Claim Mapping
export interface ClaimMapping {
  sourceType: 'journeyData' | 'claim' | 'literal' | 'input' | 'context';
  sourcePath: string;
  targetClaimType: string;
  defaultValue?: string;
  transform?: string;
}

// Transform Mapping
export interface TransformMapping {
  source: string;
  sourceType: string;
  target: string;
  /** A transform name from the step's published catalog; copy when unset. */
  transform?: string;
  transformConfig?: Record<string, string>;
  defaultValue?: string;
  required?: boolean;
}

/** A transform the transform step runs, as its schema publishes it (x-transforms). */
export interface TransformCatalogEntry {
  name: string;
  label: string;
  description?: string;
  options: { name: string; label: string; default?: string | null; multiline?: boolean }[];
}

// Session Configuration
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

// UI Configuration
export interface UiConfiguration {
  theme?: string;
  layout?: string;
  progressStyle?: string;
  logoUrl?: string;
  backgroundColor?: string;
  primaryColor?: string;
  customCss?: string;
  localization?: Record<string, string>;
  localeOverrides?: Record<string, Record<string, string>>;
  title?: string;
}

// Journey Policy
export interface JourneyPolicy {
  id: string;
  /** The handle clients use (?policy=, share links, the public API); unique within the tenant. */
  slug: string;
  name: string;
  description?: string;
  type: string;
  tenantId?: string;
  enabled: boolean;
  priority: number;
  /** Feature key of the owning module; null for a platform journey. A module journey's answers go only to that module. */
  ownerModule?: string | null;
  ownerModuleName?: string | null;
  sharing?: JourneySharing;
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
  // Embed settings
  allowedOrigins?: string;
  apiKey?: string;
  requireApiKey: boolean;
}

/** Who may use a module's journey: only that module, or any module (each run's answers go to the module that started it). */
export type JourneySharing = 'Private' | 'Shared';

/** A module the current admin may make a journey's owner. */
export interface JourneyOwnerModule {
  key: string;
  name: string;
}

// Journey Policy List Item
export interface JourneyPolicyListItem {
  id: string;
  slug: string;
  name: string;
  description?: string;
  type: string;
  enabled: boolean;
  priority: number;
  isGlobal: boolean;
  ownerModule?: string | null;
  ownerModuleName?: string | null;
  sharing?: JourneySharing;
  stepCount: number;
  version: number;
  createdAt: string;
  updatedAt?: string;
}

// Step Type Info
export interface StepTypeInfo {
  type: string;
  category: string;
  displayName: string;
  description: string;
  /** ui-icon name; falls back to the category's icon. */
  icon?: string;
  /** Owning module, when the step isn't part of core. */
  module?: string;
  isAvailable: boolean;
  /** The step can check its settings from the builder. */
  testable?: boolean;
  configurationSchema?: Record<string, unknown>;
  availableBranches?: StepBranchInfo[];
}

export interface StepTestResult {
  success: boolean;
  message: string;
  details?: string[];
}

// A named outcome a step type can emit, used to wire conditional navigation
export interface StepBranchInfo {
  id: string;
  displayName?: string;
  description?: string;
}

// Create Policy Request
export interface CreatePolicyRequest {
  /** Derived from the name when omitted. */
  slug?: string;
  name: string;
  description?: string;
  type: string;
  enabled?: boolean;
  priority?: number;
  ownerModule?: string | null;
  sharing?: JourneySharing;
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
  // Embed settings
  allowedOrigins?: string;
  apiKey?: string;
  requireApiKey?: boolean;
}

// Update Policy Request
export interface UpdatePolicyRequest {
  name?: string;
  /** A new slug; clients still using the old one stop reaching the journey. */
  slug?: string;
  description?: string;
  enabled?: boolean;
  priority?: number;
  /** A module's feature key, or '' for a platform journey; omitted leaves it unchanged. */
  ownerModule?: string;
  sharing?: JourneySharing;
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
  // Embed settings
  allowedOrigins?: string;
  apiKey?: string;
  requireApiKey?: boolean;
}

// Validation Result
export interface ValidationResult {
  isValid: boolean;
  errors: string[];
  /** Non-fatal issues — likely-typo placeholders, branch cycles, etc. Don't block save. */
  warnings: string[];
}

// Policy Type Colors
export const POLICY_TYPE_COLORS: Record<string, string> = {
  // Authentication types
  SignIn: 'bg-blue-100 text-blue-800',
  SignUp: 'bg-green-100 text-green-800',
  SignInSignUp: 'bg-purple-100 text-purple-800',
  PasswordReset: 'bg-yellow-100 text-yellow-800',
  ProfileEdit: 'bg-orange-100 text-orange-800',
  LinkAccount: 'bg-indigo-100 text-indigo-800',
  Consent: 'bg-teal-100 text-teal-800',
  // Data collection types
  Waitlist: 'bg-pink-100 text-pink-800',
  ContactForm: 'bg-cyan-100 text-cyan-800',
  Survey: 'bg-lime-100 text-lime-800',
  Feedback: 'bg-amber-100 text-amber-800',
  DataCollection: 'bg-rose-100 text-rose-800',
  // Other
  Custom: 'bg-gray-100 text-gray-800',
};

// Step Category Icons
export const STEP_CATEGORY_ICONS: Record<string, string> = {
  Authentication: 'lock',
  'User Interaction': 'user',
  'User Management': 'users',
  'Flow Control': 'lightning',
  Logic: 'lightning',
  'Account Management': 'cog',
  Billing: 'credit-card',
  Plugins: 'puzzle',
  Custom: 'wrench',
};

// Step Category Colors
export const STEP_CATEGORY_COLORS: Record<string, { bg: string; border: string; text: string }> = {
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

/** Order of the step categories in the builder; others follow alphabetically. */
export const STEP_CATEGORY_ORDER = ['Authentication', 'User Interaction', 'User Management', 'Account Management', 'Flow Control', 'Logic', 'Billing', 'Plugins', 'Custom'];

// Default Steps Configuration
export const DEFAULT_STEPS: Partial<Record<string, Partial<JourneyStep>>> = {
  LocalLogin: { displayName: 'Sign In', configuration: { allowRememberMe: true } },
  local_login: { displayName: 'Sign In', configuration: { allowRememberMe: true } },
  external_login: { displayName: 'Social Login', configuration: { autoProvision: true, autoRedirect: false } },
  subscription: { displayName: 'Subscription Selection', configuration: { allowSkip: true, filterByTenant: true } },
  'billing:subscription-check': { displayName: 'Subscription Check', configuration: { failureAction: 'block' } },
  CompositeLogin: {
    displayName: 'Combined Login',
    configuration: {
      enableLocalLogin: true,
      enableExternalLogin: true,
      enablePasskey: true,
      allowRememberMe: true,
      autoProvision: true,
    },
  },
  Mfa: { displayName: 'MFA', configuration: { required: false, methods: ['totp', 'phone', 'email'] } },
  passwordless_email: {
    displayName: 'Passwordless Email',
    configuration: {
      mode: 'otp',
      codeLength: 6,
      expirationMinutes: 15,
      allowSignUp: false,
      maxAttempts: 3,
      emailSubject: '',
      emailTemplate: '',
    },
  },
  passwordless_sms: {
    displayName: 'Passwordless SMS',
    configuration: {
      codeLength: 6,
      expirationMinutes: 10,
      allowSignUp: false,
      maxAttempts: 3,
      messageTemplate: '',
      toLinkRecipient: false,
    },
  },
  Consent: { displayName: 'Consent', configuration: { allowRemember: true } },
  DynamicForm: {
    displayName: 'Dynamic Form',
    configuration: {
      title: 'Additional Information',
      fields: [],
    },
  },
  Condition: { displayName: 'Condition', configuration: { conditions: [], combineWith: 'and' } },
  Transform: { displayName: 'Transform Claims', configuration: { mappings: [] } },
  ApiCall: { displayName: 'API Call', configuration: { method: 'GET', timeout: 30 } },
  TermsAcceptance: { displayName: 'Terms & Conditions', configuration: { requireCheckbox: true } },
  CaptchaVerification: { displayName: 'CAPTCHA', configuration: { provider: 'recaptcha' } },
  CreateUser: { displayName: 'Create User', configuration: { requireEmailVerification: true } },
  PasswordReset: { displayName: 'Password Reset', configuration: { tokenLifetimeMinutes: 60 } },
};

// Default Session Configuration
export const DEFAULT_SESSION_CONFIGURATION: SessionConfiguration = {
  sessionLifetimeMinutes: 60,
  maxSessionLifetimeMinutes: 480,
  slidingExpiration: true,
  persistentSession: true,
};

// Default UI Configuration
export const DEFAULT_UI_CONFIGURATION: UiConfiguration = {
  theme: '',
  layout: '',
  logoUrl: '',
  backgroundColor: '',
  primaryColor: '',
  customCss: '',
  progressStyle: '',
};

// Condition Operators
export const CONDITION_OPERATORS = [
  { value: 'eq', label: '=' },
  { value: 'ne', label: '!=' },
  { value: 'gt', label: '>' },
  { value: 'lt', label: '<' },
  { value: 'gte', label: '>=' },
  { value: 'lte', label: '<=' },
  { value: 'contains', label: 'contains' },
  { value: 'starts_with', label: 'starts with' },
  { value: 'ends_with', label: 'ends with' },
  { value: 'exists', label: 'exists' },
  { value: 'not_exists', label: 'not exists' },
  { value: 'empty', label: 'is empty' },
  { value: 'not_empty', label: 'not empty' },
  { value: 'in', label: 'in list' },
  { value: 'not_in', label: 'not in list' },
  { value: 'regex', label: 'matches regex' },
  { value: 'true', label: 'is true' },
  { value: 'false', label: 'is false' },
];

// Operators that don't require a value
export const NO_VALUE_OPERATORS = ['exists', 'not_exists', 'empty', 'not_empty', 'true', 'false'];

// Source Types
export const SOURCE_TYPES = [
  { value: 'journeyData', label: 'Journey Data', description: 'Data stored during the journey' },
  { value: 'claim', label: 'Claim', description: 'User claims from authentication' },
  { value: 'context', label: 'Context', description: 'Journey context (userId, tenantId, clientId)' },
  { value: 'user', label: 'User Profile', description: 'The signed-in user\'s stored profile' },
  { value: 'literal', label: 'Literal', description: 'Static value' },
  { value: 'input', label: 'Step Input', description: 'Data from previous step or user input' },
];


// Field Suggestions
// Keys the journey runtime writes: request parameters at start, then earlier steps' output.
export const JOURNEY_DATA_SUGGESTIONS = [
  'login_hint',
  'acrValues',
  'scopes',
  'client_id',
  'ui_locales',
  'locale',
  'domain_hint',
  'sub',
  'email',
  'name',
  'auth_method',
  'amr',
  'idp',
  'mfa_provider',
  'remember_me',
];

// The journey user's claims (as their session carries them), then claims earlier steps produced.
export const CLAIM_SUGGESTIONS = [
  'sub',
  'email',
  'email_verified',
  'name',
  'given_name',
  'family_name',
  'preferred_username',
  'phone_number',
  'phone_number_verified',
  'picture',
  'locale',
  'zoneinfo',
  'role',
  'tenant_id',
  'amr',
  'idp',
];

export const CONTEXT_SUGGESTIONS = ['userId', 'tenantId', 'clientId', 'isAuthenticated'];

// Stored-profile fields of the journey's user; any other name reads a custom profile property.
export const USER_SUGGESTIONS = [
  'email',
  'email_verified',
  'phone_number',
  'phone_number_verified',
  'first_name',
  'last_name',
  'display_name',
  'two_factor_enabled',
  'has_password',
  'is_federated',
];

// Dynamic Form Field Types
export const FIELD_TYPES = [
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
  { value: 'file', label: 'File upload' },
];

/** Kinds a file field can accept, recognised by content on the server. */
export const FILE_FIELD_KINDS = [
  { value: 'pdf', label: 'PDF' },
  { value: 'jpeg', label: 'JPEG photo' },
  { value: 'png', label: 'PNG image' },
  { value: 'heic', label: 'HEIC photo (iPhone)' },
  { value: 'webp', label: 'WebP image' },
  { value: 'gif', label: 'GIF image' },
];

export const DEFAULT_FILE_FIELD_KINDS = ['pdf', 'jpeg', 'png'];

// HTTP Methods
export const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

// Authentication Types for API Call
export const AUTH_TYPES = [
  { value: 'none', label: 'None' },
  { value: 'bearer', label: 'Bearer Token' },
  { value: 'basic', label: 'Basic Auth' },
  { value: 'apikey', label: 'API Key' },
];

export interface ImportPolicyOptions {
  newName?: string;
  /** Slug for the imported journey instead of the file's (e.g. when the file's is in use). */
  newSlug?: string;
  /** Derive a free slug from the journey's name instead of keeping the file's. */
  generateSlug?: boolean;
}

/** 409 body when the slug a create, update, clone or import asks for is in use. */
export interface PolicySlugConflict {
  code: 'policy_slug_taken';
  slug: string;
  suggestedSlug: string;
  errors: string[];
}

/** Slugs appear in URLs (?policy=, share links) and are unique within a tenant. */
export const POLICY_SLUG_MAX_LENGTH = 64;
export const POLICY_SLUG_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;

export function policySlugConflict(error: unknown): PolicySlugConflict | null {
  const body = (error as { status?: number; error?: PolicySlugConflict } | null)?.error;
  return (error as { status?: number })?.status === 409 && body?.code === 'policy_slug_taken' ? body : null;
}

/** The slug the server derives from a name: "Business Sign-in (EU)" → "business-sign-in-eu". */
export function slugify(name: string): string {
  const slug = name.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  return slug.slice(0, POLICY_SLUG_MAX_LENGTH).replace(/-+$/, '') || 'journey';
}

/** Result of importing a .journey file: the new (disabled) policy plus what must be re-entered. */
export interface ImportPolicyResult {
  policy: JourneyPolicy;
  /** Redacted step settings dropped on import, as "{stepId}.{key}". */
  redactedSettings: string[];
  warnings: string[];
}

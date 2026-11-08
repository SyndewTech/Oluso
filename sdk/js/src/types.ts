/**
 * SDK Configuration
 */
export interface OlusoConfig {
  /**
   * Base URL of the Oluso server
   * @example "https://auth.oluso.io"
   */
  serverUrl: string;

  /**
   * Tenant identifier (subdomain or ID). Leave it out when the server URL is the tenant's own host, or when your
   * `fetch` already names the tenant.
   * @example "acme" or "tenant_abc123"
   */
  tenant?: string;

  /**
   * Optional public API key for rate limiting/analytics
   * Not used for authentication - journeys handle their own auth
   */
  apiKey?: string;

  /**
   * Custom fetch implementation (for SSR or testing)
   */
  fetch?: typeof fetch;

  /**
   * Extra headers for each request, e.g. the signed-in user's access token (and DPoP proof) for a session your
   * own backend started for that user. Called per request with its method and URL.
   */
  headers?: (request: { method: string; url: string }) => Record<string, string> | Promise<Record<string, string>>;
}

/**
 * Field definition from a journey policy
 */
export interface JourneyField {
  name: string;
  type: 'text' | 'email' | 'password' | 'number' | 'date' | 'tel' | 'url' | 'textarea' | 'select' | 'radio' | 'checkbox';
  label: string;
  placeholder?: string;
  description?: string;
  required: boolean;
  options?: Array<{ value: string; label: string }>;
  validation?: {
    minLength?: number;
    maxLength?: number;
    pattern?: string;
    min?: number;
    max?: number;
  };
}

/**
 * Journey metadata returned from the API
 */
export interface JourneyInfo {
  id: string;
  name: string;
  type: string;
  description?: string;
  fields: JourneyField[];
  ui?: {
    title?: string;
    logoUrl?: string;
    primaryColor?: string;
    backgroundColor?: string;
  };
  requiresAuthentication: boolean;
}

/**
 * Submission result
 */
export interface SubmissionResult {
  success: boolean;
  submissionId?: string;
  message?: string;
  redirectUrl?: string;
  errors?: Record<string, string>;
}

/**
 * A journey running step by step through the headless API (start, step, session).
 * Every headless call returns the session as it stands after the call.
 */
export interface JourneySession {
  sessionId: string;
  /** The journey's slug */
  slug: string;
  status: 'in_progress' | 'completed' | 'failed' | 'cancelled' | 'expired';
  success: boolean;
  completed: boolean;
  /** The step waiting for input, while in progress */
  currentStep?: string;
  /** Same as currentStep: set while there is a step to complete */
  nextStep?: string;
  completedSteps: string[];
  expiresAt?: string;
  /** What to render for the current step */
  step?: JourneyStepView;
  /** The step continues elsewhere (e.g. an external identity provider) */
  redirectUrl?: string;
  /** Authentication journeys: the authorization code to redeem with the PKCE verifier */
  code?: string;
  /** Data-collection journeys: the success message once completed */
  message?: string;
  error?: string;
  errorDescription?: string;
}

/** A step to render: its definition and the model the server renders it from */
export interface JourneyStepView {
  id: string;
  /** Step type, e.g. local_login, mfa, dynamic_form */
  type: string;
  displayName?: string;
  /** The server view the step renders as */
  view?: string;
  /** Fields, options, messages and errors for the step */
  model?: Record<string, unknown>;
  /** The step as a form any client can draw (see JourneyStepForm) */
  form?: JourneyStepForm;
  /** The hosted view's markup, for a step that has no form description (custom pages, plugin views) */
  html?: string;
  actions: JourneyStepAction[];
}

/**
 * A step as a form. Posting its fields with the clicked action's name and value is exactly what the hosted page
 * posts, so any journey step can be drawn by any client.
 */
export interface JourneyStepForm {
  title?: string;
  description?: string;
  /** What went wrong with the last submission */
  error?: string;
  /** Something to tell the person that isn't an error */
  notice?: string;
  fields: JourneyFormField[];
  /** The buttons, in order; each posts the fields with its own name and value */
  actions: JourneyFormAction[];
  links: JourneyFormLink[];
  /** Browser work for the step: passkey, captcha, QR code, recovery codes or waiting */
  widget?: JourneyFormWidget;
}

export type JourneyFormFieldType =
  | 'text' | 'email' | 'password' | 'number' | 'date' | 'tel' | 'url' | 'textarea' | 'select' | 'radio'
  | 'checkbox' | 'file' | 'hidden' | 'otp' | 'content';

export interface JourneyFormField {
  name: string;
  type: JourneyFormFieldType | string;
  label?: string;
  placeholder?: string;
  description?: string;
  /** Guidance under the input, e.g. upload limits */
  hint?: string;
  required: boolean;
  readOnly: boolean;
  value?: string;
  error?: string;
  options?: JourneyFormOption[];
  pattern?: string;
  patternError?: string;
  minLength?: number;
  maxLength?: number;
  min?: string;
  max?: string;
  rows?: number;
  autocomplete?: string;
  autofocus: boolean;
  accept?: string;
  maxFiles?: number;
  maxFileSizeMb?: number;
  capture?: string;
  /** Phone: countries to pick from; the country is posted as `${name}__country` */
  countries?: JourneyFormCountry[];
  country?: string;
  showWhen?: JourneyFormCondition;
  /** Shown only while every one of these is met too */
  showWhenAll?: JourneyFormCondition[];
  group?: string;
}

export interface JourneyFormOption {
  value: string;
  label: string;
  description?: string;
  disabled: boolean;
}

export interface JourneyFormCountry {
  code: string;
  name: string;
  dialCode: string;
  flag?: string;
}

export interface JourneyFormCondition {
  field: string;
  operator: string;
  value?: string;
}

export interface JourneyFormAction {
  /** The posted name; absent for a button that posts no name of its own */
  name?: string;
  value?: string;
  label: string;
  /** Text shown with the button, e.g. "Didn't get the code?" before a resend link */
  description?: string;
  /** A question to confirm before the button posts; the renderer asks inline */
  confirm?: string;
  /** More values the button posts */
  values?: Record<string, string>;
  /** False: the button posts only its own name, value and values, none of the step's fields */
  postsFields: boolean;
  kind: 'primary' | 'secondary' | 'link' | 'cancel' | string;
  skipValidation: boolean;
}

export interface JourneyFormLink {
  label: string;
  url: string;
}

export interface JourneyFormWidget {
  type: 'webauthn_get' | 'webauthn_create' | 'captcha' | 'qr' | 'codes' | 'poll' | string;
  data?: Record<string, unknown>;
  /** Where the widget's result is posted */
  resultField?: string;
  /** The action posted with the result */
  submit?: JourneyFormAction;
}

export interface JourneyStepAction {
  id: string;
  label: string;
  isPrimary: boolean;
  isCancel: boolean;
}

/** The session after a step was submitted */
export type StepResult = JourneySession;

/**
 * Event types emitted by the SDK
 */
export type OlusoEventType =
  | 'journey:started'
  | 'journey:step'
  | 'journey:completed'
  | 'journey:error'
  | 'submission:success'
  | 'submission:error'
  | 'auth:success'
  | 'auth:error';

export interface OlusoEvent {
  type: OlusoEventType;
  /** The journey's slug */
  slug: string;
  /** The headless session, for journey events */
  sessionId?: string;
  data?: unknown;
  error?: Error;
}

export type OlusoEventListener = (event: OlusoEvent) => void;

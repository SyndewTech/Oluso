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
   * Tenant identifier (subdomain or ID)
   * @example "acme" or "tenant_abc123"
   */
  tenant: string;

  /**
   * Optional public API key for rate limiting/analytics
   * Not used for authentication - journeys handle their own auth
   */
  apiKey?: string;

  /**
   * Custom fetch implementation (for SSR or testing)
   */
  fetch?: typeof fetch;
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
 * Journey session for multi-step journeys
 */
export interface JourneySession {
  sessionId: string;
  journeyId: string;
  currentStep: string;
  completedSteps: string[];
  data: Record<string, unknown>;
  expiresAt: string;
}

/**
 * Step result for multi-step journeys
 */
export interface StepResult {
  success: boolean;
  nextStep?: string;
  completed?: boolean;
  redirectUrl?: string;
  errors?: Record<string, string>;
  data?: Record<string, unknown>;
}

/**
 * Authentication result (for auth journeys)
 */
export interface AuthResult {
  success: boolean;
  redirectUrl?: string;
  error?: string;
  code?: string; // Authorization code for OIDC
  state?: string;
}

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
  journeyId: string;
  data?: unknown;
  error?: Error;
}

export type OlusoEventListener = (event: OlusoEvent) => void;

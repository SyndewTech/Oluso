/**
 * Auth SDK Types
 */

import type { JourneyField, JourneyStepView } from '../types';

export interface OidcEndpoints {
  /** Authorization endpoint */
  authorization?: string;
  /** Token endpoint */
  token?: string;
  /** UserInfo endpoint */
  userinfo?: string;
  /** End session / logout endpoint */
  endSession?: string;
  /** JWKS URI */
  jwks?: string;
}

export interface OlusoAuthConfig {
  /** Base URL of the Oluso server */
  serverUrl: string;

  /** Tenant identifier */
  tenant: string;

  /** OAuth2 client ID (public client) */
  clientId: string;

  /** Redirect URI for OIDC code flow (optional if using inline/modal auth) */
  redirectUri?: string;

  /** Scopes to request (default: "openid profile email") */
  scopes?: string;

  /**
   * Manual OIDC endpoint overrides. If not provided, endpoints are
   * discovered from the server's .well-known/openid-configuration document.
   */
  endpoints?: OidcEndpoints;

  /** Slug of the sign-in journey (default: "signin") */
  signInJourney?: string;

  /** Slug of the sign-up journey (default: "signup") */
  signUpJourney?: string;

  /** Slug of the password-reset journey (default: "password-reset") */
  passwordResetJourney?: string;

  /** Token storage strategy */
  storage?: 'memory' | 'localStorage' | 'sessionStorage';

  /** Public API key for journey endpoints */
  apiKey?: string;

  /** Callback when auth state changes */
  onAuthStateChange?: (user: OlusoUser | null) => void;
}

export interface OlusoUser {
  /** Subject (user ID) */
  sub: string;

  /** Display name */
  name?: string;

  /** Email address */
  email?: string;

  /** Whether email is verified */
  emailVerified?: boolean;

  /** User roles */
  roles: string[];

  /** User permissions */
  permissions: string[];

  /** Tenant ID */
  tenantId?: string;

  /** Raw ID token claims */
  claims: Record<string, unknown>;
}

export interface TokenSet {
  accessToken: string;
  idToken?: string;
  refreshToken?: string;
  expiresAt: number;
  tokenType: string;
  scope?: string;
}

export interface AuthResult {
  success: boolean;
  user?: OlusoUser;
  tokens?: TokenSet;
  error?: string;
  code?: string;
  /** The journey needs another step (e.g. "mfa"): continue it with continueStep(sessionId, ...) */
  requiresStep?: string;
  sessionId?: string;
  /** What to render for that step */
  step?: JourneyStepView;
  /** The step continues at an external identity provider */
  redirectUrl?: string;
}

export interface JourneyStepData {
  /** Current step identifier */
  step: string;
  /** Fields to display for this step */
  fields: JourneyField[];
  /** Step title */
  title?: string;
  /** Step description */
  description?: string;
}

/** Options for signInWithPasskey */
export interface PasskeyOptions {
  /** Username for username-based flow. Omit for discoverable credential (usernameless) flow. */
  username?: string;
  /** Whether to use conditional mediation (passkey autofill in input fields) */
  conditional?: boolean;
  /** Abort signal to cancel the passkey prompt */
  signal?: AbortSignal;
}

/** Options for signInWithFedCM */
export interface FedCmOptions {
  /** Whether to use auto-reauthentication (silent sign-in if user previously approved) */
  autoReauth?: boolean;
  /** Nonce for token binding */
  nonce?: string;
  /** Login hint (email or username) to pre-select an account */
  loginHint?: string;
}

/** WebAuthn assertion options returned by the server */
export interface WebAuthnAssertionOptions {
  assertionId: string;
  challenge: string;
  timeout: number;
  rpId?: string;
  allowCredentials?: Array<{
    type: string;
    id: string;
    transports?: string[];
  }>;
  userVerification: string;
}

/** Options for OlusoAuth.mountSignIn. */
export interface MountSignInOptions {
  /** Slug of the journey to run (default: the configured sign-in journey) */
  journey?: string;
  /** Called with the outcome once the journey completes and the code is redeemed */
  onSignedIn?: (result: AuthResult) => void;
  onError?: (error: Error) => void;
  /** A step continues at an external identity provider; defaults to navigating there */
  onRedirect?: (url: string) => void;
  theme?: 'light' | 'dark' | 'auto';
  unstyled?: boolean;
  labels?: Partial<import('../journey/render').JourneyLabels>;
}

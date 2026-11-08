// Core client
export { OlusoClient, OlusoError } from './client';

// Embed (programmatic API - types only, actual embed is a standalone script)
export type { EmbedConfig } from './embed';

// Journey renderer: draws any journey step by step
export { mountJourney } from './journey/render';
export type { MountJourneyOptions, JourneyMount, JourneyLabels } from './journey/render';
export { conditionMet } from './journey/conditions';

// Types
export type {
  OlusoConfig,
  JourneyInfo,
  JourneyField,
  SubmissionResult,
  JourneySession,
  JourneyStepView,
  JourneyStepAction,
  JourneyStepForm,
  JourneyFormField,
  JourneyFormFieldType,
  JourneyFormOption,
  JourneyFormCountry,
  JourneyFormCondition,
  JourneyFormAction,
  JourneyFormLink,
  JourneyFormWidget,
  StepResult,
  OlusoEvent,
  OlusoEventType,
  OlusoEventListener,
} from './types';

// Auth SDK re-exports
export { OlusoAuth } from './auth/client';
export type {
  OidcEndpoints,
  OlusoAuthConfig,
  OlusoUser,
  TokenSet,
  AuthResult,
  JourneyStepData,
  PasskeyOptions,
  FedCmOptions,
  WebAuthnAssertionOptions,
  MountSignInOptions,
} from './auth/types';
export type { TokenStorage } from './auth/storage';

// Convenience function to create a client
import { OlusoClient } from './client';
import type { OlusoConfig } from './types';

/**
 * Create an Oluso client instance
 *
 * @example
 * ```typescript
 * import { createClient } from '@oluso/sdk';
 *
 * const oluso = createClient({
 *   serverUrl: 'https://auth.yoursite.com',
 *   tenant: 'acme',
 * });
 *
 * // Submit to a waitlist
 * await oluso.submit('waitlist', { email: 'user@example.com' });
 * ```
 */
export function createClient(config: OlusoConfig): OlusoClient {
  return new OlusoClient(config);
}

// Core client
export { OlusoClient, OlusoError } from './client';

// Types
export type {
  OlusoConfig,
  JourneyInfo,
  JourneyField,
  SubmissionResult,
  JourneySession,
  StepResult,
  AuthResult,
  OlusoEvent,
  OlusoEventType,
  OlusoEventListener,
} from './types';

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

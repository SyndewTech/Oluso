import type {
  OlusoConfig,
  JourneyInfo,
  SubmissionResult,
  JourneySession,
  StepResult,
  OlusoEvent,
  OlusoEventListener,
  OlusoEventType,
} from './types';

/**
 * Oluso SDK Client
 *
 * @example
 * ```typescript
 * const oluso = new OlusoClient({
 *   serverUrl: 'https://auth.oluso.io',
 *   tenant: 'acme',
 * });
 *
 * // Get journey info (form fields, etc.)
 * const journey = await oluso.getJourney('waitlist');
 *
 * // Submit data
 * const result = await oluso.submit('waitlist', {
 *   email: 'user@example.com',
 *   name: 'John Doe',
 * });
 * ```
 */
export class OlusoClient {
  private config: Required<Pick<OlusoConfig, 'serverUrl' | 'tenant'>> & OlusoConfig;
  private fetchFn: typeof fetch;
  private listeners: Map<OlusoEventType | '*', Set<OlusoEventListener>> = new Map();

  constructor(config: OlusoConfig) {
    if (!config.serverUrl) {
      throw new Error('serverUrl is required');
    }
    if (!config.tenant) {
      throw new Error('tenant is required');
    }

    this.config = {
      ...config,
      serverUrl: config.serverUrl.replace(/\/$/, ''), // Remove trailing slash
    };
    this.fetchFn = config.fetch || globalThis.fetch.bind(globalThis);
  }

  /**
   * Get journey information including form fields
   */
  async getJourney(policyId: string): Promise<JourneyInfo> {
    const response = await this.request(`/api/public/journeys/${policyId}`);
    return response;
  }

  /**
   * Submit data to a data collection journey (waitlist, contact, survey, etc.)
   */
  async submit(
    policyId: string,
    data: Record<string, unknown>,
    options?: {
      /** Client-side metadata */
      metadata?: Record<string, string>;
      /** Honeypot field for spam detection */
      honeypot?: string;
    }
  ): Promise<SubmissionResult> {
    try {
      const result = await this.request(`/api/public/journeys/${policyId}/submit`, {
        method: 'POST',
        body: {
          data,
          metadata: {
            ...options?.metadata,
            referrer: typeof document !== 'undefined' ? document.referrer : undefined,
            userAgent: typeof navigator !== 'undefined' ? navigator.userAgent : undefined,
          },
          honeypot: options?.honeypot,
        },
      });

      this.emit({
        type: 'submission:success',
        journeyId: policyId,
        data: result,
      });

      return result;
    } catch (error) {
      this.emit({
        type: 'submission:error',
        journeyId: policyId,
        error: error instanceof Error ? error : new Error(String(error)),
      });
      throw error;
    }
  }

  /**
   * Start a multi-step journey session
   */
  async startJourney(policyId: string): Promise<JourneySession> {
    const session = await this.request(`/api/public/journeys/${policyId}/start`, {
      method: 'POST',
    });

    this.emit({
      type: 'journey:started',
      journeyId: policyId,
      data: session,
    });

    return session;
  }

  /**
   * Submit data for the current step in a multi-step journey
   */
  async submitStep(
    sessionId: string,
    data: Record<string, unknown>
  ): Promise<StepResult> {
    const result = await this.request(`/api/public/journeys/sessions/${sessionId}/step`, {
      method: 'POST',
      body: { data },
    });

    this.emit({
      type: result.completed ? 'journey:completed' : 'journey:step',
      journeyId: sessionId,
      data: result,
    });

    return result;
  }

  /**
   * Get current session state
   */
  async getSession(sessionId: string): Promise<JourneySession> {
    return this.request(`/api/public/journeys/sessions/${sessionId}`);
  }

  /**
   * Generate URL for redirect-based journey (for auth flows)
   */
  getJourneyUrl(policyId: string, options?: {
    redirectUri?: string;
    state?: string;
  }): string {
    const params = new URLSearchParams({
      tenant: this.config.tenant,
    });

    if (options?.redirectUri) {
      params.set('redirect_uri', options.redirectUri);
    }
    if (options?.state) {
      params.set('state', options.state);
    }

    return `${this.config.serverUrl}/journey/${policyId}?${params}`;
  }

  /**
   * Generate iframe embed URL
   */
  getEmbedUrl(policyId: string, options?: {
    theme?: 'light' | 'dark';
    hideHeader?: boolean;
  }): string {
    const params = new URLSearchParams({
      tenant: this.config.tenant,
      embed: 'true',
    });

    if (options?.theme) {
      params.set('theme', options.theme);
    }
    if (options?.hideHeader) {
      params.set('hideHeader', 'true');
    }

    return `${this.config.serverUrl}/journey/${policyId}?${params}`;
  }

  /**
   * Subscribe to SDK events
   */
  on(event: OlusoEventType | '*', listener: OlusoEventListener): () => void {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, new Set());
    }
    this.listeners.get(event)!.add(listener);

    // Return unsubscribe function
    return () => {
      this.listeners.get(event)?.delete(listener);
    };
  }

  /**
   * Unsubscribe from SDK events
   */
  off(event: OlusoEventType | '*', listener: OlusoEventListener): void {
    this.listeners.get(event)?.delete(listener);
  }

  private emit(event: OlusoEvent): void {
    // Emit to specific listeners
    this.listeners.get(event.type)?.forEach((listener) => listener(event));
    // Emit to wildcard listeners
    this.listeners.get('*')?.forEach((listener) => listener(event));
  }

  private async request<T>(
    path: string,
    options?: {
      method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
      body?: unknown;
    }
  ): Promise<T> {
    const url = `${this.config.serverUrl}${path}`;
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'X-Tenant-ID': this.config.tenant,
    };

    if (this.config.apiKey) {
      headers['X-API-Key'] = this.config.apiKey;
    }

    const response = await this.fetchFn(url, {
      method: options?.method || 'GET',
      headers,
      body: options?.body ? JSON.stringify(options.body) : undefined,
      credentials: 'include', // Include cookies for session management
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: response.statusText }));
      throw new OlusoError(
        error.message || `Request failed: ${response.status}`,
        response.status,
        error.code,
        error.errors
      );
    }

    return response.json();
  }
}

/**
 * Custom error class for SDK errors
 */
export class OlusoError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
    public readonly fieldErrors?: Record<string, string>
  ) {
    super(message);
    this.name = 'OlusoError';
  }
}

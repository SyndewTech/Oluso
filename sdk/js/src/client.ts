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
  private config: Required<Pick<OlusoConfig, 'serverUrl'>> & OlusoConfig;
  private fetchFn: typeof fetch;
  private listeners: Map<OlusoEventType | '*', Set<OlusoEventListener>> = new Map();

  constructor(config: OlusoConfig) {
    if (!config.serverUrl) {
      throw new Error('serverUrl is required');
    }

    this.config = {
      ...config,
      serverUrl: config.serverUrl.replace(/\/$/, ''), // Remove trailing slash
    };
    this.fetchFn = config.fetch || globalThis.fetch.bind(globalThis);
  }

  /**
   * Get journey information including form fields. Journeys are named by slug.
   */
  async getJourney(slug: string): Promise<JourneyInfo> {
    const response = await this.request<JourneyInfo>(`/api/public/journeys/${encodeURIComponent(slug)}`);
    return response;
  }

  /**
   * Submit data to a data collection journey (waitlist, contact, survey, etc.)
   */
  async submit(
    slug: string,
    data: Record<string, unknown>,
    options?: {
      /** Client-side metadata */
      metadata?: Record<string, string>;
      /** Honeypot field for spam detection */
      honeypot?: string;
    }
  ): Promise<SubmissionResult> {
    try {
      const result = await this.request<SubmissionResult>(`/api/public/journeys/${encodeURIComponent(slug)}/submit`, {
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
        slug,
        data: result,
      });

      return result;
    } catch (error) {
      this.emit({
        type: 'submission:error',
        slug,
        error: error instanceof Error ? error : new Error(String(error)),
      });
      throw error;
    }
  }

  /**
   * Start a multi-step journey and run its first step. Without data the session's `step`
   * is the first step to render; with data, that is the first step's input.
   */
  async startJourney(slug: string, data?: Record<string, unknown>): Promise<JourneySession> {
    const session = await this.request<JourneySession>(
      `/api/public/journeys/${encodeURIComponent(slug)}/start`,
      { method: 'POST', body: { data } }
    );

    this.emit({
      type: 'journey:started',
      slug: session.slug,
      sessionId: session.sessionId,
      data: session,
    });

    return session;
  }

  /**
   * Start a sign-in (or any authentication) journey for a public client. When the session completes it carries an
   * authorization code, redeemed at the token endpoint with the PKCE verifier and redirect_uri "postmessage". Use
   * this when your app keeps its own tokens; OlusoAuth.mountSignIn does the whole round trip for you.
   */
  async startAuthJourney(slug: string, auth: { clientId: string; codeChallenge: string; scope?: string }, data?: Record<string, unknown>): Promise<JourneySession> {
    const session = await this.request<JourneySession>(
      `/api/public/journeys/${encodeURIComponent(slug)}/start`,
      {
        method: 'POST',
        body: { data, clientId: auth.clientId, codeChallenge: auth.codeChallenge, codeChallengeMethod: 'S256', scope: auth.scope },
      }
    );

    this.emit({ type: 'journey:started', slug: session.slug, sessionId: session.sessionId, data: session });
    return session;
  }

  /**
   * Submit input (or an action such as "resend") for the session's current step.
   * The result is the session afterwards: the next step to render, or the completion.
   */
  async submitStep(
    sessionId: string,
    data?: Record<string, unknown>,
    action?: string
  ): Promise<StepResult> {
    const result = await this.request<StepResult>(
      `/api/public/journeys/sessions/${encodeURIComponent(sessionId)}/step`,
      { method: 'POST', body: { data, action } }
    );

    this.emit({
      type: result.completed ? 'journey:completed' : result.success ? 'journey:step' : 'journey:error',
      slug: result.slug,
      sessionId,
      data: result,
    });

    return result;
  }

  /**
   * Submit the current step the way a form posts it: every field, files under their field's name, and the
   * clicked button's name and value. Use this for steps with file fields; `action` is a step action such as "init".
   */
  async submitStepForm(sessionId: string, form: FormData, action?: string): Promise<StepResult> {
    if (action) form.set('__journey_action', action);
    const result = await this.request<StepResult>(
      `/api/public/journeys/sessions/${encodeURIComponent(sessionId)}/step`,
      { method: 'POST', body: form }
    );

    this.emit({
      type: result.completed ? 'journey:completed' : result.success ? 'journey:step' : 'journey:error',
      slug: result.slug,
      sessionId,
      data: result,
    });

    return result;
  }

  /** The server this client talks to */
  get serverUrl(): string {
    return this.config.serverUrl;
  }

  /**
   * Get where a multi-step session stands (status, current step), without running it
   */
  async getSession(sessionId: string): Promise<JourneySession> {
    return this.request<JourneySession>(`/api/public/journeys/sessions/${encodeURIComponent(sessionId)}`);
  }

  /**
   * URL of the hosted journey page (/journey/start/{slug}) to redirect to
   */
  getJourneyUrl(slug: string, options?: {
    redirectUri?: string;
    state?: string;
  }): string {
    const params = new URLSearchParams();
    if (this.config.tenant) params.set('tenant', this.config.tenant);

    if (options?.redirectUri) {
      params.set('redirect_uri', options.redirectUri);
    }
    if (options?.state) {
      params.set('state', options.state);
    }

    return `${this.config.serverUrl}/journey/start/${encodeURIComponent(slug)}?${params}`;
  }

  /**
   * URL of the hosted journey page (/journey/start/{slug}) for an iframe
   */
  getEmbedUrl(slug: string, options?: {
    theme?: 'light' | 'dark';
    hideHeader?: boolean;
  }): string {
    const params = new URLSearchParams({ embed: 'true' });
    if (this.config.tenant) params.set('tenant', this.config.tenant);

    if (options?.theme) {
      params.set('theme', options.theme);
    }
    if (options?.hideHeader) {
      params.set('hideHeader', 'true');
    }

    return `${this.config.serverUrl}/journey/start/${encodeURIComponent(slug)}?${params}`;
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
    const method = options?.method || 'GET';
    const isForm = typeof FormData !== 'undefined' && options?.body instanceof FormData;
    const headers: Record<string, string> = {};
    if (this.config.tenant) {
      headers['X-Tenant-ID'] = this.config.tenant;
    }
    // A form sets its own multipart boundary.
    if (!isForm) {
      headers['Content-Type'] = 'application/json';
    }

    if (this.config.apiKey) {
      headers['X-API-Key'] = this.config.apiKey;
    }

    if (this.config.headers) {
      Object.assign(headers, await this.config.headers({ method, url }));
    }

    const response = await this.fetchFn(url, {
      method,
      headers,
      body: options?.body === undefined ? undefined : isForm ? (options.body as FormData) : JSON.stringify(options.body),
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

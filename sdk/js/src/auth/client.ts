import type { OlusoAuthConfig, OidcEndpoints, OlusoUser, TokenSet, AuthResult, JourneyStepData, PasskeyOptions, FedCmOptions, WebAuthnAssertionOptions, MountSignInOptions } from './types';
import { OlusoClient } from '../client';
import { mountJourney, type JourneyMount } from '../journey/render';
import type { JourneySession } from '../types';
import { createStorage, type TokenStorage } from './storage';

/**
 * Oluso Auth Client
 *
 * Handles authentication via OIDC (redirect), the hosted journey in a modal, or the
 * journey run step by step from your own UI (inline). Inline journeys complete with an
 * authorization code bound to this client's PKCE verifier, redeemed for tokens here.
 *
 * @example
 * ```typescript
 * const auth = new OlusoAuth({
 *   serverUrl: 'https://auth.yoursite.com',
 *   tenant: 'acme',
 *   clientId: 'my-spa-client',
 * });
 *
 * // Inline auth (no redirect)
 * const result = await auth.signIn({ username: 'user@example.com', password: '...' });
 * if (result.success) {
 *   console.log('Logged in as', result.user.name);
 * }
 *
 * // Check roles
 * if (auth.hasRole('admin')) { ... }
 * ```
 */
export class OlusoAuth {
  private config: Required<Pick<OlusoAuthConfig, 'serverUrl' | 'tenant' | 'clientId'>> & OlusoAuthConfig;
  private storage: TokenStorage;
  private user: OlusoUser | null = null;
  private resolvedEndpoints: OidcEndpoints | null = null;
  private discoveryPromise: Promise<OidcEndpoints> | null = null;
  /** PKCE verifiers of inline journeys still in progress, by session */
  private readonly journeyVerifiers = new Map<string, string>();

  constructor(config: OlusoAuthConfig) {
    if (!config.serverUrl || !config.tenant || !config.clientId) {
      throw new Error('serverUrl, tenant, and clientId are required');
    }

    this.config = {
      scopes: 'openid profile email',
      signInJourney: 'signin',
      signUpJourney: 'signup',
      passwordResetJourney: 'password-reset',
      storage: 'memory',
      ...config,
      serverUrl: config.serverUrl.replace(/\/$/, ''),
    };

    this.storage = createStorage(this.config.storage!);

    // Restore user from stored tokens
    const tokens = this.storage.getTokens();
    if (tokens) {
      this.user = this.parseIdToken(tokens.idToken);
    }
  }

  // =========================================================================
  // Journey Auth (inline or modal)
  // =========================================================================

  /**
   * Sign in.
   * - With data: run the sign-in journey inline with these inputs (programmatic, first-party
   *   only). If another step follows (e.g. MFA), the result has requiresStep, sessionId and
   *   the step to render; continue with continueStep().
   * - Without data: open the hosted journey in a secure iframe modal (safe for any site)
   */
  async signIn(data?: Record<string, unknown>): Promise<AuthResult> {
    if (data) {
      return this.executeJourney(this.config.signInJourney!, data);
    }
    return this.openAuthModal('signin');
  }

  /**
   * Sign up.
   * - With data: run the sign-up journey inline (programmatic, first-party only)
   * - Without data: open the hosted journey in a secure iframe modal
   */
  async signUp(data?: Record<string, unknown>): Promise<AuthResult> {
    if (data) {
      return this.executeJourney(this.config.signUpJourney!, data);
    }
    return this.openAuthModal('signup');
  }

  /**
   * Sign in with a passkey (WebAuthn).
   * No form or iframe needed — the browser shows a native biometric/key prompt.
   * Works with discoverable credentials (usernameless) or username-based flow.
   *
   * @example
   * ```typescript
   * // Discoverable credential (passkey autofill or prompt)
   * const result = await auth.signInWithPasskey();
   *
   * // Username-based (shows credentials for specific user)
   * const result = await auth.signInWithPasskey({ username: 'user@example.com' });
   *
   * // Conditional mediation (passkey suggestions in input fields)
   * const result = await auth.signInWithPasskey({ conditional: true });
   * ```
   */
  async signInWithPasskey(options?: PasskeyOptions): Promise<AuthResult> {
    if (!window.PublicKeyCredential) {
      return { success: false, error: 'WebAuthn is not supported in this browser' };
    }

    const { verifier, challenge } = await this.generatePkce();

    // 1. Get assertion options from server. The sign-in request (scope, PKCE challenge) goes along so the
    //    server can refuse it before the user is prompted for a passkey.
    const optionsUrl = `${this.config.serverUrl}/connect/webauthn/assertion-options`;
    const optionsResponse = await fetch(optionsUrl, {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        clientId: this.config.clientId,
        username: options?.username,
        scope: this.config.scopes,
        codeChallenge: challenge,
        codeChallengeMethod: 'S256',
      }),
    });

    if (!optionsResponse.ok) {
      const err = await optionsResponse.json().catch(() => ({ error_description: 'Failed to get passkey options' }));
      return { success: false, error: err.error_description || err.error };
    }

    const assertionOptions: WebAuthnAssertionOptions = await optionsResponse.json();

    // 2. Call navigator.credentials.get() with the options
    const publicKeyOptions: PublicKeyCredentialRequestOptions = {
      challenge: this.base64UrlDecode(assertionOptions.challenge),
      timeout: assertionOptions.timeout,
      rpId: assertionOptions.rpId,
      userVerification: (assertionOptions.userVerification || 'preferred') as UserVerificationRequirement,
      allowCredentials: assertionOptions.allowCredentials?.map(c => ({
        type: c.type as PublicKeyCredentialType,
        id: this.base64UrlDecode(c.id),
        transports: c.transports as AuthenticatorTransport[] | undefined,
      })),
    };

    let credential: PublicKeyCredential | null;
    try {
      const getOptions: CredentialRequestOptions = {
        publicKey: publicKeyOptions,
        signal: options?.signal,
      };

      // Conditional mediation (passkey autofill)
      if (options?.conditional) {
        (getOptions as Record<string, unknown>).mediation = 'conditional';
      }

      credential = await navigator.credentials.get(getOptions) as PublicKeyCredential | null;
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Passkey request failed';
      if (err instanceof DOMException && err.name === 'AbortError') {
        return { success: false, error: 'cancelled' };
      }
      if (err instanceof DOMException && err.name === 'NotAllowedError') {
        return { success: false, error: 'cancelled' };
      }
      return { success: false, error: message };
    }

    if (!credential) {
      return { success: false, error: 'No credential returned' };
    }

    // 3. Send assertion response to server
    const assertionResponse = credential.response as AuthenticatorAssertionResponse;
    const responsePayload = JSON.stringify({
      id: credential.id,
      rawId: this.base64UrlEncode(new Uint8Array(credential.rawId)),
      type: credential.type,
      response: {
        clientDataJSON: this.base64UrlEncode(new Uint8Array(assertionResponse.clientDataJSON)),
        authenticatorData: this.base64UrlEncode(new Uint8Array(assertionResponse.authenticatorData)),
        signature: this.base64UrlEncode(new Uint8Array(assertionResponse.signature)),
        userHandle: assertionResponse.userHandle
          ? this.base64UrlEncode(new Uint8Array(assertionResponse.userHandle))
          : null,
      },
    });

    const verifyUrl = `${this.config.serverUrl}/connect/webauthn/assertion`;
    const verifyResponse = await fetch(verifyUrl, {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        clientId: this.config.clientId,
        assertionId: assertionOptions.assertionId,
        assertionResponse: responsePayload,
        codeChallenge: challenge,
        codeChallengeMethod: 'S256',
        scope: this.config.scopes,
      }),
    });

    if (!verifyResponse.ok) {
      const err = await verifyResponse.json().catch(() => ({ error_description: 'Passkey verification failed' }));
      return { success: false, error: err.error_description || err.error };
    }

    const { code } = await verifyResponse.json();

    // 4. Exchange auth code for tokens
    return this.exchangeCodeForTokens(code, verifier, 'postmessage');
  }

  /**
   * Sign in with FedCM (Federated Credential Management).
   * Opens a browser-native account picker — no redirect or popup needed.
   * Requires the user to be already authenticated on the Oluso server (has session cookie).
   *
   * Browser support: Chrome/Edge 132+ (the provider `params` that carry the PKCE challenge).
   * The page origin must be registered for the client (CORS origin or redirect URI).
   *
   * @example
   * ```typescript
   * const result = await auth.signInWithFedCM();
   * if (result.success) {
   *   console.log('Signed in as', result.user.name);
   * }
   * ```
   */
  async signInWithFedCM(options?: FedCmOptions): Promise<AuthResult> {
    // Check FedCM support
    if (!('IdentityCredential' in window)) {
      return { success: false, error: 'FedCM is not supported in this browser' };
    }

    // PKCE: the challenge (and scope) reach the ID assertion endpoint through the provider's `params`,
    // which bind the code to this page; the verifier redeems it.
    const { verifier, challenge } = await this.generatePkce();

    try {
      const credential = await navigator.credentials.get({
        identity: {
          providers: [{
            configURL: `${this.config.serverUrl}/fedcm/config.json`,
            clientId: this.config.clientId,
            nonce: options?.nonce,
            loginHint: options?.loginHint,
            params: {
              code_challenge: challenge,
              code_challenge_method: 'S256',
              scope: this.config.scopes,
            },
          }],
        },
        mediation: options?.autoReauth ? 'optional' : 'required',
      } as CredentialRequestOptions) as { token?: string } | null;

      if (!credential?.token) {
        return { success: false, error: 'No credential returned from FedCM' };
      }

      // The FedCM token is an auth code — exchange it for real tokens
      return this.exchangeCodeForTokens(credential.token, verifier, 'fedcm');
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        return { success: false, error: 'cancelled' };
      }
      if (err instanceof DOMException && err.name === 'NetworkError') {
        return { success: false, error: 'FedCM network error — user may not be signed in to the identity provider' };
      }
      const message = err instanceof Error ? err.message : 'FedCM request failed';
      return { success: false, error: message };
    }
  }

  /**
   * Render the sign-in journey into `container` and sign in without leaving the page, e.g. in a modal when the
   * session has expired. Every step the journey has (password, MFA, passkey, consent) is shown; when it completes,
   * the code is redeemed and `onSignedIn` gets the result.
   */
  mountSignIn(container: HTMLElement, options: MountSignInOptions = {}): JourneyMount {
    const client = new OlusoClient({ serverUrl: this.config.serverUrl, tenant: this.config.tenant, apiKey: this.config.apiKey });
    let verifier = '';
    return mountJourney(container, {
      client,
      theme: options.theme,
      unstyled: options.unstyled,
      labels: options.labels,
      showCompletion: false,
      onError: options.onError,
      onRedirect: options.onRedirect,
      start: async () => {
        const pkce = await this.generatePkce();
        verifier = pkce.verifier;
        return client.startAuthJourney(
          options.journey ?? this.config.signInJourney!,
          { clientId: this.config.clientId, codeChallenge: pkce.challenge, scope: this.config.scopes });
      },
      onComplete: async (session) => {
        const result = session.code
          ? await this.exchangeCodeForTokens(session.code, verifier, 'postmessage')
          : { success: false, error: session.errorDescription || session.error || 'Authentication failed', code: session.error };
        options.onSignedIn?.(result);
      },
    });
  }

  /**
   * Continue an inline journey with the input for its current step (e.g. an MFA code)
   */
  async continueStep(sessionId: string, data: Record<string, unknown>): Promise<AuthResult> {
    const verifier = this.journeyVerifiers.get(sessionId);
    if (!verifier) {
      return { success: false, error: 'This journey was not started by this client', code: 'invalid_session' };
    }

    const url = `${this.config.serverUrl}/api/public/journeys/sessions/${encodeURIComponent(sessionId)}/step`;
    const response = await fetch(url, {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({ data }),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Step failed' }));
      return { success: false, error: error.error_description || error.message, code: error.error || error.code };
    }

    return this.handleJourneyResult(await response.json(), verifier);
  }

  /**
   * Get the fields/config of a data-collection journey (for rendering in UI)
   */
  async getJourneyStep(journeySlug: string): Promise<JourneyStepData | null> {
    const url = `${this.config.serverUrl}/api/public/journeys/${encodeURIComponent(journeySlug)}`;

    const response = await fetch(url, {
      headers: this.buildHeaders(),
      credentials: 'include',
    });

    if (!response.ok) return null;

    const journey = await response.json();
    return {
      step: 'initial',
      fields: journey.fields || [],
      title: journey.ui?.title || journey.name,
      description: journey.description,
    };
  }

  // =========================================================================
  // OIDC Redirect Flow
  // =========================================================================

  /**
   * Redirect to the Oluso login page (standard OIDC Authorization Code + PKCE)
   */
  async loginWithRedirect(options?: { prompt?: 'login' | 'consent' | 'none'; state?: string }): Promise<void> {
    const { verifier, challenge } = await this.generatePkce();

    // Store verifier for callback
    sessionStorage.setItem('oluso_pkce_verifier', verifier);
    if (options?.state) {
      sessionStorage.setItem('oluso_auth_state', options.state);
    }

    const endpoints = await this.getEndpoints();

    const params = new URLSearchParams({
      response_type: 'code',
      client_id: this.config.clientId,
      redirect_uri: this.config.redirectUri || window.location.origin + '/callback',
      scope: this.config.scopes!,
      code_challenge: challenge,
      code_challenge_method: 'S256',
      tenant: this.config.tenant,
    });

    if (options?.prompt) params.set('prompt', options.prompt);
    if (options?.state) params.set('state', options.state);

    window.location.href = `${endpoints.authorization}?${params}`;
  }

  /**
   * Handle the OIDC callback (exchange code for tokens)
   */
  async handleCallback(callbackUrl?: string): Promise<AuthResult> {
    const url = new URL(callbackUrl || window.location.href);
    const code = url.searchParams.get('code');
    const error = url.searchParams.get('error');

    if (error) {
      return { success: false, error: url.searchParams.get('error_description') || error, code: error };
    }

    if (!code) {
      return { success: false, error: 'No authorization code in callback' };
    }

    const verifier = sessionStorage.getItem('oluso_pkce_verifier');
    sessionStorage.removeItem('oluso_pkce_verifier');

    if (!verifier) {
      return { success: false, error: 'PKCE verifier not found' };
    }

    return this.exchangeCodeForTokens(code, verifier, this.config.redirectUri || window.location.origin + '/callback');
  }

  // =========================================================================
  // Token & User Management
  // =========================================================================

  /**
   * Get the current user (null if not authenticated)
   */
  getUser(): OlusoUser | null {
    return this.user;
  }

  /**
   * Check if user is authenticated (has valid tokens)
   */
  isAuthenticated(): boolean {
    return this.user !== null && this.storage.getTokens() !== null;
  }

  /**
   * Get the current access token (for API calls)
   */
  getAccessToken(): string | null {
    return this.storage.getTokens()?.accessToken ?? null;
  }

  /**
   * Check if user has a specific role
   */
  hasRole(role: string): boolean {
    return this.user?.roles.includes(role) ?? false;
  }

  /**
   * Check if user has any of the specified roles
   */
  hasAnyRole(...roles: string[]): boolean {
    return roles.some(r => this.hasRole(r));
  }

  /**
   * Check if user has a specific permission
   */
  hasPermission(permission: string): boolean {
    return this.user?.permissions.includes(permission) ?? false;
  }

  /**
   * Refresh the access token using the refresh token
   */
  async refreshToken(): Promise<boolean> {
    const tokens = this.storage.getTokens();
    if (!tokens?.refreshToken) return false;

    const endpoints = await this.getEndpoints();
    const response = await fetch(endpoints.token!, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'refresh_token',
        client_id: this.config.clientId,
        refresh_token: tokens.refreshToken,
      }),
    });

    if (!response.ok) {
      this.logout();
      return false;
    }

    const tokenData = await response.json();
    this.processTokenResponse(tokenData);
    return true;
  }

  /**
   * Log out — clear tokens and optionally redirect to end session.
   * If redirect is true, uses discovered or configured end_session_endpoint.
   */
  logout(options?: { redirect?: boolean; postLogoutRedirectUri?: string }): void {
    const tokens = this.storage.getTokens();
    this.storage.clear();
    this.user = null;
    this.config.onAuthStateChange?.(null);

    if (options?.redirect && tokens?.idToken) {
      // If we have a resolved endpoint, redirect immediately; otherwise discover first
      if (this.resolvedEndpoints?.endSession || this.config.endpoints?.endSession) {
        const params = new URLSearchParams({
          id_token_hint: tokens.idToken,
          post_logout_redirect_uri: options.postLogoutRedirectUri || window.location.origin,
        });
        window.location.href = `${this.resolvedEndpoints?.endSession || this.config.endpoints?.endSession}?${params}`;
      } else {
        // Fallback: discover then redirect
        this.getEndpoints().then(endpoints => {
          const params = new URLSearchParams({
            id_token_hint: tokens.idToken!,
            post_logout_redirect_uri: options!.postLogoutRedirectUri || window.location.origin,
          });
          window.location.href = `${endpoints.endSession}?${params}`;
        });
      }
    }
  }

  /**
   * Fetch the user's profile from the userinfo endpoint
   */
  async fetchUserInfo(): Promise<OlusoUser | null> {
    const token = this.getAccessToken();
    if (!token) return null;

    const endpoints = await this.getEndpoints();
    const response = await fetch(endpoints.userinfo!, {
      headers: { Authorization: `Bearer ${token}` },
    });

    if (!response.ok) return null;

    const claims = await response.json();
    this.user = this.claimsToUser(claims);
    this.config.onAuthStateChange?.(this.user);
    return this.user;
  }

  // =========================================================================
  // Private Helpers
  // =========================================================================

  /**
   * Opens a secure iframe modal for authentication.
   * Credentials are entered inside the iframe (auth server origin).
   * Returns tokens via postMessage + PKCE code exchange.
   */
  private async openAuthModal(mode: 'signin' | 'signup'): Promise<AuthResult> {
    const { verifier, challenge } = await this.generatePkce();

    const journeySlug = mode === 'signin'
      ? this.config.signInJourney!
      : this.config.signUpJourney!;

    const params = new URLSearchParams({
      journey: journeySlug,
      client_id: this.config.clientId,
      origin: window.location.origin,
      code_challenge: challenge,
      code_challenge_method: 'S256',
      scope: this.config.scopes!,
    });

    const iframeSrc = `${this.config.serverUrl}/embed/auth?${params}`;

    return new Promise<AuthResult>((resolve) => {
      // Create overlay + modal
      const overlay = document.createElement('div');
      overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.6);backdrop-filter:blur(4px);z-index:99999;display:flex;align-items:center;justify-content:center;padding:24px;';

      const modal = document.createElement('div');
      modal.style.cssText = 'background:#fff;border-radius:16px;width:100%;max-width:420px;max-height:90vh;overflow:hidden;box-shadow:0 25px 50px -12px rgba(0,0,0,0.25);';

      const header = document.createElement('div');
      header.style.cssText = 'display:flex;justify-content:flex-end;padding:12px 16px 0;';
      const closeBtn = document.createElement('button');
      closeBtn.innerHTML = '&times;';
      closeBtn.style.cssText = 'background:none;border:none;cursor:pointer;color:#9ca3af;font-size:20px;padding:4px;border-radius:6px;';
      header.appendChild(closeBtn);

      const iframe = document.createElement('iframe');
      iframe.src = iframeSrc;
      // Journey steps may capture documents with the camera (e.g. business verification).
      iframe.allow = 'camera; microphone';
      iframe.style.cssText = 'border:none;width:100%;min-height:300px;';

      const branding = document.createElement('div');
      branding.style.cssText = 'display:none;text-align:center;padding:10px 0;font-size:11px;color:#9ca3af;border-top:1px solid #e5e7eb;font-family:-apple-system,BlinkMacSystemFont,sans-serif;';
      branding.innerHTML = 'Powered by <a href="https://oluso.io" target="_blank" rel="noopener" style="color:#9ca3af;text-decoration:none;">Oluso</a>';

      modal.appendChild(header);
      modal.appendChild(iframe);
      modal.appendChild(branding);
      overlay.appendChild(modal);
      document.body.appendChild(overlay);
      document.body.style.overflow = 'hidden';

      const cleanup = () => {
        window.removeEventListener('message', messageHandler);
        document.removeEventListener('keydown', escHandler);
        document.body.style.overflow = '';
        overlay.remove();
      };

      const cancel = () => {
        cleanup();
        resolve({ success: false, error: 'cancelled' });
      };

      closeBtn.addEventListener('click', cancel);
      overlay.addEventListener('click', (e) => { if (e.target === overlay) cancel(); });

      const escHandler = (e: KeyboardEvent) => { if (e.key === 'Escape') cancel(); };
      document.addEventListener('keydown', escHandler);

      const messageHandler = async (event: MessageEvent) => {
        const serverOrigin = new URL(this.config.serverUrl).origin;
        if (event.origin !== serverOrigin) return;

        const data = event.data;
        if (!data || typeof data !== 'object') return;

        if (data.type === 'oluso:auth:code') {
          cleanup();
          const result = await this.exchangeCodeForTokens(data.code, verifier, 'postmessage');
          resolve(result);
        } else if (data.type === 'oluso:auth:error') {
          cleanup();
          resolve({ success: false, error: data.error });
        } else if (data.type === 'oluso:auth:ready') {
          branding.style.display = data.hideBranding ? 'none' : 'block';
        } else if (data.type === 'oluso:auth:resize' && typeof data.height === 'number') {
          iframe.style.height = `${data.height}px`;
        }
      };

      window.addEventListener('message', messageHandler);
    });
  }

  /**
   * Get OIDC endpoints — uses manual overrides, falls back to discovery.
   */
  private async getEndpoints(): Promise<Required<OidcEndpoints>> {
    // Manual overrides take priority
    if (this.config.endpoints?.authorization && this.config.endpoints?.token) {
      const base = this.config.serverUrl;
      return {
        authorization: this.config.endpoints.authorization,
        token: this.config.endpoints.token,
        userinfo: this.config.endpoints.userinfo || `${base}/connect/userinfo`,
        endSession: this.config.endpoints.endSession || `${base}/connect/endsession`,
        jwks: this.config.endpoints.jwks || `${base}/.well-known/openid-configuration/jwks`,
      };
    }

    // Use cached discovery
    if (this.resolvedEndpoints) {
      return this.resolvedEndpoints as Required<OidcEndpoints>;
    }

    // Discover from well-known
    if (!this.discoveryPromise) {
      this.discoveryPromise = this.discoverEndpoints();
    }

    this.resolvedEndpoints = await this.discoveryPromise;
    return this.resolvedEndpoints as Required<OidcEndpoints>;
  }

  private async discoverEndpoints(): Promise<OidcEndpoints> {
    const base = this.config.serverUrl;
    const wellKnownUrl = `${base}/.well-known/openid-configuration`;

    try {
      const response = await fetch(wellKnownUrl);
      if (!response.ok) {
        return this.defaultEndpoints();
      }

      const doc = await response.json();
      return {
        authorization: doc.authorization_endpoint || `${base}/connect/authorize`,
        token: doc.token_endpoint || `${base}/connect/token`,
        userinfo: doc.userinfo_endpoint || `${base}/connect/userinfo`,
        endSession: doc.end_session_endpoint || `${base}/connect/endsession`,
        jwks: doc.jwks_uri || `${base}/.well-known/openid-configuration/jwks`,
      };
    } catch {
      return this.defaultEndpoints();
    }
  }

  private defaultEndpoints(): OidcEndpoints {
    const base = this.config.serverUrl;
    return {
      authorization: `${base}/connect/authorize`,
      token: `${base}/connect/token`,
      userinfo: `${base}/connect/userinfo`,
      endSession: `${base}/connect/endsession`,
      jwks: `${base}/.well-known/openid-configuration/jwks`,
    };
  }

  private async executeJourney(journeySlug: string, data: Record<string, unknown>): Promise<AuthResult> {
    const { verifier, challenge } = await this.generatePkce();
    const url = `${this.config.serverUrl}/api/public/journeys/${encodeURIComponent(journeySlug)}/start`;

    const response = await fetch(url, {
      method: 'POST',
      headers: this.buildHeaders(),
      body: JSON.stringify({
        data,
        clientId: this.config.clientId,
        codeChallenge: challenge,
        codeChallengeMethod: 'S256',
        scope: this.config.scopes,
      }),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Auth failed' }));
      return { success: false, error: error.error_description || error.message, code: error.error || error.code };
    }

    return this.handleJourneyResult(await response.json(), verifier);
  }

  /**
   * Completed: redeem the code with the journey's verifier. In progress: hand back the step
   * to render (an error from the step's last input, e.g. a wrong password, rides along).
   */
  private async handleJourneyResult(session: JourneySession, verifier: string): Promise<AuthResult> {
    if (session.completed && session.code) {
      this.journeyVerifiers.delete(session.sessionId);
      return this.exchangeCodeForTokens(session.code, verifier, 'postmessage');
    }

    if (session.status === 'in_progress') {
      this.journeyVerifiers.set(session.sessionId, verifier);
      const stepError = session.step?.model?.errorMessage ?? session.step?.model?.formError;
      return {
        success: false,
        requiresStep: session.step?.type ?? session.currentStep,
        sessionId: session.sessionId,
        step: session.step,
        redirectUrl: session.redirectUrl,
        error: typeof stepError === 'string' ? stepError : undefined,
      };
    }

    this.journeyVerifiers.delete(session.sessionId);
    return {
      success: false,
      error: session.errorDescription || session.error || 'Authentication failed',
      code: session.error,
    };
  }

  private processTokenResponse(tokenData: Record<string, unknown>): AuthResult {
    const tokens: TokenSet = {
      accessToken: tokenData.access_token as string,
      idToken: tokenData.id_token as string | undefined,
      refreshToken: tokenData.refresh_token as string | undefined,
      expiresAt: Math.floor(Date.now() / 1000) + (tokenData.expires_in as number || 3600),
      tokenType: (tokenData.token_type as string) || 'Bearer',
      scope: tokenData.scope as string | undefined,
    };

    this.storage.setTokens(tokens);
    this.user = this.parseIdToken(tokens.idToken);
    this.config.onAuthStateChange?.(this.user);

    return { success: true, user: this.user!, tokens };
  }

  private parseIdToken(idToken?: string): OlusoUser | null {
    if (!idToken) return null;

    try {
      const parts = idToken.split('.');
      if (parts.length !== 3) return null;

      const payload = JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')));
      return this.claimsToUser(payload);
    } catch {
      return null;
    }
  }

  private claimsToUser(claims: Record<string, unknown>): OlusoUser {
    return {
      sub: claims.sub as string,
      name: (claims.name || claims.preferred_username) as string | undefined,
      email: claims.email as string | undefined,
      emailVerified: claims.email_verified as boolean | undefined,
      roles: this.parseArrayClaim(claims.role || claims.roles),
      permissions: this.parseArrayClaim(claims.permission || claims.permissions),
      tenantId: claims.tenant_id as string | undefined,
      claims,
    };
  }

  private parseArrayClaim(claim: unknown): string[] {
    if (!claim) return [];
    if (Array.isArray(claim)) return claim.map(String);
    if (typeof claim === 'string') return [claim];
    return [];
  }

  private buildHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'X-Tenant-ID': this.config.tenant,
    };
    if (this.config.apiKey) {
      headers['X-API-Key'] = this.config.apiKey;
    }
    return headers;
  }

  private async generatePkce(): Promise<{ verifier: string; challenge: string }> {
    const array = new Uint8Array(32);
    crypto.getRandomValues(array);
    const verifier = this.base64UrlEncode(array);

    const encoder = new TextEncoder();
    const digest = await crypto.subtle.digest('SHA-256', encoder.encode(verifier));
    const challenge = this.base64UrlEncode(new Uint8Array(digest));

    return { verifier, challenge };
  }

  private base64UrlEncode(buffer: Uint8Array): string {
    let binary = '';
    for (let i = 0; i < buffer.length; i++) {
      binary += String.fromCharCode(buffer[i]);
    }
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }

  private base64UrlDecode(str: string): ArrayBuffer {
    const base64 = str.replace(/-/g, '+').replace(/_/g, '/');
    const padding = '='.repeat((4 - base64.length % 4) % 4);
    const binary = atob(base64 + padding);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
  }

  /**
   * Exchange an authorization code for tokens via the token endpoint.
   * Used by passkey, FedCM, and modal auth flows.
   */
  private async exchangeCodeForTokens(code: string, verifier: string, redirectUri: string): Promise<AuthResult> {
    const endpoints = await this.getEndpoints();
    const response = await fetch(endpoints.token!, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        client_id: this.config.clientId,
        code,
        redirect_uri: redirectUri,
        code_verifier: verifier,
      }),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({ error_description: 'Token exchange failed' }));
      return { success: false, error: err.error_description || err.error };
    }

    const tokenData = await response.json();
    return this.processTokenResponse(tokenData);
  }
}

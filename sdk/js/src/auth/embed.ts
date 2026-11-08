/**
 * Oluso Auth Embed Script (Iframe-based, secure)
 *
 * Drop-in script for adding authentication modals to any site.
 * Credentials are entered inside a secure iframe on the auth server's origin.
 * Only tokens are returned to the host page via postMessage.
 *
 * Usage:
 *   <script src="https://cdn.oluso.io/sdk/auth-embed.js"></script>
 *
 *   <button data-oluso-auth="signin">Sign In</button>
 *   <button data-oluso-auth="signup">Sign Up</button>
 *
 *   <script>
 *     OlusoAuth.init({
 *       serverUrl: 'https://auth.yoursite.com',
 *       tenant: 'acme',
 *       clientId: 'my-spa',
 *       storage: 'localStorage',
 *     });
 *
 *     OlusoAuth.onAuth((user) => {
 *       console.log('Logged in:', user.name);
 *     });
 *   </script>
 */

import { OlusoAuth } from './client';
import type { OlusoAuthConfig, OlusoUser, AuthResult, PasskeyOptions, FedCmOptions } from './types';

// ============================================================================
// Styles
// ============================================================================

const EMBED_STYLES = `
.oluso-auth-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  -webkit-backdrop-filter: blur(4px);
  z-index: 99999;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  opacity: 0;
  visibility: hidden;
  transition: opacity 0.25s ease, visibility 0.25s ease;
}
.oluso-auth-overlay.oluso-active {
  opacity: 1;
  visibility: visible;
}
.oluso-auth-modal {
  background: #ffffff;
  border-radius: 16px;
  width: 100%;
  max-width: 420px;
  max-height: 90vh;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
  transform: translateY(20px) scale(0.95);
  transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1);
}
.oluso-active .oluso-auth-modal {
  transform: translateY(0) scale(1);
}
.oluso-auth-header {
  display: flex;
  justify-content: flex-end;
  padding: 12px 16px 0;
}
.oluso-auth-close {
  background: none;
  border: none;
  cursor: pointer;
  color: #9ca3af;
  padding: 4px;
  border-radius: 6px;
  line-height: 1;
  font-size: 20px;
  transition: color 0.15s, background 0.15s;
}
.oluso-auth-close:hover {
  color: #374151;
  background: #f3f4f6;
}
.oluso-auth-iframe {
  border: none;
  width: 100%;
  min-height: 300px;
  transition: height 0.2s ease;
}
.oluso-auth-branding {
  text-align: center;
  padding: 10px 0;
  font-size: 11px;
  color: #9ca3af;
  border-top: 1px solid #e5e7eb;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}
.oluso-auth-branding a {
  color: #9ca3af;
  text-decoration: none;
}
.oluso-auth-branding a:hover {
  color: #6b7280;
}
@media (max-width: 480px) {
  .oluso-auth-modal {
    max-width: 100%;
    max-height: 100vh;
    height: 100%;
    border-radius: 0;
  }
  .oluso-auth-overlay {
    padding: 0;
  }
}
@media (prefers-color-scheme: dark) {
  .oluso-auth-modal {
    background: #1f2937;
  }
  .oluso-auth-close:hover {
    color: #e5e7eb;
    background: #374151;
  }
  .oluso-auth-branding {
    border-top-color: #374151;
    color: #6b7280;
  }
}
`;

// ============================================================================
// State
// ============================================================================

let authClient: OlusoAuth | null = null;
let embedConfig: OlusoAuthConfig | null = null;
let overlayEl: HTMLElement | null = null;
let styleEl: HTMLStyleElement | null = null;
let iframeEl: HTMLIFrameElement | null = null;
let brandingEl: HTMLElement | null = null;
let authListeners: Array<(user: OlusoUser | null) => void> = [];
let pendingResolve: ((result: AuthResult) => void) | null = null;
let currentCodeVerifier: string | null = null;

// ============================================================================
// Core
// ============================================================================

function injectStyles(): void {
  if (styleEl) return;
  styleEl = document.createElement('style');
  styleEl.id = 'oluso-auth-embed-styles';
  styleEl.textContent = EMBED_STYLES;
  document.head.appendChild(styleEl);
}

function generatePkce(): Promise<{ verifier: string; challenge: string }> {
  const array = new Uint8Array(32);
  crypto.getRandomValues(array);
  const verifier = base64UrlEncode(array);

  const encoder = new TextEncoder();
  return crypto.subtle.digest('SHA-256', encoder.encode(verifier)).then(digest => {
    const challenge = base64UrlEncode(new Uint8Array(digest));
    return { verifier, challenge };
  });
}

function base64UrlEncode(buffer: Uint8Array): string {
  let binary = '';
  for (let i = 0; i < buffer.length; i++) {
    binary += String.fromCharCode(buffer[i]);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

async function exchangeCodeForTokens(code: string): Promise<AuthResult> {
  if (!authClient || !embedConfig || !currentCodeVerifier) {
    return { success: false, error: 'Not initialized' };
  }

  const endpoints = await (authClient as any).getEndpoints();
  const tokenUrl = endpoints?.token || `${embedConfig.serverUrl}/connect/token`;

  const response = await fetch(tokenUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: embedConfig.clientId,
      code,
      redirect_uri: 'postmessage',
      code_verifier: currentCodeVerifier,
    }),
  });

  currentCodeVerifier = null;

  if (!response.ok) {
    const err = await response.json().catch(() => ({ error_description: 'Token exchange failed' }));
    return { success: false, error: err.error_description || err.error };
  }

  const tokenData = await response.json();
  // Use the client's processTokenResponse (stores tokens, parses user)
  return (authClient as any).processTokenResponse(tokenData);
}

function handleMessage(event: MessageEvent): void {
  if (!embedConfig) return;

  // Validate origin — must come from our auth server
  const serverOrigin = new URL(embedConfig.serverUrl).origin;
  if (event.origin !== serverOrigin) return;

  const data = event.data;
  if (!data || typeof data !== 'object') return;

  switch (data.type) {
    case 'oluso:auth:code':
      // Exchange code for tokens
      exchangeCodeForTokens(data.code).then(result => {
        close();
        if (result.success && result.user) {
          authListeners.forEach(cb => cb(result.user!));
        }
        pendingResolve?.(result);
        pendingResolve = null;
      });
      break;

    case 'oluso:auth:error':
      close();
      const errorResult: AuthResult = { success: false, error: data.error };
      pendingResolve?.(errorResult);
      pendingResolve = null;
      break;

    case 'oluso:auth:ready':
      if (brandingEl) {
        brandingEl.style.display = data.hideBranding ? 'none' : 'block';
      }
      break;

    case 'oluso:auth:resize':
      if (iframeEl && typeof data.height === 'number') {
        iframeEl.style.height = `${data.height}px`;
      }
      break;
  }
}

async function openModal(mode: 'signin' | 'signup'): Promise<AuthResult> {
  if (!authClient || !embedConfig) {
    console.error('[OlusoAuth] Not initialized. Call OlusoAuth.init() first.');
    return { success: false, error: 'Not initialized' };
  }

  injectStyles();

  // Generate PKCE
  const { verifier, challenge } = await generatePkce();
  currentCodeVerifier = verifier;

  // Build iframe URL
  const journeySlug = mode === 'signin'
    ? (embedConfig.signInJourney || 'signin')
    : (embedConfig.signUpJourney || 'signup');

  const params = new URLSearchParams({
    journey: journeySlug,
    client_id: embedConfig.clientId,
    origin: window.location.origin,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    scope: embedConfig.scopes || 'openid profile email',
  });

  const iframeSrc = `${embedConfig.serverUrl}/embed/auth?${params}`;

  // Create modal
  if (overlayEl) overlayEl.remove();

  overlayEl = document.createElement('div');
  overlayEl.className = 'oluso-auth-overlay';
  overlayEl.innerHTML = `
    <div class="oluso-auth-modal">
      <div class="oluso-auth-header">
        <button class="oluso-auth-close" aria-label="Close">&times;</button>
      </div>
      <iframe class="oluso-auth-iframe" src="${iframeSrc}" allow="camera; microphone"></iframe>
      <div class="oluso-auth-branding" style="display:none">Powered by <a href="https://oluso.io" target="_blank" rel="noopener">Oluso</a></div>
    </div>
  `;

  document.body.appendChild(overlayEl);
  iframeEl = overlayEl.querySelector('.oluso-auth-iframe') as HTMLIFrameElement;
  brandingEl = overlayEl.querySelector('.oluso-auth-branding') as HTMLElement;

  // Close button
  const closeBtn = overlayEl.querySelector('.oluso-auth-close');
  closeBtn?.addEventListener('click', () => {
    close();
    pendingResolve?.({ success: false, error: 'cancelled' });
    pendingResolve = null;
  });

  // Click outside to close
  overlayEl.addEventListener('click', (e) => {
    if (e.target === overlayEl) {
      close();
      pendingResolve?.({ success: false, error: 'cancelled' });
      pendingResolve = null;
    }
  });

  // Escape key
  const escHandler = (e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      close();
      pendingResolve?.({ success: false, error: 'cancelled' });
      pendingResolve = null;
      document.removeEventListener('keydown', escHandler);
    }
  };
  document.addEventListener('keydown', escHandler);

  // Listen for postMessage from iframe
  window.addEventListener('message', handleMessage);

  // Animate in
  requestAnimationFrame(() => {
    overlayEl!.classList.add('oluso-active');
    document.body.style.overflow = 'hidden';
  });

  // Return promise that resolves when auth completes
  return new Promise<AuthResult>((resolve) => {
    pendingResolve = resolve;
  });
}

function close(): void {
  window.removeEventListener('message', handleMessage);

  if (overlayEl) {
    overlayEl.classList.remove('oluso-active');
    document.body.style.overflow = '';

    setTimeout(() => {
      overlayEl?.remove();
      overlayEl = null;
      iframeEl = null;
      brandingEl = null;
    }, 300);
  }
}

// ============================================================================
// Auto-init (data-oluso-auth attributes)
// ============================================================================

function autoInit(): void {
  document.addEventListener('click', (e) => {
    const trigger = (e.target as HTMLElement).closest('[data-oluso-auth]') as HTMLElement | null;
    if (!trigger) return;

    e.preventDefault();
    const mode = trigger.dataset.olusoAuth || 'signin';

    if (mode === 'passkey') {
      signInWithPasskey();
    } else if (mode === 'fedcm') {
      signInWithFedCM();
    } else {
      openModal(mode as 'signin' | 'signup');
    }
  });
}

if (typeof document !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', autoInit);
  } else {
    autoInit();
  }
}

// ============================================================================
// Public API (exposed as window.OlusoAuth)
// ============================================================================

function init(config: OlusoAuthConfig): void {
  embedConfig = config;
  authClient = new OlusoAuth(config);
}

function open(mode: 'signin' | 'signup' = 'signin'): Promise<AuthResult> {
  return openModal(mode);
}

function onAuth(callback: (user: OlusoUser | null) => void): void {
  authListeners.push(callback);
  // If already authenticated, call immediately
  const user = authClient?.getUser() ?? null;
  if (user) {
    callback(user);
  }
}

function getUser(): OlusoUser | null {
  return authClient?.getUser() ?? null;
}

function hasRole(role: string): boolean {
  return authClient?.hasRole(role) ?? false;
}

function hasPermission(permission: string): boolean {
  return authClient?.hasPermission(permission) ?? false;
}

function logout(options?: { redirect?: boolean }): void {
  authClient?.logout(options);
  authListeners.forEach(cb => cb(null));
}

async function signInWithPasskey(options?: PasskeyOptions): Promise<AuthResult> {
  if (!authClient) {
    return { success: false, error: 'Not initialized. Call OlusoAuth.init() first.' };
  }
  const result = await authClient.signInWithPasskey(options);
  if (result.success && result.user) {
    authListeners.forEach(cb => cb(result.user!));
  }
  return result;
}

async function signInWithFedCM(options?: FedCmOptions): Promise<AuthResult> {
  if (!authClient) {
    return { success: false, error: 'Not initialized. Call OlusoAuth.init() first.' };
  }
  const result = await authClient.signInWithFedCM(options);
  if (result.success && result.user) {
    authListeners.forEach(cb => cb(result.user!));
  }
  return result;
}

// Export for IIFE global
export { init, open, close, onAuth, getUser, hasRole, hasPermission, logout, signInWithPasskey, signInWithFedCM };

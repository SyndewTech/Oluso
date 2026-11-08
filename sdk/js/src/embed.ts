/**
 * Oluso Embed Script
 *
 * Drop-in script for running journeys (waitlists, contact forms, surveys, sign-ups) as a modal or inline in a
 * page, step by step. No framework dependencies - works with any HTML page.
 *
 * Usage (declarative):
 *   <script src="https://cdn.oluso.io/sdk/embed.js"></script>
 *   <div data-oluso-trigger
 *        data-oluso-server="https://auth.yoursite.com"
 *        data-oluso-tenant="acme"
 *        data-oluso-journey="waitlist"
 *        data-oluso-button-text="Join Waitlist">
 *   </div>
 *
 * Usage (inline, e.g. a contact form in the page):
 *   <div data-oluso-inline
 *        data-oluso-server="https://auth.yoursite.com"
 *        data-oluso-tenant="acme"
 *        data-oluso-journey="contact-us">
 *   </div>
 *
 * Usage (programmatic):
 *   <script src="https://cdn.oluso.io/sdk/embed.js"></script>
 *   <button onclick="Oluso.open()">Join</button>
 *   <script>
 *     Oluso.init({
 *       serverUrl: 'https://auth.yoursite.com',
 *       tenant: 'acme',
 *       journey: 'waitlist',
 *     });
 *   </script>
 */

import { OlusoClient } from './client';
import { mountJourney, type JourneyMount } from './journey/render';

// ============================================================================
// Types
// ============================================================================

interface EmbedConfig {
  serverUrl: string;
  tenant: string;
  journey: string;
  buttonText?: string;
  title?: string;
  description?: string;
  successTitle?: string;
  successMessage?: string;
  successCta?: { text: string; url: string };
  theme?: 'dark' | 'light' | 'auto';
  accentColor?: string;
  /** Called with the completed session */
  onSuccess?: (data: Record<string, unknown>) => void;
  onError?: (error: Error) => void;
}

// ============================================================================
// Styles
// ============================================================================

const STYLES = `
.oluso-modal-overlay {
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

.oluso-modal-overlay.oluso-active {
  opacity: 1;
  visibility: visible;
}

.oluso-modal {
  width: 100%;
  max-width: 420px;
  border-radius: 16px;
  position: relative;
  transform: translateY(16px) scale(0.96);
  transition: transform 0.25s ease;
  box-shadow: 0 24px 48px rgba(0, 0, 0, 0.2);
  overflow: hidden;
}

.oluso-modal-overlay.oluso-active .oluso-modal {
  transform: translateY(0) scale(1);
}

/* Dark theme (default) */
.oluso-modal--dark {
  background: #1c1c2e;
  border: 1px solid rgba(255, 255, 255, 0.08);
  color: #f5f5f5;
}

.oluso-modal--dark .oluso-modal__input,
.oluso-modal--dark .oluso-modal__textarea,
.oluso-modal--dark .oluso-modal__select {
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.12);
  color: #f5f5f5;
}

.oluso-modal--dark .oluso-modal__input::placeholder,
.oluso-modal--dark .oluso-modal__textarea::placeholder {
  color: rgba(255, 255, 255, 0.35);
}

.oluso-modal--dark .oluso-modal__label {
  color: rgba(255, 255, 255, 0.85);
}

.oluso-modal--dark .oluso-modal__desc {
  color: rgba(255, 255, 255, 0.55);
}

.oluso-modal--dark .oluso-modal__close {
  background: rgba(255, 255, 255, 0.06);
  color: rgba(255, 255, 255, 0.6);
}

.oluso-modal--dark .oluso-modal__close:hover {
  background: rgba(255, 255, 255, 0.12);
  color: #fff;
}

.oluso-modal--dark .oluso-modal__footer {
  color: rgba(255, 255, 255, 0.4);
}

/* Light theme */
.oluso-modal--light {
  background: #ffffff;
  border: 1px solid rgba(0, 0, 0, 0.08);
  color: #1a1a2e;
}

.oluso-modal--light .oluso-modal__input,
.oluso-modal--light .oluso-modal__textarea,
.oluso-modal--light .oluso-modal__select {
  background: #f8f8fa;
  border: 1px solid rgba(0, 0, 0, 0.1);
  color: #1a1a2e;
}

.oluso-modal--light .oluso-modal__input::placeholder,
.oluso-modal--light .oluso-modal__textarea::placeholder {
  color: rgba(0, 0, 0, 0.35);
}

.oluso-modal--light .oluso-modal__label {
  color: #1a1a2e;
}

.oluso-modal--light .oluso-modal__desc {
  color: rgba(0, 0, 0, 0.55);
}

.oluso-modal--light .oluso-modal__close {
  background: rgba(0, 0, 0, 0.04);
  color: rgba(0, 0, 0, 0.5);
}

.oluso-modal--light .oluso-modal__close:hover {
  background: rgba(0, 0, 0, 0.08);
  color: #1a1a2e;
}

.oluso-modal--light .oluso-modal__footer {
  color: rgba(0, 0, 0, 0.4);
}

/* Shared component styles */
.oluso-modal__body {
  padding: 36px 28px 28px;
}

.oluso-modal__close {
  position: absolute;
  top: 12px;
  right: 12px;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 50%;
  font-size: 1.25rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.15s, color 0.15s;
  line-height: 1;
  padding: 0;
}

.oluso-modal__title {
  font-size: 1.375rem;
  font-weight: 700;
  margin: 0 0 6px;
  line-height: 1.3;
}

.oluso-modal__desc {
  margin: 0 0 24px;
  font-size: 0.9rem;
  line-height: 1.5;
}

.oluso-modal__field {
  margin-bottom: 16px;
}

.oluso-modal__label {
  display: block;
  font-size: 0.8125rem;
  font-weight: 500;
  margin-bottom: 6px;
}

.oluso-modal__label-optional {
  opacity: 0.5;
  font-weight: 400;
}

.oluso-modal__input,
.oluso-modal__textarea,
.oluso-modal__select {
  width: 100%;
  padding: 10px 14px;
  border-radius: 8px;
  font-size: 0.9375rem;
  font-family: inherit;
  transition: border-color 0.15s, box-shadow 0.15s;
  box-sizing: border-box;
}

.oluso-modal__input:focus,
.oluso-modal__textarea:focus,
.oluso-modal__select:focus {
  outline: none;
  border-color: var(--oluso-accent, #6366f1);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--oluso-accent, #6366f1) 15%, transparent);
}

.oluso-modal__textarea {
  resize: vertical;
  min-height: 72px;
}

.oluso-modal__error {
  font-size: 0.8125rem;
  color: #ef4444;
  margin-top: 4px;
}

.oluso-modal__submit {
  width: 100%;
  padding: 12px 20px;
  border: none;
  border-radius: 10px;
  font-size: 0.9375rem;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin-top: 20px;
  background: var(--oluso-accent, #6366f1);
  color: #fff;
  transition: opacity 0.15s, transform 0.1s;
}

.oluso-modal__submit:hover:not(:disabled) {
  opacity: 0.9;
}

.oluso-modal__submit:active:not(:disabled) {
  transform: scale(0.98);
}

.oluso-modal__submit:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.oluso-modal__spinner {
  width: 18px;
  height: 18px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: #fff;
  border-radius: 50%;
  animation: oluso-spin 0.6s linear infinite;
}

@keyframes oluso-spin {
  to { transform: rotate(360deg); }
}

.oluso-modal__footer {
  text-align: center;
  margin-top: 16px;
  font-size: 0.8125rem;
}

.oluso-modal__footer a {
  color: var(--oluso-accent, #6366f1);
  text-decoration: none;
  font-weight: 500;
}

.oluso-modal__footer a:hover {
  text-decoration: underline;
}

/* Success state */
.oluso-modal__success {
  text-align: center;
  padding: 40px 28px 32px;
}

.oluso-modal__success-icon {
  width: 56px;
  height: 56px;
  background: var(--oluso-accent, #6366f1);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  margin: 0 auto 20px;
}

.oluso-modal__success-icon svg {
  width: 28px;
  height: 28px;
  stroke: #fff;
  fill: none;
  stroke-width: 2.5;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.oluso-modal__success h2 {
  font-size: 1.375rem;
  font-weight: 700;
  margin: 0 0 8px;
}

.oluso-modal__success p {
  font-size: 0.9rem;
  opacity: 0.7;
  margin: 0 0 24px;
  line-height: 1.5;
}

.oluso-modal__success-cta {
  display: inline-block;
  padding: 12px 24px;
  background: var(--oluso-accent, #6366f1);
  color: #fff;
  border-radius: 10px;
  text-decoration: none;
  font-weight: 600;
  font-size: 0.9375rem;
  transition: opacity 0.15s;
}

.oluso-modal__success-cta:hover {
  opacity: 0.9;
}

/* Trigger button (auto-rendered) */
.oluso-modal .oluso-journey, .oluso-inline .oluso-journey { --oluso-accent: inherit; }
.oluso-modal--dark .oluso-journey { --oluso-bg: transparent; }
.oluso-inline__success { text-align: center; padding: 16px 0; }
.oluso-trigger-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 12px 24px;
  border: none;
  border-radius: 10px;
  font-size: 0.9375rem;
  font-weight: 600;
  cursor: pointer;
  background: var(--oluso-accent, #6366f1);
  color: #fff;
  transition: opacity 0.15s, transform 0.1s;
  font-family: inherit;
}

.oluso-trigger-btn:hover {
  opacity: 0.9;
}

.oluso-trigger-btn:active {
  transform: scale(0.97);
}
`;

// ============================================================================
// Core
// ============================================================================

interface BaseConfig {
  serverUrl: string;
  tenant: string;
}

let baseConfig: BaseConfig | null = null;
let journeyConfigs: Map<string, EmbedConfig> = new Map();
let modalElement: HTMLElement | null = null;
let activeJourney: string | null = null;
let activeMount: JourneyMount | null = null;
const inlineMounts = new Map<HTMLElement, JourneyMount>();
let styleElement: HTMLStyleElement | null = null;

function injectStyles(): void {
  if (styleElement) return;
  styleElement = document.createElement('style');
  styleElement.id = 'oluso-embed-styles';
  styleElement.textContent = STYLES;
  document.head.appendChild(styleElement);
}

function resolveTheme(config: EmbedConfig): 'dark' | 'light' {
  if (config.theme === 'light') return 'light';
  if (config.theme === 'dark') return 'dark';
  if (config.theme === 'auto') {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  return 'dark';
}

function resolveConfig(journeyOrConfig?: string | Partial<EmbedConfig>): EmbedConfig {
  let config: EmbedConfig;

  if (typeof journeyOrConfig === 'string') {
    // Look up registered journey config
    const registered = journeyConfigs.get(journeyOrConfig);
    if (registered) {
      config = registered;
    } else if (baseConfig) {
      config = { ...baseConfig, journey: journeyOrConfig } as EmbedConfig;
    } else {
      throw new Error(`[Oluso] Journey "${journeyOrConfig}" not registered. Call Oluso.init() first.`);
    }
  } else if (journeyOrConfig && journeyOrConfig.journey) {
    // Inline config object
    config = { ...baseConfig, ...journeyOrConfig } as EmbedConfig;
  } else {
    // No argument — use first registered journey or error
    const first = journeyConfigs.values().next().value;
    if (first) {
      config = first;
    } else {
      throw new Error('[Oluso] Not initialized. Call Oluso.init() first.');
    }
  }

  return config;
}

function clientFor(config: EmbedConfig): OlusoClient {
  return new OlusoClient({ serverUrl: config.serverUrl, tenant: config.tenant });
}

function text<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, value: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  node.textContent = value;
  return node;
}

function createModal(config: EmbedConfig): HTMLElement {
  const theme = resolveTheme(config);
  const overlay = document.createElement('div');
  overlay.className = 'oluso-modal-overlay';
  overlay.id = 'oluso-modal-overlay';
  overlay.style.setProperty('--oluso-accent', config.accentColor || '#6366f1');

  const modal = document.createElement('div');
  modal.className = `oluso-modal oluso-modal--${theme}`;
  modal.setAttribute('role', 'dialog');
  modal.setAttribute('aria-modal', 'true');

  const closeButton = document.createElement('button');
  closeButton.className = 'oluso-modal__close';
  closeButton.setAttribute('aria-label', 'Close');
  closeButton.textContent = '\u00d7';
  closeButton.addEventListener('click', () => close());

  const body = document.createElement('div');
  body.className = 'oluso-modal__body';
  body.setAttribute('data-oluso-form', '');
  if (config.title) body.appendChild(text('h2', 'oluso-modal__title', config.title));
  if (config.description) body.appendChild(text('p', 'oluso-modal__desc', config.description));
  const journeyHost = document.createElement('div');
  journeyHost.setAttribute('data-oluso-journey-host', '');
  body.appendChild(journeyHost);

  const success = document.createElement('div');
  success.className = 'oluso-modal__success';
  success.setAttribute('data-oluso-success', '');
  success.style.display = 'none';

  modal.append(closeButton, body, success);
  overlay.appendChild(modal);
  overlay.addEventListener('click', (e) => {
    if (e.target === overlay) close();
  });

  document.body.appendChild(overlay);
  return overlay;
}

function showSuccess(overlay: HTMLElement, config: EmbedConfig, message: string | undefined): void {
  const body = overlay.querySelector<HTMLElement>('[data-oluso-form]')!;
  const success = overlay.querySelector<HTMLElement>('[data-oluso-success]')!;
  const icon = document.createElement('div');
  icon.className = 'oluso-modal__success-icon';
  icon.innerHTML = '<svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>';
  success.replaceChildren(icon);
  if (config.successTitle) success.appendChild(text('h2', '', config.successTitle));
  const detail = config.successMessage || message;
  if (detail) success.appendChild(text('p', '', detail));
  if (config.successCta) {
    const cta = text('a', 'oluso-modal__success-cta', config.successCta.text);
    cta.href = config.successCta.url;
    success.appendChild(cta);
  }
  body.style.display = 'none';
  success.style.display = '';
}

function startJourney(host: HTMLElement, config: EmbedConfig, onDone: (message: string | undefined) => void): JourneyMount {
  return mountJourney(host, {
    client: clientFor(config),
    slug: config.journey,
    theme: resolveTheme(config),
    showCompletion: false,
    onComplete: (session) => {
      onDone(session.message);
      config.onSuccess?.(session as unknown as Record<string, unknown>);
    },
    onError: (error) => config.onError?.(error),
  });
}

/**
 * Run a journey inline inside an element of the page, e.g. a contact form.
 *
 * @example
 *   Oluso.mount('#contact', 'contact-us')
 */
function mount(target: string | HTMLElement, journeyOrConfig?: string | Partial<EmbedConfig>): void {
  const host = typeof target === 'string' ? document.querySelector<HTMLElement>(target) : target;
  if (!host) {
    console.error(`[Oluso] Element ${String(target)} not found.`);
    return;
  }
  let config: EmbedConfig;
  try {
    config = resolveConfig(journeyOrConfig);
  } catch (err) {
    console.error((err as Error).message);
    return;
  }
  injectStyles();
  inlineMounts.get(host)?.destroy();
  host.classList.add('oluso-inline');
  host.style.setProperty('--oluso-accent', config.accentColor || '#6366f1');
  const journeyHost = document.createElement('div');
  host.replaceChildren(journeyHost);
  inlineMounts.set(host, startJourney(journeyHost, config, (message) => {
    const done = document.createElement('div');
    done.className = 'oluso-inline__success';
    if (config.successTitle) done.appendChild(text('h3', '', config.successTitle));
    const detail = config.successMessage || message;
    if (detail) done.appendChild(text('p', '', detail));
    host.replaceChildren(done);
  }));
}

/**
 * Open a modal for a specific journey.
 *
 * @param journeyOrConfig - Journey slug, config override object, or omit for default
 *
 * @example
 *   Oluso.open('waitlist')
 *   Oluso.open('demo')
 *   Oluso.open({ journey: 'waitlist', title: 'Custom Title' })
 *   Oluso.open() // opens the first/only registered journey
 */
function open(journeyOrConfig?: string | Partial<EmbedConfig>): void {
  let config: EmbedConfig;

  try {
    config = resolveConfig(journeyOrConfig);
  } catch (err) {
    console.error((err as Error).message);
    return;
  }

  injectStyles();

  // If a different journey is requested, destroy the old modal
  if (modalElement && activeJourney !== config.journey) {
    activeMount?.destroy();
    activeMount = null;
    modalElement.remove();
    modalElement = null;
  }

  if (!modalElement) {
    modalElement = createModal(config);
    activeJourney = config.journey;
  }

  const overlay = modalElement;
  if (!activeMount) {
    const host = overlay.querySelector<HTMLElement>('[data-oluso-journey-host]')!;
    activeMount = startJourney(host, config, (message) => showSuccess(overlay, config, message));
  }

  requestAnimationFrame(() => {
    overlay.classList.add('oluso-active');
    document.body.style.overflow = 'hidden';
  });
}

function close(): void {
  if (!modalElement) return;

  modalElement.classList.remove('oluso-active');
  document.body.style.overflow = '';

  // Start afresh next time, once the closing animation is over
  const overlay = modalElement;
  setTimeout(() => {
    activeMount?.destroy();
    activeMount = null;
    const body = overlay.querySelector<HTMLElement>('[data-oluso-form]');
    const success = overlay.querySelector<HTMLElement>('[data-oluso-success]');
    if (body) body.style.display = '';
    if (success) success.style.display = 'none';
  }, 300);
}

/**
 * Initialize with base config and optional per-journey overrides.
 *
 * @example
 *   // Single journey
 *   Oluso.init({ serverUrl: '...', tenant: 'acme', journey: 'waitlist' });
 *
 *   // Multiple journeys
 *   Oluso.init({
 *     serverUrl: '...',
 *     tenant: 'acme',
 *     journeys: {
 *       waitlist: { title: 'Join the Waitlist', buttonText: 'Join' },
 *       demo: { title: 'Book a Demo', buttonText: 'Request Demo' },
 *     }
 *   });
 */
function init(config: Partial<EmbedConfig> & { serverUrl: string; tenant: string } & (
  | { journey: string }
  | { journeys: Record<string, Partial<EmbedConfig>> }
)): void {
  baseConfig = { serverUrl: config.serverUrl, tenant: config.tenant };

  if ('journeys' in config && config.journeys) {
    // Multiple journeys
    for (const [id, journeyConfig] of Object.entries(config.journeys)) {
      journeyConfigs.set(id, {
        ...config,
        ...journeyConfig,
        journey: id,
        serverUrl: config.serverUrl,
        tenant: config.tenant,
      } as EmbedConfig);
    }
  } else if ('journey' in config && config.journey) {
    // Single journey
    journeyConfigs.set(config.journey, config as EmbedConfig);
  }
}

function destroy(): void {
  activeMount?.destroy();
  activeMount = null;
  inlineMounts.forEach((m) => m.destroy());
  inlineMounts.clear();
  if (modalElement) {
    modalElement.remove();
    modalElement = null;
  }
  if (styleElement) {
    styleElement.remove();
    styleElement = null;
  }
  journeyConfigs.clear();
  baseConfig = null;
  activeJourney = null;
}

// ============================================================================
// Auto-initialization from data attributes
// ============================================================================

function autoInit(): void {
  document.querySelectorAll<HTMLElement>('[data-oluso-inline]').forEach((el) => {
    const serverUrl = el.dataset.olusoServer;
    const tenant = el.dataset.olusoTenant;
    const journey = el.dataset.olusoJourney;
    if (!serverUrl || !tenant || !journey) {
      console.warn('[Oluso] Missing required data attributes (data-oluso-server, data-oluso-tenant, data-oluso-journey) on inline element.');
      return;
    }
    if (!baseConfig) baseConfig = { serverUrl, tenant };
    mount(el, {
      serverUrl,
      tenant,
      journey,
      successTitle: el.dataset.olusoSuccessTitle,
      successMessage: el.dataset.olusoSuccessMessage,
      theme: (el.dataset.olusoTheme as EmbedConfig['theme']) || 'auto',
      accentColor: el.dataset.olusoAccent,
    });
  });

  const triggers = document.querySelectorAll<HTMLElement>('[data-oluso-trigger]');

  triggers.forEach((el) => {
    const serverUrl = el.dataset.olusoServer;
    const tenant = el.dataset.olusoTenant;
    const journey = el.dataset.olusoJourney;

    if (!serverUrl || !tenant || !journey) {
      console.warn('[Oluso] Missing required data attributes (data-oluso-server, data-oluso-tenant, data-oluso-journey) on trigger element.');
      return;
    }

    const config: EmbedConfig = {
      serverUrl,
      tenant,
      journey,
      buttonText: el.dataset.olusoButtonText || el.dataset.olusoSubmitText || 'Submit',
      title: el.dataset.olusoTitle,
      description: el.dataset.olusoDescription,
      successTitle: el.dataset.olusoSuccessTitle,
      successMessage: el.dataset.olusoSuccessMessage,
      theme: (el.dataset.olusoTheme as EmbedConfig['theme']) || 'dark',
      accentColor: el.dataset.olusoAccent,
    };

    // Register this journey config
    if (!baseConfig) {
      baseConfig = { serverUrl, tenant };
    }
    journeyConfigs.set(journey, config);

    // Render trigger button if element is empty
    const renderButton = el.dataset.olusoRenderButton !== 'false' && el.children.length === 0;

    if (renderButton) {
      injectStyles();
      const btnText = el.dataset.olusoButtonText || 'Join Waitlist';
      const accent = config.accentColor || '#6366f1';
      const btn = document.createElement('button');
      btn.className = 'oluso-trigger-btn';
      btn.style.setProperty('--oluso-accent', accent);
      btn.textContent = btnText;
      btn.addEventListener('click', () => open(journey));
      el.appendChild(btn);
    } else {
      el.addEventListener('click', () => open(journey));
    }
  });

  // Escape key listener
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') close();
  });
}

// Run on DOMContentLoaded or immediately if already loaded
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', autoInit);
} else {
  autoInit();
}

// ============================================================================
// Global API
// ============================================================================

const OlusoEmbed = { init, open, close, destroy, mount };

// Expose globally
(window as unknown as Record<string, unknown>).Oluso = OlusoEmbed;

export { init, open, close, destroy, mount };
export type { EmbedConfig };

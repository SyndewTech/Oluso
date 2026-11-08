import { InjectionToken } from '@angular/core';

/**
 * Tells the app's other tabs when a refresh token has been spent, and hands them what it was exchanged for.
 *
 * Refresh tokens are single use. Tabs can hold the same one: a duplicated tab starts with a copy of its
 * opener's session, and remembered sign-ins share one store across every tab. When one tab redeems it, the
 * others must take the rotated tokens rather than present the spent one, which the server refuses.
 *
 * The tokens travel encrypted under a key derived from the spent refresh token, so only a tab that already
 * held that token can read them; any other tab of the app (another user's session-only sign-in, say) learns
 * nothing. An answer is kept for {@link ROTATION_ANSWER_TTL_MS}, well inside the 120 s the OIDC client
 * allows between an ID token being issued and accepted.
 */

/** The token entries of the OIDC client's stored state, as written after a refresh. */
export type RotatedTokens = Record<string, unknown>;

export const ROTATION_ANSWER_TTL_MS = 60_000;

const CHANNEL_NAME = 'oluso.token-rotation';

interface RotationMessage {
  type: 'rotated';
  /** Identifies the spent refresh token without revealing it. */
  id: string;
  iv: ArrayBuffer;
  ciphertext: ArrayBuffer;
}

interface Answer {
  readonly iv: ArrayBuffer;
  readonly ciphertext: ArrayBuffer;
  readonly at: number;
}

type Waiter = () => void;

interface Publication {
  readonly done: Promise<void>;
  readonly resolve: () => void;
}

export class TokenRotationChannel {
  private readonly answers = new Map<string, Answer>();
  private readonly waiters = new Map<string, Set<Waiter>>();
  private readonly publications = new Map<string, Publication>();
  private readonly listeners = new Set<(id: string) => void>();
  private readonly channel: BroadcastChannel | null;

  constructor(channelName = CHANNEL_NAME) {
    this.channel = typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(channelName);
    this.channel?.addEventListener('message', (event: MessageEvent<RotationMessage>) => {
      const message = event.data;
      if (message?.type !== 'rotated') return;
      this.remember(message.id, { iv: message.iv, ciphertext: message.ciphertext, at: Date.now() });
      this.listeners.forEach((listener) => listener(message.id));
    });
  }

  /**
   * Records that `spentRefreshToken` was exchanged for `tokens` and tells the other tabs. Resolves once the
   * other tabs have been told.
   */
  async publish(spentRefreshToken: string, tokens: RotatedTokens): Promise<void> {
    const id = await rotationId(spentRefreshToken);
    const key = await rotationKey(spentRefreshToken);
    const iv = crypto.getRandomValues(new Uint8Array(12)).buffer;
    const ciphertext = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, new TextEncoder().encode(JSON.stringify(tokens)));
    this.remember(id, { iv, ciphertext, at: Date.now() });
    this.channel?.postMessage({ type: 'rotated', id, iv, ciphertext } satisfies RotationMessage);
    this.publication(spentRefreshToken).resolve();
  }

  /** Resolves once this tab has published the rotation of `refreshToken`, or after `timeoutMs`. */
  awaitPublished(refreshToken: string, timeoutMs: number): Promise<void> {
    return new Promise<void>((resolve) => {
      const timer = setTimeout(resolve, timeoutMs);
      void this.publication(refreshToken).done.then(() => { clearTimeout(timer); resolve(); });
    });
  }

  /** What `refreshToken` was exchanged for, in this tab or another, while that answer is fresh; else null. */
  async answerFor(refreshToken: string): Promise<RotatedTokens | null> {
    const id = await rotationId(refreshToken);
    const answer = this.answers.get(id);
    if (!answer || Date.now() - answer.at > ROTATION_ANSWER_TTL_MS) return null;
    try {
      const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: answer.iv }, await rotationKey(refreshToken), answer.ciphertext);
      return JSON.parse(new TextDecoder().decode(plain)) as RotatedTokens;
    } catch {
      return null;
    }
  }

  /** {@link answerFor}, waiting up to `timeoutMs` for another tab's rotation of `refreshToken` to arrive. */
  async waitForAnswer(refreshToken: string, timeoutMs: number): Promise<RotatedTokens | null> {
    const id = await rotationId(refreshToken);
    if (!this.answers.has(id)) {
      await new Promise<void>((resolve) => {
        const waiter = () => { clearTimeout(timer); this.waiters.get(id)?.delete(waiter); resolve(); };
        const timer = setTimeout(waiter, timeoutMs);
        const set = this.waiters.get(id) ?? new Set<Waiter>();
        set.add(waiter);
        this.waiters.set(id, set);
      });
    }
    return this.answerFor(refreshToken);
  }

  /** Calls `listener` with the id of every rotation that arrives from another tab. Returns the unsubscribe. */
  onRotation(listener: (id: string) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** Whether `refreshToken` is the token a rotation with `id` spent. */
  async isSpentBy(refreshToken: string, id: string): Promise<boolean> {
    return (await rotationId(refreshToken)) === id;
  }

  private remember(id: string, answer: Answer): void {
    this.answers.set(id, answer);
    setTimeout(() => { if (this.answers.get(id) === answer) this.answers.delete(id); }, ROTATION_ANSWER_TTL_MS);
    this.waiters.get(id)?.forEach((wake) => wake());
  }

  private publication(refreshToken: string): Publication {
    let publication = this.publications.get(refreshToken);
    if (!publication) {
      let resolve!: () => void;
      const done = new Promise<void>((r) => (resolve = r));
      publication = { done, resolve };
      this.publications.set(refreshToken, publication);
      setTimeout(() => this.publications.delete(refreshToken), ROTATION_ANSWER_TTL_MS);
    }
    return publication;
  }
}

/** The channel the app's token storage and refresh interceptor share. */
export const tokenRotationChannel = new TokenRotationChannel();

/** Overrides {@link tokenRotationChannel} for the token storage (tests stand up one per simulated tab). */
export const TOKEN_ROTATION_CHANNEL = new InjectionToken<TokenRotationChannel>('TOKEN_ROTATION_CHANNEL');

async function rotationId(refreshToken: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`oluso.rotation.id:${refreshToken}`));
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('');
}

async function rotationKey(refreshToken: string): Promise<CryptoKey> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`oluso.rotation.key:${refreshToken}`));
  return crypto.subtle.importKey('raw', digest, 'AES-GCM', false, ['encrypt', 'decrypt']);
}

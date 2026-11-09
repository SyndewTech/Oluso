import { Inject, Injectable, Optional } from '@angular/core';
import { TOKEN_ROTATION_CHANNEL, TokenRotationChannel, tokenRotationChannel, type RotatedTokens } from './token-rotation-channel';

const REMEMBER_ME_CLAIM = 'remember_me';

/** The entries of the stored state a refresh replaces; the rest (flow state, endpoints, keys) stays per tab. */
const TOKEN_ENTRIES = ['authnResult', 'authzData', 'access_token_expires_at', 'userData', 'session_state'];

/**
 * The OIDC client stores a refresh result in several writes (the token response first, the access token and
 * its expiry after), so a rotation is published once writes to that state have been quiet this long.
 */
const ROTATION_SETTLE_MS = 100;

/**
 * Token storage that follows the user's "Remember me" choice at sign-in.
 *
 * The OIDC client hands this one JSON blob per configuration. Until sign-in
 * completes the blob only holds the pending state (nonce, code verifier) and
 * stays in session storage, which the callback in the same tab can read. Once
 * the blob contains a token response, the ID token says whether the sign-in was
 * remembered: remembered sessions move to local storage, so they survive a
 * browser restart and are shared across tabs; session-only sign-ins stay per
 * tab and vanish when it closes. Sign-out removes both copies.
 *
 * Refresh tokens are single use, and tabs can hold the same one (a duplicated
 * tab starts with a copy of its opener's session; remembered sessions share
 * one store). When a write replaces the refresh token, the spent token and the
 * new tokens go to the app's other tabs; a tab still holding the spent token
 * takes the new ones, so it never presents a token the server has retired.
 *
 * Kept free of the OIDC library: it matches the storage contract by shape and
 * is registered by the app's auth provider.
 */
@Injectable({ providedIn: 'root' })
export class RememberMeSecurityStorage {
  private readonly rotations: TokenRotationChannel;
  private readonly pendingRotations = new Map<string, { spent: string; timer: ReturnType<typeof setTimeout> }>();

  constructor(@Optional() @Inject(TOKEN_ROTATION_CHANNEL) rotations?: TokenRotationChannel | null) {
    this.rotations = rotations ?? tokenRotationChannel;
    this.rotations.onRotation((id) => void this.adoptRotation(id));
  }

  read(key: string): string | null {
    return safe(() => localStorage.getItem(key)) ?? safe(() => sessionStorage.getItem(key));
  }

  write(key: string, value: string): void {
    const previous = refreshTokenOf(this.read(key));
    this.store(key, value);
    const current = refreshTokenOf(value);
    const pending = this.pendingRotations.get(key);
    const spent = pending?.spent ?? (previous && current && previous !== current ? previous : null);
    if (!spent) return;
    if (pending) clearTimeout(pending.timer);
    const timer = setTimeout(() => this.publishRotation(key, spent), ROTATION_SETTLE_MS);
    this.pendingRotations.set(key, { spent, timer });
  }

  private publishRotation(key: string, spent: string): void {
    this.pendingRotations.delete(key);
    const blob = this.read(key);
    const current = refreshTokenOf(blob);
    if (blob && current && current !== spent) void this.rotations.publish(spent, tokenEntries(blob));
  }

  /** Takes the tokens another tab rotated to, for every stored session still holding the token it spent. */
  private async adoptRotation(id: string): Promise<void> {
    for (const key of this.authKeys()) {
      const blob = this.read(key);
      const held = refreshTokenOf(blob);
      if (!blob || !held || !(await this.rotations.isSpentBy(held, id))) continue;
      const tokens = await this.rotations.answerFor(held);
      if (!tokens || refreshTokenOf(this.read(key)) !== held) continue;
      this.store(key, JSON.stringify({ ...JSON.parse(blob), ...tokens }));
    }
  }

  private authKeys(): string[] {
    const keys = new Set<string>();
    for (const store of [safe(() => localStorage), safe(() => sessionStorage)]) {
      if (!store) continue;
      for (let i = 0; i < store.length; i++) {
        const key = store.key(i);
        if (key && isAuthBlob(store.getItem(key))) keys.add(key);
      }
    }
    return [...keys];
  }

  private store(key: string, value: string): void {
    if (isRememberedSession(value)) {
      safe(() => localStorage.setItem(key, value));
      safe(() => sessionStorage.removeItem(key));
    } else {
      safe(() => sessionStorage.setItem(key, value));
      safe(() => localStorage.removeItem(key));
    }
  }

  remove(key: string): void {
    safe(() => localStorage.removeItem(key));
    safe(() => sessionStorage.removeItem(key));
  }

  clear(): void {
    // Only our blobs leave local storage; other app preferences live there too.
    safe(() => {
      const ours: string[] = [];
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key && isAuthBlob(localStorage.getItem(key))) ours.push(key);
      }
      ours.forEach((key) => localStorage.removeItem(key));
    });
    safe(() => sessionStorage.clear());
  }
}

function isRememberedSession(blob: string): boolean {
  try {
    const parsed = JSON.parse(blob) as { authnResult?: { id_token?: unknown } } | null;
    const idToken = parsed?.authnResult?.id_token;
    if (typeof idToken !== 'string') return false;
    const payload = decodeJwtPayload(idToken);
    const flag = payload?.[REMEMBER_ME_CLAIM];
    return flag === true || flag === 'true';
  } catch {
    return false;
  }
}

function refreshTokenOf(blob: string | null): string | null {
  if (!blob) return null;
  try {
    const token = (JSON.parse(blob) as { authnResult?: { refresh_token?: unknown } } | null)?.authnResult?.refresh_token;
    return typeof token === 'string' && token ? token : null;
  } catch {
    return null;
  }
}

function tokenEntries(blob: string): RotatedTokens {
  const parsed = JSON.parse(blob) as Record<string, unknown>;
  return Object.fromEntries(TOKEN_ENTRIES.filter((k) => k in parsed).map((k) => [k, parsed[k]]));
}

function isAuthBlob(value: string | null): boolean {
  if (!value) return false;
  try {
    const parsed = JSON.parse(value) as Record<string, unknown> | null;
    return !!parsed && typeof parsed === 'object' && ('authnResult' in parsed || 'authWellKnownEndPoints' in parsed);
  } catch {
    return false;
  }
}

function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const part = token.split('.')[1];
  if (!part) return null;
  const base64 = part.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
  return JSON.parse(atob(padded)) as Record<string, unknown>;
}

function safe<T>(fn: () => T): T | null {
  try {
    return fn();
  } catch {
    return null;
  }
}

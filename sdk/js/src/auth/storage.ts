import type { TokenSet } from './types';

/**
 * Token storage abstraction
 */
export interface TokenStorage {
  getTokens(): TokenSet | null;
  setTokens(tokens: TokenSet): void;
  clear(): void;
}

const STORAGE_KEY = 'oluso_auth_tokens';

export class MemoryStorage implements TokenStorage {
  private tokens: TokenSet | null = null;

  getTokens(): TokenSet | null {
    if (this.tokens && Date.now() / 1000 >= this.tokens.expiresAt) {
      this.tokens = null;
    }
    return this.tokens;
  }

  setTokens(tokens: TokenSet): void {
    this.tokens = tokens;
  }

  clear(): void {
    this.tokens = null;
  }
}

export class BrowserStorage implements TokenStorage {
  private store: Storage;

  constructor(type: 'localStorage' | 'sessionStorage') {
    this.store = type === 'localStorage' ? localStorage : sessionStorage;
  }

  getTokens(): TokenSet | null {
    try {
      const raw = this.store.getItem(STORAGE_KEY);
      if (!raw) return null;

      const tokens: TokenSet = JSON.parse(raw);
      if (Date.now() / 1000 >= tokens.expiresAt) {
        this.clear();
        return null;
      }
      return tokens;
    } catch {
      return null;
    }
  }

  setTokens(tokens: TokenSet): void {
    this.store.setItem(STORAGE_KEY, JSON.stringify(tokens));
  }

  clear(): void {
    this.store.removeItem(STORAGE_KEY);
  }
}

export function createStorage(type: 'memory' | 'localStorage' | 'sessionStorage'): TokenStorage {
  if (type === 'memory') return new MemoryStorage();
  return new BrowserStorage(type);
}

import { RememberMeSecurityStorage } from './remember-me-security-storage';
import { TokenRotationChannel } from './token-rotation-channel';

function jwt(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${b64({ alg: 'none' })}.${b64(payload)}.sig`;
}

/** Minimal Web Storage for the test runner, which has no DOM storage. */
class MemoryStorage implements Storage {
  private map = new Map<string, string>();
  get length(): number { return this.map.size; }
  key(index: number): string | null { return Array.from(this.map.keys())[index] ?? null; }
  getItem(key: string): string | null { return this.map.get(key) ?? null; }
  setItem(key: string, value: string): void { this.map.set(key, String(value)); }
  removeItem(key: string): void { this.map.delete(key); }
  clear(): void { this.map.clear(); }
}

describe('RememberMeSecurityStorage', () => {
  const key = 'admin';
  let storage: RememberMeSecurityStorage;

  beforeAll(() => {
    const g = globalThis as unknown as { localStorage?: Storage; sessionStorage?: Storage };
    if (!g.localStorage) g.localStorage = new MemoryStorage();
    if (!g.sessionStorage) g.sessionStorage = new MemoryStorage();
  });

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    storage = new RememberMeSecurityStorage();
  });

  it('keeps pre-login state in session storage', () => {
    storage.write(key, JSON.stringify({ authNonce: 'n', codeVerifier: 'v' }));
    expect(sessionStorage.getItem(key)).not.toBeNull();
    expect(localStorage.getItem(key)).toBeNull();
  });

  it('moves a remembered sign-in to local storage', () => {
    const blob = JSON.stringify({ authnResult: { id_token: jwt({ sub: 'u1', remember_me: 'true' }) } });
    storage.write(key, blob);
    expect(localStorage.getItem(key)).toBe(blob);
    expect(sessionStorage.getItem(key)).toBeNull();
    expect(storage.read(key)).toBe(blob);
  });

  it('keeps a session-only sign-in per tab', () => {
    const blob = JSON.stringify({ authnResult: { id_token: jwt({ sub: 'u1' }) } });
    storage.write(key, blob);
    expect(sessionStorage.getItem(key)).toBe(blob);
    expect(localStorage.getItem(key)).toBeNull();
  });

  it('drops the local copy when the session is no longer remembered', () => {
    storage.write(key, JSON.stringify({ authnResult: { id_token: jwt({ remember_me: 'true' }) } }));
    storage.write(key, JSON.stringify({ authWellKnownEndPoints: {} }));
    expect(localStorage.getItem(key)).toBeNull();
    expect(sessionStorage.getItem(key)).not.toBeNull();
  });

  it('remove and clear touch both stores but leave unrelated local keys alone', () => {
    localStorage.setItem('workspace_selected_app', 'home');
    storage.write(key, JSON.stringify({ authnResult: { id_token: jwt({ remember_me: 'true' }) } }));
    storage.remove(key);
    expect(localStorage.getItem(key)).toBeNull();

    storage.write(key, JSON.stringify({ authnResult: { id_token: jwt({ remember_me: 'true' }) } }));
    storage.clear();
    expect(localStorage.getItem(key)).toBeNull();
    expect(localStorage.getItem('workspace_selected_app')).toBe('home');
  });

  it('treats malformed blobs as session-only', () => {
    storage.write(key, 'not json');
    expect(sessionStorage.getItem(key)).toBe('not json');
    expect(localStorage.getItem(key)).toBeNull();
  });

  describe('across tabs', () => {
    let channelSeq = 0;
    let thisTab: TokenRotationChannel;
    let otherTab: TokenRotationChannel;
    const session = (refreshToken: string, extra: Record<string, unknown> = {}) =>
      JSON.stringify({ authnResult: { id_token: jwt({ sub: 'u1' }), refresh_token: refreshToken }, ...extra });

    beforeEach(() => {
      const name = `test.storage-rotation.${++channelSeq}`;
      thisTab = new TokenRotationChannel(name);
      otherTab = new TokenRotationChannel(name);
      storage = new RememberMeSecurityStorage(thisTab);
    });

    it('tells the other tabs when a write replaces the refresh token, once the whole refresh result is stored', async () => {
      storage.write(key, session('rt-1', { authzData: 'at-1', access_token_expires_at: 1 }));
      storage.write(key, session('rt-1', { authzData: 'at-1', access_token_expires_at: 1, userData: { sub: 'u1' } }));
      storage.write(key, session('rt-2', { authzData: 'at-1', access_token_expires_at: 1, codeVerifier: 'v' }));
      storage.write(key, session('rt-2', { authzData: 'at-2', access_token_expires_at: 1, codeVerifier: 'v' }));
      storage.write(key, session('rt-2', { authzData: 'at-2', access_token_expires_at: 2, codeVerifier: 'v' }));

      const handed = await otherTab.waitForAnswer('rt-1', 1000);
      expect((handed?.['authnResult'] as { refresh_token: string }).refresh_token).toBe('rt-2');
      expect(handed?.['authzData']).toBe('at-2');
      expect(handed?.['access_token_expires_at']).toBe(2);
      expect(handed).not.toHaveProperty('codeVerifier');
    });

    it('takes the tokens another tab rotated to while holding the token it spent, keeping its own flow state', async () => {
      storage.write(key, session('rt-old', { codeVerifier: 'this-tab', authzData: 'at-old' }));
      await otherTab.publish('rt-old', {
        authnResult: { id_token: jwt({ sub: 'u1' }), refresh_token: 'rt-new' },
        authzData: 'at-new',
        access_token_expires_at: 123,
      });

      await vi.waitFor(() => expect(JSON.parse(storage.read(key)!).authnResult.refresh_token).toBe('rt-new'));
      const stored = JSON.parse(storage.read(key)!);
      expect(stored.authzData).toBe('at-new');
      expect(stored.access_token_expires_at).toBe(123);
      expect(stored.codeVerifier).toBe('this-tab');
      expect(sessionStorage.getItem(key)).not.toBeNull();
    });

    it("leaves a tab holding a different refresh token alone", async () => {
      storage.write(key, session('rt-mine'));
      await otherTab.publish('rt-someone-else', { authnResult: { refresh_token: 'rt-theirs' }, authzData: 'theirs' });
      await new Promise((r) => setTimeout(r, 50));

      expect(JSON.parse(storage.read(key)!).authnResult.refresh_token).toBe('rt-mine');
    });
  });
});

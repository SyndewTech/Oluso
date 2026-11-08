import { firstValueFrom, Observable, of, Subject } from 'rxjs';
import { AuthRefreshResult } from './auth-provider';
import { RenewAwareAuthProvider } from './renew-aware-auth-provider';

class TestProvider extends RenewAwareAuthProvider {
  override readonly isAuthenticated$ = of(true);
  token: string | null = 'old';
  refreshResult: AuthRefreshResult = { isAuthenticated: true, accessToken: 'refreshed' };
  refreshCalls = 0;
  readonly renewStarted = new Subject<void>();
  readonly renewSettled = new Subject<boolean>();

  constructor(waitMs = 20_000) {
    super();
    this.renewWaitMs = waitMs;
    this.trackRenew(this.renewStarted, this.renewSettled);
  }

  getAccessToken(): Observable<string | null> { return of(this.token); }
  protected refreshOnce(): Observable<AuthRefreshResult> { this.refreshCalls++; return of(this.refreshResult); }
  authorize(): void { /* not used */ }
}

describe('RenewAwareAuthProvider', () => {
  it('refreshes directly when no renew is running', async () => {
    const p = new TestProvider();
    const result = await firstValueFrom(p.forceRefreshSession());
    expect(result).toEqual({ isAuthenticated: true, accessToken: 'refreshed' });
    expect(p.refreshCalls).toBe(1);
  });

  it('waits for a running renew and answers with its token instead of refreshing again', async () => {
    const p = new TestProvider();
    p.renewStarted.next();
    const pending = firstValueFrom(p.forceRefreshSession());
    p.token = 'renewed';
    p.renewSettled.next(true);
    expect(await pending).toEqual({ isAuthenticated: true, accessToken: 'renewed' });
    expect(p.refreshCalls).toBe(0);
  });

  it('treats a declined refresh as pending when a renew started meanwhile', async () => {
    const p = new TestProvider();
    p.refreshResult = { isAuthenticated: false, accessToken: null };
    // The library declines because its own renew is running: it fired "started" first.
    p.renewStarted.next();
    const pending = firstValueFrom(p.forceRefreshSession());
    p.token = 'renewed';
    p.renewSettled.next(true);
    expect(await pending).toEqual({ isAuthenticated: true, accessToken: 'renewed' });
  });

  it('reports unauthenticated when the running renew fails', async () => {
    const p = new TestProvider();
    p.renewStarted.next();
    const pending = firstValueFrom(p.forceRefreshSession());
    p.renewSettled.next(false);
    expect(await pending).toEqual({ isAuthenticated: false, accessToken: null });
  });

  it('reports unauthenticated when the running renew never settles', async () => {
    const p = new TestProvider(20);
    p.renewStarted.next();
    expect(await firstValueFrom(p.forceRefreshSession())).toEqual({ isAuthenticated: false, accessToken: null });
  });

  it('passes a genuine refresh failure straight through', async () => {
    const p = new TestProvider();
    p.refreshResult = { isAuthenticated: false, accessToken: null };
    expect(await firstValueFrom(p.forceRefreshSession())).toEqual({ isAuthenticated: false, accessToken: null });
    expect(p.refreshCalls).toBe(1);
  });
});

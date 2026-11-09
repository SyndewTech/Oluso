import { firstValueFrom, Observable, of, throwError } from 'rxjs';
import { checkAuthRenewingSession, StartupAuthClient } from './check-auth-renewing';

type Result = { isAuthenticated: boolean; accessToken?: string };

class FakeClient implements StartupAuthClient<Result> {
  checkCalls = 0;
  serverCalls = 0;
  constructor(
    private readonly local: Result,
    private readonly refreshToken: string | null,
    private readonly server: () => Observable<Result> = () => of({ isAuthenticated: true, accessToken: 'renewed' })
  ) {}
  checkAuth(): Observable<Result> { this.checkCalls++; return of(this.local); }
  checkAuthIncludingServer(): Observable<Result> { this.serverCalls++; return this.server(); }
  getRefreshToken(): Observable<string | null> { return of(this.refreshToken); }
}

const PAGE = 'http://localhost:4204/dashboard';

describe('checkAuthRenewingSession', () => {
  it('keeps a session whose stored tokens are still valid without calling the server', async () => {
    const client = new FakeClient({ isAuthenticated: true, accessToken: 'stored' }, 'rt');
    expect(await firstValueFrom(checkAuthRenewingSession(client, PAGE))).toEqual({ isAuthenticated: true, accessToken: 'stored' });
    expect(client.serverCalls).toBe(0);
  });

  it('renews with the stored refresh token when the access token has expired', async () => {
    const client = new FakeClient({ isAuthenticated: false }, 'rt');
    expect(await firstValueFrom(checkAuthRenewingSession(client, PAGE))).toEqual({ isAuthenticated: true, accessToken: 'renewed' });
    expect(client.serverCalls).toBe(1);
  });

  it('reports signed out when there is no refresh token to renew with', async () => {
    const client = new FakeClient({ isAuthenticated: false }, null);
    expect((await firstValueFrom(checkAuthRenewingSession(client, PAGE))).isAuthenticated).toBe(false);
    expect(client.serverCalls).toBe(0);
  });

  it('reports signed out when the server refuses the refresh', async () => {
    const client = new FakeClient({ isAuthenticated: false }, 'rt', () => throwError(() => new Error('invalid_grant')));
    expect((await firstValueFrom(checkAuthRenewingSession(client, PAGE))).isAuthenticated).toBe(false);
  });

  it('leaves a sign-in callback to the plain check', async () => {
    const client = new FakeClient({ isAuthenticated: false }, 'rt');
    await firstValueFrom(checkAuthRenewingSession(client, 'http://localhost:4204/callback?code=abc&state=xyz'));
    expect(client.serverCalls).toBe(0);
  });
});

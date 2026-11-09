import { HttpErrorResponse, HttpHeaders, HttpRequest, HttpResponse, type HttpEvent } from '@angular/common/http';
import { Observable, Subject, defer, of, throwError } from 'rxjs';
import { refreshRetryDelayMs, refreshTokenOf, requestTenantSwitch, tokenRefreshRetryInterceptor } from './token-refresh-retry.interceptor';
import { ROTATION_ANSWER_TTL_MS, TokenRotationChannel, tokenRotationChannel } from '../auth/token-rotation-channel';

const TOKEN_URL = 'https://idp.example/connect/token';
let tokenSeq = 0;
/** Each test redeems its own refresh token: the interceptor remembers answers per token across calls. */
const freshToken = () => `rt-${++tokenSeq}`;
const refreshRequest = (token: string) =>
  new HttpRequest<string>('POST', TOKEN_URL, `grant_type=refresh_token&client_id=workspace-portal&refresh_token=${token}`);
const switchRequest = (token: string, tenant: string) =>
  new HttpRequest<string>('POST', TOKEN_URL, `grant_type=refresh_token&client_id=workspace-portal&refresh_token=${token}&tenant=${tenant}`);
const codeRequest = () =>
  new HttpRequest<string>('POST', TOKEN_URL, 'grant_type=authorization_code&client_id=workspace-portal&code=xyz');

const tokenResponse = (expiresIn = 3600) => new HttpResponse({ status: 200, body: { access_token: 'new', expires_in: expiresIn } });

/** A backend that answers each call with the next status in turn; 200 is a token response. */
function backend(statuses: number[], headers?: HttpHeaders) {
  let call = 0;
  const handler = (): Observable<HttpEvent<unknown>> =>
    defer(() => {
      const status = statuses[Math.min(call++, statuses.length - 1)];
      return status === 200 ? of(tokenResponse()) : throwError(() => new HttpErrorResponse({ status, url: TOKEN_URL, headers }));
    });
  return { handler, calls: () => call };
}

/** A backend whose single answer arrives when the test releases it. */
function slowBackend() {
  let calls = 0;
  const answer = new Subject<HttpEvent<unknown>>();
  const handler = (): Observable<HttpEvent<unknown>> => { calls++; return answer; };
  const release = () => { answer.next(tokenResponse()); answer.complete(); };
  return { handler, release, calls: () => calls };
}

const bodyOf = (event: unknown) => (event as HttpResponse<{ access_token: string; expires_in: number }>).body!;

describe('tokenRefreshRetryInterceptor', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('retries a refresh answered by an unavailable server until the server answers', async () => {
    const api = backend([502, 503, 504, 200]);
    const result: unknown[] = [];
    tokenRefreshRetryInterceptor(refreshRequest(freshToken()), api.handler).subscribe({ next: (e) => result.push(e) });

    await vi.waitFor(() => expect(api.calls()).toBe(1));
    await vi.advanceTimersByTimeAsync(1000);
    expect(api.calls()).toBe(2);
    await vi.advanceTimersByTimeAsync(2000);
    expect(api.calls()).toBe(3);
    await vi.advanceTimersByTimeAsync(4000);
    expect(api.calls()).toBe(4);
    expect(result).toHaveLength(1);
    expect((result[0] as HttpResponse<unknown>).status).toBe(200);
  });

  it('passes a refusal of the refresh token straight through, and a later request sends again', async () => {
    const token = freshToken();
    const api = backend([400]);
    let error: unknown;
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe({ error: (e) => (error = e) });
    await vi.waitFor(() => expect(error).toBeDefined());

    expect(api.calls()).toBe(1);
    expect((error as HttpErrorResponse).status).toBe(400);

    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe({ error: () => undefined });
    await vi.waitFor(() => expect(api.calls()).toBe(2));
  });

  it('redeems a refresh token once when two refreshes ask at the same time', async () => {
    const token = freshToken();
    const api = slowBackend();
    const answers: unknown[] = [];
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe((e) => answers.push(e));
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe((e) => answers.push(e));
    await vi.waitFor(() => expect(api.calls()).toBe(1));
    api.release();
    await vi.waitFor(() => expect(answers).toHaveLength(2));

    expect(api.calls()).toBe(1);
    expect(answers.map(bodyOf).map((b) => b.access_token)).toEqual(['new', 'new']);
  });

  it('finishes a refresh its caller abandoned and hands the answer to the next request for that token', async () => {
    const token = freshToken();
    const api = slowBackend();
    const abandoned = tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe();
    abandoned.unsubscribe();
    await vi.waitFor(() => expect(api.calls()).toBe(1));
    api.release();
    await vi.advanceTimersByTimeAsync(20_000);

    const answers: unknown[] = [];
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe((e) => answers.push(e));
    await vi.waitFor(() => expect(answers).toHaveLength(1));

    expect(api.calls()).toBe(1);
    expect(bodyOf(answers[0]).expires_in).toBe(3600 - 20);
  });

  it('forgets an answer once the reuse window has passed', async () => {
    const token = freshToken();
    const api = backend([200]);
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe();
    await vi.waitFor(() => expect(api.calls()).toBe(1));
    await vi.advanceTimersByTimeAsync(10_000 + ROTATION_ANSWER_TTL_MS + 1);
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe();

    await vi.waitFor(() => expect(api.calls()).toBe(2));
  });

  it('sends each different refresh token', async () => {
    const api = backend([200]);
    tokenRefreshRetryInterceptor(refreshRequest(freshToken()), api.handler).subscribe();
    tokenRefreshRetryInterceptor(refreshRequest(freshToken()), api.handler).subscribe();

    await vi.waitFor(() => expect(api.calls()).toBe(2));
  });

  it('answers a refresh token another tab already redeemed without sending it', async () => {
    const token = freshToken();
    const expiresAt = Date.now() + 90_000;
    await tokenRotationChannel.publish(token, {
      authnResult: { access_token: 'from-other-tab', refresh_token: 'rotated', expires_in: 120 },
      access_token_expires_at: expiresAt,
    });
    const api = backend([200]);
    const answers: unknown[] = [];
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe((e) => answers.push(e));
    await vi.waitFor(() => expect(answers).toHaveLength(1));

    expect(api.calls()).toBe(0);
    const body = bodyOf(answers[0]) as unknown as { access_token: string; refresh_token: string; expires_in: number };
    expect(body.access_token).toBe('from-other-tab');
    expect(body.refresh_token).toBe('rotated');
    expect(body.expires_in).toBeGreaterThanOrEqual(88);
    expect(body.expires_in).toBeLessThanOrEqual(90);
  });

  it('sends a tenant switch even when a plain refresh of the same token has just been answered', async () => {
    const token = freshToken();
    const api = backend([200]);
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe();
    await vi.waitFor(() => expect(api.calls()).toBe(1));

    tokenRefreshRetryInterceptor(switchRequest(token, 'tenant-b'), api.handler).subscribe();

    await vi.waitFor(() => expect(api.calls()).toBe(2));
  });

  it('makes a tenant switch with the refresh token another tab rotated to, never the spent one', async () => {
    const token = freshToken();
    await tokenRotationChannel.publish(token, {
      authnResult: { access_token: 'from-other-tab', refresh_token: 'rotated', expires_in: 120 },
    });
    const sent: HttpRequest<unknown>[] = [];
    const handler = (req: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => { sent.push(req); return of(tokenResponse()); };

    tokenRefreshRetryInterceptor(switchRequest(token, 'tenant-b'), handler).subscribe();

    await vi.waitFor(() => expect(sent).toHaveLength(1));
    const form = new URLSearchParams(sent[0].body as string);
    expect(form.get('refresh_token')).toBe('rotated');
    expect(form.get('tenant')).toBe('tenant-b');
  });

  it('adds a requested tenant switch to the next refresh only', async () => {
    const sent: HttpRequest<unknown>[] = [];
    const handler = (req: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => { sent.push(req); return of(tokenResponse()); };

    requestTenantSwitch('tenant-b');
    tokenRefreshRetryInterceptor(refreshRequest(freshToken()), handler).subscribe();
    tokenRefreshRetryInterceptor(refreshRequest(freshToken()), handler).subscribe();

    await vi.waitFor(() => expect(sent).toHaveLength(2));
    expect(new URLSearchParams(sent[0].body as string).get('tenant')).toBe('tenant-b');
    expect(new URLSearchParams(sent[1].body as string).has('tenant')).toBe(false);
  });

  it('answers a refused tenant switch with a plain refresh, so the session is never failed', async () => {
    const sent: HttpRequest<unknown>[] = [];
    const handler = (req: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => {
      sent.push(req);
      return new URLSearchParams(req.body as string).has('tenant')
        ? throwError(() => new HttpErrorResponse({ status: 400, url: TOKEN_URL, error: { error: 'access_denied' } }))
        : of(tokenResponse());
    };
    const answers: unknown[] = [];
    let error: unknown;

    tokenRefreshRetryInterceptor(switchRequest(freshToken(), 'tenant-b'), handler)
      .subscribe({ next: (e) => answers.push(e), error: (e) => (error = e) });

    await vi.waitFor(() => expect(answers).toHaveLength(1));
    expect(error).toBeUndefined();
    expect(sent).toHaveLength(2);
    expect(new URLSearchParams(sent[1].body as string).has('tenant')).toBe(false);
  });

  it('leaves other token requests and API calls alone', async () => {
    const code = backend([502]);
    let codeError: unknown;
    tokenRefreshRetryInterceptor(codeRequest(), code.handler).subscribe({ error: (e) => (codeError = e) });
    const api = backend([503]);
    let apiError: unknown;
    tokenRefreshRetryInterceptor(new HttpRequest('GET', 'https://api.example/api/workspace/me'), api.handler)
      .subscribe({ error: (e) => (apiError = e) });
    await vi.advanceTimersByTimeAsync(60_000);

    expect(code.calls()).toBe(1);
    expect((codeError as HttpErrorResponse).status).toBe(502);
    expect(api.calls()).toBe(1);
    expect((apiError as HttpErrorResponse).status).toBe(503);
  });
});

describe('refreshTokenOf', () => {
  it('reads the refresh token of a POSTed refresh_token grant only', () => {
    expect(refreshTokenOf(refreshRequest('abc'))).toBe('abc');
    expect(refreshTokenOf(new HttpRequest<string>('POST', TOKEN_URL, 'refresh_token=def&grant_type=refresh_token'))).toBe('def');
    expect(refreshTokenOf(codeRequest())).toBeNull();
    expect(refreshTokenOf(new HttpRequest<string>('POST', TOKEN_URL, 'grant_type=refresh_tokens&refresh_token=abc'))).toBeNull();
    expect(refreshTokenOf(new HttpRequest('GET', TOKEN_URL))).toBeNull();
  });
});

describe('refreshRetryDelayMs', () => {
  const error = (retryAfter?: string) =>
    new HttpErrorResponse({ status: 503, headers: retryAfter ? new HttpHeaders({ 'Retry-After': retryAfter }) : undefined });

  it('doubles from one second and stops at ten', () => {
    expect([1, 2, 3, 4, 5, 9].map((n) => refreshRetryDelayMs(error(), n))).toEqual([1000, 2000, 4000, 8000, 10000, 10000]);
  });

  it("follows the server's Retry-After seconds, capped at thirty", () => {
    expect(refreshRetryDelayMs(error('5'), 1)).toBe(5000);
    expect(refreshRetryDelayMs(error('120'), 1)).toBe(30000);
    expect(refreshRetryDelayMs(error('Wed, 21 Oct 2026 07:28:00 GMT'), 2)).toBe(2000);
  });
});

describe('tokenRefreshRetryInterceptor across tabs', () => {
  /** One Web Lock queue, enough for one lock name. */
  class FakeLocks {
    private tail: Promise<void> = Promise.resolve();
    private held = false;
    async request<T>(name: string, optionsOrWork: unknown, maybeWork?: (lock: unknown) => Promise<T>): Promise<T> {
      const options = (typeof optionsOrWork === 'function' ? {} : optionsOrWork) as { ifAvailable?: boolean };
      const work = (typeof optionsOrWork === 'function' ? optionsOrWork : maybeWork) as (lock: unknown) => Promise<T>;
      if (options.ifAvailable && this.held) return work(null);
      const previous = this.tail;
      let release!: () => void;
      this.tail = new Promise<void>((r) => (release = r));
      await previous;
      this.held = true;
      try {
        return await work({ name });
      } finally {
        this.held = false;
        release();
      }
    }
  }

  let locks: FakeLocks;
  beforeEach(() => {
    locks = new FakeLocks();
    Object.defineProperty(globalThis.navigator, 'locks', { value: locks, configurable: true });
  });
  afterEach(() => {
    Object.defineProperty(globalThis.navigator, 'locks', { value: undefined, configurable: true });
  });

  it('waits for the tab that is refreshing and takes the tokens it rotated to', async () => {
    const token = freshToken();
    let releaseOtherTab!: () => void;
    void locks.request('oluso.token-refresh:workspace-portal', () => new Promise<void>((r) => (releaseOtherTab = r)));
    const api = backend([200]);
    const answers: unknown[] = [];
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe((e) => answers.push(e));
    await new Promise((r) => setTimeout(r, 20));
    expect(api.calls()).toBe(0);

    const otherTab = new TokenRotationChannel();
    await otherTab.publish(token, { authnResult: { access_token: 'other-tab', refresh_token: 'rotated', expires_in: 120 } });
    releaseOtherTab();
    await vi.waitFor(() => expect(answers).toHaveLength(1));

    expect(api.calls()).toBe(0);
    expect(bodyOf(answers[0]).access_token).toBe('other-tab');
  });

  it('sends the refresh itself when the tab it waited for redeemed a different token', async () => {
    const token = freshToken();
    let releaseOtherTab!: () => void;
    void locks.request('oluso.token-refresh:workspace-portal', () => new Promise<void>((r) => (releaseOtherTab = r)));
    const api = backend([200]);
    const answers: unknown[] = [];
    tokenRefreshRetryInterceptor(refreshRequest(token), api.handler).subscribe((e) => answers.push(e));
    await new Promise((r) => setTimeout(r, 20));
    releaseOtherTab();
    await vi.waitFor(() => expect(answers).toHaveLength(1), { timeout: 3000 });

    expect(api.calls()).toBe(1);
  });
});

import { HttpErrorResponse, HttpEvent, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { Observable, ReplaySubject, catchError, filter, lastValueFrom, throwError, timer } from 'rxjs';
import { map, retry } from 'rxjs/operators';
import { ROTATION_ANSWER_TTL_MS, tokenRotationChannel, type RotatedTokens, type TokenRotationChannel } from '../auth/token-rotation-channel';

/**
 * Statuses meaning the token endpoint was briefly unavailable — a proxy or load balancer answering while the
 * server restarts or deploys, a gateway timeout, or throttling — rather than refusing the grant.
 */
const TRANSIENT_STATUSES = new Set([408, 429, 502, 503, 504]);

/** Longest wait between retries of our own choosing; short, so a renew follows the server back promptly. */
const MAX_BACKOFF_MS = 10_000;

/** Longest Retry-After honoured. */
const MAX_RETRY_AFTER_MS = 30_000;

/** How long a tab that has just finished a refresh may take to publish its rotation before another tab gives up waiting. */
const ROTATION_SETTLE_MS = 500;

/** How long a refresh keeps other tabs waiting for its rotation to be stored and published. */
const PUBLISH_TIMEOUT_MS = 10_000;

interface RefreshExchange {
  readonly events$: Observable<HttpEvent<unknown>>;
  receivedAt: number | null;
}

/** Refresh exchanges in this tab by the refresh token they redeem, while running and for a while after they answer. */
const exchanges = new Map<string, RefreshExchange>();

/**
 * Makes each refresh token redeem exactly once across all of the app's tabs, and keeps that redemption alive
 * through a brief server outage.
 *
 * Refresh tokens are single use. Three things would otherwise present one twice:
 * - the OIDC client abandons a renew it considers slow (its periodic check cancels the request every few
 *   seconds) and later starts another with the same token;
 * - in one tab, a 401 and the background renew can both ask for a refresh;
 * - tabs can hold the same token (a duplicated tab copies its opener's session; remembered sessions share one
 *   store), and each renews on its own timer.
 * So one exchange runs per refresh token whether or not its caller still listens, refreshes are taken in turn
 * across tabs under a Web Lock, and a request for a token another exchange already redeemed — here or in
 * another tab ({@link tokenRotationChannel}) — is answered with what it was exchanged for, its lifetime
 * counted from when the server issued it.
 *
 * The client also deletes the session on any HTTP error, so a 502 from the proxy while the API restarts would
 * sign the user out although the refresh token is still good. Transient statuses are retried after the
 * server's Retry-After (up to 30 s), else 1 s doubling to 10 s; a refusal such as invalid_grant still reaches
 * the client.
 */
export const tokenRefreshRetryInterceptor: HttpInterceptorFn = (req, next) => {
  const refreshToken = refreshTokenOf(req);
  if (refreshToken === null) return next(req);

  if (pendingTenantSwitch !== null) {
    req = withTenant(req, pendingTenantSwitch);
    pendingTenantSwitch = null;
  }

  // A tenant switch is its own request: another exchange of the same refresh token answered for the tenant
  // the session was in, which is not what the switch asked for.
  const key = exchangeKey(req, refreshToken);
  let exchange = exchanges.get(key);
  if (!exchange) {
    exchange = startExchange(req, refreshToken, key, (r) => next(r), tokenRotationChannel);
    exchanges.set(key, exchange);
  }

  const current = exchange;
  return current.events$.pipe(
    map((event) => (event instanceof HttpResponse && current.receivedAt !== null ? aged(event, Date.now() - current.receivedAt) : event))
  );
};

function startExchange(
  req: HttpRequest<unknown>,
  refreshToken: string,
  key: string,
  send: (req: HttpRequest<unknown>) => Observable<HttpEvent<unknown>>,
  rotations: TokenRotationChannel
): RefreshExchange {
  const events$ = new ReplaySubject<HttpEvent<unknown>>(1);
  const exchange: RefreshExchange = { events$, receivedAt: null };
  const answer = (response: HttpResponse<unknown>) => {
    exchange.receivedAt = Date.now();
    events$.next(response);
    events$.complete();
  };

  void withRefreshLock(clientIdOf(req), async (waited) => {
    const handedOver = waited
      ? await rotations.waitForAnswer(refreshToken, ROTATION_SETTLE_MS)
      : await rotations.answerFor(refreshToken);
    let outgoing = req;
    if (handedOver) {
      // The refresh token was already redeemed. A plain refresh takes what it was exchanged for; a tenant
      // switch is made with the refresh token that exchange left, since redeeming a spent one is refused.
      const rotated = tenantOf(req) ? refreshTokenFrom(handedOver) : null;
      if (!rotated) {
        answer(tokenResponseFrom(handedOver));
        return;
      }
      outgoing = withRefreshToken(req, rotated);
    }
    const exchangeOnce = (request: HttpRequest<unknown>) =>
      send(request).pipe(
        retry({
          delay: (error, attempt) =>
            isTransient(error) ? timer(refreshRetryDelayMs(error, attempt)) : throwError(() => error),
        }),
        filter((event): event is HttpResponse<unknown> => event instanceof HttpResponse)
      );
    // A refused tenant switch leaves the refresh token unspent, so the session refreshes where it is instead:
    // the client signs the user out on any failed refresh, and a switch that is not allowed must not do that.
    const response = await lastValueFrom(
      exchangeOnce(outgoing).pipe(
        catchError((error: unknown) =>
          tenantOf(outgoing) && isRefusal(error) ? exchangeOnce(withoutTenant(outgoing)) : throwError(() => error)
        )
      )
    );
    answer(response);
    await rotations.awaitPublished(refreshToken, PUBLISH_TIMEOUT_MS);
  }).then(
    () => setTimeout(() => exchanges.delete(key), ROTATION_ANSWER_TTL_MS),
    (error) => {
      exchanges.delete(key);
      events$.error(error);
    }
  );

  return exchange;
}

/**
 * Runs `work` holding the app-wide refresh lock for `clientId`, telling it whether it had to wait for another
 * tab. Browsers without Web Locks run it unserialised, which leaves only this tab's own exchanges deduplicated.
 */
async function withRefreshLock(clientId: string, work: (waited: boolean) => Promise<void>): Promise<void> {
  const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
  if (!locks) return work(false);
  const name = `oluso.token-refresh:${clientId}`;
  const ranImmediately = await locks.request(name, { ifAvailable: true }, async (lock) => {
    if (!lock) return false;
    await work(false);
    return true;
  });
  if (!ranImmediately) await locks.request(name, () => work(true));
}

/** The refresh token a token request redeems, or null for any other request. */
export function refreshTokenOf(req: HttpRequest<unknown>): string | null {
  if (req.method !== 'POST' || typeof req.body !== 'string') return null;
  const form = new URLSearchParams(req.body);
  return form.get('grant_type') === 'refresh_token' ? form.get('refresh_token') : null;
}

/** The tenant the next refresh switches to, set for one request by {@link requestTenantSwitch}. */
let pendingTenantSwitch: string | null = null;

/**
 * Makes the next refresh a switch to `tenantId` (the token endpoint's `tenant` parameter), or cancels a switch
 * not yet sent. The parameter rides that one request only: the auth library never holds it, so later refreshes
 * carry none (the refresh token remembers the tenant), and a refused switch never reaches the library as a
 * failure — it is answered by a plain refresh instead.
 */
export function requestTenantSwitch(tenantId: string | null): void {
  pendingTenantSwitch = tenantId;
}

/** The tenant a refresh asks to switch to (the token endpoint's `tenant` parameter), or null. */
function tenantOf(req: HttpRequest<unknown>): string | null {
  return new URLSearchParams(req.body as string).get('tenant');
}

function exchangeKey(req: HttpRequest<unknown>, refreshToken: string): string {
  const tenant = tenantOf(req);
  return tenant ? `${refreshToken}\ntenant:${tenant}` : refreshToken;
}

/** The refresh token another exchange left in storage, from the tokens it handed over. */
function refreshTokenFrom(tokens: RotatedTokens): string | null {
  const token = (tokens['authnResult'] as { refresh_token?: unknown } | undefined)?.refresh_token;
  return typeof token === 'string' && token ? token : null;
}

function withTenant(req: HttpRequest<unknown>, tenantId: string): HttpRequest<unknown> {
  const form = new URLSearchParams(req.body as string);
  form.set('tenant', tenantId);
  return req.clone({ body: form.toString() });
}

function withoutTenant(req: HttpRequest<unknown>): HttpRequest<unknown> {
  const form = new URLSearchParams(req.body as string);
  form.delete('tenant');
  return req.clone({ body: form.toString() });
}

/** The token endpoint turning the request down (invalid_request, access_denied), as opposed to failing. */
function isRefusal(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 400;
}

function withRefreshToken(req: HttpRequest<unknown>, refreshToken: string): HttpRequest<unknown> {
  const form = new URLSearchParams(req.body as string);
  form.set('refresh_token', refreshToken);
  return req.clone({ body: form.toString() });
}

function clientIdOf(req: HttpRequest<unknown>): string {
  return new URLSearchParams(req.body as string).get('client_id') ?? '';
}

/** Wait before retry number `attempt` (1-based): the server's Retry-After seconds when it sends them (at most
 * 30 s), else 1 s doubling to 10 s. */
export function refreshRetryDelayMs(error: HttpErrorResponse, attempt: number): number {
  const retryAfterSeconds = Number(error.headers?.get('Retry-After'));
  return Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0
    ? Math.min(retryAfterSeconds * 1000, MAX_RETRY_AFTER_MS)
    : Math.min(1000 * 2 ** (attempt - 1), MAX_BACKOFF_MS);
}

/** The token response a refresh produced, rebuilt from the tokens it left in storage. */
function tokenResponseFrom(tokens: RotatedTokens): HttpResponse<unknown> {
  const authnResult = (tokens['authnResult'] ?? {}) as Record<string, unknown>;
  const expiresAt = Number(tokens['access_token_expires_at']);
  const body = Number.isFinite(expiresAt) && expiresAt > 0
    ? { ...authnResult, expires_in: Math.max(Math.floor((expiresAt - Date.now()) / 1000), 1) }
    : authnResult;
  return new HttpResponse({ status: 200, body });
}

/** A token response handed out `elapsedMs` after it arrived: its lifetimes count from when the server issued it. */
function aged(response: HttpResponse<unknown>, elapsedMs: number): HttpResponse<unknown> {
  const body = response.body as { expires_in?: unknown } | null;
  const elapsedSeconds = Math.floor(elapsedMs / 1000);
  if (elapsedSeconds <= 0 || !body || typeof body.expires_in !== 'number') return response;
  return response.clone({ body: { ...body, expires_in: Math.max(body.expires_in - elapsedSeconds, 1) } });
}

function isTransient(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && TRANSIENT_STATUSES.has(error.status);
}

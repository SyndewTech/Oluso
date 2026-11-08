import { HttpErrorResponse, HttpEvent, HttpHeaders, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { dpopKey, dpopSupported } from './dpop-key';
import { createDPoPProof, proofUri } from './dpop-proof';

/** Nonces servers have asked proofs to carry (RFC 9449 §8), by origin. */
const nonces = new Map<string, string>();

/**
 * Sender-constrains the app's tokens with DPoP (RFC 9449).
 *
 * - Token requests (any grant) carry a proof, so the server binds the tokens it issues — access and refresh —
 *   to this browser's key ({@link dpopKey}); a copied token is useless without it.
 * - A request presenting a DPoP-bound access token (one whose <c>cnf.jkt</c> names a key) is sent under the
 *   DPoP scheme with a proof for that request and token instead of as a bearer token, which the server refuses.
 * - When a server asks for a nonce (<c>use_dpop_nonce</c>), the request is retried once with it.
 *
 * Register it after the interceptors that attach the access token, so it sees the final request.
 * Pages without Web Crypto (non-secure contexts) send requests unchanged; their tokens are not bound.
 */
export const dpopInterceptor: HttpInterceptorFn = (req, next) => {
  if (!dpopSupported()) return next(req);
  // A token request presents no access token. The auth library attaches the session's token to every URL under its
  // secure routes, the token endpoint included; sent there, the server's resource check consumes the proof's jti and
  // the token endpoint then refuses the same proof as replayed. Client authentication (Basic) is left alone.
  if (isTokenRequest(req) && /^(Bearer|DPoP)\s/i.test(req.headers.get('Authorization') ?? '')) {
    req = req.clone({ headers: req.headers.delete('Authorization') });
  }
  const presented = boundAccessTokenOf(req);
  if (!presented && !isTokenRequest(req)) return next(req);

  const send = (nonce: string | undefined): Observable<HttpEvent<unknown>> =>
    from(withProof(req, presented, nonce)).pipe(
      switchMap((signed) => next(signed)),
      tap((event) => { if (event instanceof HttpResponse) rememberNonce(req, event.headers); })
    );

  return send(nonces.get(originOf(req.url))).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);
      const nonce = error.headers?.get('DPoP-Nonce');
      if (nonce) nonces.set(originOf(req.url), nonce);
      return nonce && asksForNonce(error) ? send(nonce) : throwError(() => error);
    })
  );
};

async function withProof(req: HttpRequest<unknown>, boundToken: string | null, nonce: string | undefined): Promise<HttpRequest<unknown>> {
  const proof = await createDPoPProof(await dpopKey(), { method: req.method, url: req.url, accessToken: boundToken, nonce });
  let headers: HttpHeaders = req.headers.set('DPoP', proof);
  if (boundToken) headers = headers.set('Authorization', `DPoP ${boundToken}`);
  return req.clone({ headers });
}

/** A token endpoint request: a form POST naming a grant. */
function isTokenRequest(req: HttpRequest<unknown>): boolean {
  return req.method === 'POST' && typeof req.body === 'string' && new URLSearchParams(req.body).has('grant_type');
}

/** The access token a request presents, when it is bound to a DPoP key; else null. */
export function boundAccessTokenOf(req: HttpRequest<unknown>): string | null {
  const match = /^(?:Bearer|DPoP)\s+(.+)$/i.exec(req.headers.get('Authorization') ?? '');
  const token = match?.[1]?.trim();
  return token && isDPoPBound(token) ? token : null;
}

let lastChecked: { token: string; bound: boolean } | null = null;

/** Whether a JWT access token is bound to a DPoP key (has <c>cnf.jkt</c>). */
export function isDPoPBound(token: string): boolean {
  if (lastChecked?.token === token) return lastChecked.bound;
  let bound = false;
  const part = token.split('.')[1];
  if (part) {
    try {
      const json = atob(part.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(part.length / 4) * 4, '='));
      const jkt = (JSON.parse(json) as { cnf?: { jkt?: unknown } })?.cnf?.jkt;
      bound = typeof jkt === 'string' && jkt.length > 0;
    } catch {
      bound = false;
    }
  }
  lastChecked = { token, bound };
  return bound;
}

/** A refusal asking for a DPoP nonce: a 401 challenge from an API or a 400 error from the token endpoint. */
function asksForNonce(error: HttpErrorResponse): boolean {
  if (error.status === 401) return (error.headers?.get('WWW-Authenticate') ?? '').includes('use_dpop_nonce');
  return error.status === 400 && (error.error as { error?: unknown } | null)?.error === 'use_dpop_nonce';
}

function rememberNonce(req: HttpRequest<unknown>, headers: HttpHeaders): void {
  const nonce = headers.get('DPoP-Nonce');
  if (nonce) nonces.set(originOf(req.url), nonce);
}

function originOf(url: string): string {
  return new URL(proofUri(url)).origin;
}

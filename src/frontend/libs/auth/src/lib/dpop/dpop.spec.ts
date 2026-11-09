import { HttpErrorResponse, HttpHeaders, HttpRequest, HttpResponse, type HttpEvent, type HttpHandlerFn } from '@angular/common/http';
import { Observable, firstValueFrom, of, throwError } from 'rxjs';
import { dpopKey } from './dpop-key';
import { accessTokenHash, createDPoPProof, proofUri } from './dpop-proof';
import { dpopInterceptor, isDPoPBound } from './dpop.interceptor';

const API = 'https://api.example/api/workspace/me?x=1';
const TOKEN_URL = 'https://idp.example/connect/token';

function jwt(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${b64({ alg: 'RS256' })}.${b64(payload)}.sig`;
}

function decode(part: string): Record<string, unknown> {
  return JSON.parse(atob(part.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(part.length / 4) * 4, '=')));
}

function bytes(part: string): Uint8Array<ArrayBuffer> {
  return Uint8Array.from(atob(part.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(part.length / 4) * 4, '=')), (c) => c.charCodeAt(0));
}

/** Records the requests that reach the server and answers them in turn. */
function server(...answers: Array<(req: HttpRequest<unknown>) => Observable<HttpEvent<unknown>>>) {
  const seen: HttpRequest<unknown>[] = [];
  const handler: HttpHandlerFn = (req) => {
    seen.push(req);
    return (answers[Math.min(seen.length - 1, answers.length - 1)] ?? (() => of(new HttpResponse({ status: 200 }))))(req);
  };
  return { handler, seen };
}

const ok = () => of(new HttpResponse({ status: 200 }));

describe('createDPoPProof', () => {
  it('signs a proof for one request with the page key, naming the token it presents', async () => {
    const key = await dpopKey();
    const proof = await createDPoPProof(key, { method: 'get', url: API, accessToken: 'the-token', nonce: 'n-1' });
    const [header, payload, signature] = proof.split('.');

    expect(decode(header)).toEqual({ typ: 'dpop+jwt', alg: 'ES256', jwk: key.publicJwk });
    expect(decode(payload)).toMatchObject({ htm: 'GET', htu: 'https://api.example/api/workspace/me', ath: await accessTokenHash('the-token'), nonce: 'n-1' });
    expect(decode(payload)['jti']).toEqual(expect.any(String));

    const publicKey = await crypto.subtle.importKey('jwk', { ...key.publicJwk }, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['verify']);
    const valid = await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, publicKey, bytes(signature),
      new TextEncoder().encode(`${header}.${payload}`));
    expect(valid).toBe(true);
  });

  it('uses one key per page and a fresh jti per proof', async () => {
    const key = await dpopKey();
    expect(await dpopKey()).toBe(key);
    const a = decode((await createDPoPProof(key, { method: 'GET', url: API })).split('.')[1]);
    const b = decode((await createDPoPProof(key, { method: 'GET', url: API })).split('.')[1]);
    expect(a['jti']).not.toEqual(b['jti']);
    expect(a).not.toHaveProperty('ath');
  });

  it('names the request without its query or fragment', () => {
    expect(proofUri('https://api.example/a/b?c=1#d')).toBe('https://api.example/a/b');
  });
});

describe('dpopInterceptor', () => {
  const bound = jwt({ sub: 'u1', cnf: { jkt: 'thumb' } });
  const unbound = jwt({ sub: 'u1' });

  it('presents a bound token under the DPoP scheme with a proof for the request and token', async () => {
    const api = server(ok);
    const req = new HttpRequest('GET', API, { headers: new HttpHeaders({ Authorization: `Bearer ${bound}` }) });

    await firstValueFrom(dpopInterceptor(req, api.handler));

    const sent = api.seen[0];
    expect(sent.headers.get('Authorization')).toBe(`DPoP ${bound}`);
    const payload = decode(sent.headers.get('DPoP')!.split('.')[1]);
    expect(payload).toMatchObject({ htm: 'GET', htu: 'https://api.example/api/workspace/me', ath: await accessTokenHash(bound) });
  });

  it('leaves an unbound bearer token and requests without a token alone', async () => {
    const api = server(ok);
    await firstValueFrom(dpopInterceptor(new HttpRequest('GET', API, { headers: new HttpHeaders({ Authorization: `Bearer ${unbound}` }) }), api.handler));
    await firstValueFrom(dpopInterceptor(new HttpRequest('GET', API), api.handler));

    expect(api.seen[0].headers.get('Authorization')).toBe(`Bearer ${unbound}`);
    expect(api.seen.every((r) => !r.headers.has('DPoP'))).toBe(true);
  });

  it('adds a proof to every token request so the tokens are bound', async () => {
    const api = server(ok);
    for (const grant of ['authorization_code', 'refresh_token']) {
      await firstValueFrom(dpopInterceptor(new HttpRequest('POST', TOKEN_URL, `grant_type=${grant}&client_id=c`), api.handler));
    }
    // The auth library attaches the session's (bound) access token to the token endpoint too; it is not presented there.
    const bound = jwt({ sub: 'u', cnf: { jkt: 'thumb' } });
    await firstValueFrom(dpopInterceptor(new HttpRequest('POST', TOKEN_URL, 'grant_type=refresh_token&client_id=c',
      { headers: new HttpHeaders({ Authorization: `Bearer ${bound}` }) }), api.handler));

    for (const sent of api.seen) {
      expect(sent.headers.has('Authorization')).toBe(false);
      const payload = decode(sent.headers.get('DPoP')!.split('.')[1]);
      expect(payload).toMatchObject({ htm: 'POST', htu: TOKEN_URL });
      expect(payload).not.toHaveProperty('ath');
    }
  });

  it("retries once with the server's nonce, at the API and at the token endpoint, and keeps using it", async () => {
    const challenge = (status: number) => () => throwError(() => new HttpErrorResponse({
      status,
      url: API,
      headers: new HttpHeaders(status === 401
        ? { 'WWW-Authenticate': 'DPoP error="use_dpop_nonce"', 'DPoP-Nonce': `nonce-${status}` }
        : { 'DPoP-Nonce': `nonce-${status}` }),
      error: status === 400 ? { error: 'use_dpop_nonce' } : null,
    }));

    const api = server(challenge(401), ok, ok);
    const apiReq = () => new HttpRequest('GET', API, { headers: new HttpHeaders({ Authorization: `Bearer ${bound}` }) });
    await firstValueFrom(dpopInterceptor(apiReq(), api.handler));
    await firstValueFrom(dpopInterceptor(apiReq(), api.handler));
    expect(api.seen).toHaveLength(3);
    expect(decode(api.seen[1].headers.get('DPoP')!.split('.')[1])['nonce']).toBe('nonce-401');
    expect(decode(api.seen[2].headers.get('DPoP')!.split('.')[1])['nonce']).toBe('nonce-401');

    const token = server(challenge(400), ok);
    await firstValueFrom(dpopInterceptor(new HttpRequest('POST', 'https://other.example/connect/token', 'grant_type=refresh_token'), token.handler));
    expect(token.seen).toHaveLength(2);
    expect(decode(token.seen[1].headers.get('DPoP')!.split('.')[1])['nonce']).toBe('nonce-400');
  });

  it('passes other refusals through without retrying', async () => {
    const api = server(() => throwError(() => new HttpErrorResponse({ status: 401, url: API, headers: new HttpHeaders({ 'WWW-Authenticate': 'DPoP error="invalid_token"' }) })));
    const req = new HttpRequest('GET', 'https://third.example/x', { headers: new HttpHeaders({ Authorization: `Bearer ${bound}` }) });

    await expect(firstValueFrom(dpopInterceptor(req, api.handler))).rejects.toBeInstanceOf(HttpErrorResponse);
    expect(api.seen).toHaveLength(1);
  });
});

describe('isDPoPBound', () => {
  it('reads cnf.jkt from the token', () => {
    expect(isDPoPBound(jwt({ cnf: { jkt: 'k' } }))).toBe(true);
    expect(isDPoPBound(jwt({ cnf: { 'x5t#S256': 'c' } }))).toBe(false);
    expect(isDPoPBound('opaque-reference-token')).toBe(false);
  });
});

import type { DPoPKey } from './dpop-key';

/** What a DPoP proof (RFC 9449 §4.2) is for: one HTTP request, and the access token it presents if any. */
export interface DPoPProofTarget {
  method: string;
  url: string;
  accessToken?: string | null;
  nonce?: string | null;
}

/** A DPoP proof JWT signed by `key` for one request. */
export async function createDPoPProof(key: DPoPKey, target: DPoPProofTarget): Promise<string> {
  const header = { typ: 'dpop+jwt', alg: 'ES256', jwk: key.publicJwk };
  const payload: Record<string, unknown> = {
    jti: crypto.randomUUID(),
    htm: target.method.toUpperCase(),
    htu: proofUri(target.url),
    iat: Math.floor(Date.now() / 1000),
  };
  if (target.accessToken) payload['ath'] = await accessTokenHash(target.accessToken);
  if (target.nonce) payload['nonce'] = target.nonce;

  const signingInput = `${base64UrlText(JSON.stringify(header))}.${base64UrlText(JSON.stringify(payload))}`;
  const signature = await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' }, key.privateKey, new TextEncoder().encode(signingInput));
  return `${signingInput}.${base64UrlBytes(new Uint8Array(signature))}`;
}

/** The request URI a proof names: absolute, without query or fragment. */
export function proofUri(url: string): string {
  const base = typeof location !== 'undefined' ? location.href : 'http://localhost/';
  const absolute = new URL(url, base);
  return `${absolute.origin}${absolute.pathname}`;
}

/** The <c>ath</c> claim: the base64url SHA-256 of the access token. */
export async function accessTokenHash(accessToken: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(accessToken));
  return base64UrlBytes(new Uint8Array(digest));
}

function base64UrlText(text: string): string {
  return base64UrlBytes(new TextEncoder().encode(text));
}

function base64UrlBytes(bytes: Uint8Array): string {
  let binary = '';
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

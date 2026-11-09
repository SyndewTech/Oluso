/**
 * The browser's DPoP key (RFC 9449): an ECDSA P-256 key pair whose private half the page can sign with but
 * never read, so a token bound to it cannot be used anywhere else.
 *
 * The pair is kept in IndexedDB, shared by every tab of the app and surviving reloads, because refresh tokens
 * are bound to it: a remembered session's next refresh needs the same key. Where IndexedDB is unavailable the
 * key lives for the page only, and the session ends with it.
 */

/** The public half, as it goes in a proof's <c>jwk</c> header. */
export interface DPoPPublicJwk {
  kty: 'EC';
  crv: 'P-256';
  x: string;
  y: string;
}

export interface DPoPKey {
  readonly privateKey: CryptoKey;
  readonly publicJwk: DPoPPublicJwk;
}

const DATABASE = 'oluso.dpop';
const STORE = 'keys';
const KEY_ID = 'current';

let current: Promise<DPoPKey> | null = null;

/** Whether this page can hold a DPoP key: Web Crypto, which browsers offer only in secure contexts. */
export function dpopSupported(): boolean {
  const secure = typeof globalThis.isSecureContext === 'boolean' ? globalThis.isSecureContext : true;
  return secure && typeof crypto !== 'undefined' && !!crypto.subtle;
}

/** The app's DPoP key, loaded or created once per page. */
export function dpopKey(): Promise<DPoPKey> {
  current ??= loadOrCreate().catch((error) => {
    current = null;
    throw error;
  });
  return current;
}

/** Forgets the key held by this page, so the next {@link dpopKey} reads storage again (tests). */
export function resetDPoPKeyForTests(): void {
  current = null;
}

async function loadOrCreate(): Promise<DPoPKey> {
  const generated = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign', 'verify']);
  const pair = (await keepFirst(generated)) ?? generated;
  return { privateKey: pair.privateKey, publicJwk: await publicJwkOf(pair.publicKey) };
}

/**
 * Stores `candidate` unless a pair is already stored, in one transaction so two tabs creating a key at once
 * agree on a single one. Returns the stored pair, or null when IndexedDB is unavailable.
 */
function keepFirst(candidate: CryptoKeyPair): Promise<CryptoKeyPair | null> {
  if (typeof indexedDB === 'undefined') return Promise.resolve(null);
  return new Promise((resolve) => {
    const open = indexedDB.open(DATABASE, 1);
    open.onupgradeneeded = () => open.result.createObjectStore(STORE);
    open.onerror = () => resolve(null);
    open.onsuccess = () => {
      const db = open.result;
      const tx = db.transaction(STORE, 'readwrite');
      const store = tx.objectStore(STORE);
      let kept: CryptoKeyPair | null = null;
      const existing = store.get(KEY_ID);
      existing.onsuccess = () => {
        if (existing.result) {
          kept = existing.result as CryptoKeyPair;
        } else {
          store.put(candidate, KEY_ID);
          kept = candidate;
        }
      };
      tx.oncomplete = () => { db.close(); resolve(kept); };
      tx.onerror = () => { db.close(); resolve(null); };
    };
  });
}

async function publicJwkOf(publicKey: CryptoKey): Promise<DPoPPublicJwk> {
  const jwk = await crypto.subtle.exportKey('jwk', publicKey);
  return { kty: 'EC', crv: 'P-256', x: jwk.x!, y: jwk.y! };
}

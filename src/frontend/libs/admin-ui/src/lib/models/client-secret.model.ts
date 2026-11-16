export interface ClientSecret {
  id: number;
  description?: string;
  expiration?: string;
  type: string;
  created: string;
  lastThreeChars?: string;
  /** A public key's identifier: a JWK's kid, a certificate's SHA-256 thumbprint. */
  keyId?: string;
}

/** The secret types holding public key material rather than a shared secret. */
export const PUBLIC_KEY_SECRET_TYPES = ['JsonWebKey', 'X509CertificateBase64'];

/** A public key or certificate for the client to sign its JWTs with, or for self-signed mutual TLS. */
export interface AddClientKeyRequest {
  type: 'jwk' | 'certificate';
  value: string;
  description?: string;
  expiration?: string;
}

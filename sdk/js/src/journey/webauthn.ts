/** WebAuthn for journey steps: options arrive and results leave as base64url, the way the server reads them. */

export function base64UrlToBuffer(value: string): ArrayBuffer {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

export function bufferToBase64Url(buffer: ArrayBuffer | null | undefined): string | null {
  if (!buffer) return null;
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

type Json = Record<string, unknown>;

function credentialDescriptors(list: unknown): PublicKeyCredentialDescriptor[] | undefined {
  if (!Array.isArray(list)) return undefined;
  return list.map((c: Json) => ({
    type: (c.type as PublicKeyCredentialType) ?? 'public-key',
    id: base64UrlToBuffer(String(c.id)),
    transports: c.transports as AuthenticatorTransport[] | undefined,
  }));
}

export function requestOptions(publicKey: Json): PublicKeyCredentialRequestOptions {
  return {
    ...(publicKey as object),
    challenge: base64UrlToBuffer(String(publicKey.challenge)),
    allowCredentials: credentialDescriptors(publicKey.allowCredentials),
  } as PublicKeyCredentialRequestOptions;
}

export function creationOptions(publicKey: Json): PublicKeyCredentialCreationOptions {
  const user = (publicKey.user ?? {}) as Json;
  return {
    ...(publicKey as object),
    challenge: base64UrlToBuffer(String(publicKey.challenge)),
    user: { ...(user as object), id: base64UrlToBuffer(String(user.id)) },
    excludeCredentials: credentialDescriptors(publicKey.excludeCredentials),
  } as PublicKeyCredentialCreationOptions;
}

/** A passkey sign-in, as the JSON the server reads. */
export async function getAssertion(publicKey: Json): Promise<string> {
  const credential = (await navigator.credentials.get({ publicKey: requestOptions(publicKey) })) as PublicKeyCredential | null;
  if (!credential) throw new Error('No passkey was chosen');
  const response = credential.response as AuthenticatorAssertionResponse;
  return JSON.stringify({
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: bufferToBase64Url(response.clientDataJSON),
      authenticatorData: bufferToBase64Url(response.authenticatorData),
      signature: bufferToBase64Url(response.signature),
      userHandle: bufferToBase64Url(response.userHandle),
    },
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
  });
}

/** A new passkey, as the JSON the server reads. */
export async function createCredential(publicKey: Json): Promise<string> {
  const credential = (await navigator.credentials.create({ publicKey: creationOptions(publicKey) })) as PublicKeyCredential | null;
  if (!credential) throw new Error('No passkey was created');
  const response = credential.response as AuthenticatorAttestationResponse;
  return JSON.stringify({
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: bufferToBase64Url(response.clientDataJSON),
      attestationObject: bufferToBase64Url(response.attestationObject),
      transports: typeof response.getTransports === 'function' ? response.getTransports() : undefined,
    },
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
  });
}

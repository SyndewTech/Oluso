import type { AxiosInstance } from 'axios';

export interface Fido2Credential {
  id: string;
  displayName?: string;
  createdAt: string;
  lastUsedAt?: string;
  authenticatorType: string;
  isBackedUp: boolean;
  isActive: boolean;
}

export interface Fido2UserSummary {
  userId: string;
  userName?: string;
  displayName?: string;
  email?: string;
  credentialCount: number;
  lastUsedAt?: string;
}

export interface Fido2Stats {
  totalCredentials: number;
  totalUsersWithCredentials: number;
  platformCredentials: number;
  crossPlatformCredentials: number;
  credentialsRegisteredLast30Days: number;
  credentialsUsedLast30Days: number;
}

let apiClientInstance: AxiosInstance | null = null;
let apiBaseOrigin: string = '';

export function setApiClient(client: AxiosInstance) {
  apiClientInstance = client;
  // Extract the origin from the axios baseURL to use for absolute paths
  const baseURL = client.defaults.baseURL || '';
  try {
    const url = new URL(baseURL, window.location.origin);
    apiBaseOrigin = url.origin;
  } catch {
    apiBaseOrigin = window.location.origin;
  }
}

function getApiClient(): AxiosInstance {
  if (!apiClientInstance) {
    throw new Error('API client not initialized. Call setApiClient first.');
  }
  return apiClientInstance;
}

/**
 * Make an absolute URL request (bypassing the axios baseURL).
 * Use this for endpoints that are NOT under /api/admin.
 */
function absoluteUrl(path: string): string {
  return `${apiBaseOrigin}${path}`;
}

// ============================================================================
// Admin API (for Admin UI)
// Paths are relative to /api/admin, so /fido2/stats => /api/admin/fido2/stats
// ============================================================================

export const fido2Api = {
  async getStats(): Promise<Fido2Stats> {
    // Backend route is /api/admin/fido2, so use /fido2/stats (not /fido2/admin/stats)
    const response = await getApiClient().get<Fido2Stats>('/fido2/stats');
    return response.data;
  },

  async getUsersWithCredentials(params?: {
    search?: string;
    page?: number;
    pageSize?: number;
  }): Promise<{ users: Fido2UserSummary[]; totalCount: number }> {
    const response = await getApiClient().get<{ users: Fido2UserSummary[]; totalCount: number }>(
      '/fido2/users',
      { params }
    );
    return response.data;
  },

  async getUserCredentials(userId: string): Promise<Fido2Credential[]> {
    const response = await getApiClient().get<Fido2Credential[]>(
      `/fido2/users/${userId}/credentials`
    );
    return response.data;
  },

  async deleteUserCredential(userId: string, credentialId: string): Promise<void> {
    await getApiClient().delete(`/fido2/users/${userId}/credentials/${credentialId}`);
  },
};

// ============================================================================
// Account API (for end-user self-service in Account UI)
// ============================================================================

export interface AccountPasskey {
  id: string;
  credentialId: string;
  name: string;
  authenticatorType?: string;
  createdAt: string;
  lastUsedAt?: string;
  isResidentKey: boolean;
}

export interface AccountPasskeyList {
  passkeys: AccountPasskey[];
  isEnabled: boolean;
  message?: string;
}

export interface PasskeyRegistrationStart {
  registrationId: string;
  options: WebAuthnRegistrationOptions;
}

export interface WebAuthnRegistrationOptions {
  challenge: string;
  rp: { id: string; name: string };
  user: { id: string; name: string; displayName: string };
  pubKeyCredParams?: Array<{ type: string; alg: number }>;
  timeout?: number;
  attestation?: string;
  authenticatorSelection?: {
    authenticatorAttachment?: string;
    residentKey?: string;
    userVerification?: string;
  };
  excludeCredentials?: Array<{
    type: string;
    id: string;
    transports?: string[];
  }>;
}

// Account API uses absolute URLs since /api/account is NOT under /api/admin
export const fido2AccountApi = {
  async getMyPasskeys(): Promise<AccountPasskeyList> {
    const response = await getApiClient().get<AccountPasskeyList>(absoluteUrl('/api/account/passkeys'));
    return response.data;
  },

  async startRegistration(options?: {
    authenticatorType?: string;
    requireDiscoverableCredential?: boolean;
  }): Promise<PasskeyRegistrationStart> {
    const response = await getApiClient().post<PasskeyRegistrationStart>(
      absoluteUrl('/api/account/passkeys/register/start'),
      options ?? { requireDiscoverableCredential: true }
    );
    return response.data;
  },

  async completeRegistration(
    registrationId: string,
    attestationResponse: string,
    name?: string
  ): Promise<{ success: boolean; credentialId?: string; message?: string }> {
    const response = await getApiClient().post(absoluteUrl('/api/account/passkeys/register/complete'), {
      registrationId,
      attestationResponse,
      name,
    });
    return response.data;
  },

  async updatePasskey(passkeyId: string, name: string): Promise<void> {
    await getApiClient().patch(absoluteUrl(`/api/account/passkeys/${passkeyId}`), { name });
  },

  async deletePasskey(passkeyId: string): Promise<void> {
    await getApiClient().delete(absoluteUrl(`/api/account/passkeys/${passkeyId}`));
  },
};

export default fido2Api;

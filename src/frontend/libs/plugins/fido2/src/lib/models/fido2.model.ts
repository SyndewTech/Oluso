/**
 * FIDO2 Credential model
 */
export interface Fido2Credential {
  id: string;
  displayName?: string;
  createdAt: string;
  lastUsedAt?: string;
  authenticatorType: string;
  isBackedUp: boolean;
  isActive: boolean;
}

/**
 * Summary of a user with FIDO2 credentials
 */
export interface Fido2UserSummary {
  userId: string;
  userName?: string;
  displayName?: string;
  email?: string;
  credentialCount: number;
  lastUsedAt?: string;
}

/**
 * FIDO2 statistics for the dashboard
 */
export interface Fido2Stats {
  totalCredentials: number;
  totalUsersWithCredentials: number;
  platformCredentials: number;
  crossPlatformCredentials: number;
  credentialsRegisteredLast30Days: number;
  credentialsUsedLast30Days: number;
}

/**
 * Paginated response for users with credentials
 */
export interface Fido2UsersResponse {
  users: Fido2UserSummary[];
  totalCount: number;
}

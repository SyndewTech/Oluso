export interface SecurityOverview {
  hasPassword: boolean;
  twoFactorEnabled: boolean;
  mfaEnabled: boolean;
  emailVerified: boolean;
  phoneNumberVerified: boolean;
  lastPasswordChange?: string;
  passkeyCount: number;
  /** Signed in through an external identity provider, which manages the password. */
  isExternalAccount: boolean;
}

export interface MfaStatus {
  enabled: boolean;
  availableMethods: string[];
  enabledMethods: string[];
  hasTotp: boolean;
  hasSms: boolean;
  hasEmail: boolean;
}

export interface MfaSetupResponse {
  sharedKey?: string;
  qrCodeUri?: string;
  /** Server-rendered QR code (PNG data URI) for qrCodeUri. */
  qrCodeImage?: string;
  verificationCodeSent?: boolean;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface VerificationStatus {
  verified: boolean;
  value?: string;
  pendingVerification: boolean;
}

export interface ExternalLogin {
  provider: string;
  providerKey: string;
  displayName: string;
}

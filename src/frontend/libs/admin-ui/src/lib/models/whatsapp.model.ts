export interface WhatsAppAccount {
  phoneNumberId: string;
  businessAccountId?: string;
  displayPhoneNumber?: string;
  verifiedName?: string;
  qualityRating?: string;
  lastVerifiedAt?: string;
}

export interface WhatsAppSettings {
  enabled: boolean;
  hasAccessToken: boolean;
  hasAppSecret: boolean;
  phoneNumberId?: string;
  businessAccountId?: string;
  apiVersion?: string;
  defaultCountry?: string;
  webhookVerifyToken?: string;
  /** TenantSettings | Platform | null */
  effectiveSource?: string | null;
  isConfigured: boolean;
  webhookUrl?: string | null;
  account?: WhatsAppAccount | null;
}

export interface UpdateWhatsAppSettingsRequest {
  enabled: boolean;
  /** Blank keeps the stored value. */
  accessToken?: string;
  phoneNumberId?: string;
  businessAccountId?: string;
  /** Blank keeps the stored value. */
  appSecret?: string;
  webhookVerifyToken?: string;
  apiVersion?: string;
  defaultCountry?: string;
}

export interface WhatsAppTestConnectionResult {
  success: boolean;
  message?: string;
  account?: WhatsAppAccount | null;
}

export interface WhatsAppTemplate {
  id: string;
  name: string;
  language: string;
  category?: string;
  status: string;
  bodyText?: string;
  bodyParameterCount: number;
  headerFormat?: string;
  syncedAt: string;
}

export interface WhatsAppTemplateSyncResult {
  success: boolean;
  message?: string;
  count: number;
}

export interface WhatsAppContact {
  id: string;
  phoneNumber: string;
  displayName?: string;
  consentStatus: 'Unknown' | 'OptedIn' | 'OptedOut';
  consentSource?: string;
  consentAt?: string;
  lastInboundAt?: string;
  lastOutboundAt?: string;
  sessionOpen: boolean;
}

export interface WhatsAppContactList {
  items: WhatsAppContact[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface WhatsAppSendTestRequest {
  to: string;
  text?: string;
  templateName?: string;
  templateLanguage?: string;
  templateParameters?: string[];
}

export interface WhatsAppSendTestResult {
  success: boolean;
  messageId?: string;
  error?: string;
  normalizedPhone?: string;
}

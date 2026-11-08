/**
 * Outbound sender profile — one row per provider/credential set the tenant
 * has configured. Exactly one profile per tenant is the default; that one
 * is used by transactional / system mail. Other profiles are picked
 * explicitly (e.g. by campaign sends).
 *
 * Credentials are masked on read (the API returns "abcd••••••••wxyz"-style
 * placeholders for ApiKey / SmtpPassword). When editing, leaving the
 * masked value untouched preserves the existing secret server-side.
 */
export type EmailProvider = 'SendGrid' | 'Brevo' | 'Cloudflare' | 'Smtp';

export interface SenderProfile {
  id: string;
  name: string;
  provider: EmailProvider;
  fromAddress: string;
  fromName: string;
  replyToAddress?: string;
  isActive: boolean;
  isDefault: boolean;
  lastUsedAt?: string;
  lastTestSendAt?: string;
  lastTestError?: string;

  apiKey?: string;
  cloudflareAccountId?: string;
  smtpHost?: string;
  smtpPort: number;
  smtpUsername?: string;
  smtpPassword?: string;
  smtpUseSsl: boolean;
}

export const DEFAULT_SENDER_PROFILE: SenderProfile = {
  id: '',
  name: '',
  provider: 'SendGrid',
  fromAddress: '',
  fromName: '',
  isActive: true,
  isDefault: false,
  smtpPort: 587,
  smtpUseSsl: true,
};

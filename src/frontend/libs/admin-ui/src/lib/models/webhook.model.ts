/**
 * How a delivery proves it comes from Oluṣọ (backend `WebhookAuthType`, sent as its number).
 * Every type except IssuerSigned also carries the HMAC signature made with the endpoint's `whsec_` secret.
 */
export enum WebhookAuthType {
  /** HMAC signature with the shared secret only. */
  None = 0,
  /** Plus `Authorization: Bearer {credential}`. */
  BearerToken = 1,
  /** Plus `Authorization: Basic base64(username:password)`. */
  BasicAuth = 2,
  /** Plus `X-API-Key: {credential}`. */
  ApiKeyHeader = 3,
  /** A short-lived token signed with the tenant's issuer key; no shared secret. */
  IssuerSigned = 4,
}

/** The static credential types: a fixed value the receiver compares. */
export const STATIC_WEBHOOK_AUTH_TYPES: readonly WebhookAuthType[] = [
  WebhookAuthType.BearerToken,
  WebhookAuthType.BasicAuth,
  WebhookAuthType.ApiKeyHeader,
];

export const DEFAULT_WEBHOOK_API_VERSION = '2024-01-01';

export interface WebhookEventInfo {
  eventType: string;
  category: string;
  displayName: string;
  description?: string;
  enabledByDefault: boolean;
  providerId?: string;
  /** Subscribing needs `requiredPermission`; sensitive properties are withheld from unauthorised endpoints. */
  restricted?: boolean;
  requiredPermission?: string;
  classificationPurpose?: string;
  /** Payload properties withheld from endpoints not authorised for sensitive data. */
  sensitiveProperties?: string[];
  /** Whether the current administrator may subscribe to the event. */
  canSubscribe?: boolean;
}

export interface WebhookEventsResponse {
  events: WebhookEventInfo[];
  categories: string[];
}

export interface WebhookEndpointStats {
  totalDeliveries: number;
  successfulDeliveries: number;
  failedDeliveries: number;
  lastDeliveryAt?: string;
  lastSuccessAt?: string;
  lastFailureAt?: string;
}

export interface WebhookEndpoint {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  url: string;
  secret?: string;
  enabled: boolean;
  eventTypes: string[];
  headers?: Record<string, string>;
  /** How deliveries authenticate; the credential itself is never returned. */
  authType?: WebhookAuthType;
  apiVersion: string;
  /** OAuth client (client_id) of this tenant that owns the endpoint. */
  clientId?: string;
  /** Don't deliver events the linked client caused itself. */
  skipOwnClientEvents: boolean;
  /** When the endpoint was authorised to receive sensitive event data; absent when it isn't. */
  sensitiveDataAuthorizedAt?: string;
  sensitiveDataAuthorizedBy?: string;
  sensitiveDataAuthorizedByName?: string;
  createdAt: string;
  updatedAt?: string;
  stats?: WebhookEndpointStats;
}

export interface CreateWebhookEndpointRequest {
  name: string;
  description?: string;
  url: string;
  enabled?: boolean;
  eventTypes: string[];
  headers?: Record<string, string>;
  authType?: WebhookAuthType;
  /** Bearer token, `username:password` for Basic, or the API key. Write-only. */
  authCredential?: string;
  apiVersion?: string;
  clientId?: string;
  skipOwnClientEvents?: boolean;
  /** Send restricted events' sensitive properties; needs each restricted event's permission. */
  authorizeSensitiveData?: boolean;
}

export interface UpdateWebhookEndpointRequest {
  name?: string;
  description?: string;
  url?: string;
  enabled?: boolean;
  eventTypes?: string[];
  headers?: Record<string, string>;
  /** Omitted leaves the type unchanged. */
  authType?: WebhookAuthType;
  /** Omitted keeps the stored credential. */
  authCredential?: string;
  apiVersion?: string;
  /** Empty string unlinks the client; omitted leaves it unchanged. */
  clientId?: string;
  skipOwnClientEvents?: boolean;
  /** True authorises sensitive data, false revokes it, omitted leaves it unchanged. */
  authorizeSensitiveData?: boolean;
}

export type WebhookDeliveryStatus = 'Pending' | 'Success' | 'Failed' | 'Exhausted' | 'Cancelled';

export interface WebhookDelivery {
  id: string;
  endpointId: string;
  eventType: string;
  payloadId: string;
  payload?: string;
  status: WebhookDeliveryStatus;
  httpStatus?: number;
  responseBody?: string;
  errorMessage?: string;
  retryCount: number;
  nextRetryAt?: string;
  responseTimeMs?: number;
  createdAt: string;
  updatedAt?: string;
}

export interface RotateSecretResponse {
  secret: string;
}

export interface TestWebhookResponse {
  success: boolean;
  deliveryId?: string;
  error?: string;
}

export interface RetryDeliveryResponse {
  success: boolean;
}

export const WEBHOOK_DELIVERY_STATUS_LABELS: Record<number, string> = {
  0: 'Pending',
  1: 'Success',
  2: 'Failed',
  3: 'Exhausted',
  4: 'Cancelled',
};

export const WEBHOOK_DELIVERY_STATUS_COLORS: Record<number, string> = {
  0: 'yellow',
  1: 'green',
  2: 'red',
  3: 'gray',
  4: 'gray',
};

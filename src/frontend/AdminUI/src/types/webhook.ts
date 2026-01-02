export interface WebhookEventInfo {
  eventType: string;
  category: string;
  displayName: string;
  description?: string;
  enabledByDefault: boolean;
  providerId?: string;
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
  apiVersion: string;
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
  apiVersion?: string;
}

export interface UpdateWebhookEndpointRequest {
  name?: string;
  description?: string;
  url?: string;
  enabled?: boolean;
  eventTypes?: string[];
  headers?: Record<string, string>;
  apiVersion?: string;
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

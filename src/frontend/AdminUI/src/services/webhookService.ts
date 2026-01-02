import api from './api';
import type {
  WebhookEventsResponse,
  WebhookEndpoint,
  CreateWebhookEndpointRequest,
  UpdateWebhookEndpointRequest,
  WebhookDelivery,
  RotateSecretResponse,
  TestWebhookResponse,
  RetryDeliveryResponse,
  WebhookEventInfo,
} from '../types/webhook';

export const webhookService = {
  // Available Events
  async getAvailableEvents(): Promise<WebhookEventsResponse> {
    const response = await api.get<WebhookEventsResponse>('/webhooks/events');
    return response.data;
  },

  async getEventsGrouped(): Promise<Record<string, WebhookEventInfo[]>> {
    const response = await api.get<Record<string, WebhookEventInfo[]>>('/webhooks/events/grouped');
    return response.data;
  },

  // Webhook Endpoints
  async getEndpoints(): Promise<WebhookEndpoint[]> {
    const response = await api.get<WebhookEndpoint[]>('/webhooks/endpoints');
    return response.data;
  },

  async getEndpoint(endpointId: string): Promise<WebhookEndpoint> {
    const response = await api.get<WebhookEndpoint>(`/webhooks/endpoints/${endpointId}`);
    return response.data;
  },

  async createEndpoint(data: CreateWebhookEndpointRequest): Promise<WebhookEndpoint> {
    const response = await api.post<WebhookEndpoint>('/webhooks/endpoints', data);
    return response.data;
  },

  async updateEndpoint(endpointId: string, data: UpdateWebhookEndpointRequest): Promise<WebhookEndpoint> {
    const response = await api.put<WebhookEndpoint>(`/webhooks/endpoints/${endpointId}`, data);
    return response.data;
  },

  async deleteEndpoint(endpointId: string): Promise<void> {
    await api.delete(`/webhooks/endpoints/${endpointId}`);
  },

  async rotateSecret(endpointId: string): Promise<RotateSecretResponse> {
    const response = await api.post<RotateSecretResponse>(`/webhooks/endpoints/${endpointId}/rotate-secret`);
    return response.data;
  },

  async testEndpoint(endpointId: string): Promise<TestWebhookResponse> {
    const response = await api.post<TestWebhookResponse>(`/webhooks/endpoints/${endpointId}/test`);
    return response.data;
  },

  // Delivery Logs
  async getDeliveries(endpointId: string, limit: number = 50): Promise<WebhookDelivery[]> {
    const response = await api.get<WebhookDelivery[]>(`/webhooks/endpoints/${endpointId}/deliveries`, {
      params: { limit },
    });
    return response.data;
  },

  async getDelivery(deliveryId: string): Promise<WebhookDelivery> {
    const response = await api.get<WebhookDelivery>(`/webhooks/deliveries/${deliveryId}`);
    return response.data;
  },

  async retryDelivery(deliveryId: string): Promise<RetryDeliveryResponse> {
    const response = await api.post<RetryDeliveryResponse>(`/webhooks/deliveries/${deliveryId}/retry`);
    return response.data;
  },
};

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  WebhookEventsResponse,
  WebhookEventInfo,
  WebhookEndpoint,
  CreateWebhookEndpointRequest,
  UpdateWebhookEndpointRequest,
  WebhookDelivery,
  RotateSecretResponse,
  TestWebhookResponse,
  RetryDeliveryResponse,
} from '../models/webhook.model';

@Injectable({
  providedIn: 'root',
})
export class WebhookService {
  private readonly api = inject(ApiService);

  // Available Events
  getAvailableEvents(): Observable<WebhookEventsResponse> {
    return this.api.get<WebhookEventsResponse>('/webhooks/events');
  }

  getEventsGrouped(): Observable<Record<string, WebhookEventInfo[]>> {
    return this.api.get<Record<string, WebhookEventInfo[]>>('/webhooks/events/grouped');
  }

  // Webhook Endpoints
  getEndpoints(): Observable<WebhookEndpoint[]> {
    return this.api.get<WebhookEndpoint[]>('/webhooks/endpoints');
  }

  getEndpoint(endpointId: string): Observable<WebhookEndpoint> {
    return this.api.get<WebhookEndpoint>(`/webhooks/endpoints/${endpointId}`);
  }

  createEndpoint(data: CreateWebhookEndpointRequest): Observable<WebhookEndpoint> {
    return this.api.post<WebhookEndpoint>('/webhooks/endpoints', data);
  }

  updateEndpoint(endpointId: string, data: UpdateWebhookEndpointRequest): Observable<WebhookEndpoint> {
    return this.api.put<WebhookEndpoint>(`/webhooks/endpoints/${endpointId}`, data);
  }

  deleteEndpoint(endpointId: string): Observable<void> {
    return this.api.delete<void>(`/webhooks/endpoints/${endpointId}`);
  }

  rotateSecret(endpointId: string): Observable<RotateSecretResponse> {
    return this.api.post<RotateSecretResponse>(`/webhooks/endpoints/${endpointId}/rotate-secret`, {});
  }

  testEndpoint(endpointId: string): Observable<TestWebhookResponse> {
    return this.api.post<TestWebhookResponse>(`/webhooks/endpoints/${endpointId}/test`, {});
  }

  // Delivery Logs
  getDeliveries(endpointId: string, limit = 50): Observable<WebhookDelivery[]> {
    return this.api.get<WebhookDelivery[]>(`/webhooks/endpoints/${endpointId}/deliveries`, { limit });
  }

  getDelivery(deliveryId: string): Observable<WebhookDelivery> {
    return this.api.get<WebhookDelivery>(`/webhooks/deliveries/${deliveryId}`);
  }

  retryDelivery(deliveryId: string): Observable<RetryDeliveryResponse> {
    return this.api.post<RetryDeliveryResponse>(`/webhooks/deliveries/${deliveryId}/retry`, {});
  }
}

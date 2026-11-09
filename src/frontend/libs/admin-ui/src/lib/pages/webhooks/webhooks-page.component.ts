import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ConfirmDialogComponent,
  IconComponent,
  ToastService,
  extractErrorMessage,
} from '@syndew/oluso-ui-core';
import { WebhookService } from '../../services/webhook.service';
import { WebhookEndpoint, WebhookEventInfo } from '../../models/webhook.model';

/** The webhook endpoints list; creating and editing happen on the webhook page (`webhooks/new`, `webhooks/:id`). */
@Component({
  selector: 'admin-webhooks-page',
  standalone: true,
  imports: [CommonModule, RouterLink, CardComponent, ButtonComponent, BadgeComponent, ConfirmDialogComponent, IconComponent],
  templateUrl: './webhooks-page.component.html',
})
export class WebhooksPageComponent {
  private readonly webhookService = inject(WebhookService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly endpoints = signal<WebhookEndpoint[]>([]);
  readonly eventsGrouped = signal<Record<string, WebhookEventInfo[]>>({});

  readonly showDeleteModal = signal(false);
  readonly deleting = signal(false);
  readonly selectedEndpoint = signal<WebhookEndpoint | null>(null);
  readonly testingId = signal<string | null>(null);

  readonly eventsByType = computed(() => {
    const byType = new Map<string, WebhookEventInfo>();
    for (const events of Object.values(this.eventsGrouped())) {
      for (const event of events) byType.set(event.eventType, event);
    }
    return byType;
  });

  constructor() {
    this.loadEndpoints();
    this.loadEvents();
  }

  loadEndpoints(): void {
    this.loading.set(true);
    this.webhookService.getEndpoints().subscribe({
      next: (endpoints) => {
        this.endpoints.set(endpoints);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load endpoints:', error);
        this.loading.set(false);
      },
    });
  }

  loadEvents(): void {
    this.webhookService.getEventsGrouped().subscribe({
      next: (grouped) => this.eventsGrouped.set(grouped),
      error: (error) => console.error('Failed to load events:', error),
    });
  }

  newWebhook(): void {
    this.router.navigate(['/webhooks', 'new']);
  }

  openEndpoint(endpoint: WebhookEndpoint): void {
    this.router.navigate(['/webhooks', endpoint.id]);
  }

  openDeleteModal(endpoint: WebhookEndpoint): void {
    this.selectedEndpoint.set(endpoint);
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selectedEndpoint.set(null);
  }

  confirmDelete(): void {
    const endpoint = this.selectedEndpoint();
    if (!endpoint) return;

    this.deleting.set(true);
    this.webhookService.deleteEndpoint(endpoint.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.closeDeleteModal();
        this.loadEndpoints();
      },
      error: (error) => {
        console.error('Failed to delete endpoint:', error);
        this.deleting.set(false);
      },
    });
  }

  testEndpoint(endpoint: WebhookEndpoint): void {
    this.testingId.set(endpoint.id);
    this.webhookService.testEndpoint(endpoint.id).subscribe({
      next: (result) => {
        this.testingId.set(null);
        if (result?.success === false) this.toast.error(result.error || 'The test delivery failed');
        else this.toast.success('Test event sent');
        this.loadEndpoints();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to send the test event'));
        this.testingId.set(null);
      },
    });
  }

  // --- Restricted events and sensitive data ---

  carriesSensitiveData(event: WebhookEventInfo | undefined): boolean {
    return !!event && (!!event.restricted || (event.sensitiveProperties?.length ?? 0) > 0);
  }

  sensitiveEventsOf(eventTypes: string[]): WebhookEventInfo[] {
    return eventTypes
      .map((type) => this.eventsByType().get(type))
      .filter((event): event is WebhookEventInfo => this.carriesSensitiveData(event));
  }

  /** Subscribed to events with sensitive data the endpoint isn't authorised to receive. */
  isWithholding(endpoint: WebhookEndpoint): boolean {
    return !endpoint.sensitiveDataAuthorizedAt && this.sensitiveEventsOf(endpoint.eventTypes).length > 0;
  }

  authorizationTitle(endpoint: WebhookEndpoint): string {
    if (!endpoint.sensitiveDataAuthorizedAt) return '';
    const by = endpoint.sensitiveDataAuthorizedByName || endpoint.sensitiveDataAuthorizedBy;
    return `Authorised by ${by} on ${new Date(endpoint.sensitiveDataAuthorizedAt).toLocaleString()}`;
  }
}

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ToastService } from '@syndew/oluso-ui-core';
import { WebhooksPageComponent } from './webhooks-page.component';
import { WebhookService } from '../../services/webhook.service';
import { WebhookEndpoint, WebhookEventInfo } from '../../models/webhook.model';

describe('WebhooksPageComponent', () => {
  const opened: WebhookEventInfo = {
    eventType: 'cases.case_opened',
    category: 'cases',
    displayName: 'Case opened',
    enabledByDefault: false,
    restricted: true,
    requiredPermission: 'cases.share',
    sensitiveProperties: ['holder_name'],
    canSubscribe: false,
  };
  const order: WebhookEventInfo = { eventType: 'shop.order_placed', category: 'shop', displayName: 'Order placed', enabledByDefault: false };

  const endpoint = (overrides: Partial<WebhookEndpoint> = {}): WebhookEndpoint => ({
    id: 'e1',
    tenantId: 't1',
    name: 'Integration',
    url: 'https://x.example.org/hook',
    enabled: true,
    eventTypes: [opened.eventType],
    apiVersion: '2024-01-01',
    skipOwnClientEvents: true,
    createdAt: '2026-10-01T00:00:00Z',
    ...overrides,
  });

  function setup(endpoints: WebhookEndpoint[]) {
    TestBed.configureTestingModule({
      imports: [WebhooksPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: WebhookService,
          useValue: {
            getEndpoints: () => of(endpoints),
            getEventsGrouped: () => of({ Cases: [opened], Shop: [order] }),
          },
        },
        { provide: ToastService, useValue: { error: vi.fn(), success: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(WebhooksPageComponent);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    return { fixture, page: fixture.componentInstance, navigate };
  }

  it('flags an endpoint whose restricted subscriptions have their sensitive data withheld', () => {
    const { fixture, page } = setup([endpoint()]);

    expect(page.isWithholding(page.endpoints()[0])).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Sensitive data withheld');
  });

  it('shows who authorised an endpoint for sensitive data', () => {
    const authorised = endpoint({ sensitiveDataAuthorizedAt: '2026-10-02T09:00:00Z', sensitiveDataAuthorizedByName: 'Ada Admin' });
    const { fixture, page } = setup([authorised]);

    expect(page.isWithholding(authorised)).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Sensitive data authorised');
    expect(page.authorizationTitle(authorised)).toContain('Ada Admin');
  });

  it('opens the webhook page to create a webhook', () => {
    const { page, navigate } = setup([]);

    page.newWebhook();

    expect(navigate).toHaveBeenCalledWith(['/webhooks', 'new']);
  });

  it('opens the webhook page when a row is clicked', () => {
    const { fixture, navigate } = setup([endpoint()]);

    (fixture.nativeElement.querySelector('tbody tr') as HTMLElement).click();

    expect(navigate).toHaveBeenCalledWith(['/webhooks', 'e1']);
  });
});

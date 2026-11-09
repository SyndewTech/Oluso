import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ToastService } from '@syndew/oluso-ui-core';
import { WebhookDetailPageComponent, WebhookSecretHandoff, validateWebhookForm } from './webhook-detail-page.component';
import { WebhookService } from '../../services/webhook.service';
import { ClientService } from '../../services/client.service';
import { ApiService } from '../../services/api.service';
import { WebhookAuthType, WebhookEndpoint, WebhookEventInfo } from '../../models/webhook.model';

describe('WebhookDetailPageComponent', () => {
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
  const shipped: WebhookEventInfo = { eventType: 'shop.order_shipped', category: 'shop', displayName: 'Order shipped', enabledByDefault: false };

  const endpoint = (overrides: Partial<WebhookEndpoint> = {}): WebhookEndpoint => ({
    id: 'e1',
    tenantId: 't1',
    name: 'Integration',
    url: 'https://x.example.org/hook',
    enabled: true,
    eventTypes: [order.eventType],
    authType: WebhookAuthType.None,
    apiVersion: '2024-01-01',
    skipOwnClientEvents: true,
    createdAt: '2026-10-01T00:00:00Z',
    ...overrides,
  });

  function setup(id: string, existing?: WebhookEndpoint) {
    const createEndpoint = vi.fn(() => of(endpoint({ id: 'e9', secret: 'whsec_new' })));
    const updateEndpoint = vi.fn(() => of(existing ?? endpoint()));
    const getDeliveries = vi.fn(() => of([]));
    const toast = { error: vi.fn(), success: vi.fn() };
    TestBed.configureTestingModule({
      imports: [WebhookDetailPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
        {
          provide: WebhookService,
          useValue: {
            getEndpoint: () => of(existing),
            getEventsGrouped: () => of({ Cases: [opened], Shop: [order, shipped] }),
            createEndpoint,
            updateEndpoint,
            getDeliveries,
          },
        },
        { provide: ClientService, useValue: { getClients: () => of([{ clientId: 'sync-app', clientName: 'Sync' }]) } },
        { provide: ApiService, useValue: { baseUrl: 'https://id.example.org/api/admin', absoluteUrl: (p: string) => `https://id.example.org${p}` } },
        { provide: ToastService, useValue: toast },
      ],
    });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(WebhookDetailPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, createEndpoint, updateEndpoint, getDeliveries, navigate, toast };
  }

  it('does not let an admin without the permission subscribe to a restricted event', () => {
    const { page } = setup('new');

    page.toggleEventType(opened.eventType);
    page.toggleEventType(order.eventType);

    expect(page.formData.eventTypes).toEqual([order.eventType]);
  });

  it('selects every subscribable event of a group at once', () => {
    const { page } = setup('new');
    const shop = page.filteredGroups().find((g) => g.name === 'Shop') ?? { name: 'Shop', events: [] };

    page.toggleGroupSelection(shop);
    expect(page.formData.eventTypes).toEqual([order.eventType, shipped.eventType]);
    expect(page.isGroupFullySelected(shop)).toBe(true);

    page.toggleGroupSelection(shop);
    expect(page.formData.eventTypes).toEqual([]);
  });

  it('filters events by search and shows the matches expanded', () => {
    const { page } = setup('new');
    page.collapseAllGroups();
    page.eventSearchTerm = 'shipped';

    const groups = page.filteredGroups();
    expect(groups.map((g) => g.name)).toEqual(['Shop']);
    expect(groups[0].events).toEqual([shipped]);
    expect(page.isGroupExpanded('Shop')).toBe(true);
  });

  it('authorises sensitive data only after the confirmation and sends the choice', () => {
    const { page, createEndpoint } = setup('new');
    page.formData = { ...page.formData, name: 'Hook', url: 'https://x.example.org', eventTypes: [order.eventType] };

    page.onAuthorizeSensitiveDataChange(true);
    expect(page.formData.authorizeSensitiveData).toBe(false);
    expect(page.showSensitiveDataConfirm()).toBe(true);

    page.confirmSensitiveData();
    page.onSave();

    expect(createEndpoint).toHaveBeenCalledWith(expect.objectContaining({ authorizeSensitiveData: true }));
  });

  it('refuses to save without a name, URL and events', () => {
    const { page, createEndpoint, toast } = setup('new');

    page.onSave();

    expect(createEndpoint).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalled();
    expect(page.fieldError('name')).toBeTruthy();
    expect(page.fieldError('url')).toBeTruthy();
    expect(page.fieldError('eventTypes')).toBeTruthy();
  });

  it('creates an issuer-signed webhook without showing a shared secret, then opens its page', () => {
    const { page, createEndpoint, navigate } = setup('new');
    page.formData = { ...page.formData, name: 'Hook', url: 'https://x.example.org', eventTypes: [order.eventType], clientId: 'sync-app' };

    page.selectAuthMode('issuer');
    page.onSave();

    expect(createEndpoint).toHaveBeenCalledWith(
      expect.objectContaining({ authType: WebhookAuthType.IssuerSigned, clientId: 'sync-app', authCredential: undefined })
    );
    expect(TestBed.inject(WebhookSecretHandoff).take('e9')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/webhooks', 'e9']);
  });

  it('hands the new HMAC secret to the webhook page once', () => {
    const { page } = setup('new');
    page.formData = { ...page.formData, name: 'Hook', url: 'https://x.example.org', eventTypes: [order.eventType] };

    page.onSave();

    const handoff = TestBed.inject(WebhookSecretHandoff);
    expect(handoff.take('e9')).toBe('whsec_new');
    expect(handoff.take('e9')).toBeNull();
  });

  it('shows the JWKS URL from the tenant issuer discovery document', () => {
    const { page } = setup('new');
    const http = TestBed.inject(HttpTestingController);

    page.selectAuthMode('issuer');
    http.expectOne('https://id.example.org/api/admin/identity-providers/server-info').flush({ issuerUri: 'https://acme.example.org/' });
    expect(page.jwksUrl()).toBe('https://acme.example.org/.well-known/jwks');

    http.expectOne('https://acme.example.org/.well-known/openid-configuration').flush({ jwks_uri: 'https://acme.example.org/keys' });
    expect(page.jwksUrl()).toBe('https://acme.example.org/keys');
    expect(page.jwksFromDiscovery()).toBe(true);
  });

  it('builds a Basic credential and needs one for a new static type', () => {
    const { page, createEndpoint } = setup('new');
    page.formData = { ...page.formData, name: 'Hook', url: 'https://x.example.org', eventTypes: [order.eventType] };
    page.selectAuthMode('static');
    page.formData.authType = WebhookAuthType.BasicAuth;

    page.onSave();
    expect(createEndpoint).not.toHaveBeenCalled();
    expect(page.fieldError('credential')).toBeTruthy();

    page.formData.basicUsername = 'svc';
    page.formData.basicPassword = 'p@ss';
    page.onSave();
    expect(createEndpoint).toHaveBeenCalledWith(
      expect.objectContaining({ authType: WebhookAuthType.BasicAuth, authCredential: 'svc:p@ss' })
    );
  });

  it('edits an endpoint: keeps the stored credential and type unless changed, sends headers', () => {
    const existing = endpoint({ authType: WebhookAuthType.BearerToken, headers: { 'X-Tenant': 'acme' }, clientId: 'sync-app' });
    const { page, updateEndpoint, toast } = setup('e1', existing);

    expect(page.isNew()).toBe(false);
    expect(page.authMode()).toBe('static');
    expect(page.formData.headers).toEqual([{ name: 'X-Tenant', value: 'acme' }]);

    page.formData.name = 'Renamed';
    page.addHeader();
    page.formData.headers[1] = { name: 'X-Env', value: 'prod' };
    page.onSave();

    const request = (updateEndpoint.mock.calls[0] as unknown[])[1] as Record<string, unknown>;
    expect(request).toMatchObject({ name: 'Renamed', clientId: 'sync-app', headers: { 'X-Tenant': 'acme', 'X-Env': 'prod' } });
    expect(request).not.toHaveProperty('authType');
    expect(request).not.toHaveProperty('authCredential');
    expect(toast.success).toHaveBeenCalledWith('Changes saved');
  });

  it('rejects custom headers the delivery sets itself', () => {
    const errors = validateWebhookForm(
      {
        name: 'Hook',
        description: '',
        url: 'https://x.example.org',
        enabled: true,
        eventTypes: ['a'],
        authType: WebhookAuthType.None,
        credential: '',
        basicUsername: '',
        basicPassword: '',
        clientId: '',
        skipOwnClientEvents: true,
        authorizeSensitiveData: false,
        headers: [{ name: 'Authorization', value: 'x' }],
      },
      false
    );
    expect(errors.headers).toContain('Authorization');
  });

  it('loads delivery history when the Deliveries tab opens', () => {
    const { page, getDeliveries } = setup('e1', endpoint());

    page.setActiveTab('deliveries');

    expect(getDeliveries).toHaveBeenCalledWith('e1');
    expect(page.deliveriesLoaded()).toBe(true);
  });
});

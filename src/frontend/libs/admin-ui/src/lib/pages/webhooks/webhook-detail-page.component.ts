import { Component, Injectable, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpContext } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  ConfirmDialogComponent,
  IconComponent,
  InputComponent,
  ToastService,
  SKIP_ERROR_TOAST,
  extractErrorMessage,
} from '@syndew/oluso-ui-core';
import { WebhookService } from '../../services/webhook.service';
import { ClientService } from '../../services/client.service';
import { ApiService } from '../../services/api.service';
import { Client } from '../../models/client.model';
import {
  WebhookEndpoint,
  WebhookEventInfo,
  WebhookDelivery,
  WebhookDeliveryStatus,
  WebhookAuthType,
  STATIC_WEBHOOK_AUTH_TYPES,
  DEFAULT_WEBHOOK_API_VERSION,
  CreateWebhookEndpointRequest,
  UpdateWebhookEndpointRequest,
} from '../../models/webhook.model';

/**
 * Hands a newly created endpoint's signing secret to the page it navigates to, in memory only: the secret is
 * shown once and must not land in the URL or the browser history.
 */
@Injectable({ providedIn: 'root' })
export class WebhookSecretHandoff {
  private pending: { endpointId: string; secret: string } | null = null;

  put(endpointId: string, secret: string): void {
    this.pending = { endpointId, secret };
  }

  take(endpointId: string): string | null {
    if (this.pending?.endpointId !== endpointId) return null;
    const secret = this.pending.secret;
    this.pending = null;
    return secret;
  }
}

export interface HeaderRow {
  name: string;
  value: string;
}

export interface WebhookFormData {
  name: string;
  description: string;
  url: string;
  enabled: boolean;
  eventTypes: string[];
  authType: WebhookAuthType;
  /** Bearer token or API key. */
  credential: string;
  basicUsername: string;
  basicPassword: string;
  clientId: string;
  skipOwnClientEvents: boolean;
  authorizeSensitiveData: boolean;
  headers: HeaderRow[];
}

export type WebhookFormErrors = Partial<Record<'name' | 'url' | 'eventTypes' | 'credential' | 'headers', string>>;

export type WebhookTab = 'settings' | 'deliveries';

export interface EventGroup {
  name: string;
  events: WebhookEventInfo[];
}

/** A header name as HTTP allows it (RFC 9110 token). */
const HEADER_NAME = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/;

/** Headers the dispatcher sets itself; a custom header with one of these names would be overwritten or clash. */
const RESERVED_HEADERS = ['authorization', 'content-type', 'x-api-key'];

function emptyForm(): WebhookFormData {
  return {
    name: '',
    description: '',
    url: '',
    enabled: true,
    eventTypes: [],
    authType: WebhookAuthType.None,
    credential: '',
    basicUsername: '',
    basicPassword: '',
    clientId: '',
    skipOwnClientEvents: true,
    authorizeSensitiveData: false,
    headers: [],
  };
}

/** Why the form can't be saved, per field; empty when it can. */
export function validateWebhookForm(form: WebhookFormData, credentialStored: boolean): WebhookFormErrors {
  const errors: WebhookFormErrors = {};

  if (!form.name.trim()) errors.name = 'Enter a name.';

  const url = form.url.trim();
  if (!url) {
    errors.url = 'Enter the URL that receives deliveries.';
  } else {
    try {
      const parsed = new URL(url);
      if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') errors.url = 'Use an http or https URL.';
    } catch {
      errors.url = 'Enter a full URL, for example https://api.example.com/webhooks.';
    }
  }

  if (form.eventTypes.length === 0) errors.eventTypes = 'Choose at least one event.';

  if (STATIC_WEBHOOK_AUTH_TYPES.includes(form.authType) && !credentialStored) {
    if (form.authType === WebhookAuthType.BasicAuth) {
      if (!form.basicUsername.trim() || !form.basicPassword) errors.credential = 'Enter a username and password.';
      else if (form.basicUsername.includes(':')) errors.credential = 'The username cannot contain a colon.';
    } else if (!form.credential.trim()) {
      errors.credential = form.authType === WebhookAuthType.BearerToken ? 'Enter the token.' : 'Enter the API key.';
    }
  } else if (form.authType === WebhookAuthType.BasicAuth && (form.basicUsername || form.basicPassword)) {
    // Replacing a stored Basic credential needs both halves.
    if (!form.basicUsername.trim() || !form.basicPassword) errors.credential = 'Enter both the username and the password to replace them.';
    else if (form.basicUsername.includes(':')) errors.credential = 'The username cannot contain a colon.';
  }

  const seen = new Set<string>();
  for (const row of form.headers) {
    const name = row.name.trim();
    if (!name && !row.value) continue;
    if (!name) {
      errors.headers = 'Every header needs a name.';
      break;
    }
    if (!HEADER_NAME.test(name)) {
      errors.headers = `"${name}" is not a valid header name.`;
      break;
    }
    const lower = name.toLowerCase();
    if (RESERVED_HEADERS.includes(lower) || lower.startsWith('x-webhook-')) {
      errors.headers = `${name} is set by the delivery itself.`;
      break;
    }
    if (seen.has(lower)) {
      errors.headers = `${name} is listed twice.`;
      break;
    }
    seen.add(lower);
  }

  return errors;
}

@Component({
  selector: 'admin-webhook-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    ModalComponent,
    ConfirmDialogComponent,
    IconComponent,
    InputComponent,
  ],
  templateUrl: './webhook-detail-page.component.html',
})
export class WebhookDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiService);
  private readonly webhookService = inject(WebhookService);
  private readonly clientService = inject(ClientService);
  private readonly toast = inject(ToastService);
  private readonly secretHandoff = inject(WebhookSecretHandoff);

  readonly AuthType = WebhookAuthType;
  readonly defaultApiVersion = DEFAULT_WEBHOOK_API_VERSION;
  readonly staticAuthOptions: { type: WebhookAuthType; label: string; hint: string }[] = [
    { type: WebhookAuthType.BearerToken, label: 'Bearer token', hint: 'Sent as Authorization: Bearer <token>' },
    { type: WebhookAuthType.BasicAuth, label: 'Basic auth', hint: 'Sent as Authorization: Basic <username:password>' },
    { type: WebhookAuthType.ApiKeyHeader, label: 'API key header', hint: 'Sent as X-API-Key: <key>' },
  ];

  readonly sections = [
    { id: 'basics', label: 'Basics' },
    { id: 'events', label: 'Events' },
    { id: 'authentication', label: 'Authentication' },
    { id: 'client', label: 'Owning client' },
    { id: 'sensitive', label: 'Sensitive data' },
    { id: 'advanced', label: 'Headers and version' },
  ];

  readonly isNew = signal(true);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly submitted = signal(false);
  readonly endpoint = signal<WebhookEndpoint | null>(null);
  readonly clients = signal<Client[]>([]);
  readonly eventsGrouped = signal<Record<string, WebhookEventInfo[]>>({});
  readonly activeTab = signal<WebhookTab>('settings');

  readonly deliveries = signal<WebhookDelivery[]>([]);
  readonly deliveriesLoading = signal(false);
  readonly deliveriesLoaded = signal(false);
  readonly retryingId = signal<string | null>(null);
  readonly testing = signal(false);

  readonly showDeleteConfirm = signal(false);
  readonly deleting = signal(false);
  readonly showRotateConfirm = signal(false);
  readonly rotating = signal(false);
  readonly showSecretModal = signal(false);
  readonly newSecret = signal('');
  readonly secretCopied = signal(false);
  readonly showSensitiveDataConfirm = signal(false);

  /** The tenant issuer's JWKS URL, for issuer-signed delivery. */
  readonly jwksUrl = signal<string | null>(null);
  readonly jwksFromDiscovery = signal(false);
  private jwksRequested = false;

  readonly collapsedGroups = signal<Set<string>>(new Set());
  eventSearchTerm = '';

  formData: WebhookFormData = emptyForm();

  readonly eventsByType = computed(() => {
    const byType = new Map<string, WebhookEventInfo>();
    for (const events of Object.values(this.eventsGrouped())) {
      for (const event of events) byType.set(event.eventType, event);
    }
    return byType;
  });

  readonly tabs = computed(() =>
    this.isNew()
      ? []
      : [
          { id: 'settings' as WebhookTab, label: 'Settings' },
          { id: 'deliveries' as WebhookTab, label: 'Deliveries' },
        ]
  );

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    this.loadEvents();
    this.loadClients();

    if (id && id !== 'new') {
      this.isNew.set(false);
      this.loadEndpoint(id, true);
    } else {
      this.isNew.set(true);
      this.formData = emptyForm();
      this.loading.set(false);
    }
  }

  // --- Loading ---

  private loadEndpoint(id: string, first = false): void {
    if (first) this.loading.set(true);
    this.webhookService.getEndpoint(id).subscribe({
      next: (endpoint) => {
        this.endpoint.set(endpoint);
        if (first) {
          this.resetForm(endpoint);
          const secret = this.secretHandoff.take(endpoint.id);
          if (secret) {
            this.newSecret.set(secret);
            this.showSecretModal.set(true);
          }
        }
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load webhook:', error);
        this.loading.set(false);
      },
    });
  }

  private loadEvents(): void {
    this.webhookService.getEventsGrouped().subscribe({
      next: (grouped) => this.eventsGrouped.set(grouped),
      error: (error) => console.error('Failed to load events:', error),
    });
  }

  private loadClients(): void {
    this.clientService.getClients().subscribe({
      next: (clients) => this.clients.set(clients),
      error: (error) => console.error('Failed to load clients:', error),
    });
  }

  private resetForm(endpoint: WebhookEndpoint): void {
    this.formData = {
      ...emptyForm(),
      name: endpoint.name,
      description: endpoint.description ?? '',
      url: endpoint.url,
      enabled: endpoint.enabled,
      eventTypes: [...endpoint.eventTypes],
      authType: endpoint.authType ?? WebhookAuthType.None,
      clientId: endpoint.clientId ?? '',
      skipOwnClientEvents: endpoint.skipOwnClientEvents,
      authorizeSensitiveData: !!endpoint.sensitiveDataAuthorizedAt,
      headers: Object.entries(endpoint.headers ?? {}).map(([name, value]) => ({ name, value })),
    };
    if (this.formData.authType === WebhookAuthType.IssuerSigned) this.loadJwksUrl();
  }

  // --- Header, tabs, navigation ---

  pageTitle(): string {
    return this.isNew() ? 'New webhook' : this.endpoint()?.name || 'Webhook';
  }

  setActiveTab(tab: WebhookTab): void {
    this.activeTab.set(tab);
    if (tab === 'deliveries' && !this.deliveriesLoaded()) this.loadDeliveries();
  }

  scrollToSection(id: string): void {
    if (typeof document === 'undefined') return;
    document.getElementById(`webhook-${id}`)?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
  }

  onCancel(): void {
    this.router.navigate(['/webhooks']);
  }

  // --- Validation and saving ---

  /** A static credential of the selected type is already stored, so leaving the field blank keeps it. */
  credentialStored(): boolean {
    const endpoint = this.endpoint();
    return !!endpoint && endpoint.authType === this.formData.authType && STATIC_WEBHOOK_AUTH_TYPES.includes(this.formData.authType);
  }

  errors(): WebhookFormErrors {
    return validateWebhookForm(this.formData, this.credentialStored());
  }

  /** The error to show for a field, once the admin has tried to save. */
  fieldError(field: keyof WebhookFormErrors): string | undefined {
    return this.submitted() ? this.errors()[field] : undefined;
  }

  onSave(): void {
    this.submitted.set(true);
    const errors = this.errors();
    const firstInvalid = Object.keys(errors)[0];
    if (firstInvalid) {
      this.activeTab.set('settings');
      this.toast.error('Check the highlighted fields.');
      const section = { name: 'basics', url: 'basics', eventTypes: 'events', credential: 'authentication', headers: 'advanced' }[
        firstInvalid as keyof WebhookFormErrors
      ];
      setTimeout(() => this.scrollToSection(section));
      return;
    }

    const endpoint = this.endpoint();
    if (this.isNew() || !endpoint) this.create();
    else this.update(endpoint);
  }

  private headersMap(): Record<string, string> {
    const map: Record<string, string> = {};
    for (const row of this.formData.headers) {
      if (row.name.trim()) map[row.name.trim()] = row.value;
    }
    return map;
  }

  /** The credential the form holds for the selected static type, or undefined to keep (or not set) one. */
  private credentialValue(): string | undefined {
    switch (this.formData.authType) {
      case WebhookAuthType.BearerToken:
      case WebhookAuthType.ApiKeyHeader:
        return this.formData.credential.trim() || undefined;
      case WebhookAuthType.BasicAuth:
        return this.formData.basicUsername.trim() && this.formData.basicPassword
          ? `${this.formData.basicUsername.trim()}:${this.formData.basicPassword}`
          : undefined;
      default:
        return undefined;
    }
  }

  buildCreateRequest(): CreateWebhookEndpointRequest {
    const headers = this.headersMap();
    return {
      name: this.formData.name.trim(),
      description: this.formData.description.trim() || undefined,
      url: this.formData.url.trim(),
      enabled: this.formData.enabled,
      eventTypes: this.formData.eventTypes,
      headers: Object.keys(headers).length ? headers : undefined,
      authType: this.formData.authType,
      authCredential: this.credentialValue(),
      apiVersion: DEFAULT_WEBHOOK_API_VERSION,
      clientId: this.formData.clientId || undefined,
      skipOwnClientEvents: this.formData.skipOwnClientEvents,
      authorizeSensitiveData: this.formData.authorizeSensitiveData,
    };
  }

  buildUpdateRequest(endpoint: WebhookEndpoint): UpdateWebhookEndpointRequest {
    const request: UpdateWebhookEndpointRequest = {
      name: this.formData.name.trim(),
      description: this.formData.description.trim(),
      url: this.formData.url.trim(),
      enabled: this.formData.enabled,
      eventTypes: this.formData.eventTypes,
      headers: this.headersMap(),
      clientId: this.formData.clientId,
      skipOwnClientEvents: this.formData.skipOwnClientEvents,
      authorizeSensitiveData: this.formData.authorizeSensitiveData,
    };
    // The type is sent only when it changes, so an endpoint whose type the server didn't report keeps it.
    if (this.formData.authType !== (endpoint.authType ?? WebhookAuthType.None)) {
      request.authType = this.formData.authType;
    }
    const credential = this.credentialValue();
    if (credential !== undefined) request.authCredential = credential;
    return request;
  }

  private create(): void {
    this.saving.set(true);
    this.webhookService.createEndpoint(this.buildCreateRequest()).subscribe({
      next: (created) => {
        this.saving.set(false);
        if (created.secret && this.formData.authType !== WebhookAuthType.IssuerSigned) {
          this.secretHandoff.put(created.id, created.secret);
        }
        this.toast.success('Webhook created');
        this.router.navigate(['/webhooks', created.id]);
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to create webhook'));
        this.saving.set(false);
      },
    });
  }

  private update(endpoint: WebhookEndpoint): void {
    this.saving.set(true);
    this.webhookService.updateEndpoint(endpoint.id, this.buildUpdateRequest(endpoint)).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.submitted.set(false);
        // Keep stats and anything the update response leaves out.
        const merged = { ...endpoint, ...updated, authType: updated?.authType ?? this.formData.authType };
        this.endpoint.set(merged);
        this.resetForm(merged);
        this.toast.success('Changes saved');
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to update webhook'));
        this.saving.set(false);
      },
    });
  }

  // --- Delete ---

  confirmDelete(): void {
    const endpoint = this.endpoint();
    if (!endpoint) return;
    this.deleting.set(true);
    this.webhookService.deleteEndpoint(endpoint.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.showDeleteConfirm.set(false);
        this.toast.success('Webhook deleted');
        this.router.navigate(['/webhooks']);
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to delete webhook'));
        this.deleting.set(false);
      },
    });
  }

  // --- Authentication ---

  isStaticAuth(type: WebhookAuthType = this.formData.authType): boolean {
    return STATIC_WEBHOOK_AUTH_TYPES.includes(type);
  }

  selectAuthMode(mode: 'hmac' | 'issuer' | 'static'): void {
    if (mode === 'hmac') this.formData.authType = WebhookAuthType.None;
    else if (mode === 'issuer') {
      this.formData.authType = WebhookAuthType.IssuerSigned;
      this.loadJwksUrl();
    } else if (!this.isStaticAuth()) {
      const stored = this.endpoint()?.authType;
      this.formData.authType = stored !== undefined && this.isStaticAuth(stored) ? stored : WebhookAuthType.BearerToken;
    }
  }

  authMode(): 'hmac' | 'issuer' | 'static' {
    if (this.formData.authType === WebhookAuthType.IssuerSigned) return 'issuer';
    return this.isStaticAuth() ? 'static' : 'hmac';
  }

  /** Deliveries carry the HMAC signature, so the secret exists and can be rotated. */
  usesSigningSecret(): boolean {
    return this.formData.authType !== WebhookAuthType.IssuerSigned;
  }

  /** The endpoint was saved as issuer-signed and is being switched to a secret-based type. */
  leavingIssuerSigned(): boolean {
    return this.endpoint()?.authType === WebhookAuthType.IssuerSigned && this.usesSigningSecret();
  }

  /**
   * Resolves the JWKS URL the tenant's discovery document advertises: the tenant's issuer comes from the admin
   * API's server info, then jwks_uri from that issuer's discovery document. Falls back to the default path.
   */
  loadJwksUrl(): void {
    if (this.jwksRequested) return;
    this.jwksRequested = true;
    const quiet = { context: new HttpContext().set(SKIP_ERROR_TOAST, true) };
    const fallbackFor = (issuer: string) => `${issuer.replace(/\/$/, '')}/.well-known/jwks`;

    this.http.get<{ issuerUri?: string }>(`${this.api.baseUrl}/identity-providers/server-info`, quiet).subscribe({
      next: (info) => {
        const issuer = (info?.issuerUri || this.api.absoluteUrl('')).replace(/\/$/, '');
        this.jwksUrl.set(fallbackFor(issuer));
        this.http.get<{ jwks_uri?: string }>(`${issuer}/.well-known/openid-configuration`, quiet).subscribe({
          next: (discovery) => {
            if (discovery?.jwks_uri) {
              this.jwksUrl.set(discovery.jwks_uri);
              this.jwksFromDiscovery.set(true);
            }
          },
          error: () => undefined,
        });
      },
      error: () => this.jwksUrl.set(fallbackFor(this.api.absoluteUrl(''))),
    });
  }

  copy(value: string | null | undefined): void {
    if (!value) return;
    navigator.clipboard.writeText(value).then(
      () => this.toast.success('Copied'),
      () => this.toast.error('Could not copy to the clipboard')
    );
  }

  confirmRotateSecret(): void {
    const endpoint = this.endpoint();
    if (!endpoint) return;
    this.rotating.set(true);
    this.webhookService.rotateSecret(endpoint.id).subscribe({
      next: (response) => {
        this.rotating.set(false);
        this.showRotateConfirm.set(false);
        this.newSecret.set(response.secret);
        this.showSecretModal.set(true);
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to rotate the secret'));
        this.rotating.set(false);
      },
    });
  }

  async copySecret(): Promise<void> {
    await navigator.clipboard.writeText(this.newSecret());
    this.secretCopied.set(true);
    setTimeout(() => this.secretCopied.set(false), 2000);
  }

  closeSecretModal(): void {
    this.showSecretModal.set(false);
    this.newSecret.set('');
  }

  // --- Owning client ---

  clientLabel(clientId: string): string {
    const client = this.clients().find((c) => c.clientId === clientId);
    return client?.clientName ? `${client.clientName} (${clientId})` : clientId;
  }

  // --- Custom headers ---

  addHeader(): void {
    this.formData.headers = [...this.formData.headers, { name: '', value: '' }];
  }

  removeHeader(index: number): void {
    this.formData.headers = this.formData.headers.filter((_, i) => i !== index);
  }

  // --- Events ---

  toggleEventType(eventType: string): void {
    if (this.formData.eventTypes.includes(eventType)) {
      this.formData.eventTypes = this.formData.eventTypes.filter((e) => e !== eventType);
    } else if (this.canSelect(this.eventsByType().get(eventType))) {
      this.formData.eventTypes = [...this.formData.eventTypes, eventType];
    }
  }

  isSelected(eventType: string): boolean {
    return this.formData.eventTypes.includes(eventType);
  }

  filteredGroups(): EventGroup[] {
    const grouped = this.eventsGrouped();
    const groups = Object.keys(grouped).map((name) => ({ name, events: grouped[name] }));
    const term = this.eventSearchTerm.trim().toLowerCase();
    if (!term) return groups;
    return groups
      .map((group) => ({
        ...group,
        events: group.events.filter(
          (e) =>
            e.eventType.toLowerCase().includes(term) ||
            e.displayName.toLowerCase().includes(term) ||
            (e.description?.toLowerCase().includes(term) ?? false) ||
            group.name.toLowerCase().includes(term)
        ),
      }))
      .filter((group) => group.events.length > 0);
  }

  /** Groups start open; a search always shows its matches. */
  isGroupExpanded(name: string): boolean {
    return !!this.eventSearchTerm.trim() || !this.collapsedGroups().has(name);
  }

  toggleGroup(name: string): void {
    const next = new Set(this.collapsedGroups());
    if (next.has(name)) next.delete(name);
    else next.add(name);
    this.collapsedGroups.set(next);
  }

  expandAllGroups(): void {
    this.collapsedGroups.set(new Set());
  }

  collapseAllGroups(): void {
    this.collapsedGroups.set(new Set(Object.keys(this.eventsGrouped())));
  }

  selectedCount(group: EventGroup): number {
    return group.events.filter((e) => this.isSelected(e.eventType)).length;
  }

  /** The events in the group the admin can toggle: subscribable ones, plus ones already selected. */
  private toggleable(group: EventGroup): string[] {
    return group.events.filter((e) => this.canSelect(e) || this.isSelected(e.eventType)).map((e) => e.eventType);
  }

  isGroupFullySelected(group: EventGroup): boolean {
    const types = this.toggleable(group);
    return types.length > 0 && types.every((t) => this.isSelected(t));
  }

  isGroupPartlySelected(group: EventGroup): boolean {
    const count = this.selectedCount(group);
    return count > 0 && !this.isGroupFullySelected(group);
  }

  toggleGroupSelection(group: EventGroup): void {
    const types = this.toggleable(group);
    if (types.every((t) => this.isSelected(t))) {
      this.formData.eventTypes = this.formData.eventTypes.filter((e) => !types.includes(e));
    } else {
      this.formData.eventTypes = [...new Set([...this.formData.eventTypes, ...types])];
    }
  }

  clearEvents(): void {
    this.formData.eventTypes = [];
  }

  // --- Restricted events and sensitive data ---

  /** A restricted event can only be newly subscribed by an administrator holding its permission. */
  canSelect(event: WebhookEventInfo | undefined): boolean {
    return event?.canSubscribe !== false;
  }

  carriesSensitiveData(event: WebhookEventInfo | undefined): boolean {
    return !!event && (!!event.restricted || (event.sensitiveProperties?.length ?? 0) > 0);
  }

  sensitiveEventsOf(eventTypes: string[]): WebhookEventInfo[] {
    return eventTypes
      .map((type) => this.eventsByType().get(type))
      .filter((event): event is WebhookEventInfo => this.carriesSensitiveData(event));
  }

  /** Authorising needs the permission of every restricted event the endpoint will subscribe to. */
  canAuthorizeSensitiveData(): boolean {
    return this.sensitiveEventsOf(this.formData.eventTypes).every((event) => this.canSelect(event));
  }

  restrictionTitle(event: WebhookEventInfo): string {
    const parts = [
      event.classificationPurpose,
      event.requiredPermission ? `Requires the ${event.requiredPermission} permission.` : undefined,
      event.sensitiveProperties?.length ? `Withheld unless authorised: ${event.sensitiveProperties.join(', ')}` : undefined,
    ];
    return parts.filter(Boolean).join(' ');
  }

  authorizationTitle(endpoint: WebhookEndpoint): string {
    if (!endpoint.sensitiveDataAuthorizedAt) return '';
    const by = endpoint.sensitiveDataAuthorizedByName || endpoint.sensitiveDataAuthorizedBy;
    return `Authorised by ${by} on ${this.formatDate(endpoint.sensitiveDataAuthorizedAt)}`;
  }

  onAuthorizeSensitiveDataChange(checked: boolean): void {
    this.formData.authorizeSensitiveData = false;
    if (checked) this.showSensitiveDataConfirm.set(true);
  }

  confirmSensitiveData(): void {
    this.formData.authorizeSensitiveData = true;
    this.showSensitiveDataConfirm.set(false);
  }

  cancelSensitiveData(): void {
    this.formData.authorizeSensitiveData = false;
    this.showSensitiveDataConfirm.set(false);
  }

  sensitiveDataConfirmMessage(): string {
    const names = this.sensitiveEventsOf(this.formData.eventTypes).map((e) => e.displayName);
    return (
      `Deliveries to ${this.formData.url || 'this endpoint'} will include the confidential fields of ${names.join(', ')}. ` +
      'This data leaves the platform and is then governed by the receiving system. ' +
      'Only authorise endpoints your organisation is permitted to share this data with. ' +
      'Your name and the time are recorded against this authorisation.'
    );
  }

  // --- Deliveries and testing ---

  loadDeliveries(): void {
    const endpoint = this.endpoint();
    if (!endpoint) return;
    this.deliveriesLoading.set(true);
    this.webhookService.getDeliveries(endpoint.id).subscribe({
      next: (deliveries) => {
        this.deliveries.set(deliveries);
        this.deliveriesLoading.set(false);
        this.deliveriesLoaded.set(true);
      },
      error: (error) => {
        console.error('Failed to load deliveries:', error);
        this.deliveriesLoading.set(false);
      },
    });
  }

  testEndpoint(): void {
    const endpoint = this.endpoint();
    if (!endpoint) return;
    this.testing.set(true);
    this.webhookService.testEndpoint(endpoint.id).subscribe({
      next: (result) => {
        this.testing.set(false);
        if (result?.success === false) this.toast.error(result.error || 'The test delivery failed');
        else this.toast.success('Test event sent');
        this.loadEndpoint(endpoint.id);
        if (this.deliveriesLoaded() || this.activeTab() === 'deliveries') this.loadDeliveries();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to send the test event'));
        this.testing.set(false);
      },
    });
  }

  retryDelivery(delivery: WebhookDelivery): void {
    this.retryingId.set(delivery.id);
    this.webhookService.retryDelivery(delivery.id).subscribe({
      next: () => {
        this.retryingId.set(null);
        this.loadDeliveries();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to retry the delivery'));
        this.retryingId.set(null);
      },
    });
  }

  canRetry(status: WebhookDeliveryStatus): boolean {
    return status === 'Failed' || status === 'Exhausted';
  }

  getStatusVariant(status: WebhookDeliveryStatus): 'default' | 'success' | 'warning' | 'danger' | 'info' {
    switch (status) {
      case 'Success':
        return 'success';
      case 'Failed':
        return 'danger';
      case 'Pending':
        return 'warning';
      default:
        return 'default';
    }
  }

  formatDate(dateString: string | undefined): string {
    return dateString ? new Date(dateString).toLocaleString() : 'Never';
  }
}

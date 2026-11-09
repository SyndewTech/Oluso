import { Component, inject, input, signal, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent, ToastService } from '@syndew/oluso-ui-core';
import { ClientService } from '../../services/client.service';
import { PlatformClientService } from '../../services/platform-client.service';
import { ClientConnection } from '../../models/client-connection.model';

/**
 * Read-only panel showing everything an integrator needs to wire a client
 * into their app: client ID, OIDC discovery URL, all the connect/* endpoints,
 * redirect URIs, scopes. Used in two places:
 *   - the Connection tab of the client detail page,
 *   - the eye-icon side preview on the clients list.
 *
 * IssuerUri is resolved per-tenant on the server side (CustomDomain /
 * TokenSettings overrides factor in), so the URLs shown here always match
 * what /.well-known/openid-configuration actually serves for this tenant.
 */
@Component({
  selector: 'admin-client-connection-panel',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    @if (loading()) {
      <div class="flex items-center justify-center h-32">
        <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary-600"></div>
      </div>
    } @else if (error()) {
      <div class="bg-red-50 border border-red-200 rounded-md p-3 text-sm text-red-700">
        {{ error() }}
      </div>
    } @else if (connection(); as c) {
      <div class="space-y-6">
        <!-- Identity -->
        <section>
          <h3 class="text-sm font-medium text-gray-700 mb-3">Identity</h3>
          <div class="space-y-2">
            <div class="grid grid-cols-12 items-start gap-2">
              <span class="col-span-3 text-xs text-gray-500 pt-1.5">Client ID</span>
              <div class="col-span-9 flex items-center gap-2">
                <code class="flex-1 bg-gray-50 border border-gray-200 rounded px-2 py-1 text-xs font-mono break-all">{{ c.clientId }}</code>
                <button (click)="copy(c.clientId)" class="text-gray-400 hover:text-gray-600" title="Copy">
                  <ui-icon name="clipboard" class="h-4 w-4" />
                </button>
              </div>
            </div>
            <div class="grid grid-cols-12 items-start gap-2">
              <span class="col-span-3 text-xs text-gray-500 pt-1.5">Issuer URI</span>
              <div class="col-span-9 flex items-center gap-2">
                <code class="flex-1 bg-gray-50 border border-gray-200 rounded px-2 py-1 text-xs font-mono break-all">{{ c.issuerUri }}</code>
                <button (click)="copy(c.issuerUri)" class="text-gray-400 hover:text-gray-600" title="Copy">
                  <ui-icon name="clipboard" class="h-4 w-4" />
                </button>
              </div>
            </div>
          </div>
        </section>

        <!-- Discovery -->
        <section>
          <div class="flex items-center justify-between mb-3">
            <h3 class="text-sm font-medium text-gray-700">OIDC Discovery</h3>
            <a [href]="c.discoveryUrl" target="_blank" rel="noopener"
               class="inline-flex items-center gap-1 text-xs text-primary-600 hover:text-primary-700">
              <ui-icon name="arrow-top-right-on-square" class="h-3.5 w-3.5" />
              Open well-known doc
            </a>
          </div>
          <div class="space-y-2">
            <div class="grid grid-cols-12 items-start gap-2">
              <span class="col-span-3 text-xs text-gray-500 pt-1.5">Discovery URL</span>
              <div class="col-span-9 flex items-center gap-2">
                <code class="flex-1 bg-gray-50 border border-gray-200 rounded px-2 py-1 text-xs font-mono break-all">{{ c.discoveryUrl }}</code>
                <button (click)="copy(c.discoveryUrl)" class="text-gray-400 hover:text-gray-600" title="Copy">
                  <ui-icon name="clipboard" class="h-4 w-4" />
                </button>
              </div>
            </div>
          </div>
        </section>

        <!-- Endpoints -->
        <section>
          <h3 class="text-sm font-medium text-gray-700 mb-3">OIDC Endpoints</h3>
          <div class="space-y-2">
            @for (e of endpoints(c); track e.label) {
              <div class="grid grid-cols-12 items-start gap-2">
                <span class="col-span-3 text-xs text-gray-500 pt-1.5">{{ e.label }}</span>
                <div class="col-span-9 flex items-center gap-2">
                  <code class="flex-1 bg-gray-50 border border-gray-200 rounded px-2 py-1 text-xs font-mono break-all">{{ e.value }}</code>
                  <button (click)="copy(e.value)" class="text-gray-400 hover:text-gray-600" title="Copy">
                    <ui-icon name="clipboard" class="h-4 w-4" />
                  </button>
                </div>
              </div>
            }
          </div>
        </section>

        <!-- Configuration echo -->
        <section>
          <h3 class="text-sm font-medium text-gray-700 mb-3">Client Configuration</h3>
          <div class="space-y-3 text-xs">
            <div>
              <span class="text-gray-500">Allowed grant types:</span>
              <div class="mt-1 flex flex-wrap gap-1">
                @for (g of c.allowedGrantTypes; track g) {
                  <span class="px-2 py-0.5 rounded bg-gray-100 text-gray-700 font-mono">{{ g }}</span>
                }
              </div>
            </div>
            <div>
              <span class="text-gray-500">Allowed scopes:</span>
              <div class="mt-1 flex flex-wrap gap-1">
                @for (s of c.allowedScopes; track s) {
                  <span class="px-2 py-0.5 rounded bg-gray-100 text-gray-700 font-mono">{{ s }}</span>
                }
                @if (c.allowedScopes.length === 0) {
                  <span class="text-gray-400 italic">none</span>
                }
              </div>
            </div>
            <div>
              <span class="text-gray-500">Requires PKCE:</span>
              <span class="ml-1 font-mono">{{ c.requirePkce ? 'yes' : 'no' }}</span>
              <span class="ml-3 text-gray-500">Requires client secret:</span>
              <span class="ml-1 font-mono">{{ c.requireClientSecret ? 'yes' : 'no' }}</span>
            </div>
            <div>
              <span class="text-gray-500">Redirect URIs:</span>
              <ul class="mt-1 space-y-1">
                @for (u of c.redirectUris; track u) {
                  <li class="font-mono text-gray-700 break-all">{{ u }}</li>
                }
                @if (c.redirectUris.length === 0) {
                  <li class="text-gray-400 italic">none configured</li>
                }
              </ul>
            </div>
            @if (c.postLogoutRedirectUris.length > 0) {
              <div>
                <span class="text-gray-500">Post-logout redirect URIs:</span>
                <ul class="mt-1 space-y-1">
                  @for (u of c.postLogoutRedirectUris; track u) {
                    <li class="font-mono text-gray-700 break-all">{{ u }}</li>
                  }
                </ul>
              </div>
            }
            @if (c.allowedCorsOrigins.length > 0) {
              <div>
                <span class="text-gray-500">CORS origins:</span>
                <ul class="mt-1 space-y-1">
                  @for (o of c.allowedCorsOrigins; track o) {
                    <li class="font-mono text-gray-700 break-all">{{ o }}</li>
                  }
                </ul>
              </div>
            }
          </div>
        </section>
      </div>
    }
  `,
})
export class ClientConnectionPanelComponent implements OnInit, OnChanges {
  private readonly clientService = inject(ClientService);
  private readonly platformClientService = inject(PlatformClientService);
  private readonly toast = inject(ToastService);

  /** Database id of the client. */
  readonly clientId = input.required<number>();
  /** When the client lives in the platform-clients controller. */
  readonly platform = input<boolean>(false);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly connection = signal<ClientConnection | null>(null);

  ngOnInit(): void {
    this.load();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['clientId'] && !changes['clientId'].firstChange) {
      this.load();
    }
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    const obs = this.platform()
      ? this.platformClientService.getClientConnection(this.clientId())
      : this.clientService.getClientConnection(this.clientId());
    obs.subscribe({
      next: (c) => { this.connection.set(c); this.loading.set(false); },
      error: () => { this.error.set('Failed to load connection details'); this.loading.set(false); },
    });
  }

  endpoints(c: ClientConnection): { label: string; value: string }[] {
    return [
      { label: 'Authorization', value: c.authorizationEndpoint },
      { label: 'Token', value: c.tokenEndpoint },
      { label: 'UserInfo', value: c.userInfoEndpoint },
      { label: 'JWKS', value: c.jwksUri },
      { label: 'End session', value: c.endSessionEndpoint },
      { label: 'Introspection', value: c.introspectionEndpoint },
      { label: 'Revocation', value: c.revocationEndpoint },
    ];
  }

  copy(value: string): void {
    navigator.clipboard.writeText(value).then(() => this.toast.success('Copied'));
  }
}

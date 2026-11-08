import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ToastService } from '@oluso/ui-core';
import { ClientDetailPageComponent, STRICT_SCOPES_PROPERTY } from './client-detail-page.component';
import { ClientService } from '../../services/client.service';
import { PlatformClientService } from '../../services/platform-client.service';
import { RoleService } from '../../services/role.service';
import { UserService } from '../../services/user.service';
import { IdentityProviderService } from '../../services/identity-provider.service';
import { ApiScopeService } from '../../services/api-scope.service';
import { IdentityResourceService } from '../../services/identity-resource.service';
import { ResourceService } from '../../services/resource.service';
import { Client } from '../../models/client.model';
import { ClientProperty } from '../../models/client-property.model';

describe('ClientDetailPageComponent strict scopes', () => {
  const client = (properties: ClientProperty[]): Client =>
    ({
      id: 7,
      clientId: 'app',
      clientName: 'App',
      allowedGrantTypes: ['authorization_code'],
      allowedScopes: ['openid'],
      redirectUris: [],
      postLogoutRedirectUris: [],
      allowedCorsOrigins: [],
      allowedRoles: [],
      allowedUsers: [],
      identityProviderRestrictions: [],
      claims: [],
      properties,
      secrets: [],
    }) as unknown as Client;

  function setup(id: string, properties: ClientProperty[] = []) {
    const updateClient = vi.fn((_: number, fields: Partial<Client>) => of({ ...client(properties), ...fields } as Client));
    TestBed.configureTestingModule({
      imports: [ClientDetailPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }), data: {} } } },
        {
          provide: ClientService,
          useValue: {
            getClient: () => of(client(properties)),
            getClientConnection: () => of(null),
            updateClient,
          },
        },
        { provide: PlatformClientService, useValue: {} },
        { provide: RoleService, useValue: { getAllRoles: () => of([]) } },
        { provide: UserService, useValue: { getUsers: () => of({ items: [], total: 0, page: 1, pageSize: 100, totalPages: 0 }) } },
        { provide: IdentityProviderService, useValue: { getAll: () => of([]) } },
        { provide: ApiScopeService, useValue: { getServiceApiCatalog: () => of(null), getAllScopes: () => of([]) } },
        { provide: IdentityResourceService, useValue: { getAll: () => of([]) } },
        { provide: ResourceService, useValue: { getAll: () => of({ items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 }) } },
        { provide: ToastService, useValue: { error: vi.fn(), success: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(ClientDetailPageComponent);
    const page = fixture.componentInstance;
    fixture.detectChanges();
    page.setActiveTab('scopes');
    fixture.detectChanges();
    const switchEl = () => fixture.nativeElement.querySelector('[data-testid="strict-scopes-switch"]') as HTMLButtonElement | null;
    return { fixture, page, switchEl, updateClient };
  }

  it('is off by default and turning it on writes strict_scopes=true on save', () => {
    const { fixture, page, switchEl, updateClient } = setup('7', [{ key: 'other', value: 'x' }]);

    expect(switchEl()?.getAttribute('aria-checked')).toBe('false');
    switchEl()!.click();
    fixture.detectChanges();
    expect(switchEl()?.getAttribute('aria-checked')).toBe('true');

    page.onSave();
    expect(updateClient).toHaveBeenCalledWith(7, {
      properties: [{ key: 'other', value: 'x' }, { key: STRICT_SCOPES_PROPERTY, value: 'true' }],
    });
  });

  it('reads the server truthy forms and turning it off removes the property', () => {
    const { fixture, page, switchEl } = setup('7', [{ key: STRICT_SCOPES_PROPERTY, value: ' TRUE ' }, { key: 'other', value: 'x' }]);

    expect(page.strictScopes()).toBe(true);
    switchEl()!.click();
    fixture.detectChanges();

    expect(page.strictScopes()).toBe(false);
    expect(page.editedFields().properties).toEqual([{ key: 'other', value: 'x' }]);
  });

  it('treats "1" as on', () => {
    const { page } = setup('7', [{ key: STRICT_SCOPES_PROPERTY, value: '1' }]);
    expect(page.strictScopes()).toBe(true);
  });

  it('keeps strict_scopes out of the generic properties card and routes it to the switch when typed there', () => {
    const { fixture, page } = setup('7', [{ key: STRICT_SCOPES_PROPERTY, value: 'true' }, { key: 'other', value: 'x' }]);
    page.setActiveTab('claims');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('other:');
    expect(text).not.toContain(`${STRICT_SCOPES_PROPERTY}:`);
    expect(page.customProperties()).toEqual([{ key: 'other', value: 'x' }]);

    page.newPropertyKey = STRICT_SCOPES_PROPERTY;
    page.newPropertyValue = 'false';
    page.addProperty();
    expect(page.strictScopes()).toBe(false);
    expect(page.editedFields().properties).toEqual([{ key: 'other', value: 'x' }]);
  });

  it('is not offered when creating a client', () => {
    const { switchEl } = setup('new');
    expect(switchEl()).toBeNull();
  });
});

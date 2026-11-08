import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NEVER, Observable, Subject, of } from 'rxjs';
import { OidcSecurityService, PublicEventsService } from 'angular-auth-oidc-client';
import { AuthProvider, AuthStore } from '@oluso/auth';
import { MyPermissionsResponse, PermissionsService } from '@oluso/ui-core';
import { AdminAuthService } from './admin-auth.service';
import { ApiService } from './api.service';

class MemoryStorage implements Storage {
  private items = new Map<string, string>();
  get length() { return this.items.size; }
  clear() { this.items.clear(); }
  getItem(key: string) { return this.items.get(key) ?? null; }
  key(index: number) { return [...this.items.keys()][index] ?? null; }
  removeItem(key: string) { this.items.delete(key); }
  setItem(key: string, value: string) { this.items.set(key, String(value)); }
}

beforeAll(() => vi.stubGlobal('localStorage', new MemoryStorage()));
afterAll(() => vi.unstubAllGlobals());

function jwt(claims: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64(claims)}.sig`;
}

function my(permissions: string[]): MyPermissionsResponse {
  return { effectivePermissions: permissions, rolePermissions: {}, isSuperAdmin: false, explicitGrantPermissions: [] };
}

describe('AdminAuthService permissions', () => {
  let tokens: Subject<string>;
  let permissionResponses: Subject<MyPermissionsResponse>[];
  let permissions: PermissionsService;
  let authStore: AuthStore;

  /** `null` makes the sign-in /permissions/my request fail. */
  async function setup(firstApiPermissions: string[] | null) {
    localStorage.clear();
    tokens = new Subject<string>();
    permissionResponses = [];
    const initialToken = jwt({ sub: 'u1', tenant_id: 't1', permissions: ['a.read'] });

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: OidcSecurityService,
          useValue: {
            checkAuth: () => of({ isAuthenticated: true, accessToken: initialToken }),
            getAccessToken: () => tokens,
            logoff: () => of(null),
            authorize: () => undefined,
          },
        },
        { provide: AuthProvider, useValue: { isAuthenticated$: NEVER } },
        { provide: PublicEventsService, useValue: { registerForEvents: () => NEVER } },
        {
          provide: ApiService,
          useValue: {
            get: (endpoint: string): Observable<unknown> => {
              if (endpoint === '/permissions/my') {
                const response = new Subject<MyPermissionsResponse>();
                permissionResponses.push(response);
                return response;
              }
              return of({ features: {} });
            },
          },
        },
      ],
    });

    permissions = TestBed.inject(PermissionsService);
    authStore = TestBed.inject(AuthStore);
    const service = TestBed.inject(AdminAuthService);
    const init = service.initialize();
    await Promise.resolve();
    if (firstApiPermissions) {
      respond(0, firstApiPermissions);
    } else {
      permissionResponses[0].error(new Error('down'));
    }
    await init;
  }

  function respond(index: number, perms: string[]) {
    permissionResponses[index].next(my(perms));
    permissionResponses[index].complete();
  }

  afterEach(() => localStorage.clear());

  it('uses /permissions/my rather than token claims at sign-in', async () => {
    await setup(['a.read', 'b.write']);
    expect(permissions.isAuthoritative()).toBe(true);
    expect(permissions.hasPermission('b.write')).toBe(true);
  });

  it('keeps the API permissions across a token renewal and refreshes them from the API', async () => {
    await setup(['a.read', 'b.write']);

    tokens.next(jwt({ sub: 'u1', tenant_id: 't1', permissions: ['a.read'], iat: 2 }));
    expect(permissionResponses.length).toBe(2);
    // Until the refresh lands, the renewed token's narrower claims must not replace the API set.
    expect(permissions.isAuthoritative()).toBe(true);
    expect(permissions.hasPermission('b.write')).toBe(true);

    respond(1, ['a.read', 'c.read']);
    expect(permissions.hasPermission('c.read')).toBe(true);
    expect(permissions.hasPermission('b.write')).toBe(false);
  });

  it('keeps the API permissions when the refresh after renewal fails', async () => {
    await setup(['a.read', 'b.write']);
    tokens.next(jwt({ sub: 'u1', tenant_id: 't1', permissions: ['a.read'], iat: 2 }));
    permissionResponses[1].error(new Error('offline'));

    expect(permissions.isAuthoritative()).toBe(true);
    expect(permissions.hasPermission('b.write')).toBe(true);
  });

  it('drops the previous tenant\'s permissions on a tenant switch and reloads', async () => {
    await setup(['a.read', 'b.write']);

    authStore.setCurrentTenantId('t2');
    TestBed.tick();

    expect(permissionResponses.length).toBe(2);
    expect(permissions.isAuthoritative()).toBe(false);

    respond(1, ['x.read']);
    expect(permissions.isAuthoritative()).toBe(true);
    expect(permissions.hasPermission('x.read')).toBe(true);
    expect(permissions.hasPermission('b.write')).toBe(false);
  });

  it('falls back to the token claims when the API fails at sign-in with no cache', async () => {
    await setup(null);
    expect(permissions.isAuthoritative()).toBe(true);
    expect(permissions.hasPermission('a.read')).toBe(true);
    expect(permissions.hasPermission('b.write')).toBe(false);
  });
});

import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Router, UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, isObservable } from 'rxjs';
import { PermissionsService } from '@oluso/ui-core';
import {
  PERMISSION_GUARD_TIMEOUT_MS,
  allPermissionsGuard,
  anyPermissionGuard,
  permissionGuard,
} from './permission.guard';

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

type GuardResult = boolean | UrlTree | Observable<boolean | UrlTree>;

function route(data: Record<string, unknown>): ActivatedRouteSnapshot {
  return { data } as unknown as ActivatedRouteSnapshot;
}

function run(guard: CanActivateFn, data: Record<string, unknown>): GuardResult {
  return TestBed.runInInjectionContext(
    () => guard(route(data), {} as never) as GuardResult
  );
}

async function settle(result: GuardResult): Promise<boolean | UrlTree> {
  return isObservable(result) ? firstValueFrom(result) : result;
}

function isUnauthorized(result: boolean | UrlTree): boolean {
  return result instanceof UrlTree && TestBed.inject(Router).serializeUrl(result) === '/unauthorized';
}

describe('permission guards', () => {
  let permissions: PermissionsService;

  function setup(timeoutMs = 1000) {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: PERMISSION_GUARD_TIMEOUT_MS, useValue: timeoutMs }],
    });
    permissions = TestBed.inject(PermissionsService);
  }

  afterEach(() => localStorage.clear());

  it('waits while permissions load, then allows when granted', async () => {
    setup();
    const result = run(permissionGuard, { permission: 'x.read' });
    expect(isObservable(result)).toBe(true);

    const pending = settle(result);
    permissions.useIdentity('u1', 't1');
    permissions.initFromLoginResponse(['x.read'], []);
    TestBed.tick();

    expect(await pending).toBe(true);
  });

  it('waits while permissions load, then denies when not granted', async () => {
    setup();
    const pending = settle(run(permissionGuard, { permission: 'x.write' }));
    permissions.useIdentity('u1', 't1');
    permissions.initFromLoginResponse(['x.read'], []);
    TestBed.tick();

    expect(isUnauthorized(await pending)).toBe(true);
  });

  it('decides immediately once permissions are loaded', () => {
    setup();
    permissions.useIdentity('u1', 't1');
    permissions.initFromLoginResponse(['x.*'], []);

    expect(run(anyPermissionGuard, { permissions: ['y.read', 'x.read'] })).toBe(true);
    expect(isUnauthorized(run(allPermissionsGuard, { permissions: ['y.read', 'x.read'] }) as UrlTree)).toBe(true);
  });

  it('denies when the load fails', async () => {
    setup();
    const pending = settle(run(anyPermissionGuard, { permissions: ['x.read'] }));
    permissions.clear();
    TestBed.tick();

    expect(isUnauthorized(await pending)).toBe(true);
  });

  it('denies instead of hanging when permissions never load', async () => {
    setup(20);
    const result = await settle(run(allPermissionsGuard, { permissions: ['x.read'] }));
    expect(isUnauthorized(result)).toBe(true);
  });

  function seedCache(identity: string, effectivePermissions: string[]) {
    localStorage.setItem('oluso_permissions_cache', JSON.stringify({
      identity, effectivePermissions, rolePermissions: {}, isSuperAdmin: false,
      explicitGrantPermissions: [], timestamp: Date.now(),
    }));
  }

  it('accepts the same user\'s cache while offline', async () => {
    setup();
    seedCache('u1|t1', ['x.read']);
    permissions.useIdentity('u1', 't1');

    const pending = settle(run(permissionGuard, { permission: 'x.read' }));
    permissions.loadFromCache();
    permissions.setOffline(true);
    TestBed.tick();

    expect(await pending).toBe(true);
  });

  it('does not treat a same-user cache as authoritative while online', async () => {
    setup(30);
    seedCache('u1|t1', ['x.read']);
    permissions.useIdentity('u1', 't1');
    expect(permissions.isLoaded()).toBe(true);

    const result = await settle(run(permissionGuard, { permission: 'x.read' }));
    expect(isUnauthorized(result)).toBe(true);
  });

  it('ignores another user\'s cache', async () => {
    setup(30);
    seedCache('u1|t1', ['*']);
    permissions.useIdentity('u2', 't1');
    permissions.loadFromCache();
    permissions.setOffline(true);

    const result = await settle(run(permissionGuard, { permission: 'x.read' }));
    expect(isUnauthorized(result)).toBe(true);
  });

  it('allows routes without permission data', () => {
    setup();
    expect(run(permissionGuard, {})).toBe(true);
    expect(run(anyPermissionGuard, { permissions: [] })).toBe(true);
  });
});

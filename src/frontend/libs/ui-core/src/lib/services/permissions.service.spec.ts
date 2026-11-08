import { TestBed } from '@angular/core/testing';
import { PermissionsService } from './permissions.service';

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

const CACHE_KEY = 'oluso_permissions_cache';

function response(permissions: string[]) {
  return {
    effectivePermissions: permissions,
    rolePermissions: {},
    isSuperAdmin: false,
    explicitGrantPermissions: [],
  };
}

describe('PermissionsService', () => {
  let service: PermissionsService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(PermissionsService);
  });

  afterEach(() => localStorage.clear());

  it('does not read the cache before an identity is known', () => {
    localStorage.setItem(CACHE_KEY, JSON.stringify({ identity: 'u1|t1', ...response(['x.read']), timestamp: 0 }));
    const fresh = TestBed.runInInjectionContext(() => new PermissionsService());
    expect(fresh.isLoaded()).toBe(false);
    expect(fresh.isSettled()).toBe(false);
  });

  it('is not authoritative until permissions are loaded', () => {
    expect(service.isAuthoritative()).toBe(false);
    expect(service.isSettled()).toBe(false);
    expect(service.hasPermission('anything')).toBe(true);

    service.useIdentity('u1', 't1');
    service.initFromLoginResponse(['x.read'], []);

    expect(service.isAuthoritative()).toBe(true);
    expect(service.hasPermission('x.read')).toBe(true);
    expect(service.hasPermission('x.write')).toBe(false);
  });

  it('stamps the cache with the identity and restores it for the same user', () => {
    service.useIdentity('u1', 't1');
    service.setMyPermissions(response(['x.read']));
    expect(JSON.parse(localStorage.getItem(CACHE_KEY)!).identity).toBe('u1|t1');

    const next = TestBed.runInInjectionContext(() => new PermissionsService());
    next.useIdentity('u1', 't1');
    expect(next.isLoaded()).toBe(true);
    expect(next.isFromCache()).toBe(true);
    // A cache entry is only a UI hint until the API is known to be unreachable.
    expect(next.isAuthoritative()).toBe(false);

    next.setOffline(true);
    expect(next.isAuthoritative()).toBe(true);
    expect(next.hasPermission('x.read')).toBe(true);
  });

  it('ignores and drops a cache entry for another user or tenant', () => {
    service.useIdentity('u1', 't1');
    service.setMyPermissions(response(['*']));

    const other = TestBed.runInInjectionContext(() => new PermissionsService());
    other.useIdentity('u2', 't1');
    expect(other.isLoaded()).toBe(false);
    expect(other.loadFromCache()).toBe(false);
    expect(localStorage.getItem(CACHE_KEY)).toBeNull();

    service.useIdentity('u1', 't1');
    service.setMyPermissions(response(['*']));
    const otherTenant = TestBed.runInInjectionContext(() => new PermissionsService());
    otherTenant.useIdentity('u1', 't2');
    expect(otherTenant.isLoaded()).toBe(false);
  });

  it('drops the previous user\'s permissions when the identity changes', () => {
    service.useIdentity('u1', 't1');
    service.initFromLoginResponse(['*'], []);
    service.useIdentity('u2', 't1');
    expect(service.isLoaded()).toBe(false);
    expect(service.isAuthoritative()).toBe(false);
  });

  it('keeps permissions when the same identity is declared again', () => {
    service.useIdentity('u1', 't1');
    service.initFromLoginResponse(['x.read'], []);
    service.useIdentity('u1', 't1');
    expect(service.isAuthoritative()).toBe(true);
  });

  it('matches wildcards at any depth but never an explicit-grant permission', () => {
    service.useIdentity('u1', 't1');
    service.setMyPermissions({ ...response(['*', 'emr.roles.*']), explicitGrantPermissions: ['emr.roles.read', 'emr.administration'] });

    expect(service.hasPermission('users.read')).toBe(true);
    expect(service.hasPermission('emr.administration')).toBe(false);
    expect(service.hasPermission('emr.roles.read')).toBe(false);

    service.setMyPermissions(response(['emr.roles.*']));
    expect(service.hasPermission('emr.roles.read')).toBe(true);
    expect(service.hasPermission('emr.patients.view')).toBe(false);
  });

  it('settles as not authoritative after clear()', () => {
    service.useIdentity('u1', 't1');
    service.initFromLoginResponse(['x.read'], []);
    service.clear();
    expect(service.isSettled()).toBe(true);
    expect(service.isAuthoritative()).toBe(false);
    expect(localStorage.getItem(CACHE_KEY)).toBeNull();
  });
});

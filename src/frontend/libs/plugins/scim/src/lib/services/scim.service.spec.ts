import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ApiService } from '@syndew/oluso-admin-ui';
import { ScimService, internalPropertyOptions, transformationOptions } from './scim.service';

const API = 'https://idp.example/api/admin';

/** Each call and the admin API request it must make; the URLs are the backend controllers' routes. */
type Call = [
  name: string,
  call: (s: ScimService) => Observable<unknown>,
  method: string,
  url: string,
  expect?: { body?: unknown; params?: Record<string, string> },
];

const calls: Call[] = [
  ['getClients', (s) => s.getClients(), 'GET', '/scim/clients'],
  ['getClient', (s) => s.getClient('c1'), 'GET', '/scim/clients/c1'],
  [
    'createClient',
    (s) => s.createClient({ name: 'okta' } as never),
    'POST',
    '/scim/clients',
    { body: { name: 'okta' } },
  ],
  [
    'updateClient',
    (s) => s.updateClient('c1', { name: 'okta' } as never),
    'PUT',
    '/scim/clients/c1',
    { body: { name: 'okta' } },
  ],
  ['deleteClient', (s) => s.deleteClient('c1'), 'DELETE', '/scim/clients/c1'],
  ['rotateToken', (s) => s.rotateToken('c1'), 'POST', '/scim/clients/c1/rotate-token'],
  [
    'getLogs',
    (s) => s.getLogs('c1'),
    'GET',
    '/scim/clients/c1/logs',
    { params: { page: '1', pageSize: '50' } },
  ],
  ['getMappings', (s) => s.getMappings('c1'), 'GET', '/scim/clients/c1/mappings'],
  ['getMapping', (s) => s.getMapping('c1', 'm1'), 'GET', '/scim/clients/c1/mappings/m1'],
  ['getMappingOptions', (s) => s.getMappingOptions(), 'GET', '/scim/clients/mapping-options'],
  [
    'previewMappings',
    (s) => s.previewMappings('c1', { userName: 'ada' }),
    'POST',
    '/scim/clients/c1/mappings/preview',
    { body: { resource: { userName: 'ada' }, mode: 'create' } },
  ],
  [
    'createMapping',
    (s) => s.createMapping('c1', { scimAttribute: 'userName' } as never),
    'POST',
    '/scim/clients/c1/mappings',
    { body: { scimAttribute: 'userName' } },
  ],
  [
    'updateMapping',
    (s) => s.updateMapping('c1', 'm1', { scimAttribute: 'userName' } as never),
    'PUT',
    '/scim/clients/c1/mappings/m1',
    { body: { scimAttribute: 'userName' } },
  ],
  ['deleteMapping', (s) => s.deleteMapping('c1', 'm1'), 'DELETE', '/scim/clients/c1/mappings/m1'],
  [
    'applyDefaultMappings',
    (s) => s.applyDefaultMappings('c1', 'okta'),
    'POST',
    '/scim/clients/c1/mappings/defaults',
    { body: { provider: 'okta' } },
  ],
  ['getScimEndpointUrl', (s) => s.getScimEndpointUrl(), 'GET', '/scim/clients/endpoint-url'],
  ['getProviderOptions', (s) => s.getProviderOptions(), 'GET', '/scim/providers/options'],
  ['getProviders', (s) => s.getProviders(), 'GET', '/scim/providers'],
  ['getProvider', (s) => s.getProvider('p1'), 'GET', '/scim/providers/p1'],
  [
    'createProvider',
    (s) => s.createProvider({ name: 'mail' } as never),
    'POST',
    '/scim/providers',
    { body: { name: 'mail' } },
  ],
  [
    'updateProvider',
    (s) => s.updateProvider('p1', { name: 'mail' } as never),
    'PUT',
    '/scim/providers/p1',
    { body: { name: 'mail' } },
  ],
  ['deleteProvider', (s) => s.deleteProvider('p1'), 'DELETE', '/scim/providers/p1'],
  [
    'setProviderSecret',
    (s) => s.setProviderSecret('p1', 'tok'),
    'PUT',
    '/scim/providers/p1/secret',
    { body: { secret: 'tok' } },
  ],
  ['testProvider', (s) => s.testProvider('p1'), 'POST', '/scim/providers/p1/test'],
  ['reconcileProvider', (s) => s.reconcileProvider('p1'), 'POST', '/scim/providers/p1/reconcile'],
  [
    'previewProvider',
    (s) => s.previewProvider('p1', 'u1'),
    'POST',
    '/scim/providers/p1/preview',
    { body: { userId: 'u1' } },
  ],
  [
    'getProviderQueue',
    (s) => s.getProviderQueue('p1', 'Failed'),
    'GET',
    '/scim/providers/p1/queue',
    { params: { skip: '0', take: '50', status: 'Failed' } },
  ],
  [
    'retryProviderQueue',
    (s) => s.retryProviderQueue('p1'),
    'POST',
    '/scim/providers/p1/queue/retry',
  ],
  [
    'getProviderLogs failures only',
    (s) => s.getProviderLogs('p1', 1, 50, false),
    'GET',
    '/scim/providers/p1/logs',
    { params: { success: 'false', page: '1', pageSize: '50' } },
  ],
  [
    'getProviderResources',
    (s) => s.getProviderResources('p1', 'Group'),
    'GET',
    '/scim/providers/p1/resources',
    { params: { type: 'Group', page: '1', pageSize: '50' } },
  ],
  ['getProviderMappings', (s) => s.getProviderMappings('p1'), 'GET', '/scim/providers/p1/mappings'],
  [
    'createProviderMapping',
    (s) => s.createProviderMapping('p1', { scimAttribute: 'userName' } as never),
    'POST',
    '/scim/providers/p1/mappings',
    { body: { scimAttribute: 'userName' } },
  ],
  [
    'updateProviderMapping',
    (s) => s.updateProviderMapping('p1', 'm1', { scimAttribute: 'userName' } as never),
    'PUT',
    '/scim/providers/p1/mappings/m1',
    { body: { scimAttribute: 'userName' } },
  ],
  [
    'deleteProviderMapping',
    (s) => s.deleteProviderMapping('p1', 'm1'),
    'DELETE',
    '/scim/providers/p1/mappings/m1',
  ],
  [
    'applyProviderDefaultMappings',
    (s) => s.applyProviderDefaultMappings('p1'),
    'POST',
    '/scim/providers/p1/mappings/defaults',
  ],
];

describe('ScimService', () => {
  let service: ScimService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiService).setBaseUrl(API);
    service = TestBed.inject(ScimService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it.each(calls)('%s', (_name, call, method, url, expected) => {
    call(service).subscribe();
    const req = http.expectOne((r) => r.urlWithParams.split('?')[0] === API + url);
    expect(req.request.method).toBe(method);
    if (expected?.body !== undefined) expect(req.request.body).toEqual(expected.body);
    const params = Object.fromEntries(
      req.request.params.keys().map((k) => [k, req.request.params.get(k)]),
    );
    expect(params).toEqual(expected?.params ?? {});
    req.flush(null);
  });
});

describe('mapping option labels', () => {
  it('labels the properties and transformations the server lists, in its order', () => {
    expect(internalPropertyOptions(['Email', 'FirstName'])).toEqual([
      { name: 'Email', displayName: 'Email' },
      { name: 'FirstName', displayName: 'First name' },
    ]);
    expect(transformationOptions(['none', 'lowercase']).map((t) => t.description)).toEqual([
      'No transformation',
      'Convert to lowercase',
    ]);
  });

  it('offers a property or transformation the server adds later under its own name', () => {
    expect(internalPropertyOptions(['Department'])).toEqual([
      { name: 'Department', displayName: 'Department' },
    ]);
    expect(transformationOptions(['slugify'])).toEqual([
      { name: 'slugify', description: 'slugify' },
    ]);
  });
});

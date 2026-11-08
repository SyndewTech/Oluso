import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ApiService } from '@oluso/admin-ui';
import { LdapService } from './ldap.service';

const API = 'https://idp.example/api/admin';

/** Each call and the admin API request it must make; the URLs are the backend controllers' routes. */
type Call = [
  name: string,
  call: (s: LdapService) => Observable<unknown>,
  method: string,
  url: string,
  expect?: { body?: unknown; params?: Record<string, string> },
];

const calls: Call[] = [
  ['getServerInfo', (s) => s.getServerInfo(), 'GET', '/ldap/server-info'],
  ['getSettings', (s) => s.getSettings(), 'GET', '/ldap/settings'],
  [
    'updateSettings',
    (s) => s.updateSettings({ enabled: true } as never),
    'PUT',
    '/ldap/settings',
    { body: { enabled: true } },
  ],
  ['testConnection', (s) => s.testConnection(), 'POST', '/ldap/test-connection'],
  ['getTlsCertificate', (s) => s.getTlsCertificate(), 'GET', '/ldap/certificate'],
  [
    'generateTlsCertificate',
    (s) => s.generateTlsCertificate(),
    'POST',
    '/ldap/certificate/generate',
  ],
  [
    'uploadTlsCertificate',
    (s) => s.uploadTlsCertificate({ pfxBase64: 'x' } as never),
    'POST',
    '/ldap/certificate/upload',
    { body: { pfxBase64: 'x' } },
  ],
  ['resetTlsCertificate', (s) => s.resetTlsCertificate(), 'POST', '/ldap/certificate/reset'],
  ['getServiceAccounts', (s) => s.getServiceAccounts(), 'GET', '/ldap/service-accounts'],
  ['getServiceAccount', (s) => s.getServiceAccount('sa1'), 'GET', '/ldap/service-accounts/sa1'],
  [
    'createServiceAccount',
    (s) => s.createServiceAccount({ name: 'reader' } as never),
    'POST',
    '/ldap/service-accounts',
    { body: { name: 'reader' } },
  ],
  [
    'updateServiceAccount',
    (s) => s.updateServiceAccount('sa1', { name: 'reader' } as never),
    'PUT',
    '/ldap/service-accounts/sa1',
    { body: { name: 'reader' } },
  ],
  [
    'resetServiceAccountPassword',
    (s) => s.resetServiceAccountPassword('sa1', { newPassword: 'p' } as never),
    'POST',
    '/ldap/service-accounts/sa1/reset-password',
    { body: { newPassword: 'p' } },
  ],
  [
    'deleteServiceAccount',
    (s) => s.deleteServiceAccount('sa1'),
    'DELETE',
    '/ldap/service-accounts/sa1',
  ],
];

describe('LdapService', () => {
  let service: LdapService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiService).setBaseUrl(API);
    service = TestBed.inject(LdapService);
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

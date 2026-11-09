import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ApiService } from '@syndew/oluso-admin-ui';
import { SamlService } from './saml.service';

const API = 'https://idp.example/api/admin';

/** Each call and the admin API request it must make; the URLs are the backend controllers' routes. */
type Call = [
  name: string,
  call: (s: SamlService) => Observable<unknown>,
  method: string,
  url: string,
  expect?: { body?: unknown; params?: Record<string, string> },
];

const calls: Call[] = [
  ['getIdpInfo', (s) => s.getIdpInfo(), 'GET', '/saml/service-providers/idp-info'],
  [
    'checkEntityIdAvailability',
    (s) => s.checkEntityIdAvailability('urn:sp/a b'),
    'GET',
    '/saml/service-providers/check-entity-id/urn%3Asp%2Fa%20b',
  ],
  [
    'checkEntityIdAvailability excluding one',
    (s) => s.checkEntityIdAvailability('urn:sp', 7),
    'GET',
    '/saml/service-providers/check-entity-id/urn%3Asp',
    { params: { excludeId: '7' } },
  ],
  [
    'getAll',
    (s) => s.getAll(),
    'GET',
    '/saml/service-providers',
    { params: { includeDisabled: 'false' } },
  ],
  [
    'getAll including disabled',
    (s) => s.getAll(true),
    'GET',
    '/saml/service-providers',
    { params: { includeDisabled: 'true' } },
  ],
  ['getById', (s) => s.getById(3), 'GET', '/saml/service-providers/3'],
  ['getForEdit', (s) => s.getForEdit(3), 'GET', '/saml/service-providers/3/edit'],
  [
    'create',
    (s) => s.create({ entityId: 'urn:sp' } as never),
    'POST',
    '/saml/service-providers',
    { body: { entityId: 'urn:sp' } },
  ],
  [
    'update',
    (s) => s.update(3, { entityId: 'urn:sp' } as never),
    'PUT',
    '/saml/service-providers/3',
    { body: { entityId: 'urn:sp' } },
  ],
  ['delete', (s) => s.delete(3), 'DELETE', '/saml/service-providers/3'],
  ['toggle', (s) => s.toggle(3), 'POST', '/saml/service-providers/3/toggle'],
  ['test', (s) => s.test(3), 'POST', '/saml/service-providers/3/test'],
  [
    'getIdpConfiguration',
    (s) => s.getIdpConfiguration(),
    'GET',
    '/saml/service-providers/idp-configuration',
  ],
  [
    'updateIdpConfiguration',
    (s) => s.updateIdpConfiguration({ enabled: true } as never),
    'PUT',
    '/saml/service-providers/idp-configuration',
    { body: { enabled: true } },
  ],
  [
    'getSigningCertificate',
    (s) => s.getSigningCertificate(),
    'GET',
    '/saml/service-providers/idp-certificate/signing',
  ],
  [
    'getEncryptionCertificate',
    (s) => s.getEncryptionCertificate(),
    'GET',
    '/saml/service-providers/idp-certificate/encryption',
  ],
  [
    'generateSigningCertificate',
    (s) => s.generateSigningCertificate(),
    'POST',
    '/saml/service-providers/idp-certificate/signing/generate',
  ],
  [
    'generateEncryptionCertificate',
    (s) => s.generateEncryptionCertificate(),
    'POST',
    '/saml/service-providers/idp-certificate/encryption/generate',
  ],
  [
    'uploadSigningCertificate',
    (s) => s.uploadSigningCertificate({ pfxBase64: 'x' } as never),
    'POST',
    '/saml/service-providers/idp-certificate/signing/upload',
    { body: { pfxBase64: 'x' } },
  ],
  [
    'uploadEncryptionCertificate',
    (s) => s.uploadEncryptionCertificate({ pfxBase64: 'x' } as never),
    'POST',
    '/saml/service-providers/idp-certificate/encryption/upload',
    { body: { pfxBase64: 'x' } },
  ],
  [
    'resetSigningCertificate',
    (s) => s.resetSigningCertificate(),
    'POST',
    '/saml/service-providers/idp-certificate/signing/reset',
  ],
  [
    'resetEncryptionCertificate',
    (s) => s.resetEncryptionCertificate(),
    'POST',
    '/saml/service-providers/idp-certificate/encryption/reset',
  ],
];

describe('SamlService', () => {
  let service: SamlService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiService).setBaseUrl(API);
    service = TestBed.inject(SamlService);
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

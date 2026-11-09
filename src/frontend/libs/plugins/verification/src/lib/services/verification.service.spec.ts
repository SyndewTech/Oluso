import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ApiService } from '@syndew/oluso-admin-ui';
import { VerificationService } from './verification.service';

const API = 'https://idp.example/api/admin';

/** Each call and the admin API request it must make; the URLs are the backend controllers' routes. */
type Call = [
  name: string,
  call: (s: VerificationService) => Observable<unknown>,
  method: string,
  url: string,
  expect?: { body?: unknown; params?: Record<string, string> },
];

const calls: Call[] = [
  ['getProviders', (s) => s.getProviders(), 'GET', '/business-verification/providers'],
  ['getCountries', (s) => s.getCountries(), 'GET', '/business-verification/countries'],
  ['getSettings', (s) => s.getSettings(), 'GET', '/business-verification/settings'],
  [
    'updateSettings',
    (s) => s.updateSettings({ enabled: true } as never),
    'PUT',
    '/business-verification/settings',
    { body: { enabled: true } },
  ],
  [
    'testProvider',
    (s) => s.testProvider('dojah'),
    'POST',
    '/business-verification/providers/dojah/test',
  ],
  [
    'listBusinesses',
    (s) => s.listBusinesses('', '', 1, 20),
    'GET',
    '/business-verification/businesses',
    { params: { page: '1', pageSize: '20' } },
  ],
  [
    'listBusinesses filtered',
    (s) => s.listBusinesses('Verified', 'acme', 2, 20),
    'GET',
    '/business-verification/businesses',
    { params: { page: '2', pageSize: '20', status: 'Verified', search: 'acme' } },
  ],
  ['getBusiness', (s) => s.getBusiness('b/1'), 'GET', '/business-verification/businesses/b%2F1'],
  [
    'reviewQueue',
    (s) => s.reviewQueue(1, 20),
    'GET',
    '/business-verification/review-queue',
    { params: { page: '1', pageSize: '20' } },
  ],
  [
    'reviewBusiness',
    (s) => s.reviewBusiness('b1', 'Approve' as never, 'ok'),
    'POST',
    '/business-verification/businesses/b1/review',
    { body: { decision: 'Approve', note: 'ok' } },
  ],
  [
    'reviewApplicant',
    (s) => s.reviewApplicant('a1', 'Reject' as never),
    'POST',
    '/business-verification/applicants/a1/review',
    { body: { decision: 'Reject', note: undefined } },
  ],
  [
    'refreshApplicant',
    (s) => s.refreshApplicant('a1'),
    'POST',
    '/business-verification/applicants/a1/refresh',
  ],
  [
    'downloadDocument',
    (s) => s.downloadDocument('a1', 'd1'),
    'GET',
    '/business-verification/applicants/a1/documents/d1',
  ],
];

describe('VerificationService', () => {
  let service: VerificationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiService).setBaseUrl(API);
    service = TestBed.inject(VerificationService);
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

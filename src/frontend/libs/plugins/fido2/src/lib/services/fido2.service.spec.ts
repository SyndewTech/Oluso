import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ApiService } from '@syndew/oluso-admin-ui';
import { Fido2Service } from './fido2.service';

const API = 'https://idp.example/api/admin';

/** Each call and the admin API request it must make; the URLs are the backend controllers' routes. */
type Call = [
  name: string,
  call: (s: Fido2Service) => Observable<unknown>,
  method: string,
  url: string,
  expect?: { body?: unknown; params?: Record<string, string> },
];

const calls: Call[] = [
  ['getStats', (s) => s.getStats(), 'GET', '/fido2/stats'],
  ['getUsersWithCredentials', (s) => s.getUsersWithCredentials(), 'GET', '/fido2/users'],
  [
    'getUsersWithCredentials filtered',
    (s) => s.getUsersWithCredentials({ search: 'ada', page: 2, pageSize: 25 }),
    'GET',
    '/fido2/users',
    { params: { search: 'ada', page: '2', pageSize: '25' } },
  ],
  ['getUserCredentials', (s) => s.getUserCredentials('u1'), 'GET', '/fido2/users/u1/credentials'],
  [
    'deleteUserCredential',
    (s) => s.deleteUserCredential('u1', 'c1'),
    'DELETE',
    '/fido2/users/u1/credentials/c1',
  ],
];

describe('Fido2Service', () => {
  let service: Fido2Service;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiService).setBaseUrl(API);
    service = TestBed.inject(Fido2Service);
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

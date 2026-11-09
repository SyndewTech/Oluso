import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ApiService } from '@syndew/oluso-admin-ui';
import { TelemetryService } from './telemetry.service';

const API = 'https://idp.example/api/admin';

/** Each call and the admin API request it must make; the URLs are the backend controllers' routes. */
type Call = [
  name: string,
  call: (s: TelemetryService) => Observable<unknown>,
  method: string,
  url: string,
  expect?: { body?: unknown; params?: Record<string, string> },
];

const calls: Call[] = [
  [
    'getOverviewMetrics',
    (s) => s.getOverviewMetrics(),
    'GET',
    '/telemetry/metrics/overview',
    { params: { timeRange: '24h' } },
  ],
  [
    'getMetricSeries',
    (s) => s.getMetricSeries('requests', '7d'),
    'GET',
    '/telemetry/metrics/requests',
    { params: { timeRange: '7d', interval: '6h' } },
  ],
  [
    'getLogs leaves out empty filters',
    (s) =>
      s.getLogs({ level: 'Error', category: '', searchTerm: undefined, page: 1, pageSize: 50 }),
    'GET',
    '/telemetry/logs',
    { params: { level: 'Error', page: '1', pageSize: '50' } },
  ],
  ['getLogCategories', (s) => s.getLogCategories(), 'GET', '/telemetry/logs/categories'],
  [
    'getTraces',
    (s) => s.getTraces({ traceId: 'abc' }),
    'GET',
    '/telemetry/traces',
    { params: { traceId: 'abc' } },
  ],
  ['getTrace', (s) => s.getTrace('abc'), 'GET', '/telemetry/traces/abc'],
  ['getTraceBackendStatus', (s) => s.getTraceBackendStatus(), 'GET', '/telemetry/traces/status'],
  ['getLogBackendStatus', (s) => s.getLogBackendStatus(), 'GET', '/telemetry/logs/status'],
];

describe('TelemetryService', () => {
  let service: TelemetryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiService).setBaseUrl(API);
    service = TestBed.inject(TelemetryService);
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

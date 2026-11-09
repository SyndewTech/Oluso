import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  PasskeysAccountService,
  arrayBufferToBase64Url,
  base64UrlToArrayBuffer,
  convertOptionsForWebAuthn,
} from './passkeys-account.service';

const SERVER = 'https://idp.example';

describe('PasskeysAccountService', () => {
  let service: PasskeysAccountService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PasskeysAccountService);
    service.setBaseUrl(SERVER);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the signed-in user’s passkeys', () => {
    service.getPasskeys().subscribe();
    http
      .expectOne({ method: 'GET', url: `${SERVER}/api/account/passkeys` })
      .flush({ passkeys: [], isEnabled: true });
  });

  it('starts a registration asking for a discoverable credential by default', () => {
    service.startRegistration().subscribe();
    const req = http.expectOne({
      method: 'POST',
      url: `${SERVER}/api/account/passkeys/register/start`,
    });
    expect(req.request.body).toEqual({ requireDiscoverableCredential: true });
    req.flush({});
  });

  it('completes a registration with the attestation and the name', () => {
    service.completeRegistration('reg-1', 'attestation', 'Laptop').subscribe();
    const req = http.expectOne({
      method: 'POST',
      url: `${SERVER}/api/account/passkeys/register/complete`,
    });
    expect(req.request.body).toEqual({
      registrationId: 'reg-1',
      attestationResponse: 'attestation',
      name: 'Laptop',
    });
    req.flush({ success: true });
  });

  it('renames and deletes a passkey', () => {
    service.updatePasskey('pk1', 'Phone').subscribe();
    const rename = http.expectOne({ method: 'PATCH', url: `${SERVER}/api/account/passkeys/pk1` });
    expect(rename.request.body).toEqual({ name: 'Phone' });
    rename.flush(null);

    service.deletePasskey('pk1').subscribe();
    http.expectOne({ method: 'DELETE', url: `${SERVER}/api/account/passkeys/pk1` }).flush(null);
  });
});

describe('WebAuthn encoding', () => {
  it('round-trips bytes through base64url without padding or + and /', () => {
    const bytes = new Uint8Array([0xfb, 0xff, 0xbf, 0x00, 0x01]);
    const encoded = arrayBufferToBase64Url(bytes.buffer);

    expect(encoded).toBe('-_-_AAE');
    expect(new Uint8Array(base64UrlToArrayBuffer(encoded))).toEqual(bytes);
  });

  it('turns the server’s options into browser creation options', () => {
    const options = convertOptionsForWebAuthn({
      challenge: 'AQID',
      rp: { id: 'idp.example', name: 'Oluso' },
      user: { id: 'BAU', name: 'ada@example.org', displayName: 'Ada' },
      excludeCredentials: [{ type: 'public-key', id: 'Bgc', transports: ['internal'] }],
    });

    expect(new Uint8Array(options.challenge as ArrayBuffer)).toEqual(new Uint8Array([1, 2, 3]));
    expect(new Uint8Array(options.user.id as ArrayBuffer)).toEqual(new Uint8Array([4, 5]));
    expect(options.pubKeyCredParams.map((p) => p.alg)).toEqual([-7, -257]);
    expect(options.timeout).toBe(60000);
    expect(options.attestation).toBe('none');
    expect(
      options.excludeCredentials?.map((c) => Array.from(new Uint8Array(c.id as ArrayBuffer))),
    ).toEqual([[6, 7]]);
  });
});

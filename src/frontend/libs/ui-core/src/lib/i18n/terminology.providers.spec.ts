import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TranslocoService } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { LocaleService } from '../services/locale.service';
import { loadTerminology, provideTerminology } from './terminology.providers';

describe('loadTerminology', () => {
  it('resolves keys for a tenant locale outside the configured languages', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTerminology({ endpoint: '/api/terminology', defaultLang: 'en-US' }),
        { provide: LocaleService, useValue: { locale: signal('en-NG') } },
      ],
    });
    const transloco = TestBed.inject(TranslocoService);
    const http = TestBed.inject(HttpTestingController);

    const loading = loadTerminology(TestBed.inject(Injector));
    http.expectOne((r) => r.url === '/api/terminology' && r.params.get('locale') === 'en-NG')
      .flush({ 'emr.ambulance.plural': 'Ambulances' });
    await loading;

    expect(transloco.getActiveLang()).toBe('en-NG');
    expect(transloco.translate('emr.ambulance.plural')).toBe('Ambulances');
  });
});

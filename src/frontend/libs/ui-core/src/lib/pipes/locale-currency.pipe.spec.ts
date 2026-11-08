import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { LocaleCurrencyPipe } from './locale-currency.pipe';
import { LocaleService } from '../services/locale.service';

describe('LocaleCurrencyPipe', () => {
  function setup(tenantCurrency: string | null = 'GBP') {
    const currencies = [
      { code: 'GBP', name: 'Pound', symbol: '£', decimalPlaces: 2, common: true },
      { code: 'JPY', name: 'Yen', symbol: '¥', decimalPlaces: 0, common: true },
      { code: 'KWD', name: 'Kuwaiti Dinar', symbol: 'د.ك', decimalPlaces: 3, common: false },
    ];
    TestBed.configureTestingModule({
      providers: [
        LocaleCurrencyPipe,
        { provide: LocaleService, useValue: { getSupportedCurrencies: () => of(currencies), currencyCode: signal(tenantCurrency), locale: signal('en-GB') } },
      ],
    });
    return TestBed.inject(LocaleCurrencyPipe);
  }

  it("uses the currency's own decimal places", () => {
    const pipe = setup();
    expect(pipe.transform(12.3456, 'KWD')).toContain('12.346');
    expect(pipe.transform(1234.5, 'JPY')).toBe('¥1,235');
    expect(pipe.transform(12.5, 'GBP')).toBe('£12.50');
  });

  it('honours an explicit digits argument', () => {
    expect(setup().transform(1234.56, 'GBP', '1.0-0')).toBe('£1,235');
  });

  it("uses the organisation's currency only when no currency is written", () => {
    const pipe = setup('JPY');
    expect(pipe.transform(500)).toBe('¥500');
    expect(pipe.transform(1234.5, '1.0-0')).toBe('¥1,235');
    expect(pipe.transform(500, null)).toBe('');
    expect(pipe.transform(500, undefined)).toBe('');
  });

  it('renders nothing for an unknown currency', () => {
    expect(setup().transform(5, 'XYZ')).toBe('');
  });
});

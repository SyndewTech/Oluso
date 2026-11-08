import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CurrencyCatalogService } from './currency-catalog.service';
import { LocaleService } from './locale.service';

describe('CurrencyCatalogService minor units', () => {
  function setup(loaded = true) {
    const currencies = [
      { code: 'GBP', name: 'British Pound', symbol: '£', decimalPlaces: 2, common: true },
      { code: 'JPY', name: 'Japanese Yen', symbol: '¥', decimalPlaces: 0, common: true },
      { code: 'KWD', name: 'Kuwaiti Dinar', symbol: 'د.ك', decimalPlaces: 3, common: false },
    ];
    TestBed.configureTestingModule({
      providers: [
        { provide: LocaleService, useValue: { getSupportedCurrencies: () => (loaded ? of(currencies) : of()) } },
      ],
    });
    return TestBed.inject(CurrencyCatalogService);
  }

  it('uses each currency\'s own exponent', () => {
    const catalog = setup();
    expect(catalog.exponent('kwd')).toBe(3);
    expect(catalog.toMinor(12.345, 'KWD')).toBe(12345);
    expect(catalog.toMinor(1234, 'JPY')).toBe(1234);
    expect(catalog.fromMinor(1234, 'GBP')).toBe(12.34);
  });

  it('refuses an unknown currency instead of assuming two decimals', () => {
    expect(() => setup().exponent('XYZ')).toThrow();
  });

  it('refuses before the currency list has loaded', () => {
    expect(() => setup(false).exponent('GBP')).toThrow();
  });
});

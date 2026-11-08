import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { MinorUnitsPipe } from './minor-units.pipe';
import { LocaleService } from '../services/locale.service';

describe('MinorUnitsPipe', () => {
  function setup(tenantCurrency: string | null = 'NGN') {
    const currencies = [
      { code: 'NGN', name: 'Naira', symbol: '₦', decimalPlaces: 2, common: true },
      { code: 'JPY', name: 'Yen', symbol: '¥', decimalPlaces: 0, common: true },
      { code: 'KWD', name: 'Kuwaiti Dinar', symbol: 'د.ك', decimalPlaces: 3, common: false },
    ];
    TestBed.configureTestingModule({
      providers: [
        MinorUnitsPipe,
        {
          provide: LocaleService,
          useValue: { getSupportedCurrencies: () => of(currencies), currencyCode: signal(tenantCurrency), locale: signal('en-GB') },
        },
      ],
    });
    return TestBed.inject(MinorUnitsPipe);
  }

  it("uses each currency's own decimal places", () => {
    const pipe = setup();
    expect(pipe.transform(12345, 'KWD')).toContain('12.345');
    expect(pipe.transform(12345, 'JPY')).toBe('¥12,345');
    expect(pipe.transform(12345, 'NGN')).toBe('₦123.45');
  });

  it("uses the organisation's currency only when no currency is written", () => {
    const pipe = setup('JPY');
    expect(pipe.transform(500)).toBe('¥500');
    expect(pipe.transform(500, null)).toBe('');
  });

  it('drops decimals for whole-unit summaries', () => {
    expect(setup().transform(1234567, 'NGN', true)).toBe('₦12,346');
  });

  it('renders nothing for an unknown currency or no amount', () => {
    const pipe = setup();
    expect(pipe.transform(100, 'XYZ')).toBe('');
    expect(pipe.transform(null, 'NGN')).toBe('');
  });
});

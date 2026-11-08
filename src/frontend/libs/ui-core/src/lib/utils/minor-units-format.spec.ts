import { describe, expect, it } from 'vitest';
import { formatMinorUnits } from './minor-units';

describe('formatMinorUnits', () => {
  it('shows each currency at its own decimal places', () => {
    expect(formatMinorUnits(12345, 'GBP', 2, 'en-GB')).toBe('£123.45');
    expect(formatMinorUnits(12345, 'JPY', 0, 'en-GB')).toBe('¥12,345');
    expect(formatMinorUnits(12345, 'KWD', 3, 'en-GB')).toBe('KWD\u00a012.345');
  });

  it('shows nothing while the currency or its decimal places are unknown, rather than guessing two', () => {
    expect(formatMinorUnits(12345, 'JPY', null, 'en-GB')).toBe('');
    expect(formatMinorUnits(12345, null, 2, 'en-GB')).toBe('');
    expect(formatMinorUnits(12345, '', 2, 'en-GB')).toBe('');
  });

  it('shows nothing for a missing amount', () => {
    expect(formatMinorUnits(null, 'KWD', 3, 'en-GB')).toBe('');
  });
});

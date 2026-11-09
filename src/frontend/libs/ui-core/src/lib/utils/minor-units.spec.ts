import { fromMinorUnits, toMinorUnits } from './minor-units';

describe('minor units', () => {
  it.each([
    [12.34, 2, 1234],
    [1234, 0, 1234],
    [1234.5, 0, 1235],
    [12.345, 3, 12345],
    [1.005, 2, 101],
    [0.015, 2, 2],
    [-0.015, 2, -2],
    [0, 2, 0],
  ])('%s at exponent %s is %s minor units', (amount, exponent, minor) => {
    expect(toMinorUnits(amount, exponent)).toBe(minor);
  });

  it.each([
    [1234, 2, 12.34],
    [1234, 0, 1234],
    [12345, 3, 12.345],
    [-5, 2, -0.05],
  ])('%s minor units at exponent %s read back as %s', (minor, exponent, amount) => {
    expect(fromMinorUnits(minor, exponent)).toBe(amount);
  });

  it('refuses a missing or invalid exponent', () => {
    expect(() => toMinorUnits(1, -1)).toThrow();
    expect(() => fromMinorUnits(1, 1.5)).toThrow();
  });
});

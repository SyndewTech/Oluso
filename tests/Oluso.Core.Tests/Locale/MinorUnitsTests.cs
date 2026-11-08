using FluentAssertions;
using Oluso.Core.Locale;
using Xunit;

namespace Oluso.Core.Tests.Locale;

public class MinorUnitsTests
{
    [Theory]
    [InlineData("GBP", 2)]
    [InlineData("jpy", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("KRW", 0)]
    public void The_exponent_is_the_currencys_decimal_places(string code, int exponent) =>
        MinorUnits.Exponent(code).Should().Be(exponent);

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_currency_has_no_exponent(string? code)
    {
        var exponent = () => MinorUnits.Exponent(code!);
        var toMinor = () => MinorUnits.ToMinor(1m, code!);
        var fromMinor = () => MinorUnits.FromMinor(1, code!);

        exponent.Should().Throw<ArgumentException>();
        toMinor.Should().Throw<ArgumentException>();
        fromMinor.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(12.34, "GBP", 1234)]
    [InlineData(1234, "JPY", 1234)]
    [InlineData(1234.5, "JPY", 1235)]
    [InlineData(12.345, "KWD", 12345)]
    [InlineData(0.0005, "KWD", 1)]
    [InlineData(0.015, "USD", 2)]
    [InlineData(-0.015, "USD", -2)]
    public void Amounts_become_whole_minor_units_rounded_half_away_from_zero(decimal amount, string code, long minor) =>
        MinorUnits.ToMinor(amount, code).Should().Be(minor);

    [Theory]
    [InlineData(1234, "GBP", 12.34)]
    [InlineData(1234, "JPY", 1234)]
    [InlineData(12345, "KWD", 12.345)]
    public void Minor_units_read_back_as_major_units(long minor, string code, decimal amount) =>
        MinorUnits.FromMinor(minor, code).Should().Be(amount);

    [Theory]
    // 1 GBP = 0.4 KWD: 12.345 KWD is 30.8625 GBP, 30.86 in pence.
    [InlineData(12345, "KWD", "GBP", 0.4, 3086)]
    // 1 JPY-base unit = 0.0053 GBP: £10.00 is ¥1,886.79, ¥1,887.
    [InlineData(1000, "GBP", "JPY", 0.0053, 1887)]
    // 1 GBP = 190 JPY: ¥1,000 is £5.263, 526p.
    [InlineData(1000, "JPY", "GBP", 190, 526)]
    // 1 GBP = 0.4 KWD: £30.86 is 12.344 KWD.
    [InlineData(3086, "GBP", "KWD", 2.5, 12344)]
    [InlineData(1234, "GBP", "GBP", 1, 1234)]
    [InlineData(1000, "USD", "GBP", 1.25, 800)]
    public void Convert_moves_between_exponents_at_the_rate(long minor, string from, string to, decimal rate, long expected) =>
        MinorUnits.Convert(minor, from, to, rate).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Convert_refuses_a_non_positive_rate(decimal rate)
    {
        var convert = () => MinorUnits.Convert(100, "GBP", "USD", rate);

        convert.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(123456, "GBP", "1,234.56")]
    [InlineData(123456, "JPY", "123,456")]
    [InlineData(123456, "KWD", "123.456")]
    [InlineData(-5, "GBP", "-0.05")]
    public void Format_uses_the_currencys_own_decimal_places(long minor, string currency, string expected) =>
        MinorUnits.Format(minor, currency).Should().Be(expected);

    [Theory]
    [InlineData(12.3456, "GBP", 12.35)]
    [InlineData(12.5, "JPY", 13)]
    [InlineData(12.3455, "KWD", 12.346)]
    [InlineData(-0.005, "GBP", -0.01)]
    public void Round_goes_to_the_currencys_own_decimal_places(decimal amount, string currency, decimal expected) =>
        MinorUnits.Round(amount, currency).Should().Be(expected);

    [Fact]
    public void Round_refuses_an_unknown_currency()
    {
        var round = () => MinorUnits.Round(1m, "XYZ");

        round.Should().Throw<ArgumentException>();
    }
}

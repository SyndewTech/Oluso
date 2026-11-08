using System.Text.RegularExpressions;
using FluentAssertions;
using Oluso.Core.Locale;
using Xunit;

namespace Oluso.Core.Tests.Locale;

public class SupportedCurrenciesTests
{
    [Fact]
    public void Covers_the_iso_4217_list_with_common_currencies_first()
    {
        SupportedCurrencies.All.Count.Should().BeGreaterThan(150);
        SupportedCurrencies.All.Select(c => c.Code).Should().OnlyHaveUniqueItems();
        SupportedCurrencies.All.Should().OnlyContain(c => Regex.IsMatch(c.Code, "^[A-Z]{3}$"));
        SupportedCurrencies.All.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Symbol));

        SupportedCurrencies.All.Take(SupportedCurrencies.Common.Count).Should().OnlyContain(c => c.Common);
        SupportedCurrencies.All.Skip(SupportedCurrencies.Common.Count).Should().OnlyContain(c => !c.Common)
            .And.BeInAscendingOrder(c => c.Code, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("NGN", "Nigerian Naira", 2, true)]
    [InlineData("USD", "US Dollar", 2, true)]
    [InlineData("JPY", "Japanese Yen", 0, true)]
    [InlineData("KWD", "Kuwaiti Dinar", 3, false)]
    [InlineData("XOF", "West African CFA Franc", 0, false)]
    public void Knows_minor_units_and_lookup_is_case_insensitive(string code, string name, int decimals, bool common)
    {
        var currency = SupportedCurrencies.GetByCode(code.ToLowerInvariant());
        currency.Should().NotBeNull();
        currency!.Name.Should().Be(name);
        currency.DecimalPlaces.Should().Be(decimals);
        currency.Common.Should().Be(common);
        SupportedCurrencies.IsSupported("XXX").Should().BeFalse();
    }
}

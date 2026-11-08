using FluentAssertions;
using Oluso.Core.Locale;
using Xunit;

namespace Oluso.Tests.UserJourneys;

public class PhoneNumberInputTests
{
    [Theory]
    [InlineData("NG", "0803 123 4567", "+2348031234567")]
    [InlineData("NG", "8031234567", "+2348031234567")]
    [InlineData("GB", "07700 900123", "+447700900123")]
    [InlineData("US", "(415) 555-2671", "+14155552671")]
    [InlineData("IT", "06 1234 5678", "+390612345678")]
    [InlineData("DE", "+49 30 123456", "+4930123456")]
    public void Composes_e164_from_country_and_local_number(string country, string typed, string expected)
    {
        PhoneNumberInput.TryCompose(country, typed, out var e164, out var error).Should().BeTrue(error);
        e164.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "0803 123 4567")]
    [InlineData("ZZ", "0803 123 4567")]
    [InlineData("NG", "12")]
    [InlineData("NG", "+1")]
    public void Rejects_missing_country_or_impossible_numbers(string? country, string typed)
    {
        PhoneNumberInput.TryCompose(country, typed, out _, out var error).Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("+2348031234567", "NG", "8031234567")]
    [InlineData("+14155552671", "US", "4155552671")]
    [InlineData("+447700900123", "GB", "7700900123")]
    [InlineData("+390612345678", "IT", "0612345678")]
    public void Splits_e164_back_into_country_and_national_number(string e164, string country, string national)
    {
        var (found, rest) = PhoneNumberInput.Split(e164);
        found.Should().NotBeNull();
        found!.Code.Should().Be(country);
        rest.Should().Be(national);
    }

    [Fact]
    public void Country_list_covers_nigeria_and_resolves_from_culture()
    {
        SupportedCountries.All.Count.Should().BeGreaterThan(200);
        SupportedCountries.All.Select(c => c.Code).Should().OnlyHaveUniqueItems();
        SupportedCountries.GetByCode("ng")!.DialCode.Should().Be("234");
        SupportedCountries.GetByCode("NG")!.Flag.Should().Be("🇳🇬");
        SupportedCountries.FromCulture("en-NG")!.Code.Should().Be("NG");
        SupportedCountries.FromCulture("en").Should().BeNull();
        PhoneNumberInput.IsValidLocal("0803 123 4567").Should().BeTrue();
        PhoneNumberInput.IsValidLocal("12").Should().BeFalse();
    }
}

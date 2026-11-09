using FluentAssertions;
using Oluso.Enterprise.Verification;
using Oluso.Enterprise.Verification.Providers;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

public class BusinessNameAndNumberTests
{
    [Theory]
    [InlineData("Acme Foods (Nig.) Ltd", "ACME FOODS NIGERIA LIMITED")]
    [InlineData("acme foods", "ACME FOODS LIMITED")]
    [InlineData("Acme Foods Limited", "Acme Foods Ltd.")]
    [InlineData("Smith & Sons", "SMITH AND SONS LTD")]
    [InlineData("The Acme Company PLC", "ACME COMPANY")]
    [InlineData("Café Lagos Ltd", "CAFE LAGOS LIMITED")]
    [InlineData("Acme-Foods, Inc.", "ACME FOODS INCORPORATED")]
    public void Business_names_that_match(string provided, string registered) =>
        BusinessNameMatcher.BusinessNamesMatch(provided, registered).Should().BeTrue();

    [Theory]
    [InlineData("Acme Foods", "ACME FOOD LIMITED")]
    [InlineData("Foods Acme", "ACME FOODS LIMITED")]
    [InlineData("Nigerian Breweries", "NIGERIA BREWERIES PLC")]
    [InlineData("Ltd", "LIMITED")]
    [InlineData("", "ACME")]
    [InlineData(null, "ACME")]
    public void Business_names_that_do_not_match(string? provided, string registered) =>
        BusinessNameMatcher.BusinessNamesMatch(provided, registered).Should().BeFalse();

    [Theory]
    [InlineData("John", "Musa", "JOHN MUSA", true)]
    [InlineData("john", "musa", "MUSA JOHN", true)]
    [InlineData("John", "Musa", "JOHN ADEWALE MUSA", true)]
    [InlineData("Jöhn", "Musa", "JOHN MUSA", true)]
    [InlineData("John", "Musa", "JOHN MOSES", false)]
    [InlineData("John", null, "JOHN MUSA", false)]
    public void Person_names(string? first, string? last, string listed, bool expected) =>
        BusinessNameMatcher.PersonNameMatches(first, last, listed).Should().Be(expected);

    [Theory]
    [InlineData("RC 1234567", null, "RC1234567", "RC")]
    [InlineData("rc-1234567", "BN", "RC1234567", "RC")]
    [InlineData("1234567", "bn", "BN1234567", "BN")]
    [InlineData("LLP 0042", null, "LLP0042", "LLP")]
    [InlineData("lp/77", null, "LP77", "LP")]
    [InlineData("IT 555", null, "IT555", "IT")]
    public void Nigerian_numbers_are_canonical(string input, string? type, string canonical, string resolvedType)
    {
        var number = BusinessRegistrationNumber.TryParse("ng", input, type, out var error);

        error.Should().BeNull();
        number!.Canonical.Should().Be(canonical);
        number.Type.Should().Be(resolvedType);
        number.Country.Should().Be("NG");
    }

    [Theory]
    [InlineData("1234567", null, "registration_type_required")]
    [InlineData("XY1234567", null, "invalid_registration_number")]
    [InlineData("1234567", "ZZ", "invalid_registration_number")]
    [InlineData("   ", "RC", "invalid_registration_number")]
    public void Nigerian_numbers_that_cannot_be_used(string input, string? type, string expected)
    {
        BusinessRegistrationNumber.TryParse("NG", input, type, out var error).Should().BeNull();
        error.Should().Be(expected);
    }

    [Theory]
    [InlineData("ACTIVE", BusinessActivityStatus.Active)]
    [InlineData(" active ", BusinessActivityStatus.Active)]
    [InlineData("Not Active", BusinessActivityStatus.Inactive)]
    [InlineData("INACTIVE", BusinessActivityStatus.Inactive)]
    [InlineData("STRUCK OFF", BusinessActivityStatus.Inactive)]
    [InlineData("In Liquidation", BusinessActivityStatus.Inactive)]
    [InlineData("WOUND_UP", BusinessActivityStatus.Inactive)]
    [InlineData("REGISTERED", BusinessActivityStatus.Unknown)]
    [InlineData(null, BusinessActivityStatus.Unknown)]
    public void Registry_status_text(string? status, BusinessActivityStatus expected) =>
        RegistryStatus.Parse(status).Should().Be(expected);
}

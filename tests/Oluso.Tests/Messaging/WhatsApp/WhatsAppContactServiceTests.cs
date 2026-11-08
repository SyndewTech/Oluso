using FluentAssertions;
using Oluso.Core.Domain.Entities;
using Oluso.Messaging.WhatsApp;
using Xunit;

namespace Oluso.Tests.Messaging.WhatsApp;

public class WhatsAppContactServiceTests
{
    private readonly InMemoryContactStore _store = new();
    private readonly CapturingEvents _events = new();
    private WhatsAppContactService Service() => WhatsAppRig.ContactService(_store, _events);

    [Theory]
    [InlineData("+234 803 123 4567", "NG", "+2348031234567")]
    [InlineData("+2348031234567", null, "+2348031234567")]
    [InlineData("2348031234567", null, "+2348031234567")]
    [InlineData("2348031234567", "NG", "+2348031234567")]
    [InlineData("08031234567", "NG", "+2348031234567")]
    [InlineData("0803-123-4567", "NG", "+2348031234567")]
    [InlineData("8031234567", "NG", "+2348031234567")]
    [InlineData("07700 900123", "GB", "+447700900123")]
    [InlineData("447700900123", "GB", "+447700900123")]
    [InlineData("(415) 555-2671", "US", "+14155552671")]
    public void Normalises_to_e164(string input, string? country, string expected)
    {
        Service().TryNormalize(input, country, out var e164, out var error).Should().BeTrue(error);
        e164.Should().Be(expected);
    }

    [Fact]
    public void Local_number_without_a_default_country_fails_with_a_useful_message()
    {
        Service().TryNormalize("08031234567", null, out _, out var error).Should().BeFalse();
        error.Should().Contain("no default country");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("call me")]
    [InlineData("+1")]
    public void Rubbish_is_rejected(string input)
    {
        Service().TryNormalize(input, "NG", out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("STOP", "STOP")]
    [InlineData("stop.", "STOP")]
    [InlineData(" Unsubscribe ", "STOP")]
    [InlineData("START", "START")]
    [InlineData("yes", "START")]
    [InlineData("Please stop sending me these", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Detects_consent_keywords_only_when_the_message_is_the_keyword(string? text, string? expected)
    {
        IWhatsAppContactService.DetectKeyword(text).Should().Be(expected);
    }

    [Fact]
    public async Task Admin_consent_change_is_recorded_with_its_source_and_raises_an_event()
    {
        var contact = await Service().SetConsentAsync(WhatsAppRig.Tenant, "+2348031234567", WhatsAppConsentStatus.OptedOut, "admin", "admin-1");

        contact.ConsentStatus.Should().Be(WhatsAppConsentStatus.OptedOut);
        contact.ConsentSource.Should().Be("admin");
        contact.OptedOutAt.Should().NotBeNull();
        var evt = _events.Raised.OfType<Oluso.Core.Events.WhatsAppConsentChangedEvent>().Should().ContainSingle().Subject;
        evt.ChangedByUserId.Should().Be("admin-1");
        evt.Source.Should().Be("admin");
    }

    [Fact]
    public async Task Inbound_after_an_opt_out_does_not_silently_opt_back_in()
    {
        var service = Service();
        await service.SetConsentAsync(WhatsAppRig.Tenant, "+2348031234567", WhatsAppConsentStatus.OptedOut, "admin", null);

        await service.RecordInboundAsync(WhatsAppRig.Tenant, "+2348031234567", "Ada", "wamid.1", "hello again", DateTime.UtcNow);

        _store.Rows.Single().ConsentStatus.Should().Be(WhatsAppConsentStatus.OptedOut, "only START (or an admin) reverses an opt-out");
        _store.Rows.Single().LastInboundAt.Should().NotBeNull("the session still opens so a human can reply");
    }
}

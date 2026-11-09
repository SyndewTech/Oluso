using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oluso.Messaging;
using Oluso.Tests.Messaging.WhatsApp;
using Xunit;

namespace Oluso.Tests.Messaging;

/// <summary>
/// Infobip status groups: 1 PENDING, 2 UNDELIVERABLE, 3 DELIVERED, 4 EXPIRED,
/// 5 REJECTED. A submit that comes back PENDING or DELIVERED succeeded; the
/// rest did not.
/// </summary>
public class InfobipStatusMappingTests
{
    private static string Response(int groupId, string groupName, string name) =>
        $$$"""{"messages":[{"to":"2348031234567","messageId":"m-1","status":{"groupId":{{{groupId}}},"groupName":"{{{groupName}}}","id":7,"name":"{{{name}}}","description":"{{{name}}}"}}]}""";

    private static InfobipSmsSender Sender(RecordingHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new SmsOptions { Provider = "Infobip", Infobip = new InfobipSettings { ApiKey = "key", BaseUrl = "https://x.api.infobip.com" } }),
            NullLogger<InfobipSmsSender>.Instance);

    [Theory]
    [InlineData(1, "PENDING", "PENDING_ACCEPTED", true)]
    [InlineData(3, "DELIVERED", "DELIVERED_TO_HANDSET", true)]
    [InlineData(2, "UNDELIVERABLE", "UNDELIVERABLE_NOT_DELIVERED", false)]
    [InlineData(4, "EXPIRED", "EXPIRED_EXPIRED", false)]
    [InlineData(5, "REJECTED", "REJECTED_DESTINATION", false)]
    public async Task Submit_status_group_maps_to_success_correctly(int groupId, string groupName, string name, bool expectedSuccess)
    {
        var handler = new RecordingHandler().Enqueue(HttpStatusCode.OK, Response(groupId, groupName, name));

        var result = await Sender(handler).SendAsync("+2348031234567", "hello");

        result.Success.Should().Be(expectedSuccess, $"group {groupId} {groupName}");
        if (expectedSuccess) result.MessageId.Should().Be("m-1");
        else result.Error.Should().Contain(name);
    }
}

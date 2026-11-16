using FluentAssertions;
using Oluso.Core.Protocols.Models;
using Xunit;

namespace Oluso.Tests.Protocols;

public class ClaimsRequestTests
{
    [Fact]
    public void Reads_the_claims_of_both_members()
    {
        const string json = """
            {
              "userinfo": { "given_name": { "essential": true }, "email": null },
              "id_token": {
                "auth_time": { "essential": true },
                "acr": { "values": ["urn:mace:incommon:iap:silver", "urn:mace:incommon:iap:bronze"] },
                "sub": { "value": "248289761001" },
                "age": { "value": 21 }
              },
              "an_extension": 1
            }
            """;

        ClaimsRequest.TryParse(json, out var request, out var error).Should().BeTrue(error);

        request!.UserInfo.Keys.Should().BeEquivalentTo("given_name", "email");
        request.UserInfo["given_name"].Essential.Should().BeTrue();
        request.UserInfo["email"].Should().Be(RequestedClaim.Voluntary);
        request.IdToken["acr"].Values.Should().Equal("urn:mace:incommon:iap:silver", "urn:mace:incommon:iap:bronze");
        request.IdToken["sub"].Values.Should().Equal("248289761001");
        request.IdToken["age"].Values.Should().Equal("21");
    }

    [Fact]
    public void Finds_a_claim_in_the_id_token_before_userinfo()
    {
        ClaimsRequest.TryParse("""{"userinfo":{"acr":null},"id_token":{"acr":{"essential":true}}}""", out var request, out _)
            .Should().BeTrue();

        request!.Find("acr")!.Essential.Should().BeTrue();
        request.Find("email").Should().BeNull();
    }

    [Fact]
    public void An_empty_object_asks_for_nothing()
    {
        ClaimsRequest.TryParse("{}", out var request, out _).Should().BeTrue();

        request!.IdToken.Should().BeEmpty();
        request.UserInfo.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"id_token":["email"]}""")]
    [InlineData("""{"userinfo":{"email":true}}""")]
    [InlineData("""{"id_token":{"acr":{"essential":"yes"}}}""")]
    [InlineData("""{"id_token":{"acr":{"values":"urn:a"}}}""")]
    public void Refuses_a_value_that_is_not_the_section_5_5_shape(string json)
    {
        ClaimsRequest.TryParse(json, out var request, out var error).Should().BeFalse();

        request.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }
}

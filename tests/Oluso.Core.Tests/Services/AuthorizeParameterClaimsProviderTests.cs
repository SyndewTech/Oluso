using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Core.Tests.Services;

public class AuthorizeParameterClaimsProviderTests
{
    private static AuthorizeParameterClaimsProvider CreateProvider(Client client, out Mock<IClientStore> store)
    {
        store = new Mock<IClientStore>();
        store.Setup(s => s.FindClientByIdAsync(client.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        return new AuthorizeParameterClaimsProvider(store.Object, NullLogger<AuthorizeParameterClaimsProvider>.Instance);
    }

    private static Client ClientWith(params ClientAuthorizeParameter[] mappings) => new()
    {
        ClientId = "spa",
        AllowedAuthorizeParameters = mappings.ToList()
    };

    private static ClaimsProviderContext Context(Dictionary<string, object>? additionalData) => new()
    {
        SubjectId = "user-1",
        ClientId = "spa",
        AdditionalData = additionalData
    };

    [Fact]
    public async Task Maps_allowlisted_parameter_to_its_claim()
    {
        var client = ClientWith(new ClientAuthorizeParameter { Parameter = "branch_id", ClaimType = "branch" });
        var provider = CreateProvider(client, out _);

        var result = await provider.GetClaimsAsync(Context(new() { ["branch_id"] = "lagos-01" }));

        Assert.True(result.Success);
        Assert.Equal("lagos-01", Assert.Contains("branch", result.Claims));
    }

    [Fact]
    public async Task Ignores_parameter_value_that_was_not_allowlisted()
    {
        // Only branch_id is mapped; a stray value for an unmapped parameter is dropped.
        var client = ClientWith(new ClientAuthorizeParameter { Parameter = "branch_id", ClaimType = "branch" });
        var provider = CreateProvider(client, out _);

        var result = await provider.GetClaimsAsync(Context(new() { ["role"] = "SuperAdmin" }));

        Assert.True(result.Success);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public async Task Refuses_to_emit_a_reserved_claim_even_if_a_mapping_targets_it()
    {
        // Defense in depth: a row that maps a parameter onto a reserved claim
        // (blocked on write) must still never mint that claim at issuance.
        var client = ClientWith(new ClientAuthorizeParameter { Parameter = "tenant", ClaimType = "tenant_id" });
        var provider = CreateProvider(client, out _);

        var result = await provider.GetClaimsAsync(Context(new() { ["tenant"] = "attacker-tenant" }));

        Assert.True(result.Success);
        Assert.DoesNotContain("tenant_id", result.Claims);
    }

    [Fact]
    public async Task ShouldProvide_is_false_without_additional_data()
    {
        var client = ClientWith(new ClientAuthorizeParameter { Parameter = "branch_id", ClaimType = "branch" });
        var provider = CreateProvider(client, out var store);

        Assert.False(provider.ShouldProvide(Context(null)));
        store.Verify(s => s.FindClientByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

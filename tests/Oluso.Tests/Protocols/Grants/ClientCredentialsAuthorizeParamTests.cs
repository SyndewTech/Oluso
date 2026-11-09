using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Protocols.Grants;
using Xunit;

namespace Oluso.Tests.Protocols.Grants;

/// <summary>
/// Covers emitting token claims from a client's allowlisted custom request
/// parameters on the client_credentials grant (the back-channel path: the value
/// rides the client-authenticated token request body, not an /authorize redirect).
/// </summary>
public class ClientCredentialsAuthorizeParamTests
{
    private readonly Mock<IResourceStore> _resourceStore = new();
    private readonly Mock<IClientStore> _clientStore = new();

    private ClientCredentialsGrantHandler CreateHandler(params ClientAuthorizeParameter[] mappings)
    {
        _resourceStore
            .Setup(r => r.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdentityResource>());

        _clientStore
            .Setup(c => c.FindClientByIdAsync("svc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { ClientId = "svc", AllowedAuthorizeParameters = mappings.ToList() });

        return new ClientCredentialsGrantHandler(
            _resourceStore.Object, _clientStore.Object,
            NullLogger<ClientCredentialsGrantHandler>.Instance);
    }

    private static TokenRequest RequestWith(params (string Key, string Value)[] raw) => new()
    {
        GrantType = "client_credentials",
        Client = new ValidatedClient { ClientId = "svc", TenantId = "tenant-1", AllowedScopes = new[] { "api" } },
        RequestedScopes = new[] { "api" },
        Raw = raw.ToDictionary(p => p.Key, p => p.Value)
    };

    [Fact]
    public async Task Emits_claim_from_allowlisted_token_parameter()
    {
        var handler = CreateHandler(new ClientAuthorizeParameter { Parameter = "data-sm-patientId", ClaimType = "patient_id" });

        var result = await handler.HandleAsync(RequestWith(("data-sm-patientId", "guid-123")));

        result.IsValid.Should().BeTrue();
        result.Claims.Should().ContainKey("patient_id").WhoseValue.Should().Be("guid-123");
    }

    [Fact]
    public async Task Ignores_parameter_that_is_not_allowlisted()
    {
        var handler = CreateHandler(new ClientAuthorizeParameter { Parameter = "data-sm-patientId", ClaimType = "patient_id" });

        var result = await handler.HandleAsync(RequestWith(("role", "SuperAdmin")));

        result.IsValid.Should().BeTrue();
        result.Claims.Should().NotContainKey("role");
        result.Claims.Should().NotContainKey("patient_id");
    }

    [Fact]
    public async Task Refuses_to_emit_reserved_claim()
    {
        var handler = CreateHandler(new ClientAuthorizeParameter { Parameter = "t", ClaimType = "tenant_id" });

        var result = await handler.HandleAsync(RequestWith(("t", "attacker")));

        // tenant_id is still the client's registered tenant, never the parameter value.
        result.Claims["tenant_id"].Should().Be("tenant-1");
    }

    [Fact]
    public async Task Missing_required_parameter_fails_the_grant()
    {
        var handler = CreateHandler(new ClientAuthorizeParameter { Parameter = "data-sm-patientId", ClaimType = "patient_id", Required = true });

        var result = await handler.HandleAsync(RequestWith(("something_else", "x")));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
    }
}

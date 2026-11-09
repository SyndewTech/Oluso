using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Every interactive sign-in path funnels through the auditor, so the audit log
/// and dashboard count all of them. These pin down what each raised event carries.
/// </summary>
public class SignInAuditorTests
{
    private readonly List<OlusoEvent> _raised = new();
    private readonly Mock<IOlusoEventService> _events = new();
    private readonly Mock<IProtocolStateStore> _protocolState = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    public SignInAuditorTests()
    {
        _events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OlusoEvent, CancellationToken>((evt, _) => _raised.Add(evt))
            .Returns(Task.CompletedTask);
        _tenantContext.SetupGet(t => t.TenantId).Returns("ambient-tenant");
    }

    private SignInAuditor Create(IHttpContextAccessor? http = null) =>
        new(_events.Object, NullLogger<SignInAuditor>.Instance, _tenantContext.Object, _protocolState.Object, http);

    [Fact]
    public async Task Success_raises_signed_in_event_with_given_fields()
    {
        await Create().RecordSuccessAsync(new SignInAudit
        {
            SubjectId = "u1",
            Username = "ada@example.com",
            TenantId = "tenant-a",
            AuthenticationMethod = "pwd",
            ClientId = "client-x",
            SessionId = "j1",
            IpAddress = "10.0.0.1"
        });

        var evt = _raised.Should().ContainSingle().Which.Should().BeOfType<UserSignedInEvent>().Subject;
        evt.SubjectId.Should().Be("u1");
        evt.Username.Should().Be("ada@example.com");
        evt.TenantId.Should().Be("tenant-a");
        evt.AuthenticationMethod.Should().Be("pwd");
        evt.ClientId.Should().Be("client-x");
        evt.SessionId.Should().Be("j1");
        evt.IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task Client_is_resolved_from_the_return_url_correlation_when_not_given()
    {
        _protocolState.Setup(s => s.GetAsync("corr-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProtocolState { ClientId = "spa-client", ProtocolName = "oidc", SerializedRequest = "{}" });

        await Create().RecordSuccessAsync(new SignInAudit
        {
            SubjectId = "u1",
            AuthenticationMethod = "pwd",
            ReturnUrl = "/oauth/authorize/callback?correlation_id=corr-1"
        });

        _raised.Should().ContainSingle().Which.As<UserSignedInEvent>().ClientId.Should().Be("spa-client");
    }

    [Fact]
    public async Task Missing_client_and_tenant_fall_back_to_unknown_and_ambient_tenant()
    {
        await Create().RecordSuccessAsync(new SignInAudit { SubjectId = "u1", AuthenticationMethod = "fido2" });

        var evt = _raised.Should().ContainSingle().Which.As<UserSignedInEvent>();
        evt.ClientId.Should().Be("unknown");
        evt.TenantId.Should().Be("ambient-tenant");
        evt.Username.Should().Be("u1");
    }

    [Fact]
    public async Task Ip_comes_from_forwarded_header_then_connection()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.9, 10.0.0.2";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.3");
        var accessor = new HttpContextAccessor { HttpContext = ctx };

        await Create(accessor).RecordSuccessAsync(new SignInAudit { SubjectId = "u1", AuthenticationMethod = "pwd" });

        _raised.Should().ContainSingle().Which.As<UserSignedInEvent>().IpAddress.Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task Failure_raises_failed_event()
    {
        await Create().RecordFailureAsync(new SignInFailureAudit
        {
            Username = "ada@example.com",
            FailureReason = "invalid_credentials",
            ClientId = "client-x"
        });

        var evt = _raised.Should().ContainSingle().Which.Should().BeOfType<UserSignInFailedEvent>().Subject;
        evt.Username.Should().Be("ada@example.com");
        evt.FailureReason.Should().Be("invalid_credentials");
        evt.ClientId.Should().Be("client-x");
        evt.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Auditing_never_throws_into_the_sign_in()
    {
        _events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sink down"));

        var act = () => Create().RecordSuccessAsync(new SignInAudit { SubjectId = "u1", AuthenticationMethod = "pwd" });

        await act.Should().NotThrowAsync();
    }
}

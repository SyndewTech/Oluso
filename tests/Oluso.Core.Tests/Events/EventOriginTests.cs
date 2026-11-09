using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Events;
using Xunit;

namespace Oluso.Core.Tests.Events;

/// <summary>
/// Every raised event leaves <see cref="IOlusoEventService.RaiseAsync"/> with an
/// origin: the raiser's, else the ambient scope's, else the request's actor,
/// else what the event's own fields imply, else anonymous or system.
/// </summary>
public class EventOriginTests
{
    private readonly List<OlusoEvent> _raised = new();
    private readonly HttpContextAccessor _accessor = new();

    private OlusoEventService Service() => new(
        new IOlusoEventSink[] { new CallbackEventSink((e, _) => { _raised.Add(e); return Task.CompletedTask; }) },
        NullLogger<OlusoEventService>.Instance,
        originResolver: new HttpContextEventOriginResolver(_accessor));

    private void Request(string path, params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = claims.Length == 0
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        _accessor.HttpContext = context;
    }

    private async Task<EventOrigin> RaiseAsync(OlusoEvent evt)
    {
        await Service().RaiseAsync(evt);
        return _raised.Last().Origin!;
    }

    private static UserUpdatedEvent Updated() => new() { TenantId = "t1", SubjectId = "u1" };

    private static AdminRoleDeletedEvent AdminDeleted(string adminUserId) =>
        new() { TenantId = "t1", AdminUserId = adminUserId, AdminUserName = "Ada", IpAddress = "198.51.100.1", ResourceId = "r1", RoleName = "Editors" };

    [Fact]
    public async Task Admin_api_request_is_an_admin_origin()
    {
        Request("/api/admin/users/u1", new Claim("sub", "admin-1"), new Claim("name", "Ada"));

        (await RaiseAsync(Updated())).Should().Be(EventOrigin.Admin("admin-1", "Ada", "203.0.113.7"));
    }

    [Fact]
    public async Task Client_credentials_token_is_a_service_client_origin()
    {
        Request("/api/service/users", new Claim("client_id", "pay1"), new Claim("scope", "oluso:service"));

        (await RaiseAsync(Updated())).Should().Be(EventOrigin.ServiceClient("pay1", "203.0.113.7"));
    }

    [Fact]
    public async Task Signed_in_user_outside_the_admin_api_is_a_user_origin()
    {
        Request("/api/account/profile", new Claim("sub", "u1"), new Claim("email", "u1@example.org"));

        (await RaiseAsync(Updated())).Should().Be(EventOrigin.User("u1", "u1@example.org", "203.0.113.7"));
    }

    [Fact]
    public async Task Raiser_origin_wins_over_scope_and_request()
    {
        Request("/api/admin/users/u1", new Claim("sub", "admin-1"));
        using var scope = EventOriginScope.Begin(EventOrigin.Journey("signup"));
        var evt = Updated();
        evt.Origin = EventOrigin.Scim("sc1", "Payroll", "192.0.2.5");

        (await RaiseAsync(evt)).Should().Be(EventOrigin.Scim("sc1", "Payroll", "192.0.2.5"));
    }

    [Fact]
    public async Task Scope_origin_wins_over_the_request_and_takes_its_ip()
    {
        Request("/api/scim/v2/Users");
        using (EventOriginScope.Begin(EventOrigin.Scim("sc1", "Payroll")))
        {
            (await RaiseAsync(Updated())).Should().Be(EventOrigin.Scim("sc1", "Payroll", "203.0.113.7"));
        }

        EventOriginScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task Journey_scope_is_the_origin_of_events_raised_by_its_steps()
    {
        Request("/journey/signup");
        using var scope = EventOriginScope.Begin(EventOrigin.Journey("signup", "Sign up"));

        (await RaiseAsync(Updated())).Should().Be(EventOrigin.Journey("signup", "Sign up", "203.0.113.7"));
    }

    [Fact]
    public async Task Anonymous_request_falls_back_to_what_the_event_implies()
    {
        Request("/webhooks/payments");

        (await RaiseAsync(AdminDeleted("admin-1"))).Should().Be(EventOrigin.Admin("admin-1", "Ada", "198.51.100.1"));
        (await RaiseAsync(Updated())).Should().Be(EventOrigin.Anonymous("203.0.113.7"));
    }

    [Fact]
    public async Task Without_a_request_events_are_system_unless_they_imply_an_actor()
    {
        (await RaiseAsync(Updated())).Should().Be(EventOrigin.System());
        (await RaiseAsync(AdminDeleted("system"))).Should().Be(EventOrigin.System("Ada"));
        (await RaiseAsync(AdminDeleted("admin-1"))).Kind.Should().Be(EventOriginKinds.Admin);
    }
}

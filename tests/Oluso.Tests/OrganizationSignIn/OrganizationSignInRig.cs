using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Stores;
using Oluso.Protocols;
using Oluso.Protocols.Oidc;
using Oluso.Tests.Roles;

namespace Oluso.Tests.OrganizationSignIn;

/// <summary>
/// The organisation sign-in service over real stores: protocol state in a memory
/// distributed cache, hand-off codes as action tokens in SQLite (the atomic
/// consume needs a relational provider), journeys in memory, and a clock the
/// test moves. Platform and tenant requests are separate HttpContexts on their
/// own hosts, with the binding cookie carried between them like a browser would.
/// </summary>
internal sealed class OrganizationSignInRig : IDisposable
{
    public const string PlatformIssuer = "https://auth.example.com";
    public const string TenantIssuer = "https://acme.auth.example.com";
    public const string ClientId = "mail-server";

    public readonly MutableClock Clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public readonly Tenant Tenant = new() { Id = "tenant-acme", Identifier = "acme", Name = "Acme", OrganizationId = "org-1" };
    public readonly Tenant OtherTenant = new() { Id = "tenant-globex", Identifier = "globex", Name = "Globex", OrganizationId = "org-2" };
    public readonly FakeTenantContext TenantContext = new();
    public readonly InMemoryProtocolStateStore ProtocolState;
    public readonly InMemoryJourneyStateStore Journeys = new();
    public readonly IDistributedCache Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    public readonly Mock<IOlusoUserService> Users = new();
    public readonly Mock<ITenantMembershipStore> Memberships = new();
    public readonly List<OrganizationSignInEvent> Events = new();
    public readonly OidcEndpointConfiguration Endpoints = new();
    public readonly OlusoOptions Options_ = new();
    public readonly ActionTokenStore ActionTokenStore;
    public readonly DefaultActionTokenService ActionTokens;
    public readonly OrganizationSignInService Service;

    private readonly SqliteConnection _connection;
    private readonly OlusoDbContext _db;
    private readonly Dictionary<string, string> _browserCookies = new();

    public OrganizationSignInRig()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _db = IdentityTestFactory.CreateSqliteContext(_connection, new TestTenantContext());
        ActionTokenStore = new ActionTokenStore(_db);
        ActionTokens = new DefaultActionTokenService(ActionTokenStore, Clock);
        ProtocolState = new InMemoryProtocolStateStore(Cache, Options.Create(Endpoints), NullLogger<InMemoryProtocolStateStore>.Instance);

        var discovery = new Mock<ITenantDiscoveryService>();
        discovery.SetupGet(d => d.IsAvailable).Returns(true);
        discovery.Setup(d => d.FindByIdentifierAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string identifier, CancellationToken _) => identifier.Trim() switch
            {
                "acme" => new DiscoveredTenant(Tenant, TenantIssuer),
                "globex" => new DiscoveredTenant(OtherTenant, "https://globex.auth.example.com"),
                "_platform" => new DiscoveredTenant(new Tenant { Id = Oluso.Core.PlatformConstants.TenantId, Identifier = Oluso.Core.PlatformConstants.TenantId, Name = "Platform", OrganizationId = "p" }, PlatformIssuer),
                _ => null
            });

        var issuers = new Mock<IIssuerResolver>();
        issuers.Setup(i => i.GetIssuerForTenantAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant t, CancellationToken _) => $"https://{t.Identifier}.auth.example.com");

        var events = new Mock<IOlusoEventService>();
        events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OlusoEvent evt, CancellationToken _) => { if (evt is OrganizationSignInEvent o) Events.Add(o); })
            .Returns(Task.CompletedTask);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Oluso:IssuerUri"] = PlatformIssuer })
            .Build();

        Service = new OrganizationSignInService(
            ProtocolState,
            Journeys,
            discovery.Object,
            issuers.Object,
            ActionTokens,
            Cache,
            Users.Object,
            events.Object,
            configuration,
            Options.Create(Options_),
            Options.Create(Endpoints),
            NullLogger<OrganizationSignInService>.Instance,
            TenantContext,
            Memberships.Object,
            Clock);
    }

    public OlusoUserInfo AddUser(string id, string tenantId, bool active = true, DateTimeOffset? lockoutEnd = null)
    {
        var user = new OlusoUserInfo
        {
            Id = id,
            Username = $"{id}@acme.test",
            Email = $"{id}@acme.test",
            TenantId = tenantId,
            IsActive = active,
            LockoutEnabled = lockoutEnd.HasValue,
            LockoutEnd = lockoutEnd,
        };
        Users.Setup(u => u.FindByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    /// <summary>An OIDC authorize request waiting on the platform login, as the coordinator stores it.</summary>
    public async Task<string> StartPlatformAuthorizeAsync(string? prompt = null, string? loginHint = null, string? maxAge = null)
    {
        var raw = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = "https://mail.example.com/oidc/callback",
            ["response_type"] = "code",
            ["scope"] = "openid profile email",
            ["state"] = "s1",
        };
        if (prompt != null) raw["prompt"] = prompt;
        if (loginHint != null) raw["login_hint"] = loginHint;
        if (maxAge != null) raw["max_age"] = maxAge;

        var request = new OidcAuthorizeRequest
        {
            ClientId = ClientId,
            RedirectUri = raw["redirect_uri"],
            ResponseType = "code",
            Scope = raw["scope"],
            Prompt = prompt,
            LoginHint = loginHint,
            MaxAge = maxAge,
            Raw = raw,
        };
        return await ProtocolState.StoreAsync(new ProtocolState
        {
            ProtocolName = "oidc",
            SerializedRequest = JsonSerializer.Serialize(request),
            ClientId = ClientId,
            TenantId = Oluso.Core.PlatformConstants.TenantId,
            CreatedAt = Clock.GetUtcNow().UtcDateTime,
        });
    }

    public static string CallbackFor(string correlationId) =>
        $"{PlatformIssuer}/connect/authorize/callback?correlation_id={correlationId}";

    public async Task<JourneyState> StartPlatformJourneyAsync(string correlationId)
    {
        var journey = new JourneyState
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = Oluso.Core.PlatformConstants.TenantId,
            ClientId = ClientId,
            PolicyId = "signin",
            CurrentStepId = "login",
            Status = JourneyStatus.InProgress,
            CreatedAt = Clock.GetUtcNow().UtcDateTime,
            ExpiresAt = Clock.GetUtcNow().UtcDateTime.AddMinutes(30),
            CorrelationId = correlationId,
            CallbackUrl = CallbackFor(correlationId),
        };
        await Journeys.SaveAsync(journey);
        return journey;
    }

    /// <summary>A request on the platform host; carries the browser's cookies.</summary>
    public HttpContext PlatformRequest() => Request("auth.example.com");

    /// <summary>A request on the tenant host, with the tenant context host resolution would set.</summary>
    public HttpContext TenantRequest(Tenant? tenant = null)
    {
        TenantContext.Tenant = tenant ?? Tenant;
        return Request(TenantContext.Tenant.Identifier + ".auth.example.com");
    }

    public void AtPlatform() => TenantContext.Tenant = null;

    private HttpContext Request(string host)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString(host);
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        if (host == "auth.example.com" && _browserCookies.Count > 0)
        {
            http.Request.Headers.Cookie = string.Join("; ", _browserCookies.Select(c => $"{c.Key}={c.Value}"));
        }
        return http;
    }

    /// <summary>Keeps the cookies a response set, as the browser would for the platform host.</summary>
    public IReadOnlyList<string> AcceptCookies(HttpContext response)
    {
        var headers = response.Response.Headers.SetCookie.ToArray();
        foreach (var header in headers)
        {
            var pair = header!.Split(';')[0];
            var eq = pair.IndexOf('=');
            var name = pair[..eq];
            var value = pair[(eq + 1)..];
            if (string.IsNullOrEmpty(value) || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                _browserCookies.Remove(name);
            else
                _browserCookies[name] = value;
        }
        return headers!;
    }

    public void ReplaceBrowserCookie(string name, string value) => _browserCookies[name] = value;
    public void ForgetBrowserCookies() => _browserCookies.Clear();

    public static string RequestIdFrom(string startUrl) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(startUrl).Query)["request"].ToString();

    public static string CodeFrom(string callbackUrl) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(callbackUrl).Query)["code"].ToString();

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}

internal sealed class MutableClock : TimeProvider
{
    private DateTimeOffset _now;
    public MutableClock(DateTimeOffset start) => _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

internal sealed class FakeTenantContext : ITenantContext
{
    public Tenant? Tenant { get; set; }
    public string? TenantId => Tenant?.Id;
    public bool HasTenant => Tenant != null;
}

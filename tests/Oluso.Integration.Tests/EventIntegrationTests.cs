using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Events;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests that verify events are raised during actual OIDC flows.
/// </summary>
public class EventIntegrationTests : IClassFixture<EventIntegrationTests.EventTestFactory>, IAsyncLifetime
{
    private readonly EventTestFactory _factory;
    private readonly HttpClient _client;

    public EventIntegrationTests(EventTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    public Task InitializeAsync()
    {
        _factory.TestEventSink.Clear();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TokenEndpoint_ClientCredentials_RaisesTokenIssuedEvent()
    {
        // Act - Request a token using client credentials
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid"
        });

        var response = await _client.PostAsync("/connect/token", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify TokenIssuedEvent was raised
        var tokenEvents = _factory.TestEventSink.GetEvents<TokenIssuedEvent>();
        tokenEvents.Should().ContainSingle();

        var evt = tokenEvents.First();
        evt.ClientId.Should().Be("cc-client");
        evt.TokenType.Should().Be("client_credentials");
        evt.Category.Should().Be(EventCategories.Token);
    }

    [Fact]
    public async Task TokenEndpoint_PasswordGrant_RaisesTokenIssuedEvent()
    {
        // Act - Request a token using password grant
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid profile"
        });

        var response = await _client.PostAsync("/connect/token", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify TokenIssuedEvent was raised with user context
        var tokenEvents = _factory.TestEventSink.GetEvents<TokenIssuedEvent>();
        tokenEvents.Should().ContainSingle();

        var evt = tokenEvents.First();
        evt.ClientId.Should().Be("ropc-client");
        evt.SubjectId.Should().NotBeNullOrEmpty("Password grant should have a subject");
        evt.TokenType.Should().Be("password");
        evt.Scopes.Should().Contain("openid");
    }

    [Fact]
    public async Task TokenEndpoint_InvalidCredentials_DoesNotRaiseTokenIssuedEvent()
    {
        // Act - Request with invalid credentials
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "WrongPassword!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid"
        });

        var response = await _client.PostAsync("/connect/token", content);

        // Assert - Should fail
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify NO TokenIssuedEvent was raised
        var tokenEvents = _factory.TestEventSink.GetEvents<TokenIssuedEvent>();
        tokenEvents.Should().BeEmpty("Failed auth should not raise token issued event");
    }

    [Fact]
    public async Task TokenEndpoint_InvalidClient_DoesNotRaiseTokenIssuedEvent()
    {
        // Act - Request with invalid client
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "nonexistent-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid"
        });

        var response = await _client.PostAsync("/connect/token", content);

        // Assert - Should fail
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify NO TokenIssuedEvent was raised
        var tokenEvents = _factory.TestEventSink.GetEvents<TokenIssuedEvent>();
        tokenEvents.Should().BeEmpty("Invalid client should not raise token issued event");
    }

    [Fact]
    public async Task Events_HaveCorrectTimestampAndId()
    {
        var beforeRequest = DateTime.UtcNow;

        // Act
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid"
        });

        await _client.PostAsync("/connect/token", content);

        var afterRequest = DateTime.UtcNow;

        // Assert
        var evt = _factory.TestEventSink.GetEvents<TokenIssuedEvent>().First();
        evt.Id.Should().NotBeNullOrEmpty();
        evt.Timestamp.Should().BeOnOrAfter(beforeRequest);
        evt.Timestamp.Should().BeOnOrBefore(afterRequest);
    }

    /// <summary>
    /// The standard test host plus a sink that captures raised events.
    /// </summary>
    public class EventTestFactory : OlusoWebApplicationFactory
    {
        public TestEventSink TestEventSink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IOlusoEventSink>(TestEventSink));
        }
    }

    /// <summary>
    /// Test event sink that captures events for verification.
    /// </summary>
    public class TestEventSink : IOlusoEventSink
    {
        private readonly ConcurrentBag<OlusoEvent> _events = new();

        public string Name => "TestEventSink";

        public Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
        {
            _events.Add(evt);
            return Task.CompletedTask;
        }

        public IReadOnlyList<T> GetEvents<T>() where T : OlusoEvent
        {
            return _events.OfType<T>().ToList();
        }

        public IReadOnlyList<OlusoEvent> GetAllEvents()
        {
            return _events.ToList();
        }

        public void Clear()
        {
            _events.Clear();
        }
    }
}

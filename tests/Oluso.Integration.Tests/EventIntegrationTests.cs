using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Events;
using Oluso.EntityFramework;
using Oluso.Enterprise.Ldap.Server;
using Oluso.Enterprise.Scim.EntityFramework;
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
    /// Custom factory that registers a test event sink for capturing events.
    /// </summary>
    public class EventTestFactory : WebApplicationFactory<Program>
    {
        public TestEventSink TestEventSink { get; } = new();
        private readonly string _databaseName = $"OlusoEventTestDb_{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove LDAP server
                var ldapHostedService = services.SingleOrDefault(
                    d => d.ImplementationType == typeof(LdapServerHostedService));
                if (ldapHostedService != null)
                {
                    services.Remove(ldapHostedService);
                }

                // Remove existing DbContexts
                RemoveDbContextRegistrations<OlusoDbContext>(services);
                RemoveDbContextRegistrations<ScimDbContext>(services);

                // Add in-memory database
                services.AddDbContext<OlusoDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));
                services.AddDbContext<ScimDbContext>(options =>
                    options.UseInMemoryDatabase($"{_databaseName}_Scim"));

                // Register our test event sink
                services.AddSingleton<IOlusoEventSink>(TestEventSink);

                // Ensure database is created
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
                db.Database.EnsureCreated();
            });
        }

        private static void RemoveDbContextRegistrations<TContext>(IServiceCollection services) where TContext : DbContext
        {
            var optionsDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TContext>));
            if (optionsDescriptor != null)
                services.Remove(optionsDescriptor);

            var contextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(TContext));
            if (contextDescriptor != null)
                services.Remove(contextDescriptor);
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

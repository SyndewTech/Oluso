using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Oluso.EntityFramework.DataMigrations;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Webhooks;

/// <summary>
/// Subscriptions to the removed admin.user_* events move to the core user
/// events; an endpoint already subscribed to the core event keeps one row, and
/// other subscriptions are untouched.
/// </summary>
public class RemapAdminUserWebhookSubscriptionsTests : IDisposable
{
    private readonly SqliteConnection _connection = IdentityTestFactory.OpenSqliteConnection();
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;

    public RemapAdminUserWebhookSubscriptionsTests()
    {
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private void Endpoint(string id, params string[] eventTypes)
    {
        _context.WebhookEndpoints.Add(new WebhookEndpointEntity
        {
            Id = id, TenantId = "_platform", Name = id, Url = $"https://{id}.example.org", SecretHash = "x",
        });
        foreach (var type in eventTypes)
            _context.WebhookEventSubscriptions.Add(new WebhookEventSubscriptionEntity { EndpointId = id, EventType = type });
    }

    [Fact]
    public async Task Admin_user_subscriptions_move_to_the_core_events()
    {
        Endpoint("a",
            "admin.user_created", "admin.user_updated", "admin.user_deleted",
            "admin.user_role_assigned", "admin.user_role_removed", "admin.password_reset", "admin.client_created");
        Endpoint("b", "admin.user_created", "user.created");
        await _context.SaveChangesAsync();

        await new RemapAdminUserWebhookSubscriptions().UpAsync(_context, new ServiceCollection().BuildServiceProvider());
        await new RemapAdminUserWebhookSubscriptions().UpAsync(_context, new ServiceCollection().BuildServiceProvider());

        var rows = await _context.WebhookEventSubscriptions.AsNoTracking()
            .Select(s => new { s.EndpointId, s.EventType })
            .ToListAsync();
        rows.Where(r => r.EndpointId == "a").Select(r => r.EventType).Should().BeEquivalentTo(
            "user.created", "user.updated", "user.deleted",
            "user.role_assigned", "user.role_removed", "user.password_changed", "admin.client_created");
        rows.Where(r => r.EndpointId == "b").Select(r => r.EventType).Should().Equal("user.created");
    }
}

using FluentAssertions;
using Oluso.Admin.Controllers.Service;
using Xunit;

namespace Oluso.Tests.Admin;

/// <summary>
/// Modules add their Service API scopes to the catalog, after the platform's, and they are then managed like the
/// platform's own; a scope outside the reserved namespace, or one already defined differently, is refused.
/// </summary>
public class ServiceScopeRegistrationTests
{
    [Fact]
    public void A_module_scope_joins_the_catalog_once_and_is_managed()
    {
        var probe = new ServiceScopeDefinition("oluso:probe:read", "Read probes", "Read the probe module's records.", "Probe");

        ServiceScopes.Register(probe);
        ServiceScopes.Register(probe);

        ServiceScopes.Catalog.Count(d => d.Name == probe.Name).Should().Be(1);
        ServiceScopes.Catalog.Take(7).Select(d => d.Name).Should().StartWith(ServiceScopes.UsersRead);
        ServiceScopes.IsManaged(probe.Name).Should().BeTrue();
        ServiceScopes.AllNames.Should().Contain(probe.Name).And.StartWith(ServiceScopes.ServiceApi);
        ServiceScopes.Find(probe.Name).Should().Be(probe);
    }

    [Fact]
    public void Scopes_outside_the_namespace_or_redefined_are_refused()
    {
        FluentActions.Invoking(() => ServiceScopes.Register(new ServiceScopeDefinition("probe:read", "x", "x", "x")))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ServiceScopes.Register(new ServiceScopeDefinition(ServiceScopes.ServiceApi, "x", "x", "x")))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ServiceScopes.Register(new ServiceScopeDefinition(ServiceScopes.UsersRead, "Other", "x", "x")))
            .Should().Throw<InvalidOperationException>();
    }
}

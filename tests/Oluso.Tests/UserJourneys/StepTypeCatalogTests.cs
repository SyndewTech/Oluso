using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>The builder's step picker shows every built-in step with a name, category and icon.</summary>
public class StepTypeCatalogTests
{
    [Fact]
    public void Every_built_in_step_has_a_name_category_and_icon()
    {
        var registry = new DefaultStepHandlerRegistry(new ServiceCollection().BuildServiceProvider());

        registry.GetRegisteredTypes().Should().NotBeEmpty().And.AllSatisfy(t =>
        {
            t.DisplayName.Should().NotBeNullOrWhiteSpace(t.Type);
            t.Category.Should().NotBeNullOrWhiteSpace(t.Type);
            t.Icon.Should().NotBeNullOrWhiteSpace(t.Type);
        });
    }

    [Fact]
    public void A_module_registering_a_known_step_keeps_its_icon_unless_it_sets_one()
    {
        var registry = new DefaultStepHandlerRegistry(new ServiceCollection().BuildServiceProvider());

        registry.Register<StubHandler>("captcha", b => b.FromModule("acme"));
        registry.Register<StubHandler>("webhook", b => b.WithIcon("paper-airplane"));

        var types = registry.GetRegisteredTypes().ToDictionary(t => t.Type);
        types["captcha"].Icon.Should().Be("shield");
        types["webhook"].Icon.Should().Be("paper-airplane");
    }

    private sealed class StubHandler : IStepHandler
    {
        public string StepType => "stub";
        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(StepHandlerResult.Success());
    }
}

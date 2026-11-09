using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Events;

namespace Oluso.Tests.Roles;

/// <summary>
/// A real event service whose only sink records what it is given, so tests see
/// events after origin resolution. <see cref="Services"/> hands it out the way
/// a request scope does, for services that resolve it per call; given an
/// origin resolver, it hands that out too.
/// </summary>
internal sealed class CapturedEvents
{
    public CapturedEvents(IEventOriginResolver? originResolver = null, Action<IServiceCollection>? configure = null)
    {
        Service = new OlusoEventService(
            new IOlusoEventSink[] { new CallbackEventSink((e, _) => { Raised.Add(e); return Task.CompletedTask; }) },
            NullLogger<OlusoEventService>.Instance,
            originResolver: originResolver);
        var services = new ServiceCollection().AddSingleton(Service);
        if (originResolver != null) services.AddSingleton(originResolver);
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public List<OlusoEvent> Raised { get; } = new();

    public IOlusoEventService Service { get; }

    public IServiceProvider Services { get; }
}

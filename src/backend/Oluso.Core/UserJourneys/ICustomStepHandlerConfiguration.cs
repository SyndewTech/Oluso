namespace Oluso.Core.UserJourneys;

/// <summary>
/// Interface for custom step handler configuration
/// </summary>
public interface ICustomStepHandlerConfiguration
{
    void Configure(IStepHandlerRegistry registry);
}

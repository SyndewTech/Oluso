namespace Oluso.Licensing;

/// <summary>
/// Interface for claims providers
/// </summary>
public interface IClaimsProvider
{
    string ProviderId { get; }
    int Priority { get; }
    IEnumerable<string>? TriggerScopes { get; }
    bool ShouldProvide(ClaimsProviderContext context);
    Task<ClaimsProviderResult> GetClaimsAsync(ClaimsProviderContext context, CancellationToken cancellationToken = default);
}

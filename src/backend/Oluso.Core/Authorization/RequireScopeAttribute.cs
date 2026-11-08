using Microsoft.AspNetCore.Http;

namespace Oluso.Core.Authorization;

/// <summary>
/// Endpoint metadata naming the OAuth scopes a token from a scope-restricted client
/// (see <see cref="ScopeRestrictedClients"/>) must carry to call the endpoint.
/// </summary>
public interface IScopeRequirementMetadata
{
    /// <summary>
    /// The scopes, any one of which admits the request; an empty list admits nothing.
    /// Null when this metadata doesn't apply to the request.
    /// </summary>
    IReadOnlyList<string>? GetRequiredScopes(HttpContext httpContext);
}

/// <summary>
/// Declares the scopes that let a scope-restricted client call an endpoint: any one of
/// <see cref="Scopes"/> admits it. Tokens of other clients are unaffected — first-party apps
/// keep working without the scope — so this narrows what a restricted client may do
/// with a user's token rather than adding a new check for everyone.
/// </summary>
/// <remarks>
/// Several attributes on one endpoint must all be satisfied. Set <see cref="WhenRouteValue"/>
/// and <see cref="RouteValueEquals"/> to make the attribute apply only when a route value has
/// that value (for example an entity type).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireScopeAttribute : Attribute, IScopeRequirementMetadata
{
    public RequireScopeAttribute(params string[] scopes)
    {
        Scopes = scopes;
    }

    /// <summary>Any one of these scopes admits the request.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>Route value the attribute is conditional on, if any.</summary>
    public string? WhenRouteValue { get; set; }

    /// <summary>Value <see cref="WhenRouteValue"/> must have (ordinal) for the attribute to apply.</summary>
    public string? RouteValueEquals { get; set; }

    public IReadOnlyList<string>? GetRequiredScopes(HttpContext httpContext)
    {
        if (string.IsNullOrEmpty(WhenRouteValue)) return Scopes;
        var value = httpContext.Request.RouteValues.TryGetValue(WhenRouteValue, out var v) ? v?.ToString() : null;
        return string.Equals(value, RouteValueEquals, StringComparison.Ordinal) ? Scopes : null;
    }
}

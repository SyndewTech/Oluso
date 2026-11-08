using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Conditions;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Resolves the value a journey condition reads, for every condition evaluator. Sources:
/// <list type="bullet">
/// <item><c>data</c> / <c>journeyData</c> / <c>state</c> / <c>previous_step</c>: the journey's data, which holds earlier steps' output.</item>
/// <item><c>claim</c>: the journey user's claims (as their session carries them), then claims earlier steps produced.</item>
/// <item><c>context</c>: <c>userId</c>, <c>tenantId</c>, <c>clientId</c>, <c>isAuthenticated</c>.</item>
/// <item><c>user</c>: the stored profile through <see cref="UserConditionFields"/>, then the journey's data.</item>
/// <item><c>input</c> / <c>config</c>: the current step's input and settings, where a step evaluates.</item>
/// <item><c>path</c>: a dot path into the journey's data.</item>
/// </list>
/// The user and their claims are loaded only when a condition reads them, are only
/// compared, and are never written to journey state.
/// </summary>
public sealed class JourneyConditionValues
{
    private static readonly Dictionary<string, string> ShortClaimTypes = new(StringComparer.Ordinal)
    {
        [ClaimTypes.NameIdentifier] = "sub",
        [ClaimTypes.Role] = "role",
        [ClaimTypes.Email] = "email",
        [ClaimTypes.Name] = "preferred_username",
        [ClaimTypes.GivenName] = "given_name",
        [ClaimTypes.Surname] = "family_name",
    };

    private static readonly Dictionary<string, string> ClaimAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roles"] = "role",
        ["username"] = "preferred_username",
    };

    public IDictionary<string, object> JourneyData { get; init; } = new Dictionary<string, object>();
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public Func<string, string?>? Input { get; init; }
    public Func<string, string?>? Config { get; init; }
    public OlusoUserInfo? User { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Claims { get; init; }

    public IReadOnlyList<string> Get(string? source, string? field)
    {
        field ??= "";
        return Normalize(source) switch
        {
            "" or "data" or "journeydata" or "state" or "previousstep" => FromData(field),
            "claim" or "claims" => FromClaims(field),
            "context" => FromContext(field),
            "user" => FromUser(field),
            "input" => One(Input?.Invoke(field)),
            "config" => One(Config?.Invoke(field)),
            "path" => ConditionOperators.ToValues(GetNested(field)),
            _ => Array.Empty<string>()
        };
    }

    /// <summary>Values for conditions a step evaluates against its own context, input and settings.</summary>
    public static async Task<JourneyConditionValues> ForStepAsync(
        StepExecutionContext context,
        IEnumerable<string?> sources,
        CancellationToken cancellationToken)
    {
        var read = sources.ToList();
        return new JourneyConditionValues
        {
            JourneyData = context.JourneyData,
            UserId = context.UserId,
            TenantId = context.TenantId,
            ClientId = context.ClientId,
            Input = context.GetInput,
            Config = key => context.GetConfig<string>(key, null),
            User = Reads(read, "user") ? await LoadUserAsync(context.ServiceProvider, context.UserId, cancellationToken) : null,
            Claims = Reads(read, "claim") ? await LoadClaimsAsync(context.ServiceProvider, context.UserId) : null
        };
    }

    public static bool Reads(IEnumerable<string?> sources, string source) =>
        sources.Any(s => Normalize(s) is var n && (n == source || n == source + "s"));

    public static async Task<OlusoUserInfo?> LoadUserAsync(IServiceProvider services, string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        var userService = services.GetService<IOlusoUserService>();
        return userService == null ? null : await userService.FindByIdAsync(userId, cancellationToken);
    }

    /// <summary>The claims the user's session would carry, keyed by their short names.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> LoadClaimsAsync(IServiceProvider services, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;

        var userManager = services.GetService<UserManager<OlusoUser>>();
        var principalFactory = services.GetService<IUserClaimsPrincipalFactory<OlusoUser>>();
        if (userManager == null || principalFactory == null) return null;

        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var principal = await principalFactory.CreateAsync(user);
        return principal.Claims
            .GroupBy(c => ShortClaimTypes.TryGetValue(c.Type, out var shortType) ? shortType : c.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.SelectMany(c => ConditionOperators.ToValues(c.Value)).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string? source) => (source ?? "").Trim().ToLowerInvariant().Replace("_", "");

    private IReadOnlyList<string> FromData(string field) =>
        JourneyData.TryGetValue(field, out var value)
            ? ConditionOperators.ToValues(value)
            : ConditionOperators.ToValues(JourneyData.FirstOrDefault(p => string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase)).Value);

    private IReadOnlyList<string> FromClaims(string field)
    {
        if (Claims != null)
        {
            var name = ClaimAliases.TryGetValue(field, out var alias) ? alias : field;
            if (Claims.TryGetValue(name, out var values) && values.Count > 0) return values;
        }
        return FromData(field);
    }

    private IReadOnlyList<string> FromContext(string field) => Normalize(field) switch
    {
        "userid" => One(UserId),
        "tenantid" => One(TenantId),
        "clientid" => One(ClientId),
        "isauthenticated" or "authenticated" => One(string.IsNullOrEmpty(UserId) ? "false" : "true"),
        _ => Array.Empty<string>()
    };

    private IReadOnlyList<string> FromUser(string field)
    {
        switch (Normalize(field))
        {
            case "id" or "userid":
                return One(UserId);
            case "authenticated" or "isauthenticated":
                return One(string.IsNullOrEmpty(UserId) ? "false" : "true");
        }

        var profileValue = User == null ? null : UserConditionFields.GetValue(User, field);
        return profileValue != null ? One(profileValue) : FromData(field);
    }

    private object? GetNested(string path)
    {
        object? current = JourneyData;
        foreach (var part in path.Split('.'))
        {
            if (current is IDictionary<string, object> dict && dict.TryGetValue(part, out var next))
            {
                current = next;
            }
            else if (current is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } element
                     && element.TryGetPropertyIgnoreCase(part, out var property))
            {
                current = property;
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    private static IReadOnlyList<string> One(string? value) =>
        string.IsNullOrEmpty(value) ? Array.Empty<string>() : new[] { value };
}

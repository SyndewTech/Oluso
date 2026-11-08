using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Prefix scopes: an ApiScope whose property <see cref="MatchProperty"/> is <see cref="PrefixMatch"/> stands for
/// every scope value made of its name followed by a suffix the client chooses (for example a per-device, per-tenant
/// or per-resource identifier). The scope granted is the full requested value; the ApiScope row supplies everything
/// else (display name, user claims, required feature), and a client is allowed any suffix when its AllowedScopes
/// lists the prefix scope's name.
/// </summary>
/// <remarks>
/// A suffix is valid when it is non-empty, no longer than <see cref="SuffixMaxLengthProperty"/> (default: the routed
/// tenant's protocol setting <see cref="TenantProtocolSettings.PrefixScopeSuffixMaxLength"/>, else
/// <see cref="DefaultSuffixMaxLength"/>), made only of characters a scope token may contain (RFC 6749 §3.3,
/// <see cref="IsScopeTokenChar"/>), and, when the scope has a <see cref="SuffixPatternProperty"/>, matched in full by
/// that pattern. The scope-token charset is always the outer bound: scopes travel space-separated, so a suffix with a
/// space, <c>"</c> or <c>\</c> could never be requested anyway, and a pattern can only narrow it. The prefix scope's
/// own name is not a grantable scope: the suffix can't be empty. When two prefix scopes match, the longer name wins,
/// and a scope with that exact name (an ordinary ApiScope) always wins over a prefix match.
/// </remarks>
public static class ApiScopePrefixes
{
    /// <summary>The ApiScope property that marks a prefix scope (value <see cref="PrefixMatch"/>).</summary>
    public const string MatchProperty = "match";

    /// <summary>The <see cref="MatchProperty"/> value of a prefix scope.</summary>
    public const string PrefixMatch = "prefix";

    /// <summary>
    /// Optional ApiScope property: a regular expression the whole suffix must match (it is anchored). It narrows the
    /// scope-token charset; it can't widen it.
    /// </summary>
    public const string SuffixPatternProperty = "suffix_pattern";

    /// <summary>Optional ApiScope property: the longest suffix accepted (a positive integer).</summary>
    public const string SuffixMaxLengthProperty = "suffix_max_length";

    /// <summary>
    /// The longest suffix accepted when the scope doesn't set <see cref="SuffixMaxLengthProperty"/>. RFC 6749 sets no
    /// limit on a scope token, but the value is client-chosen and is copied into authorization requests (URLs), codes,
    /// stored grants, every access token and audit records, so it needs a bound. 256 fits identifiers, URNs and most
    /// URLs a client would put after a prefix, while keeping one value from bloating tokens or query strings.
    /// </summary>
    public const int DefaultSuffixMaxLength = 256;

    /// <summary>The smallest value a tenant may set as its default suffix length.</summary>
    public const int MinDefaultSuffixMaxLength = 1;

    /// <summary>The largest value a tenant may set as its default suffix length.</summary>
    public const int MaxDefaultSuffixMaxLength = 2048;

    /// <summary>
    /// The default suffix length a tenant setting stands for: <paramref name="configured"/> when it is in
    /// [<see cref="MinDefaultSuffixMaxLength"/>, <see cref="MaxDefaultSuffixMaxLength"/>], else
    /// <see cref="DefaultSuffixMaxLength"/>.
    /// </summary>
    public static int EffectiveDefaultSuffixMaxLength(int? configured) =>
        configured is >= MinDefaultSuffixMaxLength and <= MaxDefaultSuffixMaxLength ? configured.Value : DefaultSuffixMaxLength;

    /// <summary>
    /// The routed tenant's default suffix length (its protocol settings, which come from the already-resolved tenant),
    /// or <see cref="DefaultSuffixMaxLength"/> without settings.
    /// </summary>
    public static async Task<int> GetDefaultSuffixMaxLengthAsync(
        ITenantSettingsProvider? settings, CancellationToken cancellationToken = default)
    {
        if (settings == null)
            return DefaultSuffixMaxLength;
        var protocol = await settings.GetProtocolSettingsAsync(cancellationToken);
        return EffectiveDefaultSuffixMaxLength(protocol?.PrefixScopeSuffixMaxLength);
    }

    /// <summary>How long a <see cref="SuffixPatternProperty"/> may run against one suffix before it counts as no match.</summary>
    public static readonly TimeSpan PatternMatchTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly ConcurrentDictionary<string, Regex?> Patterns = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="scope"/> is a prefix scope.</summary>
    public static bool IsPrefix(ApiScope scope) =>
        string.Equals(Property(scope, MatchProperty)?.Trim(), PrefixMatch, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="suffix"/> is a suffix <paramref name="prefixScope"/> accepts.
    /// <paramref name="defaultMaxLength"/> applies when the scope doesn't set <see cref="SuffixMaxLengthProperty"/>.
    /// </summary>
    public static bool IsValidSuffix(ApiScope prefixScope, string suffix, int defaultMaxLength = DefaultSuffixMaxLength)
    {
        if (string.IsNullOrEmpty(suffix))
            return false;

        var max = int.TryParse(Property(prefixScope, SuffixMaxLengthProperty), out var configured) && configured > 0
            ? configured
            : defaultMaxLength > 0 ? defaultMaxLength : DefaultSuffixMaxLength;
        if (suffix.Length > max)
            return false;

        // The outer bound for every prefix scope: a scope value is a scope-token.
        foreach (var c in suffix)
        {
            if (!IsScopeTokenChar(c))
                return false;
        }

        var pattern = Property(prefixScope, SuffixPatternProperty);
        if (string.IsNullOrWhiteSpace(pattern))
            return true;

        var regex = Patterns.GetOrAdd(pattern, p => TryCompilePattern(p, out var compiled) ? compiled : null);
        if (regex == null)
            return false; // A pattern that doesn't compile accepts nothing.

        try
        {
            return regex.IsMatch(suffix);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="c"/> may appear in an RFC 6749 §3.3 scope-token: <c>%x21 / %x23-5B / %x5D-7E</c>, i.e.
    /// printable ASCII except space, <c>"</c> and <c>\</c>.
    /// </summary>
    public static bool IsScopeTokenChar(char c) => c is '\x21' or (>= '\x23' and <= '\x5B') or (>= '\x5D' and <= '\x7E');

    /// <summary>
    /// Compiles <paramref name="pattern"/> as a <see cref="SuffixPatternProperty"/>: anchored to the whole suffix, with
    /// <see cref="PatternMatchTimeout"/> so a pathological pattern can't stall a request. False when it doesn't compile.
    /// </summary>
    public static bool TryCompilePattern(string pattern, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Regex? regex)
    {
        try
        {
            regex = new Regex($"^(?:{pattern})$", RegexOptions.CultureInvariant, PatternMatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            regex = null;
            return false;
        }
    }

    /// <summary>
    /// The enabled prefix scope among <paramref name="scopes"/> whose name <paramref name="value"/> starts with,
    /// the longest when several do, or null. <paramref name="suffixValid"/> tells whether the rest of the value is a
    /// suffix it accepts (<paramref name="defaultMaxLength"/> as in <see cref="IsValidSuffix"/>); a match with an
    /// invalid suffix is still returned so the caller can report why.
    /// </summary>
    public static ApiScope? FindPrefixScope(
        IEnumerable<ApiScope> scopes, string value, out bool suffixValid, int defaultMaxLength = DefaultSuffixMaxLength)
    {
        suffixValid = false;
        ApiScope? best = null;
        foreach (var scope in scopes)
        {
            if (!scope.Enabled || string.IsNullOrEmpty(scope.Name) || !IsPrefix(scope))
                continue;
            if (value.Length <= scope.Name.Length || !value.StartsWith(scope.Name, StringComparison.Ordinal))
                continue;
            if (best == null || scope.Name.Length > best.Name.Length)
                best = scope;
        }

        if (best != null)
            suffixValid = IsValidSuffix(best, value[best.Name.Length..], defaultMaxLength);
        return best;
    }

    /// <summary>The prefix scope <paramref name="value"/> is a valid instance of, or null.</summary>
    public static ApiScope? Match(IEnumerable<ApiScope> scopes, string value, int defaultMaxLength = DefaultSuffixMaxLength) =>
        FindPrefixScope(scopes, value, out var valid, defaultMaxLength) is { } scope && valid ? scope : null;

    /// <summary>
    /// The ApiScope each of <paramref name="names"/> stands for in the routed tenant: the scope with that exact
    /// name, else the prefix scope it is a valid instance of. Names that are neither are left out.
    /// <paramref name="defaultMaxLength"/> is the tenant's default suffix length (<see cref="GetDefaultSuffixMaxLengthAsync"/>).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, ApiScope>> ResolveAsync(
        IResourceStore store, IEnumerable<string> names, CancellationToken cancellationToken = default,
        int defaultMaxLength = DefaultSuffixMaxLength)
    {
        var wanted = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
        var resolved = new Dictionary<string, ApiScope>(StringComparer.Ordinal);
        if (wanted.Count == 0)
            return resolved;

        // The store matches names as the database compares them; a prefix scope's own name is not a scope value.
        var exact = new Dictionary<string, ApiScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in await store.FindApiScopesByNameAsync(wanted, cancellationToken) ?? Enumerable.Empty<ApiScope>())
        {
            if (scope?.Name != null && !IsPrefix(scope))
                exact.TryAdd(scope.Name, scope);
        }
        foreach (var name in wanted)
        {
            if (exact.TryGetValue(name, out var scope))
                resolved[name] = scope;
        }

        var rest = wanted.Where(n => !resolved.ContainsKey(n)).ToList();
        if (rest.Count == 0)
            return resolved;

        var prefixScopes = (await store.FindPrefixApiScopesAsync(cancellationToken) ?? Enumerable.Empty<ApiScope>()).ToList();
        if (prefixScopes.Count == 0)
            return resolved;

        foreach (var name in rest)
        {
            if (Match(prefixScopes, name, defaultMaxLength) is { } scope)
                resolved[name] = scope;
        }
        return resolved;
    }

    /// <summary>
    /// <paramref name="scopes"/> less the names of prefix scopes: what a client's AllowedScopes grant when no scope
    /// was requested. A prefix scope's name is not a value to issue, and its suffix is the client's to choose.
    /// </summary>
    public static async Task<List<string>> WithoutPrefixScopesAsync(
        IResourceStore? store, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var list = scopes.ToList();
        if (store == null || list.Count == 0)
            return list;

        var prefixNames = (await store.FindPrefixApiScopesAsync(cancellationToken) ?? Enumerable.Empty<ApiScope>())
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);
        return prefixNames.Count == 0 ? list : list.Where(s => !prefixNames.Contains(s)).ToList();
    }

    private static string? Property(ApiScope scope, string key) =>
        scope.Properties?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
}

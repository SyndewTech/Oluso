namespace Oluso.Core.Protocols;

/// <summary>
/// The Matrix Client-Server API scope tokens (Matrix spec v1.18, "OAuth 2.0 API" → "Scope"; MSC2967), stable and
/// unstable. The API scope is a literal <c>*</c>; the device scope is a prefix scope whose suffix is the Matrix
/// device id (see docs/prefix-scopes.md).
/// </summary>
public static class MatrixScopes
{
    /// <summary>Full access to the Client-Server API.</summary>
    public const string Api = "urn:matrix:client:api:*";

    /// <summary>The MSC2967 unstable form of <see cref="Api"/>.</summary>
    public const string UnstableApi = "urn:matrix:org.matrix.msc2967.client:api:*";

    /// <summary>The device scope prefix; the suffix is the device id.</summary>
    public const string DevicePrefix = "urn:matrix:client:device:";

    /// <summary>The MSC2967 unstable form of <see cref="DevicePrefix"/>.</summary>
    public const string UnstableDevicePrefix = "urn:matrix:org.matrix.msc2967.client:device:";

    /// <summary>The audience (resource URI) Matrix access tokens carry.</summary>
    public const string Audience = "urn:matrix:client";

    /// <summary>
    /// The suffix pattern Matrix device ids follow: any RFC 6749 scope-token characters (%x21 / %x23-5B / %x5D-7E), as the
    /// homeserver accepts them. Not just the URL-safe ones: matrix-rust-sdk (Element X) signs in with QR as a device whose
    /// id is its unpadded base64 Curve25519 key, which may hold + and /.
    /// </summary>
    public const string DeviceIdPattern = @"[\x21\x23-\x5B\x5D-\x7E]+";

    /// <summary>Whether <paramref name="scope"/> is a Matrix API scope (stable or unstable).</summary>
    public static bool IsApiScope(string? scope) => scope is Api or UnstableApi;

    /// <summary>Whether <paramref name="scope"/> is a Matrix device scope prefix itself (stable or unstable).</summary>
    public static bool IsDevicePrefix(string? scope) => scope is DevicePrefix or UnstableDevicePrefix;

    /// <summary>The device id of a Matrix device scope, or null when <paramref name="scope"/> isn't one.</summary>
    public static string? DeviceId(string? scope)
    {
        if (string.IsNullOrEmpty(scope)) return null;
        foreach (var prefix in new[] { DevicePrefix, UnstableDevicePrefix })
        {
            if (scope.StartsWith(prefix, StringComparison.Ordinal) && scope.Length > prefix.Length)
            {
                return scope[prefix.Length..];
            }
        }
        return null;
    }

    /// <summary>Whether <paramref name="scope"/> is any Matrix scope: API, device prefix or device value.</summary>
    public static bool IsMatrix(string? scope) => IsApiScope(scope) || IsDevicePrefix(scope) || DeviceId(scope) != null;

    /// <summary>Whether any of <paramref name="scopes"/> is a Matrix scope.</summary>
    public static bool AnyMatrix(IEnumerable<string> scopes) => scopes.Any(IsMatrix);
}

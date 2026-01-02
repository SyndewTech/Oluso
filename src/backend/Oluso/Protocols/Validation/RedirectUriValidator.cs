using Oluso.Core.Common;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of redirect URI validator
/// </summary>
public class RedirectUriValidator : IRedirectUriValidator
{
    private static readonly string[] LoopbackAddresses = { "127.0.0.1", "[::1]", "localhost" };

    public Task<ValidationResult> ValidateAsync(
        string? redirectUri,
        IEnumerable<string> allowedRedirectUris,
        bool isImplicitOrHybridFlow = false)
    {
        if (string.IsNullOrEmpty(redirectUri))
        {
            return Task.FromResult(ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "redirect_uri is required"));
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return Task.FromResult(ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "redirect_uri is not a valid URI"));
        }

        // For implicit/hybrid flows, fragment is not allowed
        if (isImplicitOrHybridFlow && !string.IsNullOrEmpty(uri.Fragment))
        {
            return Task.FromResult(ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "redirect_uri cannot contain a fragment"));
        }

        var allowedUris = allowedRedirectUris.ToList();

        // Check for exact match
        if (allowedUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            return Task.FromResult(ValidationResult.Success());
        }

        // Check for loopback redirect (RFC 8252)
        if (IsLoopbackRedirect(uri))
        {
            // For loopback, port can vary - check base URI
            var matchesLoopback = allowedUris.Any(allowed =>
            {
                if (!Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri))
                    return false;

                return IsLoopbackRedirect(allowedUri) &&
                       allowedUri.Scheme == uri.Scheme &&
                       allowedUri.AbsolutePath == uri.AbsolutePath;
            });

            if (matchesLoopback)
            {
                return Task.FromResult(ValidationResult.Success());
            }
        }

        // Check for custom scheme (native apps)
        if (IsCustomScheme(uri))
        {
            // Exact match required for custom schemes
            if (allowedUris.Contains(redirectUri, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(ValidationResult.Success());
            }
        }

        return Task.FromResult(ValidationResult.Failure(
            OidcConstants.Errors.InvalidRequest,
            "redirect_uri is not registered for this client"));
    }

    public Task<ValidationResult> ValidatePostLogoutAsync(
        string? postLogoutRedirectUri,
        IEnumerable<string> allowedPostLogoutRedirectUris)
    {
        // post_logout_redirect_uri is optional
        if (string.IsNullOrEmpty(postLogoutRedirectUri))
        {
            return Task.FromResult(ValidationResult.Success());
        }

        if (!Uri.TryCreate(postLogoutRedirectUri, UriKind.Absolute, out _))
        {
            return Task.FromResult(ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "post_logout_redirect_uri is not a valid URI"));
        }

        if (!allowedPostLogoutRedirectUris.Contains(postLogoutRedirectUri, StringComparer.Ordinal))
        {
            return Task.FromResult(ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "post_logout_redirect_uri is not registered for this client"));
        }

        return Task.FromResult(ValidationResult.Success());
    }

    public bool IsNativeClient(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
            return false;

        return IsCustomScheme(parsedUri) || IsLoopbackRedirect(parsedUri);
    }

    private static bool IsLoopbackRedirect(Uri uri)
    {
        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;

        return LoopbackAddresses.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCustomScheme(Uri uri)
    {
        return uri.Scheme != "http" &&
               uri.Scheme != "https" &&
               uri.Scheme != "urn";
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Jobs;
using WebhookEndpoint = Oluso.Core.Events.WebhookEndpoint;

namespace Oluso.Webhooks;

/// <summary>
/// Signs <see cref="WebhookAuthType.IssuerSigned"/> deliveries as the endpoint's tenant: its issuer (as its discovery
/// document states it) and its current signing key, the one its tokens carry and its <c>jwks_uri</c> publishes. The
/// receiver verifies the token like any token of that issuer, and the body against <c>body_sha256</c>, so nothing is
/// shared with it. Works in a fresh scope set to the endpoint's tenant, so retries and dispatches raised from another
/// tenant's request sign with the right tenant.
/// </summary>
public sealed class IssuerWebhookTokenIssuer : IWebhookTokenIssuer
{
    public const string TokenType = "webhook+jwt";

    /// <summary>How long a token is valid (<c>exp</c> - <c>iat</c>).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(300);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;

    public IssuerWebhookTokenIssuer(IServiceScopeFactory scopes, TimeProvider? clock = null)
    {
        _scopes = scopes;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<string> CreateTokenAsync(
        WebhookEndpoint endpoint,
        string deliveryId,
        string eventType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        string? token = null;
        await TenantScope.RunAsync(_scopes, endpoint.TenantId, async (sp, ct) =>
        {
            var credentials = await sp.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync(endpoint.TenantId, ct)
                ?? throw new InvalidOperationException($"Tenant '{endpoint.TenantId}' has no active signing key");
            // The tenant's issuer as its discovery document states it (that resolves it with the tenant in context too).
            var issuer = await sp.GetRequiredService<IIssuerResolver>().GetIssuerAsync(ct);
            token = Create(credentials, issuer, endpoint, deliveryId, eventType, body.Span, _clock.GetUtcNow());
        }, cancellationToken);
        return token!;
    }

    /// <summary>The token, signed with <paramref name="credentials"/> (asymmetric only: a receiver must verify it from a JWKS).</summary>
    public static string Create(
        SigningCredentials credentials,
        string issuer,
        WebhookEndpoint endpoint,
        string deliveryId,
        string eventType,
        ReadOnlySpan<byte> body,
        DateTimeOffset now)
    {
        if (credentials.Key is SymmetricSecurityKey)
            throw new InvalidOperationException("Issuer-signed webhooks need an asymmetric signing key");

        // Whole seconds, so exp - iat is exactly the lifetime.
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = TokenType,
            Issuer = issuer,
            Audience = endpoint.Url,
            IssuedAt = issuedAt.UtcDateTime,
            Expires = issuedAt.Add(Lifetime).UtcDateTime,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = string.IsNullOrWhiteSpace(endpoint.ClientId) ? endpoint.Id : endpoint.ClientId,
                [JwtRegisteredClaimNames.Jti] = deliveryId,
                ["webhook_event"] = eventType,
                ["body_sha256"] = BodyHash(body),
            },
        };

        // No nbf: the claims are exactly the ones above.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>base64url (no padding) of SHA-256 over the body bytes.</summary>
    public static string BodyHash(ReadOnlySpan<byte> body) => Base64UrlEncoder.Encode(SHA256.HashData(body));
}

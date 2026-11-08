using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;

namespace Oluso.Protocols;

/// <inheritdoc />
public class SessionHintMatcher : ISessionHintMatcher
{
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IOlusoUserService _userService;
    private readonly ILogger<SessionHintMatcher> _logger;

    public SessionHintMatcher(
        ISigningCredentialStore signingCredentialStore,
        IIssuerResolver issuerResolver,
        IOlusoUserService userService,
        ILogger<SessionHintMatcher> logger)
    {
        _signingCredentialStore = signingCredentialStore;
        _issuerResolver = issuerResolver;
        _userService = userService;
        _logger = logger;
    }

    public async Task<bool> MatchesAsync(
        string sessionUserId,
        string clientId,
        string? pairwiseSubjectSalt,
        string? idTokenHint,
        string? loginHint,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(idTokenHint))
        {
            var subject = await ReadSubjectAsync(idTokenHint, clientId, cancellationToken);
            if (subject == null) return false;

            // The client knows the user by the sub it was issued, which is pairwise for some clients.
            var expected = SubjectIdentifiers.ForClient(sessionUserId, clientId, pairwiseSubjectSalt);
            if (!string.Equals(subject, expected, StringComparison.Ordinal)) return false;
        }

        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            var user = await _userService.FindByIdAsync(sessionUserId, cancellationToken);
            if (user == null) return false;

            var hint = loginHint.Trim();
            var names = new[] { user.Email, user.Username, user.PhoneNumber };
            if (!names.Any(name => !string.IsNullOrEmpty(name) && string.Equals(name.Trim(), hint, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The sub of an ID token this server issued to the client. The signature, issuer and
    /// audience must hold; expiry doesn't matter, because the token is only a hint about who
    /// the user is (OpenID Connect Core 1.0 §3.1.2.1).
    /// </summary>
    private async Task<string?> ReadSubjectAsync(string idTokenHint, string clientId, CancellationToken cancellationToken)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        if (!handler.CanReadToken(idTokenHint))
        {
            _logger.LogInformation("id_token_hint is not a JWT");
            return null;
        }

        var keys = (await _signingCredentialStore.GetValidationKeysAsync(cancellationToken)).Select(k => k.Key).ToList();
        if (keys.Count == 0)
        {
            _logger.LogWarning("No validation keys to check id_token_hint with");
            return null;
        }

        try
        {
            handler.ValidateToken(idTokenHint, new TokenValidationParameters
            {
                ValidIssuer = await _issuerResolver.GetIssuerAsync(cancellationToken),
                ValidateIssuer = true,
                ValidAudience = clientId,
                ValidateAudience = true,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys
            }, out var validated);

            return (validated as JwtSecurityToken)?.Subject is { Length: > 0 } subject ? subject : null;
        }
        catch (Exception ex)
        {
            // Any token this server can't verify is simply not a hint for this session.
            _logger.LogInformation("id_token_hint was not issued by this server to {ClientId}: {Reason}", clientId, ex.GetType().Name);
            return null;
        }
    }
}

using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols.Services;

namespace Oluso.Protocols;

/// <inheritdoc />
public sealed class AuthorizationCodeIssuer : IAuthorizationCodeIssuer
{
    private static readonly string[] DirectRedirectUris = { DirectAuthorizationRequest.PostMessage, DirectAuthorizationRequest.FedCm };

    private readonly IClientStore _clients;
    private readonly IScopeValidator _scopes;
    private readonly IPkceValidator _pkce;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IConsentStore _consents;
    private readonly IAuthorizationCodeStore _codes;
    private readonly IOlusoUserService _users;
    private readonly ITenantContext _tenant;
    private readonly IClientUsageRecorder _usage;
    private readonly ILogger<AuthorizationCodeIssuer> _logger;
    private readonly ISignInAuditor? _auditor;
    private readonly ITokenTenantResolver? _tokenTenants;
    private readonly IClientUserAccess _clientUserAccess;

    public AuthorizationCodeIssuer(
        IClientStore clients,
        IScopeValidator scopes,
        IPkceValidator pkce,
        ITenantSettingsProvider tenantSettings,
        IConsentStore consents,
        IAuthorizationCodeStore codes,
        IOlusoUserService users,
        ITenantContext tenant,
        IClientUsageRecorder usage,
        IClientUserAccess clientUserAccess,
        ILogger<AuthorizationCodeIssuer> logger,
        ISignInAuditor? auditor = null,
        ITokenTenantResolver? tokenTenants = null)
    {
        _tokenTenants = tokenTenants;
        _clientUserAccess = clientUserAccess;
        _clients = clients;
        _scopes = scopes;
        _pkce = pkce;
        _tenantSettings = tenantSettings;
        _consents = consents;
        _codes = codes;
        _users = users;
        _tenant = tenant;
        _usage = usage;
        _logger = logger;
        _auditor = auditor;
    }

    public Task<DirectAuthorizationValidation> ValidateClientAsync(
        string? clientId, string? origin, string redirectUri = DirectAuthorizationRequest.PostMessage, CancellationToken cancellationToken = default) =>
        ValidateClientCoreAsync(clientId, origin, originRequired: true, redirectUri, cancellationToken);

    public Task<DirectAuthorizationValidation> ValidatePageClientAsync(
        string? clientId, string? origin, string redirectUri, CancellationToken cancellationToken = default) =>
        ValidateClientCoreAsync(clientId, origin, originRequired: false, redirectUri, cancellationToken);

    private async Task<DirectAuthorizationValidation> ValidateClientCoreAsync(
        string? clientId, string? origin, bool originRequired, string redirectUri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.InvalidRequest, "client_id is required");
        }

        if (!DirectRedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.InvalidRequest, "Unsupported redirect_uri");
        }

        // The client store is tenant-scoped: a client of another tenant is unknown here.
        var client = await _clients.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.InvalidClient, "Unknown client");
        }
        if (!client.Enabled)
        {
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.InvalidClient, "Client is disabled");
        }

        var protocol = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken);

        // The code is redeemed with the authorization_code grant, so the client and tenant must allow it.
        if (!client.AllowedGrantTypes.Any(g => g.GrantType == OidcConstants.GrantTypes.AuthorizationCode)
            || (protocol.AllowedGrantTypes?.Count > 0 && !protocol.AllowedGrantTypes.Contains(OidcConstants.GrantTypes.AuthorizationCode)))
        {
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.UnauthorizedClient,
                "The client is not allowed to use the authorization code flow");
        }

        // Clients bound to pushed or signed requests sign in only through the authorize endpoint.
        if (client.RequirePushedAuthorization || client.RequireRequestObject || protocol.RequirePushedAuthorizationRequests)
        {
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.InvalidRequest,
                "This client signs in through the authorize endpoint (PAR or request objects are required)");
        }

        // The code goes to a script on this origin instead of a registered redirect URI, so the origin must be
        // one the client registered: a CORS origin, or the origin of one of its redirect URIs.
        var registered = ClientOrigins(client);
        if (!originRequired && string.IsNullOrEmpty(origin))
        {
            // No page asking (yet): the client must at least have a page origin codes could go to.
            return registered.Count > 0
                ? new DirectAuthorizationValidation { Client = client }
                : DirectAuthorizationValidation.Failure(OidcConstants.Errors.AccessDenied, IAuthorizationCodeIssuer.OriginNotAllowed);
        }

        var normalized = NormalizeOrigin(origin);
        if (normalized == null || !registered.Contains(normalized))
        {
            _logger.LogWarning("Origin {Origin} is not registered for client {ClientId}; no code issued", origin ?? "(none)", clientId);
            return DirectAuthorizationValidation.Failure(OidcConstants.Errors.AccessDenied, IAuthorizationCodeIssuer.OriginNotAllowed);
        }

        return new DirectAuthorizationValidation { Client = client, Origin = normalized };
    }

    public async Task<DirectAuthorizationValidation> ValidateAsync(DirectAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var clientValidation = await ValidateClientAsync(request.ClientId, request.Origin, request.RedirectUri, cancellationToken);
        if (!clientValidation.IsValid) return clientValidation;
        var client = clientValidation.Client!;

        // Scopes: the same check as the authorize endpoint, so a page can't get more than the client is allowed:
        // scopes the client may not have, that don't exist, or that the tenant doesn't offer are left out (a
        // strict client is refused instead). The default set (no scope named) is trimmed to what the client allows.
        var allowed = client.AllowedScopes.Select(s => s.Scope).ToList();
        var requested = _scopes.ParseScopes(request.Scope).ToList();
        if (requested.Count == 0)
        {
            requested = _scopes.ParseScopes(DirectAuthorizationRequest.DefaultScope)
                .Where(scope => allowed.Contains(scope, StringComparer.Ordinal))
                .ToList();
        }
        var scopeValidation = await _scopes.ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = requested,
            ClientAllowedScopes = allowed,
            ClientId = client.ClientId,
            Endpoint = "direct",
            Strict = ScopeValidationContext.IsStrict(client.Properties)
        }, cancellationToken);
        if (!scopeValidation.IsValid)
        {
            return DirectAuthorizationValidation.Failure(scopeValidation.Error!, scopeValidation.ErrorDescription ?? "Invalid scope");
        }

        // PKCE S256 always: the code reaches a script, so only the page holding the verifier may redeem it.
        var pkce = _pkce.ValidateCodeChallenge(
            request.CodeChallenge,
            request.CodeChallengeMethod ?? CodeChallengeMethods.Sha256,
            pkceRequired: true,
            allowPlainText: false);
        if (!pkce.IsValid)
        {
            return DirectAuthorizationValidation.Failure(pkce.Error!, pkce.ErrorDescription ?? "A PKCE S256 code_challenge is required");
        }

        return new DirectAuthorizationValidation
        {
            Client = client,
            Origin = clientValidation.Origin,
            Scopes = scopeValidation.ValidScopes.ToList()
        };
    }

    public async Task<AuthorizationCodeIssueResult> IssueAsync(
        DirectAuthorizationRequest request, SignInCompletion signIn, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AuthorizationCodeIssueResult.Failure(validation.Error!, validation.ErrorDescription);
        }
        var client = validation.Client!;

        var user = await _users.FindByIdAsync(signIn.UserId, cancellationToken);
        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("No code for user {UserId}: not found or inactive", signIn.UserId);
            return AuthorizationCodeIssueResult.Failure(OidcConstants.Errors.AccessDenied, "User not found or inactive");
        }

        // A tenant's host issues only its own tokens: no code for a user without access to it.
        if (_tokenTenants != null && !await _tokenTenants.CanIssueOnRoutedTenantAsync(user.Id, cancellationToken))
        {
            _logger.LogWarning("No code for user {UserId}: no access to tenant {TenantId}", user.Id, _tenant.TenantId);
            return AuthorizationCodeIssueResult.Failure(OidcConstants.Errors.AccessDenied, Oidc.OidcProtocolService.NoTenantAccessDescription);
        }

        // The client's user restrictions, against the tenant the code is for (as the token endpoint checks them).
        var codeTenant = !string.IsNullOrEmpty(user.TenantId) ? user.TenantId : (_tenant.HasTenant ? _tenant.TenantId : null);
        var denial = await _clientUserAccess.FindDenialAsync(
            client.ClientId, ClientUserRestrictions.From(client), user.Id, codeTenant, cancellationToken);
        if (denial != null)
        {
            return AuthorizationCodeIssueResult.Failure(OidcConstants.Errors.AccessDenied, denial);
        }

        // Consent, as at the authorize endpoint: what the user agreed to in this sign-in limits the grant;
        // a client that requires consent otherwise needs it remembered, since there's no screen to ask on.
        var scopes = validation.Scopes.ToList();
        if (signIn.ConsentedScopes != null)
        {
            scopes = scopes
                .Where(scope => scope == OidcConstants.Scopes.OpenId || signIn.ConsentedScopes.Contains(scope, StringComparer.Ordinal))
                .ToList();
        }
        else if (client.RequireConsent && !await _consents.HasConsentAsync(user.Id, client.ClientId, scopes, cancellationToken))
        {
            return AuthorizationCodeIssueResult.Failure(OidcConstants.Errors.ConsentRequired,
                "The user has not consented to this client; sign in through the authorize endpoint");
        }

        var sessionId = string.IsNullOrEmpty(signIn.SessionId) ? Guid.NewGuid().ToString("N") : signIn.SessionId;
        var amr = signIn.Amr.Prepend(signIn.AuthenticationMethod)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tenantId = codeTenant;

        var code = AuthorizationCodes.Create(new NewAuthorizationCode
        {
            ClientId = client.ClientId,
            SubjectId = user.Id,
            RedirectUri = request.RedirectUri,
            Scopes = scopes,
            CodeChallenge = request.CodeChallenge,
            CodeChallengeMethod = request.CodeChallengeMethod ?? CodeChallengeMethods.Sha256,
            Nonce = request.Nonce,
            SessionId = sessionId,
            AuthTime = (signIn.AuthTime ?? DateTimeOffset.UtcNow).UtcDateTime,
            Amr = amr,
            Acr = signIn.Acr,
            Claims = signIn.Claims,
            TenantId = tenantId,
            LifetimeSeconds = client.AuthorizationCodeLifetime
        });
        await _codes.StoreAsync(code, cancellationToken);

        if (signIn.IsNewSignIn && _auditor != null)
        {
            await _auditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = user.Id,
                Username = user.Username ?? user.Email ?? user.Id,
                TenantId = tenantId,
                AuthenticationMethod = signIn.AuthenticationMethod,
                ClientId = client.ClientId,
                SessionId = sessionId
            }, cancellationToken);
        }
        await _usage.RecordAsync(client.ClientId, cancellationToken);

        _logger.LogInformation(
            "Authorization code issued to {Origin} for client {ClientId}, user {UserId}, method {Method}, scopes {Scopes}",
            validation.Origin, client.ClientId, user.Id, signIn.AuthenticationMethod, string.Join(" ", scopes));

        return new AuthorizationCodeIssueResult { Code = code.Code };
    }

    public Task<AuthorizationCodeIssueResult> IssueForJourneyAsync(
        DirectAuthorizationRequest request, JourneyState journey, JourneyCompletionResult completion, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string>? consented = null;
        if (journey.Data?.TryGetValue(ConsentDecisions.ConsentedScopesDataKey, out var value) == true
            && value?.ToString() is { Length: > 0 } granted)
        {
            consented = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        var methods = JourneyAuthenticationMethods.Read(journey.Data);
        return IssueAsync(request, new SignInCompletion
        {
            UserId = completion.UserId,
            AuthenticationMethod = JourneyAuthenticationMethods.Method(journey.Data) ?? "journey",
            Amr = methods,
            SessionId = completion.SessionId ?? journey.SessionId ?? journey.Id,
            Claims = completion.Claims,
            ConsentedScopes = consented,
            // A journey that only ran on an existing session signed nobody in.
            IsNewSignIn = JourneyAuthenticationMethods.AuthenticatedInJourney(journey.Data)
        }, cancellationToken);
    }

    /// <summary>scheme://host[:port] of an http(s) address, lower-cased; null for anything else.</summary>
    public static string? NormalizeOrigin(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant();
    }

    private static HashSet<string> ClientOrigins(Client client)
    {
        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var address in client.AllowedCorsOrigins.Select(o => o.Origin)
                     .Concat(client.RedirectUris.Select(r => r.RedirectUri)))
        {
            if (NormalizeOrigin(address) is { } origin) origins.Add(origin);
        }
        return origins;
    }
}

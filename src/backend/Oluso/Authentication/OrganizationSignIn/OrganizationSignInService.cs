using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols.Oidc;

namespace Oluso.Authentication.OrganizationSignIn;

/// <summary>
/// "Sign in with your organisation" from the platform login page. A client
/// registered only against the platform issuer can then reach a tenant's own
/// identity providers: the platform hands the browser to the tenant's login
/// with a pending request, the tenant authenticates the user exactly as for any
/// other sign-in on its host, and returns a single-use code the platform
/// redeems to sign the same user in on the platform host.
///
/// <para>Two lifetimes. The pending request (and the browser binding cookie)
/// lasts as long as any in-flight authentication, so an IdP round trip, MFA,
/// password reset or consent at the tenant fits inside it. The hand-off code is
/// minted only after the tenant has authenticated the user and is redeemed by
/// the immediate redirect, so it lives for seconds.</para>
///
/// <para>State reuses existing stores: the pending request lives in the
/// protocol state store (distributed cache, readable from the tenant host); the
/// hand-off code is an action token (hashed at rest, atomic single use, purged
/// with the other expired records); the browser is bound to the request by a
/// host-only cookie on the platform host whose nonce is stored only as a hash.</para>
/// </summary>
public interface IOrganizationSignInService
{
    /// <summary>True when this request is on the platform host and the option is enabled.</summary>
    bool IsOffered(HttpContext http);

    /// <summary>
    /// Standalone platform login: URL of the organisation form for the protocol
    /// callback <paramref name="returnUrl"/>, or null when the option is not offered here.
    /// </summary>
    Task<string?> GetEntryUrlAsync(HttpContext http, string? returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Journey platform login: URL of the organisation form for the journey
    /// <paramref name="journeyId"/>, or null when the option is not offered here.
    /// </summary>
    Task<string?> GetJourneyEntryUrlAsync(HttpContext http, string? journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the organisation form may be shown: offered on this host, and exactly
    /// one of a live protocol callback (standalone) or an in-progress journey resuming
    /// a live protocol flow (journey) to return to.
    /// </summary>
    Task<bool> CanBeginAsync(HttpContext http, string? returnUrl, string? journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Platform host: resolve the organisation and send the browser to its login. A
    /// standalone login resumes at <paramref name="returnUrl"/> (the authorize
    /// callback); a journey login resumes the journey <paramref name="journeyId"/>.
    /// </summary>
    Task<OrganizationSignInBeginResult> BeginAsync(HttpContext http, string? identifier, string? returnUrl, string? journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Journey platform login: the organisation sign-in result handed to the journey
    /// by the callback, removed on read so it completes exactly one login step.
    /// </summary>
    Task<OrganizationAuthentication?> TakeJourneyResultAsync(string journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant host: the pending request, checked to exist, be unexpired and target
    /// the tenant this host serves. Failures are audited against <paramref name="stage"/>.
    /// </summary>
    Task<OrganizationHandoffLookup> GetRequestForTenantHostAsync(HttpContext http, string? requestId, string stage, CancellationToken cancellationToken = default);

    /// <summary>Tenant host: relative URL the tenant's login returns to once the user is authenticated.</summary>
    string BuildCompleteUrl(OrganizationHandoffRequest request, string? journeyId = null);

    /// <summary>Absolute platform URL of the "your sign-in took too long" page for a request.</summary>
    string BuildExpiredUrl(string requestId);

    /// <summary>Tenant host: mint the hand-off code and return the platform callback URL carrying it.</summary>
    Task<string> IssueHandoffAsync(HttpContext http, OrganizationHandoffRequest request, OrganizationAuthentication authentication, CancellationToken cancellationToken = default);

    /// <summary>Tenant host: audit a refused completion.</summary>
    Task RecordCompleteFailureAsync(HttpContext http, OrganizationHandoffRequest request, string error, CancellationToken cancellationToken = default);

    /// <summary>Platform host: redeem the code and verify the browser, request and user.</summary>
    Task<OrganizationSignInRedeemResult> RedeemAsync(HttpContext http, string? code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Platform host: drop the request and binding and audit success. For a journey
    /// login, also hands the result to the journey's login step; for a standalone
    /// login the caller has already issued the session.
    /// </summary>
    Task FinishAsync(HttpContext http, OrganizationSignInRedeemResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Platform host: for a pending request this browser started, the relative URL
    /// that starts the original authorize request over. Null when the request is
    /// unknown, belongs to another browser, or cannot be replayed. Drops the request.
    /// </summary>
    Task<string?> TakeRestartUrlAsync(HttpContext http, string? requestId, CancellationToken cancellationToken = default);
}

public sealed class OrganizationSignInService : IOrganizationSignInService
{
    private const string ReturnUrlKey = "ReturnUrl";
    private const string JourneyIdKey = "JourneyId";
    private const string RestartUrlKey = "RestartUrl";
    private const string BindingHashKey = "BindingHash";
    private const string ClientIdKey = "OriginalClientId";
    private const string PromptKey = "Prompt";
    private const string MaxAgeKey = "MaxAge";
    private const string LoginHintKey = "LoginHint";
    private const string UiLocalesKey = "UiLocales";
    private const string ExpiresAtKey = "ExpiresAt";

    private static readonly string[] NonReplayableAuthorizeParameters = { "request", "request_uri" };

    private readonly IProtocolStateStore _protocolState;
    private readonly IJourneyStateStore _journeyStates;
    private readonly ITenantDiscoveryService _discovery;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IActionTokenService _actionTokens;
    private readonly IDistributedCache _cache;
    private readonly IOlusoUserService _users;
    private readonly IOlusoEventService _events;
    private readonly IConfiguration _configuration;
    private readonly OlusoOptions _options;
    private readonly OidcEndpointConfiguration _oidcEndpoints;
    private readonly TimeSpan _stateExpiration;
    private readonly ILogger<OrganizationSignInService> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly ITenantMembershipStore? _memberships;
    private readonly TimeProvider _clock;

    public OrganizationSignInService(
        IProtocolStateStore protocolState,
        IJourneyStateStore journeyStates,
        ITenantDiscoveryService discovery,
        IIssuerResolver issuerResolver,
        IActionTokenService actionTokens,
        IDistributedCache cache,
        IOlusoUserService users,
        IOlusoEventService events,
        IConfiguration configuration,
        IOptions<OlusoOptions> options,
        IOptions<OidcEndpointConfiguration> oidcEndpoints,
        ILogger<OrganizationSignInService> logger,
        ITenantContext? tenantContext = null,
        ITenantMembershipStore? memberships = null,
        TimeProvider? clock = null)
    {
        _protocolState = protocolState;
        _journeyStates = journeyStates;
        _discovery = discovery;
        _issuerResolver = issuerResolver;
        _actionTokens = actionTokens;
        _cache = cache;
        _users = users;
        _events = events;
        _configuration = configuration;
        _options = options.Value;
        _oidcEndpoints = oidcEndpoints.Value;
        _stateExpiration = oidcEndpoints.Value.StateExpiration;
        _logger = logger;
        _tenantContext = tenantContext;
        _memberships = memberships;
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsOffered(HttpContext http)
    {
        if (!_options.EnableOrganizationSignIn || !_discovery.IsAvailable) return false;

        var contextTenant = _tenantContext?.HasTenant == true ? _tenantContext.TenantId : null;
        if (contextTenant != null && contextTenant != PlatformConstants.TenantId) return false;

        var platformIssuer = _configuration["Oluso:IssuerUri"];
        return !string.IsNullOrEmpty(platformIssuer) && IsSameOrigin(http.Request, platformIssuer);
    }

    public async Task<string?> GetEntryUrlAsync(HttpContext http, string? returnUrl, CancellationToken cancellationToken = default)
    {
        if (!IsOffered(http)) return null;
        var resume = await ResolveResumeAsync(http, returnUrl, null, cancellationToken);
        return resume == null
            ? null
            : $"{OrganizationSignInDefaults.EntryPath}?returnUrl={Uri.EscapeDataString(resume.ReturnUrl)}";
    }

    public async Task<string?> GetJourneyEntryUrlAsync(HttpContext http, string? journeyId, CancellationToken cancellationToken = default)
    {
        if (!IsOffered(http)) return null;
        var resume = await ResolveResumeAsync(http, null, journeyId, cancellationToken);
        return resume == null
            ? null
            : $"{OrganizationSignInDefaults.EntryPath}?journey={Uri.EscapeDataString(resume.JourneyId!)}";
    }

    public async Task<bool> CanBeginAsync(HttpContext http, string? returnUrl, string? journeyId, CancellationToken cancellationToken = default) =>
        IsOffered(http) && await ResolveResumeAsync(http, returnUrl, journeyId, cancellationToken) != null;

    public async Task<OrganizationAuthentication?> TakeJourneyResultAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        var key = JourneyResultKey(journeyId);
        var json = await _cache.GetStringAsync(key, cancellationToken);
        if (json == null) return null;
        await _cache.RemoveAsync(key, cancellationToken);
        return JsonSerializer.Deserialize<OrganizationAuthentication>(json);
    }

    public async Task<OrganizationSignInBeginResult> BeginAsync(
        HttpContext http, string? identifier, string? returnUrl, string? journeyId, CancellationToken cancellationToken = default)
    {
        if (!IsOffered(http))
        {
            return OrganizationSignInBeginResult.Failure(OrganizationSignInErrors.NotOffered);
        }

        var resume = await ResolveResumeAsync(http, returnUrl, journeyId, cancellationToken);
        if (resume == null)
        {
            return OrganizationSignInBeginResult.Failure(OrganizationSignInErrors.InvalidReturnUrl);
        }
        var originalState = resume.Original;
        var clientId = string.IsNullOrEmpty(originalState.ClientId) ? null : originalState.ClientId;

        var failureKey = FailureKey(http);
        if (await FailedLookupsAsync(failureKey, cancellationToken) >= OrganizationSignInDefaults.MaxFailedLookups)
        {
            await RaiseAsync(http, OrganizationSignInEvent.StageBegin, null, false, OrganizationSignInErrors.TooManyAttempts, clientId: clientId, cancellationToken: cancellationToken);
            return OrganizationSignInBeginResult.Failure(OrganizationSignInErrors.TooManyAttempts);
        }

        var discovered = string.IsNullOrWhiteSpace(identifier)
            ? null
            : await _discovery.FindByIdentifierAsync(identifier, cancellationToken);
        if (discovered == null
            || discovered.Tenant.Id == PlatformConstants.TenantId
            || discovered.Tenant.Identifier == PlatformConstants.TenantId
            || !discovered.Tenant.AllowPlatformOrgSignIn)
        {
            await RecordFailedLookupAsync(failureKey, cancellationToken);
            await RaiseAsync(http, OrganizationSignInEvent.StageBegin, null, false, OrganizationSignInErrors.UnknownOrganization, clientId: clientId, cancellationToken: cancellationToken);
            return OrganizationSignInBeginResult.Failure(OrganizationSignInErrors.UnknownOrganization);
        }

        // Resuming needs the original authorize request, so the hand-off expires with it.
        var now = _clock.GetUtcNow().UtcDateTime;
        var expiresAt = originalState.CreatedAt + _stateExpiration;
        if (expiresAt <= now)
        {
            return OrganizationSignInBeginResult.Failure(OrganizationSignInErrors.InvalidReturnUrl);
        }
        var retainFor = expiresAt - now + OrganizationSignInDefaults.ExpiredRequestRetention;

        var nonce = RandomToken();
        var properties = new Dictionary<string, string>
        {
            [ReturnUrlKey] = resume.ReturnUrl,
            [BindingHashKey] = Hash(nonce),
            [ExpiresAtKey] = expiresAt.ToString("O", CultureInfo.InvariantCulture),
        };
        if (clientId != null) properties[ClientIdKey] = clientId;
        if (resume.JourneyId != null) properties[JourneyIdKey] = resume.JourneyId;
        CopyAuthorizeParameters(originalState, properties);

        var requestId = await _protocolState.StoreAsync(new ProtocolState
        {
            ProtocolName = OrganizationSignInDefaults.ProtocolName,
            SerializedRequest = "{}",
            ClientId = string.Empty,
            TenantId = discovered.Tenant.Id,
            CreatedAt = now,
            Properties = properties,
        }, retainFor, cancellationToken);

        http.Response.Cookies.Append(BindingCookieName(requestId), nonce, BindingCookieOptions(retainFor));

        await RaiseAsync(http, OrganizationSignInEvent.StageBegin, discovered.Tenant.Id, true, null, clientId: clientId, cancellationToken: cancellationToken);
        _logger.LogInformation("Organisation sign-in handed to tenant {TenantId}", discovered.Tenant.Id);

        return OrganizationSignInBeginResult.Success(
            $"{discovered.IssuerUri.TrimEnd('/')}{OrganizationSignInDefaults.StartPath}?request={Uri.EscapeDataString(requestId)}");
    }

    public async Task<OrganizationHandoffLookup> GetRequestForTenantHostAsync(
        HttpContext http, string? requestId, string stage, CancellationToken cancellationToken = default)
    {
        var (request, error) = await LoadRequestAsync(requestId, cancellationToken);
        if (request != null && error == null)
        {
            error = await CheckTenantHostAsync(http, request, cancellationToken);
        }

        if (error != null)
        {
            _logger.LogWarning("Organisation sign-in {Stage} refused on tenant host: {Error}", stage, error);
            await RaiseAsync(http, stage, request?.TenantId ?? _tenantContext?.TenantId, false, error, clientId: request?.ClientId, cancellationToken: cancellationToken);
            return new OrganizationHandoffLookup(request, error);
        }

        if (stage == OrganizationSignInEvent.StageStart)
        {
            await RaiseAsync(http, stage, request!.TenantId, true, null, clientId: request.ClientId, cancellationToken: cancellationToken);
        }
        return new OrganizationHandoffLookup(request, null);
    }

    public string BuildCompleteUrl(OrganizationHandoffRequest request, string? journeyId = null)
    {
        var url = $"{OrganizationSignInDefaults.CompletePath}?correlation_id={Uri.EscapeDataString(request.Id)}";
        return string.IsNullOrEmpty(journeyId) ? url : $"{url}&journey={Uri.EscapeDataString(journeyId)}";
    }

    public string BuildExpiredUrl(string requestId) =>
        $"{PlatformIssuer()}{OrganizationSignInDefaults.ExpiredPath}?request={Uri.EscapeDataString(requestId)}";

    public async Task<string> IssueHandoffAsync(
        HttpContext http, OrganizationHandoffRequest request, OrganizationAuthentication authentication, CancellationToken cancellationToken = default)
    {
        var platformIssuer = PlatformIssuer();

        await _cache.SetStringAsync(
            AuthenticationKey(request.Id),
            JsonSerializer.Serialize(authentication),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = OrganizationSignInDefaults.HandoffCodeLifetime },
            cancellationToken);

        var code = await _actionTokens.IssueAsync(
            request.TenantId,
            authentication.UserId,
            OrganizationSignInDefaults.HandoffAction,
            request.Id,
            OrganizationSignInDefaults.HandoffCodeLifetime,
            cancellationToken);

        await RaiseAsync(http, OrganizationSignInEvent.StageComplete, request.TenantId, true, null,
            authentication.UserId, request.ClientId, authentication.IdentityProvider, cancellationToken);

        return $"{platformIssuer}{OrganizationSignInDefaults.CallbackPath}?code={Uri.EscapeDataString(code)}";
    }

    public Task RecordCompleteFailureAsync(HttpContext http, OrganizationHandoffRequest request, string error, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Organisation sign-in completion refused on tenant {TenantId}: {Error}", request.TenantId, error);
        return RaiseAsync(http, OrganizationSignInEvent.StageComplete, request.TenantId, false, error, clientId: request.ClientId, cancellationToken: cancellationToken);
    }

    public async Task<OrganizationSignInRedeemResult> RedeemAsync(HttpContext http, string? code, CancellationToken cancellationToken = default)
    {
        var result = await RedeemCoreAsync(http, code, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Organisation sign-in redeem refused: {Error}", result.Error);
            await RaiseAsync(http, OrganizationSignInEvent.StageRedeem, result.Request?.TenantId, false, result.Error,
                result.UserId, result.Request?.ClientId, cancellationToken: cancellationToken);
        }
        return result;
    }

    public async Task FinishAsync(HttpContext http, OrganizationSignInRedeemResult result, CancellationToken cancellationToken = default)
    {
        var request = result.Request ?? throw new ArgumentException("Redeem result carries no request", nameof(result));

        if (request.JourneyId != null)
        {
            await _cache.SetStringAsync(
                JourneyResultKey(request.JourneyId),
                JsonSerializer.Serialize(new OrganizationAuthentication(result.UserId!, result.IdentityProvider, result.AuthenticationMethods)),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = OrganizationSignInDefaults.HandoffCodeLifetime },
                cancellationToken);
        }

        await DropRequestAsync(http, request.Id, cancellationToken);
        await RaiseAsync(http, OrganizationSignInEvent.StageRedeem, request.TenantId, true, null,
            result.UserId, request.ClientId, result.IdentityProvider, cancellationToken);
    }

    public async Task<string?> TakeRestartUrlAsync(HttpContext http, string? requestId, CancellationToken cancellationToken = default)
    {
        if (!IsOffered(http)) return null;

        var (request, _) = await LoadRequestAsync(requestId, cancellationToken);
        if (request == null || !await IsBoundToBrowserAsync(http, request.Id, cancellationToken)) return null;

        await DropRequestAsync(http, request.Id, cancellationToken);
        return request.RestartUrl;
    }

    private async Task<OrganizationSignInRedeemResult> RedeemCoreAsync(HttpContext http, string? code, CancellationToken cancellationToken)
    {
        if (!IsOffered(http))
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.NotOffered);
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.InvalidCode);
        }

        // Peek first so a code minted for some other action is never consumed here.
        var peeked = await _actionTokens.PeekAsync(code, cancellationToken);
        if (peeked == null || peeked.Action != OrganizationSignInDefaults.HandoffAction)
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.InvalidCode);
        }

        var claim = await _actionTokens.RedeemAsync(code, cancellationToken);
        if (claim == null)
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.InvalidCode);
        }

        var (request, error) = await LoadRequestAsync(claim.SubjectId, cancellationToken);
        if (request == null)
        {
            return OrganizationSignInRedeemResult.Failure(error!);
        }
        if (!string.Equals(request.TenantId, claim.TenantId, StringComparison.Ordinal))
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.TenantMismatch, request);
        }
        if (!await IsBoundToBrowserAsync(http, request.Id, cancellationToken))
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.BindingMismatch, request);
        }
        if (error != null)
        {
            return OrganizationSignInRedeemResult.Failure(error, request);
        }

        var authJson = await _cache.GetStringAsync(AuthenticationKey(request.Id), cancellationToken);
        var authentication = authJson == null ? null : JsonSerializer.Deserialize<OrganizationAuthentication>(authJson);
        if (authentication == null || !string.Equals(authentication.UserId, claim.UserId, StringComparison.Ordinal))
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.HandoffStateMissing, request);
        }

        var user = await _users.FindByIdAsync(claim.UserId, cancellationToken);
        var userError = user switch
        {
            null => OrganizationSignInErrors.UserNotFound,
            { IsActive: false } => OrganizationSignInErrors.UserInactive,
            { LockoutEnabled: true, LockoutEnd: { } end } when end > _clock.GetUtcNow() => OrganizationSignInErrors.UserLockedOut,
            _ => null
        };
        if (userError != null)
        {
            return OrganizationSignInRedeemResult.Failure(userError, request) with { UserId = claim.UserId };
        }

        if (!string.Equals(user!.TenantId, request.TenantId, StringComparison.Ordinal)
            && (_memberships == null
                || await _memberships.GetByTenantAndUserAsync(request.TenantId, user.Id, cancellationToken) == null))
        {
            return OrganizationSignInRedeemResult.Failure(OrganizationSignInErrors.NotAMember, request) with { UserId = claim.UserId };
        }

        return new OrganizationSignInRedeemResult
        {
            Succeeded = true,
            Request = request,
            UserId = user.Id,
            IdentityProvider = authentication.IdentityProvider,
            AuthenticationMethods = authentication.AuthenticationMethods,
        };
    }

    private async Task<(OrganizationHandoffRequest? Request, string? Error)> LoadRequestAsync(string? requestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return (null, OrganizationSignInErrors.RequestNotFound);
        }

        var state = await _protocolState.GetAsync(requestId, cancellationToken);
        if (state == null
            || state.ProtocolName != OrganizationSignInDefaults.ProtocolName
            || string.IsNullOrEmpty(state.TenantId)
            || !state.Properties.TryGetValue(ReturnUrlKey, out var returnUrl)
            || !DateTime.TryParse(Property(state, ExpiresAtKey), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            return (null, OrganizationSignInErrors.RequestNotFound);
        }

        var request = new OrganizationHandoffRequest
        {
            Id = requestId,
            TenantId = state.TenantId,
            ReturnUrl = returnUrl,
            CreatedAt = state.CreatedAt,
            ExpiresAt = expiresAt,
            JourneyId = Property(state, JourneyIdKey),
            ClientId = Property(state, ClientIdKey),
            RestartUrl = Property(state, RestartUrlKey),
            Prompt = Property(state, PromptKey),
            MaxAge = int.TryParse(Property(state, MaxAgeKey), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : null,
            LoginHint = Property(state, LoginHintKey),
            UiLocales = Property(state, UiLocalesKey),
        };

        return request.ExpiresAt <= _clock.GetUtcNow().UtcDateTime
            ? (request, OrganizationSignInErrors.RequestExpired)
            : (request, null);
    }

    private static string? Property(ProtocolState state, string key) =>
        state.Properties.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private async Task<string?> CheckTenantHostAsync(HttpContext http, OrganizationHandoffRequest request, CancellationToken cancellationToken)
    {
        var tenant = _tenantContext?.Tenant;
        if (tenant == null
            || tenant.Id == PlatformConstants.TenantId
            || !string.Equals(tenant.Id, request.TenantId, StringComparison.Ordinal))
        {
            return OrganizationSignInErrors.TenantMismatch;
        }

        // The tenant context can also come from a header or ?tenant= on another
        // host; only the tenant's own issuer host may carry its sign-in.
        var issuer = await _issuerResolver.GetIssuerForTenantAsync(tenant, cancellationToken);
        return IsSameOrigin(http.Request, issuer) ? null : OrganizationSignInErrors.HostMismatch;
    }

    private async Task<bool> IsBoundToBrowserAsync(HttpContext http, string requestId, CancellationToken cancellationToken)
    {
        var state = await _protocolState.GetAsync(requestId, cancellationToken);
        var storedHash = state == null ? null : Property(state, BindingHashKey);
        var nonce = http.Request.Cookies[BindingCookieName(requestId)];
        return storedHash != null
            && !string.IsNullOrEmpty(nonce)
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(nonce)), Encoding.ASCII.GetBytes(storedHash));
    }

    private async Task DropRequestAsync(HttpContext http, string requestId, CancellationToken cancellationToken)
    {
        await _protocolState.RemoveAsync(requestId, cancellationToken);
        await _cache.RemoveAsync(AuthenticationKey(requestId), cancellationToken);
        http.Response.Cookies.Delete(BindingCookieName(requestId), BindingCookieOptions(null));
    }

    private void CopyAuthorizeParameters(ProtocolState? original, IDictionary<string, string> properties)
    {
        if (original == null || original.ProtocolName != "oidc" || string.IsNullOrEmpty(original.SerializedRequest)) return;

        OidcAuthorizeRequest? authorize;
        try
        {
            authorize = JsonSerializer.Deserialize<OidcAuthorizeRequest>(original.SerializedRequest);
        }
        catch (JsonException)
        {
            return;
        }
        if (authorize == null) return;

        if (!string.IsNullOrEmpty(authorize.Prompt)) properties[PromptKey] = authorize.Prompt;
        if (!string.IsNullOrEmpty(authorize.MaxAge)) properties[MaxAgeKey] = authorize.MaxAge;
        if (!string.IsNullOrEmpty(authorize.LoginHint)) properties[LoginHintKey] = authorize.LoginHint;
        if (!string.IsNullOrEmpty(authorize.UiLocales)) properties[UiLocalesKey] = authorize.UiLocales;

        var replayable = authorize.Raw?
            .Where(p => !NonReplayableAuthorizeParameters.Contains(p.Key, StringComparer.Ordinal))
            .ToDictionary(p => p.Key, p => (string?)p.Value);
        if (replayable is { Count: > 0 } && replayable.ContainsKey("client_id"))
        {
            properties[RestartUrlKey] = QueryHelpers.AddQueryString(_oidcEndpoints.AuthorizeEndpoint, replayable);
        }
    }

    private string PlatformIssuer()
    {
        var platformIssuer = _configuration["Oluso:IssuerUri"];
        if (string.IsNullOrEmpty(platformIssuer))
        {
            throw new InvalidOperationException("Oluso:IssuerUri is not configured");
        }
        return platformIssuer.TrimEnd('/');
    }

    private static CookieOptions BindingCookieOptions(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = OrganizationSignInDefaults.BindingCookiePath,
        MaxAge = maxAge,
        IsEssential = true,
    };

    /// <summary>
    /// Relative form of <paramref name="url"/> when it is a local path or an absolute
    /// URL on the request's own origin; null for anything that could leave the host.
    /// </summary>
    public static string? ToLocalUrl(HttpRequest request, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (url.StartsWith('/'))
        {
            return IsLocalPath(url) ? url : null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            || (absolute.Scheme != Uri.UriSchemeHttps && absolute.Scheme != Uri.UriSchemeHttp)
            || !IsSameOrigin(request, absolute.GetLeftPart(UriPartial.Authority)))
        {
            return null;
        }

        var local = absolute.PathAndQuery;
        return IsLocalPath(local) ? local : null;
    }

    public static bool IsLocalPath(string url) =>
        url.Length > 0
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);

    public static bool IsSameOrigin(HttpRequest request, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var expected)) return false;
        if (!string.Equals(expected.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)) return false;
        var requestPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return expected.Port == requestPort;
    }

    public static string BindingCookieName(string requestId) =>
        OrganizationSignInDefaults.BindingCookiePrefix + Hash(requestId)[..16].ToLowerInvariant();

    private static string? ExtractCorrelationId(string url)
    {
        var queryStart = url.IndexOf('?');
        if (queryStart < 0) return null;
        var query = QueryHelpers.ParseQuery(url[queryStart..]);
        return query.TryGetValue("correlation_id", out var value) ? value.ToString() : null;
    }

    private sealed record Resume(string ReturnUrl, string? JourneyId, ProtocolState Original);

    /// <summary>
    /// Where the platform resumes once the user is back: the authorize callback for a
    /// standalone login, the journey itself for a journey login. Either way the
    /// original protocol state must still be live, since resuming needs it.
    /// </summary>
    private async Task<Resume?> ResolveResumeAsync(HttpContext http, string? returnUrl, string? journeyId, CancellationToken cancellationToken)
    {
        var hasReturnUrl = !string.IsNullOrWhiteSpace(returnUrl);
        var hasJourney = !string.IsNullOrWhiteSpace(journeyId);
        if (hasReturnUrl == hasJourney) return null;

        string? callback;
        string resumeUrl;
        if (hasJourney)
        {
            var journey = await _journeyStates.GetAsync(journeyId!, cancellationToken);
            if (journey == null
                || journey.Status != JourneyStatus.InProgress
                || (journey.ExpiresAt.HasValue && journey.ExpiresAt.Value <= _clock.GetUtcNow().UtcDateTime))
            {
                return null;
            }
            callback = ToLocalUrl(http.Request, journey.CallbackUrl);
            resumeUrl = $"/journey/{Uri.EscapeDataString(journey.Id)}";
        }
        else
        {
            callback = ToLocalUrl(http.Request, returnUrl);
            resumeUrl = callback ?? string.Empty;
        }

        var correlationId = callback == null ? null : ExtractCorrelationId(callback);
        if (string.IsNullOrEmpty(correlationId)) return null;

        var original = await _protocolState.GetAsync(correlationId, cancellationToken);
        if (original == null || original.ProtocolName == OrganizationSignInDefaults.ProtocolName) return null;

        return new Resume(resumeUrl, hasJourney ? journeyId : null, original);
    }

    private static string JourneyResultKey(string journeyId) => $"oluso:org-signin:journey:{Hash(journeyId)}";

    private static string AuthenticationKey(string requestId) => $"oluso:org-signin:auth:{Hash(requestId)}";

    private static string FailureKey(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"oluso:org-signin:fail:{Hash(address)[..32]}";
    }

    private async Task<int> FailedLookupsAsync(string key, CancellationToken cancellationToken)
    {
        var value = await _cache.GetStringAsync(key, cancellationToken);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private async Task RecordFailedLookupAsync(string key, CancellationToken cancellationToken)
    {
        var failures = await FailedLookupsAsync(key, cancellationToken) + 1;
        await _cache.SetStringAsync(key, failures.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = OrganizationSignInDefaults.FailedLookupWindow },
            cancellationToken);
    }

    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task RaiseAsync(
        HttpContext http,
        string stage,
        string? tenantId,
        bool succeeded,
        string? error,
        string? subjectId = null,
        string? clientId = null,
        string? identityProvider = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _events.RaiseAsync(new OrganizationSignInEvent
            {
                TenantId = tenantId,
                Stage = stage,
                Succeeded = succeeded,
                Error = error,
                SubjectId = subjectId,
                ClientId = clientId,
                IdentityProvider = identityProvider,
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record organisation sign-in {Stage} event", stage);
        }
    }
}

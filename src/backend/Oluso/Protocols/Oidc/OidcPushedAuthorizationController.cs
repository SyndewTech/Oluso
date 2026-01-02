using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Pushed Authorization Requests (PAR) Endpoint - RFC 9126.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcPushedAuthorizationController : ControllerBase
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IAuthorizeRequestValidator _authorizeValidator;
    private readonly IPushedAuthorizationStore _parStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OidcPushedAuthorizationController> _logger;

    private const int DefaultRequestUriLifetime = 60; // seconds

    public OidcPushedAuthorizationController(
        IClientAuthenticator clientAuthenticator,
        IAuthorizeRequestValidator authorizeValidator,
        IPushedAuthorizationStore parStore,
        IConfiguration configuration,
        ILogger<OidcPushedAuthorizationController> logger)
    {
        _clientAuthenticator = clientAuthenticator;
        _authorizeValidator = authorizeValidator;
        _parStore = parStore;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Push Authorization Request (form-urlencoded)
    /// </summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> PushAuthorizationForm(CancellationToken cancellationToken)
    {
        // Authenticate client (from form or header)
        var clientAuth = await _clientAuthenticator.AuthenticateAsync(Request, cancellationToken);
        if (!clientAuth.IsValid)
        {
            return Unauthorized(new TokenErrorResponse
            {
                Error = clientAuth.Error!,
                ErrorDescription = clientAuth.ErrorDescription
            });
        }

        var client = clientAuth.Client!;
        var form = await Request.ReadFormAsync(cancellationToken);

        // Build authorize request from form
        var request = new AuthorizeRequest
        {
            ClientId = client.ClientId,
            RedirectUri = form["redirect_uri"].FirstOrDefault(),
            ResponseType = form["response_type"].FirstOrDefault(),
            Scope = form["scope"].FirstOrDefault(),
            State = form["state"].FirstOrDefault(),
            Nonce = form["nonce"].FirstOrDefault(),
            CodeChallenge = form["code_challenge"].FirstOrDefault(),
            CodeChallengeMethod = form["code_challenge_method"].FirstOrDefault(),
            ResponseMode = form["response_mode"].FirstOrDefault(),
            Prompt = form["prompt"].FirstOrDefault(),
            MaxAge = form["max_age"].FirstOrDefault(),
            IdTokenHint = form["id_token_hint"].FirstOrDefault(),
            LoginHint = form["login_hint"].FirstOrDefault(),
            AcrValues = form["acr_values"].FirstOrDefault(),
            UiLocales = form["ui_locales"].FirstOrDefault(),
            Request = form["request"].FirstOrDefault(),
            RequestUri = form["request_uri"].FirstOrDefault()
        };

        return await ProcessPushedAuthorizationAsync(client, request, cancellationToken);
    }

    /// <summary>
    /// Push Authorization Request (JSON body)
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> PushAuthorizationJson(
        [FromBody] ParRequestDto body,
        CancellationToken cancellationToken)
    {
        // For JSON, client authentication can be in body or header
        var clientAuth = await _clientAuthenticator.AuthenticateAsync(Request, cancellationToken);

        // If header auth failed and we have credentials in body, try those
        if (!clientAuth.IsValid && !string.IsNullOrEmpty(body.ClientId))
        {
            clientAuth = await _clientAuthenticator.AuthenticateAsync(
                body.ClientId,
                body.ClientSecret,
                cancellationToken);
        }

        if (!clientAuth.IsValid)
        {
            return Unauthorized(new TokenErrorResponse
            {
                Error = clientAuth.Error ?? OidcConstants.Errors.InvalidClient,
                ErrorDescription = clientAuth.ErrorDescription ?? "Client authentication failed"
            });
        }

        var client = clientAuth.Client!;

        // Build authorize request from JSON body
        var request = body.ToAuthorizeRequest();
        request.ClientId = client.ClientId; // Override with authenticated client

        return await ProcessPushedAuthorizationAsync(client, request, cancellationToken);
    }

    private async Task<IActionResult> ProcessPushedAuthorizationAsync(
        Client client,
        AuthorizeRequest request,
        CancellationToken cancellationToken)
    {
        // request_uri is not allowed in PAR request itself (would be recursive)
        if (!string.IsNullOrEmpty(request.RequestUri))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.InvalidRequest,
                ErrorDescription = "request_uri parameter is not allowed in PAR request"
            });
        }

        // Validate the request (except for user authentication which happens at authorize endpoint)
        var validationResult = await _authorizeValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = validationResult.Error!,
                ErrorDescription = validationResult.ErrorDescription
            });
        }

        // Generate request_uri with lifetime from config or default
        var lifetime = _configuration.GetValue<int>("Oluso:ParRequestUriLifetime",
            _configuration.GetValue<int>("IdentityServer:ParRequestUriLifetime", DefaultRequestUriLifetime));
        var requestUri = GenerateRequestUri();
        var referenceHash = ComputeHash(requestUri);

        // Store the request
        var storedRequest = new PushedAuthorizationRequest
        {
            RequestUri = requestUri,
            ReferenceValueHash = referenceHash,
            ClientId = client.ClientId,
            Parameters = JsonSerializer.Serialize(request),
            CreationTime = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(lifetime)
        };

        await _parStore.StoreAsync(storedRequest, cancellationToken);

        _logger.LogInformation(
            "PAR stored for client {ClientId}, request_uri {RequestUri}, expires in {Lifetime}s",
            client.ClientId, requestUri, lifetime);

        return Ok(new ParResponse
        {
            RequestUri = requestUri,
            ExpiresIn = lifetime
        });
    }

    private static string GenerateRequestUri()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var reference = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return $"urn:ietf:params:oauth:request_uri:{reference}";
    }

    private static string ComputeHash(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

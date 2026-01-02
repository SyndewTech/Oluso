using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Protocols.Grants;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Device Authorization Endpoint (RFC 8628).
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcDeviceAuthorizationController : ControllerBase
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IDeviceFlowStore _deviceFlowStore;
    private readonly IScopeValidator _scopeValidator;
    private readonly OidcEndpointConfiguration _endpointConfig;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OidcDeviceAuthorizationController> _logger;

    private const int DeviceCodeLength = 32;
    private const int UserCodeLength = 8;
    private static readonly string UserCodeCharacters = "BCDFGHJKLMNPQRSTVWXZ"; // Easy to type, no vowels

    public OidcDeviceAuthorizationController(
        IClientAuthenticator clientAuthenticator,
        IDeviceFlowStore deviceFlowStore,
        IScopeValidator scopeValidator,
        IOptions<OidcEndpointConfiguration> endpointConfig,
        IConfiguration configuration,
        ILogger<OidcDeviceAuthorizationController> logger)
    {
        _clientAuthenticator = clientAuthenticator;
        _deviceFlowStore = deviceFlowStore;
        _scopeValidator = scopeValidator;
        _endpointConfig = endpointConfig.Value;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        // Authenticate client
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

        // Check if client is allowed to use device flow
        if (!client.AllowedGrantTypes.Any(g => g.GrantType == OidcConstants.GrantTypes.DeviceCode))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.UnauthorizedClient,
                ErrorDescription = "Client is not authorized for device flow"
            });
        }

        var form = await Request.ReadFormAsync(cancellationToken);

        // Validate scope
        var scope = form["scope"].FirstOrDefault();
        var requestedScopes = _scopeValidator.ParseScopes(scope).ToList();
        var allowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList();

        var scopeValidation = await _scopeValidator.ValidateAsync(requestedScopes, allowedScopes, cancellationToken);
        if (!scopeValidation.IsValid)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = scopeValidation.Error!,
                ErrorDescription = scopeValidation.ErrorDescription
            });
        }

        // Generate codes
        var deviceCode = GenerateDeviceCode();
        var userCode = GenerateUserCode();

        // Store device code
        var lifetime = client.DeviceCodeLifetime > 0 ? client.DeviceCodeLifetime : 300;
        var data = JsonSerializer.Serialize(new DeviceCodeData
        {
            Status = DeviceCodeStatus.Pending,
            Scopes = scopeValidation.ValidScopes.ToList()
        });

        var deviceFlowCode = new DeviceFlowCode
        {
            DeviceCode = deviceCode,
            UserCode = userCode,
            ClientId = client.ClientId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddSeconds(lifetime),
            Data = data
        };

        await _deviceFlowStore.StoreDeviceAuthorizationAsync(deviceCode, userCode, deviceFlowCode, cancellationToken);

        _logger.LogInformation("Device authorization started for client {ClientId}, user code {UserCode}",
            client.ClientId, userCode);

        // Build verification URI
        var baseUri = _configuration["Oluso:IssuerUri"]
            ?? _configuration["IdentityServer:IssuerUri"]
            ?? $"{Request.Scheme}://{Request.Host}";
        var verificationUri = $"{baseUri}/device";
        var verificationUriComplete = $"{verificationUri}?user_code={userCode}";

        return Ok(new DeviceAuthorizationResponse
        {
            DeviceCode = deviceCode,
            UserCode = FormatUserCode(userCode),
            VerificationUri = verificationUri,
            VerificationUriComplete = verificationUriComplete,
            ExpiresIn = lifetime,
            Interval = 5
        });
    }

    private static string GenerateDeviceCode()
    {
        var bytes = new byte[DeviceCodeLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string GenerateUserCode()
    {
        var bytes = new byte[UserCodeLength];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[UserCodeLength];
        for (var i = 0; i < UserCodeLength; i++)
        {
            chars[i] = UserCodeCharacters[bytes[i] % UserCodeCharacters.Length];
        }

        return new string(chars);
    }

    private static string FormatUserCode(string userCode)
    {
        // Format as XXXX-XXXX for display
        if (userCode.Length == 8)
        {
            return $"{userCode.Substring(0, 4)}-{userCode.Substring(4, 4)}";
        }
        return userCode;
    }
}

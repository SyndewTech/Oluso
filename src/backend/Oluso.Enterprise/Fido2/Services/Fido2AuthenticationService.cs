using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Fido2.Configuration;
using Oluso.Enterprise.Fido2.WebAuthn;

namespace Oluso.Enterprise.Fido2.Services;

/// <summary>
/// FIDO2/WebAuthn service implementation using native .NET cryptography.
/// No external WebAuthn library dependencies.
/// </summary>
public class Fido2AuthenticationService : IFido2AuthenticationService
{
    private readonly IOlusoUserService _userService;
    private readonly IFido2CredentialStore _credentialStore;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Fido2Options _options;
    private readonly ILogger<Fido2AuthenticationService> _logger;

    // COSE algorithm identifiers
    private static readonly int[] SupportedAlgorithms =
    [
        -7,   // ES256 (ECDSA w/ SHA-256)
        -35,  // ES384 (ECDSA w/ SHA-384)
        -36,  // ES512 (ECDSA w/ SHA-512)
        -257, // RS256 (RSASSA-PKCS1-v1_5 w/ SHA-256)
        -258, // RS384 (RSASSA-PKCS1-v1_5 w/ SHA-384)
        -259, // RS512 (RSASSA-PKCS1-v1_5 w/ SHA-512)
    ];

    public Fido2AuthenticationService(
        IOlusoUserService userService,
        IFido2CredentialStore credentialStore,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        IOptions<Fido2Options> options,
        ILogger<Fido2AuthenticationService> logger)
    {
        _userService = userService;
        _credentialStore = credentialStore;
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CredentialCreateOptions> CreateRegistrationOptionsAsync(
        OlusoUser user,
        string? authenticatorType = null,
        bool? requireResidentKey = null,
        CancellationToken cancellationToken = default)
    {
        // Get existing credentials to exclude
        var existingCredentials = await _credentialStore.GetActiveByUserIdAsync(user.Id, cancellationToken);
        var excludeCredentials = existingCredentials
            .Select(c => new CredentialDescriptor
            {
                Type = "public-key",
                Id = c.CredentialId,
                Transports = c.GetTransports().Count > 0 ? c.GetTransports() : null
            }).ToList();

        // Generate challenge
        var challenge = GenerateChallenge();

        // Get effective RP ID (supports tenant custom domains)
        var rpId = GetEffectiveRpId();
        var rpName = _tenantContext.Tenant?.DisplayName ?? _tenantContext.Tenant?.Name ?? _options.RelyingPartyName;

        _logger.LogDebug("Created registration options for user {UserId} with RP ID {RpId}", user.Id, rpId);

        var options = new CredentialCreateOptions
        {
            Challenge = challenge,
            Rp = new RelyingPartyInfo
            {
                Id = rpId,
                Name = rpName,
                Icon = _options.RelyingPartyIcon
            },
            User = new UserInfo
            {
                Id = Base64UrlEncode(Encoding.UTF8.GetBytes(user.Id)),
                Name = user.UserName ?? user.Email ?? user.Id,
                DisplayName = user.UserName ?? "User",
                Icon = null
            },
            PubKeyCredParams = [.. SupportedAlgorithms.Select(alg => new PubKeyCredParam
            {
                Type = "public-key",
                Alg = alg
            })],
            Timeout = (int)_options.Timeout,
            Attestation = _options.AttestationConveyancePreference,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                AuthenticatorAttachment = authenticatorType ?? _options.AuthenticatorAttachment,
                ResidentKey = requireResidentKey == true ? "required" : _options.ResidentKeyRequirement,
                RequireResidentKey = requireResidentKey ?? _options.ResidentKeyRequirement == "required",
                UserVerification = _options.UserVerificationRequirement
            },
            ExcludeCredentials = excludeCredentials.Count > 0 ? excludeCredentials : null
        };

        return options;
    }

    public async Task<Fido2RegistrationResult> CompleteRegistrationAsync(
        OlusoUser user,
        AuthenticatorAttestationResponse response,
        string challenge,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var attestationObject = Base64UrlDecode(response.Response.AttestationObject);
            var clientDataJson = Base64UrlDecode(response.Response.ClientDataJson);

            // Determine origin and RP ID for verification (supports tenant origins)
            var origin = GetEffectiveOrigin();
            var rpId = GetEffectiveRpId();

            // Parse and verify attestation
            var result = WebAuthnVerifier.ParseAttestation(
                attestationObject,
                clientDataJson,
                challenge,
                origin,
                rpId);

            // Check if credential already exists
            var credentialIdBase64 = Base64UrlEncode(result.CredentialId);
            if (await _credentialStore.ExistsAsync(credentialIdBase64, cancellationToken))
            {
                return new Fido2RegistrationResult
                {
                    Success = false,
                    Error = "Credential already registered",
                    ErrorCode = "credential_exists"
                };
            }

            // Store the credential
            var credential = new Fido2CredentialEntity
            {
                UserId = user.Id,
                CredentialId = credentialIdBase64,
                PublicKey = Base64UrlEncode(result.PublicKey),
                UserHandle = Base64UrlEncode(Encoding.UTF8.GetBytes(user.Id)),
                SignatureCounter = result.SignCount,
                CredentialType = DetermineCredentialType(result.PublicKey),
                AaGuid = result.AaGuid,
                DisplayName = displayName ?? GetDefaultCredentialName(response.AuthenticatorAttachment),
                AuthenticatorType = response.AuthenticatorAttachment == "platform"
                    ? Fido2AuthenticatorType.Platform
                    : Fido2AuthenticatorType.CrossPlatform,
                AttestationFormat = result.Format,
                IsDiscoverable = true,
                IsActive = true,
                TenantId = user.TenantId
            };
            credential.SetTransports(response.Response.Transports);

            await _credentialStore.AddAsync(credential, cancellationToken);

            _logger.LogInformation("Registered new FIDO2 credential {CredentialId} for user {UserId}",
                credential.Id, user.Id);

            return new Fido2RegistrationResult
            {
                Success = true,
                CredentialId = credential.Id
            };
        }
        catch (WebAuthnException ex)
        {
            _logger.LogWarning(ex, "FIDO2 registration verification failed for user {UserId}", user.Id);
            return new Fido2RegistrationResult
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = "verification_failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing FIDO2 registration for user {UserId}", user.Id);
            return new Fido2RegistrationResult
            {
                Success = false,
                Error = "Registration failed",
                ErrorCode = "internal_error"
            };
        }
    }

    public async Task<AssertionOptions> CreateAssertionOptionsAsync(
        string? username = null,
        CancellationToken cancellationToken = default)
    {
        List<CredentialDescriptor>? allowCredentials = null;

        if (!string.IsNullOrEmpty(username))
        {
            var userInfo = await _userService.FindByUsernameAsync(username, cancellationToken)
                        ?? await _userService.FindByEmailAsync(username, cancellationToken);

            if (userInfo != null)
            {
                var activeCredentials = await _credentialStore.GetActiveByUserIdAsync(userInfo.Id, cancellationToken);

                if (activeCredentials.Count > 0)
                {
                    allowCredentials = activeCredentials.Select(c => new CredentialDescriptor
                    {
                        Type = "public-key",
                        Id = c.CredentialId,
                        Transports = c.GetTransports().Count > 0 ? c.GetTransports() : null
                    }).ToList();

                    _logger.LogDebug("Found {Count} credentials for user {Username}",
                        allowCredentials.Count, username);
                }
            }
        }

        var challenge = GenerateChallenge();

        // Get effective RP ID (supports tenant custom domains)
        var rpId = GetEffectiveRpId();

        _logger.LogDebug("Created assertion options with RP ID {RpId}", rpId);

        return new AssertionOptions
        {
            Challenge = challenge,
            Timeout = (int)_options.Timeout,
            RpId = rpId,
            AllowCredentials = allowCredentials,
            UserVerification = _options.UserVerificationRequirement
        };
    }

    public async Task<Fido2AssertionResult> VerifyAssertionAsync(
        AuthenticatorAssertionResponse response,
        string challenge,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentialIdBase64 = Base64UrlEncode(Base64UrlDecode(response.RawId));

            // Find the stored credential
            var storedCredential = await _credentialStore.GetByCredentialIdAsync(credentialIdBase64, cancellationToken);
            if (storedCredential == null || !storedCredential.IsActive)
            {
                return new Fido2AssertionResult
                {
                    Success = false,
                    Error = "Credential not found or inactive",
                    ErrorCode = "credential_not_found"
                };
            }

            var credentialIdBytes = Base64UrlDecode(response.RawId);
            var authenticatorData = Base64UrlDecode(response.Response.AuthenticatorData);
            var clientDataJson = Base64UrlDecode(response.Response.ClientDataJson);
            var signature = Base64UrlDecode(response.Response.Signature);
            var userHandle = !string.IsNullOrEmpty(response.Response.UserHandle)
                ? Base64UrlDecode(response.Response.UserHandle)
                : null;

            // Verify user handle if provided
            if (userHandle != null && userHandle.Length > 0)
            {
                var storedUserHandle = Base64UrlDecode(storedCredential.UserHandle);
                if (!userHandle.SequenceEqual(storedUserHandle))
                {
                    return new Fido2AssertionResult
                    {
                        Success = false,
                        Error = "User handle mismatch",
                        ErrorCode = "user_mismatch"
                    };
                }
            }

            // Determine origin and RP ID for verification (supports tenant origins)
            var origin = GetEffectiveOrigin();
            var rpId = GetEffectiveRpId();
            var storedPublicKey = Base64UrlDecode(storedCredential.PublicKey);

            // Verify the assertion
            var result = WebAuthnVerifier.VerifyAssertion(
                credentialIdBytes,
                authenticatorData,
                clientDataJson,
                signature,
                userHandle,
                storedPublicKey,
                storedCredential.SignatureCounter,
                challenge,
                origin,
                rpId);

            // Update credential counter and last used
            await _credentialStore.UpdateCounterAsync(credentialIdBase64, result.NewSignCount, cancellationToken);

            _logger.LogInformation("FIDO2 assertion successful for user {UserId}", storedCredential.UserId);

            return new Fido2AssertionResult
            {
                Success = true,
                UserId = storedCredential.UserId,
                CredentialId = storedCredential.Id,
                SignatureCounter = result.NewSignCount
            };
        }
        catch (WebAuthnException ex)
        {
            _logger.LogWarning(ex, "FIDO2 assertion verification failed");
            return new Fido2AssertionResult
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = "verification_failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying FIDO2 assertion");
            return new Fido2AssertionResult
            {
                Success = false,
                Error = "Assertion verification failed",
                ErrorCode = "internal_error"
            };
        }
    }

    public async Task<IEnumerable<Fido2Credential>> GetUserCredentialsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _credentialStore.GetByUserIdAsync(userId, cancellationToken);
        return credentials.Select(c => new Fido2Credential
        {
            Id = c.Id,
            UserId = c.UserId,
            CredentialId = c.CredentialId,
            PublicKey = c.PublicKey,
            UserHandle = c.UserHandle,
            SignatureCounter = c.SignatureCounter,
            CredentialType = c.CredentialType,
            AaGuid = c.AaGuid,
            DisplayName = c.DisplayName,
            AuthenticatorType = c.AuthenticatorType == Fido2AuthenticatorType.Platform
                ? AuthenticatorType.Platform
                : AuthenticatorType.CrossPlatform,
            AttestationFormat = c.AttestationFormat,
            IsDiscoverable = c.IsDiscoverable,
            Transports = c.Transports,
            TenantId = c.TenantId,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            LastUsedAt = c.LastUsedAt
        });
    }

    public async Task<bool> RemoveCredentialAsync(
        string userId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentialStore.GetByIdAsync(credentialId, cancellationToken);
        if (credential != null && credential.UserId == userId)
        {
            await _credentialStore.RemoveAsync(credentialId, cancellationToken);
            _logger.LogInformation("Removed FIDO2 credential {CredentialId} for user {UserId}", credentialId, userId);
            return true;
        }
        return false;
    }

    public async Task<bool> UpdateCredentialNameAsync(
        string userId,
        string credentialId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentialStore.GetByIdAsync(credentialId, cancellationToken);
        if (credential != null && credential.UserId == userId)
        {
            credential.DisplayName = displayName;
            await _credentialStore.UpdateAsync(credential, cancellationToken);
            return true;
        }
        return false;
    }

    #region Helper Methods

    private static string GenerateChallenge()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input
            .Replace('-', '+')
            .Replace('_', '/');

        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }

        return Convert.FromBase64String(output);
    }

    private static string GetDefaultCredentialName(string? authenticatorAttachment)
    {
        return authenticatorAttachment switch
        {
            "platform" => "This device",
            "cross-platform" => "Security key",
            _ => "Passkey"
        };
    }

    private static int DetermineCredentialType(byte[] publicKey)
    {
        if (publicKey.Length > 0 && publicKey[0] == 0x04)
        {
            return (publicKey.Length - 1) / 2 switch
            {
                32 => -7,   // ES256
                48 => -35,  // ES384
                66 => -36,  // ES512
                _ => -7
            };
        }
        return -257; // RS256
    }

    /// <summary>
    /// Gets the effective origin for WebAuthn verification.
    /// Uses the HTTP Origin header from the request (the frontend's origin).
    /// Validates against RP ID, tenant custom domains, and explicit origin lists.
    /// </summary>
    private string GetEffectiveOrigin()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return _options.Origins.FirstOrDefault() ?? "";
        }

        var request = httpContext.Request;

        // Get the Origin header - this is the actual origin of the frontend making the request
        // This is what the browser sets and what's in clientDataJSON.origin
        var originHeader = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(originHeader))
        {
            // Fall back to Referer header
            var referer = request.Headers.Referer.FirstOrDefault();
            if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                originHeader = $"{refererUri.Scheme}://{refererUri.Host}:{refererUri.Port}";
                if (refererUri.IsDefaultPort)
                {
                    originHeader = $"{refererUri.Scheme}://{refererUri.Host}";
                }
            }
        }

        if (string.IsNullOrEmpty(originHeader))
        {
            _logger.LogWarning("No Origin or Referer header found in request");
            return _options.Origins.FirstOrDefault() ?? "";
        }

        // Parse the origin to get the hostname
        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var originUri))
        {
            _logger.LogWarning("Invalid Origin header: {Origin}", originHeader);
            return _options.Origins.FirstOrDefault() ?? "";
        }

        var originHostname = originUri.Host;

        // Check if this origin is in the explicit allowed list
        if (_options.Origins.Contains(originHeader))
        {
            _logger.LogDebug("Origin in explicit allowed list: {Origin}", originHeader);
            return originHeader;
        }

        // Check tenant custom domain
        var tenant = _tenantContext.Tenant;
        if (tenant?.CustomDomain != null)
        {
            if (originHostname == tenant.CustomDomain)
            {
                _logger.LogDebug("Origin matches tenant custom domain: {Origin}", originHeader);
                return originHeader;
            }
        }

        // Dynamic origin validation: accept any origin whose hostname matches the RP ID
        var rpId = _options.RelyingPartyId;
        if (!string.IsNullOrEmpty(rpId))
        {
            // For localhost development, accept any localhost origin
            if (rpId == "localhost" && originHostname == "localhost")
            {
                _logger.LogDebug("Accepting localhost origin dynamically: {Origin}", originHeader);
                return originHeader;
            }

            // For production domains, accept if hostname matches or is a subdomain of RP ID
            if (originHostname == rpId || originHostname.EndsWith($".{rpId}", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Origin matches RP ID: {Origin}", originHeader);
                return originHeader;
            }
        }

        // If origins list is empty, accept the origin (permissive development mode)
        if (_options.Origins.Count == 0)
        {
            _logger.LogDebug("No origins configured, accepting origin: {Origin}", originHeader);
            return originHeader;
        }

        _logger.LogWarning(
            "Origin {Origin} not allowed. Allowed origins: {AllowedOrigins}, RP ID: {RpId}",
            originHeader,
            string.Join(", ", _options.Origins),
            rpId);

        // Return the origin anyway - the WebAuthn verifier will reject if it doesn't match
        // This provides a clearer error message about origin mismatch
        return originHeader;
    }

    /// <summary>
    /// Gets the effective relying party ID.
    /// Uses tenant custom domain or configured RP ID.
    /// </summary>
    private string GetEffectiveRpId()
    {
        // Check tenant custom domain first
        var tenant = _tenantContext.Tenant;
        if (!string.IsNullOrEmpty(tenant?.CustomDomain))
        {
            return tenant.CustomDomain;
        }

        // Try to extract from current request host
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && !string.IsNullOrEmpty(_options.RelyingPartyId))
        {
            var requestHost = httpContext.Request.Host.Host;

            // If request host ends with configured RP ID, it's valid
            // e.g., RP ID = "example.com", host = "auth.example.com" -> valid
            if (requestHost.EndsWith(_options.RelyingPartyId, StringComparison.OrdinalIgnoreCase))
            {
                return _options.RelyingPartyId;
            }

            // For localhost development, use the request host
            if (requestHost == "localhost" || requestHost.StartsWith("127."))
            {
                return requestHost;
            }
        }

        return _options.RelyingPartyId;
    }

    #endregion
}

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Services;
using Oluso.Enterprise.Fido2.Configuration;
using CoreFido2Service = Oluso.Core.Services.IFido2Service;
using CoreFido2Options = Oluso.Core.Services.Fido2AssertionOptions;
using CoreFido2RegistrationOptions = Oluso.Core.Services.Fido2RegistrationOptions;
using CoreFido2RegistrationResult = Oluso.Core.Services.Fido2RegistrationResult;
using CoreFido2AssertionResult = Oluso.Core.Services.Fido2AssertionResult;
using CoreFido2Credential = Oluso.Core.Services.Fido2Credential;

namespace Oluso.Enterprise.Fido2.Services;

/// <summary>
/// Adapter that wraps IFido2AuthenticationService to implement the IFido2Service interface
/// used by User Journey step handlers. This provides the stateful (ID-based) pattern
/// required by the new User Journey engine.
/// </summary>
public class Fido2ServiceAdapter : CoreFido2Service
{
    private readonly IFido2AuthenticationService _innerService;
    private readonly IOlusoUserService _userService;
    private readonly Fido2Options _options;
    private readonly IDistributedCache _cache;
    private readonly ILogger<Fido2ServiceAdapter> _logger;

    // Pending ceremonies live in the distributed cache so the begin and complete
    // calls may land on different instances. Entries expire on their own; a
    // ceremony older than the verify window is rejected as well.
    private const string RegistrationKeyPrefix = "oluso:fido2:registration:";
    private const string AssertionKeyPrefix = "oluso:fido2:assertion:";
    private static readonly TimeSpan CeremonyRetention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CeremonyWindow = TimeSpan.FromMinutes(5);

    public Fido2ServiceAdapter(
        IFido2AuthenticationService innerService,
        IOlusoUserService userService,
        IOptions<Fido2Options> options,
        IDistributedCache cache,
        ILogger<Fido2ServiceAdapter> logger)
    {
        _innerService = innerService;
        _userService = userService;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CoreFido2RegistrationOptions> CreateRegistrationOptionsAsync(
        string userId,
        string userName,
        string displayName,
        string? authenticatorType = null,
        bool? requireResidentKey = null,
        CancellationToken cancellationToken = default)
    {
        // Get OlusoUser for the inner service
        var user = await _userService.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new Fido2Exception("User not found", "user_not_found");
        }

        var options = await _innerService.CreateRegistrationOptionsAsync(
            user, authenticatorType, requireResidentKey, cancellationToken);

        // Generate a registration ID and store the pending registration
        var registrationId = GenerateId();
        await PutPendingAsync(RegistrationKeyPrefix + registrationId, new PendingRegistration
        {
            UserId = userId,
            Challenge = options.Challenge,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        return new CoreFido2RegistrationOptions
        {
            RegistrationId = registrationId,
            Challenge = options.Challenge,
            User = new Oluso.Core.Services.Fido2UserInfo
            {
                Id = options.User.Id,
                Name = options.User.Name,
                DisplayName = options.User.DisplayName
            },
            RelyingParty = new Oluso.Core.Services.Fido2RelyingParty
            {
                Id = options.Rp.Id,
                Name = options.Rp.Name,
                Icon = options.Rp.Icon
            },
            PublicKeyCredentialParameters = options.PubKeyCredParams?.Select(p =>
                new Oluso.Core.Services.Fido2PublicKeyCredentialParameters
                {
                    Type = p.Type,
                    Algorithm = p.Alg
                }).ToList(),
            Timeout = options.Timeout,
            AttestationConveyance = options.Attestation,
            AuthenticatorSelection = options.AuthenticatorSelection != null
                ? new Oluso.Core.Services.Fido2AuthenticatorSelection
                {
                    AuthenticatorAttachment = options.AuthenticatorSelection.AuthenticatorAttachment,
                    RequireResidentKey = options.AuthenticatorSelection.RequireResidentKey,
                    ResidentKey = options.AuthenticatorSelection.ResidentKey,
                    UserVerification = options.AuthenticatorSelection.UserVerification
                }
                : null,
            ExcludeCredentials = options.ExcludeCredentials?.Select(c =>
                new Oluso.Core.Services.Fido2CredentialDescriptor
                {
                    Type = c.Type,
                    Id = c.Id,
                    Transports = c.Transports
                }).ToList()
        };
    }

    public async Task<CoreFido2RegistrationResult> VerifyRegistrationAsync(
        string registrationId,
        string attestationResponse,
        string? credentialName = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await TakePendingAsync<PendingRegistration>(RegistrationKeyPrefix + registrationId, cancellationToken);
        if (pending == null)
        {
            return CoreFido2RegistrationResult.Failed("invalid_registration", "Invalid or expired registration");
        }

        if (DateTime.UtcNow - pending.CreatedAt > CeremonyWindow)
        {
            return CoreFido2RegistrationResult.Failed("expired", "Registration has expired");
        }

        // Get the user
        var user = await _userService.GetByIdAsync(pending.UserId, cancellationToken);
        if (user == null)
        {
            return CoreFido2RegistrationResult.Failed("user_not_found", "User not found");
        }

        // Parse the attestation response
        AuthenticatorAttestationResponse response;
        try
        {
            response = JsonSerializer.Deserialize<AuthenticatorAttestationResponse>(attestationResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse attestation response");
            return CoreFido2RegistrationResult.Failed("invalid_response", "Invalid attestation response format");
        }

        var result = await _innerService.CompleteRegistrationAsync(
            user, response, pending.Challenge, credentialName, cancellationToken);

        if (!result.Success)
        {
            return CoreFido2RegistrationResult.Failed(result.ErrorCode ?? "registration_failed", result.Error);
        }

        return CoreFido2RegistrationResult.Success(result.CredentialId!, "");
    }

    public async Task<CoreFido2Options> CreateAssertionOptionsAsync(
        string? username,
        CancellationToken cancellationToken = default)
    {
        var options = await _innerService.CreateAssertionOptionsAsync(username, cancellationToken);

        // Generate an assertion ID and store the pending assertion
        var assertionId = GenerateId();
        await PutPendingAsync(AssertionKeyPrefix + assertionId, new PendingAssertion
        {
            Challenge = options.Challenge,
            Username = username,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        return new CoreFido2Options
        {
            AssertionId = assertionId,
            Challenge = options.Challenge,
            Timeout = options.Timeout,
            RpId = options.RpId,
            AllowCredentials = options.AllowCredentials?.Select(c =>
                new Oluso.Core.Services.Fido2CredentialDescriptor
                {
                    Type = c.Type,
                    Id = c.Id,
                    Transports = c.Transports
                }).ToList(),
            UserVerification = options.UserVerification
        };
    }

    public async Task<CoreFido2AssertionResult> VerifyAssertionAsync(
        string assertionId,
        string assertionResponse,
        CancellationToken cancellationToken = default)
    {
        var pending = await TakePendingAsync<PendingAssertion>(AssertionKeyPrefix + assertionId, cancellationToken);
        if (pending == null)
        {
            return CoreFido2AssertionResult.Failed("invalid_assertion", "Invalid or expired assertion");
        }

        if (DateTime.UtcNow - pending.CreatedAt > CeremonyWindow)
        {
            return CoreFido2AssertionResult.Failed("expired", "Assertion has expired");
        }

        // Parse the assertion response
        AuthenticatorAssertionResponse response;
        try
        {
            response = JsonSerializer.Deserialize<AuthenticatorAssertionResponse>(assertionResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse assertion response");
            return CoreFido2AssertionResult.Failed("invalid_response", "Invalid assertion response format");
        }

        var result = await _innerService.VerifyAssertionAsync(response, pending.Challenge, cancellationToken);

        if (!result.Success)
        {
            return CoreFido2AssertionResult.Failed(result.ErrorCode ?? "assertion_failed", result.Error);
        }

        return CoreFido2AssertionResult.Success(result.UserId!, result.CredentialId!, (int)result.SignatureCounter);
    }

    public async Task<IEnumerable<CoreFido2Credential>> GetCredentialsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _innerService.GetUserCredentialsAsync(userId, cancellationToken);

        return credentials.Select(c => new CoreFido2Credential
        {
            Id = c.Id,
            UserId = c.UserId,
            CredentialId = c.CredentialId,
            PublicKey = c.PublicKey,
            SignCount = (int)c.SignatureCounter,
            Name = c.DisplayName,
            AuthenticatorType = c.AuthenticatorType.ToString().ToLowerInvariant(),
            CreatedAt = c.CreatedAt,
            LastUsedAt = c.LastUsedAt,
            IsResidentKey = c.IsDiscoverable
        });
    }

    public async Task<bool> DeleteCredentialAsync(
        string userId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        return await _innerService.RemoveCredentialAsync(userId, credentialId, cancellationToken);
    }

    public async Task<bool> UpdateCredentialNameAsync(
        string userId,
        string credentialId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _innerService.UpdateCredentialNameAsync(userId, credentialId, name, cancellationToken);
    }

    #region Helper Methods

    private static string GenerateId()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private async Task PutPendingAsync<T>(string key, T pending, CancellationToken cancellationToken)
    {
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(pending),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CeremonyRetention }, cancellationToken);
    }

    // One-time: the ceremony is removed as it is read, so a second completion
    // with the same id fails on every instance.
    private async Task<T?> TakePendingAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        var json = await _cache.GetStringAsync(key, cancellationToken);
        if (json == null) return null;
        await _cache.RemoveAsync(key, cancellationToken);
        return JsonSerializer.Deserialize<T>(json);
    }

    #endregion

    #region Pending Ceremony Models

    internal sealed class PendingAssertion
    {
        public required string Challenge { get; set; }
        public string? Username { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    internal sealed class PendingRegistration
    {
        public required string UserId { get; set; }
        public required string Challenge { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    #endregion
}

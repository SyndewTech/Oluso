using System.Security.Claims;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Context for resource owner password validation
/// </summary>
public class ResourceOwnerPasswordValidationContext
{
    /// <summary>
    /// The username
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The password
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// The client
    /// </summary>
    public required Client Client { get; init; }

    /// <summary>
    /// The requested scopes
    /// </summary>
    public IEnumerable<string>? RequestedScopes { get; init; }

    /// <summary>
    /// Result - set this to indicate success/failure
    /// </summary>
    public GrantValidationResult Result { get; set; } = GrantValidationResult.Invalid("Not validated");
}

/// <summary>
/// Result of grant validation
/// </summary>
public class GrantValidationResult
{
    public bool IsValid { get; private init; }
    public string? SubjectId { get; private init; }
    public string? Error { get; private init; }
    public string? ErrorDescription { get; private init; }
    public IEnumerable<Claim>? Claims { get; private init; }

    public static GrantValidationResult Success(string subjectId, IEnumerable<Claim>? claims = null)
    {
        return new GrantValidationResult
        {
            IsValid = true,
            SubjectId = subjectId,
            Claims = claims
        };
    }

    public static GrantValidationResult Invalid(string error, string? description = null)
    {
        return new GrantValidationResult
        {
            IsValid = false,
            Error = error,
            ErrorDescription = description
        };
    }
}

/// <summary>
/// Validates resource owner password credentials.
/// Implement this to customize password validation logic.
/// </summary>
/// <example>
/// <code>
/// public class CustomPasswordValidator : IResourceOwnerPasswordValidator
/// {
///     private readonly ILdapService _ldap;
///
///     public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
///     {
///         var result = await _ldap.AuthenticateAsync(context.Username, context.Password);
///         if (result.Success)
///         {
///             context.Result = GrantValidationResult.Success(result.UserId);
///         }
///         else
///         {
///             context.Result = GrantValidationResult.Invalid("invalid_grant", "Invalid credentials");
///         }
///     }
/// }
/// </code>
/// </example>
public interface IResourceOwnerPasswordValidator
{
    /// <summary>
    /// Validates the resource owner password credential
    /// </summary>
    Task ValidateAsync(ResourceOwnerPasswordValidationContext context);
}

/// <summary>
/// Extension grant validator for custom grant types.
/// Implement this to add support for custom OAuth grant types.
/// </summary>
/// <example>
/// <code>
/// public class SmsGrantValidator : IExtensionGrantValidator
/// {
///     public string GrantType => "urn:mycompany:sms";
///
///     public async Task ValidateAsync(ExtensionGrantValidationContext context)
///     {
///         var phone = context.Request.Raw["phone_number"];
///         var code = context.Request.Raw["verification_code"];
///
///         if (await _smsService.VerifyCode(phone, code))
///         {
///             var user = await _users.GetByPhoneAsync(phone);
///             context.Result = GrantValidationResult.Success(user.Id);
///         }
///     }
/// }
/// </code>
/// </example>
public interface IExtensionGrantValidator
{
    /// <summary>
    /// The grant type this validator handles
    /// </summary>
    string GrantType { get; }

    /// <summary>
    /// Validates the extension grant
    /// </summary>
    Task ValidateAsync(ExtensionGrantValidationContext context);
}

/// <summary>
/// Context for extension grant validation
/// </summary>
public class ExtensionGrantValidationContext
{
    /// <summary>
    /// The client
    /// </summary>
    public required Client Client { get; init; }

    /// <summary>
    /// Raw request parameters
    /// </summary>
    public required IDictionary<string, string> Request { get; init; }

    /// <summary>
    /// Result - set this to indicate success/failure
    /// </summary>
    public GrantValidationResult Result { get; set; } = GrantValidationResult.Invalid("Not validated");
}

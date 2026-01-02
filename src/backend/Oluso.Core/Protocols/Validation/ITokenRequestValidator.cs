using Microsoft.AspNetCore.Http;
using Oluso.Core.Common;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Validates token endpoint requests
/// </summary>
public interface ITokenRequestValidator
{
    Task<ValidationResult<TokenRequest>> ValidateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default);
}

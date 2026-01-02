namespace Oluso.Core.Common;

/// <summary>
/// Result of validation operations
/// </summary>
public class ValidationResult
{
    public bool IsValid => Error == null;
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// Response headers to include in the error response (e.g., DPoP-Nonce)
    /// </summary>
    public IDictionary<string, string>? ResponseHeaders { get; set; }

    public static ValidationResult Success() => new();

    public static ValidationResult Failure(string error, string? description = null) => new()
    {
        Error = error,
        ErrorDescription = description
    };

    public static ValidationResult Failure(string error, string? description, IDictionary<string, string> headers) => new()
    {
        Error = error,
        ErrorDescription = description,
        ResponseHeaders = headers
    };
}

/// <summary>
/// Result with validated data
/// </summary>
public class ValidationResult<T> : ValidationResult
{
    public T? Data { get; set; }

    public static ValidationResult<T> Success(T data) => new() { Data = data };

    public new static ValidationResult<T> Failure(string error, string? description = null) => new()
    {
        Error = error,
        ErrorDescription = description
    };

    public static ValidationResult<T> Failure(string error, string? description, IDictionary<string, string> headers) => new()
    {
        Error = error,
        ErrorDescription = description,
        ResponseHeaders = headers
    };
}

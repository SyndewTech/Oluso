using Oluso.Core.Common;

namespace Oluso.Core.Protocols;

/// <summary>
/// Result of grant processing
/// </summary>
public class GrantResult : ValidationResult
{
    /// <summary>
    /// Subject identifier (user ID) for the grant
    /// </summary>
    public string? SubjectId { get; set; }

    /// <summary>
    /// Session ID for the grant
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Claims to include in tokens
    /// </summary>
    public IDictionary<string, object> Claims { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Scopes granted
    /// </summary>
    public ICollection<string> Scopes { get; set; } = new List<string>();

    /// <summary>
    /// Custom data for the grant
    /// </summary>
    public IDictionary<string, object> CustomData { get; set; } = new Dictionary<string, object>();

    public static GrantResult Success(string? subjectId = null, IEnumerable<string>? scopes = null)
    {
        return new GrantResult
        {
            SubjectId = subjectId,
            Scopes = scopes?.ToList() ?? new List<string>()
        };
    }

    public new static GrantResult Failure(string error, string? description = null) => new()
    {
        Error = error,
        ErrorDescription = description
    };
}

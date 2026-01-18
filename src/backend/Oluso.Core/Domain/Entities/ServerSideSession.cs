namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Server-side session for user sessions
/// </summary>
public class ServerSideSession : TenantEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = default!;
    public string Scheme { get; set; } = default!;
    public string SubjectId { get; set; } = default!;
    public string? SessionId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
    public string Data { get; set; } = default!;
}

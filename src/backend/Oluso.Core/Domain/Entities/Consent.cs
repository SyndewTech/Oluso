namespace Oluso.Core.Domain.Entities;

/// <summary>
/// User consent record
/// </summary>
public class Consent : TenantEntity
{
    public int Id { get; set; }
    public string SubjectId { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string Scopes { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public IEnumerable<string> GetScopes() =>
        string.IsNullOrEmpty(Scopes)
            ? Enumerable.Empty<string>()
            : Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public void SetScopes(IEnumerable<string> scopes) =>
        Scopes = string.Join(' ', scopes);
}

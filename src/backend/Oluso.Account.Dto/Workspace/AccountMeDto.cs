namespace Oluso.Account.Dto.Workspace;

public class AccountMeDto
{
    public string Id { get; set; } = null!;
    public string? Email { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? ProfilePictureUrl { get; set; }
    public string? TenantId { get; set; }
    public List<string> Features { get; set; } = new();
    /// <summary>Per-module facts about this user (e.g. emr.isPatient), from IProfileEnricher.</summary>
    public IDictionary<string, object> Extensions { get; set; } = new Dictionary<string, object>();
}

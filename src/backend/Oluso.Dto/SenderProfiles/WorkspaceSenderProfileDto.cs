namespace Oluso.Dto.SenderProfiles;

/// <summary>
/// Minimal workspace-facing shape. No provider, no credentials, no
/// last-test/usage state. If a workspace surface needs more, it doesn't
/// belong here — that data is for admin.
/// </summary>
public class WorkspaceSenderProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

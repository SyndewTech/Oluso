namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Device flow authorization code
/// </summary>
public class DeviceFlowCode : TenantEntity
{
    public string DeviceCode { get; set; } = default!;
    public string UserCode { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime Expiration { get; set; }
    public string Data { get; set; } = default!;
}

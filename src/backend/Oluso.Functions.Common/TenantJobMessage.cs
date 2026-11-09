namespace Oluso.Functions.Common;

/// <summary>
/// Message payload for tenant-scoped job queue processing.
/// Serialised as JSON to Azure Storage Queues.
/// </summary>
public record TenantJobMessage(string TenantId, string JobType);

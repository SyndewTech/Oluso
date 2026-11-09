namespace Oluso.Core.Storage;

/// <summary>An entity that attachments are kept on: its tenant and its plaintext id.</summary>
public sealed record FileAttachmentOwner(string TenantId, string EntityId);

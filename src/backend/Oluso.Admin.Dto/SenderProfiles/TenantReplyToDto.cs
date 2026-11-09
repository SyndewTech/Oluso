namespace Oluso.Admin.Dto.SenderProfiles;

/// <summary>The tenant's reply-to inbox and any address awaiting confirmation.</summary>
public sealed class TenantReplyToDto
{
    /// <summary>The confirmed inbox in use; null when none is confirmed.</summary>
    public string? ReplyToAddress { get; set; }
    public DateTime? VerifiedAt { get; set; }

    /// <summary>An address a confirmation link was sent to; not used until confirmed.</summary>
    public string? PendingAddress { get; set; }
    public DateTime? PendingSentAt { get; set; }

    /// <summary>Set on a request that did not send a confirmation, explaining why.</summary>
    public string? Message { get; set; }
}

public sealed class RequestReplyToDto
{
    public string? ReplyToAddress { get; set; }
}

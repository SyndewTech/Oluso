using Oluso.Core.Domain.Entities;

namespace Oluso.Admin.Dto.SenderProfiles;

public class SenderProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EmailProvider Provider { get; set; } = EmailProvider.SendGrid;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? ReplyToAddress { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? LastTestSendAt { get; set; }
    public string? LastTestError { get; set; }
    public string? ApiKey { get; set; }
    public string? CloudflareAccountId { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;
}

public class SendTestEmailRequest
{
    public string To { get; set; } = null!;
}

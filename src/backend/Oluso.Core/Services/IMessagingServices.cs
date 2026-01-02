namespace Oluso.Core.Services;

/// <summary>
/// Unified messaging service for sending emails and SMS
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// Sends an email
    /// </summary>
    Task SendEmailAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an SMS
    /// </summary>
    Task SendSmsAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);
}

#region Email Interfaces

/// <summary>
/// Service for sending emails
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email with HTML content
    /// </summary>
    Task<EmailResult> SendAsync(
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email using a provider template
    /// </summary>
    Task<EmailResult> SendTemplateAsync(
        string email,
        string templateName,
        IDictionary<string, object> templateData,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of sending an email
/// </summary>
public class EmailResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? MessageId { get; init; }

    public static EmailResult Succeeded(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static EmailResult Failed(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Alias for IEmailSender for backward compatibility
/// </summary>
public interface IEmailService : IEmailSender;

#endregion

#region SMS Interfaces

/// <summary>
/// Service for sending SMS messages
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Sends an SMS to the specified phone number
    /// </summary>
    Task<SmsResult> SendAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of sending an SMS
/// </summary>
public class SmsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? MessageId { get; init; }

    public static SmsResult Succeeded(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static SmsResult Failed(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Alias for ISmsSender for backward compatibility
/// </summary>
public interface ISmsService : ISmsSender;

#endregion

/// <summary>
/// Generic result for message sending operations.
/// Use EmailResult or SmsResult for specific operations.
/// </summary>
public class MessageSendResult
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static MessageSendResult Succeeded(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static MessageSendResult Failed(string error, string? errorCode = null) =>
        new() { Success = false, Error = error, ErrorCode = errorCode };

    public static implicit operator EmailResult(MessageSendResult result) =>
        result.Success ? EmailResult.Succeeded(result.MessageId) : EmailResult.Failed(result.Error ?? "Unknown error");

    public static implicit operator SmsResult(MessageSendResult result) =>
        result.Success ? SmsResult.Succeeded(result.MessageId) : SmsResult.Failed(result.Error ?? "Unknown error");
}

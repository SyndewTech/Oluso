namespace Oluso.Core.Services;

/// <summary>
/// A completed interactive sign-in, recorded once at the point the session
/// is actually issued (cookie or journey completion), whatever route the
/// user took to get there.
/// </summary>
public sealed class SignInAudit
{
    public required string SubjectId { get; init; }
    public string? Username { get; init; }
    public string? TenantId { get; init; }
    public required string AuthenticationMethod { get; init; }
    public string? ClientId { get; init; }
    /// <summary>
    /// The protocol callback the sign-in will return to. When <see cref="ClientId"/>
    /// is not known directly, the auditor resolves it from the correlation id here.
    /// </summary>
    public string? ReturnUrl { get; init; }
    public string? SessionId { get; init; }
    public string? IpAddress { get; init; }
}

public sealed class SignInFailureAudit
{
    public required string Username { get; init; }
    public required string FailureReason { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public string? ReturnUrl { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raises the sign-in success/failure events that feed the audit log, webhooks
/// and the admin dashboard. Never throws: auditing must not break a sign-in.
/// </summary>
public interface ISignInAuditor
{
    Task RecordSuccessAsync(SignInAudit audit, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(SignInFailureAudit audit, CancellationToken cancellationToken = default);
}

namespace Oluso.Core.Authentication;

/// <summary>How a locked link is opened.</summary>
public enum ActionTokenLockMode
{
    /// <summary>The visitor types the email address the link was sent to.</summary>
    Email,

    /// <summary>The visitor types that address and then a one-time code mailed to it.</summary>
    EmailCode,
}

/// <summary>
/// What a visitor must prove before a link shows its content. Returned by a
/// handler that implements <see cref="IActionTokenAccessGuard"/>; null means the
/// link opens straight away.
/// </summary>
public sealed class ActionTokenAccessRequirement
{
    public ActionTokenLockMode Mode { get; init; }

    /// <summary>The address the link was sent to. Null means nobody can unlock it online.</summary>
    public string? ExpectedEmail { get; init; }

    /// <summary>Shown on the locked page, e.g. "Quote from Acme Ltd". Must not reveal the document's content.</summary>
    public string Title { get; init; } = "";
    public string? Description { get; init; }

    /// <summary>Presentation-only data for the locked page, such as branding.</summary>
    public Dictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// Implemented alongside <see cref="IActionTokenHandler"/> by handlers whose
/// content may need the recipient to confirm who they are first.
/// </summary>
public interface IActionTokenAccessGuard
{
    Task<ActionTokenAccessRequirement?> GetAccessRequirementAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default);
}

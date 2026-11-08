using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Landing page for an organization invitation link
/// (<c>/account/accept-invitation?token=...</c>).
/// <para>
/// Like <see cref="ResetPasswordModel"/>, this is reached from an email in a
/// fresh browser session with no protocol flow in flight, so it is deliberately
/// NOT behind <see cref="AuthFlowGate"/>. The invitation token is the gate.
/// </para>
/// </summary>
public class AcceptInvitationModel : PageModel
{
    private readonly IOrganizationInvitationService _invitations;
    private readonly Oluso.Core.Domain.Interfaces.ITenantSettingsProvider _tenantSettings;
    private readonly ILogger<AcceptInvitationModel> _logger;

    public AcceptInvitationModel(
        IOrganizationInvitationService invitations,
        Oluso.Core.Domain.Interfaces.ITenantSettingsProvider tenantSettings,
        ILogger<AcceptInvitationModel> logger)
    {
        _invitations = invitations;
        _tenantSettings = tenantSettings;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public AcceptInvitationInputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    /// <summary>True when the token is missing or unknown.</summary>
    public bool LinkInvalid { get; set; }

    /// <summary>Set when the invitation exists but can't be redeemed (expired, revoked, already used).</summary>
    public string? BlockedReason { get; set; }

    /// <summary>True once the invitation has been redeemed — show the success state.</summary>
    public bool Accepted { get; set; }

    public string? OrganizationName { get; set; }
    public string? InvitedEmail { get; set; }
    public string? InvitedRole { get; set; }

    /// <summary>True when the invitee must choose a password to finish.</summary>
    public bool RequiresPassword { get; set; }

    public int MinLength { get; private set; }
    public bool RequireDigit { get; private set; }
    public bool RequireLowercase { get; private set; }
    public bool RequireUppercase { get; private set; }
    public bool RequireNonAlphanumeric { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await ApplyPasswordPolicyAsync(cancellationToken);

        if (string.IsNullOrEmpty(Token))
        {
            _logger.LogWarning("Accept-invitation page opened without a token.");
            LinkInvalid = true;
            return Page();
        }

        var lookup = await _invitations.LookupAsync(Token, cancellationToken);
        if (lookup == null)
        {
            LinkInvalid = true;
            return Page();
        }

        Apply(lookup);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await ApplyPasswordPolicyAsync(cancellationToken);

        if (string.IsNullOrEmpty(Token))
        {
            LinkInvalid = true;
            return Page();
        }

        var lookup = await _invitations.LookupAsync(Token, cancellationToken);
        if (lookup == null)
        {
            LinkInvalid = true;
            return Page();
        }

        Apply(lookup);

        if (!lookup.IsRedeemable)
        {
            return Page();
        }

        // Validate before redeeming so bad input doesn't consume the invitation.
        if (RequiresPassword)
        {
            var password = Input.Password ?? string.Empty;
            foreach (var error in ValidatePassword(password))
            {
                ValidationErrors.TryAdd("password", error);
            }

            if (password != (Input.ConfirmPassword ?? string.Empty))
            {
                ValidationErrors["confirm_password"] = "Passwords do not match.";
            }

            if (ValidationErrors.Count > 0)
            {
                return Page();
            }
        }

        var result = await _invitations.AcceptAsync(new AcceptInvitationRequest
        {
            Token = Token,
            Password = RequiresPassword ? Input.Password : null,
            FirstName = Input.FirstName,
            LastName = Input.LastName
        }, cancellationToken);

        if (!result.Succeeded)
        {
            ErrorMessage = result.Error;
            return Page();
        }

        Accepted = true;
        OrganizationName = result.OrganizationName;
        return Page();
    }

    private void Apply(InvitationLookup lookup)
    {
        OrganizationName = lookup.OrganizationName;
        InvitedEmail = lookup.Invitation.Email;
        InvitedRole = lookup.Invitation.Role.ToString();
        RequiresPassword = lookup.RequiresPassword;
        BlockedReason = lookup.BlockedReason;
    }

    /// <summary>
    /// The policy this tenant enforces when the password is saved (<c>TenantPasswordValidator</c>), so the checklist
    /// shows the rules that apply. ASP.NET Identity's own options are left permissive on purpose and say nothing useful.
    /// </summary>
    private async Task ApplyPasswordPolicyAsync(CancellationToken cancellationToken = default)
    {
        var policy = await _tenantSettings.GetPasswordSettingsAsync(cancellationToken);
        MinLength = policy.MinimumLength;
        RequireDigit = policy.RequireDigit;
        RequireLowercase = policy.RequireLowercase;
        RequireUppercase = policy.RequireUppercase;
        RequireNonAlphanumeric = policy.RequireNonAlphanumeric;
    }

    private IEnumerable<string> ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            yield return "Password is required.";
            yield break;
        }

        if (password.Length < MinLength)
            yield return $"Password must be at least {MinLength} characters.";
        if (RequireDigit && !password.Any(char.IsDigit))
            yield return "Password must contain at least one digit.";
        if (RequireLowercase && !password.Any(char.IsLower))
            yield return "Password must contain at least one lowercase letter.";
        if (RequireUppercase && !password.Any(char.IsUpper))
            yield return "Password must contain at least one uppercase letter.";
        if (RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
            yield return "Password must contain at least one special character.";
    }
}

public class AcceptInvitationInputModel
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
}

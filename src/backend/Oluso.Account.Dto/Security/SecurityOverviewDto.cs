namespace Oluso.Account.Dto.Security;

public class SecurityOverviewDto
{
    public bool HasPassword { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool MfaEnabled { get; set; }
    public bool EmailVerified { get; set; }
    public bool PhoneNumberVerified { get; set; }
    public DateTime? LastPasswordChange { get; set; }
    public int PasskeyCount { get; set; }

    /// <summary>
    /// Signed in through an external identity provider (the token carries an <c>idp</c> claim), whose
    /// password is managed there, not here.
    /// </summary>
    public bool IsExternalAccount { get; set; }
}

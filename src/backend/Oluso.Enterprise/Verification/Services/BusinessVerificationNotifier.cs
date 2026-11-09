using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Locale;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Verification.Services;

/// <summary>
/// Emails an applicant the outcome of a verification they stopped waiting for. Sent
/// through the organisation's transactional email sender; the link, when there is one,
/// is the absolute start page of the journey they came from.
/// </summary>
public sealed class BusinessVerificationNotifier
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BusinessVerificationNotifier> _logger;

    public BusinessVerificationNotifier(IServiceProvider services, ILogger<BusinessVerificationNotifier> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>Whether the host can send email at all; the step only offers the email path when it can.</summary>
    public bool CanEmail => _services.GetService<IEmailSender>() != null;

    /// <summary>Sends the outcome when the applicant asked for it and it's final; true when an email went out.</summary>
    public async Task<bool> SendOutcomeAsync(VerifiedBusiness business, BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(applicant.NotifyEmail) || !applicant.Status.IsFinal() || applicant.TenantId == null) return false;

        var sender = _services.GetService<IEmailSender>()
            ?? throw new InvalidOperationException("An outcome email was requested but no email sender is registered.");

        var name = WebUtility.HtmlEncode(business.RegisteredName ?? business.RegistrationNumber);
        var statusKey = applicant.Status == BusinessVerificationStatus.Verified ? "verified" : "not_verified";
        var subject = UiStringDefaults.Resolve($"business_verification_email_subject_{statusKey}", business.RegisteredName ?? business.RegistrationNumber);
        var body = $"<p>{WebUtility.HtmlEncode(UiStringDefaults.Resolve($"business_verification_email_body_{statusKey}", business.RegisteredName ?? business.RegistrationNumber))}</p>";
        if (!string.IsNullOrEmpty(applicant.ResumeUrl) && Uri.TryCreate(applicant.ResumeUrl, UriKind.Absolute, out var resume)
            && (resume.Scheme == Uri.UriSchemeHttps || resume.Scheme == Uri.UriSchemeHttp))
        {
            body += $"<p><a href=\"{WebUtility.HtmlEncode(resume.AbsoluteUri)}\">{WebUtility.HtmlEncode(UiStringDefaults.Resolve("business_verification_email_continue"))}</a></p>";
        }

        var result = await sender.SendAsync(applicant.TenantId, applicant.NotifyEmail, subject, body, cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Business verification outcome email for applicant {ApplicantId} ({Business}) failed: {Error}", applicant.Id, name, result.Error);
            return false;
        }
        return true;
    }
}

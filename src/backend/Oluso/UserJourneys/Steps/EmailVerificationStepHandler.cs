using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Verifies that the submitter controls an email address by sending an OTP
/// (or magic link) and checking the response — without authenticating anyone.
/// On success the journey state gets <c>email</c> (the verified address)
/// and <c>email_verified=true</c>; no <c>UserId</c> is set. Downstream steps
/// (sign-up, password reset, sensitive profile changes) read the
/// canonical <c>email</c> key and get the proven address.
/// </summary>
/// <remarks>
/// Configuration options:
/// - mode: "otp" (default) or "magic-link"
/// - codeLength: number of digits (default: 6)
/// - expirationMinutes: code expiration time (default: 15)
/// - maxAttempts: invalid-code attempts before failing (default: 3, 0 = unlimited)
/// - emailSubject: optional subject override
/// - emailTemplate: optional platform template name override
/// - toLinkRecipient: verify the address the journey's link was sent to; it is sent the code at once and can't be
///   changed. Runs not opened from a link sent to someone skip the step.
/// </remarks>
public class EmailVerificationStepHandler : IStepHandler
{
    public string StepType => "email_verification";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var emailService = context.ServiceProvider.GetService<IEmailService>()
            ?? (IEmailSender?)context.ServiceProvider.GetService<IEmailSender>();
        var templateService = context.ServiceProvider.GetService<IPlatformEmailTemplateService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<EmailVerificationStepHandler>>();

        var mode = context.GetConfig("mode", "otp")!;
        var codeLength = context.GetConfig("codeLength", 6);
        var expirationMinutes = context.GetConfig("expirationMinutes", 15);
        var maxAttempts = context.GetConfig("maxAttempts", 3);
        var customSubject = context.GetConfig<string?>("emailSubject", null);
        var customTemplate = context.GetConfig<string?>("emailTemplate", null);

        string? recipientEmail = null;
        if (context.GetConfig("toLinkRecipient", false))
        {
            var recipient = JourneyLaunchData.ReadRecipient(context.JourneyData);
            if (recipient == null) return StepHandlerResult.Skip();
            if (recipient.Email == null)
                return StepHandlerResult.Fail("recipient_unverifiable", "This link can't be verified by email. Ask whoever sent it for a new one.");
            recipientEmail = recipient.Email.ToLowerInvariant();
        }

        // Email submission — generate + send the code. Only acts on a real
        // form submission; a value carried over from a prior step is used
        // to pre-fill the entry form below, not to silently auto-send. The
        // link's recipient is sent the code on arrival and can't be replaced.
        var alreadyHasPendingCode = !string.IsNullOrEmpty(context.GetData<string>("email_verify_email"));
        var email = recipientEmail == null ? context.GetInput("email") : alreadyHasPendingCode ? null : recipientEmail;
        if (!string.IsNullOrEmpty(email))
        {
            email = email.Trim().ToLowerInvariant();

            if (!email.Contains('@'))
            {
                return StepHandlerResult.ShowUi("Journey/_EmailVerification", new EmailVerificationViewModel
                {
                    Mode = mode,
                    ErrorMessage = "Please enter a valid email address"
                });
            }

            var code = GenerateCode(codeLength);
            // Magic-link token is independent from the OTP — 32 bytes from a CSPRNG,
            // base64url-encoded. The OTP stays usable for users who type the code
            // by hand; the link uses this stronger, single-use token so it survives
            // being logged in mail-server / Referer headers without trivial brute force.
            var magicToken = GenerateMagicToken();
            context.SetData("email_verify_email", email);
            context.SetData("email_verify_code", code);
            context.SetData("email_verify_magic_token", magicToken);
            context.SetData("email_verify_sent_at", DateTime.UtcNow.ToString("O"));

            if (emailService == null)
            {
                logger.LogError(
                    "Email-verification {Mode} requested for {Email} but no IEmailSender/IEmailService is registered. " +
                    "Call AddMessaging(configuration) (or AddEmailSender) in startup so the platform Email config can ship the code.",
                    mode, MaskEmail(email));
            }
            else
            {
                var (subject, body) = await BuildMessageAsync(
                    templateService, mode, context, code, magicToken, expirationMinutes, email, customTemplate, cancellationToken);

                if (!string.IsNullOrEmpty(customSubject))
                {
                    subject = customSubject;
                }

                var result = await emailService.SendAsync(email, subject, body, cancellationToken);
                if (!result.Success)
                {
                    logger.LogError(
                        "Email-verification {Mode} send failed for {Email}: {Error}",
                        mode, MaskEmail(email), result.Error);
                }
                else
                {
                    logger.LogInformation("Email-verification {Mode} sent to {Email}", mode, MaskEmail(email));
                }
            }

            return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm",
                ConfirmViewModel(context, MaskEmail(email), mode, expirationMinutes));
        }

        // Magic-link consumption — the click endpoint forwards the link
        // token via this input. Independent path from the OTP so the strong
        // token isn't compared against the 6-digit code (which would defeat
        // the point of the longer token). One-shot: ClearState on success
        // wipes the stored token so a replay returns session_expired.
        var submittedMagicToken = context.GetInput("magic_token");
        if (!string.IsNullOrEmpty(submittedMagicToken))
        {
            var storedEmailForLink = context.GetData<string>("email_verify_email");
            var storedMagicToken = context.GetData<string>("email_verify_magic_token");
            var sentAtForLink = context.GetData<string>("email_verify_sent_at");

            if (string.IsNullOrEmpty(storedEmailForLink) || string.IsNullOrEmpty(storedMagicToken))
            {
                return StepHandlerResult.Fail("session_expired", "This verification link has already been used or the session has expired.");
            }

            if (DateTime.TryParse(sentAtForLink, out var linkSentTime) &&
                DateTime.UtcNow - linkSentTime > TimeSpan.FromMinutes(expirationMinutes))
            {
                logger.LogWarning("Email-verification magic link expired");
                return StepHandlerResult.Fail("link_expired", "This verification link has expired. Please request a new one.");
            }

            if (!FixedTimeEqualsString(storedMagicToken, submittedMagicToken))
            {
                logger.LogWarning("Invalid email-verification magic token submitted");
                return StepHandlerResult.Fail("invalid_link", "This verification link is invalid.");
            }

            ClearState(context);
            logger.LogInformation("Email verification (magic link) successful for {Email}", MaskEmail(storedEmailForLink));
            await CompleteSignUpAsync(context, storedEmailForLink, logger, cancellationToken);

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["email"] = storedEmailForLink,
                ["email_verified"] = true
            });
        }

        // Code verification.
        var submittedCode = context.GetInput("code");
        if (!string.IsNullOrEmpty(submittedCode))
        {
            var storedEmail = context.GetData<string>("email_verify_email");
            var storedCode = context.GetData<string>("email_verify_code");
            var sentAt = context.GetData<string>("email_verify_sent_at");

            if (string.IsNullOrEmpty(storedEmail) || string.IsNullOrEmpty(storedCode))
            {
                return StepHandlerResult.Fail("session_expired", "Session expired. Please try again.");
            }

            if (DateTime.TryParse(sentAt, out var sentTime) &&
                DateTime.UtcNow - sentTime > TimeSpan.FromMinutes(expirationMinutes))
            {
                logger.LogWarning("Email-verification code expired");
                return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm",
                    ConfirmViewModel(context, MaskEmail(storedEmail), mode, expirationMinutes,
                        errorMessage: "Code has expired. Please request a new one."));
            }

            var isValid = FixedTimeEqualsString(storedCode, submittedCode.Trim());
            if (!isValid)
            {
                var attempts = context.GetData<int?>("email_verify_attempts") ?? 0;
                attempts++;
                context.SetData("email_verify_attempts", attempts);

                if (maxAttempts > 0 && attempts >= maxAttempts)
                {
                    logger.LogWarning(
                        "Email-verification exceeded max attempts ({MaxAttempts}) for {Email}",
                        maxAttempts, MaskEmail(storedEmail));
                    ClearState(context);
                    return StepHandlerResult.Fail(
                        "too_many_attempts",
                        "Too many invalid attempts. Please start over.");
                }

                logger.LogWarning("Invalid email-verification code submitted (attempt {Attempt}/{MaxAttempts})",
                    attempts, maxAttempts);
                return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm",
                    ConfirmViewModel(context, MaskEmail(storedEmail), mode, expirationMinutes,
                        errorMessage: maxAttempts > 0
                            ? $"Invalid code. {Math.Max(0, maxAttempts - attempts)} attempt(s) remaining."
                            : "Invalid code. Please try again."));
            }

            // Valid code — emit verified-email artifacts. UserId is intentionally
            // not set: this step proves email ownership, it does not authenticate.
            ClearState(context);

            logger.LogInformation("Email verification successful for {Email}", MaskEmail(storedEmail));
            await CompleteSignUpAsync(context, storedEmail, logger, cancellationToken);

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["email"] = storedEmail,
                ["email_verified"] = true
            });
        }

        // Resend.
        if (!string.IsNullOrEmpty(context.GetInput("resend")))
        {
            var storedEmail = context.GetData<string>("email_verify_email");
            if (!string.IsNullOrEmpty(storedEmail))
            {
                context.JourneyData.Remove("email_verify_code");
                context.JourneyData.Remove("email_verify_magic_token");
                context.JourneyData.Remove("email_verify_sent_at");

                var code = GenerateCode(codeLength);
                var magicToken = GenerateMagicToken();
                context.SetData("email_verify_code", code);
                context.SetData("email_verify_magic_token", magicToken);
                context.SetData("email_verify_sent_at", DateTime.UtcNow.ToString("O"));

                if (emailService == null)
                {
                    logger.LogError(
                        "Email-verification resend requested for {Email} but no IEmailSender/IEmailService is registered.",
                        MaskEmail(storedEmail));
                }
                else
                {
                    var (subject, body) = await BuildMessageAsync(
                        templateService, mode, context, code, magicToken, expirationMinutes, storedEmail, customTemplate, cancellationToken);

                    if (!string.IsNullOrEmpty(customSubject))
                    {
                        subject = customSubject;
                    }

                    var result = await emailService.SendAsync(storedEmail, subject, body, cancellationToken);
                    if (!result.Success)
                    {
                        logger.LogError(
                            "Email-verification resend failed for {Email}: {Error}",
                            MaskEmail(storedEmail), result.Error);
                    }
                    else
                    {
                        logger.LogInformation("Email-verification resent to {Email}", MaskEmail(storedEmail));
                    }
                }

                return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm",
                    ConfirmViewModel(context, MaskEmail(storedEmail), mode, expirationMinutes,
                        successMessage: "A new code has been sent to your email."));
            }
        }

        // A different address: the code and link sent to the old one stop working.
        if (recipientEmail == null && !string.IsNullOrEmpty(context.GetInput("change_email")))
        {
            ClearState(context);
            return StepHandlerResult.ShowUi("Journey/_EmailVerification", new EmailVerificationViewModel
            {
                Mode = mode,
                Email = context.GetData<string>("email"),
            });
        }

        // Refresh / GET re-entry after a code has been sent — render the
        // confirm view instead of dropping the user back at the email-entry
        // form (or worse, re-sending the code).
        if (alreadyHasPendingCode)
        {
            var pendingEmail = context.GetData<string>("email_verify_email")!;
            return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm",
                ConfirmViewModel(context, MaskEmail(pendingEmail), mode, expirationMinutes));
        }

        return StepHandlerResult.ShowUi("Journey/_EmailVerification", new EmailVerificationViewModel
        {
            Mode = mode,
            Email = context.GetData<string>("email"),
        });
    }

    internal static async Task<(string Subject, string Body)> BuildMessageAsync(
        IPlatformEmailTemplateService? templateService,
        string mode,
        StepExecutionContext context,
        string code,
        string magicToken,
        int expirationMinutes,
        string email,
        string? customTemplateName,
        CancellationToken cancellationToken)
    {
        if (templateService != null)
        {
            var templateName = !string.IsNullOrWhiteSpace(customTemplateName)
                ? customTemplateName!
                : (mode == "magic-link"
                    ? PlatformTemplateNames.PasswordlessMagicLink
                    : PlatformTemplateNames.PasswordlessOtp);

            var scope = new Dictionary<string, object>
            {
                [PlatformTemplateScopeKeys.OtpCode] = code,
                [PlatformTemplateScopeKeys.ExpirationMinutes] = expirationMinutes,
                [PlatformTemplateScopeKeys.ExpiresIn] = Durations.Describe(TimeSpan.FromMinutes(expirationMinutes)),
                [PlatformTemplateScopeKeys.UserEmail] = email,
            };
            if (mode == "magic-link")
            {
                scope[PlatformTemplateScopeKeys.MagicLink] = JourneyLinks.Absolute(context, BuildMagicLinkUrl(context.JourneyId, magicToken));
            }

            var rendered = await templateService.RenderAsync(templateName, scope, cancellationToken);
            if (rendered != null)
            {
                return (rendered.Subject, rendered.HtmlBody);
            }
        }

        var inlineSubject = mode == "magic-link"
            ? "Verify your email address"
            : "Your email verification code";
        var inlineBody = mode == "magic-link"
            ? GenerateMagicLinkEmail(context, code, magicToken, expirationMinutes)
            : GenerateOtpEmail(code, expirationMinutes);
        return (inlineSubject, inlineBody);
    }

    // Razor-pages handler routing on the Journey/Index page (route
    // "/journey/{journeyId}/{handler?}") maps the handler segment to a
    // method suffix via case-insensitive equality with no dash handling, so
    // the URL must be a single word — "/verifyemail" matches OnGetVerifyEmailAsync.
    private static string BuildMagicLinkUrl(string journeyId, string magicToken) =>
        $"/journey/{Uri.EscapeDataString(journeyId)}/verifyemail?t={Uri.EscapeDataString(magicToken)}";

    /// <summary>
    /// Builds the verify-screen view model with the polling-required fields
    /// (JourneyId, CurrentStepId) pre-populated from the execution context,
    /// so the original device can detect a cross-device magic-link click.
    /// </summary>
    private static EmailVerificationConfirmViewModel ConfirmViewModel(
        StepExecutionContext context,
        string maskedEmail,
        string mode,
        int expirationMinutes,
        string? errorMessage = null,
        string? successMessage = null)
    {
        var codeLength = context.GetConfig("codeLength", 6);
        return new EmailVerificationConfirmViewModel
        {
            Email = maskedEmail,
            Mode = mode,
            ExpirationMinutes = expirationMinutes,
            CodeLength = codeLength,
            ErrorMessage = errorMessage,
            SuccessMessage = successMessage,
            JourneyId = context.JourneyId,
            CurrentStepId = context.StepId,
            CanChangeEmail = !context.GetConfig("toLinkRecipient", false)
        };
    }

    private static void ClearState(StepExecutionContext context)
    {
        context.JourneyData.Remove("email_verify_email");
        context.JourneyData.Remove("email_verify_code");
        context.JourneyData.Remove("email_verify_magic_token");
        context.JourneyData.Remove("email_verify_sent_at");
        context.JourneyData.Remove("email_verify_attempts");
    }

    internal static string GenerateCode(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));

    /// <summary>
    /// 32 random bytes (256 bits) base64url-encoded. Sized to make
    /// guess-the-link attacks infeasible even if a token leaks via email
    /// archives or proxy logs before it expires.
    /// </summary>
    internal static string GenerateMagicToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Constant-time string comparison via UTF-8 byte equality. Length leak
    /// is acceptable here because both tokens are fixed-length base64url
    /// strings of the same size.
    /// </summary>
    internal static bool FixedTimeEqualsString(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }

    private static string GenerateOtpEmail(string code, int expirationMinutes) => BrandedEmailLayout.Document(
        title: "Verify your email address",
        preheader: "Verify your email address",
        accent: BrandedEmailLayout.DefaultAccent,
        brand: "",
        eyebrow: "Email verification",
        heading: "Verify your email",
        rows:
            BrandedEmailLayout.Lead("Enter this code to confirm you own this email address:") +
            BrandedEmailLayout.Code(code, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note($"This code expires in <strong>{Durations.Describe(TimeSpan.FromMinutes(expirationMinutes))}</strong>.") +
            BrandedEmailLayout.Divider() +
            BrandedEmailLayout.Paragraph("Didn't ask for a code? You can safely ignore this email."),
        footnote: "This is an automated security email.");

    private static string GenerateMagicLinkEmail(StepExecutionContext context, string code, string magicToken, int expirationMinutes)
    {
        var magicLink = JourneyLinks.Absolute(context, BuildMagicLinkUrl(context.JourneyId, magicToken));
        return BrandedEmailLayout.Document(
        title: "Verify your email address",
        preheader: "Verify your email address",
        accent: BrandedEmailLayout.DefaultAccent,
        brand: "",
        eyebrow: "Email verification",
        heading: "Verify your email",
        rows:
            BrandedEmailLayout.Lead("Tap the button below to confirm you own this email address.") +
            BrandedEmailLayout.Button(magicLink, "Verify email &rarr;", BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note($"This link expires in <strong>{Durations.Describe(TimeSpan.FromMinutes(expirationMinutes))}</strong>.") +
            BrandedEmailLayout.Note("Or enter this code instead:") +
            BrandedEmailLayout.Code(code, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Divider() +
            BrandedEmailLayout.LinkFallback(magicLink, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Paragraph("Didn't ask for this? You can safely ignore this email."),
        footnote: "This is an automated security email.");
    }

    /// <summary>
    /// After sign-up created an account that still needs its email verified, verifying
    /// that account's own address records it as verified and signs the new user in
    /// (setting <c>completeSignUp</c>, on by default). Any other verification only proves
    /// the address.
    /// </summary>
    private static async Task CompleteSignUpAsync(StepExecutionContext context, string? verifiedEmail, ILogger logger, CancellationToken cancellationToken)
    {
        if (!context.GetConfig("completeSignUp", true) || string.IsNullOrEmpty(verifiedEmail)) return;

        var newUserId = context.GetData<string>(SignUpStepHandler.PendingVerificationUserDataKey);
        if (string.IsNullOrEmpty(newUserId)) return;

        var userService = context.ServiceProvider.GetService<IOlusoUserService>();
        var user = userService == null ? null : await userService.FindByIdAsync(newUserId, cancellationToken);
        if (user == null || !string.Equals(user.Email, verifiedEmail, StringComparison.OrdinalIgnoreCase)) return;

        if (!user.EmailVerified)
        {
            await userService!.SetEmailVerifiedAsync(user.Id, true, cancellationToken);
        }

        context.SetAuthenticated(user.Id, "pwd");
        context.JourneyData.Remove(SignUpStepHandler.PendingVerificationUserDataKey);
        context.JourneyData.Remove("pending_email_verification");
        context.SetData("pending_verification", false);
        logger.LogInformation("Sign-up for {UserId} completed by verifying their email", user.Id);
    }

    internal static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        if (localPart.Length <= 2)
            return localPart[0] + "***" + domain;

        return localPart[0] + "***" + localPart[^1] + domain;
    }
}

#region ViewModels

public class EmailVerificationViewModel
{
    public string Mode { get; set; } = "otp";
    public string? ErrorMessage { get; set; }
    public string? Email { get; set; }
}

public class EmailVerificationConfirmViewModel
{
    public string Email { get; set; } = null!;

    /// <summary>False when the address is fixed (the journey link's recipient), so the view offers no other.</summary>
    public bool CanChangeEmail { get; set; } = true;
    public string Mode { get; set; } = "otp";
    public int ExpirationMinutes { get; set; }
    public int CodeLength { get; set; } = 6;
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    /// <summary>
    /// Journey ID — surfaced so the view can poll the status endpoint at
    /// <c>/journey/{JourneyId}/status</c> to detect cross-device magic-link clicks.
    /// </summary>
    public string JourneyId { get; set; } = null!;

    /// <summary>
    /// Current step ID at render time. The polling script compares this
    /// against the live status response and reloads the page when they
    /// differ — i.e. another device has advanced the journey past this step.
    /// </summary>
    public string CurrentStepId { get; set; } = null!;
}

#endregion


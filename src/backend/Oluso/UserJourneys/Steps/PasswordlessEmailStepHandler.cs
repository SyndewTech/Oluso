using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles passwordless authentication via email magic links or OTP codes.
/// Users receive an email with either a magic link or a one-time code to authenticate.
/// </summary>
/// <remarks>
/// Configuration options:
/// - mode: "otp" (default) or "magic-link"
/// - codeLength: number of digits (default: 6)
/// - expirationMinutes: code expiration time (default: 15)
/// - allowSignUp: allow new users to register (default: false)
/// </remarks>
public class PasswordlessEmailStepHandler : IPrimaryAuthenticationStep
{
    public string StepType => "passwordless_email";

    /// <summary>Where the code was sent: the account's own address, or a new sign-up's typed one.</summary>
    private const string RecipientDataKey = "passwordless_recipient";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        // Prefer IEmailService (legacy alias); fall back to IEmailSender —
        // both resolve to TenantAwareEmailSender when AddMessaging is wired,
        // so the platform Email config (with per-tenant overrides) is used.
        var emailService = context.ServiceProvider.GetService<IEmailService>()
            ?? (IEmailSender?)context.ServiceProvider.GetService<IEmailSender>();
        // Optional platform template service — when an implementation is
        // registered, resolves the auth.passwordless.{otp,magic_link} template
        // with tenant-override → "_platform" default cascade. When
        // unavailable (or template not seeded yet), the handler falls back
        // to the inline body below so the auth flow still works.
        var templateService = context.ServiceProvider.GetService<IPlatformEmailTemplateService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PasswordlessEmailStepHandler>>();

        var mode = context.GetConfig("mode", "otp")!;
        var codeLength = context.GetConfig("codeLength", 6);
        var expirationMinutes = context.GetConfig("expirationMinutes", 15);
        var allowSignUp = context.GetConfig("allowSignUp", false);
        // When true, an unknown email that successfully verifies is created
        // inline (Email + Username = email, no password) and the journey
        // continues exactly as for an existing user — no downstream signup
        // step required. When false, the step emits requires_signup=true
        // and hands off to the next step (typically SignUpStepHandler) to
        // collect more profile fields before creating the account.
        var autoProvision = context.GetConfig("autoProvision", false);
        var maxAttempts = context.GetConfig("maxAttempts", 3);
        var customSubject = context.GetConfig<string?>("emailSubject", null);
        var customTemplate = context.GetConfig<string?>("emailTemplate", null);

        // Handle email submission
        var email = context.GetInput("email");
        if (!string.IsNullOrEmpty(email))
        {
            email = email.Trim().ToLowerInvariant();

            if (!email.Contains('@'))
            {
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmail", new PasswordlessEmailViewModel
                {
                    Mode = mode,
                    AllowSignUp = allowSignUp,
                    ErrorMessage = "Please enter a valid email address"
                });
            }

            var user = await userService.FindByEmailAsync(email, cancellationToken);

            // If user doesn't exist and sign-up is not allowed
            if (user == null && !allowSignUp)
            {
                // For security, don't reveal that the user doesn't exist
                logger.LogWarning("Passwordless login attempted for non-existent email");
                context.SetData("passwordless_email", email);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    CodeLength = codeLength,
                    Email = MaskEmail(email),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes
                });
            }

            // An existing account's code goes to the address on the account, never the one
            // typed: lookups are case- and Unicode-normalised, so a different string (e.g. a
            // dotless-i lookalike) can match the account while naming someone else's mailbox.
            // A new sign-up has no account yet, so its code goes to the address it gave.
            var recipient = user != null ? user.Email : email;
            if (string.IsNullOrWhiteSpace(recipient))
            {
                logger.LogWarning("Passwordless login not available for user {UserId}: the account has no email", user!.Id);
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    CodeLength = codeLength,
                    Email = MaskEmail(email),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes
                });
            }

            // Generate code
            var code = GenerateCode(codeLength);

            // Store code and email in state
            context.SetData("passwordless_email", email);
            context.SetData(RecipientDataKey, recipient);
            context.SetData("passwordless_code", code);
            context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));
            context.SetData("passwordless_user_exists", (user != null).ToString().ToLower());

            // Send email
            if (emailService == null)
            {
                logger.LogError(
                    "Passwordless {Mode} requested for {Email} but no IEmailSender/IEmailService is registered. " +
                    "Call AddMessaging(configuration) (or AddEmailSender) in startup so the platform Email config can ship the code.",
                    mode, MaskEmail(email));
            }
            else
            {
                var (subject, body) = await BuildPasswordlessMessageAsync(
                    templateService, mode, context, code, expirationMinutes, recipient, customTemplate, cancellationToken);

                if (!string.IsNullOrEmpty(customSubject))
                {
                    subject = customSubject;
                }

                var result = await emailService.SendAsync(recipient, subject, body, cancellationToken);
                if (!result.Success)
                {
                    logger.LogError(
                        "Passwordless {Mode} send failed for {Email}: {Error}",
                        mode, MaskEmail(email), result.Error);
                }
                else
                {
                    logger.LogInformation("Passwordless {Mode} sent to {Email}", mode, MaskEmail(email));
                }
            }

            return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
            {
                CodeLength = codeLength,
                Email = MaskEmail(email),
                Mode = mode,
                ExpirationMinutes = expirationMinutes
            });
        }

        // Handle code verification
        var submittedCode = context.GetInput("code");
        if (!string.IsNullOrEmpty(submittedCode))
        {
            var storedEmail = context.GetData<string>("passwordless_email");
            var storedCode = context.GetData<string>("passwordless_code");
            var sentAt = context.GetData<string>("passwordless_sent_at");
            var userExists = context.GetData<string>("passwordless_user_exists") == "true";

            if (string.IsNullOrEmpty(storedEmail) || string.IsNullOrEmpty(storedCode))
            {
                return StepHandlerResult.Fail("session_expired", "Session expired. Please try again.");
            }

            // Check expiration
            if (DateTime.TryParse(sentAt, out var sentTime) &&
                DateTime.UtcNow - sentTime > TimeSpan.FromMinutes(expirationMinutes))
            {
                logger.LogWarning("Passwordless code expired");
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    CodeLength = codeLength,
                    Email = MaskEmail(storedEmail),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = "Code has expired. Please request a new one."
                });
            }

            // Verify code
            var isValid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedCode), Encoding.UTF8.GetBytes(submittedCode.Trim()));

            if (!isValid)
            {
                // Track failed attempts and abort the journey when the
                // configured cap is reached. maxAttempts == 0 means unlimited.
                var attempts = context.GetData<int?>("passwordless_attempts") ?? 0;
                attempts++;
                context.SetData("passwordless_attempts", attempts);

                if (maxAttempts > 0 && attempts >= maxAttempts)
                {
                    logger.LogWarning(
                        "Passwordless verification exceeded max attempts ({MaxAttempts}) for {Email}",
                        maxAttempts, MaskEmail(storedEmail));
                    ClearPasswordlessState(context);
                    return StepHandlerResult.Fail(
                        "too_many_attempts",
                        $"Too many invalid attempts. Please start over.");
                }

                logger.LogWarning("Invalid passwordless code submitted (attempt {Attempt}/{MaxAttempts})",
                    attempts, maxAttempts);
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    CodeLength = codeLength,
                    Email = MaskEmail(storedEmail),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = maxAttempts > 0
                        ? $"Invalid code. {Math.Max(0, maxAttempts - attempts)} attempt(s) remaining."
                        : "Invalid code. Please try again."
                });
            }

            // Code is valid
            context.SetData("email_verified", "true");

            var user = await userService.FindByEmailAsync(storedEmail, cancellationToken);
            if (user != null)
            {
                // Existing user - authenticate
                context.SetAuthenticated(user.Id, "email");
                context.SetData("amr", "email");

                // Receiving the OTP at the user's mailbox proves they own
                // the address — persist that fact on the user record.
                // SMS verification never sets this; it proves phone
                // ownership, not email.
                if (!user.EmailVerified)
                {
                    try
                    {
                        await userService.SetEmailVerifiedAsync(user.Id, true, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Passwordless email succeeded for user {UserId} but failed to persist EmailVerified=true",
                            user.Id);
                    }
                }

                logger.LogInformation("Passwordless login successful for user {UserId}", user.Id);

                // Clear state
                ClearPasswordlessState(context);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["sub"] = user.Id,
                    ["email"] = storedEmail,
                    ["email_verified"] = true,
                    ["amr"] = "email"
                });
            }
            else if (allowSignUp && autoProvision)
            {
                // Auto-provision branch — create the user inline so the
                // journey can complete without a downstream signup step.
                // Mirrors the existing-user branch above so callers see one
                // consistent shape (sub / email / email_verified / amr).
                // Username defaults to email because Identity's UserValidator
                // rejects a null/empty UserName; admins who want a different
                // policy can post-process via an UpdateUser step.
                var createResult = await userService.CreateUserAsync(new CreateUserRequest
                {
                    Email = storedEmail,
                    Username = storedEmail,
                    RequireEmailVerification = false,
                    ClientId = context.ClientId
                }, cancellationToken);

                if (!createResult.Succeeded)
                {
                    logger.LogError(
                        "Passwordless auto-provision failed for {Email}: {Errors}",
                        MaskEmail(storedEmail),
                        createResult.Errors != null ? string.Join("; ", createResult.Errors) : createResult.Error);
                    return StepHandlerResult.Fail(
                        "create_failed",
                        createResult.MessageOr("Failed to create account"));
                }

                var newUserId = createResult.UserId!;

                try
                {
                    await userService.SetEmailVerifiedAsync(newUserId, true, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Auto-provisioned user {UserId} but failed to persist EmailVerified=true",
                        newUserId);
                }

                context.SetAuthenticated(newUserId, "email");
                context.SetData("amr", "email");
                ClearPasswordlessState(context);

                logger.LogInformation(
                    "Passwordless auto-provisioned and signed in new user {UserId} for {Email}",
                    newUserId, MaskEmail(storedEmail));

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["sub"] = newUserId,
                    ["email"] = storedEmail,
                    ["email_verified"] = true,
                    ["amr"] = "email"
                });
            }
            else if (allowSignUp)
            {
                // Hand-off branch — emit the verified email and let the
                // next step (typically SignUpStepHandler) collect more
                // fields before creating the account.
                context.SetData("verified_email", storedEmail);

                logger.LogInformation("Email verified for sign-up: {Email}", MaskEmail(storedEmail));

                ClearPasswordlessState(context);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["verified_email"] = storedEmail,
                    ["requires_signup"] = true
                });
            }
            else
            {
                return StepHandlerResult.Fail("user_not_found", "User not found");
            }
        }

        // Handle resend request
        if (!string.IsNullOrEmpty(context.GetInput("resend")))
        {
            var storedEmail = context.GetData<string>("passwordless_email");
            var resendTo = context.GetData<string>(RecipientDataKey);
            if (!string.IsNullOrEmpty(storedEmail))
            {
                // Remove old code and regenerate
                context.JourneyData.Remove("passwordless_code");
                context.JourneyData.Remove("passwordless_sent_at");

                // Generate new code
                var code = GenerateCode(codeLength);
                context.SetData("passwordless_code", code);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                // Unknown address with sign-up off: nothing to send, but answer the same way.
                if (string.IsNullOrEmpty(resendTo))
                {
                    logger.LogDebug("Passwordless code resend for {Email} not sent: no account and sign-up is off", MaskEmail(storedEmail));
                }
                else if (emailService == null)
                {
                    logger.LogError(
                        "Passwordless code resend requested for {Email} but no IEmailSender/IEmailService is registered.",
                        MaskEmail(storedEmail));
                }
                else
                {
                    var (subject, body) = await BuildPasswordlessMessageAsync(
                        templateService, mode, context, code, expirationMinutes, resendTo, customTemplate, cancellationToken);

                    if (!string.IsNullOrEmpty(customSubject))
                    {
                        subject = customSubject;
                    }

                    var result = await emailService.SendAsync(resendTo, subject, body, cancellationToken);
                    if (!result.Success)
                    {
                        logger.LogError(
                            "Passwordless code resend failed for {Email}: {Error}",
                            MaskEmail(storedEmail), result.Error);
                    }
                    else
                    {
                        logger.LogInformation("Passwordless code resent to {Email}", MaskEmail(storedEmail));
                    }
                }

                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    CodeLength = codeLength,
                    Email = MaskEmail(storedEmail),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes,
                    SuccessMessage = "A new code has been sent to your email."
                });
            }
        }

        // Initial state - show email input
        return StepHandlerResult.ShowUi("Journey/_PasswordlessEmail", new PasswordlessEmailViewModel
        {
            Mode = mode,
            AllowSignUp = allowSignUp
        });
    }

    /// <summary>
    /// Resolves the platform template for the requested mode (when the
    /// service is registered + template seeded), otherwise returns the
    /// hardcoded inline body so the auth flow keeps working without
    /// Workflows wired in. Honours an explicit <paramref name="customTemplateName"/>
    /// override from the journey step config — useful when a tenant has
    /// saved their own variant alongside the platform default.
    /// </summary>
    private static async Task<(string Subject, string Body)> BuildPasswordlessMessageAsync(
        IPlatformEmailTemplateService? templateService,
        string mode,
        StepExecutionContext context,
        string code,
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
                scope[PlatformTemplateScopeKeys.MagicLink] = JourneyLinks.Absolute(context, $"/journey/{context.JourneyId}?code={code}");
            }

            var rendered = await templateService.RenderAsync(templateName, scope, cancellationToken);
            if (rendered != null)
            {
                return (rendered.Subject, rendered.HtmlBody);
            }
        }

        var inlineSubject = mode == "magic-link"
            ? "Sign in to your account"
            : "Your verification code";
        var inlineBody = mode == "magic-link"
            ? GenerateMagicLinkEmail(context, code, expirationMinutes)
            : GenerateOtpEmail(code, expirationMinutes);
        return (inlineSubject, inlineBody);
    }

    private static void ClearPasswordlessState(StepExecutionContext context)
    {
        context.JourneyData.Remove("passwordless_email");
        context.JourneyData.Remove(RecipientDataKey);
        context.JourneyData.Remove("passwordless_code");
        context.JourneyData.Remove("passwordless_sent_at");
        context.JourneyData.Remove("passwordless_user_exists");
    }

    private static string GenerateCode(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));

    private static string GenerateOtpEmail(string code, int expirationMinutes) => BrandedEmailLayout.Document(
        title: "Your verification code",
        preheader: "Your verification code",
        accent: BrandedEmailLayout.DefaultAccent,
        brand: "",
        eyebrow: "Verification code",
        heading: "Here's your code",
        rows:
            BrandedEmailLayout.Lead("Enter this code to sign in:") +
            BrandedEmailLayout.Code(code, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note($"This code expires in <strong>{Durations.Describe(TimeSpan.FromMinutes(expirationMinutes))}</strong>.") +
            BrandedEmailLayout.Divider() +
            BrandedEmailLayout.Paragraph("Didn't ask for a code? You can safely ignore this email."),
        footnote: "This is an automated security email.");

    private static string GenerateMagicLinkEmail(StepExecutionContext context, string code, int expirationMinutes)
    {
        var magicLink = JourneyLinks.Absolute(context, $"/journey/{context.JourneyId}?code={code}");
        return BrandedEmailLayout.Document(
        title: "Sign in",
        preheader: "Sign in",
        accent: BrandedEmailLayout.DefaultAccent,
        brand: "",
        eyebrow: "Magic link",
        heading: "Sign in to your account",
        rows:
            BrandedEmailLayout.Lead("Tap the button below to sign in &mdash; no password needed.") +
            BrandedEmailLayout.Button(magicLink, "Sign in &rarr;", BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note($"This link expires in <strong>{Durations.Describe(TimeSpan.FromMinutes(expirationMinutes))}</strong>.") +
            BrandedEmailLayout.Note("Or enter this code instead:") +
            BrandedEmailLayout.Code(code, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Divider() +
            BrandedEmailLayout.LinkFallback(magicLink, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Paragraph("Didn't try to sign in? You can safely ignore this email."),
        footnote: "This is an automated security email.");
    }

    private static string MaskEmail(string email)
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

public class PasswordlessEmailViewModel
{
    public string Mode { get; set; } = "otp";
    public bool AllowSignUp { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Email { get; set; }
}

public class PasswordlessEmailVerifyViewModel
{
    public string Email { get; set; } = null!;
    public string Mode { get; set; } = "otp";
    public int CodeLength { get; set; } = 6;
    public int ExpirationMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

#endregion

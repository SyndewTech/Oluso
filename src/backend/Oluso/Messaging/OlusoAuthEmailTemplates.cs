using Oluso.Core;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using L = Oluso.Core.Services.BrandedEmailLayout;

namespace Oluso.Messaging;

/// <summary>
/// Platform-default email templates Oluso ships for its built-in auth
/// flows (passwordless OTP / magic-link, MFA email codes, password reset,
/// welcome invite, organisation invitation). Seeded under the
/// <see cref="PlatformConstants.TenantId"/> row on startup; tenants can save
/// a row of the same name under their own id to override.
///
/// Bodies use Scriban placeholders so the same template covers every
/// tenant — see <see cref="PlatformTemplateScopeKeys"/> for the standard
/// keys the service auto-populates from tenant context + branding — and
/// share the <see cref="BrandedEmailLayout"/> look.
/// </summary>
public class OlusoAuthEmailTemplates : IPlatformTemplateSeeder
{
    public IEnumerable<EmailTemplateSeed> GetPlatformTemplates()
    {
        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.PasswordlessOtp,
            Description = "One-time code emailed for passwordless sign-in",
            Subject = "Your verification code",
            HtmlBody = OtpHtml,
            TextBody = OtpText,
            DesignJson = OtpDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.OtpCode, Description = "6-digit code", Required = true },
                new() { Name = PlatformTemplateScopeKeys.ExpirationMinutes, Description = "Code TTL in minutes", Required = true },
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Tenant logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Tenant primary color (auto-filled)" },
            ],
        };

        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.PasswordlessMagicLink,
            Description = "Magic-link emailed for passwordless sign-in",
            Subject = "Sign in to {{tenant_name}}",
            HtmlBody = MagicLinkHtml,
            TextBody = MagicLinkText,
            DesignJson = MagicLinkDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.MagicLink, Description = "Sign-in URL", Required = true },
                new() { Name = PlatformTemplateScopeKeys.OtpCode, Description = "Fallback 6-digit code", Required = true },
                new() { Name = PlatformTemplateScopeKeys.ExpirationMinutes, Description = "Link TTL in minutes", Required = true },
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Tenant logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Tenant primary color (auto-filled)" },
            ],
        };

        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.MfaEmail,
            Description = "Email-channel one-time code for MFA challenges",
            Subject = "Your verification code",
            HtmlBody = MfaHtml,
            TextBody = MfaText,
            DesignJson = MfaDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.OtpCode, Description = "6-digit code", Required = true },
                new() { Name = PlatformTemplateScopeKeys.ExpirationMinutes, Description = "Code TTL in minutes" },
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Tenant logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Tenant primary color (auto-filled)" },
            ],
        };

        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.PasswordReset,
            Description = "Password reset: a link from the forgot-password page, or a code from a password-reset journey step",
            Subject = "Reset your password",
            HtmlBody = PasswordResetHtml,
            TextBody = PasswordResetText,
            DesignJson = PasswordResetDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.ResetLink, Description = "Reset URL" },
                new() { Name = PlatformTemplateScopeKeys.UserName, Description = "Account holder's name" },
                new() { Name = PlatformTemplateScopeKeys.OtpCode, Description = "Reset code (journey step)" },
                new() { Name = PlatformTemplateScopeKeys.ExpirationMinutes, Description = "Code TTL in minutes (journey step)" },
                new() { Name = PlatformTemplateScopeKeys.UserEmail, Description = "Account email" },
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Tenant logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Tenant primary color (auto-filled)" },
            ],
        };

        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.WelcomeInvite,
            Description = "Sent to the administrator of a newly provisioned organisation: the platform welcomes the organisation and they set their password",
            Subject = WelcomeInviteSubject,
            HtmlBody = WelcomeInviteHtml,
            TextBody = WelcomeInviteText,
            DesignJson = WelcomeInviteDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.ResetLink, Description = "Password reset URL", Required = true },
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = "org_name", Description = "Organisation name", Required = true },
                new() { Name = "tenant_identifier", Description = "Tenant short identifier" },
                new() { Name = PlatformTemplateScopeKeys.PlatformName, Description = "The platform's name (from configuration)" },
                new() { Name = PlatformTemplateScopeKeys.ExpiresIn, Description = "How long the link stays valid" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Primary colour (auto-filled)" },
            ],
        };

        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.AccountInvite,
            Description = "Sent when an administrator creates a user and invites them: the tenant welcomes them and they set their own password",
            Subject = AccountInviteSubject,
            HtmlBody = AccountInviteHtml,
            TextBody = AccountInviteText,
            DesignJson = AccountInviteDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.ResetLink, Description = "Set-password URL", Required = true },
                new() { Name = PlatformTemplateScopeKeys.ExpiresIn, Description = "How long the link stays valid", Required = true },
                new() { Name = PlatformTemplateScopeKeys.UserName, Description = "The new user's name" },
                new() { Name = PlatformTemplateScopeKeys.UserEmail, Description = "The new user's email" },
                new() { Name = "inviter_name", Description = "Who created the account" },
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Tenant logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Tenant primary color (auto-filled)" },
            ],
        };

        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.OrgInvitation,
            Description = "Invitation to join an organization, carrying the accept link",
            Subject = "You've been invited to join {{org_name}}",
            HtmlBody = OrgInvitationHtml,
            TextBody = OrgInvitationText,
            DesignJson = OrgInvitationDesign,
            Category = "Auth",
            Variables =
            [
                new() { Name = "accept_link", Description = "Invitation accept URL", Required = true },
                new() { Name = "org_name", Description = "Organisation name", Required = true },
                new() { Name = "role_name", Description = "Role the invitee will hold" },
                new() { Name = "inviter_name", Description = "Who sent the invitation" },
                new() { Name = "personal_message", Description = "Optional note from the inviter" },
                new() { Name = "expires_at", Description = "When the invitation expires" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Primary colour (auto-filled)" },
            ],
        };
    }

    // ─────────────────────────── shared pieces ────────────────────────────

    // Missing values are null and empty ones are "", and the renderer treats "" as true,
    // so every optional value is tested with Has().
    private static string Has(string name) => $"{name} && {name} != \"\"";

    private const string Accent = "{{ accent }}";
    private const string Brand = "{{ brand_name }}";

    /// <summary>Sets accent (brand colour or the default) and brand_name (the expression given).</summary>
    private static string Prelude(string brandExpression) =>
        $"{{{{~ accent = primary_color | object.default \"{L.DefaultAccent}\"; brand_name = {brandExpression} ~}}}}";

    private const string TenantBrand = "tenant_display_name | object.default tenant_name";

    private static readonly string BrandMark =
        $"{{{{ if {Has("logo_url")} }}}}{L.Logo("{{ logo_url }}", Brand)}" +
        $"{{{{ else if {Has("brand_name")} }}}}{L.Monogram("{{ brand_name | string.slice 0 1 | string.upcase }}", Accent)}{{{{ end }}}}";

    private static readonly string ToBrand = $"{{{{ if {Has("brand_name")} }}}} to {L.Strong(Brand)}{{{{ end }}}}";

    // The lifetime: expires_in ("24 hours") when the caller gives it, otherwise expiration_minutes.
    private const string Lifetime =
        "{{ if expires_in && expires_in != \"\" }}{{ expires_in }}{{ else }}{{ expiration_minutes }} minutes{{ end }}";

    private static readonly string HasLifetime = $"{Has("expires_in")} || {Has("expiration_minutes")}";

    private static string Expiry(string what) =>
        $"{{{{ if {HasLifetime} }}}}{L.Note($"This {what} expires in <strong>{Lifetime}</strong>.")}{{{{ end }}}}";

    private static readonly string SecurityFootnote =
        $"This is an automated security email{{{{ if {Has("brand_name")} }}}} from {Brand}{{{{ end }}}}.";

    // ─────────────────────────── one-time code ────────────────────────────

    private static readonly string OtpHtml = Prelude(TenantBrand) + L.Document(
        title: "Your verification code",
        preheader: "Your code is {{ otp_code }}. It expires in {{ expiration_minutes }} minutes.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "Verification code",
        heading: "Here's your code",
        rows:
            L.Lead($"Enter this code to continue{ToBrand}.") +
            L.Code("{{ otp_code }}", Accent) +
            Expiry("code") +
            L.Divider() +
            L.Paragraph("Didn't ask for a code? You can safely ignore this email &mdash; someone may have typed your address by mistake."),
        footnote: SecurityFootnote);

    private const string OtpText = """
Your verification code

Enter this code to continue{{ if tenant_name && tenant_name != "" }} to {{ tenant_name }}{{ end }}: {{ otp_code }}

This code expires in {{ expiration_minutes }} minutes.

Didn't ask for a code? You can safely ignore this email.
""";

    private static readonly string OtpDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Here's your code")
        .AddText("""<p style="text-align: center;">Enter this code to continue:</p><p style="text-align: center; font-size: 32px; font-weight: 700; letter-spacing: 8px;">{{ otp_code }}</p><p style="text-align: center; font-size: 13px; color: #71717A;">This code expires in {{ expiration_minutes }} minutes.</p>""")
        .AddFooter("<p>Didn't ask for a code? You can safely ignore this email.</p>")
        .Build();

    // ─────────────────────────── magic link ────────────────────────────

    private static readonly string MagicLinkHtml = Prelude(TenantBrand) + L.Document(
        title: "Sign in",
        preheader: "Your sign-in link is ready. It expires in {{ expiration_minutes }} minutes.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "Magic link",
        heading: $"Sign in{{{{ if {Has("brand_name")} }}}} to {Brand}{{{{ end }}}}",
        rows:
            L.Lead("Tap the button below to sign in &mdash; no password needed.") +
            L.Button("{{ magic_link }}", "Sign in &rarr;", Accent) +
            Expiry("link") +
            $"{{{{ if {Has("otp_code")} }}}}" + L.Note("Or enter this code instead:") + L.Code("{{ otp_code }}", Accent) + "{{ end }}" +
            L.Divider() +
            L.LinkFallback("{{ magic_link }}", Accent) +
            L.Paragraph("Didn't try to sign in? You can safely ignore this email."),
        footnote: SecurityFootnote);

    private const string MagicLinkText = """
Sign in to {{ tenant_name }}

Use this link to sign in: {{ magic_link }}

Or enter this code: {{ otp_code }}

The link and code expire in {{ expiration_minutes }} minutes.

Didn't try to sign in? You can safely ignore this email.
""";

    private static readonly string MagicLinkDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Sign in to {{ tenant_name }}")
        .AddText("""<p style="text-align: center;">Tap the button below to sign in &mdash; no password needed.</p>""")
        .AddButton("Sign in &rarr;", "{{ magic_link }}")
        .AddText("""<p style="text-align: center;">Or enter this code: <strong>{{ otp_code }}</strong></p><p style="text-align: center; font-size: 13px; color: #71717A;">The link and code expire in {{ expiration_minutes }} minutes.</p>""")
        .AddFooter("<p>Didn't try to sign in? You can safely ignore this email.</p>")
        .Build();

    // ─────────────────────────── MFA code ────────────────────────────

    private static readonly string MfaHtml = Prelude(TenantBrand) + L.Document(
        title: "Your verification code",
        preheader: "Your code is {{ otp_code }}. Use it to finish signing in.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "Two-step verification",
        heading: "Confirm it's you",
        rows:
            L.Lead($"Enter this code to finish signing in{ToBrand}.") +
            L.Code("{{ otp_code }}", Accent) +
            Expiry("code") +
            L.Divider() +
            L.Paragraph($"{L.Strong("Didn't try to sign in?")} Someone may know your password. Change it as soon as you can and review your account's recent activity."),
        footnote: SecurityFootnote);

    private const string MfaText = """
Confirm it's you

Your verification code: {{ otp_code }}

{{ if expiration_minutes && expiration_minutes != "" }}This code expires in {{ expiration_minutes }} minutes.{{ end }}

Didn't try to sign in? Someone may know your password. Change it as soon as you can.
""";

    private static readonly string MfaDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Confirm it's you")
        .AddText("""<p style="text-align: center;">Enter this code to finish signing in:</p><p style="text-align: center; font-size: 32px; font-weight: 700; letter-spacing: 8px;">{{ otp_code }}</p>""")
        .AddFooter("<p>Didn't try to sign in? Someone may know your password. Change it as soon as you can.</p>")
        .Build();

    // ─────────────────────────── password reset ────────────────────────────

    private static readonly string PasswordResetHtml = Prelude(TenantBrand) + L.Document(
        title: "Reset your password",
        preheader: "Here's how to choose a new password.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "Password reset",
        heading: "Reset your password",
        rows:
            L.Lead(
                $"We received a request to reset the password for{{{{ if {Has("user_email")} }}}} {L.Strong("{{ user_email }}")}{{{{ else }}}} your account{{{{ end }}}}" +
                $"{{{{ if {Has("brand_name")} }}}} at {L.Strong(Brand)}{{{{ end }}}}.") +
            $"{{{{ if {Has("reset_link")} }}}}" + L.Button("{{ reset_link }}", "Choose a new password &rarr;", Accent) + "{{ end }}" +
            $"{{{{ if {Has("otp_code")} }}}}" +
            L.Note($"{{{{ if {Has("reset_link")} }}}}Or enter this code where you asked for the reset:{{{{ else }}}}Enter this code to continue:{{{{ end }}}}") +
            L.Code("{{ otp_code }}", Accent) + "{{ end }}" +
            $"{{{{ if {HasLifetime} }}}}{L.Note($"{{{{ if {Has("reset_link")} && {Has("otp_code")} }}}}The link and code expire{{{{ else if {Has("reset_link")} }}}}This link expires{{{{ else }}}}This code expires{{{{ end }}}} in <strong>{Lifetime}</strong>.")}{{{{ end }}}}" +
            L.Divider() +
            $"{{{{ if {Has("reset_link")} }}}}" + L.LinkFallback("{{ reset_link }}", Accent) + "{{ end }}" +
            L.Paragraph("Didn't ask to reset your password? You can safely ignore this email &mdash; your password won't change."),
        footnote: SecurityFootnote);

    private const string PasswordResetText = """
Reset your password

We received a request to reset the password for {{ if user_email && user_email != "" }}{{ user_email }}{{ else }}your account{{ end }}{{ if tenant_name && tenant_name != "" }} at {{ tenant_name }}{{ end }}.
{{ if reset_link && reset_link != "" }}
Choose a new password:
{{ reset_link }}
{{ end }}{{ if otp_code && otp_code != "" }}
Your reset code: {{ otp_code }}
{{ end }}{{ if (expires_in && expires_in != "") || (expiration_minutes && expiration_minutes != "") }}
This expires in {{ if expires_in && expires_in != "" }}{{ expires_in }}{{ else }}{{ expiration_minutes }} minutes{{ end }}.
{{ end }}
Didn't ask to reset your password? You can safely ignore this email - your password won't change.
""";

    private static readonly string PasswordResetDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Reset your password")
        .AddText("""<p style="text-align: center;">We received a request to reset the password for {{ user_email }}.</p>""")
        .AddButton("Choose a new password &rarr;", "{{ reset_link }}")
        .AddFooter("<p>Didn't ask to reset your password? You can safely ignore this email &mdash; your password won't change.</p>")
        .Build();

    // ─────────────────────────── welcome (new org admin) ────────────────────────────

    // The platform speaks: it welcomes the organisation on board. Without a configured platform name the wording
    // still reads right, it just doesn't name the platform.
    private const string HasPlatform = "platform_name && platform_name != \"\"";

    private const string WelcomeInviteSubject =
        "{{ if platform_name && platform_name != \"\" }}Welcome to {{ platform_name }}, {{ org_name }}{{ else }}Your administrator account for {{ org_name }} is ready{{ end }}";

    private static readonly string WelcomeInviteHtml = Prelude("platform_name | object.default org_name") + L.Document(
        title: $"{{{{ if {HasPlatform} }}}}Welcome to {{{{ platform_name }}}}{{{{ else }}}}Welcome aboard{{{{ end }}}}",
        preheader: "Your administrator account is ready. Set your password to get started.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "Welcome aboard",
        heading: $"{{{{ if {HasPlatform} }}}}Welcome to {{{{ platform_name }}}}{{{{ else }}}}Welcome aboard{{{{ end }}}}",
        rows:
            L.Lead($"{{{{ if {HasPlatform} }}}}{L.Strong("{{ platform_name }}")} is delighted to welcome {L.Strong("{{ org_name }}")} on board." +
                   $"{{{{ else }}}}{L.Strong("{{ org_name }}")} is ready to go.{{{{ end }}}} " +
                   "An administrator account has been created for you. Set your password to get started.") +
            $"{{{{ if {Has("tenant_name")} }}}}" +
            L.Pill("Workspace", $"{{{{ tenant_name }}}}{{{{ if {Has("tenant_identifier")} }}}} ({{{{ tenant_identifier }}}}){{{{ end }}}}", Accent) +
            "{{ end }}" +
            L.Button("{{ reset_link }}", "Set your password &rarr;", Accent) +
            Expiry("link") +
            L.Divider() +
            L.LinkFallback("{{ reset_link }}", Accent),
        footnote: $"You received this because an administrator account was created for this address for {{{{ org_name }}}}" +
                  $"{{{{ if {HasPlatform} }}}} on {{{{ platform_name }}}}{{{{ end }}}}.");

    private const string WelcomeInviteText = """
{{ if platform_name && platform_name != "" }}Welcome to {{ platform_name }}

{{ platform_name }} is delighted to welcome {{ org_name }} on board.{{ else }}Welcome aboard

{{ org_name }} is ready to go.{{ end }} An administrator account has been created for you.
{{ if tenant_name && tenant_name != "" }}Your workspace {{ tenant_name }}{{ if tenant_identifier && tenant_identifier != "" }} ({{ tenant_identifier }}){{ end }} is ready to use.{{ end }}

Set your password using this link:
{{ reset_link }}
{{ if expires_in && expires_in != "" }}
This link expires in {{ expires_in }}.
{{ end }}
""";

    private static readonly string WelcomeInviteDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Welcome to {{ platform_name }}")
        .AddText("""<p style="text-align: center;"><strong>{{ platform_name }}</strong> is delighted to welcome <strong>{{ org_name }}</strong> on board. An administrator account has been created for you. Set your password to get started.</p>""")
        .AddButton("Set your password &rarr;", "{{ reset_link }}")
        .AddText("""<p style="text-align: center; font-size: 13px; color: #71717A;">This link expires in <strong>{{ expires_in }}</strong>.</p>""")
        .AddDivider()
        .AddText("""<p style="font-size: 12px; color: #71717A;">Button not working? Paste this link into your browser:<br /><a href="{{ reset_link }}">{{ reset_link }}</a></p>""")
        .Build();

    // ─────────────────────────── account invite (new tenant user) ────────────────────────────

    // The tenant speaks: an administrator created the account and the user sets their own password.
    private const string AccountInviteSubject =
        "You're invited to {{ tenant_display_name | object.default tenant_name }}: set your password";

    private static readonly string AccountInviteHtml = Prelude(TenantBrand) + L.Document(
        title: "Welcome to {{ brand_name }}",
        preheader: "An account has been created for you. Set your password to sign in.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "You're invited",
        heading: "Welcome to {{ brand_name }}",
        rows:
            L.Lead($"{{{{ if {Has("user_name")} }}}}Hi {{{{ user_name }}}}, {{{{ else }}}}Hello, {{{{ end }}}}" +
                   $"{{{{ if {Has("inviter_name")} }}}}{{{{ inviter_name }}}} has{{{{ else }}}}an administrator has{{{{ end }}}} created an account for you at {L.Strong(Brand)}. " +
                   "Set your password to sign in.") +
            $"{{{{ if {Has("user_email")} }}}}" + L.Pill("Your sign-in", "{{ user_email }}", Accent) + "{{ end }}" +
            L.Button("{{ reset_link }}", "Set your password &rarr;", Accent) +
            Expiry("link") +
            L.Divider() +
            L.LinkFallback("{{ reset_link }}", Accent) +
            L.Paragraph("If the link has expired, ask your administrator to send the invite again."),
        footnote: $"You received this because an account was created for this address at {Brand}.");

    private const string AccountInviteText = """
Welcome to {{ tenant_display_name | object.default tenant_name }}

{{ if user_name && user_name != "" }}Hi {{ user_name }}, {{ else }}Hello, {{ end }}{{ if inviter_name && inviter_name != "" }}{{ inviter_name }} has{{ else }}an administrator has{{ end }} created an account for you at {{ tenant_display_name | object.default tenant_name }}. Set your password to sign in.
{{ if user_email && user_email != "" }}Your sign-in: {{ user_email }}{{ end }}

Set your password using this link:
{{ reset_link }}
{{ if expires_in && expires_in != "" }}
This link expires in {{ expires_in }}. If it has expired, ask your administrator to send the invite again.
{{ end }}
""";

    private static readonly string AccountInviteDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Welcome to {{ tenant_name }}")
        .AddText("""<p style="text-align: center;">An account has been created for you at <strong>{{ tenant_name }}</strong>. Set your password to sign in.</p>""")
        .AddButton("Set your password &rarr;", "{{ reset_link }}")
        .AddText("""<p style="text-align: center; font-size: 13px; color: #71717A;">This link expires in <strong>{{ expires_in }}</strong>. If it has expired, ask your administrator to send the invite again.</p>""")
        .AddDivider()
        .AddText("""<p style="font-size: 12px; color: #71717A;">Button not working? Paste this link into your browser:<br /><a href="{{ reset_link }}">{{ reset_link }}</a></p>""")
        .Build();

    // ─────────────────────────── organisation invitation ────────────────────────────

    private static readonly string OrgInvitationHtml = Prelude("org_name") + L.Document(
        title: "You're invited to join {{ org_name }}",
        preheader: $"{{{{ if {Has("inviter_name")} }}}}{{{{ inviter_name }}}} invited you{{{{ else }}}}You're invited{{{{ end }}}} to join {{{{ org_name }}}}{{{{ if {Has("role_name")} }}}} as {{{{ role_name }}}}{{{{ end }}}}.",
        accent: Accent,
        brand: BrandMark,
        eyebrow: "You're invited",
        heading: "Join {{ org_name }}",
        rows:
            L.Lead($"{{{{ if {Has("inviter_name")} }}}}{L.Strong("{{ inviter_name }}")} has invited you{{{{ else }}}}You've been invited{{{{ end }}}} to collaborate with the team at {L.Strong("{{ org_name }}")}.") +
            $"{{{{ if {Has("role_name")} }}}}" + L.Pill("Your role", "{{ role_name }}", Accent) + "{{ end }}" +
            $"{{{{ if {Has("personal_message")} }}}}" +
            L.Quote("{{ personal_message }}", $"{{{{ if {Has("inviter_name")} }}}}{L.Attribution("{{ inviter_name }}")}{{{{ end }}}}", Accent) +
            "{{ end }}" +
            L.Button("{{ accept_link }}", "Accept invitation &rarr;", Accent) +
            $"{{{{ if {Has("expires_at")} }}}}" + L.Note("This invitation expires on <strong>{{ expires_at }}</strong>.") + "{{ end }}" +
            L.Divider() +
            L.LinkFallback("{{ accept_link }}", Accent),
        footnote: "You received this because someone at {{ org_name }} invited this address.<br />Not expecting it? You can safely ignore this email &mdash; nothing happens unless you accept.");

    private const string OrgInvitationText = """
You're invited to join {{ org_name }}

{{ if inviter_name && inviter_name != "" }}{{ inviter_name }} has invited you{{ else }}You've been invited{{ end }} to collaborate with the team at {{ org_name }}{{ if role_name && role_name != "" }} as {{ role_name }}{{ end }}.
{{ if personal_message && personal_message != "" }}
"{{ personal_message }}"{{ if inviter_name && inviter_name != "" }}
  - {{ inviter_name }}{{ end }}
{{ end }}
Accept the invitation:
{{ accept_link }}
{{ if expires_at && expires_at != "" }}
This invitation expires on {{ expires_at }}.
{{ end }}
Not expecting this? You can safely ignore this email - nothing happens unless you accept.
""";

    private static readonly string OrgInvitationDesign = UnlayerDesignBuilder.Create()
        .WithAccentColor(L.DefaultAccent)
        .AddHeader("Join {{ org_name }}")
        .AddText("""
<p style="font-size: 16px; line-height: 1.6;">{{ if inviter_name && inviter_name != "" }}<strong>{{ inviter_name }}</strong> has invited you{{ else }}You've been invited{{ end }} to collaborate with the team at <strong>{{ org_name }}</strong>{{ if role_name && role_name != "" }} as <strong>{{ role_name }}</strong>{{ end }}.</p>
{{ if personal_message && personal_message != "" }}<p style="font-style: italic; border-left: 4px solid #4F46E5; padding-left: 14px;">&ldquo;{{ personal_message }}&rdquo;</p>{{ end }}
""")
        .AddButton("Accept invitation &rarr;", "{{ accept_link }}")
        .AddText("""{{ if expires_at && expires_at != "" }}<p style="font-size: 13px; color: #71717A; text-align: center;">This invitation expires on <strong>{{ expires_at }}</strong>.</p>{{ end }}""")
        .AddDivider()
        .AddText("""<p style="font-size: 12px; color: #71717A;">Button not working? Paste this link into your browser:<br /><a href="{{ accept_link }}">{{ accept_link }}</a></p>""")
        .AddFooter("<p>Not expecting this? You can safely ignore this email &mdash; nothing happens unless you accept.</p>")
        .Build();
}

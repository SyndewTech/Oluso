namespace Oluso.Core.UserJourneys;

/// <summary>
/// Canonical platform-default journey policies. Used by both
/// <see cref="InMemoryJourneyPolicyStore"/> (constructor seed) and the
/// EF-backed seeder that runs after migrations so EF deployments get the
/// same baseline as in-memory deployments.
/// <para>
/// All defaults have <c>TenantId = null</c> so tenant-scoped policies (added
/// via the admin UI) can override them. IDs are stable strings so the
/// idempotent seeder can detect "already present" without GUID drift.
/// </para>
/// </summary>
public static class DefaultJourneyPolicies
{
    /// <summary>
    /// Returns a fresh list of the canonical platform default policies.
    /// Returns a new list on each call so callers can safely mutate.
    /// </summary>
    public static IList<JourneyPolicy> Build() => new List<JourneyPolicy>
    {
        BuildSignIn(),
        BuildSignUpSignIn(),
        BuildSignUp(),
        BuildPasswordReset(),
        BuildProfileEdit(),
        BuildMfaRequired(),
    };

    private static JourneyPolicy BuildSignIn() => new()
    {
        Id = "signin",
        Slug = "signin",
        Name = "Sign In",
        Description = "Default sign-in policy with optional MFA",
        Type = JourneyType.SignIn,
        Enabled = true,
        Priority = 100,
        Steps = new List<JourneyPolicyStep>
        {
            new()
            {
                Id = "login",
                Type = "local_login",
                DisplayName = "Sign In",
                Order = 1,
                Configuration = new Dictionary<string, object>
                {
                    ["allowRememberMe"] = true,
                    ["allowSelfRegistration"] = false
                }
            },
            new()
            {
                Id = "mfa",
                Type = "mfa",
                DisplayName = "Multi-Factor Authentication",
                Order = 2,
                Optional = true,
                Configuration = new Dictionary<string, object>
                {
                    ["required"] = false,
                    ["methods"] = new[] { "totp", "phone" }
                }
            },
            new()
            {
                Id = "consent",
                Type = "consent",
                DisplayName = "Consent",
                Order = 3
            }
        }
    };

    private static JourneyPolicy BuildSignUpSignIn() => new()
    {
        Id = "signup-signin",
        Slug = "signup-signin",
        Name = "Sign Up or Sign In",
        Description = "Combined sign-up and sign-in flow",
        Type = JourneyType.SignInSignUp,
        Enabled = true,
        Priority = 90,
        Steps = new List<JourneyPolicyStep>
        {
            new()
            {
                Id = "login",
                Type = "local_login",
                DisplayName = "Sign In or Sign Up",
                Order = 1,
                Configuration = new Dictionary<string, object>
                {
                    ["allowRememberMe"] = true,
                    ["allowSelfRegistration"] = true
                },
                Branches = new Dictionary<string, string>
                {
                    ["signup"] = "create_user"
                }
            },
            new()
            {
                Id = "create_user",
                Type = "create_user",
                DisplayName = "Create Account",
                Order = 2,
                Optional = true
            },
            new()
            {
                Id = "consent",
                Type = "consent",
                DisplayName = "Consent",
                Order = 3
            }
        }
    };

    private static JourneyPolicy BuildSignUp() => new()
    {
        Id = "signup",
        Slug = "signup",
        Name = "Sign Up",
        Description = "Self-registration flow",
        Type = JourneyType.SignUp,
        Enabled = true,
        Priority = 95,
        Steps = new List<JourneyPolicyStep>
        {
            new()
            {
                Id = "create_user",
                Type = "create_user",
                DisplayName = "Create Account",
                Order = 1
            },
            new()
            {
                Id = "consent",
                Type = "consent",
                DisplayName = "Consent",
                Order = 2
            }
        }
    };

    private static JourneyPolicy BuildPasswordReset() => new()
    {
        Id = "password-reset",
        Slug = "password-reset",
        Name = "Password Reset",
        Description = "Self-service password reset",
        Type = JourneyType.PasswordReset,
        Enabled = true,
        Priority = 100,
        Steps = new List<JourneyPolicyStep>
        {
            new()
            {
                Id = "reset",
                Type = "password_reset",
                DisplayName = "Reset Password",
                Order = 1
            }
        }
    };

    private static JourneyPolicy BuildProfileEdit() => new()
    {
        Id = "profile-edit",
        Slug = "profile-edit",
        Name = "Edit Profile",
        Description = "Update user profile information",
        Type = JourneyType.ProfileEdit,
        Enabled = true,
        Priority = 100,
        Steps = new List<JourneyPolicyStep>
        {
            new()
            {
                Id = "update",
                Type = "update_user",
                DisplayName = "Update Profile",
                Order = 1
            }
        }
    };

    private static JourneyPolicy BuildMfaRequired() => new()
    {
        Id = "signin-mfa",
        Slug = "signin-mfa",
        Name = "Sign In with MFA",
        Description = "Sign-in requiring multi-factor authentication",
        Type = JourneyType.SignIn,
        Enabled = true,
        Priority = 85,
        Conditions = new List<JourneyPolicyCondition>
        {
            new() { Type = "AcrValue", Operator = "contains", Value = "mfa" }
        },
        Steps = new List<JourneyPolicyStep>
        {
            new()
            {
                Id = "login",
                Type = "local_login",
                DisplayName = "Sign In",
                Order = 1
            },
            new()
            {
                Id = "mfa",
                Type = "mfa",
                DisplayName = "Multi-Factor Authentication",
                Order = 2,
                Configuration = new Dictionary<string, object>
                {
                    ["required"] = true
                }
            },
            new()
            {
                Id = "consent",
                Type = "consent",
                DisplayName = "Consent",
                Order = 3
            }
        }
    };
}

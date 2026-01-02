using System.Collections.Concurrent;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// In-memory implementation of journey policy store (for development/testing)
/// </summary>
public class InMemoryJourneyPolicyStore : IJourneyPolicyStore
{
    private readonly ConcurrentDictionary<string, JourneyPolicy> _policies = new();

    public InMemoryJourneyPolicyStore()
    {
        SeedDefaultPolicies();
    }

    private void SeedDefaultPolicies()
    {
        // Default sign-in policy
        var signIn = new JourneyPolicy
        {
            Id = "signin",
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

        // Sign-up/Sign-in combined policy
        var signUpSignIn = new JourneyPolicy
        {
            Id = "signup-signin",
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

        // Sign-up policy
        var signUp = new JourneyPolicy
        {
            Id = "signup",
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

        // Password reset policy
        var passwordReset = new JourneyPolicy
        {
            Id = "password-reset",
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

        // Profile edit policy
        var profileEdit = new JourneyPolicy
        {
            Id = "profile-edit",
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

        // MFA-required sign-in policy
        var mfaRequired = new JourneyPolicy
        {
            Id = "signin-mfa",
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

        _policies[signIn.Id] = signIn;
        _policies[signUpSignIn.Id] = signUpSignIn;
        _policies[signUp.Id] = signUp;
        _policies[passwordReset.Id] = passwordReset;
        _policies[profileEdit.Id] = profileEdit;
        _policies[mfaRequired.Id] = mfaRequired;
    }

    public Task<JourneyPolicy?> GetAsync(string policyId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(policyId, cancellationToken);
    }

    public Task<JourneyPolicy?> GetByIdAsync(string policyId, CancellationToken cancellationToken = default)
    {
        _policies.TryGetValue(policyId, out var policy);
        return Task.FromResult(policy);
    }

    public Task<JourneyPolicy?> GetByTypeAsync(JourneyType type, CancellationToken cancellationToken = default)
    {
        var policy = _policies.Values
            .Where(p => p.Enabled && p.Type == type)
            .OrderByDescending(p => p.Priority)
            .FirstOrDefault();

        return Task.FromResult(policy);
    }

    public Task<IEnumerable<JourneyPolicy>> GetByTenantAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        var policies = _policies.Values
            .Where(p => p.TenantId == tenantId || p.TenantId == null)
            .OrderByDescending(p => p.Priority);

        return Task.FromResult<IEnumerable<JourneyPolicy>>(policies);
    }

    public Task<JourneyPolicy?> FindMatchingAsync(JourneyPolicyMatchContext context, CancellationToken cancellationToken = default)
    {
        var policies = _policies.Values
            .Where(p => p.Enabled)
            .Where(p => p.TenantId == context.TenantId || p.TenantId == null)
            .Where(p => p.Type == context.Type || p.Type == JourneyType.Custom)
            .OrderByDescending(p => p.TenantId != null) // Prefer tenant-specific
            .ThenByDescending(p => p.Priority)
            .ToList();

        foreach (var policy in policies)
        {
            if (MatchesConditions(policy, context))
            {
                return Task.FromResult<JourneyPolicy?>(policy);
            }
        }

        // Return first matching type as fallback
        return Task.FromResult(policies.FirstOrDefault());
    }

    private static bool MatchesConditions(JourneyPolicy policy, JourneyPolicyMatchContext context)
    {
        if (policy.Conditions == null || policy.Conditions.Count == 0)
        {
            return true;
        }

        foreach (var condition in policy.Conditions)
        {
            string? value = condition.Type switch
            {
                "ClientId" => context.ClientId,
                "Scope" => context.Scopes != null ? string.Join(" ", context.Scopes) : null,
                "AcrValue" => context.AcrValues,
                _ => context.AdditionalParameters?.TryGetValue(condition.Type, out var v) == true ? v : null
            };

            var matches = condition.Operator switch
            {
                "equals" => value == condition.Value,
                "contains" => value?.Contains(condition.Value) ?? false,
                "startsWith" => value?.StartsWith(condition.Value) ?? false,
                "endsWith" => value?.EndsWith(condition.Value) ?? false,
                "matches" => !string.IsNullOrEmpty(value) &&
                    System.Text.RegularExpressions.Regex.IsMatch(value, condition.Value),
                _ => value == condition.Value
            };

            if (!matches) return false;
        }

        return true;
    }

    public Task SaveAsync(JourneyPolicy policy, CancellationToken cancellationToken = default)
    {
        policy.UpdatedAt = DateTime.UtcNow;
        _policies[policy.Id] = policy;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string policyId, CancellationToken cancellationToken = default)
    {
        _policies.TryRemove(policyId, out _);
        return Task.CompletedTask;
    }
}

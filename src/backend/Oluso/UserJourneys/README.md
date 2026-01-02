# User Journey Engine

The User Journey Engine provides a flexible, policy-driven authentication flow system that allows you to define custom login, signup, and account management experiences.

## Quick Start

```csharp
services.AddOluso()
    .AddUserJourneys(journeys =>
    {
        // Add built-in step handlers
        journeys.AddBuiltInSteps();    // All UI steps
        journeys.AddLogicSteps();       // All logic steps

        // Or add specific handlers
        journeys.AddLocalLogin();
        journeys.AddMfa();
        journeys.AddPasswordlessEmail();
    });
```

## Table of Contents

1. [Concepts](#concepts)
2. [Built-in Step Handlers](#built-in-step-handlers)
3. [Creating Custom Step Handlers](#creating-custom-step-handlers)
4. [Implementing Required Services](#implementing-required-services)
5. [Journey Policies](#journey-policies)
6. [State Management](#state-management)
7. [Extending Built-in Handlers](#extending-built-in-handlers)

---

## Concepts

### Journey Policy
A JSON configuration that defines the sequence of steps in an authentication flow.

### Step Handler
A class that implements `IStepHandler` and handles a specific step type (e.g., `local_login`, `mfa`, `captcha`).

### Step Execution Context
Contains the current journey state, user input, and services needed to execute a step.

### Step Handler Result
The outcome of executing a step: success, failure, show UI, redirect, or branch to another step.

---

## Built-in Step Handlers

### UI Steps (require user interaction)

| Step Type | Handler | Description |
|-----------|---------|-------------|
| `local_login` | `LocalLoginStepHandler` | Username/password authentication |
| `sign_up` | `SignUpStepHandler` | User registration |
| `mfa` | `MfaStepHandler` | Multi-factor authentication (TOTP, SMS, Email) |
| `consent` | `ConsentStepHandler` | OAuth consent screen |
| `password_reset` | `PasswordResetStepHandler` | Password reset flow |
| `external_login` | `ExternalLoginStepHandler` | OAuth/OIDC social login |
| `passwordless_email` | `PasswordlessEmailStepHandler` | Email OTP or magic link |
| `passwordless_sms` | `PasswordlessSmsStepHandler` | SMS OTP |
| `dynamic_form` | `DynamicFormStepHandler` | Claims collection forms |
| `link_account` | `LinkAccountStepHandler` | Link external providers |
| `captcha` | `CaptchaStepHandler` | Bot protection |

### Logic Steps (no UI)

| Step Type | Handler | Description |
|-----------|---------|-------------|
| `condition` | `ConditionStepHandler` | Conditional branching |
| `branch` | `BranchStepHandler` | Multi-path routing |
| `transform` | `TransformStepHandler` | Data transformation |
| `api_call` | `ApiCallStepHandler` | External API integration |
| `webhook` | `WebhookStepHandler` | Event notifications |

---

## Creating Custom Step Handlers

### Basic Structure

```csharp
using Oluso.Core.UserJourneys;

public class MyCustomStepHandler : IStepHandler
{
    // Unique identifier for this step type
    public string StepType => "my_custom_step";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Get services from DI
        var logger = context.ServiceProvider.GetRequiredService<ILogger<MyCustomStepHandler>>();

        // Read configuration from policy
        var timeout = context.GetConfig("timeout", 30);
        var requiredField = context.GetConfig<string>("requiredField");

        // Read user input from form submission
        var userInput = context.GetInput("fieldName");

        // Access journey data (shared across steps)
        var previousValue = context.GetData<string>("some_key");

        // Store data for later steps
        context.SetData("my_key", "my_value");

        // Return appropriate result
        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["claim_name"] = "claim_value"
        });
    }
}
```

### Registration

```csharp
services.AddOluso()
    .AddUserJourneys(journeys =>
    {
        // Register by type
        journeys.AddStepHandler<MyCustomStepHandler>();

        // Or register an instance
        journeys.AddStepHandler(new MyCustomStepHandler(options));
    });
```

### Step Handler Results

```csharp
// Success - proceed to next step with output claims
return StepHandlerResult.Success(new Dictionary<string, object>
{
    ["sub"] = userId,
    ["email"] = email
});

// Fail - stop the journey with an error
return StepHandlerResult.Fail("error_code", "Human-readable message");

// Show UI - render a Razor view
return StepHandlerResult.ShowUi("Journey/_MyView", new MyViewModel
{
    Message = "Please enter your information"
});

// Redirect - send user to external URL
return StepHandlerResult.Redirect("https://external-provider.com/auth");

// Skip - skip this step and continue
return StepHandlerResult.Skip();

// Branch - jump to a different step
return StepHandlerResult.Branch("step_id_to_jump_to");
```

### Example: Custom Verification Step

```csharp
public class PhoneVerificationStepHandler : IStepHandler
{
    public string StepType => "phone_verification";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var smsService = context.ServiceProvider.GetService<ISmsService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PhoneVerificationStepHandler>>();

        // Configuration
        var codeLength = context.GetConfig("codeLength", 6);
        var expirationMinutes = context.GetConfig("expirationMinutes", 10);

        // Handle phone submission
        var phone = context.GetInput("phone");
        if (!string.IsNullOrEmpty(phone))
        {
            var code = GenerateCode(codeLength);

            context.SetData("verification_phone", phone);
            context.SetData("verification_code", code);
            context.SetData("verification_expires", DateTime.UtcNow.AddMinutes(expirationMinutes));

            if (smsService != null)
            {
                await smsService.SendAsync(phone, $"Your code: {code}", cancellationToken);
            }

            return StepHandlerResult.ShowUi("Journey/_PhoneVerify", new PhoneVerifyViewModel
            {
                MaskedPhone = MaskPhone(phone),
                ExpirationMinutes = expirationMinutes
            });
        }

        // Handle code verification
        var submittedCode = context.GetInput("code");
        if (!string.IsNullOrEmpty(submittedCode))
        {
            var storedCode = context.GetData<string>("verification_code");
            var expires = context.GetData<DateTime>("verification_expires");

            if (DateTime.UtcNow > expires)
            {
                return StepHandlerResult.ShowUi("Journey/_PhoneVerify", new PhoneVerifyViewModel
                {
                    ErrorMessage = "Code expired"
                });
            }

            if (storedCode != submittedCode)
            {
                return StepHandlerResult.ShowUi("Journey/_PhoneVerify", new PhoneVerifyViewModel
                {
                    ErrorMessage = "Invalid code"
                });
            }

            var phone = context.GetData<string>("verification_phone");
            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["phone_number"] = phone,
                ["phone_number_verified"] = true
            });
        }

        // Initial state
        return StepHandlerResult.ShowUi("Journey/_PhoneInput", new PhoneInputViewModel());
    }

    private static string GenerateCode(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => Random.Shared.Next(0, 10)));

    private static string MaskPhone(string phone) =>
        phone.Length > 4 ? "***" + phone[^4..] : phone;
}
```

---

## Implementing Required Services

The step handlers depend on several services that you must implement:

### IEmailService (for passwordless email)

```csharp
public class SmtpEmailService : IEmailService
{
    private readonly SmtpClient _client;

    public async Task<MessageSendResult> SendAsync(
        string to, string subject, string htmlBody,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new MailMessage("noreply@example.com", to, subject, htmlBody)
            {
                IsBodyHtml = true
            };
            await _client.SendMailAsync(message, cancellationToken);
            return MessageSendResult.Succeeded();
        }
        catch (Exception ex)
        {
            return MessageSendResult.Failed(ex.Message);
        }
    }

    public Task<MessageSendResult> SendAsync(
        string to, string subject, string htmlBody, string? textBody,
        CancellationToken cancellationToken = default)
    {
        // Implementation with text fallback
        return SendAsync(to, subject, htmlBody, cancellationToken);
    }

    public Task<MessageSendResult> SendTemplatedAsync(
        string to, string templateId, IDictionary<string, object> templateData,
        CancellationToken cancellationToken = default)
    {
        // Template-based sending (e.g., SendGrid, Mailchimp)
        throw new NotImplementedException();
    }
}

// Register
services.AddSingleton<IEmailService, SmtpEmailService>();
```

### ISmsService (for passwordless SMS)

```csharp
public class TwilioSmsService : ISmsService
{
    private readonly TwilioRestClient _client;
    private readonly string _fromNumber;

    public async Task<MessageSendResult> SendAsync(
        string phoneNumber, string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await MessageResource.CreateAsync(
                to: new PhoneNumber(phoneNumber),
                from: new PhoneNumber(_fromNumber),
                body: message);

            return MessageSendResult.Succeeded(result.Sid);
        }
        catch (Exception ex)
        {
            return MessageSendResult.Failed(ex.Message);
        }
    }
}

// Register
services.AddSingleton<ISmsService, TwilioSmsService>();
```

### IExternalAuthService (for external login and account linking)

```csharp
public class IdentityExternalAuthService : IExternalAuthService
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public async Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes.Select(s => new ExternalProviderInfo
        {
            Scheme = s.Name,
            DisplayName = s.DisplayName
        }).ToList();
    }

    public async Task<IList<ExternalLoginInfo>> GetUserLoginsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return new List<ExternalLoginInfo>();

        var logins = await _userManager.GetLoginsAsync(user);
        return logins.Select(l => new ExternalLoginInfo
        {
            Provider = l.LoginProvider,
            ProviderKey = l.ProviderKey,
            DisplayName = l.ProviderDisplayName
        }).ToList();
    }

    public async Task<string?> FindUserByLoginAsync(
        string provider, string providerKey,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByLoginAsync(provider, providerKey);
        return user?.Id;
    }

    public async Task<ExternalLoginOperationResult> LinkLoginAsync(
        string userId, string provider, string providerKey, string? displayName,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return ExternalLoginOperationResult.Failed("User not found");

        var info = new UserLoginInfo(provider, providerKey, displayName);
        var result = await _userManager.AddLoginAsync(user, info);

        return result.Succeeded
            ? ExternalLoginOperationResult.Success()
            : ExternalLoginOperationResult.Failed(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    public async Task<ExternalLoginOperationResult> UnlinkLoginAsync(
        string userId, string provider, string providerKey,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return ExternalLoginOperationResult.Failed("User not found");

        var result = await _userManager.RemoveLoginAsync(user, provider, providerKey);

        return result.Succeeded
            ? ExternalLoginOperationResult.Success()
            : ExternalLoginOperationResult.Failed(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    // ... implement other methods
}
```

---

## Journey Policies

### Policy Structure

```json
{
  "id": "login",
  "name": "Login Journey",
  "description": "Standard login with optional MFA",
  "steps": [
    {
      "id": "captcha",
      "type": "captcha",
      "config": {
        "provider": "recaptcha_v3",
        "siteKey": "your-site-key",
        "scoreThreshold": 0.5
      }
    },
    {
      "id": "login",
      "type": "local_login",
      "config": {
        "allowRememberMe": true,
        "showForgotPassword": true
      }
    },
    {
      "id": "check_mfa",
      "type": "condition",
      "config": {
        "expression": "user.mfa_enabled == true",
        "trueStep": "mfa",
        "falseStep": null
      }
    },
    {
      "id": "mfa",
      "type": "mfa",
      "config": {
        "allowedMethods": ["totp", "sms", "email"]
      }
    }
  ],
  "uiConfig": {
    "title": "Sign In",
    "logoUrl": "/images/logo.png",
    "primaryColor": "#4F46E5"
  }
}
```

### Storing Policies

```csharp
// Use the built-in in-memory store
services.AddOluso()
    .AddUserJourneys(j => j.AddBuiltInSteps());

// Or implement a database-backed store
public class DatabaseJourneyPolicyStore : IJourneyPolicyStore
{
    private readonly AppDbContext _db;

    public async Task<JourneyPolicy?> GetPolicyAsync(
        string policyId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.JourneyPolicies.FindAsync(policyId);
        return entity?.ToPolicy();
    }

    public async Task SavePolicyAsync(
        JourneyPolicy policy, CancellationToken cancellationToken = default)
    {
        var entity = JourneyPolicyEntity.FromPolicy(policy);
        _db.JourneyPolicies.Update(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePolicyAsync(
        string policyId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.JourneyPolicies.FindAsync(policyId);
        if (entity != null)
        {
            _db.JourneyPolicies.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<JourneyPolicy>> GetAllPoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.JourneyPolicies
            .Select(e => e.ToPolicy())
            .ToListAsync(cancellationToken);
    }
}

// Register
services.AddOluso()
    .AddUserJourneys(j =>
    {
        j.AddBuiltInSteps();
        j.UsePolicyStore<DatabaseJourneyPolicyStore>();
    });
```

---

## State Management

### Custom State Store

For distributed environments, use Redis or another distributed cache:

```csharp
// Option 1: Use built-in distributed cache support
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

services.AddOluso()
    .AddUserJourneys(j =>
    {
        j.AddBuiltInSteps();
        j.UseDistributedCache(); // Uses IDistributedCache
    });

// Option 2: Implement custom store
public class RedisJourneyStateStore : IJourneyStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _expiration = TimeSpan.FromHours(1);

    public async Task<JourneyState?> GetStateAsync(
        string journeyId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var data = await db.StringGetAsync($"journey:{journeyId}");

        return data.HasValue
            ? JsonSerializer.Deserialize<JourneyState>(data!)
            : null;
    }

    public async Task SaveStateAsync(
        JourneyState state, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(state);
        await db.StringSetAsync($"journey:{state.JourneyId}", json, _expiration);
    }

    public async Task DeleteStateAsync(
        string journeyId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"journey:{journeyId}");
    }
}

// Register
services.AddOluso()
    .AddUserJourneys(j =>
    {
        j.AddBuiltInSteps();
        j.UseStateStore<RedisJourneyStateStore>();
    });
```

---

## Extending Built-in Handlers

### Override Behavior with a Wrapper

```csharp
public class CustomLocalLoginHandler : IStepHandler
{
    private readonly LocalLoginStepHandler _inner = new();
    private readonly IAuditService _audit;

    public string StepType => "local_login"; // Same type to override

    public CustomLocalLoginHandler(IAuditService audit)
    {
        _audit = audit;
    }

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Pre-processing
        var ip = context.GetData<string>("client_ip");

        // Call the built-in handler
        var result = await _inner.ExecuteAsync(context, cancellationToken);

        // Post-processing
        if (result.IsSuccess)
        {
            await _audit.LogLoginAsync(context.UserId, ip, "success");
        }
        else if (result.Error != null)
        {
            await _audit.LogLoginAsync(null, ip, $"failed: {result.Error}");
        }

        return result;
    }
}

// Register your handler AFTER AddBuiltInSteps to override
services.AddOluso()
    .AddUserJourneys(j =>
    {
        j.AddBuiltInSteps();
        j.AddStepHandler<CustomLocalLoginHandler>(); // Overrides built-in
    });
```

### Add Pre/Post Processing with Decorators

```csharp
public class LoggingStepHandlerDecorator : IStepHandler
{
    private readonly IStepHandler _inner;
    private readonly ILogger _logger;

    public string StepType => _inner.StepType;

    public LoggingStepHandlerDecorator(IStepHandler inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing step {StepType} for journey {JourneyId}",
            StepType, context.JourneyId);

        var sw = Stopwatch.StartNew();
        var result = await _inner.ExecuteAsync(context, cancellationToken);
        sw.Stop();

        _logger.LogInformation("Step {StepType} completed in {Elapsed}ms with result {Result}",
            StepType, sw.ElapsedMilliseconds, result.Type);

        return result;
    }
}
```

### Custom Views

Override built-in views by placing your own in the application's Views folder:

```
YourApp/
  Views/
    Journey/
      _LocalLogin.cshtml      <- Overrides Oluso.UI's version
      _MfaVerify.cshtml       <- Overrides Oluso.UI's version
```

Or use a custom view name in the policy:

```json
{
  "id": "login",
  "type": "local_login",
  "config": {
    "viewName": "CustomViews/_BrandedLogin"
  }
}
```

---

## Complete Example: Multi-tenant Login

```csharp
// Custom handler that resolves tenant before login
public class TenantAwareLoginHandler : IStepHandler
{
    public string StepType => "tenant_login";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var tenantService = context.ServiceProvider.GetRequiredService<ITenantService>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();

        // Step 1: Tenant selection
        var tenantId = context.GetInput("tenant");
        if (string.IsNullOrEmpty(tenantId))
        {
            var tenants = await tenantService.GetAllTenantsAsync(cancellationToken);
            return StepHandlerResult.ShowUi("Journey/_TenantSelect", new TenantSelectViewModel
            {
                Tenants = tenants
            });
        }

        // Store selected tenant
        context.SetData("tenant_id", tenantId);

        // Step 2: Login with tenant context
        var username = context.GetInput("username");
        var password = context.GetInput("password");

        if (string.IsNullOrEmpty(username))
        {
            var tenant = await tenantService.GetTenantAsync(tenantId, cancellationToken);
            return StepHandlerResult.ShowUi("Journey/_TenantLogin", new TenantLoginViewModel
            {
                TenantName = tenant.Name,
                TenantLogo = tenant.LogoUrl
            });
        }

        // Validate credentials with tenant scope
        using (tenantService.SetCurrentTenant(tenantId))
        {
            var result = await userService.ValidateCredentialsAsync(username, password, cancellationToken);

            if (!result.Succeeded)
            {
                return StepHandlerResult.ShowUi("Journey/_TenantLogin", new TenantLoginViewModel
                {
                    TenantName = (await tenantService.GetTenantAsync(tenantId, cancellationToken)).Name,
                    ErrorMessage = result.ErrorMessage
                });
            }

            context.UserId = result.User!.Id;
            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["sub"] = result.User.Id,
                ["tenant_id"] = tenantId
            });
        }
    }
}
```

---

## Best Practices

1. **Keep handlers stateless** - All state should go through `context.SetData/GetData`
2. **Use configuration** - Make handlers configurable via policy JSON
3. **Handle errors gracefully** - Return `StepHandlerResult.Fail` with meaningful errors
4. **Log appropriately** - Use structured logging with correlation IDs
5. **Validate input** - Never trust user input; validate on the server
6. **Use async/await** - All I/O operations should be async
7. **Consider localization** - Support multiple languages in views
8. **Test thoroughly** - Write unit tests for custom handlers

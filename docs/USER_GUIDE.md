# Identity Server User Guide

This guide covers the configuration and usage of the Identity Server's features including multi-tenancy, user journeys, authentication methods, and extensibility via WASM plugins.

---

## Table of Contents

1. [Multi-Tenancy](#multi-tenancy)
2. [Authentication Flows](#authentication-flows)
3. [User Journeys](#user-journeys)
4. [Journey Step Types Reference](#journey-step-types-reference)
5. [FIDO2/WebAuthn (Passkeys)](#fido2webauthn-passkeys)
6. [Multi-Factor Authentication (MFA)](#multi-factor-authentication-mfa)
7. [Self-Service Registration](#self-service-registration)
8. [External Identity Providers](#external-identity-providers)
9. [WASM Plugins](#wasm-plugins)
10. [Client Configuration](#client-configuration)
11. [Signing Keys](#signing-keys)
12. [Events and Webhooks](#events-and-webhooks)
13. [Audit Logging](#audit-logging)
14. [Custom Styling](#custom-styling)
15. [Platform Billing](#platform-billing)
16. [Subscription Plans](#subscription-plans)
17. [OIDC Extensions](#oidc-extensions)
18. [DPoP (Demonstrating Proof of Possession)](#dpop-demonstrating-proof-of-possession)
19. [Pushed Authorization Requests (PAR)](#pushed-authorization-requests-par)

---

## Multi-Tenancy

The Identity Server supports multi-tenant deployments where each tenant has isolated users, clients, and configuration.

### Tenant Resolution

Tenants are resolved via:
- **Subdomain**: `tenant1.auth.example.com`
- **Path**: `auth.example.com/tenant1`
- **Header**: `X-Tenant-Id: tenant1`

### Tenant Settings

Each tenant can configure:

| Setting | Description | Default |
|---------|-------------|---------|
| `UseJourneyFlow` | Use journey-based authentication vs standalone pages | `true` |
| `AllowSelfRegistration` | Allow users to register themselves | `true` |
| `RequireTermsAcceptance` | Require ToS acceptance during registration | `false` |
| `TermsOfServiceUrl` | URL to terms of service document | - |
| `PrivacyPolicyUrl` | URL to privacy policy document | - |
| `RequireEmailVerification` | Require email verification before sign-in | `true` |
| `AllowedEmailDomains` | Comma-separated list of allowed email domains | - (all allowed) |

### Admin UI Configuration

Navigate to **Settings** > **User Registration Settings** to configure tenant-level registration options.

---

## Authentication Flows

The server supports two authentication flow modes:

### Journey-Based Flow (Recommended)

Step-by-step authentication with full control over the user experience. Journeys are defined as policies with ordered steps.

**Enable**: Set `UseJourneyFlow = true` at tenant or client level.

### Standalone Pages

Traditional login/registration pages. Useful for simple deployments.

**Enable**: Set `UseJourneyFlow = false` at tenant or client level.

### Priority Order

Flow selection follows this priority:
1. Request `ui_mode` parameter (`journey` or `standalone`)
2. Client `UseJourneyFlow` setting
3. Tenant `UseJourneyFlow` setting
4. Default: `true` (journey flow)

---

## User Journeys

User Journeys are customizable authentication flows composed of steps.

### Creating a Journey

1. Navigate to **Journeys** in Admin UI
2. Click **Create New Journey**
3. Configure:
   - **Name**: Display name for the journey
   - **Type**: `SignIn`, `SignUp`, `SignInSignUp`, `PasswordReset`, `ProfileEdit`, or `Custom`
   - **Priority**: Higher priority journeys are selected first when multiple match

### Available Step Types

#### Authentication Steps
| Step Type | Description |
|-----------|-------------|
| `LocalLogin` | Username/password authentication |
| `ExternalIdp` | OAuth/OIDC external provider |
| `Mfa` | Multi-factor authentication |
| `PasswordlessEmail` | Email magic link |
| `PasswordlessSms` | SMS OTP |
| `WebAuthn` | FIDO2/Passkey authentication |
| `Ldap` | LDAP directory authentication |
| `Saml` | SAML federation |

#### User Interaction Steps
| Step Type | Description |
|-----------|-------------|
| `Consent` | OAuth consent screen |
| `ClaimsCollection` | Collect additional user information |
| `TermsAcceptance` | Terms of service acceptance |
| `CaptchaVerification` | Bot protection |

#### Logic Steps
| Step Type | Description |
|-----------|-------------|
| `Condition` | Conditional branching |
| `Branch` | Multi-way branching |
| `Transform` | Claim transformation |
| `ApiCall` | Call external API |
| `Webhook` | Send webhook notification |

#### Account Management Steps
| Step Type | Description |
|-----------|-------------|
| `CreateUser` | Create new user account |
| `UpdateUser` | Update user profile |
| `LinkAccount` | Link external identity |
| `PasswordChange` | Change password |
| `PasswordReset` | Reset forgotten password |

#### Custom Steps
| Step Type | Description |
|-----------|-------------|
| `CustomPlugin` | Execute WASM plugin |
| `CustomPage` | Render custom HTML page |

### Step Configuration Example

```json
{
  "id": "login-step",
  "type": "LocalLogin",
  "displayName": "Sign In",
  "configuration": {
    "allowRememberMe": true,
    "allowSelfRegistration": true
  },
  "onSuccess": "mfa-step",
  "onFailure": null
}
```

### Flow Control

Each step can specify:
- `onSuccess`: Step ID to jump to on success (default: next step)
- `onFailure`: Step ID to jump to on failure (default: fail journey)
- `optional`: If true, continue journey even if step fails
- `conditions`: Conditions that must be met to execute step

---

## Journey Step Types Reference

This section provides detailed configuration options for each step type available in the Admin UI Journey Builder.

### LocalLogin Step

Authenticate users with username/email and password.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `allowRememberMe` | boolean | `true` | Show "Remember me" checkbox |
| `allowSelfRegistration` | boolean | `false` | Show link to registration page |
| `loginHintClaim` | string | - | Claim to use as login hint (pre-fill username) |

**Admin UI Location**: Select "Local Login" from step types dropdown.

**Example**:
```json
{
  "type": "LocalLogin",
  "configuration": {
    "allowRememberMe": true,
    "allowSelfRegistration": true,
    "loginHintClaim": "email"
  }
}
```

---

### ExternalIdp Step

Authenticate via external identity providers (Google, Microsoft, etc.).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `providers` | string[] | - | Allowed identity provider schemes |
| `autoProvision` | boolean | `true` | Auto-create user on first login |
| `autoRedirect` | boolean | `false` | Auto-redirect if only one provider |
| `claimMappings` | object | - | Map external claims to internal claims |

**Admin UI Location**: Select "External IdP" from step types dropdown.

**Example**:
```json
{
  "type": "ExternalIdp",
  "configuration": {
    "providers": ["Google", "Microsoft"],
    "autoProvision": true,
    "autoRedirect": false,
    "claimMappings": {
      "email": "email",
      "name": "name",
      "picture": "profile_picture"
    }
  }
}
```

---

### Mfa Step

Require multi-factor authentication (TOTP, SMS, Email, WebAuthn).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `required` | boolean | `false` | Always require MFA (vs. only if enrolled) |
| `methods` | string[] | `["totp", "phone", "email"]` | Allowed MFA methods |
| `allowSetup` | boolean | `true` | Allow users to set up MFA during flow |
| `rememberDevice` | boolean | `true` | Remember trusted devices |
| `rememberDeviceDays` | number | `30` | Days to remember device |

**Admin UI Location**: Select "MFA" from step types dropdown.

**Example**:
```json
{
  "type": "Mfa",
  "configuration": {
    "required": true,
    "methods": ["totp", "webauthn"],
    "rememberDevice": true,
    "rememberDeviceDays": 14
  }
}
```

---

### Consent Step

Show OAuth consent screen for scope approval.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `allowRemember` | boolean | `true` | Allow user to remember consent |
| `rememberDays` | number | `365` | Days to remember consent |
| `showResourceScopes` | boolean | `true` | Show API resource scopes |
| `showIdentityScopes` | boolean | `true` | Show identity scopes |

**Admin UI Location**: Select "Consent" from step types dropdown.

**Example**:
```json
{
  "type": "Consent",
  "configuration": {
    "allowRemember": true,
    "rememberDays": 90,
    "showResourceScopes": true,
    "showIdentityScopes": true
  }
}
```

---

### ClaimsCollection Step

Collect custom user information with configurable form fields.

#### Basic Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `title` | string | - | Form title displayed to user |
| `description` | string | - | Form description/instructions |
| `submitButtonText` | string | `"Continue"` | Submit button label |
| `cancelButtonText` | string | - | Cancel button label |
| `allowCancel` | boolean | `false` | Show cancel button |
| `viewName` | string | `"Journey/_ClaimsCollection"` | Custom view name |
| `localizedTitles` | object | - | Title translations by culture code |
| `localizedDescriptions` | object | - | Description translations by culture code |

#### Field Configuration

Each field in the `fields` array supports:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `name` | string | **required** | Field name (form input name) |
| `type` | string | **required** | `text`, `email`, `password`, `number`, `date`, `tel`, `url`, `textarea`, `select`, `radio`, `checkbox` |
| `label` | string | - | Field label displayed to user |
| `placeholder` | string | - | Input placeholder text |
| `description` | string | - | Help text below field |
| `required` | boolean | `false` | Field is required |
| `claimType` | string | - | Claim type to store value (defaults to name) |
| `defaultValue` | string | - | Default value |
| `pattern` | string | - | Regex validation pattern |
| `patternError` | string | - | Error message for pattern mismatch |
| `minLength` | number | - | Minimum character length |
| `maxLength` | number | - | Maximum character length |
| `min` | string | - | Minimum value (for number/date) |
| `max` | string | - | Maximum value (for number/date) |
| `rows` | number | `3` | Rows for textarea |
| `readOnly` | boolean | `false` | Field is read-only |
| `hidden` | boolean | `false` | Field is hidden |
| `group` | string | - | Group name for fieldset grouping |
| `options` | array | - | Options for select/radio fields |

#### Select/Radio Options

Each option in `options` array:

| Property | Type | Description |
|----------|------|-------------|
| `value` | string | Option value |
| `label` | string | Option display label |
| `localizedLabels` | object | Label translations by culture |

#### Conditional Visibility

Use `showWhen` to conditionally show fields:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `field` | string | **required** | Field name to check |
| `operator` | string | `"equals"` | `equals`, `notEquals`, `contains`, `notEmpty`, `empty` |
| `value` | string | - | Value to compare |

**Admin UI Location**: Select "Claims Collection" from step types dropdown. The Admin UI provides a visual field builder.

**Example**:
```json
{
  "type": "ClaimsCollection",
  "configuration": {
    "title": "Additional Information",
    "description": "Please provide the following details",
    "submitButtonText": "Save & Continue",
    "fields": [
      {
        "name": "department",
        "type": "select",
        "label": "Department",
        "required": true,
        "options": [
          { "value": "engineering", "label": "Engineering" },
          { "value": "sales", "label": "Sales" },
          { "value": "hr", "label": "Human Resources" }
        ]
      },
      {
        "name": "manager_email",
        "type": "email",
        "label": "Manager Email",
        "placeholder": "manager@company.com",
        "showWhen": {
          "field": "department",
          "operator": "notEmpty"
        }
      },
      {
        "name": "phone",
        "type": "tel",
        "label": "Phone Number",
        "pattern": "^\\+?[0-9]{10,14}$",
        "patternError": "Please enter a valid phone number"
      }
    ]
  }
}
```

---

### CaptchaVerification Step

Verify user is human via CAPTCHA. Supports reCAPTCHA, hCaptcha, and Cloudflare Turnstile.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `provider` | string | `"recaptcha"` | `recaptcha`, `hcaptcha`, `cloudflare` |
| `siteKey` | string | **required** | CAPTCHA site key from provider |
| `secretKey` | string | **required** | CAPTCHA secret key from provider |
| `scoreThreshold` | number | `0.5` | Minimum score for reCAPTCHA v3 (0.0-1.0) |
| `theme` | string | `"light"` | `light` or `dark` |
| `size` | string | `"normal"` | `normal`, `compact`, or `invisible` |
| `language` | string | - | Language code (e.g., `en`, `es`, `fr`) |

**Admin UI Location**: Select "CAPTCHA" from step types dropdown.

**Example**:
```json
{
  "type": "CaptchaVerification",
  "configuration": {
    "provider": "recaptcha",
    "siteKey": "6LcX...",
    "secretKey": "6LcX...",
    "scoreThreshold": 0.7,
    "theme": "light"
  }
}
```

---

### ApiCall Step

Call external APIs for validation, enrichment, or integration. The Admin UI provides a comprehensive editor with collapsible sections.

#### Basic Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `url` | string | **required** | API endpoint URL |
| `method` | string | `"GET"` | `GET`, `POST`, `PUT`, `PATCH`, `DELETE` |
| `timeout` | number | `30` | Request timeout in seconds |

**URL Placeholders**: Use `{claim:name}`, `{state:userId}`, `{input:field}` for dynamic values.

#### Headers

| Property | Type | Description |
|----------|------|-------------|
| `headers` | object | Key-value pairs of HTTP headers (supports placeholders) |

#### Authentication

| Property | Type | Description |
|----------|------|-------------|
| `authentication.type` | string | `none`, `bearer`, `basic`, `apikey` |
| `authentication.token` | string | Bearer token (supports `{claim:access_token}`) |
| `authentication.username` | string | Basic auth username |
| `authentication.password` | string | Basic auth password |
| `authentication.apiKey` | string | API key value |
| `authentication.headerName` | string | Header name for API key (default: `X-API-Key`) |

#### Retry & Error Handling

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `retryCount` | number | `0` | Number of retry attempts |
| `retryDelay` | number | `1000` | Delay between retries (ms) |
| `failOnError` | boolean | `true` | Fail journey on API error |
| `continueOnStatus` | number[] | - | HTTP status codes to continue on (e.g., `[404, 409]`) |

#### Request Body

| Property | Type | Description |
|----------|------|-------------|
| `bodyTemplate` | string | JSON template with placeholders |
| `bodyFromClaims` | string[] | Claims to include in request body |

#### Input Mapping (Advanced)

Map claims/state/input to request body fields with transforms:

| Property | Type | Description |
|----------|------|-------------|
| `inputMapping[field].from` | string | Source name |
| `inputMapping[field].source` | string | `claim`, `state`, `input`, `constant` |
| `inputMapping[field].value` | string | Constant value (when source=constant) |
| `inputMapping[field].transform` | string | `uppercase`, `lowercase`, `trim`, `base64encode`, `base64decode`, `urlencode`, `urldecode` |
| `inputMapping[field].defaultValue` | string | Default if source empty |

#### Output Mapping

| Property | Type | Description |
|----------|------|-------------|
| `outputMapping` | object | Map JSON paths to claim types (e.g., `{"user.id": "external_id"}`) |
| `includeResponseMeta` | boolean | Add `_api_status` and `_api_success` claims |

#### Response Validation

Array of validation rules:

| Property | Type | Description |
|----------|------|-------------|
| `path` | string | JSON path to validate |
| `type` | string | `required`, `equals`, `notequals`, `contains`, `matches`, `in`, `notin` |
| `expectedValue` | string | Value for equals/contains |
| `pattern` | string | Regex for matches |
| `allowedValues` | string[] | Values for `in` validation |
| `errorMessage` | string | Custom error message |

#### Branch on Response

Array of branching rules:

| Property | Type | Description |
|----------|------|-------------|
| `onStatus` | number[] | HTTP status codes to match |
| `path` | string | JSON path to check |
| `condition` | string | `equals`, `notequals`, `exists`, `notexists`, `contains`, `true`, `false` |
| `value` | string | Value to compare |
| `branchTo` | string | Step ID to branch to |

**Admin UI Location**: Select "API Call" from step types dropdown. The specialized editor provides sections for Basic Settings, Headers, Authentication, Retry & Error Handling, Request Body, and Output Mapping.

**Example**:
```json
{
  "type": "ApiCall",
  "configuration": {
    "url": "https://api.example.com/users/{claim:sub}/profile",
    "method": "POST",
    "timeout": 15,
    "headers": {
      "X-Request-ID": "{state:journeyId}",
      "Content-Type": "application/json"
    },
    "authentication": {
      "type": "bearer",
      "token": "{claim:access_token}"
    },
    "retryCount": 2,
    "retryDelay": 1000,
    "failOnError": false,
    "continueOnStatus": [404],
    "bodyTemplate": "{ \"email\": \"{claim:email}\", \"action\": \"login\" }",
    "outputMapping": {
      "user.tier": "subscription_tier",
      "user.flags": "feature_flags"
    },
    "responseValidation": [
      {
        "path": "status",
        "type": "equals",
        "expectedValue": "active",
        "errorMessage": "Account is not active"
      }
    ],
    "branchOnResponse": [
      {
        "path": "requires_mfa",
        "condition": "true",
        "branchTo": "mfa-step"
      }
    ]
  }
}
```

---

### Condition Step

Evaluate conditions and branch flow based on claims, context, or previous step results.

#### Conditions Array

| Property | Type | Description |
|----------|------|-------------|
| `source` | string | `claim`, `context`, `previous_step` |
| `key` | string | Key/claim name to check |
| `operator` | string | `equals`, `notEquals`, `contains`, `exists`, `notExists`, `greaterThan`, `lessThan`, `regex` |
| `value` | string | Value to compare |
| `negate` | boolean | Negate the result |

#### Flow Control

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `combineWith` | string | `"and"` | `and` or `or` |
| `onTrue` | string | - | Step ID when conditions are true |
| `onFalse` | string | - | Step ID when conditions are false |

**Admin UI Location**: Select "Condition" from step types dropdown.

**Example**:
```json
{
  "type": "Condition",
  "configuration": {
    "conditions": [
      {
        "source": "claim",
        "key": "email_verified",
        "operator": "equals",
        "value": "true"
      },
      {
        "source": "claim",
        "key": "role",
        "operator": "contains",
        "value": "admin"
      }
    ],
    "combineWith": "and",
    "onTrue": "admin-dashboard",
    "onFalse": "verify-email"
  }
}
```

---

### Transform Step

Transform or map claims between formats.

#### Mappings Array

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `source` | string | **required** | Source claim type |
| `target` | string | **required** | Target claim type |
| `transform` | string | `"copy"` | `copy`, `uppercase`, `lowercase`, `hash`, `split`, `join`, `regex`, `template` |
| `transformArg` | string | - | Argument for transform (regex pattern, delimiter, template) |

#### Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `removeSourceClaims` | boolean | `false` | Remove source claims after mapping |

**Admin UI Location**: Select "Transform Claims" from step types dropdown.

**Example**:
```json
{
  "type": "Transform",
  "configuration": {
    "mappings": [
      {
        "source": "given_name",
        "target": "first_name",
        "transform": "copy"
      },
      {
        "source": "email",
        "target": "email_domain",
        "transform": "regex",
        "transformArg": "@(.+)$"
      },
      {
        "source": "name",
        "target": "display_name",
        "transform": "template",
        "transformArg": "User: {value}"
      }
    ],
    "removeSourceClaims": false
  }
}
```

---

### CreateUser Step

Create a new user account in the system.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `emailClaim` | string | `"email"` | Claim containing email |
| `usernameClaim` | string | - | Claim containing username (defaults to email) |
| `passwordClaim` | string | `"password"` | Claim containing password |
| `requireEmailVerification` | boolean | `true` | Require email verification |
| `defaultRoles` | string[] | - | Roles to assign to new user |
| `claimMappings` | object | - | Map collected claims to user properties |

**Admin UI Location**: Select "Create User" from step types dropdown.

**Example**:
```json
{
  "type": "CreateUser",
  "configuration": {
    "emailClaim": "email",
    "passwordClaim": "new_password",
    "requireEmailVerification": true,
    "defaultRoles": ["user", "beta-tester"],
    "claimMappings": {
      "given_name": "FirstName",
      "family_name": "LastName",
      "phone_number": "PhoneNumber"
    }
  }
}
```

---

### TermsAcceptance Step

Require user to accept terms of service and/or privacy policy.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `termsUrl` | string | **required** | URL to terms of service |
| `privacyUrl` | string | - | URL to privacy policy |
| `requireCheckbox` | boolean | `true` | Require checkbox acceptance |
| `version` | string | - | Terms version for tracking acceptance |

**Admin UI Location**: Select "Terms Acceptance" from step types dropdown.

**Example**:
```json
{
  "type": "TermsAcceptance",
  "configuration": {
    "termsUrl": "https://example.com/terms",
    "privacyUrl": "https://example.com/privacy",
    "requireCheckbox": true,
    "version": "2.0"
  }
}
```

---

### PasswordReset Step

Reset user password via email verification.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `tokenLifetimeMinutes` | number | `60` | Reset token lifetime |
| `requireCurrentPassword` | boolean | `false` | Require current password |

**Admin UI Location**: Select "Password Reset" from step types dropdown.

**Example**:
```json
{
  "type": "PasswordReset",
  "configuration": {
    "tokenLifetimeMinutes": 30,
    "requireCurrentPassword": false
  }
}
```

---

### CustomPlugin Step

Execute custom WASM or managed plugin.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `pluginName` | string | **required** | Plugin assembly or WASM file name |
| `entryPoint` | string | `"execute"` | Plugin entry point function |
| `config` | object | - | Custom configuration passed to plugin |

**Admin UI Location**: Select "Custom Plugin" from step types dropdown.

**Example**:
```json
{
  "type": "CustomPlugin",
  "configuration": {
    "pluginName": "risk-assessment.wasm",
    "entryPoint": "evaluate_risk",
    "config": {
      "threshold": 0.8,
      "blockHighRisk": true
    }
  }
}
```

---

### CustomPage Step

Display custom HTML page or Razor template.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `templatePath` | string | **required** | Path to custom Razor template |
| `model` | object | - | Data to pass to template |

**Admin UI Location**: Select "Custom Page" from step types dropdown.

**Example**:
```json
{
  "type": "CustomPage",
  "configuration": {
    "templatePath": "~/Views/Custom/Welcome.cshtml",
    "model": {
      "welcomeMessage": "Welcome to our platform!",
      "showFeatures": true
    }
  }
}
```

---

## FIDO2/WebAuthn (Passkeys)

> **License Required:** Professional+ license or `fido2` add-on

Enable passwordless authentication using FIDO2/WebAuthn (passkeys). This feature uses the [Fido2NetLib](https://github.com/passwordless-lib/fido2-net-lib) library for full WebAuthn specification compliance.

### Prerequisites

1. **HTTPS is required** - WebAuthn doesn't work over HTTP (except localhost)
2. **Configure the Relying Party** - Set your domain as the relying party ID
3. **License** - Professional tier or purchase the `fido2` add-on

### Installation

Add the FIDO2 package to your Identity Server project:

```csharp
// Program.cs
builder.Services.AddFido2(builder.Configuration);
```

### Configuration

In `appsettings.json`:

```json
{
  "Fido2": {
    "RelyingPartyId": "auth.example.com",
    "RelyingPartyName": "My Identity Server",
    "RelyingPartyIcon": "https://auth.example.com/logo.png",
    "Origins": ["https://auth.example.com"],
    "Timeout": 60000,
    "AttestationConveyancePreference": "none",
    "UserVerificationRequirement": "preferred",
    "AuthenticatorAttachment": null,
    "ResidentKeyRequirement": "preferred",
    "MaxCredentialsPerUser": 10,
    "StoreAttestationData": false,
    "MetadataService": {
      "Enabled": false,
      "CachePath": "/data/fido2-mds-cache.json",
      "RefreshIntervalHours": 24
    }
  }
}
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RelyingPartyId` | string | **required** | Domain name (e.g., `example.com`) |
| `RelyingPartyName` | string | **required** | Display name shown to users |
| `RelyingPartyIcon` | string | - | URL to relying party icon |
| `Origins` | string[] | **required** | Allowed origins for WebAuthn |
| `Timeout` | uint | `60000` | Ceremony timeout in milliseconds |
| `AttestationConveyancePreference` | string | `"none"` | `none`, `indirect`, `direct`, `enterprise` |
| `UserVerificationRequirement` | string | `"preferred"` | `required`, `preferred`, `discouraged` |
| `AuthenticatorAttachment` | string | null | `platform`, `cross-platform`, or null (any) |
| `ResidentKeyRequirement` | string | `"preferred"` | `required`, `preferred`, `discouraged` |
| `MaxCredentialsPerUser` | int | `10` | Maximum passkeys per user |
| `StoreAttestationData` | bool | `false` | Store attestation for enterprise scenarios |

### Adding WebAuthn to a Journey

Add a `WebAuthn` step to your journey:

```json
{
  "id": "webauthn-login",
  "type": "WebAuthn",
  "displayName": "Sign in with Passkey",
  "configuration": {
    "passkeyOnly": false,
    "allowFallback": true
  }
}
```

#### WebAuthn Step Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `passkeyOnly` | boolean | `false` | Skip username entry, use discoverable credentials only |
| `allowFallback` | boolean | `true` | Allow falling back to password authentication |

### User Registration Flow

1. User signs in with password (or other method)
2. Navigate to account settings or registration step
3. Click "Register Passkey" to start WebAuthn ceremony
4. Browser prompts for biometric/PIN verification
5. Credential is stored and linked to user

### User Authentication Flow

#### Username-First Flow (Default)
1. User enters username/email
2. Server looks up user's registered passkeys
3. Browser prompts for passkey authentication
4. Server verifies assertion and authenticates user

#### Passkey-Only Flow (Discoverable Credentials)
1. User clicks "Sign in with Passkey"
2. Browser shows available passkeys for this domain
3. User selects passkey and verifies with biometric/PIN
4. Server identifies user from credential and authenticates

Enable passkey-only flow:

```json
{
  "type": "WebAuthn",
  "configuration": {
    "passkeyOnly": true,
    "allowFallback": true
  }
}
```

### Supported Authenticator Types

| Type | Description | Use Case |
|------|-------------|----------|
| **Platform** | Built into device (Touch ID, Face ID, Windows Hello) | User's own devices |
| **Cross-Platform** | USB/NFC security keys (YubiKey, Titan) | Shared devices, high security |

### API Endpoints

The FIDO2 module exposes these endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/fido2/register/options` | POST | Get registration options |
| `/api/fido2/register/complete` | POST | Complete registration |
| `/api/fido2/assert/options` | POST | Get assertion options |
| `/api/fido2/assert/complete` | POST | Verify assertion |
| `/api/fido2/credentials` | GET | List user's credentials |
| `/api/fido2/credentials/{id}` | DELETE | Remove a credential |
| `/api/fido2/credentials/{id}/name` | PATCH | Rename a credential |

### Browser Support

FIDO2/WebAuthn is supported in all modern browsers:

| Browser | Platform Authenticator | Roaming Authenticator |
|---------|----------------------|----------------------|
| Chrome 67+ | Yes | Yes |
| Firefox 60+ | Yes | Yes |
| Safari 14+ | Yes | Yes |
| Edge 18+ | Yes | Yes |

### Security Considerations

1. **Always use HTTPS** - WebAuthn requires secure context
2. **Validate origins** - Ensure `Origins` config matches your actual domain
3. **Enable user verification** - Use `required` or `preferred` for sensitive operations
4. **Consider attestation** - Enable for enterprise scenarios requiring device trust
5. **Rate limit** - Protect registration endpoints from abuse

### Troubleshooting

#### "NotAllowedError" during authentication
- User cancelled the ceremony
- No matching credentials found
- Browser doesn't have permission for WebAuthn

#### "SecurityError"
- Not using HTTPS (required except localhost)
- Origin mismatch between config and actual domain

#### Passkeys not showing
- Check `RelyingPartyId` matches your domain exactly
- Ensure credentials were registered on same RP ID
- Verify browser supports WebAuthn

---

## Multi-Factor Authentication (MFA)

### Supported MFA Methods

| Method | Description |
|--------|-------------|
| `totp` | Time-based One-Time Password (Google Authenticator, etc.) |
| `phone` | SMS verification code |
| `email` | Email verification code |
| `webauthn` | FIDO2 security key |

### Configuring MFA Step

```json
{
  "id": "mfa-step",
  "type": "Mfa",
  "displayName": "Verify Your Identity",
  "configuration": {
    "required": false,
    "methods": ["totp", "phone", "email", "webauthn"],
    "rememberDevice": true,
    "rememberDuration": 30
  }
}
```

### Requiring MFA per Client

In Admin UI, edit the client and set **Require MFA** to **Yes**.

Or via API:

```json
{
  "requireMfa": true
}
```

### MFA Setup Journey

Create a `ProfileEdit` journey with MFA setup:

```json
{
  "id": "mfa-setup",
  "type": "MfaSetup",
  "displayName": "Set Up Two-Factor Authentication",
  "configuration": {
    "allowedMethods": ["totp", "webauthn"],
    "requireAtLeastOne": true
  }
}
```

### SMS and Email Messaging

The MFA system uses `ISmsSender` and `IEmailSender` interfaces to send verification codes. By default, no-op implementations are registered that log messages instead of sending them.

#### Configuring SMS Provider

Implement `ISmsSender` to integrate with your SMS provider (Twilio, AWS SNS, etc.):

```csharp
public class TwilioSmsSender : ISmsSender
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;

    public TwilioSmsSender(IConfiguration config)
    {
        _accountSid = config["Twilio:AccountSid"];
        _authToken = config["Twilio:AuthToken"];
        _fromNumber = config["Twilio:FromNumber"];
    }

    public async Task<SmsResult> SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        TwilioClient.Init(_accountSid, _authToken);

        try
        {
            var twilioMessage = await MessageResource.CreateAsync(
                to: new PhoneNumber(phoneNumber),
                from: new PhoneNumber(_fromNumber),
                body: message);

            return SmsResult.Succeeded(twilioMessage.Sid);
        }
        catch (Exception ex)
        {
            return SmsResult.Failed(ex.Message);
        }
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddSmsSender<TwilioSmsSender>();
```

#### Configuring Email Provider

Implement `IEmailSender` to integrate with your email provider (SendGrid, AWS SES, SMTP, etc.):

```csharp
public class SendGridEmailSender : IEmailSender
{
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public SendGridEmailSender(IConfiguration config)
    {
        _apiKey = config["SendGrid:ApiKey"];
        _fromEmail = config["SendGrid:FromEmail"];
        _fromName = config["SendGrid:FromName"];
    }

    public async Task<EmailResult> SendAsync(
        string email,
        string subject,
        string htmlMessage,
        CancellationToken ct = default)
    {
        var client = new SendGridClient(_apiKey);
        var from = new EmailAddress(_fromEmail, _fromName);
        var to = new EmailAddress(email);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlMessage);

        var response = await client.SendEmailAsync(msg, ct);

        return response.IsSuccessStatusCode
            ? EmailResult.Succeeded(response.Headers.GetValues("X-Message-Id").FirstOrDefault())
            : EmailResult.Failed($"SendGrid error: {response.StatusCode}");
    }

    public async Task<EmailResult> SendTemplateAsync(
        string email,
        string templateName,
        IDictionary<string, object> templateData,
        CancellationToken ct = default)
    {
        // Use SendGrid dynamic templates
        var client = new SendGridClient(_apiKey);
        var msg = new SendGridMessage
        {
            From = new EmailAddress(_fromEmail, _fromName),
            TemplateId = templateName
        };
        msg.AddTo(email);
        msg.SetTemplateData(templateData);

        var response = await client.SendEmailAsync(msg, ct);
        return response.IsSuccessStatusCode
            ? EmailResult.Succeeded()
            : EmailResult.Failed($"SendGrid error: {response.StatusCode}");
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddEmailSender<SendGridEmailSender>();
```

#### Interfaces Reference

**ISmsSender**

| Method | Description |
|--------|-------------|
| `SendAsync(phoneNumber, message, ct)` | Sends an SMS to the specified phone number |

**SmsResult**

| Property | Type | Description |
|----------|------|-------------|
| `Success` | bool | Whether the send succeeded |
| `Error` | string? | Error message if failed |
| `MessageId` | string? | Provider message ID if available |

**IEmailSender**

| Method | Description |
|--------|-------------|
| `SendAsync(email, subject, htmlMessage, ct)` | Sends an email with HTML content |
| `SendTemplateAsync(email, templateName, data, ct)` | Sends using a provider template |

**EmailResult**

| Property | Type | Description |
|----------|------|-------------|
| `Success` | bool | Whether the send succeeded |
| `Error` | string? | Error message if failed |
| `MessageId` | string? | Provider message ID if available |

#### MFA Message Content

The MFA step sends the following messages:

| Flow | SMS Message | Email Subject |
|------|-------------|---------------|
| Verification | `Your verification code is: {code}` | Your verification code |
| Setup | `Your verification code is: {code}` | Set up two-factor authentication |

To customize message content, create a custom sender that wraps the default implementation.

#### Development Mode

By default, `NoOpSmsSender` and `NoOpEmailSender` are registered. These log messages instead of sending:

```
warn: IdentityServer.Core.Services.Messaging.NoOpSmsSender
      SMS sending not configured. Would send to +1234567890: Your verification code is: 123456
```

This is useful for development and testing without incurring SMS/email costs.

---

## Self-Service Registration

### Enabling Registration

1. Set `AllowSelfRegistration = true` on the tenant
2. Create a `SignUp` or `SignInSignUp` journey

### Registration Journey Example

```json
{
  "name": "User Registration",
  "type": "SignUp",
  "steps": [
    {
      "id": "collect-info",
      "type": "ClaimsCollection",
      "configuration": {
        "title": "Create Account",
        "fields": [
          { "name": "email", "type": "email", "label": "Email", "required": true },
          { "name": "given_name", "type": "text", "label": "First Name", "required": true },
          { "name": "family_name", "type": "text", "label": "Last Name", "required": true },
          { "name": "password", "type": "password", "label": "Password", "required": true }
        ]
      }
    },
    {
      "id": "terms",
      "type": "TermsAcceptance",
      "configuration": {
        "termsUrl": "https://example.com/terms",
        "privacyUrl": "https://example.com/privacy",
        "requireCheckbox": true
      }
    },
    {
      "id": "create-user",
      "type": "CreateUser",
      "configuration": {
        "requireEmailVerification": true,
        "defaultRoles": ["user"]
      }
    }
  ]
}
```

### Email Domain Restrictions

Restrict registration to specific email domains:

```
AllowedEmailDomains = "company.com, partner.com"
```

---

## External Identity Providers

### Supported Providers

- Google
- Microsoft (Azure AD, Microsoft Account)
- Facebook
- Apple
- GitHub
- Generic OIDC
- Generic OAuth 2.0
- SAML 2.0

### Adding an External Provider

1. Navigate to **Identity Providers** in Admin UI
2. Click **Add Provider**
3. Select provider type
4. Configure credentials and settings

### Provider Configuration Example (Google)

```json
{
  "name": "Google",
  "type": "Google",
  "enabled": true,
  "clientId": "your-client-id.apps.googleusercontent.com",
  "clientSecret": "your-client-secret",
  "scopes": ["openid", "profile", "email"],
  "autoProvision": true,
  "mapClaims": {
    "email": "email",
    "given_name": "given_name",
    "family_name": "family_name",
    "picture": "picture"
  }
}
```

### Using External IdP in Journey

```json
{
  "id": "social-login",
  "type": "ExternalIdp",
  "displayName": "Sign in with Google",
  "configuration": {
    "providerId": "google",
    "autoProvision": true,
    "linkIfExists": true
  }
}
```

---

## WASM Plugins

Extend the Identity Server with WebAssembly plugins using Extism.

### Plugin Capabilities

Plugins can:
- Execute custom logic in journey steps
- Transform claims
- Call external APIs
- Validate user input
- Implement custom authentication methods

### Creating a Plugin

#### 1. Set Up Project

```bash
# Using Rust
cargo new my-plugin --lib
```

#### 2. Add Dependencies

```toml
# Cargo.toml
[dependencies]
extism-pdk = "1.0"
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"

[lib]
crate-type = ["cdylib"]
```

#### 3. Implement Plugin

```rust
use extism_pdk::*;
use serde::{Deserialize, Serialize};

#[derive(Deserialize)]
struct PluginInput {
    claims: std::collections::HashMap<String, String>,
    configuration: serde_json::Value,
    context: ExecutionContext,
}

#[derive(Deserialize)]
struct ExecutionContext {
    tenant_id: Option<String>,
    client_id: String,
    user_id: Option<String>,
}

#[derive(Serialize)]
struct PluginOutput {
    success: bool,
    claims: Option<std::collections::HashMap<String, String>>,
    error_code: Option<String>,
    error_message: Option<String>,
    redirect_url: Option<String>,
}

#[plugin_fn]
pub fn execute(input: Json<PluginInput>) -> FnResult<Json<PluginOutput>> {
    let input = input.into_inner();

    // Your custom logic here
    let mut output_claims = input.claims.clone();
    output_claims.insert("custom_claim".to_string(), "custom_value".to_string());

    Ok(Json(PluginOutput {
        success: true,
        claims: Some(output_claims),
        error_code: None,
        error_message: None,
        redirect_url: None,
    }))
}
```

#### 4. Build Plugin

```bash
cargo build --release --target wasm32-unknown-unknown
```

#### 5. Deploy Plugin

1. Navigate to **Plugins** in Admin UI
2. Click **Upload Plugin**
3. Select your `.wasm` file
4. Configure plugin metadata

### Using Plugin in Journey

```json
{
  "id": "custom-validation",
  "type": "CustomPlugin",
  "displayName": "Custom Validation",
  "pluginName": "my-validation-plugin",
  "configuration": {
    "setting1": "value1",
    "setting2": "value2"
  }
}
```

### Plugin Input/Output Schema

#### Input
```typescript
interface PluginInput {
  claims: Record<string, string>;      // Current claims bag
  configuration: Record<string, any>;  // Step configuration
  context: {
    tenantId?: string;
    clientId: string;
    userId?: string;
    sessionId: string;
    stepId: string;
    journeyId: string;
  };
  userInput?: Record<string, string>;  // Form submission data
}
```

#### Output
```typescript
interface PluginOutput {
  success: boolean;
  claims?: Record<string, string>;     // Claims to add/update
  errorCode?: string;
  errorMessage?: string;
  redirectUrl?: string;                // External redirect
  uiSchema?: DynamicFormSchema;        // Render custom form
}
```

### Dynamic Form Schema

Plugins can return a UI schema to render custom forms:

```rust
#[derive(Serialize)]
struct DynamicFormSchema {
    title: String,
    description: Option<String>,
    fields: Vec<FormField>,
    submit_button_text: Option<String>,
}

#[derive(Serialize)]
struct FormField {
    name: String,
    field_type: String, // text, email, password, select, checkbox, etc.
    label: String,
    required: bool,
    placeholder: Option<String>,
    options: Option<Vec<SelectOption>>, // For select/radio fields
}
```

---

## Client Configuration

### Journey Settings per Client

Each client can override tenant-level journey settings:

| Setting | Description |
|---------|-------------|
| `UseJourneyFlow` | Override tenant's flow setting |
| `SignInPolicyId` | Specific sign-in journey |
| `SignUpPolicyId` | Specific sign-up journey |
| `SignInSignUpPolicyId` | Combined sign-in/sign-up journey |
| `PasswordResetPolicyId` | Password reset journey |
| `ProfileEditPolicyId` | Profile edit journey |
| `RequireMfa` | Force MFA for this client |
| `AllowedPolicies` | Restrict which policies can be used |

### Restricting Policies

To restrict which journey policies a client can use:

1. Edit the client in Admin UI
2. Go to **User Journey Settings**
3. Add policies to **Allowed Policies**

When restricted, only listed policies can be requested via the `policy` parameter.

### Admin UI Configuration

1. Navigate to **Clients** > select client
2. Click **Edit**
3. Scroll to **User Journey Settings** card
4. Configure settings as needed

### User and Role Restrictions

Restrict which users can access a client application using AllowedUsers and AllowedRoles.

#### How Restrictions Work

- **No restrictions**: If neither AllowedUsers nor AllowedRoles are configured, all authenticated users can access the client
- **With restrictions**: User must satisfy at least ONE of the conditions:
  - User ID is in the AllowedUsers list, OR
  - User has at least one role from the AllowedRoles list

#### Configuring via Admin UI

1. Navigate to **Clients** > select client
2. Click **Edit**
3. Scroll to **Access Control** section
4. Add users to **Allowed Users** (by subject ID and display name)
5. Add roles to **Allowed Roles**
6. Save changes

#### Configuring via API

```http
PUT /api/clients/{clientId}
Content-Type: application/json

{
  "allowedUsers": [
    { "subjectId": "user-123", "displayName": "John Doe" },
    { "subjectId": "user-456", "displayName": "Jane Smith" }
  ],
  "allowedRoles": [
    { "role": "admin" },
    { "role": "premium-user" }
  ]
}
```

#### Use Cases

| Scenario | Configuration |
|----------|--------------|
| Admin-only application | Add `admin` role to AllowedRoles |
| Specific users only | Add user IDs to AllowedUsers |
| Department access | Add department role (e.g., `hr`, `engineering`) to AllowedRoles |
| Beta testers | Add `beta-tester` role or specific user IDs |
| Mixed access | Combine specific users AND roles |

#### Error Handling

When a user is denied access:
- Authorization endpoint returns `access_denied` error
- Error is logged with user ID and client ID
- User sees a clear error message

#### Enforcement Points

Restrictions are enforced at:
1. **Authorization endpoint** - When user is already authenticated
2. **Journey completion** - After user completes authentication journey
3. **Password grant** - For Resource Owner Password Credentials flow

#### Example: Internal Admin Tool

```json
{
  "clientId": "admin-dashboard",
  "clientName": "Admin Dashboard",
  "allowedRoles": [
    { "role": "admin" },
    { "role": "super-admin" }
  ],
  "allowedUsers": []
}
```

Only users with `admin` or `super-admin` roles can access this client.

#### Example: Beta Application

```json
{
  "clientId": "new-feature-beta",
  "clientName": "New Feature Beta",
  "allowedRoles": [
    { "role": "beta-tester" }
  ],
  "allowedUsers": [
    { "subjectId": "ceo-user-id", "displayName": "CEO" }
  ]
}
```

Users with the `beta-tester` role OR the CEO (by user ID) can access this client.

---

## Signing Keys

Manage cryptographic keys used for signing tokens and other security operations.

### Key Types

| Type | Algorithms | Use Cases |
|------|------------|-----------|
| **RSA** | RS256, RS384, RS512 | General purpose, widest compatibility |
| **Elliptic Curve** | ES256, ES384, ES512 | Smaller keys, faster operations |
| **Symmetric** | HS256, HS384, HS512 | Client secret signing |

### Key Lifecycle

Keys have the following statuses:

| Status | Description |
|--------|-------------|
| `Pending` | Created but not yet active |
| `Active` | Currently in use for signing |
| `Expired` | Past expiration date (can still verify) |
| `Revoked` | Manually disabled |
| `Archived` | Retained for historical verification |

### Managing Keys via Admin UI

1. Navigate to **Settings** > **Signing Keys**
2. View all keys with their status, algorithm, and usage statistics

#### Generate New Key

1. Click **Generate New Key**
2. Configure:
   - **Name**: Descriptive name for the key
   - **Key Type**: RSA, EC, or Symmetric
   - **Algorithm**: Signing algorithm (e.g., RS256, ES256)
   - **Key Size**: 2048, 3072, 4096 (RSA) or curve (EC)
   - **Lifetime**: How long the key should be valid (days)
   - **Activate Immediately**: Whether to make active upon creation

#### Rotate Keys

1. Click **Rotate Keys**
2. A new key is generated and activated
3. Existing active keys are demoted (can still verify, not sign)
4. Follows rotation configuration settings

#### Revoke a Key

1. Select the key
2. Click **Revoke**
3. Enter a revocation reason
4. Key can no longer sign or verify

### Key Rotation Configuration

Configure automatic key rotation:

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enable automatic rotation | `false` |
| `KeyType` | Type for new keys | `RSA` |
| `Algorithm` | Algorithm for new keys | `RS256` |
| `KeySize` | Size for new keys | `2048` |
| `KeyLifetimeDays` | How long keys remain valid | `90` |
| `RotationLeadTimeDays` | Days before expiration to rotate | `14` |
| `GracePeriodDays` | Days expired keys can still verify | `30` |
| `MaxKeys` | Maximum keys to retain | `5` |

### Storage Providers

Keys can be stored in different backends:

#### Local Storage (Default)
```json
{
  "storageProvider": "Local"
}
```
- Keys encrypted at rest in database
- Private key material never exposed via API

#### Azure Key Vault
```json
{
  "storageProvider": "AzureKeyVault",
  "keyVaultUri": "https://myvault.vault.azure.net/keys/my-signing-key"
}
```
- Private keys never leave Azure Key Vault
- Signing operations happen inside Key Vault
- Uses DefaultAzureCredential (Managed Identity, CLI, environment)

#### AWS KMS
```json
{
  "storageProvider": "AwsKms",
  "keyVaultUri": "arn:aws:kms:us-east-1:123456789:key/abc-123"
}
```

#### HashiCorp Vault
```json
{
  "storageProvider": "HashiCorpVault",
  "keyVaultUri": "transit/keys/my-signing-key"
}
```

#### Google Cloud KMS
```json
{
  "storageProvider": "GoogleCloudKms",
  "keyVaultUri": "projects/my-project/locations/global/keyRings/my-ring/cryptoKeys/my-key"
}
```

### Admin API

#### List Keys
```http
GET /api/admin/signing-keys
```

#### Generate Key
```http
POST /api/admin/signing-keys
Content-Type: application/json

{
  "name": "Production Key 2025",
  "keyType": "RSA",
  "algorithm": "RS256",
  "keySize": 2048,
  "lifetimeDays": 90,
  "activateImmediately": true
}
```

#### Rotate Keys
```http
POST /api/admin/signing-keys/rotate
```

#### Update Key Status
```http
PATCH /api/admin/signing-keys/{id}/status
Content-Type: application/json

{
  "priority": 100,
  "includeInJwks": true
}
```

#### Revoke Key
```http
POST /api/admin/signing-keys/{id}/revoke
Content-Type: application/json

{
  "reason": "Key compromised"
}
```

#### Get Expiring Keys
```http
GET /api/admin/signing-keys/expiring?daysThreshold=14
```

### JWKS Endpoint

Public keys are exposed at the standard JWKS endpoint:
```
/.well-known/openid-configuration/jwks
```

Only keys with `includeInJwks = true` and valid status are included.

---

## Events and Webhooks

The Identity Server publishes events for authentication, token, and system activities. These events can be consumed via webhooks or custom event sinks.

### Event Categories

| Category | Description |
|----------|-------------|
| `Authentication` | Login, logout, MFA, registration |
| `Token` | Token issued, revoked, refreshed |
| `Authorization` | Authorization requests, consent |
| `Client` | Client authentication |
| `Session` | Session lifecycle |
| `Device` | Device authorization flow |
| `ExternalIdp` | External provider authentication |
| `System` | Errors, configuration issues |

### Event Types

#### Authentication Events

| Event | Description |
|-------|-------------|
| `UserLoginSuccess` | User successfully authenticated |
| `UserLoginFailure` | Authentication failed (bad password, locked, etc.) |
| `UserLogoutSuccess` | User logged out |
| `MfaChallenge` | MFA challenge initiated |
| `MfaSuccess` | MFA verification passed |
| `MfaFailure` | MFA verification failed |
| `UserRegistrationSuccess` | New user registered |
| `UserRegistrationFailure` | Registration failed |
| `EmailVerificationSuccess` | Email verified |
| `EmailVerificationFailure` | Email verification failed |

#### FIDO2/WebAuthn Events

| Event | Description |
|-------|-------------|
| `Fido2CredentialRegistered` | Passkey registered |
| `Fido2AuthenticationSuccess` | Passkey authentication succeeded |
| `Fido2AuthenticationFailure` | Passkey authentication failed |
| `Fido2CredentialRemoved` | Passkey removed |

#### Token Events

| Event | Description |
|-------|-------------|
| `TokenIssuedSuccess` | Token successfully issued |
| `TokenIssuedFailure` | Token issuance failed |
| `TokenRevokedSuccess` | Token revoked |
| `TokenIntrospectionSuccess` | Token introspection completed |
| `RefreshTokenUsed` | Refresh token exchanged |

#### External IdP Events

| Event | Description |
|-------|-------------|
| `ExternalLoginSuccess` | External provider login succeeded |
| `ExternalLoginFailure` | External provider login failed |

### Event Structure

All events include:

```json
{
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "eventType": "UserLoginSuccess",
  "category": "Authentication",
  "timestamp": "2025-01-15T10:30:00Z",
  "activityId": "trace-correlation-id",
  "tenantId": "tenant-1",
  "remoteIpAddress": "192.168.1.100",
  "data": {
    "subjectId": "user-123",
    "clientId": "my-app",
    "authenticationMethod": "pwd"
  }
}
```

### Configuring Webhooks

#### In Application Startup

```csharp
services.AddIdentityServerEvents()
    .AddWebhookEventSink("https://my-service.com/webhooks/identity");
```

#### Multiple Webhooks

```csharp
services.AddIdentityServerEvents()
    .AddWebhookEventSink("https://audit.example.com/events")
    .AddWebhookEventSink("https://analytics.example.com/events");
```

#### Filtered Webhooks

```csharp
services.AddIdentityServerEvents()
    .AddFilteredEventSink<WebhookEventSink>(
        filter: e => e.Category == "Authentication",
        configure: sink => sink.WebhookUrl = "https://auth-monitor.example.com/events"
    );
```

### Webhook Payload

Webhooks receive HTTP POST requests with JSON payload:

```http
POST /webhooks/identity HTTP/1.1
Content-Type: application/json

{
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "eventType": "UserLoginSuccess",
  "category": "Authentication",
  "timestamp": "2025-01-15T10:30:00Z",
  "tenantId": "tenant-1",
  "data": { ... }
}
```

### Custom Event Sinks

Implement `IEventSink` for custom event handling:

```csharp
public class MyCustomEventSink : IEventSink
{
    public async Task PersistAsync(IdentityServerEvent evt)
    {
        // Send to your monitoring system
        await _monitoring.TrackEventAsync(new {
            Type = evt.EventType,
            Category = evt.Category,
            Timestamp = evt.Timestamp,
            TenantId = evt.TenantId,
            Data = evt.Data
        });
    }
}
```

Register in startup:

```csharp
services.AddIdentityServerEvents()
    .AddEventSink<MyCustomEventSink>();
```

### Batching Events

For high-throughput scenarios, use batching:

```csharp
services.AddIdentityServerEvents()
    .AddBatchingEventSink(
        processEvents: async events => {
            await _bulkWriter.WriteAsync(events);
        },
        batchSize: 100,
        flushInterval: TimeSpan.FromSeconds(5)
    );
```

### Event Configuration

Control which events are raised:

```csharp
services.AddIdentityServerEvents(options =>
{
    options.RaiseAuthenticationEvents = true;
    options.RaiseTokenEvents = true;
    options.RaiseAuthorizationEvents = true;
    options.RaiseClientEvents = true;
    options.RaiseSessionEvents = true;
    options.RaiseErrorEvents = true;
    options.RaiseDeviceEvents = true;
    options.RaiseExternalIdpEvents = true;

    // Only emit failure events (reduce noise)
    options.RaiseFailureEventsOnly = false;
});
```

> **Note:** Events are raised but not persisted by default. For persistent audit logs with querying capabilities, see the [Audit Logging](#audit-logging) section (requires Professional+ license or add-on).

### Using Webhooks in User Journeys

Add a Webhook step to your journey:

```json
{
  "id": "notify-signup",
  "type": "Webhook",
  "displayName": "Notify CRM",
  "configuration": {
    "url": "https://crm.example.com/api/new-user",
    "method": "POST",
    "headers": {
      "Authorization": "Bearer ${env:CRM_API_KEY}"
    },
    "body": {
      "email": "${claims:email}",
      "name": "${claims:name}",
      "source": "identity-server"
    },
    "timeoutSeconds": 10,
    "continueOnError": true
  }
}
```

---

## Audit Logging

> **License Required:** Professional+ license or `audit-logging` add-on

Audit Logging provides persistent, queryable records of security events for compliance, forensics, and monitoring. Unlike events (which are fire-and-forget), audit logs are stored in the database and can be searched, filtered, and exported.

### What's Captured

Audit logs capture the **who, what, when, where, and how** of security-relevant actions:

| Field | Description |
|-------|-------------|
| **Who** | Subject ID, name, and email of the actor |
| **What** | Action performed, resource type, and resource ID |
| **When** | Timestamp (UTC) of the event |
| **Where** | IP address, user agent, tenant ID |
| **How** | Authentication method, changed fields, details |
| **Result** | Success/failure status, error messages |

### Event Categories

#### Authentication Events

| Event Type | Description |
|------------|-------------|
| `UserLoginSuccess` | Successful user authentication |
| `UserLoginFailure` | Failed authentication attempt |
| `UserLogout` | User logged out |
| `MfaSuccess` | MFA verification passed |
| `MfaFailure` | MFA verification failed |
| `MfaSetupComplete` | User completed MFA setup |

#### Admin Events

| Event Type | Description |
|------------|-------------|
| `UserCreated` | New user account created |
| `UserUpdated` | User profile modified |
| `UserDeleted` | User account deleted |
| `UserLocked` | User account locked |
| `UserUnlocked` | User account unlocked |
| `UserRolesChanged` | User role assignments changed |
| `RoleCreated` | New role created |
| `RoleUpdated` | Role modified |
| `RoleDeleted` | Role deleted |
| `RolePermissionsChanged` | Role permissions modified |
| `ClientCreated` | New OAuth client created |
| `ClientUpdated` | Client configuration changed |
| `ClientDeleted` | Client deleted |
| `ClientSecretRegenerated` | Client secret rotated |

#### Configuration Events

| Event Type | Description |
|------------|-------------|
| `TenantSettingsChanged` | Tenant configuration modified |
| `SigningKeyRotated` | Signing keys rotated |
| `IdentityProviderCreated` | External IdP added |
| `IdentityProviderUpdated` | External IdP modified |
| `IdentityProviderDeleted` | External IdP removed |
| `JourneyPolicyCreated` | Journey policy created |
| `JourneyPolicyUpdated` | Journey policy modified |
| `JourneyPolicyDeleted` | Journey policy deleted |

#### Access Events

| Event Type | Description |
|------------|-------------|
| `AccessDenied` | User denied access to resource |
| `AdminLogin` | Admin user logged in |
| `AdminLoginFailure` | Admin login attempt failed |

### Enabling Audit Logging

#### 1. Register Services

```csharp
// Program.cs
builder.Services.AddIdentityServerEvents(options => options.EnableAuditEvents());
builder.Services.AddAuditLogging();
builder.Services.AddAuditLoggingEventSink(); // Only registers if licensed
```

#### 2. Run Database Migration

After enabling audit logging, create and apply the migration:

```bash
dotnet ef migrations add AddAuditLogs
dotnet ef database update
```

### Admin UI

Navigate to **Audit Logs** in the Admin UI to:

- View all audit log entries with filtering
- Search by user, action, resource, or date range
- Filter by success/failure status
- Export logs to JSON or CSV
- View detailed event information

### API Endpoints

#### Check Status

```http
GET /api/auditlogs/status
```

Response:
```json
{
  "enabled": true,
  "message": "Audit logging is active"
}
```

If not licensed:
```json
{
  "enabled": false,
  "message": "Audit logging requires Professional license or AuditLogging add-on"
}
```

#### Query Audit Logs

```http
GET /api/auditlogs?category=Admin&action=Create&from=2025-01-01&pageSize=50
```

Query Parameters:

| Parameter | Type | Description |
|-----------|------|-------------|
| `action` | string | Filter by action (Create, Update, Delete, Login, etc.) |
| `category` | string | Filter by category (Authentication, Admin, Token, etc.) |
| `eventType` | string | Filter by specific event type |
| `subjectId` | string | Filter by user who performed action |
| `resourceType` | string | Filter by resource type (User, Role, Client, etc.) |
| `resourceId` | string | Filter by specific resource ID |
| `clientId` | string | Filter by OAuth client |
| `success` | boolean | Filter by success/failure |
| `search` | string | Full-text search in names and details |
| `from` | datetime | Start date (ISO 8601) |
| `to` | datetime | End date (ISO 8601) |
| `sortBy` | string | Sort field (Timestamp, EventType, Category) |
| `sortDesc` | boolean | Sort descending (default: true) |
| `page` | int | Page number (default: 1) |
| `pageSize` | int | Results per page (default: 50, max: 200) |

Response:
```json
{
  "items": [
    {
      "id": 12345,
      "timestamp": "2025-01-15T10:30:00Z",
      "eventType": "UserCreated",
      "category": "Admin",
      "action": "Create",
      "subjectId": "admin-user-id",
      "subjectName": "Admin User",
      "resourceType": "User",
      "resourceId": "new-user-id",
      "resourceName": "john@example.com",
      "ipAddress": "192.168.1.100",
      "success": true,
      "details": "{\"email\":\"john@example.com\",\"username\":\"john\"}"
    }
  ],
  "totalCount": 1523,
  "pageNumber": 1,
  "pageSize": 50,
  "totalPages": 31
}
```

#### Get Single Entry

```http
GET /api/auditlogs/12345
```

#### Get Logs for a Resource

```http
GET /api/auditlogs/resource/User/user-123?limit=100
```

#### Get Logs for a User

```http
GET /api/auditlogs/user/user-123?limit=100
```

#### Export Logs

```http
GET /api/auditlogs/export?from=2025-01-01&to=2025-01-31&format=csv
```

Formats: `json`, `csv`

#### Purge Old Logs

```http
DELETE /api/auditlogs/purge?olderThanDays=90
```

Minimum retention: 30 days

### Instrumenting Custom Code

Use `AdminAuditHelper` to raise audit events from your own controllers or services:

```csharp
public class MyController : ControllerBase
{
    private readonly AdminAuditHelper _audit;

    public MyController(AdminAuditHelper audit)
    {
        _audit = audit;
    }

    [HttpPost]
    public async Task<IActionResult> CreateSomething(CreateRequest request)
    {
        // ... create logic ...

        // Raise audit event
        await _audit.RaiseAsync(new MyCustomEvent
        {
            ResourceId = newItem.Id,
            ResourceName = newItem.Name,
            Details = new Dictionary<string, object>
            {
                ["field1"] = request.Field1,
                ["field2"] = request.Field2
            }
        });

        return Ok(newItem);
    }
}
```

### Creating Custom Events

Extend `AdminEvent` for custom audit events:

```csharp
public class MyCustomEvent : AdminEvent
{
    public override string EventType => "MyCustomAction";
    public override string ResourceType => "MyResource";
    public override string Action => "CustomAction";

    public string? CustomField { get; set; }
}
```

### License Behavior

| License Tier | Behavior |
|--------------|----------|
| **Community** | API returns `402 Payment Required` |
| **Starter** | API returns `402 Payment Required` |
| **Professional** | Full functionality |
| **Enterprise** | Full functionality |
| **Development** | Full functionality (for testing) |

Users without the required license can purchase the `audit-logging` add-on separately.

### Database Schema

Audit logs are stored in the `AuditLogs` table with indexes on:

- `(TenantId, Timestamp)` - Time-based queries
- `(TenantId, Category, Timestamp)` - Category filtering
- `(TenantId, SubjectId, Timestamp)` - User activity
- `(TenantId, ResourceType, ResourceId)` - Resource history
- `(TenantId, EventType, Timestamp)` - Event type filtering
- `(TenantId, Success, Timestamp)` - Success/failure filtering
- `(ActivityId)` - Request correlation

### Retention Policy

Implement a retention policy by scheduling the purge endpoint:

```bash
# Cron job to purge logs older than 90 days
0 2 * * * curl -X DELETE "https://your-server/api/auditlogs/purge?olderThanDays=90" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Or use a background service:

```csharp
public class AuditLogRetentionService : BackgroundService
{
    private readonly IServiceProvider _services;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var cutoff = DateTime.UtcNow.AddDays(-90);
            var deleted = await auditService.PurgeOldLogsAsync(cutoff, ct);

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
```

### Compliance Considerations

Audit logging helps satisfy requirements for:

- **SOC 2** - Access logging and monitoring controls
- **ISO 27001** - Information security event logging
- **GDPR** - Accountability and demonstrating compliance
- **HIPAA** - Audit controls for PHI access
- **PCI DSS** - Tracking access to cardholder data

Ensure your retention policy aligns with regulatory requirements (typically 1-7 years depending on jurisdiction and data type).

---

## Custom Styling

Customize the look and feel of authentication pages at the tenant or journey level.

### Styling Hierarchy

Styles are applied in this order (later overrides earlier):

1. **Default Theme** - Built-in styles
2. **Tenant Branding** - Applied to all journeys in a tenant
3. **Journey/Policy UI** - Policy-specific customization
4. **Theme Selection** - Predefined theme variants

### Per-Policy Styling

Each journey policy can have custom UI configuration:

| Setting | Description |
|---------|-------------|
| `Theme` | Predefined theme (light, dark, minimal) |
| `LogoUrl` | Custom logo image URL |
| `PrimaryColor` | Primary brand color (hex) |
| `BackgroundColor` | Background color (hex) |
| `CustomCss` | Arbitrary CSS rules |

### Configuring in Admin UI

1. Navigate to **Journeys** > select journey
2. Scroll to **UI Customization** section
3. Configure colors, logo, and custom CSS

### Available CSS Variables

The following CSS variables are available for customization:

```css
:root {
  /* Colors */
  --primary-color: #3b82f6;
  --secondary-color: #64748b;
  --journey-primary-color: #3b82f6;
  --journey-background-color: #ffffff;

  /* Text */
  --text-primary: #1f2937;
  --text-secondary: #6b7280;
  --text-muted: #9ca3af;

  /* Backgrounds */
  --bg-primary: #ffffff;
  --bg-secondary: #f3f4f6;
  --bg-card: #ffffff;

  /* Borders */
  --border-color: #e5e7eb;
  --border-radius: 8px;
  --border-radius-lg: 12px;

  /* Shadows */
  --shadow-sm: 0 1px 2px rgba(0, 0, 0, 0.05);
  --shadow-md: 0 4px 6px rgba(0, 0, 0, 0.1);
  --shadow-lg: 0 10px 15px rgba(0, 0, 0, 0.1);

  /* Spacing */
  --spacing-xs: 0.25rem;
  --spacing-sm: 0.5rem;
  --spacing-md: 1rem;
  --spacing-lg: 1.5rem;
  --spacing-xl: 2rem;
}
```

### CSS Classes

#### Container Classes
```css
.journey-container    /* Main journey wrapper */
.journey-logo         /* Logo container */
.journey-content      /* Content area */
.journey-footer       /* Footer area */
```

#### Form Classes
```css
.form-group          /* Form field wrapper */
.form-label          /* Field label */
.form-input          /* Input fields */
.form-select         /* Select dropdowns */
.form-checkbox       /* Checkboxes */
.form-error          /* Error messages */
.form-helper         /* Helper text */
```

#### Button Classes
```css
.btn                 /* Base button */
.btn-primary         /* Primary action */
.btn-secondary       /* Secondary action */
.btn-danger          /* Destructive action */
.btn-link            /* Link-style button */
.btn-social          /* Social login buttons */
```

#### Alert Classes
```css
.alert               /* Base alert */
.alert-success       /* Success message */
.alert-error         /* Error message */
.alert-warning       /* Warning message */
.alert-info          /* Info message */
```

### Custom CSS Examples

#### Modern Card Style
```css
.journey-container {
  max-width: 420px;
  margin: 2rem auto;
  padding: 2rem;
  border-radius: 16px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.1);
  background: #ffffff;
}

.journey-logo img {
  max-height: 48px;
  margin-bottom: 1.5rem;
}
```

#### Gradient Background
```css
body {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  min-height: 100vh;
}

.journey-container {
  background: rgba(255, 255, 255, 0.95);
  backdrop-filter: blur(10px);
}
```

#### Custom Button Styling
```css
.btn-primary {
  background: linear-gradient(135deg, var(--primary-color) 0%, #4f46e5 100%);
  border: none;
  border-radius: 8px;
  padding: 12px 24px;
  font-weight: 600;
  transition: transform 0.2s, box-shadow 0.2s;
}

.btn-primary:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(79, 70, 229, 0.4);
}
```

#### Social Login Buttons
```css
.btn-social {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  width: 100%;
  padding: 12px;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  background: white;
  transition: background 0.2s;
}

.btn-social:hover {
  background: var(--bg-secondary);
}

.btn-social img {
  width: 20px;
  height: 20px;
}
```

#### Dark Mode Theme
```css
:root {
  --bg-primary: #0f172a;
  --bg-secondary: #1e293b;
  --bg-card: #1e293b;
  --text-primary: #f1f5f9;
  --text-secondary: #94a3b8;
  --border-color: #334155;
}

body {
  background: var(--bg-primary);
}

.journey-container {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
}

.form-input {
  background: var(--bg-secondary);
  border-color: var(--border-color);
  color: var(--text-primary);
}
```

#### Custom Form Inputs
```css
.form-input {
  width: 100%;
  padding: 12px 16px;
  border: 2px solid var(--border-color);
  border-radius: 8px;
  font-size: 16px;
  transition: border-color 0.2s, box-shadow 0.2s;
}

.form-input:focus {
  outline: none;
  border-color: var(--primary-color);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.form-input::placeholder {
  color: var(--text-muted);
}

.form-input.error {
  border-color: #ef4444;
}
```

#### Animated Loader
```css
.loading-spinner {
  width: 40px;
  height: 40px;
  border: 3px solid var(--border-color);
  border-top-color: var(--primary-color);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
```

### Per-Tenant Branding

Configure tenant-wide branding in tenant settings:

```json
{
  "branding": {
    "primaryColor": "#3b82f6",
    "secondaryColor": "#64748b",
    "logoUrl": "https://cdn.example.com/logo.png",
    "faviconUrl": "https://cdn.example.com/favicon.ico",
    "customCss": ".journey-container { max-width: 400px; }"
  }
}
```

### Branding Priority

When multiple styling sources exist:

1. Journey policy `CustomCss` (highest priority)
2. Journey policy `PrimaryColor`/`BackgroundColor`
3. Journey policy `Theme`
4. Tenant `Branding.CustomCss`
5. Tenant `Branding.PrimaryColor`/`SecondaryColor`
6. Default theme (lowest priority)

### Theme Options

| Theme | Description |
|-------|-------------|
| `default` | Standard light theme |
| `light` | Clean, minimal light theme |
| `dark` | Dark mode theme |
| `minimal` | Ultra-minimal, reduced chrome |

### Responsive Design

Built-in styles are mobile-responsive. Custom CSS should follow:

```css
/* Mobile-first approach */
.journey-container {
  padding: 1rem;
}

/* Tablet and up */
@media (min-width: 640px) {
  .journey-container {
    padding: 2rem;
  }
}

/* Desktop */
@media (min-width: 1024px) {
  .journey-container {
    max-width: 480px;
    margin: 4rem auto;
  }
}
```

### Testing Custom CSS

1. Use browser DevTools to test styles live
2. Preview in Admin UI journey builder
3. Test across different devices and browsers
4. Verify accessibility (contrast ratios, focus states)

---

## Platform Billing

Platform Billing enables you to bill your tenants for using the identity platform. This is a two-tier billing model where you (the platform operator) bill tenants, and optionally tenants can bill their end users.

### Architecture Overview

```
Platform Operator
       │
       ▼
┌─────────────────┐
│ Platform Plans  │ ← Plans you offer to tenants (Starter, Pro, Enterprise)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│    Tenants      │ ← Each tenant subscribes to a platform plan
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  End Users      │ ← Tenants can optionally bill their users
└─────────────────┘
```

### Platform Plans

Platform plans define what you charge tenants for using the identity service.

#### Creating a Platform Plan

Navigate to **Platform Billing** in the Admin UI (SuperAdmin only):

1. Click **Add Plan**
2. Configure basic settings:
   - **Plan ID**: Unique identifier (e.g., `starter`, `pro`, `enterprise`)
   - **Name**: Internal name
   - **Display Name**: Shown to tenants
   - **Description**: Plan benefits description
   - **Price**: Base price in cents (e.g., 9900 = $99.00)
   - **Currency**: USD, EUR, GBP
   - **Billing Interval**: Monthly or Yearly
   - **Trial Days**: Free trial period

#### Plan Features

Configure which features are included in each plan:

| Feature Key | Description |
|-------------|-------------|
| `custom_domain` | Allow custom authentication domains |
| `custom_branding` | Full UI customization |
| `advanced_mfa` | Hardware keys, biometrics |
| `sso_connections` | Enterprise SSO (SAML, OIDC) |
| `audit_logs` | Extended audit log retention |
| `api_access` | Management API access |
| `webhooks` | Webhook integrations |
| `user_subscriptions` | Tenant can bill their end users |

#### Plan Limits

Set usage limits per plan:

| Limit | Description |
|-------|-------------|
| `maxMonthlyActiveUsers` | MAU limit (-1 = unlimited) |
| `maxTotalUsers` | Total user limit |
| `maxClients` | OAuth client limit |
| `maxApiRequestsPerMonth` | API rate limit |
| `maxAuthRequestsPerMonth` | Auth request limit |
| `auditLogRetentionDays` | Log retention period |

#### Metered Billing

Configure usage-based pricing for overages:

```json
{
  "meteredPricing": [
    {
      "usageType": "mau",
      "includedQuantity": 1000,
      "pricePerUnitInCents": 10,
      "unitName": "user",
      "billingIncrement": 1
    }
  ]
}
```

### Stripe Integration

Platform billing integrates with Stripe for payment processing.

#### Configuration

```json
{
  "Stripe": {
    "SecretKey": "sk_live_xxx",
    "PublishableKey": "pk_live_xxx",
    "WebhookSecret": "whsec_xxx"
  }
}
```

#### Syncing Plans with Stripe

When you create a platform plan with an external price ID, it syncs with Stripe:

```csharp
// In Admin UI, set the External Plan ID to your Stripe Price ID
ExternalPlanId = "price_1234567890"
```

#### Webhook Events

Configure your Stripe webhook to send events to:
```
POST /api/payments/webhook
```

Required events:
- `customer.subscription.created`
- `customer.subscription.updated`
- `customer.subscription.deleted`
- `invoice.paid`
- `invoice.payment_failed`
- `checkout.session.completed`

### API Endpoints

#### List Platform Plans

```http
GET /api/tenant-billing/plans
Authorization: Bearer {token}
```

#### Get Tenant Subscription

```http
GET /api/tenant-billing/subscription
Authorization: Bearer {token}
X-Tenant-Id: {tenantId}
```

#### Subscribe to Plan

```http
POST /api/tenant-billing/subscribe
Authorization: Bearer {token}
Content-Type: application/json

{
  "planId": "pro",
  "paymentMethodId": "pm_xxx"
}
```

#### Record Usage (for metered billing)

```http
POST /api/tenant-billing/usage
Authorization: Bearer {token}
Content-Type: application/json

{
  "usageType": "mau",
  "quantity": 150,
  "timestamp": "2025-01-15T00:00:00Z"
}
```

---

## Subscription Plans

Subscription Plans allow tenants to monetize their applications by billing their end users. This is the second tier of the billing model.

### Overview

Tenants can create subscription plans for their users, enabling SaaS business models:

- **Free tier** with limited features
- **Paid tiers** with premium features
- **Usage-based billing** for API calls, storage, etc.

### Enabling User Subscriptions

User subscriptions must be enabled at the platform level:

1. Create a platform plan with `allowUserSubscriptions: true`
2. Tenant subscribes to that plan
3. Tenant can now create subscription plans for their users

### Creating Subscription Plans

Tenants create plans via Admin UI or API:

#### Admin UI

Navigate to **Subscriptions** > **Plans**:

1. Click **Create Plan**
2. Configure:
   - **Name**: Plan name (e.g., "Pro")
   - **Display Name**: Shown to users
   - **Description**: Benefits description
   - **Price**: Amount in cents
   - **Billing Interval**: Monthly, Yearly, Lifetime, OneTime
   - **Trial Days**: Free trial period
   - **Features**: Key-value feature flags

#### API

```http
POST /api/subscriptions/plans
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "pro",
  "displayName": "Pro Plan",
  "description": "For power users",
  "priceInCents": 1999,
  "currency": "USD",
  "billingInterval": "monthly",
  "trialDays": 14,
  "features": [
    { "key": "api_calls", "value": "10000" },
    { "key": "storage_gb", "value": "50" },
    { "key": "priority_support", "value": "true" }
  ],
  "externalPlanId": "price_stripe_xxx"
}
```

### Plan Features

Features are key-value pairs that your application checks:

```json
{
  "features": [
    { "key": "max_projects", "value": "10" },
    { "key": "api_rate_limit", "value": "1000" },
    { "key": "advanced_analytics", "value": "true" },
    { "key": "white_label", "value": "false" }
  ]
}
```

### User Subscription Flow

#### 1. User Views Plans

```http
GET /api/subscriptions/plans
```

Returns active plans for the tenant.

#### 2. User Subscribes

**Option A: Direct subscription (payment method on file)**
```http
POST /api/subscriptions/subscribe
{
  "planId": "pro",
  "paymentMethodId": "pm_xxx"
}
```

**Option B: Hosted checkout (Stripe Checkout)**
```http
POST /api/subscriptions/checkout
{
  "planId": "pro",
  "successUrl": "https://app.example.com/success",
  "cancelUrl": "https://app.example.com/cancel"
}
```

Returns a checkout URL to redirect the user.

#### 3. Check Subscription Status

```http
GET /api/subscriptions/current
```

```json
{
  "id": "sub_xxx",
  "planId": "pro",
  "planName": "Pro Plan",
  "status": "active",
  "currentPeriodEnd": "2025-02-15T00:00:00Z",
  "cancelAtPeriodEnd": false,
  "features": {
    "max_projects": "10",
    "api_rate_limit": "1000"
  }
}
```

### Subscription Statuses

| Status | Description |
|--------|-------------|
| `active` | Subscription is active and paid |
| `trialing` | In free trial period |
| `past_due` | Payment failed, in grace period |
| `canceled` | Subscription canceled |
| `incomplete` | Initial payment pending |
| `expired` | Trial or subscription expired |

### Managing Subscriptions

#### Cancel Subscription

```http
POST /api/subscriptions/cancel
```

By default, cancels at period end. User retains access until then.

#### Reactivate Subscription

```http
POST /api/subscriptions/reactivate
```

Removes the cancellation if still within the billing period.

#### Change Plan

```http
POST /api/subscriptions/change-plan
{
  "newPlanId": "enterprise"
}
```

Prorates the change automatically.

### Billing Page

Users can manage their subscription at:
```
/Account/Billing
```

This page shows:
- Current plan and status
- Available plans to upgrade/downgrade
- Payment method management
- Billing history

### Checking Features in Your Application

#### Server-Side (C#)

```csharp
public class MyService
{
    private readonly ISubscriptionService _subscriptions;

    public async Task<bool> CanAccessFeatureAsync(string userId, string feature)
    {
        var subscription = await _subscriptions.GetUserSubscriptionAsync(userId);
        if (subscription == null) return false;

        return subscription.HasFeature(feature);
    }

    public async Task<int> GetFeatureLimitAsync(string userId, string feature)
    {
        var subscription = await _subscriptions.GetUserSubscriptionAsync(userId);
        return subscription?.GetFeatureValue<int>(feature) ?? 0;
    }
}
```

#### Client-Side (JavaScript)

```javascript
// Fetch current subscription
const response = await fetch('/api/subscriptions/current', {
  headers: { 'Authorization': `Bearer ${token}` }
});
const subscription = await response.json();

// Check feature
if (subscription.features.advanced_analytics === 'true') {
  showAnalyticsDashboard();
}

// Check limit
const maxProjects = parseInt(subscription.features.max_projects) || 3;
if (userProjects.length >= maxProjects) {
  showUpgradePrompt();
}
```

### Webhooks for Subscription Events

Subscribe to these webhook events in your application:

| Event | Description |
|-------|-------------|
| `subscription.created` | New subscription started |
| `subscription.updated` | Plan changed, renewed |
| `subscription.canceled` | Subscription canceled |
| `subscription.trial_ending` | Trial ends in 3 days |
| `payment.succeeded` | Payment processed |
| `payment.failed` | Payment failed |

Example webhook payload:
```json
{
  "event": "subscription.created",
  "timestamp": "2025-01-15T10:30:00Z",
  "data": {
    "subscriptionId": "sub_xxx",
    "userId": "user_123",
    "planId": "pro",
    "status": "trialing",
    "trialEnd": "2025-01-29T10:30:00Z"
  }
}
```

### Best Practices

1. **Always validate server-side**: Don't trust client-side feature checks alone
2. **Handle grace periods**: Give users time to fix payment issues
3. **Provide clear upgrade paths**: Show users what they're missing
4. **Send trial ending notifications**: Email users before trial expires
5. **Offer annual discounts**: Incentivize longer commitments
6. **Monitor churn**: Track cancellation reasons

---

## OIDC Extensions

### Custom Parameters

#### `ui_mode`
Override flow type per request:
```
/connect/authorize?...&ui_mode=standalone
```
Values: `journey`, `standalone`

#### `policy` or `p`
Specify exact journey policy (Azure AD B2C compatible):
```
/connect/authorize?...&policy=B2C_1_signup
/connect/authorize?...&p=B2C_1_signin
```

#### `prompt=create`
Force registration flow (OIDC standard):
```
/connect/authorize?...&prompt=create
```

### ACR Values

Use ACR values to select policy type:
```
/connect/authorize?...&acr_values=signup
/connect/authorize?...&acr_values=password_reset
```

Supported values:
- `signup` - Sign-up flow
- `signin_signup` - Combined flow
- `password_reset` - Password reset
- `profile_edit` - Profile editing

---

## DPoP (Demonstrating Proof of Possession)

DPoP binds tokens to a client's key pair, preventing token theft.

### Enabling DPoP

Per-client in Admin UI:
1. Edit client
2. Enable **Require DPoP**

Or via configuration:
```json
{
  "requireDPoP": true
}
```

### Client Implementation

1. Generate a key pair (ES256 or RS256)
2. Create DPoP proof JWT for each request
3. Include `DPoP` header in token requests

```javascript
// DPoP Proof Header
{
  "typ": "dpop+jwt",
  "alg": "ES256",
  "jwk": { /* public key */ }
}

// DPoP Proof Payload
{
  "jti": "unique-id",
  "htm": "POST",
  "htu": "https://auth.example.com/connect/token",
  "iat": 1234567890
}
```

### Token Request

```http
POST /connect/token
DPoP: eyJhbGciOiJFUzI1NiIsInR5cCI6ImRwb3Arand0IiwiandrIjp7Li4ufX0...
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&code=...&dpop_jkt=thumbprint
```

---

## Pushed Authorization Requests (PAR)

PAR improves security by pushing authorization parameters server-side.

### Enabling PAR

Per-client:
```json
{
  "requirePushedAuthorization": true
}
```

### PAR Flow

1. **Push Request**
```http
POST /connect/par
Content-Type: application/x-www-form-urlencoded

client_id=my-client&redirect_uri=...&scope=openid&response_type=code
```

Response:
```json
{
  "request_uri": "urn:ietf:params:oauth:request_uri:abc123",
  "expires_in": 60
}
```

2. **Authorization Request**
```http
GET /connect/authorize?client_id=my-client&request_uri=urn:ietf:params:oauth:request_uri:abc123
```

### Benefits

- Sensitive parameters not exposed in browser URL
- Request integrity protection
- Supports complex/large authorization requests

---

## Troubleshooting

### Common Issues

#### Journey not starting
- Check `UseJourneyFlow` is enabled
- Verify policy exists and is enabled
- Check policy type matches request (SignIn vs SignUp)

#### Custom CSS not applying
- Ensure CSS is valid
- Check browser developer tools for errors
- Verify policy is correctly loaded

#### FIDO2 not working
- Ensure HTTPS is configured
- Verify `ServerDomain` matches your domain
- Check browser WebAuthn support

#### Plugin not executing
- Verify plugin is uploaded and enabled
- Check plugin logs for errors
- Ensure plugin exports required functions

### Logging

Enable detailed logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "IdentityServer": "Debug",
      "IdentityServer.UserJourneys": "Debug",
      "IdentityServer.Plugins": "Debug"
    }
  }
}
```

---

## API Reference

See [API Documentation](./API_REFERENCE.md) for complete endpoint documentation.

---

## Changelog

See [CHANGELOG.md](../CHANGELOG.md) for version history and updates.

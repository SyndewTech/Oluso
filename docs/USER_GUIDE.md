# Identity Server User Guide

This guide covers the configuration and usage of the Identity Server's features including multi-tenancy, user journeys, authentication methods, and extensibility via WASM plugins.

---

## Table of Contents

1. [Multi-Tenancy](#multi-tenancy)
2. [Authentication Flows](#authentication-flows)
3. [User Journeys](#user-journeys)
4. [Journey Step Types Reference](#journey-step-types-reference)
5. [FIDO2/WebAuthn (Passkeys)](#fido2webauthn-passkeys)
   - [Business Verification (KYB)](#business-verification-kyb)
6. [Multi-Factor Authentication (MFA)](#multi-factor-authentication-mfa)
7. [Self-Service Registration](#self-service-registration)
8. [External Identity Providers](#external-identity-providers)
9. [SAML, LDAP and SCIM](#saml-ldap-and-scim)
10. [WASM Plugins](#wasm-plugins)
11. [Client Configuration](#client-configuration)
12. [Signing Keys](#signing-keys)
13. [Events and Webhooks](#events-and-webhooks)
14. [Audit Logging](#audit-logging)
15. [Custom Styling](#custom-styling)
16. [OIDC Extensions](#oidc-extensions)
17. [DPoP (Demonstrating Proof of Possession)](#dpop-demonstrating-proof-of-possession)
18. [Pushed Authorization Requests (PAR)](#pushed-authorization-requests-par)
19. [Troubleshooting](#troubleshooting)

---

## Multi-Tenancy

The Identity Server supports multi-tenant deployments where each tenant has isolated users, clients, and configuration. Enable it with `AddMultiTenancy()` on the Oluso builder (`builder.Services.AddOluso(configuration).AddMultiTenancy(...)`).

### Tenant Resolution

For browser and other unauthenticated requests the tenant is resolved, in order, from:

1. **Subdomain**: `tenant1.auth.example.com`, when a base domain is known. `MultiTenancyOptions.BaseDomain` defaults to the host of `Oluso:IssuerUri`. Reserved subdomains (`www`, `api`, `admin`, `auth`, `login`, `mail`, `cdn`, `docs` and others in `ReservedSubdomains`) are never treated as tenants.
2. **Custom domain**: the request host matched against an enabled tenant's custom domain.
3. **Header**: `X-Tenant-Id: tenant1` (`TenantHeaderName`).
4. **Query string**: `?tenant=tenant1` (`TenantQueryParameterName`).

Authenticated API requests use the `X-Tenant-Id` header when present (access is checked against the token's allowed tenants) and otherwise the token's `tenant_id` claim. When nothing resolves, `DefaultTenantId` (the platform tenant) applies.

### Tenant Settings

Each tenant can configure:

| Setting | Description | Default |
|---------|-------------|---------|
| `UseJourneyFlow` | Use journey-based authentication vs standalone pages | `false` |
| `JourneyFallbackToStandard` | With journeys on, use the standard pages when a sign-in names no policy and no enabled policy applies (otherwise the sign-in stops with an error) | `false` |
| `AllowSelfRegistration` | Allow users to register themselves | `true` |
| `RequireTermsAcceptance` | Require ToS acceptance during registration | `false` |
| `TermsOfServiceUrl` | URL to terms of service document | - |
| `PrivacyPolicyUrl` | URL to privacy policy document | - |
| `RequireEmailVerification` | Require email verification before sign-in | `true` |
| `AllowedEmailDomains` | Comma-separated list of allowed email domains | - (all allowed) |
| `EnableLocalLogin` | Offer username/password sign-in | `true` |
| `EnablePasskey` | Offer passkey sign-in (also skips the passkey journey steps when off) | `true` |

### Admin UI Configuration

Navigate to **Settings** > **General**. The **Authentication Flow Settings** card holds the journey settings and the **User Registration & Authentication** card holds the registration options.

---

## Authentication Flows

The server supports two authentication flow modes:

### Journey-Based Flow

Step-by-step authentication with full control over the user experience. Journeys are defined as policies with ordered steps. Requires `AddUserJourneyEngine()` on the Oluso builder.

**Enable**: Set `UseJourneyFlow = true` on the tenant. A client can opt out with its own `UseJourneyFlow = false`.

### Standalone Pages

Traditional login/registration pages. This is what a newly provisioned tenant uses until journeys are switched on.

**Enable**: Set `UseJourneyFlow = false` at tenant or client level.

### Priority Order

Flow selection follows this priority:
1. Tenant `UseJourneyFlow`: when `false`, the standalone pages are used and nothing below can override it.
2. Client `UseJourneyFlow`: `false` forces the standalone pages for that client; `true` or unset (inherit) allows journeys.
3. Request `ui_mode` parameter (`journey` or `standalone`), only within what the tenant and client allow.
4. Otherwise: journey flow.

---

## User Journeys

User Journeys are customizable authentication flows composed of steps.

### Creating a Journey

1. Navigate to **Experience** > **User Journeys** in the Admin UI
2. Click **Create Journey**
3. Configure:
   - **Name**: Display name for the journey
   - **Slug**: The identifier clients pass as `?policy=` (unique per tenant)
   - **Type**: `SignIn`, `SignUp`, `SignInSignUp`, `PasswordReset`, `PasswordChange`, `ProfileEdit`, `LinkAccount`, `Consent`, data collection types (`Waitlist`, `ContactForm`, `Survey`, `Feedback`, `DataCollection`) or `Custom`
   - **Priority**: When a sign-in names no policy, the enabled policy of the requested type with the highest priority is used

### Available Step Types

Step types are identified by the snake_case names below. Steps from optional modules (passkeys, LDAP, SAML, business verification) only appear when that module is registered.

#### Authentication Steps
| Step Type | Description |
|-----------|-------------|
| `local_login` | Username/password authentication (plus optional passkey) |
| `composite_login` | Password, passkey and external providers on one screen |
| `external_login` | OAuth/OIDC external provider |
| `mfa` | Multi-factor authentication (TOTP, SMS, email) |
| `passwordless_email` | Email one-time code or magic link |
| `passwordless_sms` | SMS one-time code |
| `webauthn` | FIDO2/passkey authentication |
| `fido2_login` | Passkey sign-in |
| `ldap` | LDAP / Active Directory authentication |
| `saml` | SAML 2.0 federation |

#### User Interaction Steps
| Step Type | Description |
|-----------|-------------|
| `consent` | OAuth consent screen |
| `dynamic_form` | Collect additional user information |
| `email_verification` | Prove ownership of an email address without signing in |
| `terms_acceptance` | Terms of service / privacy policy acceptance |
| `captcha` | Bot protection |

#### Flow Control Steps
| Step Type | Description |
|-----------|-------------|
| `condition` | Conditional branching |
| `branch` | Multi-way branching |
| `transform` | Derive and transform journey values |
| `api_call` | Call external API |
| `webhook` | Send webhook notification |

#### User Management Steps
| Step Type | Description |
|-----------|-------------|
| `signup` | Self-registration: collects email/password and creates a local user |
| `create_user` | Alias of `signup` (same settings and handler) |
| `update_user` | Update user profile |
| `assign_roles` | Give the journey's user roles |
| `link_account` | Link external identity |
| `password_change` | Change password |
| `password_reset` | Reset forgotten password |
| `fido2_register` | Offer to create a passkey |
| `business_verification` | Business (KYB) verification, see [Business Verification](#business-verification-kyb) |

#### Plugin Steps
| Step Type | Description |
|-----------|-------------|
| `custom_plugin` | Execute WASM or managed plugin |
| `custom_page` | Render a host-provided Razor template |

### Step Configuration Example

```json
{
  "id": "login-step",
  "type": "local_login",
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
- `branches`: Named outcomes mapped to step IDs (for example `"mfa_required": "mfa-step"`). Each step type publishes the branch names it can take; the journey builder lists them.
- `optional`: If true, continue journey even if step fails
- `conditions`: Conditions that must be met to execute step (`type` is the value source, `field`, `operator`, `value`, `logicalOperator`, `negate`)
- `layout`: Width of this step's screen (`narrow`, `medium`, `wide`, `full`), overriding the policy's layout

### Localizable Text

Settings marked localizable in the builder (titles, labels, button text) take either a plain string or an object of translations keyed by culture, for example `{ "en": "Continue", "fr": "Continuer" }`.

---

## Journey Step Types Reference

This section provides detailed configuration options for each step type available in the Admin UI Journey Builder. The builder renders each step's settings form from the schema the step type publishes, so the field names below are the keys stored in the step's `configuration`.

### Local Login (`local_login`)

Authenticate users with username/email and password.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `allowRememberMe` | boolean | `true` | Show "Remember me" checkbox |
| `allowSelfRegistration` | boolean | `false` | Show link to self-registration |
| `allowForgotPassword` | boolean | `true` | Show "Forgot password?" link |
| `showPasskey` | boolean | `true` | Show passkey/WebAuthn sign-in option |
| `passkeyUsernameless` | boolean | `true` | Allow passkey sign-in without entering an email first |
| `loginHintClaim` | string | - | Claim to pre-fill the username from when present in journey state |
| `reauthenticateAfter` | number | `0` | When the policy runs for someone already signed in, their session counts as this sign-in only if it signed in within this many seconds. Otherwise they sign in again, pre-filled, and only as that same account. `0` accepts any live session. Composite Login takes the same setting. |

Branches: `auth_local` (password), `auth_passkey` (passkey).

**Example**:
```json
{
  "type": "local_login",
  "configuration": {
    "allowRememberMe": true,
    "allowSelfRegistration": true,
    "loginHintClaim": "email"
  }
}
```

---

### Composite Login (`composite_login`)

One screen with password, passkey and external provider sign-in.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `showLocalLogin` | boolean | `true` | Display username/password form |
| `showExternalLogin` | boolean | `true` | Display external provider buttons |
| `showPasskey` | boolean | `true` | Display passkey sign-in option |
| `passkeyUsernameless` | boolean | `true` | Allow passkey sign-in without entering an email first |
| `allowRememberMe` | boolean | `true` | Show "Remember me" checkbox |
| `allowRegistration` | boolean | `false` | Show link to registration |
| `allowForgotPassword` | boolean | `true` | Show "Forgot password?" link |
| `forgotPasswordUrl` / `registrationUrl` | string | - | Override URLs for those links |
| `autoProvision` | boolean | `true` | Create the user on first external sign-in |
| `externalProviders` | string[] | - | Allowed provider schemes (empty allows all) |
| `primaryAction` | string | `"local"` | Which action to emphasise: `local`, `external`, `passkey` |
| `loginLayout` | string | `"standard"` | Order of the methods: `standard`, `social-first`, `passkey-first`, `email-expandable` |
| `reauthenticateAfter` | number | `0` | As for Local Login |

Branches: `auth_local`, `auth_passkey`, `auth_external`, `mfa_required`.

---

### External Login (`external_login`)

Authenticate via external identity providers (Google, Microsoft, etc.).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `providers` | string[] | - | Allowed identity provider schemes (empty allows all) |
| `autoProvision` | boolean | `true` | Auto-create user on first login |
| `autoRedirect` | boolean | `false` | Auto-redirect when only one provider matches |
| `proxyMode` | boolean | `false` | Pass external claims through without creating a local user (federation broker) |
| `claimMappings` | object | - | Provider claims to carry into the journey: external claim type to claim name. The account's own identity claims (`sub`, `email`, `name`) aren't replaced. |

Branches: `auth_external`, `auth_proxy`.

**Example**:
```json
{
  "type": "external_login",
  "configuration": {
    "providers": ["Google", "Microsoft"],
    "autoProvision": true,
    "autoRedirect": false,
    "claimMappings": {
      "picture": "profile_picture"
    }
  }
}
```

---

### MFA (`mfa`)

Require an additional verification factor.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `required` | boolean | `false` | Always require MFA. When false, MFA is only required for users who already have it enabled. |
| `methods` | string[] | `["totp", "email"]` | Allowed methods: `totp`, `sms` (`phone` is accepted as an alias), `email` |
| `allowSetup` | boolean | `true` | Let users set up MFA during the flow when they don't have it yet |
| `rememberDevice` | boolean | `true` | Skip MFA on trusted devices |
| `rememberDeviceDays` | number | `30` | Days to remember a trusted device |
| `reuseSessionMfaSeconds` | number | `0` | When the policy runs for a signed-in user, accept MFA that session completed within this many seconds. `0` always asks again. |

Branches: `mfa_verified`, `mfa_recovery_used`, `mfa_not_required`, `mfa_setup_complete`, `mfa_trusted_device`, `mfa_session`.

**Example**:
```json
{
  "type": "mfa",
  "configuration": {
    "required": true,
    "methods": ["totp", "email"],
    "rememberDevice": true,
    "rememberDeviceDays": 14
  }
}
```

---

### Passwordless Email (`passwordless_email`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `mode` | string | `"otp"` | `otp` (one-time code) or `magic-link` |
| `codeLength` | number | `6` | Digits in the code |
| `expirationMinutes` | number | `15` | Minutes before the code or link expires |
| `allowSignUp` | boolean | `false` | Allow new users to register when no account exists for the email |
| `autoProvision` | boolean | `false` | With sign-up allowed, create the account inline (username = email, no password) |
| `maxAttempts` | number | `3` | Invalid-code attempts before the journey fails (`0` = unlimited) |
| `emailSubject` | string | - | Overrides the rendered template subject |
| `emailTemplate` | string | - | Platform email template name to use instead of the default for the mode |

### Passwordless SMS (`passwordless_sms`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `codeLength` | number | `6` | Digits in the code |
| `expirationMinutes` | number | `10` | Minutes before the code expires |
| `allowSignUp` | boolean | `false` | Allow new users to register when no account exists for the number |
| `maxAttempts` | number | `3` | Invalid-code attempts before the journey fails (`0` = unlimited) |
| `messageTemplate` | string | - | Custom SMS text; supports `{code}` and `{expirationMinutes}` |
| `toLinkRecipient` | boolean | `false` | Verify the phone a journey link was sent to, without signing anyone in |

### Email Verification (`email_verification`)

Proves ownership of an email address without authenticating, and writes `email` and `email_verified=true` into journey state. Takes `mode`, `codeLength`, `expirationMinutes`, `maxAttempts`, `emailSubject` and `emailTemplate` as for Passwordless Email, plus:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `completeSignUp` | boolean | `true` | After sign-up, verifying the new account's own address marks it verified and signs the user in |
| `toLinkRecipient` | boolean | `false` | Verify the address a journey link was sent to; runs not opened from such a link skip the step |

---

### Consent (`consent`)

Show OAuth consent screen for scope approval.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `allowRemember` | boolean | `true` | Allow user to remember consent |
| `rememberDays` | number | `365` | Days to remember consent |
| `showResourceScopes` | boolean | `true` | Show API resource scopes |
| `showIdentityScopes` | boolean | `true` | Show identity scopes |

Branches: `consent_granted`, `consent_not_required`, `consent_already_granted`.

**Example**:
```json
{
  "type": "consent",
  "configuration": {
    "allowRemember": true,
    "rememberDays": 90,
    "showResourceScopes": true,
    "showIdentityScopes": true
  }
}
```

---

### Dynamic Form (`dynamic_form`)

Collect custom user information with configurable form fields, optionally saving values to the user's profile.

#### Basic Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `title` | string | - | Form title displayed to user (localizable) |
| `description` | string | - | Form description/instructions (localizable) |
| `submitButtonText` | string | `"Continue"` | Submit button label (localizable) |
| `cancelButtonText` | string | - | Cancel button label (localizable) |
| `allowCancel` | boolean | `false` | Show cancel button |
| `viewName` | string | `"Journey/_DynamicForm"` | Custom view name |

#### Field Configuration

Each field in the `fields` array supports:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `name` | string | **required** | Field name (form input name) |
| `type` | string | **required** | `text`, `email`, `password`, `number`, `date`, `tel`, `url`, `textarea`, `select`, `radio`, `checkbox`, `file` |
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
| `phoneFormat` | string | `"international"` | Phone fields: `international` (country picker, stored as E.164) or `local` |
| `phoneDefaultCountry` | string | - | Phone fields: ISO 3166-1 alpha-2 country preselected (defaults to the journey culture's region) |
| `readOnly` | boolean | `false` | Field is read-only |
| `hidden` | boolean | `false` | Field is hidden |
| `group` | string | - | Group name for fieldset grouping |
| `persistToUser` | boolean | `false` | Save the value to the user's profile |
| `userPropertyName` | string | - | User property to save to (e.g. `firstName`, `lastName`, `phoneNumber`, `picture`, or a custom property key) |
| `options` | array | - | Options for select/radio fields |
| `localizedLabels` / `localizedPlaceholders` / `localizedDescriptions` | object | - | Translations by culture |
| `fileTypes` | array | `["pdf","jpeg","png"]` | File fields: kinds accepted (`pdf`, `jpeg`, `png`, `gif`, `webp`, `heic`) |
| `maxFileSizeMb` | number | `10` | File fields: largest file, in MB |
| `maxFiles` | number | `1` | File fields: most files |
| `capture` | boolean | `false` | File fields: also offer "Take a photo" (opens the phone's rear camera) |

**File fields.** The form posts as `multipart/form-data`. Each file is checked by its
content (magic bytes), never by the name or type the browser sends, and its name must end
in an extension matching that content, so `invoice.pdf` that is really an image is refused.
SVG, HTML and other active formats are never accepted. Accepted files are stored through the
attachment service under the organisation's storage path (local disk or blob storage),
recorded as attachments of the journey (`journey_upload`), and the field's value is the
comma-separated attachment ids. There is no public URL: the uploader (once the journey has
signed them up or in) and administrators with `submissions.read` open them through
`GET /api/workspace/file-attachments/{id}`. Files posted before the account exists are
reassigned to the user when the journey signs them up or in. Request size: Kestrel's
default 30 MB request limit applies to the whole form; keep `maxFileSizeMb × maxFiles`
under it or raise `Kestrel:Limits:MaxRequestBodySize` on the host.

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

**Admin UI Location**: Select "Dynamic Form" in the journey builder. The builder provides a visual field editor.

**Example**:
```json
{
  "type": "dynamic_form",
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
          { "value": "support", "label": "Customer Support" }
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
        "persistToUser": true,
        "userPropertyName": "phoneNumber"
      }
    ]
  }
}
```

---

### CAPTCHA (`captcha`)

Verify user is human via CAPTCHA. Supports reCAPTCHA, hCaptcha, and Cloudflare Turnstile.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `provider` | string | `"recaptcha"` | `recaptcha`, `hcaptcha`, `cloudflare` |
| `siteKey` | string | **required** | CAPTCHA site key from provider |
| `secretKey` | string | **required** | CAPTCHA secret key from provider |
| `scoreThreshold` | number | `0.5` | Minimum score for reCAPTCHA v3 (0.0-1.0) |
| `action` | string | `"submit"` | Action label sent with the challenge (reCAPTCHA v3) |
| `theme` | string | `"light"` | `light` or `dark` |
| `size` | string | `"normal"` | `normal`, `compact`, or `invisible` |
| `language` | string | `"en"` | Widget language (e.g., `en`, `es`, `fr`) |

**Example**:
```json
{
  "type": "captcha",
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

### API Call (`api_call`)

Call external APIs for validation, enrichment, or integration. The builder groups the settings into collapsible sections.

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
| `authentication.type` | string | `none`, `bearer`, `basic`, `apikey`, `client_credentials` |
| `authentication.token` | string | Bearer token (supports `{claim:access_token}`) |
| `authentication.username` | string | Basic auth username |
| `authentication.password` | string | Basic auth password |
| `authentication.apiKey` | string | API key value |
| `authentication.headerName` | string | Header name for API key (default: `X-API-Key`) |
| `authentication.tokenEndpoint` | string | OAuth 2.0 token endpoint (client_credentials) |
| `authentication.clientId` / `clientSecret` | string | OAuth 2.0 client credentials |
| `authentication.scope` | string | Space-separated scopes (optional) |
| `authentication.resource` | string | Resource indicator(s) (RFC 8707) the token is for |
| `authentication.audience` | string | `audience` parameter, for providers that read it instead of `resource` |

Client-credentials tokens are cached and refreshed shortly before they expire.

#### Retry & Error Handling

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `retryCount` | number | `0` | Number of retry attempts |
| `retryDelayMs` | number | `1000` | Delay between retries (ms) |
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
| `inputMapping[field].required` | boolean | Include even if empty |

#### Response Handling

| Property | Type | Description |
|----------|------|-------------|
| `outputMapping` | object | Map JSON paths to claim types (e.g., `{"user.id": "external_id"}`) |
| `outputMappingAdvanced` | object | Per-claim `path`, `transform`, `defaultValue`, `required` |
| `includeResponseMeta` | boolean | Add `_api_status` and `_api_success` claims |

#### Response Validation

Array of validation rules (`responseValidation`):

| Property | Type | Description |
|----------|------|-------------|
| `path` | string | JSON path to validate |
| `type` | string | `required`, `equals`, `notequals`, `contains`, `matches`, `in`, `notin` |
| `expectedValue` | string | Value for equals/contains |
| `pattern` | string | Regex for matches |
| `allowedValues` | string[] | Values for `in` validation |
| `errorMessage` | string | Custom error message |

#### Branch on Response

Array of branching rules (`branchOnResponse`):

| Property | Type | Description |
|----------|------|-------------|
| `onStatus` | number[] | HTTP status codes to match |
| `path` | string | JSON path to check |
| `condition` | string | `equals`, `notequals`, `exists`, `notexists`, `contains`, `true`, `false` |
| `value` | string | Value to compare |
| `branchTo` | string | Step ID to branch to |

**Example**:
```json
{
  "type": "api_call",
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
    "retryDelayMs": 1000,
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

### Webhook (`webhook`)

POST a payload to an external service during the journey.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `url` | string | **required** | Endpoint URL (supports `{claim:name}` / `{state:key}`) |
| `timeout` | number | `30` | Request timeout in seconds |
| `waitForResponse` | boolean | `false` | Block the journey until the webhook returns (false = fire-and-forget) |
| `failOnError` | boolean | `false` | Fail the journey on a non-2xx response (only with `waitForResponse`) |
| `headers` | object | - | Custom HTTP headers |
| `payload` | object | - | JSON payload template; text values may use placeholders such as `{claim:email}`. Empty sends the default payload with user and journey context. |
| `responseMapping` | object | - | Map response JSON paths to claim types (only with `waitForResponse`) |
| `includeUserData` | boolean | `true` | Include user data in the default payload |
| `includeJourneyData` | boolean | `false` | Include journey values in the default payload |
| `journeyDataKeys` | string[] | - | Journey values to include; empty sends the standard identity claims (`Oluso:UserJourneys:DefaultOutputClaims`) |
| `signPayload` | boolean | `false` | Sign the body with HMAC-SHA256 and add an `X-Oluso-Signature` header |
| `secretKey` | string | - | HMAC secret (used with `signPayload`) |

**Example**:
```json
{
  "id": "notify-signup",
  "type": "webhook",
  "displayName": "Notify onboarding service",
  "configuration": {
    "url": "https://onboarding.example.com/api/new-user",
    "headers": { "X-Source": "identity-server" },
    "payload": {
      "email": "{claim:email}",
      "name": "{claim:name}"
    },
    "timeout": 10,
    "waitForResponse": false,
    "signPayload": true,
    "secretKey": "your-shared-secret"
  }
}
```

---

### Condition (`condition`)

Evaluate conditions and continue to a different step depending on the result.

#### Conditions Array

| Property | Type | Description |
|----------|------|-------------|
| `type` | string | Where the value is read from: `data`, `claim`, `context`, `user`, `input`, `config`, `path`, `previous_step` |
| `field` | string | Key/claim name to check |
| `operator` | string | `equals`, `notEquals`, `gt`, `gte`, `lt`, `lte`, `greaterThan`, `lessThan`, `contains`, `notContains`, `startsWith`, `endsWith`, `matches`, `regex`, `exists`, `notExists`, `empty`, `notEmpty`, `in`, `notIn`, `true`, `false` |
| `value` | string | Value to compare |
| `logicalOperator` | string | How the next condition joins this one: `and` (default) or `or` |
| `negate` | boolean | Negate the result |

Conditions are evaluated left to right.

#### Flow Control

| Property | Type | Description |
|----------|------|-------------|
| `onTrue` | string | Step ID when conditions are true (blank = the next step) |
| `onFalse` | string | Step ID when conditions are false (blank = skip to the next step) |

**Example**:
```json
{
  "type": "condition",
  "configuration": {
    "conditions": [
      {
        "type": "claim",
        "field": "email_verified",
        "operator": "equals",
        "value": "true"
      },
      {
        "type": "claim",
        "field": "role",
        "operator": "contains",
        "value": "admin"
      }
    ],
    "onTrue": "admin-consent",
    "onFalse": "verify-email"
  }
}
```

### Branch (`branch`)

Multi-way branching. Each entry in `branches` has `targetStepId`, optional `name`, `description`, `priority`, `logicOperator` (`and`/`or`) and `conditions` (`source`, `field`, `operator`, `value`, `values`, `negate`). `evaluationMode` is `first-match` (default) or `highest-priority`; `defaultBranch` is the step used when nothing matches.

---

### Transform (`transform`)

Derive journey values. Each entry in `mappings` reads a source, applies a transform and writes the target.

#### Mappings Array

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `source` | string | - | Key to read (the text itself for `literal`) |
| `sourceType` | string | `"journeyData"` | `journeyData`, `claim`, `context`, `user`, `input`, `literal` |
| `target` | string | **required** | Journey key to write |
| `transform` | string | `"copy"` | See below |
| `transformConfig` | object | - | The transform's options |
| `defaultValue` | string | - | Used when no value is produced |
| `required` | boolean | `false` | Fail the journey when no value is produced |

Transforms and their `transformConfig` options: `copy`, `lowercase`, `uppercase`, `trim`, `split` (`delimiter`, `index`), `join` (`separator`), `replace` (`find`, `replaceWith`), `regex_replace` (`pattern`, `replaceWith`), `regex_match` (`pattern`), `substring` (`start`, `length`), `prefix` (`value`), `suffix` (`value`), `template` (`template`, with placeholders such as `{data:given_name}`), `map` (`pairs`, one `from=to` per line, and `otherwise`), `hash` (`algorithm`: `sha256`, `sha384`, `sha512`), `base64encode`, `base64decode`, `urlencode`, `urldecode`.

**Example**:
```json
{
  "type": "transform",
  "configuration": {
    "mappings": [
      {
        "source": "given_name",
        "sourceType": "claim",
        "target": "first_name",
        "transform": "copy"
      },
      {
        "source": "email",
        "target": "email_domain",
        "transform": "regex_match",
        "transformConfig": { "pattern": "@(.+)$" }
      },
      {
        "target": "display_name",
        "transform": "template",
        "transformConfig": { "template": "User: {data:name}" }
      }
    ]
  }
}
```

---

### Sign Up / Create User (`signup`, `create_user`)

Self-registration: collects email and password and creates a local user. `create_user` is an alias with the same settings.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `allowSelfRegistration` | boolean | `true` | When false, the step skips (or takes `registration_disabled`) |
| `requireEmailVerification` | boolean | `true` | Require the user to verify their email before continuing |
| `verificationStepId` | string | - | Step to go to after the user is created to verify the email (a `passwordless_email` step in OTP mode) |
| `requirePassword` | boolean | `true` | Collect a password (false for passwordless or external-only) |
| `requireTermsAcceptance` | boolean | `false` | Require terms acceptance before account creation |
| `termsUrl` / `privacyUrl` | string | - | Links shown alongside the form |
| `allowedEmailDomains` | string[] | - | Restrict sign-up to these email domains |
| `emailClaim` | string | `"email"` | Claim/input carrying the email |
| `usernameMode` | string | `"email"` | `email` uses the email as username; `ask` adds a username field |
| `usernameClaim` | string | - | Claim carrying the username from an earlier step |
| `passwordClaim` | string | `"password"` | Claim/input carrying the password |
| `defaultRoles` | string[] | - | Roles to assign to new user |
| `claimMappings` | object | - | Map collected claims onto user properties |

Branches (`signup`): `login` ("Already have an account"), `registration_disabled`.

**Example**:
```json
{
  "type": "signup",
  "configuration": {
    "requireEmailVerification": true,
    "verificationStepId": "verify-email",
    "defaultRoles": ["user"],
    "claimMappings": {
      "given_name": "FirstName",
      "family_name": "LastName"
    }
  }
}
```

### Assign Roles (`assign_roles`)

Gives the journey's user the roles in `roles` (role IDs). Place it after the step that creates or signs in the account.

### Update User (`update_user`)

Profile form bound to user properties: `title`, `description`, `submitButtonText`, `fields` or the shorthand `editableFields`, `requireCurrentPassword` (default `false`) and `requireEmailVerification` (default `true`: a changed email is applied only once a code sent to the new address is entered).

### Link Account (`link_account`)

`allowedProviders` (empty allows all configured), `allowUnlink` (default `true`), `requireConfirmation` (default `true`).

### Password Change (`password_change`)

`requireCurrentPassword` (default `true`), `minPasswordLength` (`8`), `requireUppercase`, `requireLowercase`, `requireDigit` (all `true`), `requireSpecialChar` (`false`).

---

### Terms Acceptance (`terms_acceptance`)

Require user to accept terms of service and/or privacy policy.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `termsTitle` / `privacyTitle` | string | `"Terms of Service"` / `"Privacy Policy"` | Section headings |
| `termsUrl` / `privacyUrl` | string | - | Links used when no inline content is given |
| `termsContent` / `privacyContent` | string | - | Inline HTML/Markdown; overrides the URL |
| `showTermsInline` / `showPrivacyInline` | boolean | `false` | Render inline instead of linking out |
| `termsVersion` / `privacyVersion` | string | `"1.0"` | Version recorded with the acceptance |
| `requireBoth` | boolean | `true` | Require both (false = either is enough) |
| `requireCheckbox` | boolean | `true` | Render an explicit acceptance checkbox |
| `canDecline` | boolean | `false` | Show a "Decline" button that ends the journey |
| `acceptButtonText` / `declineButtonText` | string | `"I Accept"` / `"Decline"` | Button labels |
| `version` | string | - | Legacy combined version; prefer the separate versions |

**Example**:
```json
{
  "type": "terms_acceptance",
  "configuration": {
    "termsUrl": "https://example.com/terms",
    "privacyUrl": "https://example.com/privacy",
    "requireCheckbox": true,
    "termsVersion": "2.0",
    "privacyVersion": "2.0"
  }
}
```

---

### Password Reset (`password_reset`)

Reset a forgotten password and enforce the new-password policy.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `tokenExpirationMinutes` | number | `60` | Minutes before the reset token expires |
| `minLength` | number | `8` | Minimum password length |
| `requireUppercase` / `requireLowercase` / `requireDigit` | boolean | `true` | Character requirements |
| `requireNonAlphanumeric` | boolean | `false` | Require a special character |
| `requireCurrentPassword` | boolean | `false` | Require current password |

**Example**:
```json
{
  "type": "password_reset",
  "configuration": {
    "tokenExpirationMinutes": 30,
    "minLength": 10
  }
}
```

---

### Custom Plugin (`custom_plugin`)

Execute a WASM or managed (.NET) plugin. See [WASM Plugins](#wasm-plugins).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `pluginName` | string | **required** | Plugin to execute (chosen from the uploaded or registered plugins) |
| `entryPoint` | string | `"execute"` | Plugin function to call |
| `config` | object | - | Settings passed to managed plugins |

**Example**:
```json
{
  "type": "custom_plugin",
  "configuration": {
    "pluginName": "risk-assessment",
    "entryPoint": "evaluate_risk"
  }
}
```

---

### Custom Page (`custom_page`)

Render a host-authored Razor partial as a journey screen.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `templatePath` | string | **required** | Template name; maps to `Views/Shared/Journey/Templates/_{name}.cshtml` shipped by the host app or a referenced Razor Class Library |
| `model` | object | - | Static data exposed to the template as `CustomPageViewModel.Config` |

**Example**:
```json
{
  "type": "custom_page",
  "configuration": {
    "templatePath": "Welcome",
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

Enable passwordless authentication using FIDO2/WebAuthn (passkeys). This feature, in `Oluso.Enterprise.Fido2`, uses the [Fido2NetLib](https://github.com/passwordless-lib/fido2-net-lib) library for full WebAuthn specification compliance.

### Prerequisites

1. **HTTPS is required** - WebAuthn doesn't work over HTTP (except localhost)
2. **Configure the Relying Party** - Set your domain as the relying party ID
3. **License** - Professional tier or purchase the `fido2` add-on

### Installation

Add passkey support to the Oluso builder, register the credential store, and enable sessions (registration and assertion state is kept in the session):

```csharp
// Program.cs
builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddFido2();                       // reads the Oluso:Fido2 section

// Credential storage: "Sqlite", "SqlServer" or "PostgreSQL"
builder.Services.AddFido2ForProvider(provider, connectionString);

var app = builder.Build();
// ...
app.UseSession();
```

`AddFido2` also accepts an `Action<Fido2Options>` or a fluent builder:

```csharp
.AddFido2(fido2 => fido2
    .WithRelyingParty("auth.example.com", "My Identity Server")
    .WithOrigins("https://auth.example.com")
    .RequireUserVerification()
    .PreferPlatformAuthenticator());
```

### Configuration

In `appsettings.json`:

```json
{
  "Oluso": {
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
| `MetadataService` | object | - | FIDO Metadata Service: `Enabled`, `CachePath`, `RefreshIntervalHours` (default `24`) |

A tenant can switch passkeys off with its `EnablePasskey` setting; the passkey steps then skip.

### Adding Passkeys to a Journey

The module adds three step types:

| Step Type | Purpose |
|-----------|---------|
| `webauthn` | Sign in with an existing passkey |
| `fido2_login` | Passkey sign-in page, optionally offered next to other methods |
| `fido2_register` | Offer a signed-in user to create a passkey |

```json
{
  "id": "webauthn-login",
  "type": "webauthn",
  "displayName": "Sign in with Passkey",
  "configuration": {
    "passkeyOnly": false,
    "allowFallback": true
  }
}
```

#### `webauthn` Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `passkeyOnly` | boolean | `false` | Sign in with a passkey only, without asking for a username first |
| `allowFallback` | boolean | `true` | Let the user continue another way when they have no passkey or it fails |

#### `fido2_login` Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `allowUsernameless` | boolean | `true` | Sign in with a passkey without typing a username |
| `showAsOption` | boolean | `true` | Offer passkey sign-in next to other sign-in methods |
| `title` / `description` | string | - | Page heading and text (localizable) |

#### `fido2_register` Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `promptNewUsers` | boolean | `true` | Offer to users who just signed up |
| `promptExistingUsers` | boolean | `false` | Offer to existing users |
| `skipIfHasPasskey` | boolean | `true` | Skip for users who already have a passkey |
| `maxPasskeys` | number | `10` | Skip once a user has this many |
| `allowSkip` | boolean | `true` | Let users skip creating a passkey |
| `authenticatorType` | string | `""` | `platform`, `cross-platform`, or blank for both |
| `requireResidentKey` | boolean | `false` | Require a discoverable credential |
| `showSuccessMessage` | boolean | `true` | Confirm the new passkey before continuing |
| `title` / `description` | string | - | Page heading and text (localizable) |

The `local_login` and `composite_login` steps also offer passkey sign-in (`showPasskey`, `passkeyUsernameless`).

### User Registration Flow

1. User signs in with password (or other method)
2. A `fido2_register` step offers to create a passkey, or the user goes to the account portal's security page
3. Browser prompts for biometric/PIN verification
4. Credential is stored and linked to user

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
  "type": "webauthn",
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

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/fido2/register/options` | POST | Get registration options |
| `/api/fido2/register/complete` | POST | Complete registration |
| `/api/fido2/assert/options` | POST | Get assertion options |
| `/api/fido2/assert/complete` | POST | Verify assertion |
| `/api/fido2/credentials` | GET | List user's credentials |
| `/api/fido2/credentials/{id}` | DELETE | Remove a credential |
| `/api/account/passkeys` | GET | List the signed-in user's passkeys (account portal) |
| `/api/account/passkeys/register/start` | POST | Start passkey registration (account portal) |
| `/api/account/passkeys/register/complete` | POST | Complete passkey registration (account portal) |
| `/api/account/passkeys/{passkeyId}` | PATCH | Rename a passkey |
| `/api/account/passkeys/{passkeyId}` | DELETE | Remove a passkey |
| `/api/admin/fido2/stats` | GET | Passkey statistics (admin) |
| `/api/admin/fido2/users` | GET | Users with passkeys (admin) |
| `/api/admin/fido2/users/{userId}/credentials` | GET / DELETE | List or remove a user's passkeys (admin) |
| `/api/admin/fido2/users/{userId}/credentials/{credentialId}` | DELETE | Remove one passkey (admin) |

Admin permissions: `fido2.read`, `fido2.write`, `fido2.delete`.

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
- Check the tenant's `EnablePasskey` setting
- Verify browser supports WebAuthn

---

## Business Verification (KYB)

The `Oluso.Enterprise.Verification` module checks that a business is real and that the
person signing up may act for it: a `business_verification` journey step, Dojah and
Youverify providers, provider-hosted sessions with webhooks, document uploads, an admin
review queue and the `business.verification_completed` event.

### Installation

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddOlusoVerification();

// The module's tables (businesses, applicants, checks) in its own context and
// migrations history table (__EFMigrationsHistory_Verification). Pending migrations
// apply at startup with the other contexts.
builder.Services.AddOlusoVerificationForProvider(provider, connectionString); // Sqlite, SqlServer or PostgreSQL
```

The step only appears in the journey builder when the module is added. The host also needs
file storage (`AddLocalFileStorage(...)` or blob storage) for documents, and an email sender
(`AddMessaging(...)`) for the "email me the outcome" option; without one the option isn't
offered. The admin pages ship as the `@syndew/oluso-plugin-verification` admin plugin
(`pluginService.registerAdminPlugin(VERIFICATION_PLUGIN)`).

Permissions: `business_verification.read` (businesses, applicants, documents),
`business_verification.review` (decide businesses and applicants),
`business_verification.manage` (providers and routing).

### Provider setup

Configure under **Settings → Business verification** (credentials are stored encrypted in
the organisation's `IdentityVerification` configuration section, never returned).

| Provider | Credentials | Sandbox |
|---|---|---|
| Dojah | App ID, secret key (Dojah dashboard → Developers → Configuration). Hosted mode also needs the public key, the EasyOnboard widget ID of a KYB flow, and the webhook secret (Developers → Webhooks) | `https://sandbox.dojah.io` with sandbox keys: test data, not billed |
| Youverify | API secret key (workspace → Settings → API Keys) | `https://api.sandbox.youverify.co` with the sandbox key |

Choose **Sandbox** while testing; switching to **Live** clears the stored secrets so live
keys must be entered. **Test saved credentials** uses a free endpoint (Dojah wallet balance,
Youverify supported countries). Route each country to one provider; a country without a
route can't be verified (no fallback). **Reuse registry results** reuses a business's
registry snapshot for that many days instead of buying a new lookup.

### Webhooks

Hosted (asynchronous) results arrive by webhook at

```
https://{organisation host}/api/verification/webhooks/{provider}
```

on the organisation's own subdomain or custom domain (the host identifies the organisation;
there is no tenant id in the URL). The settings page shows the exact URL per provider. For
Dojah, subscribe the `kyc_widget` service to it (dashboard → Developers → Webhooks, or
`POST /api/v1/webhook/subscribe`). Requests must carry `x-dojah-signature`, the hex
HMAC-SHA256 of the raw body keyed with the webhook secret; anything else gets 401. The
webhook only identifies the session: the outcome is read from Dojah's
`GET /api/v1/kyc/verification?reference_id=` and never taken from the browser.

Youverify documents no hosted KYB flow and no webhook signature scheme yet, so it serves the
synchronous lookup only.

### The journey step

| Setting | Default | Meaning |
|---|---|---|
| `allowedCountries` | required | Countries offered; each needs a routed provider |
| `mode` | `lookup` | `lookup`: instant registry check. `hosted`: the provider's flow opens in the page (Dojah EasyOnboard widget); the applicant waits on a status page |
| `requireNameMatch` | `true` | The typed name must match the registered name (legal-form words ignored) |
| `requireActive` | `true` | A business the registry lists as not trading is rejected |
| `applicantEvidence` | `none` | `none`, `director_match`, `documents`, `director_or_documents` |
| `directorRoles` | director, proprietor, partner, trustee | Registry roles that count as a director |
| `documentTypes` / `maxDocumentSizeMb` / `maxDocuments` | pdf, jpeg, png / 10 / 3 | Upload limits for supporting documents |
| `allowRetry` | `true` | After a rejection, let the applicant correct details and try again |
| `pendingWaitSeconds` | 180 | How long the waiting page polls before offering to email the outcome |
| `*Claim` settings | - | Read inputs from journey data an earlier step wrote instead of showing the form |

Branches: `verified`, `not_verified`, `pending` (waiting on the provider or a reviewer;
use it to finish sign-up now and provision later), `provider_unavailable`. Outputs:
`kyb_status`, `kyb_business_id`, `kyb_applicant_id`, `kyb_business_name`,
`kyb_registration_number`, `kyb_country`, `kyb_relationship`.

**Resuming.** A business is stored once per registration number; each person acting for it
is a separate link with its own status. On entry the step looks up the user's link (or,
before sign-up, the link this journey made): verified links continue straight away,
pending/in-review links show the waiting page (or take `pending`), rejected links show the
reason and a retry. A link made before the account exists is tied to the user when the same
journey signs them up or in, and `business.applicant_linked` is raised.

**Waiting.** The waiting page polls `/journey/{id}/status`. When a webhook or a reviewer
decides the check, the journey's status revision changes and the page reloads into the
outcome. After `pendingWaitSeconds` it offers to email the outcome instead.

**Events.** `business.verification_completed` is raised once per final outcome (verified,
rejected, failed) with the deciding origin: the journey, the provider webhook
(`integration`, `verification:dojah`) or the reviewer (`admin`). A webhook subscription or event sink on it can
provision the business's account when a review completes later.

### Documents and cameras

Supporting documents (certificate of incorporation, letter of authority) are checked by
content and size, stored privately as attachments of the applicant link, and opened by
reviewers through the admin API or by people linked to the same business through
`/api/account/businesses/{id}/documents/{documentId}`. The upload offers "Take a photo",
which opens the phone's camera. Journeys embedded in another site need the host iframe to
allow the camera: `<iframe src="…" allow="camera; microphone">`. The SDK's modal and
`JourneyEmbed` set it already. Liveness and selfie capture are left to the provider's SDK.

---

## Multi-Factor Authentication (MFA)

### Supported MFA Methods

| Method | Description |
|--------|-------------|
| `totp` | Time-based One-Time Password (authenticator apps) |
| `sms` | SMS verification code (`phone` is accepted as an alias) |
| `email` | Email verification code |

Recovery codes are accepted in place of a code. Passkeys are a separate first factor (see [FIDO2/WebAuthn](#fido2webauthn-passkeys)).

### Configuring MFA Step

```json
{
  "id": "mfa-step",
  "type": "mfa",
  "displayName": "Verify Your Identity",
  "configuration": {
    "required": false,
    "methods": ["totp", "sms", "email"],
    "rememberDevice": true,
    "rememberDeviceDays": 30
  }
}
```

### Requiring MFA

MFA is set per journey, not per client: set `required: true` on the policy's `mfa` step to make every user of that policy complete MFA. A client that needs MFA names that policy with `?policy=` (see [OIDC Extensions](#oidc-extensions)).

### MFA Setup

With `allowSetup` (default `true`), a user without MFA who reaches a step with `required: true` sets up one of the allowed methods there and then continues (branch `mfa_setup_complete`). With `allowSetup: false`, such a user cannot continue (`mfa_setup_required`) until an administrator enables MFA for them.

### SMS and Email Messaging

The MFA system and the passwordless steps send codes through the `ISmsSender` and `IEmailSender` interfaces (namespace `Oluso.Core.Services`).

#### Built-in Providers

`AddMessaging(configuration)` registers the email and SMS senders from configuration:

```csharp
builder.Services.AddMessaging(builder.Configuration);
```

```json
{
  "Oluso": {
    "Messaging": {
      "Email": {
        "Provider": "SendGrid",
        "FromAddress": "noreply@example.com",
        "FromName": "My App",
        "SendGrid": { "ApiKey": "SG.xxxxx" }
      },
      "Sms": {
        "Provider": "Twilio",
        "Twilio": {
          "AccountSid": "ACxxxx",
          "AuthToken": "xxxx",
          "FromNumber": "+15550100"
        }
      }
    }
  }
}
```

| Channel | Providers | Settings |
|---------|-----------|----------|
| Email | `SendGrid` (default), `Smtp`, `Cloudflare` (Brevo is also available in tenant sender profiles) | `FromAddress`, `FromName`, `ReplyToAddress`, plus `SendGrid:ApiKey`, `Smtp:Host/Port/Username/Password/UseSsl` or `Cloudflare:AccountId/ApiToken` |
| SMS | `Infobip` (default), `Twilio` | `Infobip:ApiKey/BaseUrl/SenderId` or `Twilio:AccountSid/AuthToken/FromNumber` |

The email sender is tenant-aware: a tenant's default sender profile (configured under **Settings** > **Email** in the Admin UI) is used when it has one, and the `Oluso:Messaging:Email` settings otherwise. `AddEmailSender(configuration)` and `AddSmsSender(configuration)` register one channel only.

#### Custom SMS Provider

Implement `ISmsSender` to integrate another SMS provider:

```csharp
public class MySmsSender : ISmsSender
{
    private readonly HttpClient _http;

    public MySmsSender(HttpClient http) => _http = http;

    public async Task<SmsResult> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            "https://sms.example.com/v1/messages",
            new { to = phoneNumber, text = message },
            cancellationToken);

        return response.IsSuccessStatusCode
            ? SmsResult.Succeeded(response.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null)
            : SmsResult.Failed($"SMS provider returned {(int)response.StatusCode}");
    }
}
```

Register in `Program.cs` (the sender gets a typed `HttpClient`):

```csharp
builder.Services.AddSmsSender<MySmsSender>();
```

#### Custom Email Provider

Implement `IEmailSender`. Besides the ambient-tenant send and the template send, it has a tenant-explicit overload used by background work; the attachment and multi-recipient overloads have default implementations.

```csharp
public class MyEmailSender : IEmailSender
{
    public Task<EmailResult> SendAsync(string email, string subject, string htmlMessage,
        CancellationToken cancellationToken = default)
        => SendCoreAsync(email, subject, htmlMessage, cancellationToken);

    public Task<EmailResult> SendAsync(string tenantId, string email, string subject, string htmlMessage,
        CancellationToken cancellationToken = default)
        => SendCoreAsync(email, subject, htmlMessage, cancellationToken);

    public Task<EmailResult> SendTemplateAsync(string email, string templateName,
        IDictionary<string, object> templateData, CancellationToken cancellationToken = default)
        => Task.FromResult(EmailResult.Failed("Templates are not supported by this sender"));

    private async Task<EmailResult> SendCoreAsync(string email, string subject, string html, CancellationToken ct)
    {
        // Call your provider here and return EmailResult.Succeeded(messageId) or EmailResult.Failed(error)
        await Task.CompletedTask;
        return EmailResult.Succeeded();
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddEmailSender<MyEmailSender>();
```

With `AddMessaging` also in place, register the custom sender after it: each registration adds a typed `HttpClient` client for the interface, and the last one wins.

#### Interfaces Reference

**ISmsSender**

| Method | Description |
|--------|-------------|
| `SendAsync(phoneNumber, message, ct)` | Sends an SMS to the specified phone number |

**IEmailSender**

| Method | Description |
|--------|-------------|
| `SendAsync(email, subject, htmlMessage, ct)` | Sends an email in the current tenant's context |
| `SendAsync(tenantId, email, subject, htmlMessage, ct)` | Sends an email for an explicit tenant |
| `SendAsync(tenantId, email, subject, htmlMessage, attachments, ct)` | With attachments (default implementation refuses attachments) |
| `SendAsync(tenantId, EmailMessage, ct)` | Several recipients, CC, BCC, reply-to, headers (default implementation covers one recipient) |
| `SendTemplateAsync(email, templateName, data, ct)` | Sends using a provider template |

**SmsResult / EmailResult**

| Property | Type | Description |
|----------|------|-------------|
| `Success` | bool | Whether the send succeeded |
| `Error` | string? | Error message if failed |
| `MessageId` | string? | Provider message ID if available |
| `Skipped` | bool | Email only: nothing was sent because no recipient can receive mail (reserved test domains such as `example.com` or `.local`); counts as success |

---

## Self-Service Registration

### Enabling Registration

1. Set `AllowSelfRegistration = true` on the tenant
2. Create a `SignUp` or `SignInSignUp` journey (the platform seeds default `signup` and `signup-signin` policies), or let the `local_login` step link to registration with `allowSelfRegistration`

Clients can open the sign-up journey with `prompt=create` (see [OIDC Extensions](#oidc-extensions)).

### Registration Journey Example

```json
{
  "name": "User Registration",
  "slug": "register",
  "type": "SignUp",
  "steps": [
    {
      "id": "terms",
      "type": "terms_acceptance",
      "order": 1,
      "configuration": {
        "termsUrl": "https://example.com/terms",
        "privacyUrl": "https://example.com/privacy",
        "requireCheckbox": true
      }
    },
    {
      "id": "create-user",
      "type": "signup",
      "order": 2,
      "configuration": {
        "requireEmailVerification": true,
        "verificationStepId": "verify-email",
        "defaultRoles": ["user"]
      }
    },
    {
      "id": "verify-email",
      "type": "passwordless_email",
      "order": 3,
      "configuration": {
        "mode": "otp"
      }
    },
    {
      "id": "profile",
      "type": "dynamic_form",
      "order": 4,
      "configuration": {
        "title": "Tell us about yourself",
        "fields": [
          { "name": "given_name", "type": "text", "label": "First Name", "required": true, "persistToUser": true, "userPropertyName": "firstName" },
          { "name": "family_name", "type": "text", "label": "Last Name", "required": true, "persistToUser": true, "userPropertyName": "lastName" }
        ]
      }
    }
  ]
}
```

The `signup` step collects the email and password itself; collect further profile data in a `dynamic_form` step.

### Email Domain Restrictions

Restrict registration to specific email domains with the tenant setting:

```
AllowedEmailDomains = "company.com, partner.com"
```

or per journey with the `signup` step's `allowedEmailDomains` list.

---

## External Identity Providers

### Supported Providers

- Google
- Microsoft (work, school and personal accounts)
- Facebook
- Apple
- GitHub
- LinkedIn
- Twitter
- Generic OIDC
- Generic OAuth 2.0
- SAML 2.0
- LDAP

### Adding an External Provider

1. Navigate to **Federation** > **Identity Providers** in Admin UI
2. Add a provider and select its type
3. Configure credentials and settings
4. Register the callback URL `https://{your host}/signin-{scheme}` with the provider

### Provider Configuration Example (Google)

`POST /api/admin/identity-providers`:

```json
{
  "scheme": "google",
  "displayName": "Google",
  "enabled": true,
  "providerType": "Google",
  "configuration": {
    "clientId": "your-client-id.apps.googleusercontent.com",
    "clientSecret": "your-client-secret",
    "hostedDomain": "example.com"
  }
}
```

`clientId` and `clientSecret` are required for Google; `hostedDomain` and `accessType` are optional. `GET /api/admin/identity-providers/types` lists the required and optional fields of each type. `allowedClientIds` limits which clients may use the provider.

### Using External IdP in Journey

```json
{
  "id": "social-login",
  "type": "external_login",
  "displayName": "Sign in with Google",
  "configuration": {
    "providers": ["google"],
    "autoProvision": true,
    "autoRedirect": true
  }
}
```

---

## SAML, LDAP and SCIM

These enterprise modules live in `Oluso.Enterprise` and are registered by the host. SAML and LDAP check for their licensed features (`saml`, `ldap`).

### SAML 2.0

```csharp
builder.Services
    .AddSaml(builder.Configuration)                     // Oluso:Saml:ServiceProvider and Oluso:Saml:IdentityProvider
    .AddSamlForProvider(provider, connectionString);    // service-provider registrations
```

- **As a service provider**: add a SAML 2.0 identity provider under **Identity Providers** and sign in with the `saml` journey step (`idpName`, `autoRedirect`, `forceAuthn`, `isPassive`, `autoProvision`, `proxyMode`).
- **As an identity provider**: register the applications that trust Oluso through `/api/admin/saml/service-providers`. The IdP endpoints are `/saml/idp/metadata`, `/saml/idp/sso` and `/saml/idp/slo`.

### LDAP / Active Directory

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddLdap();                                         // Oluso:Ldap
```

Add an LDAP identity provider and use the `ldap` journey step (see its settings in the journey builder: `ldapProvider`, server and bind overrides, `autoProvision`, `allowPasswordChange`, `syncGroups`).

### SCIM 2.0 Provisioning

```csharp
builder.Services
    .AddScim(options => options.BasePath = "/scim/v2")
    .AddScimForProvider(provider, connectionString);

app.UseScim();
```

Inbound provisioning is served at `/scim/v2/Users`, `/scim/v2/Groups` and `/scim/v2/Bulk`, with discovery at `/scim/v2`. Each provisioning system authenticates with a bearer token issued to a SCIM client (`/api/admin/scim/clients`, which can also rotate the token). Outbound provisioning to other systems is configured through `/api/admin/scim/providers`.

---

## WASM Plugins

Extend the Identity Server with WebAssembly plugins using Extism. A plugin runs in a `custom_plugin` journey step; .NET code can instead be registered as a managed plugin with `AddPlugin<TPlugin>()` on the Oluso builder (`TPlugin : IManagedPlugin`), which the step tries first.

### Plugin Capabilities

Plugins can:
- Execute custom logic in journey steps
- Derive values and write them into the journey
- Validate user input
- Ask the user for more input with a form
- Choose the branch the journey takes
- Call external APIs, only on hosts the plugin's network policy allows

### Plugin Storage

The host chooses where uploaded plugins are kept, for example on disk:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddFileSystemPluginStore(Path.Combine(builder.Environment.ContentRootPath, "plugins"));
```

### Creating a Plugin

A complete example is in `samples/plugins/hello-plugin`.

#### 1. Set Up Project

```bash
cargo new my-plugin --lib
rustup target add wasm32-unknown-unknown
```

#### 2. Add Dependencies

```toml
# Cargo.toml
[dependencies]
extism-pdk = "1.1.0"
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"

[lib]
crate-type = ["cdylib"]
```

#### 3. Implement Plugin

```rust
use extism_pdk::*;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct PluginInput {
    function: String,
    user_id: Option<String>,
    tenant_id: Option<String>,
    input: HashMap<String, serde_json::Value>,
    journey_data: HashMap<String, serde_json::Value>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PluginOutput {
    success: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
    action: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    data: Option<HashMap<String, serde_json::Value>>,
}

#[plugin_fn]
pub fn execute(input_json: String) -> FnResult<String> {
    let input: PluginInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse input: {}", e)))?;

    let mut data = HashMap::new();
    data.insert("custom_claim".to_string(), serde_json::json!("custom_value"));

    let output = PluginOutput {
        success: true,
        error: None,
        action: "continue".to_string(),
        data: Some(data),
    };

    Ok(serde_json::to_string(&output)?)
}
```

#### 4. Build Plugin

```bash
cargo build --release --target wasm32-unknown-unknown
```

#### 5. Deploy Plugin

1. Navigate to **Experience** > **Plugins** in Admin UI
2. Upload your `.wasm` file
3. Configure plugin metadata and, if it calls out, its network access

Or upload with `POST /api/admin/plugins` (multipart form with the `.wasm` file).

### Using Plugin in Journey

```json
{
  "id": "custom-validation",
  "type": "custom_plugin",
  "displayName": "Custom Validation",
  "configuration": {
    "pluginName": "my-validation-plugin",
    "entryPoint": "execute"
  }
}
```

### Plugin Input/Output Schema

The plugin function receives one JSON string and returns one. Credential-bearing values (passwords, codes) are removed before the plugin sees them.

#### Input
```typescript
interface PluginInput {
  function: string;                     // the entry point being called
  userId?: string;
  tenantId?: string;
  input: Record<string, unknown>;       // the step's form submission
  journeyData: Record<string, unknown>; // values collected so far
}
```

#### Output
```typescript
interface PluginOutput {
  success: boolean;
  error?: string;
  action?: 'continue' | 'complete' | 'require_input' | 'branch' | 'fail';
  data?: Record<string, unknown>;       // values to write into the journey
}
```

| Action | Effect |
|--------|--------|
| `continue` / `complete` | Step succeeds; `data` is written into the journey |
| `require_input` | Shows a form described by `data`, in the same shape as a `dynamic_form` step's settings (`title`, `description`, `fields`, ...) |
| `branch` | Takes the branch named by `data.branchId` |
| `fail` | Step fails with `error` |

A missing or unknown `action` counts as `continue`; `success: false` fails the step.

### Dynamic Form Example

```rust
#[plugin_fn]
pub fn collect_data(_input_json: String) -> FnResult<String> {
    let mut form = HashMap::new();
    form.insert("title".to_string(), serde_json::json!("Additional Information"));
    form.insert("fields".to_string(), serde_json::json!([
        { "name": "company", "type": "text", "label": "Company Name", "required": true }
    ]));

    let output = PluginOutput {
        success: true,
        error: None,
        action: "require_input".to_string(),
        data: Some(form),
    };
    Ok(serde_json::to_string(&output)?)
}
```

### Network Access

WASM plugins have no network access by default. A plugin's network policy (`/api/admin/plugins/{pluginName}/network`) can allow `Self` (the server's own issuer host) or `Whitelist` (a list of hosts). Unrestricted access is refused; code that needs it should be a managed plugin.

---

## Client Configuration

### Journey Settings per Client

Each client can override the tenant's flow setting:

| Setting | Description |
|---------|-------------|
| `UseJourneyFlow` | Unset inherits the tenant; `false` forces the standalone pages for this client; `true` allows journeys |

The client chooses a journey per request with `?policy={slug}`; without one, the tenant's highest-priority enabled policy of the requested type runs (see [OIDC Extensions](#oidc-extensions)).

### Admin UI Configuration

1. Navigate to **Applications** > **Clients** > select client
2. In the **UI Flow Settings** card, set **Authentication Flow** to **Inherit from tenant**, **Journey flow** or **Standalone pages**
3. Save

### User and Role Restrictions

Restrict which users can obtain tokens for a client using Allowed Users, Allowed Roles and Required Permission.

#### How Restrictions Work

- **No restrictions**: If none are configured, all authenticated users can access the client
- **Allowed Users**: subject IDs allowed to use the client
- **Allowed Roles**: role names; the user must hold at least one
- **Required Permission**: a permission the user must have through their roles (exact match, no wildcards)

When several are configured, the authorization code exchange requires each configured restriction to pass, while the refresh token and password grants accept a user who passes any one of them.

#### Configuring via Admin UI

1. Navigate to **Applications** > **Clients** > select client
2. Add users in the **Allowed Users** card (by subject ID and display name)
3. Add roles in the **Allowed Roles** card
4. Optionally set **Required Permission**
5. Save changes

#### Configuring via API

```http
PUT /api/admin/clients/{id}
Content-Type: application/json

{
  "allowedUsers": [
    { "subjectId": "user-123", "displayName": "John Doe" },
    { "subjectId": "user-456", "displayName": "Jane Smith" }
  ],
  "allowedRoles": ["admin", "premium-user"]
}
```

`{id}` is the client's numeric ID.

#### Use Cases

| Scenario | Configuration |
|----------|--------------|
| Admin-only application | Add `admin` role to Allowed Roles |
| Specific users only | Add user IDs to Allowed Users |
| Department access | Add a department role (e.g., `engineering`) to Allowed Roles |
| Beta testers | Add a `beta-tester` role or specific user IDs |
| Custom roles | Set Required Permission and grant it through any role |

#### Error Handling

When a user is denied access:
- The token endpoint returns an `access_denied` error with the reason
- A warning is logged with the user ID and client ID

#### Enforcement Points

Restrictions are enforced at the token endpoint for:
1. **Authorization code** exchange
2. **Refresh token** grant
3. **Password grant** (Resource Owner Password Credentials)

#### Example: Internal Admin Tool

```json
{
  "clientId": "admin-dashboard",
  "clientName": "Admin Dashboard",
  "allowedRoles": ["admin", "super-admin"],
  "allowedUsers": []
}
```

Only users with `admin` or `super-admin` roles can access this client.

---

## Signing Keys

Manage cryptographic keys used for signing tokens and other security operations. Enable managed keys with `AddSigningKeys()` on the Oluso builder; without it a development signing credential is used.

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

Keys that encrypt stored data (data-protection keys) cannot be rotated, revoked, changed or deleted; the API refuses those requests.

### Managing Keys via Admin UI

1. Navigate to **Signing Keys** in the Admin UI
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

Automatic rotation is configured per tenant (and optionally per client) through `GET` / `PUT /api/admin/signing-keys/rotation-config`:

| Setting | Description | Default |
|---------|-------------|---------|
| `enabled` | Enable automatic rotation | `true` |
| `keyType` | Type for new keys | `RSA` |
| `algorithm` | Algorithm for new keys | `RS256` |
| `keySize` | Size for new keys | `2048` |
| `keyLifetimeDays` | How long keys remain valid | `90` |
| `rotationLeadDays` | Days before expiration to rotate | `14` |
| `gracePeriodDays` | Days expired keys can still verify | `30` |
| `maxKeys` | Maximum keys to retain | `5` |

Host-wide defaults are set with `AddSigningKeys(options => ...)` (`SigningKeyOptions`: `DefaultStorageProvider`, `DefaultKeyType`, `DefaultRsaAlgorithm`, `DefaultEcAlgorithm`, `DefaultRsaKeySize`, `DefaultKeyLifetimeDays`, `RotationLeadDays`, `GracePeriodDays`, `MaxKeysPerContext`, `EnableAutoRotation`, `AutoRotationIntervalHours`).

### Storage Providers

#### Local Storage (Default)

- Keys encrypted at rest in the database with ASP.NET Core Data Protection
- Private key material never exposed via API

#### Azure Key Vault

Add the `Oluso.Enterprise.AzureKeyVault` module (licensed feature `keyvault`):

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddSigningKeys(opts => opts.DefaultStorageProvider = KeyStorageProvider.AzureKeyVault)
    .AddOlusoAzureKeyVault(options =>
    {
        options.VaultUri = "https://myvault.vault.azure.net/";
        options.UseHsmKeys = true;
    });
```

- Private keys never leave Azure Key Vault
- Signing operations happen inside Key Vault
- Uses DefaultAzureCredential (Managed Identity, CLI, environment)

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
  "name": "Production Key 2026",
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
  "status": "Active",
  "priority": 100,
  "includeInJwks": true
}
```

All fields are optional; `expiresAt` can also be set.

#### Revoke Key
```http
POST /api/admin/signing-keys/{id}/revoke
Content-Type: application/json

{
  "reason": "Key compromised"
}
```

#### Delete Key
```http
DELETE /api/admin/signing-keys/{id}
```

#### Get Expiring Keys
```http
GET /api/admin/signing-keys/expiring?daysUntilExpiration=14
```

### JWKS Endpoint

Public keys are exposed at the JWKS endpoint advertised as `jwks_uri` in discovery:
```
/.well-known/jwks
```

Only keys with `includeInJwks = true` and valid status are included.

---

## Events and Webhooks

The Identity Server publishes events for authentication, token, user, administrative and system activities. Every event is an `OlusoEvent`; it is handed to every registered `IOlusoEventSink` (the audit log, webhooks, your own sinks). `AddOluso` registers the event service.

### Event Categories

| Category | Description |
|----------|-------------|
| `Authentication` | Sign-in, sign-out, MFA |
| `User` | Registration, profile, role and password changes |
| `Token` | Token issued, revoked, refreshed, introspected |
| `Client` | Client administration |
| `Security` | Consent, feature and module-admin changes |
| `Role`, `Resource`, `Settings`, `Admin` | Administrative changes |
| `Team`, `Conversation` | Team membership and conversation activity |
| `System` | System-level events |

### Webhook Event Types

Webhook subscriptions use dotted event names. Core ones include:

| Group | Events |
|-------|--------|
| Authentication | `auth.login_success`, `auth.login_failed`, `auth.logout`, `auth.token_issued`, `auth.token_revoked` |
| OIDC | `oidc.authorization_request`, `oidc.authorization_code_issued`, `oidc.authorization_failed`, `oidc.refresh_token_used`, `oidc.token_failed` |
| Users | `user.created`, `user.updated`, `user.deleted`, `user.deactivated`, `user.reactivated`, `user.email_verified`, `user.password_changed`, `user.locked_out`, `user.role_assigned`, `user.role_removed` |
| Security | `security.consent_granted`, `security.consent_revoked`, `security.feature_enabled`, `security.feature_disabled`, `security.module_admin_assigned`, `security.module_admin_removed` |
| Teams | `team.created`, `team.updated`, `team.archived`, `team.restored`, `team.deleted`, `team.member_added`, `team.member_removed`, `team.member_role_changed` |
| Journeys | `journey.submission_created` |
| Admin (platform) | `admin.client_created`, `admin.client_updated`, `admin.client_deleted`, `admin.role_*`, `admin.idp_*`, `admin.journey_policy_*`, `admin.signing_key_changed`, `admin.tenant_*`, `admin.webhook_*`, `admin.api_resource_*`, `admin.api_scope_*`, `admin.resource_*` |
| Modules | `saml.*`, `ldap.*`, `business.*`, `whatsapp.*` when those modules are registered |

`GET /api/admin/webhooks/events` (or `events/grouped`) returns the full list available on your deployment, with descriptions. Admin events are platform-scoped and only platform endpoints can subscribe to them.

### Configuring Webhooks

Register webhook delivery and retries in `Program.cs`:

```csharp
builder.Services.AddOlusoWebhooks(options =>
{
    options.TimeoutSeconds = 30;
    options.MaxRetries = 5;
    options.RequireHttps = true;
});
builder.Services.AddWebhookRetryProcessing();   // in-process retries of failed deliveries
```

Then create endpoints on the **Webhooks** page of the Admin UI (`/webhooks`) or through the API:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/webhooks/endpoints` | GET / POST | List or create endpoints (URL, subscribed events, auth type) |
| `/api/admin/webhooks/endpoints/{endpointId}` | GET / PUT / DELETE | Read, update or delete an endpoint |
| `/api/admin/webhooks/endpoints/{endpointId}/rotate-secret` | POST | Issue a new signing secret |
| `/api/admin/webhooks/endpoints/{endpointId}/test` | POST | Send a test delivery |
| `/api/admin/webhooks/endpoints/{endpointId}/deliveries` | GET | Delivery history |
| `/api/admin/webhooks/deliveries/{deliveryId}/retry` | POST | Retry a delivery |

Endpoint authentication (`authType`): `None` (HMAC signature only), `BearerToken`, `BasicAuth`, `ApiKeyHeader` (`X-API-Key`), or `IssuerSigned`: a short-lived `Authorization: Bearer` JWT (`typ` `webhook+jwt`) signed with the tenant's signing key, verifiable against its `jwks_uri` with no shared secret. Its claims include `aud` (the endpoint URL), `jti` (the delivery ID), `webhook_event` and `body_sha256`.

### Webhook Payload

Webhooks receive HTTP POST requests with a snake_case JSON payload:

```http
POST /webhooks/identity HTTP/1.1
Content-Type: application/json
X-Webhook-ID: 3f6c...
X-Webhook-Event: auth.login_success
X-Webhook-Retry: 1
X-Webhook-Timestamp: 2026-01-15T10:30:00.0000000Z
X-Webhook-Signature: v1=5d41402abc4b2a76b9719d911017c592...

{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "event_type": "auth.login_success",
  "tenant_id": "tenant-1",
  "timestamp": "2026-01-15T10:30:00Z",
  "api_version": "2026-01-01",
  "origin": { ... },
  "data": { ... }
}
```

When the endpoint has a secret, `X-Webhook-Signature` is `v1=` followed by the lowercase hex HMAC-SHA256 of `{X-Webhook-Timestamp}.{raw body}` keyed with the secret. Events whose data is marked sensitive are delivered with those fields redacted (listed in `redacted_fields`) until an administrator authorises sensitive data for the endpoint.

### Custom Event Sinks

Implement `IOlusoEventSink` for custom event handling:

```csharp
public class MyCustomEventSink : IOlusoEventSink
{
    private readonly IMonitoringClient _monitoring;

    public MyCustomEventSink(IMonitoringClient monitoring) => _monitoring = monitoring;

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        switch (evt)
        {
            case UserSignInFailedEvent failed:
                await _monitoring.AlertAsync(failed, cancellationToken);
                break;
            default:
                await _monitoring.TrackAsync(evt.EventType, evt.Category, evt.TenantId, evt.Timestamp, cancellationToken);
                break;
        }
    }
}
```

Register in startup:

```csharp
builder.Services.AddEventSink<MyCustomEventSink>();

// or a callback
builder.Services.AddEventSink(evt => { Console.WriteLine(evt.EventType); return Task.CompletedTask; }, "console");

// or log every event (development)
builder.Services.AddLoggerEventSink();
```

`FilteredEventSink.ForCategories(inner, ...)`, `FilteredEventSink.ExcludeCategories(inner, ...)` and `FilteredEventSink.FailuresOnly(inner)` wrap a sink to receive only some events.

### Batching Events

For high-throughput scenarios, `BatchingEventSink` collects events and hands them to a callback in batches:

```csharp
builder.Services.AddSingleton<IOlusoEventSink>(sp =>
{
    var writer = sp.GetRequiredService<IBulkEventWriter>();
    return new BatchingEventSink(
        (events, ct) => writer.WriteAsync(events, ct),
        batchSize: 100,
        flushInterval: TimeSpan.FromSeconds(5));
});
```

### Event Configuration

Configure the event service with `OlusoEventOptions`:

```csharp
builder.Services.Configure<OlusoEventOptions>(options =>
{
    options.ContinueOnSinkError = true;           // one failing sink doesn't stop the others
    options.IncludeCategories.Add("Authentication"); // empty means all categories
    options.ExcludeCategories.Add("Conversation");
    options.CaptureActivityId = true;             // record the trace ID on each event
});
```

### Using Webhooks in User Journeys

To call an external service from inside a journey, use the `webhook` step (see [Webhook](#webhook-webhook)) or the `api_call` step when the response should feed the journey.

---

## Audit Logging

Audit Logging provides persistent, queryable records of security events for compliance, forensics, and monitoring. With the Entity Framework stores (`AddEntityFrameworkStores...`), every auditable event raised through the event service is written to the `AuditLogs` table by the built-in audit sink.

### What's Captured

Audit logs capture the **who, what, when, where, and how** of security-relevant actions:

| Field | Description |
|-------|-------------|
| **Who** | Subject ID, name, and email of the user the entry is about |
| **Origin** | What caused it: `admin`, `service_client`, `scim`, `journey`, `user`, `anonymous` or `system`, with the actor's ID and name |
| **What** | Event type, category, action, resource type, and resource ID |
| **When** | Timestamp (UTC) of the event |
| **Where** | IP address, user agent, tenant ID, client ID |
| **How** | Details and changed fields |
| **Result** | Success/failure status, error messages |

### Event Types

The event type is the event's class name, for example `UserSignedInEvent`, `UserSignInFailedEvent`, `UserSignedOutEvent`, `MfaCompletedEvent`, `UserRegisteredEvent`, `UserUpdatedEvent`, `UserDeletedEvent`, `UserLockedOutEvent`, `UserRoleAssignedEvent`, `TokenIssuedEvent`, `TokenRevokedEvent`, `ConsentGrantedEvent`, and the admin events `AdminClientCreatedEvent`, `AdminRoleUpdatedEvent`, `AdminIdpDeletedEvent`, `AdminJourneyPolicyUpdatedEvent`, `AdminSigningKeyChangedEvent`, `AdminTenantSettingsUpdatedEvent` and so on. Events that are only mechanisms (cancellable pre-hooks such as `UserSigningInEvent`, scheduler ticks) are not recorded.

### Admin UI

Navigate to **Activity Logs** in the Admin UI to:

- View all audit log entries with filtering
- Search by user, action, resource, or date range
- Filter by success/failure status
- Export logs to JSON or CSV
- View detailed event information

Requires the `audit_logs.read` permission.

### API Endpoints

#### Check Status

```http
GET /api/admin/auditlogs/status
```

Response:
```json
{
  "enabled": true,
  "message": "Audit logging is active"
}
```

#### Query Audit Logs

```http
GET /api/admin/auditlogs?category=Admin&action=Create&from=2026-01-01&pageSize=50
```

Query Parameters:

| Parameter | Type | Description |
|-----------|------|-------------|
| `action` | string | Filter by action (Create, Update, Delete, Login, etc.) |
| `category` | string | Filter by category (Authentication, Admin, Token, etc.) |
| `eventType` | string | Filter by specific event type |
| `subjectId` | string | Filter by the user the entry is about |
| `originKind` | string | Filter by what caused it: `admin`, `service_client`, `scim`, `journey`, `user`, `anonymous`, `system` |
| `originId` | string | Filter by the causing actor's id (admin user id, client id, SCIM client id, journey policy id) |
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
      "timestamp": "2026-01-15T10:30:00Z",
      "eventType": "UserRegisteredEvent",
      "category": "User",
      "action": "CreateUser",
      "subjectId": "new-user-id",
      "subjectEmail": "john@example.com",
      "subjectName": "john",
      "resourceType": "User",
      "resourceId": "new-user-id",
      "originKind": "admin",
      "originId": "admin-user-id",
      "originName": "Admin User",
      "ipAddress": "192.168.1.100",
      "success": true
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
GET /api/admin/auditlogs/12345
```

#### Get Logs for a Resource

```http
GET /api/admin/auditlogs/resource/User/user-123?limit=100
```

#### Get Logs for a User

```http
GET /api/admin/auditlogs/user/user-123?limit=100
```

#### Export Logs

```http
GET /api/admin/auditlogs/export?from=2026-01-01&to=2026-01-31&format=csv
```

Formats: `json`, `csv`

#### Purge Old Logs

```http
DELETE /api/admin/auditlogs/purge?olderThanDays=90
```

Minimum retention: 30 days.

### Recording Custom Events

Raise your own events through `IOlusoEventService`; they reach the audit log, webhooks and every other sink. Administrative actions derive from `AdminActionEvent`:

```csharp
public class AdminWidgetCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Widget";
    public override string? WebhookEventType => "admin.widget_created";   // null: no webhook

    public required string WidgetName { get; init; }
}

public class WidgetsController : ControllerBase
{
    private readonly IOlusoEventService _events;

    public WidgetsController(IOlusoEventService events) => _events = events;

    [HttpPost]
    public async Task<IActionResult> Create(CreateWidgetRequest request, CancellationToken ct)
    {
        var widget = await CreateWidgetAsync(request, ct);

        await _events.RaiseAsync(new AdminWidgetCreatedEvent
        {
            AdminUserId = User.FindFirst("sub")!.Value,
            ResourceId = widget.Id,
            WidgetName = widget.Name
        }, ct);

        return Ok(widget);
    }
}
```

Other events derive from `OlusoEvent` and override `Category` (and `WebhookEventType` to offer them as webhooks).

### Database Schema

Audit logs are stored in the `AuditLogs` table, indexed on `TenantId`, `(TenantId, Timestamp)`, `Timestamp`, `Category`, `EventType`, `SubjectId`, `(ResourceType, ResourceId)`, `ClientId`, `ActivityId` and `(OriginKind, OriginId)`.

### Retention Policy

Implement a retention policy by scheduling the purge endpoint:

```bash
# Cron job to purge logs older than 90 days
0 2 * * * curl -X DELETE "https://your-server/api/admin/auditlogs/purge?olderThanDays=90" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Or use a background service:

```csharp
public class AuditLogRetentionService : BackgroundService
{
    private readonly IServiceProvider _services;

    public AuditLogRetentionService(IServiceProvider services) => _services = services;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var cutoff = DateTime.UtcNow.AddDays(-90);
            await auditService.PurgeOldLogsAsync(tenantId: null, cutoff, ct);

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

Customize the look and feel of journey pages at the tenant or journey level.

### Styling Hierarchy

1. **Default Theme** - Built-in styles (`journey.css`)
2. **Tenant Branding** - Applied to all journeys in a tenant
3. **Journey Policy UI** - Policy-specific customization

For the logo, primary color, background color and custom CSS, a value set on the journey policy replaces the tenant's value for that setting; settings the policy leaves empty use the tenant branding.

### Per-Policy Styling

Each journey policy has a `ui` configuration:

| Setting | Description |
|---------|-------------|
| `theme` | Predefined theme: `light`, `dark`, `minimal`, or empty for the default |
| `title` | Page title |
| `logoUrl` | Custom logo image URL |
| `primaryColor` | Primary brand color (hex) |
| `backgroundColor` | Background color (hex) |
| `customCss` | Arbitrary CSS rules |
| `progressStyle` | Step indicator: `horizontal` (default), `vertical`, `none` |
| `layout` | Card width: `narrow` (default, 460px), `medium` (600px), `wide` (900px), `full` |
| `localeOverrides` | Per-locale text overrides, layered over the tenant's overrides |

### Configuring in Admin UI

1. Navigate to **Experience** > **User Journeys** > select journey
2. Open the **UI Customization** section
3. Configure theme, step indicator, layout width, logo, title, colors, and custom CSS

### Available CSS Variables

The journey pages read these variables; `primaryColor` sets `--primary-color` (with derived `--primary-hover`, `--primary-light` and `--primary-glow`) and `backgroundColor` sets `--background-start` and `--background-end`:

```css
:root {
  /* Brand */
  --primary-color: #f97316;
  --primary-hover: #ea580c;
  --primary-dark: #c2410c;
  --primary-light: rgba(249, 115, 22, 0.1);
  --primary-glow: rgba(249, 115, 22, 0.35);
  --accent-color: #14b8a6;

  /* Page background (gradient) */
  --background-start: #0f172a;
  --background-end: #1e293b;

  /* Text and lines */
  --text-primary: #1a1a2e;
  --text-secondary: #6c757d;
  --text-muted: #9ca3af;
  --border-color: #e5e7eb;

  /* Status */
  --error-color: #dc2626;
  --success-color: #16a34a;
  --warning-color: #f59e0b;

  /* Text that sits directly on the page background */
  --on-bg: #ffffff;
  --on-bg-muted: rgba(255, 255, 255, 0.72);
  --on-bg-subtle: rgba(255, 255, 255, 0.4);
}
```

Themes also set `--card-bg`.

### CSS Classes

#### Container Classes
```css
body.journey-page     /* Page body (also carries theme-{name}) */
.journey-wrapper      /* Outer wrapper */
.journey-container    /* Main journey container */
.journey-brand        /* Brand name/logo row */
.journey-logo         /* Logo container (image has class .logo) */
.journey-card         /* The card holding the step */
.journey-body         /* Card content */
.journey-footer       /* Footer area */
.journey-progress     /* Horizontal step indicator */
.journey-steps-vertical /* Vertical step indicator */
```

#### Form Classes
```css
.form-group          /* Form field wrapper */
.form-row            /* Fields side by side */
.form-control        /* Input fields */
.form-select         /* Select dropdowns */
.form-check          /* Checkbox/radio wrapper */
.form-check-input    /* Checkbox/radio input */
.form-check-label    /* Checkbox/radio label */
.form-text           /* Helper text */
.otp-input-group     /* One-time code inputs */
.phone-input         /* Phone number with country picker */
```

#### Button Classes
```css
.btn                 /* Base button */
.btn-primary         /* Primary action */
.btn-secondary       /* Secondary action */
.btn-outline         /* Outlined button */
.btn-link            /* Link-style button */
.btn-external        /* External provider buttons */
.btn-passkey         /* Passkey button */
.btn-block           /* Full-width button */
```

#### Alert Classes
```css
.alert               /* Base alert */
.alert-success       /* Success message */
.alert-danger        /* Error message */
.alert-warning       /* Warning message */
```

### Custom CSS Examples

#### Modern Card Style
```css
.journey-card {
  border-radius: 16px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.1);
  background: #ffffff;
}

.journey-logo .logo {
  max-height: 40px;
}
```

#### Gradient Background
```css
:root {
  --background-start: #667eea;
  --background-end: #764ba2;
}

.journey-card {
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

#### External Provider Buttons
```css
.btn-external {
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

.btn-external:hover {
  background: #f3f4f6;
}

.btn-external img {
  width: 20px;
  height: 20px;
}
```

#### Dark Card
```css
:root {
  --card-bg: #1e293b;
  --text-primary: #f1f5f9;
  --text-secondary: #94a3b8;
  --border-color: #334155;
}

.form-control {
  background: #0f172a;
  border-color: var(--border-color);
  color: var(--text-primary);
}
```

The `dark` theme applies a complete dark palette without custom CSS.

#### Custom Form Inputs
```css
.form-control {
  width: 100%;
  padding: 12px 16px;
  border: 2px solid var(--border-color);
  border-radius: 8px;
  font-size: 16px;
  transition: border-color 0.2s, box-shadow 0.2s;
}

.form-control:focus {
  outline: none;
  border-color: var(--primary-color);
  box-shadow: 0 0 0 3px var(--primary-light);
}

.form-control::placeholder {
  color: var(--text-muted);
}
```

#### Loader Color
```css
.loading-spinner {
  border-top-color: var(--primary-color);
}
```

### Per-Tenant Branding

Configure tenant-wide branding under **Settings** > **Branding** in the Admin UI. The tenant branding settings are `primaryColor`, `secondaryColor`, `backgroundColor`, `logoUrl`, `faviconUrl`, `companyName`, `supportEmail`, `privacyPolicyUrl`, `termsOfServiceUrl`, `footerText` and `customCss`:

```json
{
  "primaryColor": "#3b82f6",
  "backgroundColor": "#0f172a",
  "logoUrl": "https://cdn.example.com/logo.png",
  "faviconUrl": "https://cdn.example.com/favicon.ico",
  "customCss": ".journey-card { border-radius: 4px; }"
}
```

### Theme Options

| Theme | Description |
|-------|-------------|
| (default) | Dark gradient page with a light card |
| `light` | Soft neutral page, white card, dark text |
| `dark` | Dark page and dark card with light text |
| `minimal` | Flat: no gradient, glow, blur or heavy shadow |

Themes only change the shell variables, so an explicit primary color still applies.

### Responsive Design

Built-in styles are mobile-responsive. Custom CSS should follow:

```css
/* Mobile-first approach */
.journey-card {
  padding: 1rem;
}

/* Tablet and up */
@media (min-width: 640px) {
  .journey-card {
    padding: 2rem;
  }
}
```

### Testing Custom CSS

1. Use browser DevTools to test styles live
2. Preview in Admin UI journey builder
3. Test across different devices and browsers
4. Verify accessibility (contrast ratios, focus states)

---

## OIDC Extensions

### Custom Parameters

#### `ui_mode`
Choose the flow type per request, within what the tenant and client allow (see [Priority Order](#priority-order)):
```
/connect/authorize?...&ui_mode=standalone
```
Values: `journey`, `standalone`

#### `policy`
Run a specific journey policy, named by its slug:
```
/connect/authorize?...&policy=signup
/connect/authorize?...&policy=register-business
```
A named policy that doesn't exist or is disabled ends with an `invalid_request` error; it never falls back to another policy.

#### `prompt=create`
Start the sign-up journey (OIDC standard). Without `policy`, the tenant's highest-priority enabled `SignUp` policy runs:
```
/connect/authorize?...&prompt=create
```

#### Journeys for someone already signed in
An app can start a journey such as registering a business, editing a profile or changing a password for someone who is already signed in. Name the journey with `policy` and say who you expect with `id_token_hint`. Leave out `prompt=login`:
```
/connect/authorize?...&policy=register-business&id_token_hint=<the ID token you hold>
```
- **Signed in as that person:** the journey runs on their session. Its sign-in step (Local Login, Composite Login, External Login and the other first-factor steps) is met by the session, so they aren't asked for their password. The journey carries on with its other steps, and the tokens you get back are for the same person and the same session.
- **Hints:** `id_token_hint` must be an ID token this server issued to your client. It may have expired. `login_hint` must be the person's email, username or phone number. If a hint you send names someone else, or the ID token can't be verified, the journey shows the normal sign-in instead.
- **Fresh sign-in:** `max_age` works as the standard says. If the session signed in longer ago than that, the person signs in again. For a sensitive journey, set `reauthenticateAfter` on its sign-in step (see Local Login). A Password Change step asks for the current password itself unless `requireCurrentPassword` is off. An MFA step asks again unless `reuseSessionMfaSeconds` allows the session's MFA.
- **`prompt=login`** still ends the session and asks for a fresh sign-in, as the standard requires, so clients that send it behave as before.
- **`prompt=none`** never shows a journey. It answers silently, as before.
- **No `policy`:** a request without one is a plain sign-in. With a live session it is answered at once, as before.

### Policy Selection

When a request names no policy, the policy type is `SignUp` for `prompt=create` and `SignIn` otherwise. The tenant's enabled policy of that type with the highest priority runs (falling back to its `SignIn` policy). With none, the tenant's `JourneyFallbackToStandard` setting decides between the standard pages and an error.

### ACR Values

`acr_values` from the authorization request is passed into the journey as the `acrValues` value, so a `condition` or `branch` step can route on it:

```json
{
  "type": "condition",
  "configuration": {
    "conditions": [
      { "type": "data", "field": "acrValues", "operator": "contains", "value": "mfa" }
    ],
    "onTrue": "mfa-step"
  }
}
```

---

## DPoP (Demonstrating Proof of Possession)

DPoP (RFC 9449) binds tokens to a client's key pair, preventing token theft.

### Enabling DPoP

Per-client in Admin UI:
1. Edit client
2. Enable **Require DPoP (Demonstrating Proof of Possession)**

Or via the client API:
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

grant_type=authorization_code&code=...&code_verifier=...
```

To bind the authorization code itself to the key, send `dpop_jkt` (the base64url SHA-256 JWK thumbprint of the public key) on the authorization request; the token request must then prove that key.

---

## Pushed Authorization Requests (PAR)

PAR (RFC 9126) improves security by pushing authorization parameters server-side.

### Enabling PAR

Per-client, enable **Require Pushed Authorization Requests (PAR)** in the Admin UI, or via the client API:
```json
{
  "requirePushedAuthorization": true,
  "pushedAuthorizationLifetime": 60
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
- Check `UseJourneyFlow` is enabled on the tenant and not turned off on the client
- Verify the policy exists, is enabled, and that `?policy=` uses its slug
- Check policy type matches request (SignIn vs SignUp)
- Without a matching policy, check the tenant's `JourneyFallbackToStandard` setting

#### Custom CSS not applying
- Ensure CSS is valid
- Check browser developer tools for errors
- A policy's `customCss` replaces the tenant branding CSS rather than adding to it

#### FIDO2 not working
- Ensure HTTPS is configured
- Verify `Oluso:Fido2:RelyingPartyId` and `Origins` match your domain
- Check that `app.UseSession()` is in the pipeline and the tenant's `EnablePasskey` is on
- Check browser WebAuthn support

#### Plugin not executing
- Verify plugin is uploaded and enabled
- Check the plugin's execution history (`/api/admin/plugins/{pluginName}/executions`)
- Ensure plugin exports the function named in `entryPoint`

### Logging

Enable detailed logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Oluso": "Debug",
      "Oluso.UserJourneys": "Debug"
    }
  }
}
```

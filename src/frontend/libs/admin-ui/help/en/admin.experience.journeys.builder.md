---
title: Journey Builder
relatedTopics:
  - User Journeys | admin.experience.journeys
  - Plugins | admin.experience.plugins
  - Data Collection | admin.experience.submissions
---

The journey builder is a visual editor for creating and editing multi-step authentication and data collection flows. The left panel shows the journey configuration and step list; the right panel shows settings for the selected step.

## Journey Settings

### Basic Properties

- **Name** — identifies the journey in the admin portal and API
- **Type** — the journey category (Sign In, Sign Up, Data Collection, etc.)
- **Priority** — lower numbers take precedence when multiple journeys match

### UI Customization

Customize the look of the authentication screens:

- **Theme** — light, dark, or minimal
- **Login Template** — Standard, Social First, Passkey First, or Combined (Tabs)
- **Logo URL** — your brand logo
- **Page Title** — heading text
- **Primary / Background Color** — brand colors with a color picker
- **Custom CSS** — override any style with free-form CSS

### Data Collection Settings

Available for data collection journey types (Waitlist, Survey, Contact Form, etc.):

- **Save form submissions** — persist submitted data for review
- **Require authentication** — users must sign in before submitting
- **Allow duplicate submissions** — whether users can submit more than once
- **Max submissions** — cap the total number of submissions (0 = unlimited)
- **Success redirect URL** — where to send users after submission
- **Success message** — confirmation text shown on completion

### Embed Settings

Control how the journey can be embedded on external sites:

- **Allowed Origins** — domains permitted to embed the journey (use `*` for all)
- **Require API key** — protect public access with an API key
- **API Key** — generate a `pk_` prefixed key for embedding

## Steps

Steps are the building blocks of a journey. They execute in order and can branch based on conditions.

### Step Types

| Category | Steps |
|---|---|
| **Authentication** | Sign In, Social Login, Combined Login, MFA, Consent |
| **User Interaction** | Custom Form, Terms Acceptance, CAPTCHA |
| **User Management** | User Registration, Password Reset |
| **Verification** | Business Verification (company registry check or the provider's hosted flow, plus the applicant's right to act for the business; resumes where an earlier attempt left off; branches verified / not verified / pending / provider unavailable; outputs `kyb_status`, `kyb_business_id`, `kyb_applicant_id`, `kyb_business_name`, `kyb_registration_number`, `kyb_country`, `kyb_relationship`) |
| **Flow Control** | Conditional Logic, Data Transformation |
| **Logic** | HTTP Request (API Call) |
| **Plugins** | Custom WASM Plugin |

### Step Configuration

Every step has:

- **Step ID** — unique identifier within the journey
- **Display Name** — label shown in the builder and optionally to users
- **Optional** — if checked, the journey continues even if this step fails

### Flow Control

Control what happens after each step:

- **On Success** — which step to go to next (default: next in list)
- **On Failure** — which step to go to on error (default: fail the journey)
- **Branches** — named conditional paths for complex routing

### Execution Conditions

Make steps conditional. A step can check journey data, user claims, or context values using operators like equals, contains, starts_with, regex, exists, and more. Combine multiple conditions with AND/OR logic.

## Key Step Types

### Custom Form (DynamicForm)

Build forms with fields of any type — text, email, number, date, select, radio, checkbox, textarea. Each field can:

- Map to a **claim type** for downstream use
- Be marked **required** with validation rules (min/max length, regex pattern)
- Be **saved to the user profile** (maps to standard or custom user properties)
- Include **help text** and **placeholder** guidance

**File upload** fields take documents and photos. Choose the accepted kinds (PDF, JPEG, PNG, HEIC, WebP, GIF), the largest file (MB, 10 by default) and how many files (1 by default). Files are checked by their content, not their name or the type the browser claims, and a file whose name doesn't match its content is refused. They're stored privately with the submission (never at a public URL): the person who uploaded them, and administrators who can read submissions, open them through the signed-in file endpoint. The field's value is the uploaded files' ids. Turn on **Take a photo** to also offer the phone's camera; when the journey is embedded in another site, the host's iframe needs `allow="camera"`.

### API Call

Make HTTP requests to external services during the journey:

- Configure URL, method, headers, and request body with **placeholders** like `{claim:email}` or `{state:userId}`
- Set authentication (Bearer, Basic, API Key)
- Map response fields to claims for use in later steps
- Configure retry logic and error handling

### Conditional Logic

Branch the journey based on data. Evaluate conditions on claims, journey state, or context, then route to different steps based on the result.

## Validation and Saving

- Click **Validate** to check for configuration errors before saving
- Click **Save** to create or update the journey
- Validation errors are shown inline if the journey has issues

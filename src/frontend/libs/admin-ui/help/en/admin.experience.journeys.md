---
title: User Journeys
relatedTopics:
  - Journey Builder | admin.experience.journeys.builder
  - Data Collection | admin.experience.submissions
  - Plugins | admin.experience.plugins
---

User Journeys are configurable multi-step flows that define how users authenticate, register, and interact with your application. They are similar to Azure AD B2C user flows and custom policies.

## Journey Types

### Authentication Journeys

| Type | Purpose |
|---|---|
| **Sign In** | Authenticate existing users |
| **Sign Up** | Register new users |
| **Sign In / Sign Up** | Combined flow — sign in or register on one screen |
| **Password Reset** | Self-service password recovery |
| **Profile Edit** | Let users update their profile information |
| **Link Account** | Connect an external identity to an existing account |
| **Consent** | Collect user consent for scopes or terms |

### Data Collection Journeys

| Type | Purpose |
|---|---|
| **Waitlist** | Collect interest before launch |
| **Contact Form** | Gather inquiries from visitors |
| **Survey** | Collect structured feedback |
| **Feedback** | Free-form user feedback |
| **Data Collection** | General-purpose form submission |

## Journey List

The table shows each journey's name, type, status, number of steps, and priority. **Priority** determines which journey is used when multiple match — lower numbers take precedence.

## Global vs Tenant Journeys

- **Global journeys** (globe icon) are system-provided templates. They cannot be edited or disabled directly, but you can **clone** them to create a tenant-specific copy.
- **Tenant journeys** (building icon) are created by you and fully editable.

Toggle **Show global policies** to include system templates in the list.

## Creating a Journey

Click **Create Journey** to open the journey builder. You will define the journey type, configure its steps, and optionally customize the UI appearance.

## Managing Journeys

- Click a journey row to edit it in the builder
- Use the **clipboard icon** to clone a global journey
- Toggle the **status switch** to enable or disable a journey

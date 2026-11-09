---
title: Activity Logs
relatedTopics:
  - Users | admin.users
  - Webhooks | admin.webhooks
  - Settings | admin.settings
---

A searchable, filterable audit trail of all security and system events. Use activity logs for compliance reporting, troubleshooting failed logins, and monitoring administrative actions.

## Log Table

Each entry includes:

- **Timestamp** — when the event occurred
- **Action** — the specific event (e.g., `UserLogin`, `ClientCreated`, `RoleAssigned`)
- **Category** — grouping such as Authentication, User, Security, Token, Client, Role, Resource, or Settings
- **User** — the user who triggered the event
- **Client** — the OAuth client involved, if applicable
- **Status** — Success or Failed
- **IP Address** — source IP of the request

## Filtering

Narrow results using:

- **Search** — free-text search across user, action, and details
- **Category** — filter by event category
- **Date Range** — From and To date pickers
- **Action** — filter by a specific action name
- **User ID** — show events for a single user
- **Client ID** — show events for a single client

Click **Apply** to run the filters, or **Clear** to reset.

## Exporting

Click **Export CSV** to download the current filtered view as a CSV file for offline analysis or compliance archives.

## Tips

- Failed login events include the IP address — useful for detecting brute-force attempts
- Check the Client column to understand which application triggered an event
- Combine the User ID filter with a date range to audit a specific user's activity

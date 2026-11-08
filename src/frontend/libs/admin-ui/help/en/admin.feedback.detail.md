---
title: Feedback Details
relatedTopics:
  - Beta Feedback | admin.feedback
  - Webhooks | admin.webhooks
---

View the full details of a feedback submission including the description, metadata, and any attached screenshot.

## Description

The user's full feedback text, exactly as submitted.

## Details

Contextual information captured automatically at submission time:

- **Submitted By** — user name and email
- **Portal** — Admin or Workspace
- **Page URL** — the page the user was on when they submitted
- **Element** — CSS selector of a highlighted UI element (if the user selected one)
- **Viewport** — the user's screen resolution
- **Browser** — the user agent string

## Screenshot

If the user captured a screenshot, it is displayed at the bottom of the page. Click the image to view it at full size.

## Updating Status

Click **Update Status** to change the triage state. Available statuses:

- **New** — default for incoming submissions
- **Reviewed** — you've seen it and are considering action
- **Resolved** — the underlying issue or request has been addressed
- **Dismissed** — closed without action (e.g., duplicate, out of scope)

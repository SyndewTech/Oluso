---
title: Beta Feedback
relatedTopics:
  - Webhooks | admin.webhooks
  - Activity Logs | admin.audit-logs
---

Review and manage feedback submissions from users in every tenant, across both the admin and workspace portals. During beta testing, this page collects bug reports, feature requests, and general notes submitted via the in-app feedback widget.

## Feedback List

The table shows all submissions with:

- **Title** — a short summary of the feedback (camera icon indicates a screenshot is attached)
- **Category** — Bug, Feature Request, or Note
- **Status** — current triage status (new, reviewed, resolved, dismissed)
- **Tenant** - the tenant the user submitted it from
- **Portal** — whether it was submitted from the Admin or Workspace portal
- **Submitted By** — the user who submitted the feedback
- **Date** — when the feedback was submitted

Click any row to view the full details.

## Filtering

Use the dropdowns to narrow results by:

- **Category** — show only bugs, feature requests, or notes
- **Status** — show only items in a specific triage state

## Statuses

| Status | Meaning |
|---|---|
| **New** | Just submitted, not yet reviewed |
| **Reviewed** | Acknowledged by an admin |
| **Resolved** | The issue has been addressed or the feature shipped |
| **Dismissed** | Closed without action |

## Webhooks

When feedback is submitted, a `feedback.submitted` webhook event is fired. Configure a webhook to forward submissions to Slack, email, or your issue tracker.

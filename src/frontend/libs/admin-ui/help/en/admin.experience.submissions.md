---
title: Data Collection
relatedTopics:
  - User Journeys | admin.experience.journeys
  - Journey Builder | admin.experience.journeys.builder
---

Review and manage form submissions from data collection journeys — waitlists, surveys, contact forms, and other custom forms. The left panel lists your data collection policies; the right panel shows submissions for the selected policy.

## Viewing Submissions

Select a data collection policy on the left to load its submissions. Each policy shows its name, submission count (relative to the max limit), and active/inactive status.

The submissions table displays:

- **Submitted** — when the submission was made (with relative time)
- **Data Preview** — the first few fields submitted
- **Source** — country and IP address of the submitter
- **Tags** — labels applied during review
- **Status** — the current review status

## Submission Statuses

| Status | Meaning |
|---|---|
| **New** | Just received, not yet reviewed |
| **Reviewed** | Looked at by an admin |
| **Processing** | Being acted on |
| **Approved** | Accepted |
| **Rejected** | Declined |
| **Follow Up** | Needs further action |
| **Archived** | Filed away, no longer active |

Change a submission's status directly from the table dropdown or from the detail view.

## Filtering

Use the filter bar to narrow submissions:

- **Search** — free-text search across submitted data
- **Status** — filter by review status
- **Date range** — filter by submission date

Click **Apply** to run filters or **Clear** to reset.

## Submission Details

Click the **eye icon** on any row to open the full submission view:

- **Collected Data** — all fields and values submitted
- **Metadata** — IP address, country, locale, referrer, user agent, device, browser, and OS
- **Tags** — add, remove, and manage labels. Suggested tags from other submissions appear for consistency.
- **Notes** — free-text notes for internal review
- **Review info** — who reviewed it and when

## Bulk Actions

Select multiple submissions using the checkboxes, then use the bulk action bar to:

- **Approve** — mark selected as approved
- **Reject** — mark selected as rejected
- **Archive** — move selected to archived
- **Delete** — permanently remove selected submissions

## Exporting

Click **CSV** or **JSON** in the header to export the current policy's submissions. Exports respect any active filters.

## Journey URL

The **Journey URL** shown in the header is the public link for the data collection form. Click the copy icon to copy it to your clipboard for sharing or embedding.

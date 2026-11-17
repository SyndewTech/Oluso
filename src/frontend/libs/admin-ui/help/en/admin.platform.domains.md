---
title: Domains
relatedTopics:
  - Organizations | admin.organizations
  - Organization settings | admin.tenants.detail
---

This page lists every organization's custom domains in one place, including domains that were removed or failed verification. It is only visible to Super Admins.

A domain has a single record across the platform. When an organization adds a domain that another organization removed, or that failed verification, the existing record is reopened for the new organization. A removed or failed record listed here is therefore what explains a domain that an organization can't add, or one that moves between organizations.

## The list

- **Domain**: the hostname. **Serving sign-in** means it is the organization's current custom domain. The last error, when there is one, is shown underneath.
- **Organization**: the organization holding the record. Click it to open that organization on its Domains tab.
- **Status**: Pending (waiting for the DNS TXT record), Verified (ownership proven, waiting for the edge certificate), Active (serving traffic), Failed (verification timed out) or Removed. **Edge failed** is a verified domain whose edge hostname couldn't be provisioned.
- **Edge**: whether the edge hostname and certificate were provisioned, once ownership is proven.
- **Checks**: how many times verification ran, when it last ran, and when an active domain's DNS was last revalidated. A domain whose DNS check has started failing shows when that began.
- **Dates**: when the domain was added, verified and activated.

Search by domain or organization, and click a status to show only those domains.

## Actions

- **Verify now** checks the DNS TXT record of a pending domain straight away.
- **Retry provisioning** asks the edge again for a verified domain whose hostname or certificate failed.
- **Retry verification** starts a failed domain's verification over.
- **Remove** switches a domain off for its organization. If it was serving sign-in, the organization stops using it straight away. The record is kept.
- **Restore** adds a removed domain back to the organization that held it. Verification starts again with the same TXT record.

These are the same actions the organization's own Domains tab offers.

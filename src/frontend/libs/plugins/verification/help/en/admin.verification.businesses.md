---
title: Business Verification
relatedTopics:
  - Journey Builder | admin.experience.journeys.builder
  - Settings | admin.settings
---

Business verification (KYB) checks that a business is real and that the person signing up may act for it. The **Business Verification** journey step does the checking; this area shows what it found.

## Businesses and the people acting for them

Each business appears once per registration number, with its latest registry record (name, status, address, directors). The people acting for it are listed under it, each with their own status, because one business can have several representatives and one person can represent several businesses.

A person acts for a business on one of these grounds:

| Grounds | Meaning |
|---|---|
| **Director on the registry** | Their first and last name match a director, proprietor, partner or trustee the registry lists |
| **Declared representative** | They say they represent the business; when the step asks for documents, a reviewer approves them |
| **Verified by the provider** | The provider's hosted flow verified the business with them present |

## Statuses

| Status | Meaning |
|---|---|
| **Pending** | Nothing decided yet: the provider couldn't answer, or documents are still to come |
| **In review** | Waiting on the provider's hosted check or on a reviewer |
| **Verified** | The business checks out / the person may act for it |
| **Rejected** | Not found, not trading, a name or director mismatch, or the provider or a reviewer said no |
| **Failed** | The applicant abandoned the provider's flow |

## Reviewing

The **Review queue** lists everyone waiting on a decision, oldest first. On a business you can:

- **Verify or reject the business** when the registry gave no answer (provider unavailable). People waiting on it follow: a rejection rejects them; a verification verifies those the step asked nothing more of and moves the rest to review.
- **Approve or reject a person**, for example after opening their documents. Approving needs the business to be verified first.
- **Ask the provider again** for a hosted check whose webhook never arrived.

Every decision is recorded in the business's **Checks** with who made it, raises the `business.verification_completed` webhook event, refreshes the applicant's waiting page and, if they asked, emails them. Reviewing needs the *Review business verifications* permission; seeing businesses and documents needs *View business verifications*.

## Documents

Documents (certificate of incorporation, letter of authority, photos taken on a phone) are stored privately. **Open** downloads them through the admin API; there is no public link. The people linked to the same business can also download them from their account.

## Checks

Every registry lookup, reuse of an earlier registry result, hosted session and review is kept as a check, newest first, with the provider's reference and who caused it (a journey, a provider webhook or an administrator). Nothing is overwritten.

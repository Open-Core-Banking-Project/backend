---
decision: P1-E03-D07, P1-E03-D08
title: Provider retry, failover, status enquiry and raw evidence handling
epic: P1-E03 Customer and Onboarding
owners:
  - Integration
  - Provider owner
  - Compliance
  - Security
required_by: Slice 8
status: Approved
approved_on: 2026-10-30
brief_date: 2026-10-30
related: [P1-E03-D06, P1-E03-D14]
decisions: [P1-E03-D07, P1-E03-D08]
---

# Decision brief: provider retry, failover, status enquiry and raw evidence

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D07-A | When may a submission be retried? | Only when the failure happened **before** the provider accepted the submission (transport or route unavailable). Such a submission is recorded as a cancelled attempt, and the retry is a **new attempt** on the same or an approved alternate route, up to a configured attempt limit. A failure after the provider accepted the submission is `Uncertain` and is never resubmitted blindly |
| D07-B | How is an `Uncertain` attempt resolved? | Only by **status enquiry** against the same route and provider reference. Enquiry is bounded, keeps the verification `Uncertain` while the provider reports pending, and leaves it `Uncertain` with outstanding work when the bound is reached. An enquiry is never treated as a fresh verification request |
| D07-C | When is failover allowed? | Only to a route the approved routing configuration lists for the same check code, and only when a status enquiry proves the earlier attempt produced no accepted result. Two accepted outcomes for one verification are never created |
| D07-D | What happens when two accepted results conflict? | The verification becomes `ManualReview` and an operator decides with recorded evidence. There is no automatic winner and no silent overwrite |
| D07-E | How is a replayed or forged provider result handled? | A result for an attempt that is already resolved, a result whose provider reference belongs to another verification, or an idempotency key reused with a different payload is refused with a stable code and recorded as security evidence. Re-delivery of an identical result is idempotent |
| D08-A | Is raw provider evidence stored? | No. This service stores an **evidence reference** (opaque Integration Hub reference), its SHA-256 hash, the provider route identifier and the mapping version. Raw payloads, documents and biometrics stay with the provider or Integration Hub |
| D08-B | How is evidence accessed? | Through Integration Hub under its own authorisation and purpose. It never appears in events, logs, Customer 360, audit detail or non-production fixtures |
| D08-C | What is the retention of evidence references? | They follow `P1-E03-D14`: append-only in this service and never deleted by code. Provider-side retention remains the provider agreement's |
| D08-D | What does a canonical result require? | A succeeded attempt must carry the canonical outcome, the mapping version that produced it and an evidence reference with a hash. Missing evidence is refused (`kyc_evidence_reference_required`), never defaulted |

## 2. Approved synthetic adapter

`SyntheticKycProviderAdapter` is the only adapter bound in Phase 1. It is deterministic, uses no network, and
its behaviour is a **script** supplied as configuration or by a test: pre-submission unavailability, pending
enquiries before a result, and the canonical outcome to report. It defines no KYC rule and no jurisdictional
policy; check codes, routes and outcomes remain approved configuration data.

## 3. Constraints

- A retry never reuses an attempt identifier: every submission is a new, immutable attempt record.
- Timeout after submission is not failure. It becomes `Uncertain` and raises status-enquiry work.
- Status enquiry never mutates the requirement directly; it resolves the attempt, and the requirement follows
  the canonical outcome through the existing rules.
- Evidence references are validated as stable codes with a bound length and are never free text carrying PII.
- Every automatic retry, failover and enquiry is audited with the verification, attempt and route identifiers.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Approved provider routes, check codes and mapping tables | Compliance and Integration | Policy content, not code |
| Attempt limits, enquiry bounds and poll intervals | Integration and Operations | Operational tuning per provider |
| Real provider adapter and credential handling | Integration Hub | Requires the provider agreement |
| Provider-side evidence retention and erasure terms | Compliance and the provider owner | Contractual |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved bounded pre-submission retries, status enquiry before any failover, manual review on conflict, evidence references only, and a deterministic synthetic adapter | 2026-10-30 |
| Integration | | Named confirmation required before any real provider route is bound | |
| Compliance | | Named confirmation required before check codes and mapping tables are published | |

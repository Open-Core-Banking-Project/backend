---
phase: "Phase 3 - Growth Channels"
epic: "Virtual Accounts and Collection Matching"
owning_service: "Payments and VTU with Integration Hub"
modules: [Virtual Accounts, Collection References, Incoming Funds, Matching, Investigations]
status: Draft
requirements: [PAY-008..PAY-010, PAY-013..PAY-015, CHN-006, PSP-003..PSP-006]
stories: [US-031]
dependencies: [Phase 1 Payments Providers Ledger, Phase 2 Reconciliation]
adrs: [Virtual account ownership/lifecycle, Collection finality and matching]
last_updated: 2026-10-16
---

# Epic 04: Virtual Accounts and Collection Matching

## 1. Objective

Issue static/dynamic virtual accounts linked to institution, customer/invoice and collection reference, then validate and idempotently match incoming funds while routing unmatched collections to investigation.

## 2. Success measures

- Account issuance/lifecycle and provider mapping are unique, traceable and tenant-bound.
- Notifications are authenticated, deduplicated and verified before final posting.
- Matching is deterministic and creates one collection/payment/ledger outcome.
- Unmatched/ambiguous/duplicate funds remain visible in controlled investigation and reconciliation.

## 3. Scope

Included: static/dynamic issuance, reference/expiry/status, provider mapping, incoming event validation, verification, matching, posting orchestration, unmatched queue, refund/correction reference and reconciliation. Excluded: client-chosen settlement accounts and provider callback as final proof.

## 4. Actors, roles and permissions

Collections administrator `collection.virtual_account.manage`; authorised service/client `collection.virtual_account.issue`; investigator `collection.unmatched.resolve`; provider identity `collection.notification.receive`. Scope includes institution, customer/invoice, provider, account and amount; manual resolution requires reason/approval.

## 5. Domain model

Concepts: `VirtualAccount`, `ProviderMapping`, `CollectionReference`, `IncomingCollection`, `MatchDecision`, `UnmatchedCollection`, `CollectionPosting`. State account `Pending -> Active -> Expired/Closed`; collection `Received -> Validating -> Verified -> Matched -> PostingPending -> Posted`, with `Unmatched`, `Duplicate`, `Uncertain`, `Rejected`.

Invariants: unique provider account mapping; reference belongs to same tenant; raw notification is not financial truth; amount/currency/settlement account server validated; provider/event/reference deduplication; ledger alone posts; manual match/adjustment audited.

## 6. Use cases

Issue static/dynamic account; expire/close; receive/authenticate notification; verify provider status; exact/reference match; open/resolve unmatched case; post once; reconcile provider/collection/ledger.

## 7. API contracts

Routes: `POST /virtual-accounts`, `GET /virtual-accounts/{id}`, `POST /provider-collections/{provider}/webhooks`, `GET /collections/{id}`, `GET /unmatched-collections`, `POST /unmatched-collections/{id}/actions`. Webhooks acknowledge only after durable receipt; financial state stays explicit.

## 8. Event contracts

Produce `VirtualAccountIssued/Expired`, `IncomingCollectionVerified`, `CollectionMatched`, `CollectionPostingRequested/Completed`, `UnmatchedCollectionOpened/Resolved`. Consume provider verification and ledger outcomes. Deduplicate event/provider reference and order by virtual account/collection.

## 9. Database design

`payments_vtu_db`: virtual accounts, provider mappings, collection references, incoming collections, match decisions, unmatched cases refs, posting instructions; Integration Hub owns raw webhook/provider attempts. Enforce tenant/provider/account/reference uniqueness, explicit amount/currency, expiry, immutable raw digest and idempotency/posting refs.

## 10. Consistency, concurrency and idempotency

Durable receipt/inbox precedes async processing. Match/post state and outbox commit locally. Concurrent events use unique provider event/account/reference constraints. Uncertain posting is queried/reconciled. No distributed transaction.

## 11. Security and privacy

Verify signature/source/timestamp/event/replay, encrypt provider evidence, mask account/customer data and rate-limit. Test forged/replayed webhook, cross-tenant reference, amount/settlement tampering, enumeration and manual-match abuse.

## 12. Audit and observability

Audit issuance/mapping, notifications, verification, match rule/result, posting refs and manual actions. Metrics: issue failure, notification lag, matched/unmatched/duplicate/uncertain age/value and reconciliation breaks. No raw provider payloads in logs.

## 13. Failure, retry and recovery

Invalid webhook rejected/quarantined. Provider timeout remains pending/uncertain. Duplicate delivery returns prior result. Matching worker resumes from inbox/checkpoint. Restore verifies mapping, collection counts, postings and open cases.

## 14. Configuration

Approve providers/services, static/dynamic lifecycle, expiry, reference format, matching hierarchy, amount tolerance if any, settlement mapping, fees/refunds, retention and investigation SLA.

## 15. Implementation slices

1. Ownership/provider contracts. 2. Issuance/lifecycle. 3. Secure webhook receipt. 4. Verification/matching. 5. Posting/unmatched workflow. 6. Reconciliation/load/runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| VAC-01 | Static/dynamic issuance | Tenant/customer/reference linked |
| VAC-02 | Valid notification | Verified/matched/posted once |
| VAC-03 | Replay/concurrent duplicate | One outcome |
| VAC-04 | Unmatched/ambiguous reference | Investigation queue |
| VAC-05 | Provider timeout | Explicit uncertainty/status enquiry |
| VAC-06 | Cross-tenant/tampered amount | Denied/no posting |
| VAC-07 | Reconciliation | Provider/payment/ledger agree |

## 17. Acceptance criteria

- [ ] Virtual accounts link tenant, customer/invoice and collection reference.
- [ ] Incoming notifications validate and match idempotently.
- [ ] Unmatched collections enter controlled investigation.
- [ ] Final outcomes verify provider and ledger state and reconcile.

## 18. Definition of Done

Repository DoD plus approved provider/lifecycle/matching ADR, migrations, webhook/provider contract tests, concurrency/idempotency/reconciliation/load evidence, dashboard and runbook.

## 19. Open decisions

Providers, ownership, lifecycle/expiry, reference format, verification/finality, matching/tolerance, settlement/refund/fee policy and volumes remain open.

## 20. Prohibited assumptions

Do not trust callback as final, infer tenant/customer, allow duplicate posting, silently force ambiguous match, or accept client settlement/fee values.

## 21. Agent implementation instructions

Read payment/provider/ledger/reconciliation epics. Keep adapters in Integration Hub and collection state in Payments. Run webhook, migration, replay/concurrency, security, posting, reconciliation and recovery tests. Stop for unresolved finality/matching policy.


---
phase: "Phase 3 - Growth Channels"
epic: "Card Lifecycle and Controls"
owning_service: "Requires card-boundary ADR; provisional Payments and VTU module"
modules: [Card References, Issuance, Lifecycle, Controls, Processor Integration]
status: Draft
requirements: [CHN-001, CHN-003, API-005..API-007, DAT-006, SEC-001..SEC-013]
stories: [US-062]
dependencies: [Phase 1 Accounts Payments Providers, Phase 2 Risk and Reconciliation]
adrs: [Card ownership and processor, PCI scope/tokenisation, Authorization and clearing]
last_updated: 2026-10-16
---

# Epic 06: Card Lifecycle and Controls

## 1. Objective

Issue/reference and safely activate, freeze, replace and limit physical or virtual cards through approved processors while tokenising card data and enforcing channel, amount, geography and merchant-category controls.

## 2. Success measures

- Card lifecycle state converges with processor state and remains explicitly uncertain when ambiguous.
- PCI-sensitive data is tokenised and not stored unnecessarily; PIN/CVV never enter ordinary application storage/logs.
- Controls are server/processor enforced under approved capability and effective version.
- Every sensitive action requires appropriate authentication and immutable audit evidence.

## 3. Scope

Included: card/product/reference, issuance request, activation, freeze/unfreeze, replace, close, limits/channel/geography/MCC controls, processor attempts/status and reconciliation hooks. Excluded: choosing processor, custom PIN/cryptography, storing raw PAN/CVV, ATM switch and full disputes unless separately selected.

## 4. Actors, roles and permissions

Customer `card.self.manage` for owned card/account; card officer `card.lifecycle.manage` under institution/branch; supervisor `card.override.approve`; processor identity `card.processor.callback`. Step-up for activation/unfreeze/replacement/limit increase; overrides use maker-checker.

## 5. Domain model

Concepts: `CardReference`, `CardholderLink`, `ProcessorToken`, `CardLifecycleRequest`, `CardControlVersion`, `ProcessorAttempt`. State `Requested -> Issuing -> Inactive -> Active -> Frozen -> Replaced/Closed`, with `Uncertain` synchronization state.

Invariants: token not PAN is domain identity; card links authorised customer/account; lifecycle transitions legal and idempotent; current controls never exceed product/customer/risk limits; client cannot set final processor status; replacement links history; only ledger posts financial outcomes.

## 6. Use cases

Request physical/virtual card; activate; freeze/unfreeze; replace; close; set limits/channel/geography/MCC controls; query safe status; process authenticated processor result; reconcile processor/internal state.

## 7. API contracts

Routes: `POST /cards`, `/cards/{id}/activate`, `/freeze`, `/unfreeze`, `/replace`, `/close`, `PUT /cards/{id}/controls`, `GET /cards/{id}` and processor callbacks through Integration Hub. Responses return masked/token references only.

## 8. Event contracts

Produce `CardRequested/Issued/Activated/Frozen/Replaced/Closed`, `CardControlsChanged`, `CardProcessorStateUncertain`. Consume customer/account/risk and processor canonical events. Deduplicate processor event and order by card reference.

## 9. Database design

No schema before card ADR/PCI review. Approved owner stores card references/tokens, account/customer refs, lifecycle history, control versions, requests and processor attempt refs. No raw CVV/PIN; PAN only if explicitly required, tokenised/encrypted in PCI boundary. Unique processor token/card ref, tenant scope and optimistic state version.

## 10. Consistency, concurrency and idempotency

Local request/outbox then processor convergence. Operation key scopes card/action/client key. Concurrent freeze/unfreeze/replace uses version/transition guards. Timeout remains uncertain and status-queried. No distributed transaction.

## 11. Security and privacy

PCI scope assessment, tokenisation, HSM/processor PIN handling, step-up, ownership, rate limits and masking. Test IDOR, PAN/CVV/PIN leakage, processor callback forgery/replay, control bypass, lost/stolen card race and cross-tenant access.

## 12. Audit and observability

Audit lifecycle/control actions, actor/auth strength, reason, before/after, processor/evidence refs. Metrics: issue/activation failures, uncertain state, control declines and processor latency/errors; never use PAN/token as metric label.

## 13. Failure, retry and recovery

Processor timeout stays uncertain; freeze safety policy requires approval. Callback replay deduplicates. Lost/stolen workflow prioritizes containment. Restore verifies tokens, lifecycle/control versions and processor reconciliation.

## 14. Configuration

Approve card products/types, processor capabilities, channels, limits, geography/MCC, authentication, replacement, fees, uncertain-state behavior, PCI retention and reconciliation.

## 15. Implementation slices

1. Ownership/PCI/processor ADRs. 2. Token/reference and issuance. 3. Lifecycle controls. 4. Limit/channel/geography/MCC. 5. Callback/status/reconciliation. 6. Security/operations.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| CLC-01 | Issue/activate/freeze/replace | Legal audited transitions |
| CLC-02 | Limit/channel/geography/MCC | Enforced by approved policy |
| CLC-03 | Processor timeout/replay | Uncertain/query; one transition |
| CLC-04 | Unauthorized card action | Denied/audited |
| CLC-05 | Sensitive-data scan | No PAN/CVV/PIN leakage |
| CLC-06 | Concurrent lifecycle actions | One valid outcome |
| CLC-07 | Restore/reconcile | Internal/processor state accounted |

## 17. Acceptance criteria

- [ ] Physical/virtual activation, freeze, replacement and limits work safely.
- [ ] Card data is tokenised and PCI-sensitive values are not stored unnecessarily.
- [ ] Channel, amount, geography and merchant-category controls are enforced.
- [ ] Strong authentication, audit and processor reconciliation are complete.

## 18. Definition of Done

Repository DoD plus ownership/PCI/processor approval, migration evidence, sensitive-data scans, processor contracts/certification, concurrency/replay/reconciliation tests and runbooks.

## 19. Open decisions

Owner/database, processor, PCI scope, token/PAN custody, card products, PIN, controls, fees, authorization/clearing, disputes and uncertain-state policy remain open.

## 20. Prohibited assumptions

Do not create a new service before ADR, store CVV/PIN, expose PAN, invent processor semantics, claim timeout success or let UI controls replace backend enforcement.

## 21. Agent implementation instructions

Stop before persistence/provider code until card/PCI ADRs are approved. Keep processor adapter in Integration Hub and money in Ledger. Run PCI/security, migration, contract, concurrency, replay and reconciliation tests.


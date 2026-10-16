---
phase: "Phase 4 - Advanced Banking"
epic: "Trade Finance Instruments and Correspondent Messaging"
owning_service: "Requires trade-finance-boundary ADR; coordinated across Operations, Integration, Risk and Core Banking"
modules: [Trade Instruments, Letters of Credit, Guarantees, Documents, Correspondent Messaging, Settlement]
status: Draft
requirements: [LED-001..LED-013, WFL-001..WFL-008, RSK-003..RSK-008, API-001..API-007, SEC-001..SEC-018, AUD-001..AUD-004]
stories: [US-078]
dependencies: [Corporate Parties and Mandates, Documents, Risk and Sanctions, Workflow, Ledger, Reconciliation, Integration Hub]
adrs: [Trade-finance ownership, Instrument lifecycle/legal rules, SWIFT/correspondent messaging, Trade accounting and settlement]
last_updated: 2026-10-16
---

# Epic 02: Trade Finance Instruments and Correspondent Messaging

## 1. Objective

Originate, amend, approve, message and settle selected letters of credit and guarantees with validated trade limits, margins, collateral, charges, expiry, sanctions checks and traceable SWIFT/correspondent interactions.

## 2. Success measures

- Selected instrument types have approved legal/lifecycle requirements and immutable version history.
- Limits, margin, collateral, charges, parties, documents and expiry validate before approval and settlement.
- Sanctions screening is current at required checkpoints; potential matches block progression pending review.
- Correspondent messages are schema/security validated, deduplicated and traceable to instrument/version/action.
- Settlement components post once through Ledger and reconcile to messages, fees, collateral/margin and correspondent evidence.

## 3. Scope

Included: selected LC/guarantee application, version/amendment, parties, terms, document references, limits/margin/collateral/charges, approval, issue/advice/message, expiry, claim/draw/settlement only where baselined, discrepancies and reconciliation. Excluded: assuming all UCP/URDG/SWIFT/country rules, full correspondent account management, documentary examination or every trade product without detailed requirements.

## 4. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Trade officer | `trade.instrument.create` | Institution/corporate/product/country | Submit/amend | Maker |
| Trade approver | `trade.instrument.approve` | Amount/currency/limit/product | Required | Multi-level as configured |
| Compliance reviewer | `trade.sanctions.review` | Instrument/party/match | Required | Independent |
| Message releaser | `trade.message.release` | Instrument/message/network | Required | Dual control |
| Settlement officer | `trade.settlement.execute` | Approved instrument/action | Required | Maker-checker |

Corporate mandate and bank authority both apply where configured. Message-network access never implies trade approval or ledger authority.

## 5. Domain model

| Concept | Responsibility | Key rules |
| --- | --- | --- |
| `TradeInstrument` | Stable identity and lifecycle | Type selected from approved catalogue |
| `InstrumentVersion/Amendment` | Terms, parties, amount, currency, expiry | Approved versions immutable and linked |
| `TradePartyRole` | Applicant, beneficiary, issuing/advising/correspondent parties | Verified and screened |
| `TradeAssessment` | Limits, margin, collateral, charges and documents | Exact policy/source versions |
| `CorrespondentMessage` | Canonical message/action/evidence | Schema/network/security/version validation |
| `TradeSettlementInstruction` | Financial components and authority | Idempotent ledger outcome |
| `TradeDiscrepancy` | Document/message/settlement exception | Owner, evidence, SLA, resolution |

Generic state: `Draft -> Submitted -> Assessment/Screening -> ApprovalPending -> Approved -> MessagePending -> Issued/Active -> Amended/ExpiryPending -> Expired/Cancelled/Settled`, with `Rejected`, `Held`, `MessageUncertain`, `SettlementUncertain`. Instrument-specific state requires approved requirements.

Invariants: amendment never rewrites prior version; amount/currency/dates explicit; approved terms bind message/settlement; current sanctions check required at configured checkpoints; client/correspondent messages cannot declare financial success; charges/margin/collateral explicit; Ledger alone changes balances; retry/replay cannot duplicate message or posting.

## 6. Use cases

### UC-01 - Originate and assess instrument

Capture selected instrument terms, parties, documents and consent; validate corporate authority, trade limit, margin, collateral, charges and expiry; screen parties; route approval.

### UC-02 - Approve and issue/message

Bind approval to exact version/assessment/screening; generate canonical message; validate schema/security; dual-control release; track acknowledgement/rejection/uncertainty.

### UC-03 - Amend instrument

Create linked version, recalculate effects, re-screen/reapprove as policy requires and message exact amendment.

### UC-04 - Settle/expire/cancel

Validate lifecycle/documents/screening/authority, submit idempotent ledger components, reconcile and close only after evidence.

### UC-05 - Investigate discrepancy

Assign message/document/amount/settlement discrepancy, preserve evidence and resolve through approved workflow without editing history.

## 7. API contracts

| Method | Route | Permission | Idempotent |
| --- | --- | --- | --- |
| POST | `/api/v1/trade-instruments` | `trade.instrument.create` | Request key |
| PUT | `/api/v1/trade-instruments/{id}/draft` | `trade.instrument.update` | Version token |
| POST | `/api/v1/trade-instruments/{id}/submit` | `trade.instrument.submit` | Instrument/version |
| POST | `/api/v1/trade-instruments/{id}/decisions` | `trade.instrument.approve` | Decision key |
| POST | `/api/v1/trade-instruments/{id}/amendments` | `trade.instrument.amend` | Amendment key |
| POST | `/api/v1/trade-messages/{id}/release` | `trade.message.release` | Message/version key |
| POST | `/api/v1/trade-instruments/{id}/settlements` | `trade.settlement.execute` | Settlement key |

Stable errors cover missing document, limit/margin/collateral/charge/expiry violation, stale version, screening hold, approval/dual-control, invalid message, period lock and uncertain message/settlement.

## 8. Event contracts

Produce `TradeInstrumentSubmitted/Approved/Issued/Amended/Expired/Settled`, `TradeInstrumentHeld`, `CorrespondentMessageReleased/Acknowledged/Rejected/Uncertain`, `TradeDiscrepancyOpened/Resolved`. Consume corporate party/mandate, document, risk/screening, workflow, collateral, ledger and correspondent canonical results. Order by instrument/message; outbox/inbox and event/provider-message deduplication required.

## 9. Database design

No migration before trade-owner ADR and detailed requirements. Candidate owner stores instrument/version, party refs, terms, document/evidence refs, assessments, decisions, amendments, lifecycle, message refs and settlement instructions. Integration Hub stores canonical/provider message attempts/evidence; Operations stores approval/discrepancy cases; Ledger owns postings; Audit/Reconciliation owns trace/breaks.

Constraints: tenant/instrument/version/message/business reference uniqueness, explicit decimals/currency/rate where applicable, separate trade/value/expiry/message/settlement dates, append-only approved versions/decisions/message evidence, optimistic transitions, legal hold/retention and no cross-service foreign keys.

## 10. Consistency, concurrency and idempotency

Each owner commits local state/outbox. Instrument version digest binds assessment/approval/message/settlement. Message key includes network/instrument/version/action; settlement key includes instrument/version/action/component. Concurrent amendments/settlements use optimistic guards. Timeouts remain uncertain and status/reconciliation resolves them; no distributed transaction.

## 11. Security and privacy

Protect trade documents, pricing, counterparties, messages, account and sanctions evidence. Use step-up, dual control, network identity, mTLS/signing/encryption/HSM where approved, source/timestamp/replay checks and least privilege. Test message forgery/replay, version/beneficiary/account substitution, sanctions bypass, document-reference attack, cross-tenant access and payload leakage.

## 12. Audit and observability

Audit instrument/version/terms, parties/documents, assessments, screening, decisions, overrides, message validation/release/attempt/result, charges/margin/collateral, settlement/postings and discrepancies. Metrics: application/approval/message age, holds, expiry, rejects/timeouts, settlement uncertainty/breaks and certificate expiry. Do not log message/document bodies.

## 13. Failure, retry and recovery

Missing/failed screening blocks controlled checkpoints. Invalid message never releases. Network timeout creates message uncertainty and status enquiry/manual runbook, not new message identity. Settlement timeout is requeried/reconciled. Worker/job replay uses checkpoints and does not duplicate messages/postings. Restore verifies versions, message digests, approvals, open discrepancies and ledger refs.

## 14. Configuration

Version/approve instrument catalogue/lifecycle, country/legal/document rules, authority/limits, margin/collateral, charges/taxes, expiry/cutoffs/calendars, sanctions checkpoints, message types/schemas/mappings, network/security/certificates, settlement/accounting, SLA and retention.

## 15. Implementation slices

1. Baseline detailed requirements and ownership/legal/accounting/message ADRs.
2. Instrument/version/party/document model and permissions.
3. Assessment for limits/margin/collateral/charges/expiry.
4. Screening and multi-level workflow approval.
5. Canonical correspondent message validation/release/status.
6. Amendment and discrepancy handling.
7. Settlement/ledger/reconciliation, audit, dashboards and recovery rehearsal.

## 16. Test plan

| ID | Scenario | Expected evidence |
| --- | --- | --- |
| TFC-01 | Valid selected LC/guarantee | Complete versioned lifecycle |
| TFC-02 | Limit/margin/collateral/charge/expiry violation | Approval blocked |
| TFC-03 | Sanctions potential match | Hold before approval/settlement |
| TFC-04 | Maker self-approval/authority exceeded | Denied/escalated |
| TFC-05 | Amendment | Prior version retained; revalidation |
| TFC-06 | Invalid/tampered/replayed message | Rejected; no duplicate release |
| TFC-07 | Network timeout | Explicit uncertainty/status resolution |
| TFC-08 | Settlement retry | One balanced ledger outcome |
| TFC-09 | Message/instrument/ledger mismatch | Open reconciliation discrepancy |
| TFC-10 | Cross-tenant/document access | Denied/audited |
| TFC-11 | Restore/replay | Versions/messages/postings preserved once |

## 17. Acceptance criteria

- [ ] Selected letters of credit and guarantees support approved originate, amend, approve, expiry and settlement lifecycles.
- [ ] Trade limits, margins, collateral, charges and expiry are validated under versioned policy.
- [ ] Sanctions screening occurs before approval and settlement.
- [ ] SWIFT/correspondent messages are validated, secure, deduplicated and end-to-end traceable.
- [ ] Financial components post idempotently through Ledger and reconcile.
- [ ] Detailed trade/legal/message requirements and certifications are approved.

## 18. Definition of Done

Repository DoD plus approved requirements/ownership/legal/accounting/message ADRs, migrations, document/sanctions/workflow tests, provider/network certification, message security/replay, posting/reconciliation, recovery rehearsal, dashboards/runbooks and stakeholder sign-off.

## 19. Open decisions

Owner/database, selected instrument/rules, governing law/standards, parties/documents, limits/authority, margin/collateral, charges/taxes, expiry/claims, SWIFT/network/message types/mappings/security, correspondent behavior, settlement/suspense/accounting, retention and targets remain open.

## 20. Prohibited assumptions

Do not infer UCP/URDG/SWIFT/legal rules, create unsupported instrument states, treat message acknowledgement as settlement, bypass sanctions, mutate approved versions, retry uncertain messages with new identity, or post outside Ledger.

## 21. Agent implementation instructions

Required reading: Phase 4 overview, corporate/mandate/document/risk/workflow/ledger/reconciliation/integration epics and approved legal/provider specifications. Stop before persistence if ownership or detailed requirements are missing. Run build, migration, contract, sanctions, message security/replay, concurrency, posting, reconciliation, tenant/privacy and recovery tests; report actual evidence and unresolved risk.


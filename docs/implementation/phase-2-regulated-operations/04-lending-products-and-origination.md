---
phase: "Phase 2 - Regulated Operations"
epic: "Lending Products and Origination"
owning_service: "Requires lending-boundary ADR; provisional module within Core Banking and Ledger"
modules: [Loan Products, Simulation, Applications, Credit Assessment, Approval, Disbursement]
status: Draft
requirements: [LND-001, LND-002, LND-003, LND-004, LED-001..LED-010, WFL-001..WFL-008, SEC-014]
stories: [US-038, US-039]
dependencies: [Epic 02 Corporate Party Onboarding, Epic 06 Risk Fraud AML and Sanctions, Phase 1 Ledger and Workflow]
adrs: [Lending service and data ownership, Credit decision boundary, Loan accounting and calculation]
last_updated: 2026-10-16
---

# Epic 04: Lending Products and Origination

## 1. Objective

Define approved loan products, simulate repayment expectations, capture complete applications, assess affordability/risk and disburse approved credit exactly once through the protected ledger.

## 2. Success measures

- Product versions preserve eligibility, flat, reducing-balance and other approved interest methods, fees, tenor, collateral and repayment rules.
- Simulations disclose separated principal, interest, fees and total repayment under the exact version.
- Applications retain consent, affordability, documents, guarantors/collateral references and assessment evidence.
- Authority thresholds and maker-checker precede a single balanced disbursement.

## 3. Scope

Included: product configuration/publication, calculation simulation, application, evidence, assessment, risk decision, approval, offer/acceptance and disbursement orchestration. Excluded: servicing/delinquency (Epic 05), invented bureau/provider semantics, and a new microservice before the lending-boundary ADR.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Loan product manager | `lending.product.manage` | Institution/product | Publish | Maker-checker |
| Applicant/loan officer | `lending.application.create` | Customer/branch/product | Sensitive submit | Maker |
| Credit assessor | `lending.application.assess` | Assigned amount/risk | Required | Separate role |
| Credit approver | `lending.application.approve` | Authority/amount/product | Required | No self-approval |
| Disbursement identity | `lending.disbursement.execute` | Approved application only | Workload identity | Prior approval |

## 5. Domain model

`LoanProductVersion` owns calculation/eligibility references; `LoanSimulation` is time-bound and non-binding; `LoanApplication` owns requested terms and evidence; `CreditAssessment` records inputs/recommendation; `LoanOffer` freezes approved terms; `DisbursementInstruction` links one offer to one ledger outcome.

State: `Draft -> Submitted -> Screening/Assessment -> ApprovalPending -> Approved -> OfferAccepted -> DisbursementPending -> Disbursed`; terminal/exception paths: `Rejected`, `Withdrawn`, `Expired`, `DisbursementUncertain`, `DisbursementFailed`.

Invariants: product/rule version is immutable after publication; approval never equals posting; offer cannot exceed approved authority; applicant-supplied affordability/risk values are untrusted; one accepted offer creates at most one financial outcome; every amount has explicit currency.

## 6. Use cases

- Configure, simulate and publish a loan product through maker-checker.
- Simulate an eligible product with approved calculation policy.
- Submit individual/corporate application with consent/evidence.
- Obtain screening, affordability and risk results; route exceptions.
- Approve within authority; issue and accept versioned offer.
- Submit idempotent ledger disbursement and converge on authoritative outcome.

## 7. API contracts

| Method | Route | Permission | Idempotency |
| --- | --- | --- | --- |
| POST | `/api/v1/loan-products/{id}/simulations` | `lending.product.simulate` | Input digest |
| POST | `/api/v1/loan-applications` | `lending.application.create` | Required key |
| POST | `/api/v1/loan-applications/{id}/submit` | `lending.application.submit` | Application/version |
| POST | `/api/v1/loan-applications/{id}/assessments` | `lending.application.assess` | Assessment key |
| POST | `/api/v1/loan-applications/{id}/decisions` | `lending.application.approve` | Decision key |
| POST | `/api/v1/loan-applications/{id}/accept-offer` | Applicant authority | Offer/version key |
| POST | `/api/v1/loan-applications/{id}/disburse` | `lending.disbursement.execute` | Required key |

Errors distinguish ineligible, evidence missing, screening hold, stale product/offer, authority exceeded, approval required and financial outcome uncertain.

## 8. Event contracts

Produced: `LoanProductPublished`, `LoanApplicationSubmitted`, `CreditAssessmentCompleted`, `LoanApplicationApproved`, `LoanOfferAccepted`, `LoanDisbursementRequested`, `LoanDisbursed`, `LoanDisbursementFailed`. Consumed: customer/KYC/screening/risk/document/workflow and ledger posting outcomes. Order by application/loan; inbox deduplication required.

## 9. Database design

No lending table or migration may be created until the ownership ADR is approved. The chosen owner gets schemas such as `lending`/`credit`, a dedicated migration assembly and principal. Candidate tables: product/rule versions, simulations, applications/versions, evidence references, assessments, decisions, offers, acceptances and disbursement instructions. Tenant IDs, explicit decimals/currency, effective dates, unique idempotency/business references and append-only decisions are mandatory. Ledger data is referenced, never copied as authority.

## 10. Consistency, concurrency and idempotency

Optimistic concurrency protects product/application/offer transitions. Local state/outbox is atomic; risk, workflow and ledger converge asynchronously. Disbursement key is scoped to institution/application/accepted-offer version. Uncertain posting is requeried, not resubmitted. No distributed transaction.

## 11. Security and privacy

Protect income, credit, guarantor, collateral, consent and assessment data. Enforce applicant/mandate authority, branch/customer scope, step-up and need-to-know. Test application tampering, assessor/approver conflict, authority bypass, evidence substitution, cross-tenant access and decision leakage.

## 12. Audit and observability

Audit product versions, simulations used, submitted values, evidence, assessments, explainable factors, decisions, offer versions, acceptance and ledger refs. Metrics: stage age, approval SLA, rejection/exception category, uncertain disbursement and provider failure. Never log raw bureau or income documents.

## 13. Failure, retry and recovery

Unavailable risk/provider leaves explicit pending state. Offer expiry blocks acceptance. Ledger timeout creates `DisbursementUncertain` and status enquiry. Failed event delivery retries from outbox. Restore/replay cannot duplicate applications, approvals or postings.

## 14. Configuration

Version/effective-date eligibility, interest method, fees, tenor, repayment frequency, collateral, affordability, approval authority, offer expiry and accounting mappings. All monetary/calculation policy needs approval.

## 15. Implementation slices

1. Approve lending ownership/calculation/accounting ADRs.
2. Product versions and deterministic simulation.
3. Application/evidence and permissions.
4. Assessment/risk and workflow approval.
5. Offer acceptance and idempotent disbursement.
6. Reconciliation, observability, runbooks and E2E evidence.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| LOR-01 | Product simulation | Reproducible separated schedule |
| LOR-02 | Missing consent/evidence | Submission blocked |
| LOR-03 | Maker self-approves | Denied/audited |
| LOR-04 | Authority exceeded | Higher approval required |
| LOR-05 | Stale offer accepted | Rejected |
| LOR-06 | Duplicate disbursement | One posting/outcome |
| LOR-07 | Ledger timeout | Uncertain plus enquiry |
| LOR-08 | Cross-tenant application | Denied |

## 17. Acceptance criteria

- [ ] Product versions and simulations satisfy approved calculation rules.
- [ ] Applications capture affordability, documents, guarantors/collateral and consent.
- [ ] Approval enforces maker-checker and authority thresholds.
- [ ] Disbursement occurs once through Core Banking and Ledger.

## 18. Definition of Done

Repository DoD plus approved ownership/accounting ADRs, golden calculations, migrations, permission matrix, provider contracts, posting/reconciliation evidence and operational runbook.

## 19. Open decisions

Owner/database, calculation/day-count/rounding, affordability, bureau semantics, risk authority, offer/acceptance, disbursement dates, fees/taxes and accounting mappings are open.

## 20. Prohibited assumptions

Do not create a tenth service, invent credit policy, trust client calculations, let risk/workflow post balances, or retry uncertain disbursement blindly.

## 21. Agent implementation instructions

Stop before persistence until the lending-boundary ADR is approved. Read prior customer/risk/workflow/ledger contracts. Run golden calculation, migration, authorisation, concurrency, idempotency, contract and posting tests; report actual evidence.


---
phase: "Phase 2 - Regulated Operations"
epic: "Risk, Fraud, AML and Sanctions"
owning_service: "Risk and Reporting"
modules: [Screening, Transaction Monitoring, Risk Scoring, Alerts, Investigations]
status: Draft
requirements: [RSK-001..RSK-008, API-005..API-007, SEC-009, SEC-013, AUD-001..AUD-004]
stories: [US-043, US-044, US-045, US-046]
dependencies: [Phase 1 Customer Identity Payments Workflow Audit, Epic 02 Corporate Party Onboarding]
adrs: [Risk decision contract, Screening/watchlist integration, Monitoring rule governance]
last_updated: 2026-10-16
---

# Epic 06: Risk, Fraud, AML and Sanctions

## 1. Objective

Screen customers/related parties, evaluate transaction and behavioral risk, score login/device activity, create prioritized alerts and manage role-separated investigations with explainable, auditable decisions.

## 2. Success measures

- Approved sanctions, PEP and watchlist sources/list versions produce canonical results; potential matches create review cases.
- Versioned rules evaluate amount, velocity, behavior, geography and counterparty signals reproducibly.
- Login/device decisions explicitly evaluate approved signals including device change, IP reputation, impossible travel and failed attempts, then allow, challenge, hold or block under approved policy.
- Alerts link customers, transactions, evidence, rule versions and investigations; closure requires disposition/rationale.

## 3. Scope

Included: watchlist/provider setup references, screening, monitoring rules, risk scores, signals, alert prioritization, evidence, investigation workflow and reporting events. Excluded: inventing legal thresholds/filings, opaque autonomous balance changes, Phase 5 predictive/model automation.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Rule manager | `risk.rule.manage` | Institution/rule family | Publish | Maker-checker |
| Screening reviewer | `risk.screening.review` | Assigned matches | Required | Policy based |
| AML/fraud analyst | `risk.alert.investigate` | Assigned queue/case | Evidence access | Separate roles where required |
| Investigation approver | `risk.case.close` | Disposition/authority | Required | Policy based |
| Service identity | `risk.decision.request` | Approved producer/purpose | Workload | None |

## 5. Domain model

Concepts: `RuleDefinition/Version`, `ScreeningRequest/Result`, `WatchlistSourceVersion`, `RiskSignal`, `RiskScore`, `RiskDecision`, `RiskAlert`, `Investigation`, `EvidenceReference`, `Disposition`.

States: screening `Requested -> Processing -> Clear/PotentialMatch/Error -> Reviewed`; alert `Open -> Assigned -> Investigating -> Escalated/Closed`; rule `Draft -> Simulated -> PendingApproval -> Active -> Retired/RolledBack`.

Invariants: a potential match never silently clears; decisions reference exact rules/lists/inputs; risk may recommend/hold/reject but never alter balances; closure needs disposition and rationale; evidence/notes are append-only; replay does not duplicate alerts/cases.

## 6. Use cases

- Screen individual/corporate parties during onboarding and periodically.
- Publish a rule after simulation/approval.
- Evaluate transaction/behavioral signals and create/deduplicate alerts.
- Score login/device signals and return explainable action.
- Assign, investigate, escalate and close an AML/fraud case.

## 7. API contracts

Routes: `POST /screenings`, `GET /screenings/{id}`, `POST /risk-decisions`, `POST /risk-rules/{id}/simulate`, `POST /risk-rules/{id}/publish`, `GET /risk-alerts`, `POST /risk-alerts/{id}/assign`, `POST /risk-investigations/{id}/decisions`. Internal decision APIs require authenticated service identity, purpose, tenant and correlation; external callers never receive hidden thresholds.

## 8. Event contracts

Consume customer/related-party, login/device, payment, ledger and channel facts. Produce `ScreeningCompleted`, `PotentialMatchDetected`, `RiskDecisionMade`, `RiskAlertCreated`, `RiskAlertEscalated`, `InvestigationClosed`. Partition by subject/transaction as required; inbox deduplication and late/out-of-order policy mandatory. Minimize PII.

## 9. Database design

Database `risk_reporting_db`; schemas `risk`, `screening`, `aml`, `fraud`, `integration`. Use architecture tables plus rule versions, signals, decisions, alert links, assignments, investigation notes/evidence/dispositions. Tenant IDs, unique request/event keys, effective dates, constrained decision states, immutable input/version references and optimistic concurrency are required. High-volume retention/partitioning needs approval.

## 10. Consistency, concurrency and idempotency

Rule/alert/investigation state plus outbox commit locally. Decisions use request idempotency and input digest. Alerts deduplicate by approved rule/subject/event scope without suppressing distinct suspicious activity. Provider screening uncertainty remains pending. No distributed transaction.

## 11. Security and privacy

Apply need-to-know, role separation, masked subjects, protected notes/evidence and sensitive-access audit. Secure provider credentials in Integration Hub. Defend rule/threshold disclosure, alert suppression, insider tampering, prompt/free-text injection and cross-tenant inference. Rate-limit searches and decision endpoints.

## 12. Audit and observability

Audit rule lifecycle, list/source version, request/result, explainable factors, overrides, assignments, notes, evidence access, dispositions and exports. Metrics: screening/rule latency, provider error, potential-match backlog, alert age/severity, investigation SLA and decision distribution. Alerts cover ingestion gaps and failed risk decisions without leaking case data.

## 13. Failure, retry and recovery

Provider timeout returns pending/error per policy, never clear. Rule engine failure applies approved fail-open/fail-closed behavior by journey; this must be explicit. Dead-letter signals are repaired idempotently. Restore verifies active rule versions, alert links and evidence integrity.

## 14. Configuration

Approve/effective-date providers, list refresh, match policy, rules, thresholds, velocity windows, score bands, action mapping, prioritization, SLA, escalation, disposition and retention. Never bury legal rules in code.

## 15. Implementation slices

1. Canonical risk/screening contracts and permissions.
2. Rule/version/simulation governance.
3. Screening adapters and review cases.
4. Transaction/login/device decisions and alert creation.
5. Investigations, evidence, disposition and reporting links.
6. Replay, performance, audit, dashboards and runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| RFA-01 | Screening clear | Versioned clear result |
| RFA-02 | Potential sanctions match | Review case; no silent approval |
| RFA-03 | Rule replay | One alert/decision per key |
| RFA-04 | Device/login anomaly | Approved challenge/hold/block factors |
| RFA-05 | Rule version changes | Historical decision reproducible |
| RFA-06 | Unauthorized case/evidence view | Denied/audited |
| RFA-07 | Investigator closes without disposition | Blocked |
| RFA-08 | Provider/engine unavailable | Approved explicit failure behavior |
| RFA-09 | Cross-tenant query | Non-disclosing denial |

## 17. Acceptance criteria

- [ ] Screening providers/lists are configurable and potential matches create review.
- [ ] Monitoring rules support required condition families with controlled versions.
- [ ] Device/login risk decisions retain explainable factors.
- [ ] Alerts/cases enforce role separation, SLA, evidence, disposition and approval.

## 18. Definition of Done

Repository DoD plus compliance-approved policies, provider contracts, rule simulation/golden tests, migrations, privacy/security review, replay/load evidence, dashboards and response runbooks.

## 19. Open decisions

Providers/lists, matching, refresh, AML/fraud rules, fail behavior, score/action bands, SLA, role separation, filing obligations, retention and evidence access remain open.

## 20. Prohibited assumptions

Do not invent thresholds or filing rules, auto-clear ambiguous matches, expose hidden rules, mutate balances, use unexplained scores for irreversible action, or treat provider availability as compliance evidence.

## 21. Agent implementation instructions

Read Phase 1 customer/payment/IAM/workflow/audit contracts and country policies. Keep adapters in Integration Hub and decisions in Risk and Reporting. Run rule, provider-contract, replay, security/privacy, migration, load and E2E tests. Stop for missing compliance policy.


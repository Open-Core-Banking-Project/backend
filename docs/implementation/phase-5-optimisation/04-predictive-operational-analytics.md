---
phase: "Phase 5 - Optimisation"
epic: "Predictive Operational Analytics"
owning_service: "Risk and Reporting, with Operations and Workflow owning human action"
modules: [Forecasting, Recommendations, Feature Governance, Shadow Evaluation, Outcome Measurement]
status: Draft
requirements: [ARC-001..ARC-006, RSK-004..RSK-007, RPT-003..RPT-007, DAT-001..DAT-008, SEC-001..SEC-018, AUD-001..AUD-004, NFR-OBS-001..NFR-OBS-002]
stories: []
dependencies: [Epic 01 Data Governance, Epic 03 Model Governance, Approved Operational Use Cases, Workflow, Observability]
adrs: [Predictive use cases and action boundaries, Feature and outcome definitions, Shadow and promotion policy]
last_updated: 2026-10-16
---

# Epic 04: Predictive Operational Analytics

## 1. Objective

Provide authorised operational teams with explainable, time-bounded forecasts and recommendations that improve measurable service outcomes while keeping final action in approved human or deterministic owning-service workflows.

## 2. Success measures

- Every enabled forecast/recommendation has approved target, horizon, owner, baseline, metric, guardrails and action boundary.
- Models prove value in offline and shadow evaluation before controlled operational exposure.
- Recommendations retain input/feature/model versions, uncertainty, explanation, expiry, reviewer action and measured outcome.
- No prediction directly changes balances, access, customer status, risk disposition, staffing or workflow priority without approved rules and authority.
- Drift, quality degradation or harm triggers fallback/suspension within approved detection and recovery targets.

## 3. Scope

Included: approved operational forecasting/recommendation use cases such as workload, queue/SLA, liquidity/capacity or failure-risk signals only after detailed approval; feature/outcome definitions; offline/shadow runs; explanations; review queues; feedback/outcome measurement; monitoring and rollback. Excluded: autonomous financial posting, customer eligibility/credit/AML decisions, unapproved employee surveillance, prescriptive action without review, and claims of causal improvement without approved evaluation.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Approval |
| --- | --- | --- | --- |
| Operations analyst | `prediction.view` | Institution/use case/queue | None |
| Operational reviewer | `prediction.action.record` | Assigned recommendation | Owning workflow policy |
| Model owner/validator | `prediction.model.manage` / `model.validate` | Use case/version | Independent validation |
| Operations approver | `prediction.use-case.publish` | Environment/action boundary | Maker-checker |
| Auditor/model risk | `prediction.evidence.view` | Run/decision/outcome | Read-only |

Recommendation visibility and action are separate. Identity and Access grants permissions; Risk and Reporting and Operations enforce use-case, institution, branch/queue, sensitivity and authority scopes.

## 5. Domain model

| Concept | Responsibility | Important rules |
| --- | --- | --- |
| `PredictiveUseCase` | Target, horizon, population and action boundary | Approved owner/risk tier/expiry |
| `FeatureSetVersion` | Governed feature definitions and source lineage | Point-in-time correct; no leakage |
| `OutcomeDefinition` | Observable target and measurement window | Stable/versioned ground truth |
| `ForecastRun` | Fixed as-of/model/feature snapshot | Reproducible and idempotent |
| `Prediction/Recommendation` | Score/range/confidence/explanation/expiry | Not a business decision |
| `OperationalReview` | Human accept/reject/defer/action/reason | Authority verified |
| `OutcomeObservation` | Actual result and guardrail effects | No retroactive label rewrite |

Invariants: prediction time precedes outcome window; tenant/population scope is fixed; features use only approved point-in-time data; scores retain uncertainty and model version; expired/stale prediction cannot drive action; outcome/evaluation cannot be selectively deleted to improve metrics.

## 6. Use cases

### UC-01 - Define and approve predictive use case

Specify problem, baseline, target/outcome, population, exclusions, features, horizon, metrics, guardrails, human workflow and rollback; validate and approve.

### UC-02 - Run offline or shadow forecast

Freeze features at an as-of watermark, score approved model, store explanation/uncertainty and withhold recommendations from action in shadow mode.

### UC-03 - Review recommendation

Present authorised user with provenance, uncertainty, explanation and expiry; record decision/reason and invoke only an approved owning-workflow command.

### UC-04 - Measure and monitor outcomes

Join approved delayed outcomes without label leakage, compare baseline/model/guardrails and trigger review, rollback or suspension.

## 7. API contracts

| Method | Route | Permission | Idempotent |
| --- | --- | --- | --- |
| POST | `/api/v1/predictions/use-cases` | `prediction.use-case.manage` | Request key |
| POST | `/api/v1/predictions/runs` | `prediction.run.execute` | Use-case/model/as-of key |
| GET | `/api/v1/predictions/recommendations` | `prediction.view` | Yes |
| POST | `/api/v1/predictions/recommendations/{id}/reviews` | `prediction.action.record` | Reviewer/action key |
| POST | `/api/v1/predictions/outcomes/observations` | `prediction.outcome.record` | Source/outcome key |
| POST | `/api/v1/predictions/deployments/{id}/suspend` | `model.suspend` | Action key |

Responses expose status, valid horizon, uncertainty, explanation summary, source/model versions and permitted next action. Stable errors cover stale/missing features, unapproved model, out-of-population subject, expired recommendation, authority, duplicate outcome and guardrail suspension.

## 8. Event contracts

Produce `ForecastRunCompleted/Failed`, `OperationalRecommendationCreated/Expired/Reviewed`, `PredictionOutcomeObserved`, `PredictiveGuardrailBreached`. Consume governed feature facts, workflow/queue outcomes, observability incidents and model deployment state. Events minimise subject data, identify purpose/use case/version and deduplicate by run/recommendation/outcome.

## 9. Database design

Risk and Reporting owns use cases, target/outcome versions, feature-set references, model/deployment references, run/checkpoints, predictions/recommendations, explanations, shadow comparisons, evaluations and guardrail incidents. Operations owns review/action workflow and authoritative operational outcome. Store local references and event-fed outcomes, never cross-service foreign keys. Partition high-volume predictions; enforce tenant/use-case/as-of/subject/version uniqueness, expiry, immutable scored values and append-only observations/corrections.

## 10. Consistency, concurrency and idempotency

Run/checkpoint/prediction/outbox commit locally in bounded batches. Fixed feature watermark enables restart/reproduction. Concurrent identical runs converge on one run; reviews use one active decision/version and duplicate action keys return prior outcome. Operational action occurs in its owning service and is correlated asynchronously; prediction never claims action success before confirmation. No distributed transaction.

## 11. Security and privacy

Approve purpose/population/feature necessity; prohibit hidden protected/sensitive proxies unless explicitly reviewed. Enforce tenant/use-case/subject/queue scope, minimum display, export controls and anti-enumeration. Threat-model feature poisoning, inference, score manipulation, model extraction, feedback gaming and employee/customer surveillance. Test cross-tenant features, stale entitlement, explanation leakage and unauthorised bulk scoring.

## 12. Audit and observability

Audit use-case/feature/outcome/model versions, validation/promotion, run, recommendation visibility, review/reason, workflow action, override, outcome and suspension. Metrics cover source/feature freshness, score distribution, coverage, accuracy/calibration where relevant, drift, false impacts, review/acceptance, outcome lift, guardrails, latency, cost and queue effects. Monitor by approved cohorts without leaking identities.

## 13. Failure, retry and recovery

Missing/stale/invalid features produce no actionable recommendation. Failed scoring resumes deterministically; partial run is not published. Expired model/deployment suspends scoring. Outcome delay remains unknown rather than labelled failure/success. Workflow action timeout follows owning-service status/reconciliation. Restore verifies runs, feature/model references, recommendation/review/outcome links and incidents before resuming.

## 14. Configuration

Version/approve population/exclusions, target/outcome/horizon, feature set, model/deployment, run schedule, score thresholds, uncertainty/explanation, expiry, shadow/canary share, human workflow, action allow-list, metrics/guardrails, drift, retention and suspension/rollback. Threshold changes require evaluation and approval.

## 15. Implementation slices

1. Baseline detailed use case, outcome, action and governance decisions.
2. Point-in-time feature/outcome contracts with leakage and tenant tests.
3. Offline runner, baselines and reproducible evaluation.
4. Shadow deployment, explanations, guardrails and dashboards.
5. Controlled recommendation review and owning-workflow integration.
6. Outcome measurement, promotion/rollback, resilience and acceptance evidence.

## 16. Test plan

| ID | Scenario | Expected evidence |
| --- | --- | --- |
| POA-01 | Point-in-time feature build | No future/outcome leakage |
| POA-02 | Duplicate/restarted run | Reproducible single prediction set |
| POA-03 | Shadow mode | No operational action available |
| POA-04 | Recommendation review | Provenance/explanation/expiry and authority shown |
| POA-05 | Direct action attempt | Denied; owning workflow required |
| POA-06 | Stale/missing/cross-tenant feature | No actionable score; alert/denial |
| POA-07 | Delayed/corrected outcome | Append-only versioned evaluation |
| POA-08 | Drift/guardrail breach | Suspend/fallback/rollback |
| POA-09 | Cohort/privacy analysis | Approved policy and masking enforced |
| POA-10 | Restore/replay | Runs/reviews/outcomes remain linked once |

## 17. Acceptance criteria

- [ ] Detailed predictive use cases, baselines, targets, actions and measurable acceptance criteria are approved.
- [ ] Features/outcomes are governed, point-in-time correct, tenant-isolated and lineage-complete.
- [ ] Offline and shadow evidence demonstrates approved value and guardrails before action exposure.
- [ ] Recommendations are explainable, uncertain where appropriate, expiring and human/workflow controlled.
- [ ] Outcome monitoring, drift response, suspension, rollback and recovery are verified.

## 18. Definition of Done

Repository DoD plus approved predictive/action/model ADRs, feature/outcome contracts, leakage/golden tests, independent validation, shadow evaluation, security/privacy/fairness evidence, controlled workflow integration, rollback/restore rehearsal, dashboards/runbooks and Operations/Model Risk/Compliance approval.

## 19. Open decisions

Stories/use cases, target populations, outcomes/horizons, baselines, feature sources, models, explanations, thresholds, human owners/action allow-list, evaluation design, fairness/guardrails, shadow/canary duration, freshness/volume/performance/cost, retention and recovery targets remain open.

## 20. Prohibited assumptions

Do not use future data, infer causality from correlation, optimise an unapproved proxy, suppress adverse outcomes, score outside population/purpose, expose raw sensitive features, act on stale predictions, automate high-impact decisions or let predictions mutate authoritative state.

## 21. Agent implementation instructions

Read Phase 5 overview, Epics 01 and 03, affected operational/domain epics, observability and approved use-case/model ADRs. Use C#/.NET and event/API contracts; do not read source databases. Do not implement production action before detailed requirements and shadow approval. Run build, migration, feature leakage, reproducibility, contract, tenant/privacy, model/evaluation, performance, workflow, resilience, rollback and recovery tests. Stop on unresolved outcome, action owner or guardrail.


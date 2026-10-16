---
phase: "Phase 5 - Optimisation"
status: Draft
owning_services:
  - Risk and Reporting
  - Channels and Engagement
  - Core Banking and Ledger
  - Operations and Workflow
  - Audit and Reconciliation
requirements:
  - ARC-001..ARC-006
  - RSK-004..RSK-007
  - RPT-003..RPT-007
  - REP-001..REP-003
  - REG-002
  - DAT-001..DAT-008
  - SEC-001..SEC-018
  - AUD-001..AUD-004
  - NFR-PERF-001..NFR-PERF-002
  - NFR-SCAL-001
  - NFR-AVAIL-001
  - NFR-RES-001..NFR-RES-002
  - NFR-DR-001..NFR-DR-002
  - NFR-OBS-001..NFR-OBS-002
stories: [US-066]
dependencies:
  - Phases 0 through 4 verified for every enabled source domain
  - Approved analytical-store, data-governance and model-governance decisions
last_updated: 2026-10-16
---

# Phase 5 - Optimisation

## Objective

Deliver governed data, profitability insight, safely constrained AI-assisted service and predictive operational analytics without turning analytical systems or models into banking sources of truth or autonomous financial decision makers.

Phase 5 exit emphasis is model governance, measurable efficiency and explainability. Optimisation must be measured against an approved baseline and must preserve security, tenant isolation, data ownership, financial integrity, human accountability and recoverability.

## Requirements-baselining notice

The supplied sources allocate only US-066 directly to Phase 5. The SRS names profitability, enterprise data governance, AI-assisted service and predictive analytics at roadmap level, but it does not provide dedicated user stories or complete acceptance criteria for profitability, AI-assisted service or predictive operations. Therefore:

- US-066 and RPT-006/RPT-007 define the committed minimum data-governance outcome.
- Existing reporting, risk-model, data, security, audit and non-functional requirements apply where relevant.
- Epics 2 through 4 are planning contracts, not approval to deploy models or automate decisions.
- Product, Finance, Data Governance, Model Risk, Compliance, Privacy, Security and Operations must baseline detailed requirements before those epics can become `Approved`.
- Implementers must not invent profit-allocation rules, training data, model objectives, protected-attribute treatment, customer advice, automated actions or performance targets.

## Entry criteria

- Enabled prior-phase source services, contracts, reconciliations and operational controls are verified.
- The analytical-store architecture, ingestion pattern, tenant isolation, retention, recovery and source-watermark strategy are approved.
- Data ownership, stewardship, classification, glossary, quality and lineage governance are approved.
- Finance approves profitability definitions, dimensions, allocations, source hierarchy and reconciliation tolerances before Epic 2.
- Model Risk and Compliance approve model inventory, validation, explainability, drift, change, rollback and human-oversight policy before Epics 3 or 4.
- Privacy, consent, purpose limitation, data minimisation and lawful-use decisions are recorded for each dataset and use case.
- Production baselines and measurable target outcomes exist before claiming optimisation.

## Epic map

| Order | Epic | Primary owner | Stories | Outcome |
| --- | --- | --- | --- | --- |
| 1 | Enterprise Data Governance, Lineage and Ownership | Risk and Reporting | US-066 | Governed catalogue of critical data elements, owners, glossary, classifications, quality rules and report-field lineage |
| 2 | Profitability and Management Analytics | Risk and Reporting with Finance and Ledger | None allocated | Reconciled, reproducible profitability views based on approved definitions and sources |
| 3 | AI-Assisted Service and Model Governance | Channels and Engagement with Risk and Reporting | None allocated | Human-controlled assistance backed by approved knowledge and governed model lifecycle |
| 4 | Predictive Operational Analytics | Risk and Reporting with Operations | None allocated | Explainable forecasts and recommendations measured in shadow mode before controlled use |

## Dependency map

```text
Verified source domains and approved analytical architecture
    |
    +--> 01 Enterprise Data Governance, Lineage and Ownership
    |        establishes catalogue, classification, ownership, quality and lineage
    |
    +--> 02 Profitability and Management Analytics
    |        requires governed finance sources and approved allocation methodology
    |
    +--> 03 AI-Assisted Service and Model Governance
    |        requires approved content, privacy, model and human-handoff controls
    |
    +--> 04 Predictive Operational Analytics
             requires governed features, outcomes, baselines and operational owners
```

Epic 1 is foundational. Epics 2 through 4 may explore contracts in parallel only after their source datasets are registered and classified. No model may consume an unregistered source or produce an operational action outside its owning service contract.

## Service ownership boundaries

### Risk and Reporting

- Own the data catalogue, business glossary, lineage/quality metadata, governed analytical read models, model registry and approved analytical outputs.
- Own analytical calculations only under approved definitions; never become the authoritative source for balances, transactions, customers, permissions or workflow decisions.
- Preserve institution separation in ingestion, storage, computation, export and deletion/retention workflows.

### Core Banking and Ledger

- Remains authoritative for balances, journals, accounting dimensions, periods and approved financial facts.
- Supplies versioned snapshots/events and reconciliation evidence; profitability output cannot post or adjust the ledger.
- Owns any financial correction triggered after an analytical discrepancy is investigated and approved.

### Channels and Engagement

- Owns AI-assisted conversation orchestration, approved knowledge retrieval, disclosure, consent-aware interaction and human handoff.
- May invoke only allow-listed, authorised application APIs; generated text never becomes a privileged command or final financial advice/decision.

### Operations and Workflow

- Owns review tasks, exception handling, human approval, escalation and operational action workflows.
- Predictive recommendations create controlled work, not direct financial, customer, risk or access changes.

### Audit and Reconciliation

- Retains trace evidence for data versions, lineage, report/model runs, approvals, recommendations, human actions and reconciliations.
- Verifies analytical outputs against authoritative sources without allowing analytics to rewrite them.

### Identity and Access and Integration Hub

- Identity and Access owns permission grants, service identities and entitlement freshness; every consuming service enforces resource scopes.
- Integration Hub owns approved external data/model provider adapters, credential references, canonical contracts, provider evidence and health.

## Cross-service contracts

- Data asset, critical data element, business term, owner/steward, classification, purpose and retention reference.
- Source system/object/field, transformation step, dataset/version, report/field, lineage edge and evidence digest.
- Data-quality rule/version, execution, result, exception, waiver and remediation reference.
- Analytical snapshot, source watermark, calculation policy/version, reconciliation state and publication status.
- Profitability allocation rule/version, dimension, driver, component, result and source/accounting reference.
- Model/use-case registration, dataset/feature version, training/validation evidence, approval, deployment, decision/recommendation and rollback.
- AI interaction, approved knowledge citations, safety decision, human handoff and feedback without unrestricted prompts or secrets.
- Forecast/recommendation, confidence/uncertainty, explanation, expiry, operational decision and measured outcome.

Contracts contain explicit institution, source, version, as-of time, business date where relevant, correlation, causation and data classification.

## Data-governance gates

- Critical data elements have approved definitions, accountable owners, stewards, classifications and permitted purposes.
- PII and financial data are tagged; access, export, retention, deletion and legal-hold policy is enforceable.
- Every published analytical/report field traces through versioned transformations to approved source fields and source snapshots.
- Quality results never silently alter source data; remediation occurs in the owning source service through controlled processes.
- Lineage metadata is append-only/versioned where evidence requires; changes preserve prior published report/model reproducibility.
- Data contracts detect schema drift, missing fields, stale sources, duplicates and invalid tenant mixing before publication.

## Profitability and financial-integrity gates

- Finance approves revenue, cost, fee, interest, funding, capital, tax and allocation definitions before use.
- Results retain original currency, reporting currency, approved rate/version, period, accounting source and rounding method.
- Allocation totals reconcile to approved inputs within explicit tolerances; residuals and exceptions remain visible.
- Restatement creates a new version and preserves prior published results, source watermarks and rationale.
- Profitability outputs are analytical and cannot change pricing, fees, balances, limits or ledger entries directly.
- Access to customer/account profitability is purpose-limited, scoped, masked and audited.

## AI and model-governance gates

- Every model/use case has an accountable owner, purpose, allowed inputs/outputs/actions, risk tier, validation, approval and expiry/review date.
- Training, evaluation and production datasets are versioned, classified, authorised and protected against cross-tenant leakage.
- Evaluation covers accuracy, calibration where applicable, false positive/negative impact, robustness, privacy, security, fairness and explainability under approved policy.
- High-impact or customer-affecting decisions require approved human oversight and appeal/review paths; a model cannot approve its own exception.
- Prompt injection, data exfiltration, unsafe tool use, hallucinated policy and adversarial inputs are tested for AI-assisted service.
- Drift, quality, latency, cost and outcome monitoring trigger alert, fallback, rollback or suspension under approved thresholds.
- Model/provider timeout or ambiguity produces an explicit unavailable/uncertain outcome; it does not justify an invented answer or autonomous action.

## Security and privacy gates

- Permissions are capability-based and scoped by institution, dataset, classification, purpose, report/model, environment and export authority.
- Privileged catalogue changes, quality waivers, profitability publication, model approval/deployment and override require step-up and maker-checker where configured.
- Secrets, raw prompts/responses, features, documents and analytical exports follow classification-specific storage/logging controls.
- Production data is prohibited from development/training unless formally approved, minimised and irreversibly protected under policy.
- Membership inference, model extraction, poisoning, prompt injection, data exfiltration and excessive export are threat-modelled.
- Deny access and disable publication/inference when entitlement, lineage, data-purpose or model approval cannot be proven.

## Operational and measurement gates

- Each optimisation has a pre-change baseline, target metric, guardrail metrics, measurement window and accountable owner.
- Metrics distinguish business improvement from model/service latency, errors, saturation, cost, data quality and human workload.
- Models begin in offline or shadow mode; promotion requires approved evidence and rollback rehearsal.
- Pipelines and jobs use durable checkpoints, bounded replay and reproducible source watermarks.
- Dashboards identify stale datasets, lineage gaps, quality failures, reconciliation exceptions, drift, unsafe responses and recommendation aging.
- Backup/restore verifies catalogue, transformations, approved datasets, model artifacts/metadata, reports, decisions and audit evidence.

## Phase-level test journeys

1. A critical report field traces to its approved source fields and exact transformation/version.
2. Cross-tenant lineage, dataset query, export and model input attempts are denied without disclosure.
3. PII/financial classification drives masking, access approval, retention and audit.
4. Failed quality rule blocks publication while preserving source data and creating remediation evidence.
5. Profitability calculation reconciles components and allocations to approved finance inputs.
6. Profitability restatement preserves prior version and explains source/rule changes.
7. Unapproved dataset, feature or model version cannot train, deploy or serve.
8. AI-assisted service answers only from approved knowledge, cites evidence and hands off when confidence/policy requires.
9. Prompt injection or tool-manipulation attempt cannot expose data or execute a privileged command.
10. Predictive model runs in shadow mode, records explanations/outcomes and cannot act autonomously.
11. Drift or guardrail breach suspends/falls back according to approved policy and alerts the owner.
12. Restore/replay reproduces an analytical result without duplicating events, decisions or operational work.

## Phase exit criteria

- [ ] US-066 traces through governance decisions, contracts, metadata, code, tests and operational evidence.
- [ ] Every enabled roadmap epic has approved detailed requirements and measurable acceptance criteria.
- [ ] Critical data ownership, glossary, classification, quality and field-level lineage are operational.
- [ ] Profitability outputs reconcile to approved sources and allocation rules and remain non-authoritative.
- [ ] Enabled AI/model use cases have registry, validation, explainability, human oversight, monitoring and rollback evidence.
- [ ] Improvements meet approved targets without breaching financial, security, privacy, fairness or service guardrails.
- [ ] No open severity-1/2 financial-integrity, privacy, tenant-isolation, model-risk or security defects remain.
- [ ] Data Governance, Finance, Model Risk, Compliance, Privacy, Security, Operations and UAT approvals are complete.

## Open decisions

| Decision | Epic | Owner |
| --- | --- | --- |
| Analytical platform, storage, compute and tenant-isolation design | All | Architecture, Data Platform, Security |
| Catalogue/lineage metadata model and ownership workflow | 1 | Data Governance, Architecture |
| Critical data elements, classifications, purposes, retention and quality rules | 1 | Data Owners, Privacy, Compliance |
| Profitability dimensions, allocations, rates, reconciliation and restatement | 2 | Finance, Product, Ledger |
| Approved AI-assisted journeys, disclosures, knowledge sources and handoff | 3 | Product, Service, Compliance |
| Model inventory, risk tiers, validation, explainability and approval lifecycle | 3/4 | Model Risk, Compliance, Data Science |
| Predictive use cases, target outcomes, operational owners and action boundaries | 4 | Operations, Product, Risk |
| Model/AI hosting, providers, data residency, security and exit strategy | 3/4 | Architecture, Security, Procurement |
| Performance, scale, cost, retention, availability and recovery targets | All | Platform, Operations, Business Owners |

## Prohibited assumptions

- Do not infer profit definitions, allocation drivers, transfer-pricing methods, targets or accounting treatment.
- Do not train on or expose production/PII/financial data without approved purpose, access and protection.
- Do not let reports, features, vectors, models or caches become balance or transaction sources of truth.
- Do not let generated text or predictions execute privileged, financial, risk, customer, access or workflow decisions directly.
- Do not represent correlation as causation, a prediction as fact, or a model score as an approved business decision.
- Do not hide model/data limitations, uncertainty, overrides, quality failures or adverse outcomes.
- Do not introduce a new backend language or deployable microservice without approved architecture change.

## Epic implementation documents

1. [Enterprise Data Governance, Lineage and Ownership](01-enterprise-data-governance-lineage-and-ownership.md)
2. [Profitability and Management Analytics](02-profitability-and-management-analytics.md)
3. [AI-Assisted Service and Model Governance](03-ai-assisted-service-and-model-governance.md)
4. [Predictive Operational Analytics](04-predictive-operational-analytics.md)

## Agent instructions

- Read root/service `AGENTS.md`, this overview, the target epic, source requirements/stories and approved ADRs before editing.
- Confirm an epic is `Approved`; Draft status permits documentation, prototypes with synthetic data and decision work only.
- Use C#/.NET for all production APIs, workers, pipelines, inference orchestration and business logic.
- Keep analytical and model stores behind Risk and Reporting ownership; use contracts rather than source-database access.
- Start with contracts and failing acceptance tests; implement the smallest safe vertical slice.
- Stop when data purpose, ownership, finance method, model action, human oversight, provider terms or production targets are unresolved.
- Report actual build/test/migration/model-evaluation evidence and never describe a roadmap capability as implemented.


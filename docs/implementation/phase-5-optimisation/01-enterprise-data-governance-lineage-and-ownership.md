---
phase: "Phase 5 - Optimisation"
epic: "Enterprise Data Governance, Lineage and Ownership"
owning_service: "Risk and Reporting"
modules: [Data Catalogue, Business Glossary, Classification, Ownership, Data Quality, Lineage]
status: Draft
requirements: [RPT-004..RPT-007, REG-002, DAT-001..DAT-008, SEC-001..SEC-018, AUD-001..AUD-004]
stories: [US-066]
dependencies: [Approved Analytical Architecture, Source Data Contracts, IAM, Audit, Reporting]
adrs: [Analytical platform and tenant isolation, Metadata and lineage model, Data access and purpose governance]
last_updated: 2026-10-16
---

# Epic 01: Enterprise Data Governance, Lineage and Ownership

## 1. Objective

Give data stewards an authoritative governance catalogue in which critical data elements have approved definitions, owners, classifications and quality rules, and every published report field can be traced to approved source data and transformations.

## 2. Success measures

- All in-scope critical data elements have accountable owner, steward, definition, classification, purpose and review date.
- PII and financial fields enforce classification-driven access, masking, export, retention and audit policy.
- Published report/regulatory fields expose reproducible source-to-output lineage and exact version/as-of evidence.
- Quality failures block or label publication according to approved severity and create owned remediation work.
- Cross-tenant and unauthorised metadata/data access tests pass; restore reproduces catalogue and lineage evidence.

## 3. Scope

Included: data assets, critical elements, glossary, ownership/stewardship, classifications, approved purposes, quality-rule versions/results, source contracts, dataset/report-field lineage, change review, access requests and evidence. Excluded: direct source-data editing, universal master-data management, automatic legal classification, unsupported discovery across private databases and model-specific governance beyond foundational dataset/feature metadata.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Approval |
| --- | --- | --- | --- |
| Data steward | `data.catalogue.manage` | Institution/domain/asset | Owner approval for publication |
| Data owner | `data.governance.approve` | Owned domain/elements | Maker-checker |
| Quality analyst | `data.quality.execute` | Dataset/rule | Waiver independently approved |
| Report developer | `data.lineage.register` | Report/dataset | Publication review |
| Auditor/compliance | `data.governance.view` | Approved evidence scope | Read-only |

Identity and Access owns grants; Risk and Reporting enforces resource, classification, purpose, environment and export scope. Makers cannot approve their own catalogue publication, quality waiver or access exception.

## 5. Domain model

| Concept | Responsibility | Important rules |
| --- | --- | --- |
| `DataAsset` / `DataElement` | Governed dataset/object/field identity | Stable identity; exact source owner |
| `BusinessTerm` | Approved meaning and usage | Versioned; conflicts resolved visibly |
| `OwnershipAssignment` | Owner, steward and review period | Effective-dated; no orphan critical element |
| `ClassificationAssignment` | Sensitivity and permitted purposes | Evidence and approver retained |
| `LineageGraph` | Source, transformation and output edges | Versioned, acyclic where required, reproducible |
| `QualityRule/Execution` | Test definition and result | Source unchanged; waiver explicit |

Invariants: every tenant-owned node carries institution scope; lineage references contracts rather than private-table access; published versions are immutable; corrections create successors; source services retain data authority; report fields cannot be `Trusted` with unresolved critical lineage/quality failure.

## 6. Use cases

### UC-01 - Register and publish governed metadata

Create draft asset/elements/terms, assign owner/classification/purpose, review conflicts and publish an effective-dated version.

### UC-02 - Capture and query lineage

Register source-to-transformation-to-report edges with version/digest, validate completeness and answer authorised impact/source queries.

### UC-03 - Evaluate data quality

Execute approved rule at a fixed watermark, retain aggregate/minimised evidence, open remediation and block/label publication by policy.

### UC-04 - Review change or exception

Assess schema/meaning/classification/owner change or quality waiver, require independent decision and preserve the prior version.

## 7. API contracts

| Method | Route | Permission | Idempotent |
| --- | --- | --- | --- |
| POST | `/api/v1/data-governance/assets` | `data.catalogue.manage` | Request key |
| POST | `/api/v1/data-governance/assets/{id}/versions` | `data.catalogue.manage` | Version key |
| POST | `/api/v1/data-governance/versions/{id}/decisions` | `data.governance.approve` | Decision key |
| POST | `/api/v1/data-governance/lineage` | `data.lineage.register` | Edge/version key |
| GET | `/api/v1/data-governance/lineage` | `data.lineage.view` | Yes |
| POST | `/api/v1/data-governance/quality-runs` | `data.quality.execute` | Rule/watermark key |

Queries are bounded and mask restricted physical details. Stable errors cover ownership gap, classification conflict, invalid/cyclic lineage, stale contract, quality failure, self-approval, purpose denial and version conflict.

## 8. Event contracts

Produce `DataAssetVersionPublished`, `DataClassificationChanged`, `LineageVersionPublished`, `DataQualityRunCompleted`, `DataQualityExceptionOpened/Resolved`. Consume approved source schema/contract, report publication, identity entitlement and retention/legal-hold facts. Use outbox/inbox, schema versions, dataset/lineage keys, minimum metadata and no row-level PII.

## 9. Database design

Risk and Reporting owns catalogue schemas/tables for assets, elements, terms, definitions, ownership assignments, classifications, purposes, lineage nodes/edges/versions, quality rule versions, executions, exceptions, access decisions and publication evidence. Constraints enforce tenant/stable-name/version uniqueness, required owner/classification for critical elements, immutable published versions, optimistic draft transitions and indexed impact/source traversal. Store references/digests, not copied secrets or unrestricted samples. No cross-service foreign keys.

## 10. Consistency, concurrency and idempotency

Draft/version/decision/outbox commit locally. Publication binds a digest of elements, terms, ownership, classification and lineage. Concurrent edits use optimistic versions; only one version is active for an effective interval. Registration and quality-run keys deduplicate retries. Cross-service schema state is eventually consistent and stale evidence prevents `Trusted` publication; no distributed transaction.

## 11. Security and privacy

Metadata may expose sensitive structure even without values. Enforce institution/domain/classification/purpose scope, field-name masking where needed, bounded graph traversal, export controls, rate limits and step-up for bulk/privileged access. Test enumeration, lineage injection, cross-tenant edges, owner spoofing, waiver abuse, formula/script injection and sensitive sample leakage.

## 12. Audit and observability

Audit creation/change/publication, owner/classification/purpose decisions, lineage evidence, quality execution, waiver, access/export and break-glass use. Metrics include catalogue coverage, orphan/stale ownership, lineage completeness, quality pass/fail age, unresolved exceptions, schema drift and access denial. Traces link source contract, pipeline, dataset and report without row contents.

## 13. Failure, retry and recovery

Invalid or incomplete metadata remains Draft. Stale/missing source contract blocks trusted publication. Quality jobs restart from fixed watermark/checkpoint and deduplicate results. A failed lineage traversal returns explicit incomplete status. Restore verifies version digests, active intervals, graph integrity, open exceptions and audit links before publication resumes.

## 14. Configuration

Version and approve critical-element criteria, classification taxonomy, allowed purposes, review intervals, lineage completeness rules, quality severity/blocking, waiver authority, graph/query limits, retention and notification/escalation. Configuration never grants data access by itself.

## 15. Implementation slices

1. Approve metadata/tenant architecture and source-contract conventions.
2. Catalogue, glossary, ownership and classification with permissions.
3. Versioned lineage registration and impact/source queries.
4. Quality rules, fixed-watermark executions and remediation workflow.
5. Report/regulatory publication gates and access/export controls.
6. Drift detection, audit, dashboards, restore and acceptance evidence.

## 16. Test plan

| ID | Scenario | Expected evidence |
| --- | --- | --- |
| DGV-01 | Register critical element | Owner, definition, classification and purpose required |
| DGV-02 | Trace report field | Exact source/transformation/version returned |
| DGV-03 | Schema change | Impact identified; stale lineage blocks trust |
| DGV-04 | Critical quality failure | Publication blocked; remediation opened |
| DGV-05 | Waiver/self-approval | Independent authority enforced |
| DGV-06 | PII/financial access/export | Purpose/scope/masking/audit enforced |
| DGV-07 | Cross-tenant edge/query | Non-disclosing denial |
| DGV-08 | Duplicate/replayed registration | One logical version/result |
| DGV-09 | Concurrent publication | One active effective version |
| DGV-10 | Restore | Catalogue/lineage/quality evidence verifies |

## 17. Acceptance criteria

- [ ] Critical data elements have approved owners and definitions.
- [ ] PII and financial data are classified and protection policy is enforceable.
- [ ] Report fields trace to approved sources and transformations.
- [ ] Quality, ownership and lineage changes are versioned, approved and audited.
- [ ] US-066 and RPT-006/RPT-007 have automated and operational evidence.

## 18. Definition of Done

Repository DoD plus approved platform/metadata/access ADRs, migrations, source/report contracts, tenant/privacy/security tests, lineage and quality acceptance tests, recovery rehearsal, dashboards/runbooks and Data Governance/Privacy/Compliance approval.

## 19. Open decisions

Catalogue technology, graph representation, discovery approach, critical elements, taxonomies, purposes, owner/steward workflow, physical-detail exposure, lineage granularity, quality rules/tolerances, publication gates, retention, scale and recovery targets remain open.

## 20. Prohibited assumptions

Do not crawl private databases without owner approval, infer ownership/classification from names, include row samples by default, edit source data from quality jobs, mark incomplete lineage trusted, merge tenant graphs, or treat the catalogue as authority for source values.

## 21. Agent implementation instructions

Read the Phase 5 overview, reporting/regulatory/document epics, architecture, source contracts and approved governance ADRs. Change only Risk and Reporting governance modules plus deliberate contracts/consumers. Use C#/.NET. Run build, migration, contract, graph, concurrency, tenant/privacy, security, quality, performance and restore tests. Stop before persistence if metadata ownership, tenant isolation or access purpose is unresolved.


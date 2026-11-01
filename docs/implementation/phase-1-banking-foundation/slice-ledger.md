---
phase: "Phase 1 - Banking Foundation"
artifact: Slice and decision ledger
status: Active
approval_model: Human approves each slice
snapshot: 2026-10-28
source_documents: 01..12 epic implementation documents
---

# Phase 1 slice ledger

Tracks every implementation slice in Phase 1, the status claimed for it, the human approval it needs and the open decisions that block it.
The epic documents define scope; this ledger is the single place that records execution and approval state.

## Status and approval model

An agent may move a slice to `in progress` or `implemented`. Only a named human reviewer may set `approved`.
A slice is never auto-approved, and an epic never advances because its slices were merely marked implemented.

| Status | Meaning | Who sets it |
| --- | --- | --- |
| `blocked` | The slice is an approval or a decision that only a human owner can complete | Human |
| `not started` | No implementation claim recorded | Agent |
| `in progress` | Partial implementation recorded, remaining work named | Agent |
| `implemented` | Implementation claimed with evidence, awaiting review | Agent |

| Approval | Meaning |
| --- | --- |
| empty | No implementation to review |
| `pending` | Implemented or partly implemented, review not done |
| `approved <date>` | A human reviewer accepted the slice and its evidence on that date |

A decision id in a slice row means the epic document still carries an unresolved decision that applies to that slice;
it is not proof the slice is unimplemented, and an implemented slice can still be gated for production use by one.

## Snapshot

- Slices: **176** across **12** epics.
- Implemented and awaiting review: **14**.
- Approved by a human reviewer: **26** (P1-E01 slices 2-12 on 2026-10-28; P1-E03 slices 1-15 on 2026-10-31).
- In progress: **30**. Not started: **108**. Blocked approvals: **10**.
- Decisions: **188** total — 134 open, 4 partially approved, 19 with no recorded status, 31 resolved.
- Slices that may start right now without a new human decision: **0**.

Statuses are transcribed from the epic documents on 2026-10-28. Verification is recorded separately in the
*Verification runs* section; a slice without a verification entry has not been re-checked.

## Epic summary

| Epic | Document status | Slices | Approved | Implemented | In progress | Blocked | Not started | Unresolved decisions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P1-E01 Institution and Tenancy | Implemented | 12 | 11 | 11 | 0 | 1 | 0 | 9 |
| P1-E02 Identity, Roles and Permissions | In Progress | 15 | 0 | 7 | 8 | 0 | 0 | 3 |
| P1-E03 Customer and Onboarding | Implemented | 15 | 15 | 3 | 12 | 0 | 0 | 0 |
| P1-E04 Products and Accounts | Draft | 15 | 0 | 3 | 2 | 1 | 9 | 13 |
| P1-E05 Core Ledger and Financial Posting | Draft | 15 | 0 | 0 | 0 | 1 | 14 | 17 |
| P1-E06 Workflow and Maker-Checker | Draft | 13 | 0 | 4 | 0 | 1 | 8 | 12 |
| P1-E07 Payments, Collections and VTU | Draft | 15 | 0 | 0 | 0 | 1 | 14 | 17 |
| P1-E08 Integration Hub and Providers | Draft | 15 | 0 | 0 | 3 | 1 | 11 | 16 |
| P1-E09 Channels, CMS and Communications | Draft | 17 | 0 | 0 | 1 | 1 | 15 | 19 |
| P1-E10 Documents and Statements | Draft | 15 | 0 | 0 | 1 | 1 | 13 | 19 |
| P1-E11 Audit, Traceability and Recovery | Draft | 15 | 0 | 0 | 1 | 1 | 13 | 17 |
| P1-E12 Operations and Release Readiness | Draft | 14 | 0 | 0 | 2 | 1 | 11 | 15 |

## Ready queue

**No slice is startable without a human decision.** Every epic still ahead is gated by its own approval slice,
and P1-E02's next slice (6) is itself blocked by an open MFA decision. This is the correct stopping point for an
autonomous loop, not a defect.

## Human gates before the loop can advance

| Gate | Epic | What it gates | Owner input needed |
| --- | --- | --- | --- |
| P1-E01 slice 1: approve decisions | P1-E01 | epic closure and production use | 9 decisions · Architecture/Security, Architecture/Operations, Finance/Product +6 more |
| P1-E04 slice 1: approve decisions | P1-E04 | slices 2-15 | 13 decisions · Finance/Product, Finance/Tax/Compliance, Institution/Core operations +9 more |
| P1-E05 slice 1: approve decisions | P1-E05 | slices 2-15 | 17 decisions · Architecture/Finance, Finance, Finance/Domain owners +12 more |
| P1-E06 slice 1: approve decisions | P1-E06 | slices 2-13 | 12 decisions · Architecture/Operations, Risk/Compliance/Domain owners, Identity/Security +8 more |
| P1-E07 slice 1: approve decisions | P1-E07 | slices 2-15 | 17 decisions · Product/Payments, Finance/Payments/Ledger, Integration/Payments/Provider owner +13 more |
| P1-E08 slice 1: approve decisions | P1-E08 | slices 2-15 | 16 decisions · Product/Integration, Architecture/Domain owners, Security/Platform +13 more |
| P1-E09 slice 1: approve decisions | P1-E09 | slices 2-17 | 19 decisions · Product/Channel/Risk, Product/Channel/Security, Identity/Security/Channel +15 more |
| P1-E10 slice 1: approve decisions | P1-E10 | slices 2-15 | 19 decisions · Architecture, Product/Compliance/Domain owners, Platform/Security/Compliance +13 more |
| P1-E11 slice 1: approve decisions | P1-E11 | slices 2-15 | 17 decisions · Audit/Architecture/Domain owners, Compliance/Security/Domain owners, Security/DBA/Audit +13 more |
| P1-E12 slice 1: approve decisions | P1-E12 | slices 2-14 | 15 decisions · Architecture/Platform, Platform/Security, Security/Platform +12 more |

Phase exit criteria additionally require security, compliance, UAT, operational-readiness, provider-certification and
backup-restore evidence that no agent can produce. See [00-phase-overview.md](00-phase-overview.md).

## Review queue

14 of the 28 implemented slices are awaiting review; 26 are approved.

## Verification runs

| Date | Scope | Command | Result |
| --- | --- | --- | --- |
| 2026-10-28 | Solution build | `dotnet build CoreBanking.sln` | Pass, 0 warnings, 0 errors |
| 2026-10-28 | P1-E01 unit and architecture | `dotnet test` CoreLedger unit, ArchitectureTests | 101 + 15 passed |
| 2026-10-28 | Nine-service migrations, database and principal security | `scripts/db-verify.ps1` | Pass: migrations applied, DB integration suites green, 54 security tests passed |
| 2026-10-28 | P1-E02 slice 10 approver eligibility | `dotnet test` IdentityAccess unit, plus live Development API calls | 8 new tests and 110 non-database identity tests pass; eligible, maker-self, cross-tenant, invalid-permission and unauthenticated responses verified |
| 2026-10-28 | P1-E02 slice 6 MFA and step-up | `dotnet test` IdentityAccess unit and `scripts/db-verify.ps1` | 122 unit tests and 56 identity database tests pass; migration `20261028083701` applied; enrolment, activation, expiry, single use, purpose, session, device and tenant isolation verified |
| 2026-10-28 | P1-E02 slice 8 grant administration | `dotnet test` IdentityAccess unit and database suites | 126 unit tests and 62 identity database/integration tests pass; conflict refusal, version advancement, invalidation event, expiry history, revocation and a tenant-less sweep verified |
| 2026-10-28 | P1-E02 slice 7 role drafting | `dotnet test` IdentityAccess unit and database suites | 138 unit tests and 70 identity database/integration tests pass; catalogue seed, draft inertness, duplicate and unknown-permission refusal, self-conflict refusal, simulation and tenant isolation verified |
| 2026-10-28 | P1-E02 slice 9 entitlement distribution | `dotnet test` IdentityAccess unit, plus live Development API calls | 145 unit tests pass; self snapshot returned three grants at version 3, actor mismatch 403, institution mismatch 403 and regressed minimum version 409 verified |
| 2026-10-28 | P1-E02 slice 13 service clients | `dotnet test` IdentityAccess unit and database suites | 157 unit tests and 75 identity database/integration tests pass; registration, rotation overlap, expiry, revocation and tenant isolation verified |
| 2026-10-28 | P1-E02 slice 14 reconciliation | `dotnet test` IdentityAccess database suite | 76 identity database/integration tests pass; the pass detected all four finding types with delta assertions |

Gaps found by the verification run: CoreLedger exposes no `Category=Database` or `Category=Integration` tests, so
runtime concurrency evidence (INS-002 concurrent duplicate institution code, INS-004 provisioning acknowledgement,
INS-005 service-unavailable retry) is model-level only; PostgreSQL contention tests do not exist yet. The security
suite requires the local stack and fails closed without it, which is expected but must be part of review instructions.
The Identity database and integration suites run serially because they share one database and assert on the shared
outbox table; parallel classes produced false failures before `xunit.runner.json` disabled collection parallelism.

## Slice detail

### P1-E01 — Institution and Tenancy

Owning service: Core Banking and Ledger · document status `Implemented` · 11 implemented, 0 in progress, 1 blocked, 0 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve tenant-isolation, configuration-lifecycle and service-ownership decisions. | ⛔ blocked | — | P1-E01-D01, P1-E01-D02, P1-E01-D03, P1-E01-D04, P1-E01-D05, P1-E01-D06, P1-E01-D07, P1-E01-D08, P1-E01-D09 | 9 unresolved decisions in the epic document |
| 2 | Define institution, branch, calendar and configuration contracts plus failing acceptance tests. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 3 | Implement Institution aggregate, states and domain invariants. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 4 | Add EF Core mappings, constraints and migrations for institution/branch/calendar/provisioning. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 5 | Implement idempotent institution creation and provisioning outbox event. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 6 | Implement one reference service's idempotent boundary initialisation, then apply the approved pattern to all… | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 7 | Implement activation and suspension with permission and maker-checker enforcement. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 8 | Implement regions, branches and lifecycle transitions. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 9 | Implement calendar versions and controlled business-date advancement. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 10 | Implement generic configuration version, simulation, approval, publication and rollback lifecycle. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 11 | Implement Integration Hub API-client and credential-reference administration. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |
| 12 | Add audit, telemetry, repair operations, runbooks and full acceptance tests. | ✔ implemented (claim) | 2026-10-28 | — | Epic doc progress note 2026-10-26 |

### P1-E02 — Identity, Roles and Permissions

Owning service: Identity and Access · document status `In Progress` · 7 implemented, 8 in progress, 0 blocked, 0 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve ADR-0008 identity provider/token/channel model and ADR-0009 authorisation distribution/invalidation m… | ✔ implemented (claim) | pending | — | ADR-0008/0009 accepted; threat model recorded |
| 2 | Promote Phase 0 actor, tenant, permission, scope, decision and entitlement contracts to versioned production… | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-26 |
| 3 | Implement identity, membership and provider-link lifecycle with institution provisioning events and cross-ten… | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-26 |
| 4 | Implement authentication-policy versions and the approved provider adapter using synthetic/non-production cre… | ✔ implemented (claim) | pending | P1-E02-D07 | Epic doc progress note 2026-10-26 |
| 5 | Implement sessions, refresh/revocation coordination, lockouts and append-only authentication evidence. In pro… | ◐ in progress | pending | — | Epic doc: session lifecycle, lockouts, authentication evidence |
| 6 | Implement MFA enrolment/challenges and step-up evidence for one approved customer channel and one staff/privi… | ◐ in progress | pending | — | TOTP enrolment, provider-verified challenges and purpose/session/devi… |
| 7 | Implement permission catalogue plus role draft, simulation, approval and publication lifecycle. Status: in pr… | ◐ in progress | pending | — | Catalogue seeded by migration; role draft create/update/read and simu… |
| 8 | Implement scoped grants, conflict-of-duty evaluation, temporary expiry and entitlement version advancement. S… | ✔ implemented (claim) | pending | — | Scoped role assignment, temporary grants, conflict evaluation, revoca… |
| 9 | Implement entitlement distribution/invalidation and integrate one financial and one non-financial protected b… | ◐ in progress | pending | — | Self-scoped entitlement snapshot with version handshake and fail-clos… |
| 10 | Implement maker identity/approver eligibility contracts with Workflow; prove self-approval denial across role… | ◐ in progress | pending | — | Approval eligibility contract, service, endpoint and eight segregatio… |
| 11 | Implement trusted-device registration, listing, step-up, revocation and bound-session termination. Status: in… | ◐ in progress | pending | — | TrustedDevice stores only a public-key thumbprint, untrusted display… |
| 12 | Approve the payload-encryption design; implement public-key discovery and one authentication-field envelope f… | ✔ implemented (claim) | pending | — | One approved envelope suite (RSA-OAEP-256+A256GCM) with ciphertext bo… |
| 13 | Implement service identity authentication, audience restrictions, rotation and one service-to-service integra… | ◐ in progress | pending | — | Service client registration, rotation metadata, revocation and pre-au… |
| 14 | Add SIEM/audit adapters, dashboards, alerts, reconciliation jobs and runbooks. Status: in progress — an hourl… | ◐ in progress | pending | — | Hourly reconciliation pass with bounded counters, on-demand operator… |
| 15 | Complete security, contract, integration, concurrency, recovery and end-to-end evidence before enabling produ… | ✔ implemented (claim) | pending | — | Acceptance evidence pass recorded in docs/operations/phase-1-identity… |

### P1-E03 — Customer and Onboarding

Owning service: Customer and Onboarding · document status `Implemented` · 3 implemented, 12 in progress, 0 blocked, 0 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve customer data classification, country-pack ownership, provider evidence, duplicate and retention deci… | ✔ implemented (claim) | 2026-10-31 | — | Epic doc section 21 decisions D01-D15 approved 2026-10-29..30 with br… |
| 2 | Define versioned Party, Customer, Application, KYC and event contracts plus architecture tests. | ◐ in progress | 2026-10-31 | — | Party/Application contracts and capture exist |
| 3 | Implement tenant/branch reference consumers and institution provisioning for customer_onboarding_db. | ✔ implemented (claim) | 2026-10-31 | — | P1-E01 slice 6 boundary initialisation covers customer_onboarding_db |
| 4 | Implement individual Party/Application capture with explicit field mapping, encryption/tokenisation ports and… | ✔ implemented (claim) | 2026-10-31 | — | Epic doc inline status: implemented; docs/operations/customer-onboard… |
| 5 | Implement requirement snapshots, document metadata/quarantine integration and submission validation. Status:… | ◐ in progress | 2026-10-31 | — | Institution-owned versioned requirement packs with SHA-256 content ha… |
| 6 | Implement exact duplicate tokens, explainable candidate sets and review workflow; add concurrency and enumera… | ◐ in progress | 2026-10-31 | — | Exact contact, name+birth-date and address-line factors with derived… |
| 7 | Implement KYC case/requirement model and canonical verification request/result contracts. Status: in progress… | ◐ in progress | 2026-10-31 | — | KYC case opened from the submitted revision requirement snapshot; req… |
| 8 | Integrate one approved synthetic KYC adapter through Integration Hub, including uncertain status and evidence… | ◐ in progress | 2026-10-31 | — | Canonical provider port with one deterministic synthetic adapter; a r… |
| 9 | Implement classification versions, Risk references, Workflow approval application and customer activation. St… | ◐ in progress | 2026-10-31 | — | Versioned tier/segment/risk/EDD classification with Workflow decision… |
| 10 | Implement customer status transitions, corrections and downstream events without account mutation. Status: in… | ◐ in progress | 2026-10-31 | — | Epic transition table with reasons, approval references for restricte… |
| 11 | Implement approved merge planning/application, aliases, consumer acknowledgements and reconciliation. Status:… | ◐ in progress | 2026-10-31 | — | Frozen merge plans with plan hash and Workflow approval reference; on… |
| 12 | Implement preferences, consent evidence and relationship-manager assignments. Status: in progress — versioned… | ◐ in progress | 2026-10-31 | — | Versioned per-purpose/per-channel preferences with effective periods;… |
| 13 | Implement section-authorised, masked Customer 360 with Core/Workflow/Risk/Document/Channel stubs or approved… | ◐ in progress | 2026-10-31 | — | Read-only Customer 360 composition with per-section status, source, r… |
| 14 | Add immutable audit, SIEM events, dashboards, alerts, reconciliation jobs and runbooks. Status: in progress —… | ◐ in progress | 2026-10-31 | — | Immutable append-only audit.customer_audit_events plus a customer_aud… |
| 15 | Complete provider, security, privacy, recovery, performance and end-to-end acceptance evidence. Status: in pr… | ◐ in progress | 2026-10-31 | — | Category=Integration acceptance evidence executed by scripts/db-verif… |

### P1-E04 — Products and Accounts

Owning service: Core Banking and Ledger · document status `Draft` · 3 implemented, 2 in progress, 1 blocked, 9 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve product taxonomy, account-number, rounding/calculation, restriction/lien, dormancy and statement owne… | ⛔ blocked | — | P1-E04-D03, P1-E04-D04, P1-E04-D05, P1-E04-D06, P1-E04-D07, P1-E04-D09, P1-E04-D10, P1-E04-D11, P1-E04-D12, P1-E04-D13, P1-E04-D14, P1-E04-D15, P1-E04-D16 | 13 unresolved decisions in the epic document |
| 2 | Define versioned Product, Account, Control and Statement contracts plus module architecture tests. | ◐ in progress | pending | — | Product/Control contracts exist |
| 3 | Implement product identity/version lifecycle and typed rule persistence with migration tests. | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-21 |
| 4 | Implement deterministic rule evaluation and simulation with boundary/golden tests and content hashing. | ✔ implemented (claim) | pending | P1-E04-D03, P1-E04-D04, P1-E04-D05 | Epic doc progress note 2026-10-21 |
| 5 | Integrate product approval/publication with Workflow using exact version/hash and outbox events. | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-21 |
| 6 | Build customer/branch/classification reference consumers and freshness handling. | ◐ in progress | pending | — | Internal eligibility boundary resolves published version and authorit… |
| 7 | Implement account application and eligibility snapshots with negative scope tests. | · not started | — | P1-E04-D07 | — |
| 8 | Implement atomic account-number reservation, account creation and retry/concurrency tests. | · not started | — | P1-E04-D06 | — |
| 9 | Implement approval/activation and ledger-initialization handoff, proving no opening balance/posting. | · not started | — | P1-E04-D09 | — |
| 10 | Implement typed restrictions, freezes and blocks plus ledger control-evaluation contract. | · not started | — | P1-E04-D10 | — |
| 11 | Implement lien lifecycle and ledger availability-control handoff without Account balance arithmetic. | · not started | — | P1-E04-D11 | — |
| 12 | Implement dormancy assessment/reactivation and controlled closure prerequisites. | · not started | — | P1-E04-D12, P1-E04-D13 | — |
| 13 | Implement account summaries and statement-request authorisation/handoff. | · not started | — | P1-E04-D15 | — |
| 14 | Add audit, observability, reconciliation, alerts and recovery runbooks. | · not started | — | — | — |
| 15 | Complete security, performance, backup/restore and Phase 1 end-to-end evidence. | · not started | — | — | — |

### P1-E05 — Core Ledger and Financial Posting

Owning service: Core Banking and Ledger · document status `Draft` · 0 implemented, 0 in progress, 1 blocked, 14 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve ADR-0005 plus chart, mapping, precision, rounding, available-funds, reversal, period and EOD decision… | ⛔ blocked | — | P1-E05-D01, P1-E05-D02, P1-E05-D03, P1-E05-D04, P1-E05-D05, P1-E05-D06, P1-E05-D07, P1-E05-D08, P1-E05-D09, P1-E05-D10, P1-E05-D11, P1-E05-D12, P1-E05-D13, P1-E05-D14, P1-E05-D15, P1-E05-D16, P1-E05-D17 | 17 unresolved decisions in the epic document |
| 2 | Define Money/date/reference/idempotency contracts and architecture guards that prohibit Ledger persistence ac… | · not started | — | P1-E05-D08 | Domain scaffolding exists in CoreLedger; no slice claim |
| 3 | Implement ledger-account and approved mapping-version lifecycle with balanced simulation. | · not started | — | P1-E05-D02, P1-E05-D03, P1-E05-D04, P1-E05-D09 | Domain scaffolding exists in CoreLedger; no slice claim |
| 4 | Implement transaction/journal/line domain invariants and database append-only/balance guards. | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |
| 5 | Implement scoped idempotency claim/outcome and canonical request hashing under concurrency. | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |
| 6 | Implement atomic single-currency posting transaction with balance rows, movements, audit and outbox. | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |
| 7 | Integrate account controls and prove concurrent debit/available-funds safety using real PostgreSQL contention… | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |
| 8 | Implement hold lifecycle and capture-to-posting linkage. | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |
| 9 | Implement full reversal plus original/reversal trace and duplicate/concurrent reversal prevention. | · not started | — | P1-E05-D10 | Domain scaffolding exists in CoreLedger; no slice claim |
| 10 | Implement manual journal/adjustment and Workflow decision application. | · not started | — | P1-E05-D11 | Domain scaffolding exists in CoreLedger; no slice claim |
| 11 | Implement accounting periods, date policies and controlled backdated/value-dated posting. | · not started | — | P1-E05-D12, P1-E05-D13 | Domain scaffolding exists in CoreLedger; no slice claim |
| 12 | Implement EOD run graph, batches, checkpoints, restart and balancing summaries for approved Phase 1 steps. | · not started | — | P1-E05-D14 | Domain scaffolding exists in CoreLedger; no slice claim |
| 13 | Implement authoritative balance/history/statement-data queries and controlled document handoff. | · not started | — | P1-E05-D15 | Domain scaffolding exists in CoreLedger; no slice claim |
| 14 | Implement balance rebuild, reconciliation feeds, dashboards, alerts and operational runbooks. | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |
| 15 | Execute security, concurrency, resilience, performance, backup/restore and end-to-end acceptance evidence. | · not started | — | — | Domain scaffolding exists in CoreLedger; no slice claim |

### P1-E06 — Workflow and Maker-Checker

Owning service: Operations and Workflow · document status `Draft` · 4 implemented, 0 in progress, 1 blocked, 8 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve definition DSL/schema, approval patterns, routing ownership, SLA/calendar, case taxonomy, override an… | ⛔ blocked | — | P1-E06-D01, P1-E06-D03, P1-E06-D04, P1-E06-D05, P1-E06-D06, P1-E06-D07, P1-E06-D08, P1-E06-D09, P1-E06-D10, P1-E06-D11, P1-E06-D12, P1-E06-D13 | 12 unresolved decisions in the epic document |
| 2 | Define versioned definition, approval, case, action and event contracts plus architecture/security tests. | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-21 and 2026-10-31: profile-driven inge… |
| 3 | Implement workflow definition/version simulation and publication lifecycle. | · not started | — | — | — |
| 4 | Implement instance/request creation pinned to subject/version/hash and source idempotency. | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-21 and 2026-10-31: profile-driven inge… |
| 5 | Implement sequential maker-checker with work items, Identity decision revalidation and self-approval denial. | ✔ implemented (claim) | pending | P1-E06-D03, P1-E06-D04, P1-E06-D05 | Epic doc progress note 2026-10-21 and 2026-10-31: profile-driven inge… |
| 6 | Add parallel/quorum/multi-level patterns with concurrency/property tests. | · not started | — | — | — |
| 7 | Implement SLA clocks, business-calendar deadlines, reminders and idempotent escalation workers. | · not started | — | P1-E06-D06 | — |
| 8 | Implement source decision delivery, application acknowledgement, retry and explicit ApplyFailed state. | ✔ implemented (claim) | pending | — | Epic doc progress note 2026-10-21 and 2026-10-31: profile-driven inge… |
| 9 | Implement complaint/service case lifecycle, assignment, notes, evidence refs and notification handoff. | · not started | — | P1-E06-D08, P1-E06-D09 | — |
| 10 | Implement failed-payment investigation projections and requery/escalate actions with synthetic owning-service… | · not started | — | P1-E06-D10, P1-E06-D11 | — |
| 11 | Implement approved repair/reversal/freeze/override requests without direct business-state mutation. | · not started | — | P1-E06-D12 | — |
| 12 | Add audit, dashboards, alerts, reconciliation jobs and runbooks. | · not started | — | — | — |
| 13 | Complete security, resilience, performance, backup/restore and end-to-end evidence. | · not started | — | — | — |

### P1-E07 — Payments, Collections and VTU

Owning service: Payments and VTU · document status `Draft` · 0 implemented, 0 in progress, 1 blocked, 14 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve Phase 1 payment types, financial sequences, provider semantics, limits, pricing, routing, VTU catalog… | ⛔ blocked | — | P1-E07-D01, P1-E07-D02, P1-E07-D03, P1-E07-D04, P1-E07-D05, P1-E07-D06, P1-E07-D07, P1-E07-D08, P1-E07-D09, P1-E07-D10, P1-E07-D11, P1-E07-D12, P1-E07-D13, P1-E07-D14, P1-E07-D15, P1-E07-D16, P1-E07-D17 | 17 unresolved decisions in the epic document |
| 2 | Define canonical payment, provider-attempt, Ledger and event contracts with state-machine/property tests. | · not started | — | — | — |
| 3 | Implement payment instruction/idempotency/status history and reference consumers. | · not started | — | — | — |
| 4 | Implement internal transfer end to end through one atomic Ledger command. | · not started | — | — | — |
| 5 | Implement beneficiary verification, confirmation binding, cooling-off and limit reservations. | · not started | — | P1-E07-D06, P1-E07-D07 | — |
| 6 | Implement NIP name enquiry/transfer/status enquiry with synthetic Integration adapter and Uncertain state. | · not started | — | P1-E07-D03, P1-E07-D05 | — |
| 7 | Implement route-policy versions/decisions and safe failover eligibility. | · not started | — | P1-E07-D09 | — |
| 8 | Implement server-side collection initialisation, verified canonical callbacks, matching and one Ledger credit. | · not started | — | P1-E07-D04 | — |
| 9 | Implement canonical VTU catalogue/mapping publication and customer-parameter validation. | · not started | — | P1-E07-D10, P1-E07-D11 | — |
| 10 | Implement airtime/data purchase with provider wallet/availability, Ledger, uncertainty and delivery state. | · not started | — | P1-E07-D12 | — |
| 11 | Implement approved bill categories and protected token/voucher/receipt flows. | · not started | — | P1-E07-D13 | — |
| 12 | Implement refund/recall/reversal/investigation requests through Workflow/owners. | · not started | — | P1-E07-D14 | — |
| 13 | Implement scheduled occurrence foundation only for approved Phase 1 journeys; keep bulk/virtual accounts disa… | · not started | — | P1-E07-D15 | — |
| 14 | Add audit, reconciliation feeds/jobs, dashboards, alerts and runbooks. | · not started | — | P1-E07-D16 | — |
| 15 | Complete provider certification, security, concurrency, resilience, performance and restore evidence. | · not started | — | — | — |

### P1-E08 — Integration Hub and Providers

Owning service: Integration Hub · document status `Draft` · 0 implemented, 3 in progress, 1 blocked, 11 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve provider-adapter ADRs, canonical status/submission model, secret/certificate lifecycle, webhook evide… | ⛔ blocked | — | P1-E08-D01, P1-E08-D02, P1-E08-D03, P1-E08-D04, P1-E08-D05, P1-E08-D06, P1-E08-D07, P1-E08-D08, P1-E08-D09, P1-E08-D10, P1-E08-D11, P1-E08-D12, P1-E08-D13, P1-E08-D14, P1-E08-D15, P1-E08-D16 | 16 unresolved decisions in the epic document |
| 2 | Create CoreBanking.IntegrationHub solution boundaries, architecture tests and versioned canonical contracts w… | ◐ in progress | pending | — | CoreBanking.IntegrationHub solution boundaries and architecture tests… |
| 3 | Implement provider/environment/service configuration, permissions, maker-checker publication and migrations. | ◐ in progress | pending | — | P1-E01 slice 11: credential-reference and API-client administration,… |
| 4 | Implement secret/certificate references, rotation lineage, expiry worker and non-reveal security tests agains… | ◐ in progress | pending | P1-E08-D04 | P1-E01 slice 11: credential-reference and API-client administration,… |
| 5 | Implement invocation/idempotency/attempt persistence plus synthetic connector for success, pre-submit failure… | · not started | — | — | — |
| 6 | Implement NIBSS canonical operations and certified sandbox adapter once exact specifications are approved. | · not started | — | P1-E08-D04, P1-E08-D05 | — |
| 7 | Implement PSP initialisation/verification/webhook adapter for each approved provider, one provider/version at… | · not started | — | P1-E08-D06 | — |
| 8 | Implement webhook ingress, raw-byte verification, durable receipt, deduplication, canonical dispatch and dead… | · not started | — | P1-E08-D09, P1-E08-D10 | — |
| 9 | Implement authorised replay/repair with Workflow integration and idempotent consumer contract tests. | · not started | — | — | — |
| 10 | Implement KYC/identity and VTU/biller adapter ports required by approved Phase 1 journeys. | · not started | — | — | — |
| 11 | Implement SMS/email transport adapters and USSD/WhatsApp protocol foundations needed by Epic 09, without chan… | · not started | — | — | — |
| 12 | Implement health/readiness/circuit projections, freshness, expiry/configuration drift alerts and owner-facing… | · not started | — | P1-E08-D11 | — |
| 13 | Implement connector certification plans/runs/evidence and production enablement gate. | · not started | — | P1-E08-D12 | — |
| 14 | Implement audit/reconciliation evidence feed, dashboards and provider outage/uncertainty/webhook/rotation run… | · not started | — | P1-E08-D14 | — |
| 15 | Complete performance, security, dependency, chaos, replay, restore and end-to-end evidence for enabled provid… | · not started | — | — | — |

### P1-E09 — Channels, CMS and Communications

Owning service: Channels and Engagement · document status `Draft` · 0 implemented, 1 in progress, 1 blocked, 15 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve Phase 1 channel journeys, identity/link/step-up, retention, CMS/localisation and notification classif… | ⛔ blocked | — | P1-E09-D01, P1-E09-D02, P1-E09-D03, P1-E09-D04, P1-E09-D05, P1-E09-D06, P1-E09-D07, P1-E09-D08, P1-E09-D09, P1-E09-D10, P1-E09-D11, P1-E09-D12, P1-E09-D13, P1-E09-D14, P1-E09-D15, P1-E09-D16, P1-E09-D17, P1-E09-D18, P1-E09-D19 | 19 unresolved decisions in the epic document |
| 2 | Create C# module boundaries, architecture tests and shared canonical channel contracts without live providers. | ◐ in progress | pending | — | CoreBanking.ChannelsEngagement module boundaries exist |
| 3 | Implement channel profiles/policies, permissions, Workflow publication and tenant-isolated migrations. | · not started | — | — | — |
| 4 | Implement CMS content/version/localisation/approval/publication plus safe public/authorised projections. | · not started | — | P1-E09-D07, P1-E09-D08, P1-E09-D09 | — |
| 5 | Implement notification template/version/placeholder/preview/publication with OTP-secret-path tests. | · not started | — | P1-E09-D10, P1-E09-D15 | — |
| 6 | Implement notification request/idempotency, trusted recipient resolution, preference/consent/quiet-hours/supp… | · not started | — | P1-E09-D10, P1-E09-D11, P1-E09-D12 | — |
| 7 | Implement Integration Hub delivery contract, route decisions, delivery status, uncertainty and safe failover… | · not started | — | P1-E09-D13, P1-E09-D14 | — |
| 8 | Implement USSD menu graph/version/publication/session state with synthetic canonical ingress. | · not started | — | P1-E09-D05 | — |
| 9 | Implement one approved read-only USSD journey end to end before any financial command. | · not started | — | — | — |
| 10 | Implement controlled USSD command confirmation/idempotency/status recovery for each separately approved owner… | · not started | — | — | — |
| 11 | Implement conversation/message/participant/link state and WhatsApp/website canonical ingress. | · not started | — | — | — |
| 12 | Implement public CMS knowledge intent, deterministic controlled intents and explicit safe fallback. | · not started | — | P1-E09-D06 | — |
| 13 | Implement authenticated conversational action/step-up pattern one owner action at a time. | · not started | — | — | — |
| 14 | Implement human handoff/assignment/service-request integration and access controls. | · not started | — | P1-E09-D17 | — |
| 15 | Implement campaign persistence only and keep production execution disabled until approved. | · not started | — | — | — |
| 16 | Add audit, dashboards, alerts, provider/session/chatbot/publication/delivery runbooks and privacy jobs. | · not started | — | — | — |
| 17 | Complete security, accessibility/localisation contract, concurrency, resilience, performance and restore evid… | · not started | — | — | — |

### P1-E10 — Documents and Statements

Owning service: Risk and Reporting · document status `Draft` · 0 implemented, 1 in progress, 1 blocked, 13 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve module ownership, Phase 1 document types, source contracts, formats, object storage, secure access, r… | ⛔ blocked | — | P1-E10-D01, P1-E10-D02, P1-E10-D03, P1-E10-D04, P1-E10-D05, P1-E10-D06, P1-E10-D07, P1-E10-D08, P1-E10-D09, P1-E10-D10, P1-E10-D11, P1-E10-D12, P1-E10-D13, P1-E10-D14, P1-E10-D15, P1-E10-D16, P1-E10-D17, P1-E10-D18, P1-E10-D19 | 19 unresolved decisions in the epic document |
| 2 | Create C# Documents/Statements boundaries, architecture tests and versioned contracts without production file… | ◐ in progress | pending | — | Risk/Reporting module boundaries exist |
| 3 | Provision tenant/environment object-store controls and implement artifact metadata/opaque-reference persisten… | · not started | — | P1-E10-D03, P1-E10-D04 | — |
| 4 | Implement upload sessions, quarantine, type/size validation, scanning, activation and rejection with maliciou… | · not started | — | P1-E10-D05, P1-E10-D06 | — |
| 5 | Implement document registry, owner links, classification and successor-version lifecycle. | · not started | — | P1-E10-D07 | — |
| 6 | Implement template/version/localisation/assets/preview/Workflow publication and render sandbox. | · not started | — | P1-E10-D08, P1-E10-D09 | — |
| 7 | Implement secure metadata view, access grants/download gateway, current authorisation and sensitive-access au… | · not started | — | P1-E10-D10, P1-E10-D11 | — |
| 8 | Implement Account StatementRequested consumer and Ledger statement snapshot contract using synthetic authorit… | · not started | — | P1-E10-D12, P1-E10-D13 | — |
| 9 | Implement deterministic PDF statement generation, reconciliation checks, manifests, watermark/integrity and R… | · not started | — | P1-E10-D14 | — |
| 10 | Implement receipt/confirmation generation one verified owner fact/type at a time. | · not started | — | P1-E10-D15 | — |
| 11 | Implement Customer/KYC document upload/link/retrieval contract and classification policies. | · not started | — | — | — |
| 12 | Implement retention assignment, archive, legal hold/release and approved disposition with race tests. | · not started | — | P1-E10-D16, P1-E10-D17 | — |
| 13 | Implement object/database reconciliation, integrity workers, orphan repair, dashboards, alerts and runbooks. | · not started | — | — | — |
| 14 | Complete rendering visual QA, security/parser isolation, performance, concurrency, accessibility/localisation… | · not started | — | — | — |
| 15 | Complete backup/PITR/object restore, source-reference reconciliation and safe worker-resume evidence. | · not started | — | — | — |

### P1-E11 — Audit, Traceability and Recovery

Owning service: Audit and Reconciliation · document status `Draft` · 0 implemented, 1 in progress, 1 blocked, 13 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Approve enterprise audit envelope, source commit, trace identity/link, integrity, retention and recovery ADRs. | ⛔ blocked | — | P1-E11-D01, P1-E11-D02, P1-E11-D03, P1-E11-D04, P1-E11-D05, P1-E11-D06, P1-E11-D07, P1-E11-D08, P1-E11-D09, P1-E11-D10, P1-E11-D11, P1-E11-D12, P1-E11-D13, P1-E11-D14, P1-E11-D15, P1-E11-D16, P1-E11-D17 | 17 unresolved decisions in the epic document |
| 2 | Create C# Audit/Reconciliation module boundaries, append-only database grants and architecture tests. | ◐ in progress | pending | P1-E11-D03 | CoreBanking.AuditReconciliation exists; append-only institution audit… |
| 3 | Implement producer/schema registry, ingestion validation, dedup/conflict quarantine, inbox and immutable pers… | · not started | — | — | — |
| 4 | Add source-service audit SDK/contracts/building blocks and prove atomic local evidence/outbox in one non-fina… | · not started | — | P1-E11-D02 | — |
| 5 | Implement sensitive-access/configuration-change evidence, masking/tokenisation and permission-scoped search/d… | · not started | — | P1-E11-D05 | — |
| 6 | Implement integrity checkpoints, verification, independent admin evidence and failure alerts. | · not started | — | P1-E11-D04, P1-E11-D09 | — |
| 7 | Implement typed trace nodes/links/completeness for internal transfer end to end. | · not started | — | P1-E11-D07 | — |
| 8 | Extend trace mapping to NIP/provider, workflow, Ledger, notification, document and reconciliation references. | · not started | — | P1-E11-D07 | — |
| 9 | Implement approved evidence export, frozen manifests, Documents artifact generation and secure access. | · not started | — | P1-E11-D08 | — |
| 10 | Implement backup catalogue/verification ingestion for every Phase 1 database/object-store resource. | · not started | — | P1-E11-D10 | — |
| 11 | Implement versioned recovery plans, steps, gates, exercise evidence and independent sign-off. | · not started | — | P1-E11-D11, P1-E11-D12 | — |
| 12 | Run service-specific isolated restore exercises, beginning with non-financial services, then Ledger/Payments/… | · not started | — | — | — |
| 13 | Implement cross-service PITR reconciliation, controlled replay, projection rebuild and traffic/worker fencing… | · not started | — | P1-E11-D13, P1-E11-D14 | — |
| 14 | Add dashboards, alerts, SIEM handoff and audit-gap/integrity/backup/restore runbooks. | · not started | — | — | — |
| 15 | Complete security, scale, retention/legal hold, chaos, restore and Phase 1 end-to-end evidence. | · not started | — | — | — |

### P1-E12 — Operations and Release Readiness

Owning service: Platform-wide · document status `Draft` · 0 implemented, 2 in progress, 1 blocked, 11 not started.

| Slice | Scope | Status | Approval | Unresolved decisions attached | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Define and approve the deployment, observability, migration/recovery and supply-chain ADRs. | ⛔ blocked | — | P1-E12-D01, P1-E12-D02, P1-E12-D03, P1-E12-D04, P1-E12-D05, P1-E12-D06, P1-E12-D07, P1-E12-D08, P1-E12-D09, P1-E12-D10, P1-E12-D11, P1-E12-D12, P1-E12-D13, P1-E12-D14, P1-E12-D15 | 15 unresolved decisions in the epic document |
| 2 | Create a versioned service inventory and release-manifest schema. | · not started | — | — | — |
| 3 | Standardise .NET build, analyzer, test, contract and artifact packaging across all services. | ◐ in progress | pending | — | Phase 0 build, analyzer, test and CI standardisation |
| 4 | Add dependency inventory, secret, vulnerability, container and infrastructure scanning gates. | · not started | — | — | — |
| 5 | Implement protected environment promotion with immutable artifacts and approval binding. | · not started | — | — | — |
| 6 | Package service-owned migrations and validate expand-migrate-contract behaviour. | · not started | — | — | — |
| 7 | Add shared technical observability conventions without sharing domain models. | · not started | — | — | — |
| 8 | Implement liveness, readiness, startup, graceful shutdown and deployment verification per service. | ◐ in progress | pending | — | Per-service database readiness and health wiring shipped |
| 9 | Add role-aware staff context and operations case/incident APIs with cross-tenant negative tests. | · not started | — | — | — |
| 10 | Define dashboards, alerts and business health signals for every Phase 1 service. | · not started | — | — | — |
| 11 | Implement incident/runbook evidence and immutable audit integration. | · not started | — | — | — |
| 12 | Run approved performance, resilience, restore and replay exercises. | · not started | — | — | — |
| 13 | Execute a full non-production release rehearsal including failed migration and rollback/forward-fix scenarios. | · not started | — | — | — |
| 14 | Assemble production-readiness evidence and obtain the required business, security, operations and compliance… | · not started | — | — | — |

## Decision register

Every decision the epic documents still require, with its owner and the slice it gates. The machine-readable copy is
[decision-register.csv](decision-register.csv).

### P1-E01 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E01-D01 | Physical tenant-isolation model | Architecture/Security | Persistence implementation | unresolved | — |
| P1-E01-D02 | Required service acknowledgement list | Architecture/Operations | Provisioning workflow app… | unresolved | — |
| P1-E01-D03 | Base-currency change policy | Finance/Product | Institution activation | unresolved | — |
| P1-E01-D04 | Country-pack catalogue/versioning | Compliance/Product | Production country assign… | unresolved | — |
| P1-E01-D05 | Business-date timezone and cut-off | Operations/Finance | Date advancement implemen… | unresolved | — |
| P1-E01-D06 | Configuration simulation semantics by area | Owning epic | Area-specific implementat… | unresolved | — |
| P1-E01-D07 | Secret manager and HSM integration | Security/Platform | Production credentials | unresolved | — |
| P1-E01-D08 | Retention, residency and legal-hold policy | Legal/Compliance | Production data | unresolved | — |
| P1-E01-D09 | Institution suspension behaviour by service | Product/Operations | Suspension implementation | unresolved | — |

### P1-E02 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E02-D01 | Identity provider and licensing/hosting model | Architecture/Security/Product | Slice 1 | resolved | — |
| P1-E02-D02 | Channel-specific OAuth/OIDC flows and token transport | Security/Channel owners | Slice 1 | resolved | — |
| P1-E02-D03 | Token lifetimes, refresh rotation and revocation mechanism | Security | Slice 1 | resolved | — |
| P1-E02-D04 | Service-to-service authentication mechanism | Security/Platform | Slice 1 | resolved | — |
| P1-E02-D05 | Entitlement distribution/read-model strategy | Architecture/Security | Slice 1 | resolved | — |
| P1-E02-D06 | Maximum entitlement/revocation propagation window | Security/Risk | Slice 1 | resolved | — |
| P1-E02-D07 | Platform security floors for passwords, lockout and MFA | Security | Slice 4 | open | — |
| P1-E02-D08 | Approved MFA and recovery methods per actor/channel | Security/Product | Slice 6 | resolved | [brief](decisions/P1-E02-D08-mfa-and-recovery-methods.md) |
| P1-E02-D09 | Initial system permission catalogue and role templates | Domain owners/Security | Slice 7 | resolved | [brief](decisions/P1-E02-D09-permission-catalogue-and-role-templates.md) |
| P1-E02-D10 | Role/grant approval thresholds and conflicts | Security/Operations | Slice 7 | resolved | [brief](decisions/P1-E02-D10-role-and-grant-approval-thresholds-and-conflicts.md) |
| P1-E02-D11 | Device-key protocol, trust duration and privacy fields | Security/Mobile/Privacy | Slice 11 | resolved | [brief](decisions/P1-E02-D11-trusted-device-protocol-and-trust-duration.md) |
| P1-E02-D12 | Payload-envelope format, algorithms and replay policy | Security/Architecture | Slice 12 | resolved | [brief](decisions/P1-E02-D12-payload-envelope-format-algorithms-and-replay-policy.md) |
| P1-E02-D13 | KMS/HSM product and key-operation boundary | Security/Platform | Slice 12 | resolved | [brief](decisions/P1-E02-D13-kms-hsm-product-and-key-operation-boundary.md) |
| P1-E02-D14 | Authentication/security evidence retention | Security/Compliance | Before migration approval | open | — |
| P1-E02-D15 | Authentication and authorisation SLOs/load profile | Product/Platform | Before performance test | open | — |

### P1-E03 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E03-D01 | Launch country pack and authoritative policy owner | Product/Compliance | Slice 1 | resolved | [brief](decisions/P1-E03-D01-D02-D03-requirement-packs-numbering-and-classification.md) |
| P1-E03-D02 | Customer/party numbering rules | Product/Operations | Slice 2 | resolved | [brief](decisions/P1-E03-D01-D02-D03-requirement-packs-numbering-and-classification.md) |
| P1-E03-D03 | Field classification and encryption/tokenisation design | Security/Privacy | Slice 1 | resolved | [brief](decisions/P1-E03-D01-D02-D03-requirement-packs-numbering-and-classification.md) |
| P1-E03-D04 | Duplicate rules/model, thresholds and reviewer evidence | Compliance/Data | Slice 6 | resolved | [brief](decisions/P1-E03-D04-D05-duplicate-detection-and-identifier-uniqueness.md) |
| P1-E03-D05 | Verified-identifier uniqueness policy | Compliance | Slice 6 | resolved | [brief](decisions/P1-E03-D04-D05-duplicate-detection-and-identifier-uniqueness.md) |
| P1-E03-D06 | Approved Phase 1 KYC checks and providers | Compliance/Product | Slice 7 | resolved | [brief](decisions/P1-E03-D06-kyc-checks-providers-and-canonical-outcomes.md) |
| P1-E03-D07 | Provider retry/failover/status-enquiry semantics | Integration/Provider owner | Slice 8 | resolved | [brief](decisions/P1-E03-D07-D08-provider-retry-evidence-and-status-enquiry.md) |
| P1-E03-D08 | Raw evidence format, storage, access and retention | Compliance/Security | Slice 8 | resolved | [brief](decisions/P1-E03-D07-D08-provider-retry-evidence-and-status-enquiry.md) |
| P1-E03-D09 | KYC tier, risk class, EDD and review policy | Compliance/Risk | Slice 9 | resolved | [brief](decisions/P1-E03-D09-D10-classification-activation-and-status-transitions.md) |
| P1-E03-D10 | Customer status transitions and account consequences | Product/Compliance/Core | Slice 10 | resolved | [brief](decisions/P1-E03-D09-D10-classification-activation-and-status-transitions.md) |
| P1-E03-D11 | Merge eligibility, freezes and required consumers | Architecture/Operations | Slice 11 | resolved | [brief](decisions/P1-E03-D11-customer-merge-eligibility-and-reconciliation.md) |
| P1-E03-D12 | Consent purposes and lawful bases | Legal/Privacy | Slice 12 | resolved | [brief](decisions/P1-E03-D12-consent-purposes-lawful-bases-and-preferences.md) |
| P1-E03-D13 | Customer 360 launch sections and freshness limits | Product/Domain owners | Slice 13 | resolved | [brief](decisions/P1-E03-D13-customer-360-sections-and-freshness.md) |
| P1-E03-D14 | Data retention, legal hold and deletion/anonymisation | Legal/Compliance/Privacy | Before migration approval | resolved | [brief](decisions/P1-E03-D14-retention-audit-and-reconciliation-posture.md) |
| P1-E03-D15 | Capacity, SLO, RTO and RPO targets | Product/Platform | Before release test | resolved | [brief](decisions/P1-E03-D15-acceptance-evidence-and-measurement-posture.md) |

### P1-E04 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E04-D01 | Phase 1 live product families | Product/Architecture | Slice 1 | resolved | — |
| P1-E04-D02 | Product/version code and coexistence rules | Product | Slice 3 | resolved | — |
| P1-E04-D03 | Decimal precision, rate scale and rounding points | Finance/Product | Slice 4 | open | — |
| P1-E04-D04 | Interest/day-count/accrual/capitalisation rules | Finance/Product | Slice 4 | open | — |
| P1-E04-D05 | Fee/tax definitions and legal approval | Finance/Tax/Compliance | Slice 4 | open | — |
| P1-E04-D06 | Account-number format, scope, check digit and exhaustion | Institution/Core operations | Slice 8 | open | — |
| P1-E04-D07 | Multiple-account-per-product/customer rules | Product/Risk | Slice 7 | open | — |
| P1-E04-D08 | Eligibility freshness/final recheck point | Product/Compliance | Slice 7 | resolved | — |
| P1-E04-D09 | Account approval/activation sequence | Operations/Product | Slice 9 | open | — |
| P1-E04-D10 | Restriction/freeze/block precedence | Legal/Risk/Core | Slice 10 | open | — |
| P1-E04-D11 | Lien priority, partial use and release authority | Legal/Finance/Core | Slice 11 | open | — |
| P1-E04-D12 | Dormancy qualifying activity and business-date policy | Compliance/Product | Slice 12 | open | — |
| P1-E04-D13 | Closure prerequisites and permitted post-close events | Finance/Operations | Slice 12 | open | — |
| P1-E04-D14 | Existing-account product migration policy | Product/Legal | Before migration feature | unresolved | — |
| P1-E04-D15 | Statement periods, formats, retention and delivery | Product/Compliance | Slice 13 | open | — |
| P1-E04-D16 | Capacity, latency, RTO and RPO targets | Product/Platform | Before release testing | open | — |

### P1-E05 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E05-D01 | ADR-0005 consistency, balances and transaction boundary | Architecture/Finance | Slice 1 | open | — |
| P1-E05-D02 | Chart of accounts and legal-entity balancing scopes | Finance | Slice 3 | open | — |
| P1-E05-D03 | Phase 1 accounting event catalogue/mappings | Finance/Domain owners | Slice 3 | open | — |
| P1-E05-D04 | Monetary precision, rate scale and rounding | Finance | Slice 3 | open | — |
| P1-E05-D05 | Debit/credit sign and balance representation | Finance/Architecture | Slice 1 | open | — |
| P1-E05-D06 | Available-funds, overdraft, hold, lien and pending formula | Finance/Risk/Product | Slice 1 | open | — |
| P1-E05-D07 | PostgreSQL isolation/locking/retry policy | Architecture/DBA | Slice 1 | open | — |
| P1-E05-D08 | External/source reference uniqueness scopes | Domain owners/Finance | Slice 2 | open | — |
| P1-E05-D09 | Fee/tax/commission/interest/waiver accounting treatment | Finance/Tax/Product | Slice 3 | open | — |
| P1-E05-D10 | Reversal/partial reversal and component policy | Finance/Operations | Slice 9 | open | — |
| P1-E05-D11 | Manual journal/customer-account permissions | Finance/Security | Slice 10 | open | — |
| P1-E05-D12 | Backdate/value-date windows and recalculation impact | Finance/Compliance | Slice 11 | open | — |
| P1-E05-D13 | Period close/lock/reopen state and authority | Finance | Slice 11 | open | — |
| P1-E05-D14 | EOD step graph, partitions and completion gates | Finance/Core operations | Slice 12 | open | — |
| P1-E05-D15 | Statement running-balance/snapshot semantics | Finance/Product | Slice 13 | open | — |
| P1-E05-D16 | Retention/partitioning/archival | Compliance/DBA | Before migration approval | open | — |
| P1-E05-D17 | Load, latency, availability, RTO and RPO | Product/Platform | Before release testing | open | — |

### P1-E06 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E06-D01 | Definition DSL/schema and supported approval patterns | Architecture/Operations | Slice 1 | partial | — |
| P1-E06-D02 | Initial workflow catalogue and source namespaces | Product/Domain owners | Slice 1 | resolved | — |
| P1-E06-D03 | Thresholds, quorum and unique-approver policies | Risk/Compliance/Domain owners | Slice 5 | partial | — |
| P1-E06-D04 | Candidate resolution/cache/freshness policy | Identity/Security | Slice 5 | partial | — |
| P1-E06-D05 | Delegation and reassignment policy | Security/Operations | Slice 5 | partial | — |
| P1-E06-D06 | Business calendars, SLA pause and escalation rules | Operations/Compliance | Slice 7 | open | — |
| P1-E06-D07 | Definition/instance migration policy | Architecture/Operations | Before migration feature | open | — |
| P1-E06-D08 | Complaint/service taxonomy and customer communications | Customer Service/Product | Slice 9 | open | — |
| P1-E06-D09 | Case note/evidence classification and retention | Compliance/Privacy | Slice 9 | open | — |
| P1-E06-D10 | Failed-payment case creation thresholds and status views | Payments/Operations | Slice 10 | open | — |
| P1-E06-D11 | Allowed requery/repair/reversal/freeze action contracts | Domain owners/Risk | Slice 10 | open | — |
| P1-E06-D12 | Override catalogue and non-overridable invariants | Security/Risk/Finance | Slice 11 | open | — |
| P1-E06-D13 | Capacity, latency, availability, RTO and RPO | Product/Platform | Before release testing | open | — |

### P1-E07 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E07-D01 | Phase 1 enabled payment/service/provider catalogue | Product/Payments | Slice 1 | open | — |
| P1-E07-D02 | Financial sequence per transfer/collection/VTU type | Finance/Payments/Ledger | Slice 1 | open | — |
| P1-E07-D03 | NIBSS NIP specification/session/status/reversal semantics | Integration/Payments/Provider owner | Slice 6 | open | — |
| P1-E07-D04 | Paystack/Flutterwave collection verification semantics | Integration/Payments | Slice 8 | open | — |
| P1-E07-D05 | Provider timeout/retry/status/failover eligibility | Provider owner/Risk | Slice 6 | open | — |
| P1-E07-D06 | Beneficiary verification/cooling-off policy | Risk/Product | Slice 5 | open | — |
| P1-E07-D07 | Limit windows, reservation/release and timezones | Risk/Product/Finance | Slice 5 | open | — |
| P1-E07-D08 | Pricing, fee, tax, commission, margin/provider-cost rules | Finance/Tax/Product | Before execution | unresolved | — |
| P1-E07-D09 | Route scoring inputs/windows/weights/freshness | Payments/Operations/Risk | Slice 7 | open | — |
| P1-E07-D10 | Approved bill categories/validation parameters | Product/Payments | Slice 9 | open | — |
| P1-E07-D11 | VTU provider catalogue sync/mapping approval | Payments/Integration | Slice 9 | open | — |
| P1-E07-D12 | Provider wallet threshold/freshness | Operations/Finance | Slice 10 | open | — |
| P1-E07-D13 | Token/voucher disclosure and retention | Security/Product | Slice 11 | open | — |
| P1-E07-D14 | Refund/recall/reversal/partial action policy | Finance/Payments/Provider owner | Slice 12 | open | — |
| P1-E07-D15 | Scheduled/standing-order Phase 1 journeys | Product/Risk | Slice 13 | open | — |
| P1-E07-D16 | Reconciliation keys/tolerances/cut-offs | Finance/Reconciliation | Slice 14 | open | — |
| P1-E07-D17 | Capacity, latency, availability, RTO/RPO | Product/Platform | Before release testing | open | — |

### P1-E08 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E08-D01 | Phase 1 provider/service catalogue per institution | Product/Integration | Slice 1 | open | — |
| P1-E08-D02 | Canonical operation/status/submission taxonomy | Architecture/Domain owners | Slice 1 | open | — |
| P1-E08-D03 | Secret manager, KMS/HSM and workload identity | Security/Platform | Slice 1 | open | — |
| P1-E08-D04 | Certificate validation, signing/encryption and rotation profiles | Security/Provider owner | Slice 4/6 | open | — |
| P1-E08-D05 | NIBSS specification, endpoints, fields, session refs and enquiry/reversal seman… | Integration/Payments/NIBSS owner | Slice 6 | open | — |
| P1-E08-D06 | Paystack/Flutterwave specifications, verification and webhook semantics | Integration/Payments | Slice 7 | open | — |
| P1-E08-D07 | KYC/VTU/biller/SMS/email Phase 1 providers and contracts | Product/Domain owners | Slices 10/11 | open | — |
| P1-E08-D08 | Per-operation timeout/retry/idempotency/enquiry policy | Provider owner/Risk | Before live adapter | unresolved | — |
| P1-E08-D09 | Webhook raw evidence retention/encryption/access | Security/Compliance | Slice 8 | open | — |
| P1-E08-D10 | Webhook acknowledgement and retry semantics per provider | Provider owner | Slice 8 | open | — |
| P1-E08-D11 | Health signals, windows, thresholds and freshness | Operations/Route owners | Slice 12 | open | — |
| P1-E08-D12 | Certification plans, evidence and approver authority | QA/Security/Provider owner | Slice 13 | open | — |
| P1-E08-D13 | Connector deployment/rollback and version coexistence | Platform/Integration | Before production | unresolved | — |
| P1-E08-D14 | Provider evidence export and reconciliation keys | Integration/Reconciliation | Slice 14 | open | — |
| P1-E08-D15 | Retention, residency and legal-hold rules | Compliance/Security | Before production | unresolved | — |
| P1-E08-D16 | Capacity, latency, availability, RTO/RPO | Product/Platform | Before release testing | open | — |

### P1-E09 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E09-D01 | Production Phase 1 USSD journeys | Product/Channel/Risk | Slice 1 | open | — |
| P1-E09-D02 | Production WhatsApp/website-chat journeys | Product/Channel/Security | Slice 1 | open | — |
| P1-E09-D03 | Channel identity linking and revocation proof | Identity/Security/Channel | Slice 1 | open | — |
| P1-E09-D04 | Per-intent step-up/confirmation/permission policy | Risk/Domain owners | Before protected action | unresolved | — |
| P1-E09-D05 | USSD provider constraints, session timeout and encoding/size limits | Integration/Channel owner | Slice 8 | open | — |
| P1-E09-D06 | Chatbot classifier/model use and confidence/fallback policy | Product/Security/AI governance | Slice 12 | open | — |
| P1-E09-D07 | CMS content types, categories and access classifications | Content/Compliance | Slice 4 | open | — |
| P1-E09-D08 | Supported locales and fallback rules | Product/Institution | Slice 4 | open | — |
| P1-E09-D09 | Author/approver/publisher separation matrix | Compliance/Content | Slice 4 | open | — |
| P1-E09-D10 | Notification event finality/classification catalogue | Domain owners/Communication | Slice 5/6 | open | — |
| P1-E09-D11 | Trusted recipient resolution and contact-change handling | Customer/Security/Channel | Slice 6 | open | — |
| P1-E09-D12 | Consent, mandatory notices, suppression and quiet-hour policy | Compliance/Legal/Product | Slice 6 | open | — |
| P1-E09-D13 | SMS/email/push/in-app Phase 1 provider/channel catalogue | Product/Integration | Slice 7 | open | — |
| P1-E09-D14 | Provider retry/failover/uncertainty semantics | Integration/Communication | Slice 7 | open | — |
| P1-E09-D15 | OTP protected-delivery mechanism | Identity/Security/Channel | Slice 5 | open | — |
| P1-E09-D16 | Conversation/message/content/notification retention and legal hold | Compliance/Privacy | Before production | unresolved | — |
| P1-E09-D17 | Human-handoff queues, SLAs and agent access | Operations/Product | Slice 14 | open | — |
| P1-E09-D18 | Production campaign scope and approval | Marketing/Compliance | Deferred/approval required | open | — |
| P1-E09-D19 | Capacity, latency, availability, RTO/RPO | Product/Platform | Before release testing | open | — |

### P1-E10 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E10-D01 | Documents/Statements module ownership ADR | Architecture | Slice 1 | open | — |
| P1-E10-D02 | Phase 1 document/receipt/confirmation types | Product/Compliance/Domain owners | Slice 1 | open | — |
| P1-E10-D03 | Approved object store, tenant layout and residency | Platform/Security/Compliance | Slice 1/3 | open | — |
| P1-E10-D04 | Encryption/key/object-lock/versioning model | Security/Platform | Slice 3 | open | — |
| P1-E10-D05 | Allowed file types/sizes/encryption and scan profiles | Security/Customer | Slice 4 | open | — |
| P1-E10-D06 | Malware/content scanner and isolation runtime | Security/Platform | Slice 4 | open | — |
| P1-E10-D07 | Document classification and owner-link taxonomy | Compliance/Domain owners | Slice 5 | open | — |
| P1-E10-D08 | Rendering engine, PDF profile, fonts and accessibility target | Product/Platform/Accessibility | Slice 6 | open | — |
| P1-E10-D09 | Template approval/publication and branding ownership | Product/Brand/Compliance | Slice 6 | open | — |
| P1-E10-D10 | Secure gateway versus direct time-limited object URL | Security/Platform | Slice 7 | open | — |
| P1-E10-D11 | Access-grant lifetime/use/audience policy | Security/Product | Slice 7 | open | — |
| P1-E10-D12 | Statement periods, running-balance/snapshot/order semantics | Finance/Product/Ledger | Slice 8 | open | — |
| P1-E10-D13 | Pending/disputed item statement display policy | Finance/Product | Slice 8 | open | — |
| P1-E10-D14 | Watermark, tamper evidence and signing policy | Compliance/Security | Slice 9 | open | — |
| P1-E10-D15 | Receipt final statuses/fields/components per journey | Domain owner/Finance | Slice 10 | open | — |
| P1-E10-D16 | Retention triggers/periods/archive/disposition by class | Legal/Compliance | Slice 12 | open | — |
| P1-E10-D17 | Legal-hold authority/scope/release process | Legal/Compliance | Slice 12 | open | — |
| P1-E10-D18 | Backup/object replication/RTO/RPO | Platform/Compliance | Before release | open | — |
| P1-E10-D19 | Capacity, generation latency and maximum workload | Product/Platform | Before load testing | open | — |

### P1-E11 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E11-D01 | Enterprise audit envelope/event taxonomy | Audit/Architecture/Domain owners | Slice 1 | open | — |
| P1-E11-D02 | Source action/access audit coverage matrix | Compliance/Security/Domain owners | Slice 1/4 | open | — |
| P1-E11-D03 | Append-only database/storage/grant model | Security/DBA/Audit | Slice 2 | open | — |
| P1-E11-D04 | Canonicalisation/digest/signature/checkpoint/key model | Security/Audit | Slice 1/6 | open | — |
| P1-E11-D05 | Protected reference/token/search strategy | Security/Privacy/Audit | Slice 5 | open | — |
| P1-E11-D06 | Audit retention, archive, legal hold and disposition | Legal/Compliance | Before production | unresolved | — |
| P1-E11-D07 | Trace node/link types and journey completeness policies | Domain owners/Audit | Slice 7/8 | open | — |
| P1-E11-D08 | Evidence export formats/destinations/watermark/signing | Audit/Legal/Security | Slice 9 | open | — |
| P1-E11-D09 | Audit self-audit and independent infrastructure evidence | Security/Platform/Audit | Slice 6 | open | — |
| P1-E11-D10 | Backup platform/resource coverage/verification method | Platform/Service owners | Slice 10 | open | — |
| P1-E11-D11 | Per-institution/service RTO and RPO targets | Institution/Product/Platform | Slice 11 | open | — |
| P1-E11-D12 | Recovery service dependency/order/fencing matrix | Platform/Service owners | Slice 11 | open | — |
| P1-E11-D13 | Broker retention/checkpoint/replay strategy | Platform/Domain owners | Slice 13 | open | — |
| P1-E11-D14 | Cross-service recovery-point reconciliation rules | Finance/Domain owners | Slice 13 | open | — |
| P1-E11-D15 | Production recovery authority/sign-off matrix | Executive/Operations/Security | Before production | unresolved | — |
| P1-E11-D16 | Restore exercise frequency, isolation and evidence retention | Compliance/Platform | Before release | unresolved | — |
| P1-E11-D17 | Audit/trace ingestion/search capacity and performance | Product/Platform | Before load testing | open | — |

### P1-E12 decisions

| ID | Decision | Owner | Required by | State | Brief |
| --- | --- | --- | --- | --- | --- |
| P1-E12-D01 | Hosting platform and environment topology | Architecture/Platform | Deployment ADR | open | — |
| P1-E12-D02 | CI/CD, artifact and provenance products | Platform/Security | CI implementation | open | — |
| P1-E12-D03 | Container signing/attestation policy | Security/Platform | Artifact promotion | open | — |
| P1-E12-D04 | Protected-environment approval matrix | Operations/Security/Risk | Production pipeline | open | — |
| P1-E12-D05 | Deployment strategies by service tier | SRE/Service owners | Production rehearsal | open | — |
| P1-E12-D06 | API/event compatibility window | Architecture | Contract gates | open | — |
| P1-E12-D07 | Production load and capacity model | Product/SRE/Architecture | Performance tests | open | — |
| P1-E12-D08 | Service tiers, SLIs, SLOs and availability targets | Product/SRE/Risk | Readiness assessment | open | — |
| P1-E12-D09 | RTO/RPO per institution/service | Business/Risk/SRE | Recovery sign-off | open | — |
| P1-E12-D10 | Observability, SIEM and on-call tooling | SRE/Security | Operational rollout | open | — |
| P1-E12-D11 | Log, trace, metric and release-evidence retention | Security/Compliance | Production configuration | open | — |
| P1-E12-D12 | Alert severity and escalation policy | Operations/SRE | Alert activation | open | — |
| P1-E12-D13 | Emergency access/change policy | Security/Risk/Operations | Production access | open | — |
| P1-E12-D14 | Data migration and go-live cutover plan | Business/Architecture/Operations | Go-live rehearsal | open | — |
| P1-E12-D15 | Production support model and provider escalation | Operations/Vendor management | Go-live | open | — |

## Maintenance

- Update `slice-ledger.csv` when a slice changes status; keep `approval` in step with the review.
- Record evidence as a test name, migration, runbook or pull request, not a narrative.
- Add a blocker id to a slice when a decision that gates it remains open; the register holds the owner.
- Re-read the epic document implementation-slices progress note before changing a claim, and correct the epic document if it disagrees.
- Phase 1 is complete only when every slice is `approved` and every phase exit criterion is evidenced.

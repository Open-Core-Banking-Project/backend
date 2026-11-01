# Phase 1 institution and tenancy acceptance evidence

Evidence date: 2026-10-26. Scope: implementation slices 1-12 of
`01-institution-and-tenancy.md`. Human slice acceptance for slices 2-12 was recorded on 2026-10-28
in the [slice ledger](../../implementation/phase-1-banking-foundation/slice-ledger.md); production-policy
approvals and the concurrency coverage named below remain open.

## Automated traceability

| Acceptance area | Primary executable evidence | Status |
| --- | --- | --- |
| CFG-001 / US-001 creation and provisioning | `InstitutionTests`, `InstitutionCreationServiceTests`, `InstitutionProvisioningContractTests`, `TenantProvisioningPlanOptionsTests` | Automated |
| CFG-002 isolation and service boundaries | service boundary consumer tests, query-filter model tests, `InstitutionApiSecurityTests` | Automated |
| CFG-003 branches/regions | `OrganisationLifecycleTests` and migration/model evidence | Automated |
| CFG-004 calendars/business date | `BusinessCalendarTests` and contract/model evidence | Automated |
| CFG-005/007 configuration lifecycle | `InstitutionConfigurationTests`, `SavingsProductConfigurationServiceTests`, simulation and approval-policy tests | Automated |
| CFG-006 provider/API-client references | Integration Hub `ProviderAdministrationTests`; no secret-value fields | Automated |
| ARC-003 replay | inbox/outbox replay tests plus `InstitutionProvisioningRepairTests` | Automated |
| Privileged repair audit | append-only model/policy tests and migration `20261026084852` | Automated |
| Operational response | provisioning, publication and business-date runbooks | Documented |

## Acceptance cases

INS-001 through INS-016 and INS-018 have implementation-level automated coverage distributed across
the test classes above. INS-017 (retention/residency and legal hold) remains gated by the explicit
Legal/Compliance policy decision and is not implemented or claimed. Environment-level broker,
PostgreSQL concurrency, backup/restore and dashboard screenshots remain release evidence to be
captured in the target environment.

## Safety evidence

- Partial provisioning stops at `provisioning` or `provisioning_failed`; fully acknowledged runs
  stop at `pending_activation` until a separate protected activation.
- Repair accepts only incomplete pending boundaries, keeps the original event identity and refuses
  failed downstream boundaries with `downstream_repair_required`.
- Repair requires a stable capability, step-up authentication, a reason, idempotency and a trusted
  institution context.
- Repair audit evidence is source-owned, tenant-filtered, append-only in the runtime model and
  contains references rather than event payloads or secret material.
- Institution metrics use bounded, identifier-free measurements for incomplete count, failure count,
  oldest age, repair requests and repair rejections.

## Remaining release gates

- Human Security review of tenant isolation and credential-reference handling.
- Product/Compliance approval of retention, residency, country-pack and suspension semantics.
- Operations/Finance approval of business-date timezone/cut-off automation.
- Production secret manager/HSM choice and target-environment verification of runtime grants.
- Target-environment migration, broker replay, concurrent-command, backup/restore and telemetry
  redaction evidence.
- PostgreSQL contention tests for INS-002, INS-004 and INS-005; the current CoreLedger suites provide
  model-level and unit-level evidence only.

## Verification run 2026-10-28

Commands and results from a local re-verification of the claims above. This run does not change the
status: the slices remain implemented and awaiting human acceptance.

| Check | Command | Result |
| --- | --- | --- |
| Solution build | `dotnet build CoreBanking.sln` | Pass, 0 warnings, 0 errors |
| Institution unit and architecture | `dotnet test` CoreLedger unit and ArchitectureTests | 101 and 15 passed |
| Migrations and database suites | `scripts/db-verify.ps1` | Pass: migrations applied for all nine services; CustomerOnboarding 11, IdentityAccess 48 and AuditReconciliation 2 database tests passed |
| Principal privileges | `scripts/db-verify.ps1` security phase | 54 passed |

Gap found: CoreLedger publishes no `Category=Database` or `Category=Integration` tests, so INS-002
(concurrent duplicate institution code), INS-004 (boundary acknowledgement) and INS-005
(service-unavailable retry) currently have model-level or unit-level evidence only. PostgreSQL
contention coverage is still required before those concurrency claims can be called verified.

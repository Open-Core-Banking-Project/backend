# P1-E04 slice 15 evidence plan

Slice 15 is the only implementation slice of the epic that asserts results rather than code: security, performance,
backup/restore and Phase 1 end-to-end evidence. It cannot be claimed from unit tests alone, and two of its inputs do not
exist in this environment:

- the PostgreSQL suites, migration application and concurrency proofs need `scripts/db-verify.ps1` and the containerised
  stack;
- the capacity, latency, RTO and RPO figures need `P1-E04-D16`, which is still open.

Nothing in this document changes a slice status beyond `in progress`, and no figure is quoted that was not observed.

## What runs today

`scripts/phase1-evidence.ps1` collects every reproducible, database-free result into `artifacts/`:

| Step | Command | What it proves |
| --- | --- | --- |
| Repository gate | `scripts/verify.ps1` | restore, Release build, migration-model drift for all nine contexts, non-database suites, formatting |
| Per-service suites | `dotnet test <project> --filter Category!=Database&Category!=Integration` | one recorded Passed/Failed count per service instead of one solution-wide figure |
| Permission grammar | literal scan of `PermissionIdentifier.Parse` | every capability is exactly `domain.resource.action`; the current report shows 59 capabilities and no exception |
| Supply chain | `dotnet list package --vulnerable --include-transitive` | vulnerable direct and transitive packages, or an explicit `unknown` when the feed cannot be reached |

The first run of the harness (commit `90d6296`) recorded 325 Core Ledger, 198 Identity, 169 Customer Onboarding,
29 Workflow, 11 Integration Hub, 7 Audit Reconciliation and 6 Channels Engagement non-database tests passing, and recorded
the supply-chain scan as `unknown` because `api.nuget.org` timed out from this workstation. That is the honest result: the
harness reports what it could not observe rather than printing a clean scan.

## Acceptance criteria as they stand

`ACC-001` to `ACC-034` in the epic traceability table are grouped here by what currently proves them. "Unit-proven" means
a database-free test fails if the behaviour regresses; it does not mean the deployed system was observed.

| Criteria | Status | Evidence or gap |
| --- | --- | --- |
| ACC-001, ACC-006, ACC-007 | Unit-proven | typed product families, deterministic calculation traces and explicit missing-input failures in `ProductEligibilitySimulationTests` and `ProductsAccountsContractTests` |
| ACC-002, ACC-005 | Partial | simulate/approve/publish and immutability are unit-proven; the exact-version publication and audit/outbox write need the database suite |
| ACC-003, ACC-018, ACC-030 | Unit-proven | maker self-approval denial by stable actor id, control self-approval denial and mass-assignment rejection |
| ACC-004, ACC-012, ACC-019 | Needs the stack | overlap, concurrent reservation and concurrent control outcomes are PostgreSQL constraints and races; only `scripts/db-verify.ps1` can evidence them |
| ACC-008 | Unit-proven | the Phase 2 term-deposit capability is unavailable despite schema vocabulary |
| ACC-009, ACC-010, ACC-011, ACC-013, ACC-014, ACC-016 | Partial | eligibility, refusal, idempotency and non-postable-state behaviour are unit-proven in `AccountApplicationTests` and `AccountApprovalTests`; tenant isolation over real rows needs the database suite |
| ACC-015 | Unit-proven | `LedgerAccountRecord` has no amount, balance or posting column, asserted reflectively |
| ACC-017, ACC-021, ACC-025 | Unit-proven | deny-dominant precedence, stale-version review and the closed-account refusal in `AccountControlTests` and `AccountClosureTests` |
| ACC-020 | Unit-proven | lien immutability, ordering and release rules in `AccountLienTests` |
| ACC-022, ACC-023 | Unit-proven | business-date assessment, notice refusal and reactivation preconditions in `AccountDormancyTests` |
| ACC-024 | Unit-proven | closure refuses an unsettled or restricted account with the approved codes |
| ACC-026 | Partial | `AccountStatementTests` proves a summary carries the ledger answer or no balances at all; a live ledger answer needs the stack |
| ACC-027, ACC-029 | Partial | the capability grammar and the absence of any non-authoritative statement source are unit-proven; the deployed 403 needs the stack |
| ACC-028 | Blocked | the ledger posted-history query is `P1-E05`, so the request is refused with `statement_source_unavailable` |
| ACC-031 | Partial | contracts expose only masked numbers and control reasons are not returned; log and event inspection needs a running stack |
| ACC-032, ACC-033, ACC-034 | Blocked | trace propagation, restore/replay and the end-to-end journey need the stack, and ACC-033 also needs the `P1-E04-D16` RTO/RPO targets |

## What must happen for slice 15 to be claimed

1. Start the stack (`scripts/infra.ps1 -Action up`) and run `scripts/db-verify.ps1`, which applies every migration with the
   migration principal and runs the database and integration suites as the application principal.
2. Approve `P1-E04-D16`: account volume, contention profile, latency budget, RTO and RPO. Without those numbers a
   performance or recovery result has no pass condition and must not be written down.
3. Resolve `P1-E04-D14` for the existing-account product migration, which the end-to-end journey touches.
4. Re-run `scripts/phase1-evidence.ps1` on the final commit so the recorded counts belong to the acceptance commit, and
   record the database, performance and recovery results beside it.

Until all four are done, the slice stays `in progress` and the epic stays below `Implemented`.

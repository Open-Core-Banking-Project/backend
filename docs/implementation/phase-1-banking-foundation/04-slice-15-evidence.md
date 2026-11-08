# P1-E04 slice 15 evidence plan

Slice 15 is the only implementation slice of the epic that asserts results rather than code: security, performance,
backup/restore and Phase 1 end-to-end evidence. It cannot be claimed from unit tests alone. On 2026-11-08 the
containerised stack was available and `scripts/db-verify.ps1` was run for the first time against the account slices, so
the PostgreSQL, concurrency and effective-privilege results below are observed rather than assumed.

Two inputs are still missing, so the slice stays `in progress`:

- the capacity, latency, RTO and RPO figures need `P1-E04-D16`, which is still open, and without them a performance or
  recovery result has no pass condition;
- the end-to-end Phase 1 journey touches `P1-E04-D14` (existing-account product migration), which is still open.

Nothing in this document changes a slice status beyond `in progress`, and no figure is quoted that was not observed.

## What runs today

`scripts/phase1-evidence.ps1` collects every reproducible, database-free result into `artifacts/`:

| Step | Command | What it proves |
| --- | --- | --- |
| Repository gate | `scripts/verify.ps1` | restore, Release build, migration-model drift for all nine contexts, non-database suites, formatting |
| Database gate | `scripts/db-verify.ps1` | migrations applied with each migration principal, database and integration suites as each application principal, effective privileges per schema |
| Per-service suites | `dotnet test <project> --filter Category!=Database&Category!=Integration` | one recorded Passed/Failed count per service instead of one solution-wide figure |
| Permission grammar | literal scan of `PermissionIdentifier.Parse` | every capability is exactly `domain.resource.action`; the current report shows 59 capabilities and no exception |
| Supply chain | `dotnet list package --vulnerable --include-transitive` | vulnerable direct and transitive packages, or an explicit `unknown` when the feed cannot be reached |

### Database evidence, 2026-11-08

`scripts/db-verify.ps1` passed end to end: Release build with 0 warnings and 0 errors, all nine contexts migrated by
their migration principal, and these database results:

| Suite | Result |
| --- | --- |
| Core Ledger | 30 passed — the suite did not exist before the 2026-11-08 runs |
| Customer Onboarding | 102 passed |
| Identity and Access | 105 passed |
| Operations Workflow | 6 passed |
| Audit and Reconciliation | 2 passed |
| Effective privileges for every application principal | 54 passed |
| Payments and VTU, Channels and Engagement, Risk Reporting, Integration Hub | no database test exists yet, and the harness reports that explicitly instead of a zero |

The Core Ledger suite (`src/Services/CoreLedger/tests/CoreBanking.CoreLedger.UnitTests/Persistence`, tagged
`Category=Database`, connected as `coreledger_app`) proves, against stored rows:

- an application is created and evaluated against the persisted product version, publication and reference facts, and
  the evaluation evidence keeps the product version, content hash, fact versions, rule trace and decision;
- a replayed create returns the recorded result and leaves exactly one application row;
- submitting an eligible application binds the exact application version and eligibility snapshot hash, writes
  `approval_requested` to the outbox and records audit evidence;
- another institution sees neither the application nor its evaluation, and its evaluation attempt answers
  `account_application_not_found` while the row still exists for its owner;
- opening an approved application reserves exactly one number and creates exactly one ledger account with a status
  history row and an `account_opened` outbox fact whose payload contains no full account number;
- a replayed opening returns the same account and does not allocate a second number;
- six concurrent openings receive exactly the numbers `00000001`–`00000006` with no duplicate and no gap, leaving the
  reservation at next value 7;
- opening an application that is not approved is refused with `account_application_not_approved` and persists no account
  and no reservation;
- a typedly scoped restriction denies a debit inside its channel scope, allows the same debit outside that scope and
  allows a credit, and the decision names the denying control;
- an evaluation that presents a stale control version is answered with `review` and `control_version_stale` instead of a
  decision, and reports the current version;
- a regulatory freeze evaluates to `review`, refuses a release without an approval reference with
  `account_control_release_requires_approval`, and releases with one;
- four concurrent control changes on one account receive four distinct control versions;
- a lien records an immutable claimed amount with a separate outstanding amount and the ledger availability position,
  an uncovered lien needs the explicit approver acknowledgement, and a partial release appends a movement rather than
  editing the claim;
- a release above the outstanding amount is refused with `account_lien_over_release` and appends no movement;
- a statutory lien release requires an approval reference and an actor other than the maker;
- an account summary carries the ledger answer with its source version, or `ledger_unavailable` with no balances at all;
- a statement request the ledger cannot answer is stored as `refused` with `statement_source_unavailable` and answers
  with no artifact reference, a request outside the published policy is refused and stores nothing, and an answered
  request records the ledger query and integrity references with its `statement_requested` fact;
- a dormancy assessment below the threshold records `NotDormant` against the institution business date and leaves an
  active account unchanged, while an assessment past the threshold records `Dormant`, moves the account to dormant and
  appends the status fact;
- reactivation of that dormant account returns it to active, and a repeated assessment of one business date returns the
  recorded assessment instead of writing a second;
- a missing dormancy rule is recorded as `NotAssessable` rather than assumed, and the account stays active;
- a settled account closes only through proposal → approval → close, and a replayed proposal records the refusal codes
  while submission is refused when the conditions were never met;
- a ledger answer outside the 60-second freshness ceiling is refused with `balance_source_stale`;
- approval with a balance that moved after submission leaves the account in `pending_closure` with the refusal recorded,
  and only a settled re-check closes it.

### Defects this run found and fixed

The database suite was written because the account slices had no runtime proof. Running it exposed three defects that
unit tests over the EF model could not see:

1. **The `account` schema had no privileges.** `coreledger_app` was granted `USAGE` on `institution`, `product`,
   `integration` and `audit` only, and the account migrations created a fifth schema afterwards. Every account read and
   write would have failed at runtime while the model-level suites passed. Fixed by
   `20261108093058_AddAccountSchemaPrivileges` and by adding `account` to the Core Ledger entry of the privilege-test
   catalogue, so the 54-test security suite now covers it.
2. **No account application could be evaluated.** `ProductEligibilityEvaluator` asked the residency criterion to decide
   on an absent fact, and the criterion threw instead of answering, so an unrestricted product rule failed with
   `ArgumentException`. Publication already refuses a residency criterion, so every published product hit this path.
   Fixed with `EligibilityCodeCriterion.AllowsOptional`: an absent optional fact satisfies an unrestricted rule, never
   satisfies an allow-list, and the existing `residency_fact_unavailable` guard still fails closed. Four unit tests
   cover the criterion and both recheck outcomes.
3. **Concurrent first openings lost their numbers.** When two openings competed to create a scheme's reservation row,
   the loser failed with a raw unique-violation instead of taking the next available number. Fixed by detaching the
   rejected insert and reading the winner's row under the same `SELECT ... FOR UPDATE`, so concurrent openings
   serialise and every caller still receives a number. The six-way contention test above is the guard.
4. **Every control, dormancy, closure and summary call failed at runtime.** All four services computed the current
   control version with `Select(...).DefaultIfEmpty(0).MaxAsync()`, which has no relational translation in EF Core 10,
   so applying a control, evaluating one, reading a summary, assessing dormancy and proposing a closure all threw
   `InvalidOperationException` before touching a row. Replaced by one shared
   [AccountControlVersionReader](/Users/USER/Documents/Projects/Core banking project/backend/src/Services/CoreLedger/src/CoreBanking.CoreLedger.Infrastructure/Accounts/AccountControlVersionReader.cs)
   that uses a SQL `MAX` over a nullable projection. The control, lien, summary and statement tests above are the guard.
5. **Concurrent control changes shared one control version.** The next version was read as `MAX + 1` without any lock,
   so four simultaneous restrictions all received version 1 and the stale-version check could no longer detect a change.
   Fixed by taking the account row `FOR UPDATE` inside the control transaction, which serialises control changes per
   account. The four-way contention test above is the guard.

### Repository gate, 2026-11-08

776 non-database tests passed across 10 projects (Core Ledger 329, Identity 198, Customer Onboarding 169, Workflow 29,
Architecture 15, Integration Hub 11, Audit and Reconciliation 7, Payments and VTU 6, Risk Reporting 6, Channels and
Engagement 6) with 0 failures; migration-model drift is consistent for all nine contexts and
`dotnet format --verify-no-changes` passes. `scripts/verify.ps1` itself cannot complete on this workstation because
`dotnet restore` fails its vulnerability audit when `api.nuget.org` is unreachable, so the same steps were run
individually with `-p:NuGetAudit=false --ignore-failed-sources` (and `dotnet format --no-restore`), exactly as
`scripts/db-verify.ps1` and `scripts/phase1-evidence.ps1` already do for restore.

The first run of the harness (commit `90d6296`) recorded 325 Core Ledger, 198 Identity, 169 Customer Onboarding,
29 Workflow, 11 Integration Hub, 7 Audit Reconciliation and 6 Channels Engagement non-database tests passing, and recorded
the supply-chain scan as `unknown` because `api.nuget.org` timed out from this workstation. That is the honest result: the
harness reports what it could not observe rather than printing a clean scan.

## Acceptance criteria as they stand

`ACC-001` to `ACC-034` in the epic traceability table are grouped here by what currently proves them. "Unit-proven" means
a database-free test fails if the behaviour regresses. "Database-proven" means a `Category=Database` test running as the
application principal against the local PostgreSQL stack fails if the behaviour regresses. Neither means the production
system was observed.

| Criteria | Status | Evidence or gap |
| --- | --- | --- |
| ACC-001, ACC-006, ACC-007 | Unit-proven | typed product families, deterministic calculation traces and explicit missing-input failures in `ProductEligibilitySimulationTests` and `ProductsAccountsContractTests` |
| ACC-002, ACC-005 | Partial | simulate/approve/publish and immutability are unit-proven, and the approval submission now proves the exact application version and eligibility snapshot hash with its `approval_requested` outbox fact and audit row in PostgreSQL; the exact-version publication itself is still unit-proven only |
| ACC-003, ACC-018, ACC-030 | Unit-proven | maker self-approval denial by stable actor id, control self-approval denial and mass-assignment rejection |
| ACC-004, ACC-012 | Database-proven | six concurrent openings on one scheme receive `00000001`–`00000006` with no duplicate and no gap, and the reservation row ends at next value 7 |
| ACC-019 | Database-proven | four concurrent control changes on one account receive four distinct control versions, and a stale known version is answered with `review` + `control_version_stale` |
| ACC-008 | Unit-proven | the Phase 2 term-deposit capability is unavailable despite schema vocabulary |
| ACC-009, ACC-010, ACC-011, ACC-013, ACC-014 | Database-proven | eligibility evaluation, replay of create and open, the refusal of a non-approved application and tenant isolation over stored rows |
| ACC-016 | Unit-proven | the account is opened with no balance, journal line or hold; the ledger handoff is asserted in the database suite |
| ACC-015 | Unit-proven | `LedgerAccountRecord` has no amount, balance or posting column, asserted reflectively and exercised by the opening suite |
| ACC-017, ACC-021, ACC-025 | Database-proven for 017 and 021 | the deny-dominant decision and the stale-version review run over stored control rows; the closed-account refusal is still unit-proven only |
| ACC-020 | Database-proven | immutable claimed amount, separate outstanding, append-only partial release, over-release refusal and the dual-control statutory release over stored lien rows |
| ACC-022, ACC-023 | Database-proven | business-date assessment, the not-assessable outcome, the dormant transition and reactivation over stored rows, plus the unit-level notice refusal in `AccountDormancyTests` |
| ACC-024 | Database-proven | closure refuses an unsettled or stale-condition account with the approved codes, and a settled account closes only through proposal → approval → re-check |
| ACC-026 | Partial | the database suite proves a summary carries the ledger answer or no balances at all, and a statement the ledger cannot answer is refused with `statement_source_unavailable`; a live ledger answer is `P1-E05` and has no source yet |
| ACC-027, ACC-029 | Partial | the capability grammar, the policy refusals and the artifact authorisation that answers with no reference for a refused request are proven; the deployed 403 needs a running API host |
| ACC-028 | Blocked | the ledger posted-history query is `P1-E05`, so the request is refused with `statement_source_unavailable` (now asserted against a real request row) |
| ACC-031 | Partial | contracts expose only masked numbers and control reasons are not returned, and the opening suite proves the outbox payload excludes the full number; log and live event inspection still needs a running host |
| ACC-032, ACC-033, ACC-034 | Blocked | trace propagation, restore/replay and the end-to-end journey need a running host, and ACC-033 also needs the `P1-E04-D16` RTO/RPO targets |

## What must happen for slice 15 to be claimed

1. ~~Start the stack and run `scripts/db-verify.ps1`.~~ Done on 2026-11-08; repeat it on the acceptance commit.
2. Approve `P1-E04-D16`: account volume, contention profile, latency budget, RTO and RPO. Without those numbers a
   performance or recovery result has no pass condition and must not be written down.
3. Resolve `P1-E04-D14` for the existing-account product migration, which the end-to-end journey touches.
4. ~~Give the account controls, liens, dormancy and statement slices their own `Category=Database` evidence.~~ Every
   account slice now has it: application, opening, approval, controls, liens, dormancy, reactivation, closure, summaries
   and statements.
5. Re-run `scripts/phase1-evidence.ps1` on the final commit so the recorded counts belong to the acceptance commit, and
   record the database, performance and recovery results beside it.

Until the remaining steps are done, the slice stays `in progress` and the epic stays below `Implemented`.

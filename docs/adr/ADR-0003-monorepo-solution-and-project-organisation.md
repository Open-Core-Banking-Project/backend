---
id: ADR-0003
title: "Monorepo, solution and project organisation"
status: Accepted
date: 2026-10-16
owners: [Architecture, Platform Engineering]
required_by: "Phase 0 - Repository and Build Foundation, slice 1"
architecture_item: 3
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/01-repository-and-build.md
supersedes: []
approved_by: [Architecture, Platform Engineering]
approved_on: 2026-10-16
---

# ADR-0003: Monorepo, solution and project organisation

## Status

`Accepted` 2026-10-16. The solution, central build policy, analyzer policy, formatting gate and test conventions are binding, and `01-repository-and-build.md` may now create the solution, central build files and project templates.

Approval recorded on the instruction of the repository owner (`adamsondamilola <adamsondamilola@gmail.com>`) against the owners named in the front matter. Replace the role names with individual approver identities when the approval record is formalised.

## Context

- `01-repository-and-build.md` requires `CoreBanking.sln`, central package versions, shared compiler and analyzer settings, `.editorconfig` rules, architecture tests and documented restore/build/test/format commands, and records `.sln` format, analyzer policy and test conventions as open decisions.
- `BACKEND_ARCHITECTURE.md` (`## Repository structure`, `## Per-microservice structure`) already fixes the target tree, the per-service project set and the project responsibility table.
- `AGENTS.md` (`## Architecture`, `## C# coding standards`) requires the `CoreBanking.<Context>.<Layer>` shape, the stated dependency direction, nullable reference types, no generic repository or generic service layer, and `.editorconfig` conformance.
- Requirement and story traceability: `ARC-001`, `ARC-004`, `ARC-009`, `NFR-MNT-001`, `US-079`.

## Decision

1. **One repository, one solution.** A single `backend/` monorepo with a single root `CoreBanking.sln` that includes every production, building-block and test project. The classic `.sln` format is used; `.slnx` and per-service solutions are not adopted, and no second solution file is added without amending this ADR.
2. **Top-level layout.** Exactly as specified in `BACKEND_ARCHITECTURE.md`: root files (`global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `CoreBanking.sln`), then `src/BuildingBlocks/`, `src/Services/`, `tests/`, `docs/`, `deploy/`, `scripts/` and `.github/`. A new top-level directory requires a durable, documented purpose.
3. **Building blocks.** The five approved projects `CoreBanking.BuildingBlocks.Domain`, `.Application`, `.Infrastructure`, `.Observability` and `.Testing`. They must not reference any service project, and must contain only the domain-neutral content permitted by the architecture document's building-block policy.
4. **Service projects.** Per service: `CoreBanking.<Service>.Domain`, `.Application`, `.Infrastructure`, `.Contracts`, `.Api`, plus `.Workers` only where the service needs background processing. Tests per service: `CoreBanking.<Service>.UnitTests`, `.IntegrationTests`, `.ContractTests`. The five repository-level test projects are `CoreBanking.ArchitectureTests`, `.EndToEndTests`, `.ContractTests`, `.PerformanceTests` and `.SecurityTests`.
5. **Naming.** Namespaces mirror project names. Directories use the short service names from the architecture document (`CoreLedger`, `CustomerOnboarding`, ...). No abbreviations that obscure banking meaning.
6. **Central build policy** in `Directory.Build.props`, applied to every project:

| Setting | Value |
| --- | --- |
| `TargetFramework` | The pinned SDK's TFM from [ADR-0001](ADR-0001-csharp-dotnet-only-backend-and-sdk-policy.md) |
| `Nullable` | `enable` |
| `ImplicitUsings` | `disable` - explicit usings everywhere so files stay readable in review |
| `TreatWarningsAsErrors` | `true` locally and in CI, so local and pipeline results agree |
| `AnalysisMode` | `Recommended` using the .NET SDK built-in analyzers only |
| `EnforceCodeStyleInBuild` | `true` |
| `Deterministic` | `true` |
| `ContinuousIntegrationBuild` | `true` when the CI environment variable is set |
| `ManagePackageVersionsCentrally` | `true` in `Directory.Packages.props`, exact versions only |

7. **Analyzer policy.** Phase 0 uses only the SDK-provided analyzers at the level above. Adding a third-party analyzer or analyzer package requires an amendment to this ADR that states the rule, its severity and the migration cost. `<NoWarn>` entries are forbidden in `Directory.Build.props`; a narrowly scoped suppression in a project or file requires a linked work item and an expiry, per the rule in `01-repository-and-build.md`.
8. **Formatting.** `.editorconfig` is the single source of truth for formatting and naming. `dotnet format --verify-no-changes` is a required CI check; no separate formatter configuration is introduced.
9. **Test conventions.** One test class per subject under test. Method names state behaviour as `MethodUnderTest_Scenario_ExpectedOutcome`. Every test carries a `Category` trait of `Unit`, `Integration`, `Contract`, `Architecture` or `EndToEnd` so CI can run targeted selections. Integration and contract tests use Testcontainers against real PostgreSQL, Redis and broker containers; in-memory providers are not accepted as proof of persistence, locking or constraints. Tests control time and identifiers and do not rely on sleeps.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| One repository per service | Contradicts `AGENTS.md`; fragments shared build policy, architecture tests and CI |
| `.slnx` or per-service solutions | Extra novelty and tooling risk for no Phase 0 benefit |
| `ImplicitUsings=enable` | Makes dependency usage invisible in review, which matters for domain purity rules |
| Warnings-as-errors in CI only | Allows local and pipeline results to diverge, which the epic explicitly warns about |
| Adopt a third-party analyzer suite now | Unreviewed rule volume and churn before the codebase exists |
| Central package management without exact pinning | Non-reproducible restore |

## Consequences

- **Enables:** a single deterministic restore/build/test/format entry point, mechanical enforcement of the nine-service boundaries, and consistent project shape across services.
- **Constrains:** every later epic inherits these project names, namespaces, test projects and analyzer severities; migrations must be owned by the service's `Infrastructure` project; new top-level areas and new packages are deliberate, reviewed acts.
- **Cost:** explicit usings and a strict test taxonomy add friction; warnings-as-errors everywhere means analyzer-driven fixes are mandatory rather than optional.

## Compliance and evidence

- CI: `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build`, `dotnet format --verify-no-changes` all pass.
- `tests/CoreBanking.ArchitectureTests` failing on a forbidden reference, an out-of-pattern namespace, or a contract exposing EF entities.
- Check that no project declares a package `Version` and that no `<NoWarn>` appears in `Directory.Build.props`.
- Recorded evidence for approval: the tree above, the `Directory.Build.props` content and a clean-checkout build from a fresh clone.

## Open questions

- Whether `CoreBanking.PerformanceTests` and `CoreBanking.SecurityTests` are populated in Phase 0 or only created as empty, non-blocking projects.
- Whether coverage thresholds become a required check, and at what level (deferred to `05-observability-and-ci.md`).
- Whether a `.github/pull_request_template.md` is required by this ADR or by `CONTRIBUTING.md` alone.

## Approval gate

Satisfied 2026-10-16. The solution, central build files, analyzer configuration and project templates may now be created. The package-management scope question recorded in the [index](README.md) mapping table should be closed by an amendment if a separate ADR is preferred.

## Related documents

- [ADR index](README.md)
- [ADR-0001](ADR-0001-csharp-dotnet-only-backend-and-sdk-policy.md) - SDK pin consumed by the build policy
- [ADR-0002](ADR-0002-nine-initial-microservices-and-progressive-decomposition.md) - the service boundaries the layout expresses
- [BACKEND_ARCHITECTURE.md](../architecture/BACKEND_ARCHITECTURE.md) - `## Repository structure`, `## Per-microservice structure`
- [01-repository-and-build.md](../implementation/phase-0-engineering-foundation/01-repository-and-build.md)

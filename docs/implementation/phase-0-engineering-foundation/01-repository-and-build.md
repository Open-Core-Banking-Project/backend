---
phase: "Phase 0 - Engineering Foundation"
epic: "Repository and Build Foundation"
owning_service: "Platform-wide"
modules: [Build, Repository Structure, Architecture Tests]
status: In progress
requirements: [ARC-001, ARC-004, ARC-009, NFR-MNT-001]
stories: [US-079]
dependencies: []
adrs: [ADR-0001, ADR-0002, ADR-0003]
last_updated: 2026-10-16
---

# Epic: Repository and Build Foundation

Approved 2026-10-16 on the instruction of the repository owner, after ADR-0001, ADR-0002 and ADR-0003 were accepted. Ready to implement.

Implementation progress: slices 2 to 4 are complete on `devops/phase-0-repository-and-build` - root build and configuration files, the solution, the nine services with their standard projects, the building-block projects and the approved project references. Slices 5 to 7 remain: test projects with the architecture tests, the build and test scripts, and CI-ready deterministic verification.

## Objective

Create a deterministic C#/.NET monorepo foundation that exposes the nine microservice boundaries, applies one set of engineering standards, and prevents invalid dependencies before feature development begins.

## Included scope

- Pin the supported .NET SDK.
- Create `CoreBanking.sln` and central build/package configuration.
- Create the nine service directories and standard project boundaries.
- Create small domain-neutral building-block projects only where immediately required.
- Enable nullable analysis, analyzers, deterministic builds and formatting.
- Create architecture-test and test-utility foundations.
- Document restore, build, test and format commands.

## Excluded scope

- Business entities, APIs and database tables beyond minimal compile/test fixtures.
- Production infrastructure.
- Provider SDKs.
- Banking feature implementation.

## Required structure

Follow `docs/architecture/BACKEND_ARCHITECTURE.md`. Each service receives Domain, Application, Infrastructure, Contracts and API projects. Add a Workers project only when Phase 0 messaging work requires it.

The repository must not reference one service's Domain, Application or Infrastructure project from another service. Cross-service compile-time sharing is limited to deliberately public contracts and domain-neutral building blocks.

## Central build policy

Configure:

- Repository-pinned SDK through `global.json`.
- Central package versions through `Directory.Packages.props`.
- Shared compiler and analyzer settings through `Directory.Build.props`.
- Nullable reference types.
- Deterministic builds.
- Warnings as errors in CI.
- Consistent language version selected by ADR.
- Source-link and reproducible package metadata if packages are produced.
- `.editorconfig` formatting and naming rules.

Do not suppress a warning globally to unblock one project. Fix it or document and narrowly scope an approved suppression.

## Architecture tests

At minimum, automatically enforce:

- Domain projects do not reference Infrastructure, API, ASP.NET Core, EF Core or messaging/provider packages.
- Application projects do not reference concrete Infrastructure.
- Contracts do not reference EF entities or private domain implementations.
- APIs do not reference another service's Infrastructure.
- No service references another service's database or migration assembly.
- Only approved public contracts and building blocks may cross service boundaries.
- Project and namespace naming follows the documented pattern.

## Implementation slices

1. Approve SDK, solution and package-management ADRs.
2. Add root build/configuration files.
3. Create empty standard projects for all nine services.
4. Add project references in the approved dependency direction.
5. Add test projects and architecture tests.
6. Add build/test/format scripts or documented commands.
7. Add CI-ready deterministic verification.

## Tests and evidence

- Clean restore and build.
- All test projects execute successfully.
- Architecture tests fail when an intentional forbidden reference is introduced, then pass after it is removed.
- Formatting verification passes.
- No application package has an unapproved version outside central package management.
- A clean checkout reproduces the result.

## Acceptance criteria

- [ ] The pinned SDK is documented and used.
- [ ] `CoreBanking.sln` contains the approved projects.
- [ ] Nine service boundaries are visible.
- [ ] Central build and package policies apply to all projects.
- [ ] Architecture tests enforce dependency direction.
- [ ] Restore, build, test and formatting commands pass.
- [ ] No banking behaviour or speculative framework has been introduced.

## Open decisions

- Exact .NET SDK version and upgrade cadence.
- Analyzer packages and severity policy.
- `.sln` versus any future solution format.
- Test naming and categorisation conventions.

## Agent instructions

- Read the repository tree before scaffolding and preserve existing files.
- Do not generate application controllers, entities or migrations in this epic.
- Do not add packages merely because a template includes them.
- Report exact projects created and verification commands executed.


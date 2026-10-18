---
id: ADR-0001
title: "C#/.NET-only backend and SDK support policy"
status: Accepted
date: 2026-10-16
owners: [Architecture, Platform Engineering]
required_by: "Phase 0 - Repository and Build Foundation, slice 1"
architecture_item: 1
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/01-repository-and-build.md
supersedes: []
approved_by: [Architecture, Platform Engineering]
approved_on: 2026-10-16
---

# ADR-0001: C#/.NET-only backend and SDK support policy

## Status

`Accepted` 2026-10-16. All production backend code targets the current .NET LTS line as pinned in section 2, and `01-repository-and-build.md` may now commit `global.json`, the language baseline and container base images that encode the pinned SDK.

Approval recorded on the instruction of the repository owner (`adamsondamilola <adamsondamilola@gmail.com>`) against the owners named in the front matter. Replace the role names with individual approver identities when the approval record is formalised.

## Context

- `AGENTS.md` (`## Technology baseline`) requires all production backend code to use C# and .NET, requires the repository-pinned SDK and centrally managed package versions, and forbids introducing Java, Spring Boot, Node.js, NestJS, Python, FastAPI or another backend runtime without an approved architecture decision and an explicit policy change.
- `README.md` (`## Technology strategy`) fixes the standard stack: repository-pinned supported .NET SDK, nullable reference types, ASP.NET Core, EF Core with PostgreSQL, OpenAPI, OpenTelemetry, Kafka-compatible messaging, Redis for approved non-authoritative use, xUnit and Testcontainers.
- `01-repository-and-build.md` requires a repository-pinned SDK through `global.json`, deterministic builds, warnings as errors in CI, and records "Exact .NET SDK version and upgrade cadence" as an open decision.
- Repository state: no `global.json`, `Directory.Build.props`, `Directory.Packages.props` or solution file exists yet. The development machine currently has SDKs 8.0.413, 8.0.416 and 9.0.304 installed, none of which is the line selected below.
- Requirement and story traceability: `ARC-001`, `ARC-004`, `ARC-009`, `NFR-MNT-001`, `US-079`.

## Decision

1. **Language and runtime floor.** All production backend code targets the current .NET **LTS** line, currently .NET 10 (`net10.0`). Short-term-support (odd-numbered) releases are never pinned for production code. The backend is C#-only: no Java, Spring Boot, Node.js, NestJS, Python, FastAPI or other runtime, and no polyglot exception is granted by this ADR.
2. **Pin.** The exact SDK version is recorded in a root `global.json`:

```json
{
  "sdk": {
    "version": "<pinned 10.0.1xx version>",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

`latestPatch` keeps the feature band stable while accepting servicing patches. `allowPrerelease` is `false` everywhere, including CI.
3. **Language version.** `LangVersion` is not overridden in any project; the default for the pinned SDK applies. Preview language features are prohibited. `TargetFramework` is `net10.0` for production projects and test projects alike; multi-targeting is not permitted without amending this ADR.
4. **Package management.** Central package management is mandatory (`ManagePackageVersionsCentrally=true` with `Directory.Packages.props`). No project file declares a package `Version`. Package versions are pinned exactly, not floating.
5. **Upgrade cadence.**
   - Servicing and security patches within the pinned band: applied within 7 days of release; security patches treated as priority.
   - Feature-band changes: one pull request that updates `global.json`, CI runner images and container base images together, with the full suite green.
   - New LTS adoption: within one release cycle of general availability, with regression evidence and a recorded decision.
6. **Enforcement.** CI asserts that the SDK reported by `dotnet --version` equals the pinned version; a build or architecture test fails when a project declares `LangVersion` or a `TargetFramework` outside this policy; container base images use the pinned version tag.
7. **Scope.** This policy covers the backend repository only. The three frontend repositories are out of scope.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Pin .NET 9 (installed locally) | Odd-numbered short-term-support line; unsuitable as the long-term floor for a banking platform |
| Pin .NET 8 (LTS, installed locally) | Late in its support window; adopting it as the floor now guarantees an imminent forced upgrade |
| No pin, use the newest installed SDK | Fails the deterministic and repeatable build requirement and makes CI non-reproducible |
| Multi-target production projects | Doubles build, analyzer and test cost with no approved consumer |
| Permit a second runtime for a utility | Would require amending this ADR and the C#-only policy in `AGENTS.md` |

## Consequences

- **Enables:** a deterministic, reproducible build; one analyzer and language baseline for all nine services; a predictable upgrade path.
- **Constrains:** developer machines, CI runners and container base images must all carry the pinned SDK. Neither of the locally installed 8.0 nor 9.0 lines satisfies the pin, so the pinned SDK must be installed before Phase 0 slice 2 can build.
- **Cost:** an LTS upgrade every year, budgeted as planned work rather than a forced emergency.
- **Out of scope:** frontend tooling, database engine versions (see [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md)) and package-level choices beyond central versioning.

## Compliance and evidence

- CI job failure when `dotnet --version` differs from the `global.json` value.
- Build test asserting the pinned `TargetFramework`, no `LangVersion` override, and no package `Version` attribute in project files.
- Recorded evidence for approval: `dotnet --list-sdks` output from a developer machine and a CI runner, plus a vendor lifecycle check stating the end-of-support date of the selected line.
- Warnings-as-errors and deterministic-build settings verified by review of `Directory.Build.props`.

## Open questions

- The exact patch version to record, which must be confirmed against an installed SDK when `global.json` is first committed.
- Whether a developer-local second SDK is permitted for tooling, or whether the pin is mandatory on all machines.
- Whether preview SDKs may be used in a time-boxed evaluation branch without merging.

## Approval gate

Satisfied 2026-10-16. `global.json`, `LangVersion` settings, container base images and CI images that encode an SDK choice may now be committed. The exact patch version must be confirmed against an installed SDK when `global.json` is first committed, and the pinned line must be installed on developer machines and CI runners.

## Related documents

- [ADR index](README.md)
- [ADR-0003](ADR-0003-monorepo-solution-and-project-organisation.md) - build, package and analyzer policy that consumes the pin
- [AGENTS.md](../../AGENTS.md) - `## Technology baseline`
- [README.md](../../README.md) - `## Technology strategy`, `## Local development requirements`
- [01-repository-and-build.md](../implementation/phase-0-engineering-foundation/01-repository-and-build.md)

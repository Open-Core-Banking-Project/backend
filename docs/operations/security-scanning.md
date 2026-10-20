# Security scanning and merge protection

## Scope

Phase 0 uses three independent automated controls:

- GitHub CodeQL scans C# source with the `security-extended` query suite.
- Gitleaks scans the complete Git history for committed secrets and redacts findings in logs.
- `dotnet list package --vulnerable --include-transitive` checks direct and transitive NuGet dependencies.

The security workflow runs for pull requests, pushes to `main`, a weekly scheduled scan and manual
dispatch. All third-party workflow code and the Gitleaks container are pinned to immutable commit or
image digests. Version comments remain beside pins so dependency automation can propose reviewed
upgrades without silently floating production CI code.

## Merge policy

The `main` branch must require these checks before merge:

- `Build, format and test`
- `Infrastructure and database integration`
- `Secret scan`
- `CodeQL (C#)`

Require pull requests, require branches to be up to date before merging, dismiss stale approvals after
new commits and include administrators in enforcement. Direct pushes, force pushes and branch deletion
remain disabled.

## Findings and exceptions

A detected secret is an incident, not merely a build failure. Rotate or revoke the credential first,
then remove it from the visible branch and assess whether history rewriting is required. Never add a
real secret to an allow-list.

A false positive may be suppressed only with a narrow, reviewed rule that identifies synthetic or
public test material. Record the reason, owner and expiry where the exception is not permanent by
design. CodeQL and dependency findings require triage before merge; accepted risk requires a linked,
time-bounded security decision.

## Verification

For every policy change:

1. Run the normal repository verification script.
2. Run Gitleaks against the complete local checkout using the same pinned image digest.
3. Open a pull request and retain successful GitHub Actions evidence for all required checks.
4. Confirm branch protection names exactly match the emitted check names.

CodeQL is verified on GitHub because its analysis service and SARIF upload are platform-managed.

# P1-E02 slice 9 consumer entitlement enforcement review pack

## Outcome

The Identity and Access snapshot contract is now enforced independently by one financial and one non-financial service boundary:

- Core Ledger refreshes entitlement state before the step-up-protected business-date advance operation.
- Operations Workflow refreshes entitlement state before the step-up-protected approval decision operation.

## Controls

- Consumers request the exact entitlement version carried by the authenticated human token.
- Actor and institution identity, exact version, completeness and snapshot freshness are checked before permission evaluation.
- A source outage, non-success response, incomplete response, actor/institution/version mismatch or unsupported scope dimension removes the local projection and results in denial.
- Financial and privileged operations refresh from Identity and Access on every protected request; they do not use a cached allow after source failure.
- The HTTP adapter sends the trusted institution context and consumes only the versioned `EntitlementSnapshotV1` contract.
- The client is configured with a five-second timeout. Workload-token acquisition remains outside the adapter and is the slice-13 provider integration gate.

## Verification

- Core Ledger API build: passed with 0 warnings/errors.
- Operations Workflow API build: passed with 0 warnings/errors.
- Core Ledger non-database suite: 102 passed.
- Operations Workflow non-database suite: 23 passed.
- Focused financial and non-financial source-refresh tests: passed.
- `dotnet format CoreBanking.sln --no-restore --verify-no-changes`: passed.

## Remaining production gate

The deployed HTTP clients still require the platform's audience-restricted workload-token handler and a live provider-issued token. Until configured, the call fails closed and no protected operation is authorised from a missing projection. This is tracked by P1-E02 slice 13 rather than reopening slice 9.

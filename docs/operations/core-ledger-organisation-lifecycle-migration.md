# Core Ledger organisation lifecycle migration

Migration: `20261019143913_AddInstitutionSuspensionAndOrganisationLifecycle`

Scope: Phase 1 Institution and Tenancy slices 7 and 8.

- Adds institution suspension actor, reason and correlation evidence.
- Creates institution-scoped `regions`, `branches` and append-only `organisation_status_history` tables.
- Enforces unique region and branch codes per institution, valid status values, branch-to-region ownership and optimistic lifecycle concurrency.
- Apply after `20261019134858_AddInstitutionActivationEvidence` and before enabling the suspension or organisation lifecycle endpoints.
- Roll forward on deployment failure. Do not remove lifecycle history after the endpoints have accepted production traffic.

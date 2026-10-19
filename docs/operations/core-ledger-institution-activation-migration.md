# Core Ledger institution activation rollout

Migration: `20261019134858_AddInstitutionActivationEvidence`

Scope: Phase 1 Institution and Tenancy slice 7, CFG-001/CFG-002 and US-001.

## Data impact

- Adds nullable maker and activation actor evidence to `institution.institutions`.
- Adds the activation correlation identifier used to trace the command and `InstitutionActivated.v1` event.
- Adds an optimistic-concurrency version to lifecycle updates.
- Existing rows intentionally receive no synthetic maker identity. They remain ineligible for activation until trusted maker evidence is repaired through an approved process.

## HTTP boundary

- `POST /api/v1/institutions` requires `platform.institution.create`, step-up authentication, `X-Institution-Id`, and `Idempotency-Key`.
- `POST /api/v1/institutions/{institutionId}/activate` requires `platform.institution.activate`, step-up authentication, and `Idempotency-Key`.
- The activation actor must differ from the recorded creation actor.
- Activation succeeds only for a `pending_activation` institution whose mandatory provisioning steps are all `initialised`.
- A successful activation commits the lifecycle transition and `InstitutionActivated.v1` outbox record together.
- Every mandatory service consumes that event through its existing inbox and changes only a matching local `provisioning` boundary to `active`; duplicate delivery returns the recorded inbox outcome.

## Authentication gate

The approved identity-provider product and entitlement-projection delivery remain open ADR-0008/ADR-0009 decisions. Therefore:

- Development may use the synthetic `X-Development-Actor` adapter and local synthetic grants.
- Non-development uses the unconfigured resolver and denies every protected request with `auth.missing_authentication`.
- Never enable the development actor adapter outside the Development environment.

## Rollout

1. Apply all earlier Core Ledger migrations.
2. Back up `core_ledger_db` according to the approved recovery procedure.
3. Apply this migration with the Core Ledger migration principal.
4. Deploy the API and worker with the same Core Ledger database and Kafka environment configuration.
5. Verify pre-existing pending institutions without maker evidence remain blocked.
6. Create a synthetic institution in a non-production environment with a step-up maker identity.
7. Complete all mandatory service acknowledgements, then activate with a distinct step-up approver.
8. Verify the institution, provisioning run, idempotency outcome and activation outbox evidence.
9. Verify every mandatory service records the activation inbox event and exposes an `active` local institution boundary.

## Rollback and forward fix

Rollback is safe only before any activation evidence is written. After activation, retain the columns and event evidence and use an append-only forward-fix migration.

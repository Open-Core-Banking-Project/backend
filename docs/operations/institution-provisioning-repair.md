# Institution provisioning repair

Use this runbook when `corebanking.institution.provisioning.incomplete` or the oldest-incomplete-age
gauge indicates a stuck institution. This procedure replays delivery; it does not override a failed
service boundary or make an institution active.

## Triage

1. Read `GET /api/v1/institutions/{institutionId}/provisioning-runs/{runId}` with the target
   institution context and `platform.provisioning.repair` capability.
2. Compare the returned required services with the deployed provisioning plan. Record the run ID,
   correlation ID and change/incident reference without copying payloads into the ticket.
3. Check the Core Ledger outbox worker, broker health, unpublished backlog and oldest age.
4. If any step is `failed`, stop. The API returns `downstream_repair_required`; the owning service
   must use an approved service-specific recovery before Core Ledger can continue.
5. If only `pending` steps remain and the delivery cause is corrected, obtain step-up authentication
   and call `POST /api/v1/institutions/{institutionId}/provisioning-runs/{runId}/repair` with a fresh
   `Idempotency-Key`, correlation header and bounded reason.

The operation queues the original `institution_provisioning_started` event identity, never creates a
replacement fact, resets delivery attempts, marks matching dead letters repaired and atomically adds
an append-only `audit.institution_audit_events` record.

## Verification

- The response is `202` with `replay_queued`, the original source event ID and a new audit event ID.
- Repeating the same idempotency key and request returns the recorded result with `replayed=true`.
- Each pending service reaches `initialised`; the run reaches `pending_activation`, not `active`.
- `corebanking.institution.provisioning.incomplete` and oldest age fall after acknowledgements arrive.
- The audit row contains actor, permission, action, reason, correlation, run ID and before/after
  references; it contains no envelope, payload or secret value.

Never edit an envelope, acknowledgement, provisioning step or audit row directly. Never use this
operation to clear a `failed` step, bypass a required service, or activate a partial institution.

## Escalation

Escalate a failed boundary to its owning service, repeated cross-tenant denials to Security, a
missing source event to Core Ledger engineering, and an unchanged pending run after confirmed replay
to Platform Messaging. Keep the institution inactive while any prerequisite is unresolved.

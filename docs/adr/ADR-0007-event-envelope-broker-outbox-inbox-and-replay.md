---
id: ADR-0007
title: "Event envelope, broker, outbox/inbox and replay"
status: Accepted
date: 2026-10-16
owners: [Architecture, Platform Engineering]
required_by: "Phase 0 - Messaging and Outbox, slice 1"
architecture_item: 7
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/02-local-infrastructure.md
  - docs/implementation/phase-0-engineering-foundation/04-messaging-and-outbox.md
  - docs/implementation/phase-1-banking-foundation/01-institution-and-tenancy.md
supersedes: []
approved_by: [Architecture, Platform Engineering]
approved_on: 2026-10-16
---

# ADR-0007: Event envelope, broker, outbox/inbox and replay

## Status

`Accepted` 2026-10-16. The envelope, topic and partition conventions, outbox and inbox rules, idempotency scope, retry and dead-letter behaviour, replay procedure and compatibility rules are binding. Integration events may be published and the outbox, inbox, idempotency and dead-letter tables may be committed.

Approval recorded on the instruction of the repository owner (`adamsondamilola <adamsondamilola@gmail.com>`) against the owners named in the front matter. Replace the role names with individual approver identities when the approval record is formalised.

## Context

- `AGENTS.md` (`## Events and messaging`) requires past-tense event names, publication only after the source transaction commits (normally through a transactional outbox), idempotent consumers with inbox or deduplication state, an envelope carrying event ID, type, schema version, occurred time, institution, correlation, causation and business reference, no secrets or unnecessary personal data, ordering only for the smallest required business scope, and defined retry, backoff, dead-letter, replay and poison-message behaviour before a consumer is enabled. Replay must not create duplicate financial outcomes, audit records, notifications or reconciliation breaks.
- `04-messaging-and-outbox.md` fixes the envelope field list, outbox and inbox rules, idempotent-command rules, the reference flow, the required failure scenarios, local table names (`integration.outbox_messages`, `integration.inbox_messages`, `integration.idempotency_records`, `integration.dead_letters`) and the metric set, and records broker, serialization, topic naming, retry, retention and compatibility tooling as open decisions.
- `AGENTS.md` (`## Technology baseline`) fixes Kafka-compatible messaging; `02-local-infrastructure.md` requires a Kafka-compatible local broker selected by ADR.
- `01-institution-and-tenancy.md` depends on the envelope for configuration publication events.
- Requirement and story traceability: `ARC-002`, `ARC-003`, `API-007`, `LED-004`, `LED-005`, `NFR-RES-002`, `US-020`, `US-068`, `US-071`.

## Decision

1. **Delivery model.** At-least-once transport with effectively-once business outcomes enforced by inbox and idempotency state. Exactly-once delivery is not claimed anywhere in code, documentation or tests.
2. **Envelope.** The field list defined in `04-messaging-and-outbox.md` is normative: `event_id`, `event_type`, `schema_version`, `occurred_at`, `institution_id`, `correlation_id`, `causation_id`, `producer`, `business_reference`, `payload`. The envelope is transport-neutral; broker headers are an implementation detail of the shared messaging primitive.
3. **Serialization.** UTF-8 JSON using `System.Text.Json`, with camel-case property names and ISO 8601 timestamps. Payloads are versioned contract types in the producer's `Contracts` project. No schema registry is introduced in Phase 0; the trigger for revisiting this is recorded under Open questions.
4. **Topics and ordering.** Topic name is `corebanking.<service>.<event-family>.v<major>`. The partition key is the business aggregate reference (`business_reference`), giving per-aggregate ordering. Global ordering is never assumed, and consumers must not rely on cross-partition sequence. `event_type` carries the event name in past tense; `schema_version` carries the integer major.
5. **Outbox.** Source state and outbox record commit in one local transaction. Publication is performed by a relay in the owning service's `Workers` project using batched `FOR UPDATE SKIP LOCKED` claims, attempts counter, `next_attempt_at`, and explicit `pending`, `published` and `manual_repair` states. A record is marked published only after the broker acknowledgement required by policy. Retry never recreates the source event.
6. **Inbox and consumption.** The consumer registers the event identity in the same transaction as its business outcome. A repeat delivery returns the recorded outcome or no-ops. Invalid schema versions are rejected to the dead-letter path rather than parsed best-effort. Two consumers receiving the same event concurrently must converge on one outcome.
7. **Idempotent commands.** Every financial command carries a scoped idempotency key covering institution, operation and initiating identity or client. Same key with an equivalent request returns the original result; same key with a materially different request is rejected with a stable conflict error. Records are retained according to approved risk and business policy, never an arbitrary cache timeout. Redis is never the only durable store for authoritative operations.
8. **Retry, dead letter and replay.** Consumer retry uses exponential backoff with jitter and a bounded attempt count, after which the message moves to `integration.dead_letters`, which is visible to operators. Replay uses the original event identity, writes new attempt evidence and cannot create a second business outcome. Only a documented operator procedure may trigger replay, and its execution is audited.
9. **Compatibility.** Within a major version, changes are additive only and consumers ignore unknown fields. A breaking change is a new major version on a new topic with both versions published during a stated transition window. Compatibility is verified by contract tests in `CoreBanking.ContractTests`.
10. **Prohibitions.** No publishing before commit; no domain-specific banking events inside the shared building blocks; no secrets or unnecessary personal data in any envelope or payload; no payload or envelope contents in telemetry beyond identifiers; no distributed transaction.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Publish directly from the request path | Can publish before commit or lose events on failure |
| At-least-once without inbox state | Duplicate financial outcomes and reconciliation breaks on redelivery |
| Avro or Protobuf with a schema registry in Phase 0 | Adds tooling and operational cost before a non-.NET or multi-team consumer exists |
| Topic per event type | Topic explosion with no operational benefit at this scale |
| Global ordering assumption | Not provided by the broker and not required by any consumer |
| Broker-managed retry only, no application dead-letter table | Loses the operator-visible repair path the epic requires |
| Exactly-once semantics via transactions or Redis locks | Not achievable with the approved store and prohibited as a balance-protection mechanism |

## Consequences

- **Enables:** durable, replay-safe messaging the Phase 1 ledger outbox and Phase 2 reconciliation work can rely on; one envelope for every service.
- **Constrains:** every producing service owns outbox, inbox, idempotency and dead-letter tables plus migrations; every consumer must be idempotent; event schema changes become reviewed, versioned contract changes; partition keys must be chosen per event family.
- **Cost:** an outbox relay, retry state machine, operator replay procedure and compatibility test suite per event family.
- **Out of scope:** the production broker product and its managed-service choice, which remains a stakeholder decision recorded below, and retention periods, which are risk and business policy.

## Compliance and evidence

- Integration and contract tests covering every failure scenario listed in `04-messaging-and-outbox.md`, including stop-after-commit, stop-after-ack, duplicate and concurrent delivery, poison payload and replay-after-processing.
- Architecture test failing when a publish call exists outside the outbox relay path.
- Metrics as specified in the epic: unpublished outbox count and oldest age, publish and consume success or failure, retry count, inbox duplicate count, dead-letter count and oldest age, idempotency hits, conflicts and in-progress duplicates.
- Recorded evidence for approval: the envelope contract, topic naming convention, the four table definitions and a passing interruption-and-replay test run.

## Open questions

- Production broker implementation and whether it is a managed service. Until approved, the local and CI broker must be replaceable behind a documented adapter and configuration, per `02-local-infrastructure.md`.
- Retention periods for outbox, inbox, idempotency and dead-letter records, which are risk and business policy and must be recorded as an amendment.
- Schema-registry trigger: revisit when a non-.NET consumer, a third-party producer, or more than roughly twenty event families exists.
- Whether topic provisioning is automated as code or managed manually in the production environment.

## Approval gate

Satisfied 2026-10-16 for the envelope, outbox, inbox, idempotency, dead-letter and replay design. The production broker product is not selected by this ADR: until it is confirmed by the stakeholders listed in `BACKEND_ARCHITECTURE.md`, the broker stays behind a documented adapter and configuration, and no production topic provisioning or managed-service commitment may be made.

## Related documents

- [ADR index](README.md)
- [ADR-0005](ADR-0005-ledger-consistency-balances-and-posting-boundary.md) - atomic commit of posting and outbox record
- [ADR-0006](ADR-0006-api-versioning-and-problem-details.md) - consistent versioning and error conventions
- [04-messaging-and-outbox.md](../implementation/phase-0-engineering-foundation/04-messaging-and-outbox.md)
- [AGENTS.md](../../AGENTS.md) - `## Events and messaging`

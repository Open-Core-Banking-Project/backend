---
phase: "Phase 0 - Engineering Foundation"
epic: "Messaging, Outbox, Inbox and Idempotency"
owning_service: "Platform-wide"
modules: [Event Contracts, Transactional Outbox, Inbox, Idempotency, Replay]
status: In progress
requirements: [ARC-002, ARC-003, LED-004, LED-005, NFR-RES-002, API-007]
stories: [US-020, US-068, US-071]
dependencies: [Repository and Build Foundation, Local Infrastructure, Identity and Permission Foundation]
adrs: [ADR-0007]
last_updated: 2026-10-16
---

# Epic: Messaging, Outbox, Inbox and Idempotency

Approved 2026-10-16 on the instruction of the repository owner, after ADR-0007 was accepted. Ready to implement.

Implementation evidence, 2026-10-17 (slices 1 to 6 and the failure-scenario tests; the participants and the runbook are still outstanding):

- The envelope is `IntegrationEventEnvelope` in `CoreBanking.BuildingBlocks.Application/Messaging`, carrying exactly the ten documented fields. `EventContract` fixes the serializer (UTF-8 JSON, camel case, ISO 8601, null causation omitted) and the topic rule `corebanking.<service>.<event-family>.v<major>`, and can read the major version back out of a topic. `PartitionKey` is the business reference.
- `IdempotencyKey` scopes a key to institution, operation and initiator, and fingerprints the request body with SHA-256 so a materially different request can be recognised. `RetrySchedule` is a pure exponential-backoff-with-jitter function, so retry timing is provable instead of wall-clock dependent.
- The four integration tables exist in `identity_access_db` under the `integration` schema: `outbox_messages`, `inbox_messages`, `idempotency_records` and `dead_letters`. All four are tenant-owned, carry the institution column in their keys, and receive the mandatory institution query filter. Their technical mappings live in the shared building block; the table, the migration and the retention belong to Identity and Access, as ADR-0007 requires.
- `OutboxRelay` claims pending records with `FOR UPDATE SKIP LOCKED`, publishes, and marks a record `published` only after the broker acknowledged it. A broker failure increments attempts and schedules the next attempt; exhausted attempts move the record to `manual_repair` and raise a dead letter. Retry never recreates the event identity. The relay is the only production code allowed to publish, and an architecture test now fails if anything else references the publisher port or the broker client.
- `InboxProcessor` registers the event identity in the same unit of work as the consumer outcome, treats a repeat delivery as a no-op that leaves the recorded outcome, and converges two concurrent consumers through the unique inbox key. An unsupported schema version is dead-lettered without invoking the handler.
- `IdempotencyService` writes the claim before the command runs, returns the recorded result for an equivalent request, returns a stable conflict for a materially different request, and releases the claim when the command fails so a retry can execute. The record is durable in PostgreSQL, never only in Redis.
- `KafkaEventPublisher` is the adapter behind the publisher port: `acks=all`, idempotent producer, broker failure translated into `EventPublicationException`. `MessagingMetrics` publishes the counters and the consumer-duration histogram that the epic requires, with bounded labels only.
- Verification, run and passed: `dotnet build CoreBanking.sln -c Release` reports 0 warnings and 0 errors; `scripts/verify.ps1` passes with 48 tests (13 architecture, 35 Identity and Access) and a clean format gate; `scripts/db-verify.ps1` applies the migrations and passes 18 tests (12 database, 6 integration categories) as the application principal.
- The failure scenarios of the epic are covered by passing tests: source state and the outbox record commit together or not at all (committed and rolled-back cases); a record that is not yet due is not claimed; a broker failure schedules a retry with the same event identity; exhausted attempts produce `manual_repair` plus a visible dead letter; a duplicate delivery is a no-op; two concurrent consumers converge on one outcome; an unsupported schema version is dead-lettered without running the handler; a command failure releases the idempotency claim.
- The Kafka adapter is proven against the real local broker, not a mock: a published envelope round-trips with its partition key and payload intact, and an unreachable broker surfaces as a publication failure rather than a silent loss.

Status is `In progress`, not `Implemented`. The Identity Access API now writes a synthetic foundation
record and outbox row in one transaction; its separate worker relays committed events, while the
Audit Reconciliation worker consumes the versioned event into its independent database. Its inbox
identity and append-only audit evidence are persisted atomically. The operator runbook covers the
two processes and database-specific repair path. Automated unit and model tests cover the audit
contract and tenant-scoped schema; real broker/database restart and replay verification still
requires the local infrastructure stack.

Outstanding:

- Run the complete local infrastructure verification against both service databases and the real
  broker, including restart-after-commit and replay-after-consumption.
- Add end-to-end correlation/causation tracing, standard liveness/readiness endpoints and telemetry
  redaction evidence under epic 05.
- Add durable audit evidence for authorization allow/deny decisions and the operator replay action.
- Complete the remaining outbox/inbox operational gauges and confirm the relevant meter instruments
  through a listener.

## Objective

Provide a durable, versioned and replay-safe asynchronous communication foundation that never loses an event after a local commit and never creates duplicate business outcomes when a message is delivered more than once.

## Event envelope

The standard envelope contains:

```text
event_id
event_type
schema_version
occurred_at
institution_id
correlation_id
causation_id
producer
business_reference
payload
```

The envelope is transport-neutral. Broker-specific headers and serialization details are defined by the messaging ADR.

## Outbox rules

- Persist source state and outbox record in the same local database transaction.
- Publish only committed outbox records.
- Retry publication without recreating the source event.
- Track attempts, next-attempt time and terminal/manual-repair state.
- Never mark an outbox record published before broker acknowledgement required by policy.
- Retain enough evidence for operational repair and audit without storing unnecessary sensitive payloads.

## Inbox rules

- Register the event identity before or atomically with the consumer outcome.
- Repeated delivery returns the recorded outcome or no-ops safely.
- Consumer failure must leave the message eligible for controlled retry.
- Poison messages move to a visible dead-letter/manual-repair path.
- Replay uses the original event identity and cannot create a second business outcome.

## Idempotent command rules

- Every financial command eventually uses a scoped idempotency key, although Phase 0 uses a synthetic non-financial command.
- Scope includes institution, operation and initiating identity/client where appropriate.
- Same key plus equivalent request returns the original result.
- Same key plus materially different request is rejected with a stable conflict error.
- Concurrent duplicate requests produce one authoritative outcome.
- Retention is defined by risk and business policy; it is not an arbitrary cache timeout.

## Reference flow

```text
Reference API
  -> validate tenant and permission
  -> begin local transaction
  -> create synthetic foundation record
  -> create outbox record
  -> commit
  -> publisher sends event
  -> consumer records inbox identity and local read model
  -> duplicate delivery is ignored or returns original outcome
```

## Failure scenarios

Test at minimum:

- Process stops after local commit but before publish.
- Broker is unavailable.
- Broker acknowledges but publisher stops before local published marker.
- Consumer stops before local commit.
- Consumer commits then receives the same event again.
- Two consumers receive the same event concurrently.
- Invalid schema version arrives.
- Poison payload repeatedly fails.
- Replayed event arrives after normal processing.

## Data structures

Each participating service owns local tables equivalent to:

```text
integration.outbox_messages
integration.inbox_messages
integration.idempotency_records
integration.dead_letters
```

Shared libraries may provide technical EF configurations and processing primitives, but each service owns its migrations, records and retention.

## Observability

Emit metrics for:

- Unpublished outbox count and oldest age.
- Publish success/failure and retry count.
- Consumer success/failure and processing latency.
- Inbox duplicate count.
- Dead-letter count and oldest age.
- Idempotency hits, conflicts and in-progress duplicates.

Trace producer and consumer spans using correlation and causation identifiers without placing sensitive payloads in telemetry.

## Implementation slices

1. Approve event-envelope, broker and serialization ADR.
2. Define public event and command metadata contracts.
3. Implement local outbox persistence and publisher.
4. Implement inbox/deduplication consumer behaviour.
5. Implement scoped command idempotency.
6. Add retry, dead-letter and manual-repair states.
7. Run interruption, concurrency and replay integration tests.
8. Document operations and recovery.

## Acceptance criteria

- [ ] Source state and outbox are atomic.
- [ ] Duplicate publication and consumption do not duplicate outcomes.
- [ ] Concurrent duplicate commands produce one outcome.
- [ ] Replay is safe and observable.
- [ ] Dead letters and stuck outbox records are visible and repairable.
- [ ] Event contracts are versioned and documented.
- [ ] No distributed transaction is introduced.

## Open decisions

- Broker implementation.
- Serialization format and schema registry.
- Topic naming and partition strategy.
- Retry and dead-letter policy.
- Retention periods.
- Contract compatibility testing tooling.

## Agent instructions

- Do not claim exactly-once delivery; implement effectively-once business outcomes through idempotency.
- Do not use Redis as the only durable idempotency store for authoritative operations.
- Do not include domain-specific banking events in the shared foundation unless an approved epic defines them.

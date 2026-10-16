---
phase: "Phase 0 - Engineering Foundation"
epic: "Messaging, Outbox, Inbox and Idempotency"
owning_service: "Platform-wide"
modules: [Event Contracts, Transactional Outbox, Inbox, Idempotency, Replay]
status: Approved
requirements: [ARC-002, ARC-003, LED-004, LED-005, NFR-RES-002, API-007]
stories: [US-020, US-068, US-071]
dependencies: [Repository and Build Foundation, Local Infrastructure, Identity and Permission Foundation]
adrs: [ADR-0007]
last_updated: 2026-10-16
---

# Epic: Messaging, Outbox, Inbox and Idempotency

Approved 2026-10-16 on the instruction of the repository owner, after ADR-0007 was accepted. Ready to implement.

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


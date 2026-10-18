# Messaging operations and recovery

How to read, repair and replay the Identity and Access outbox, inbox, idempotency and dead-letter
records. Decisions: [ADR-0007](../adr/ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md),
[ADR-0004](../adr/ADR-0004-database-per-service-and-tenant-isolation.md).

Nothing here is a substitute for the ledger reconciliation of Phase 1. This runbook covers the
Phase 0 foundation only.

## Processes

| Process | Runs | Publishes | Consumes |
| --- | --- | --- | --- |
| `CoreBanking.IdentityAccess.Api` | request path | never | never |
| `CoreBanking.IdentityAccess.Workers` | outbox relay | yes, through the outbox relay | never |
| `CoreBanking.AuditReconciliation.Workers` | foundation-event consumer | never | yes, through its local inbox |

The API writes the source record and its outbox row in one transaction. Identity Access publishes
committed rows; Audit Reconciliation consumes the foundation topic into its own database. The
consumer commits its inbox identity and audit evidence atomically, so a replay cannot duplicate
the evidence. A request can never publish, which is enforced by an architecture test.

Configuration, all from the environment:

```text
ConnectionStrings__IdentityAccess    application principal connection string
ConnectionStrings__AuditReconciliation  application principal connection string for the consumer
Messaging__Kafka__BootstrapServers   broker address, for example 127.0.0.1:9092
```

Start the local pair after `scripts/infra.ps1 -Action up`:

```powershell
$env:ConnectionStrings__IdentityAccess = 'Host=127.0.0.1;Port=15432;Database=identity_access_db;Username=identityaccess_app;Password=<from deploy/compose/.env>'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://127.0.0.1:4317'
$env:Messaging__Kafka__BootstrapServers = '127.0.0.1:9092'
dotnet run --project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Workers
```

In a separate process, also set `DOTNET_ENVIRONMENT` to `Development` and
`OTEL_EXPORTER_OTLP_ENDPOINT` to `http://127.0.0.1:4317`, then configure
`ConnectionStrings__AuditReconciliation` with the
`auditreconciliation_app` credentials from `deploy/compose/.env` and run
`dotnet run --project src/Services/AuditReconciliation/src/CoreBanking.AuditReconciliation.Workers`.
Do not put a database password in shell history or print it to logs.

## Normal operation

Read the backlog before assuming a problem:

```sql
select status, count(*), min(created_at) as oldest
from integration.outbox_messages
group by status;

select count(*) as unprocessed, min(received_at) as oldest
from integration.inbox_messages
where processed_at is null;

select source, reason, count(*)
from integration.dead_letters
where repaired_at is null
group by source, reason;

select action, decision, count(*), min(occurred_at) as oldest
from audit.events
group by action, decision;

select decision, reason, count(*), min(occurred_at) as oldest
from audit.authorisation_events
group by decision, reason;
```

`integration.outbox_messages.status` is `Pending`, `Published` or `ManualRepair`. A record becomes
`Published` only after the broker acknowledged it.

## Stuck `Pending` records

1. Confirm the worker is running and the broker is reachable.
2. Check `attempts`, `last_error` and `next_attempt_at`. A record backs off exponentially up to
   `OutboxRelayOptions.MaximumRetryDelay`, so a short delay is normal.
3. A broker outage needs no action: the records publish themselves when the broker returns, with
   the original `event_id`.
4. Only if a record is genuinely stuck may an operator bring `next_attempt_at` forward:

```sql
update integration.outbox_messages
set next_attempt_at = now()
where event_id = '<event-id>' and status = 'Pending';
```

Never delete an outbox record to clear a backlog, and never edit `envelope` or `event_id`.

## `ManualRepair` records and dead letters

A record reaches `ManualRepair` when `attempts` reach the maximum. It is visible here:

```sql
select dead_letter_id, source, event_id, event_type, reason, attempts, created_at
from integration.dead_letters
where repaired_at is null
order by created_at;
```

Procedure:

1. Identify the cause from `reason` and `last_error`. A rejected payload or an unsupported schema
   version is a code or contract defect, not an operational one: fix it, do not replay.
2. When the cause is fixed, replay the **original** event identity by returning the record to the
   queue and marking the dead letter repaired:

```sql
begin;

insert into audit.operator_actions (
    audit_event_id, institution_id, actor_id, action, resource_type,
    resource_id, reason, correlation_id, occurred_at)
select
    '<new-audit-event-id>', institution_id, '<operator-id>',
    'messaging.outbox.replay', 'integration_event', event_id,
    '<verified-repair-reason>', '<change-correlation-id>', now()
from integration.outbox_messages
where event_id = '<event-id>' and status = 'ManualRepair';

update integration.outbox_messages
set status = 'Pending', attempts = 0, next_attempt_at = now(), last_error = null
where event_id = '<event-id>' and status = 'ManualRepair';

update integration.dead_letters
set repaired_at = now()
where event_id = '<event-id>' and repaired_at is null;

commit;
```

3. Confirm the transaction inserted exactly one `audit.operator_actions` row and changed exactly
   one outbox row. Keep the same identifiers in the change ticket. Abort rather than committing if
   either row count is not one.

A replay cannot create a second business outcome: the consumer registers the original `event_id`
in `integration.inbox_messages`, so a repeated delivery is a no-op.

The Audit Reconciliation application principal can select and insert `audit.events`, but cannot
update or delete evidence through SQL. Ordinary application paths expose no edit or delete operation.
The Identity Access application principal has the same append-only restriction on
`audit.operator_actions`, so replay evidence cannot be rewritten or removed by the runtime role.

## Malformed transport messages

An envelope that cannot be parsed or is missing required envelope fields is durably quarantined in
`integration.untrusted_message_quarantine` before its Kafka offset is committed. This prevents one
poison message from blocking the partition while preserving the original transport coordinates.

```sql
select transport, stream, partition, position, message_key, reason, quarantined_at
from integration.untrusted_message_quarantine
where repaired_at is null
order by quarantined_at;
```

Treat `payload` as sensitive operational evidence: restrict access and never copy it into logs,
tickets or metric labels. After the producer or contract defect is understood, create a corrected
event through the owning service; do not edit and republish the quarantined bytes. Mark the record
repaired only after the corrected event has reached its intended consumer.

## Duplicate and out-of-order delivery

Delivery is at-least-once. Ordering is per partition key, which is the business reference, so
events for one aggregate stay in order and nothing may assume global order. A duplicate delivery is
expected and safe; `messaging.inbox.duplicates` counts them.

## Unsupported schema version

A consumer that receives a version it does not understand writes `rejected` to the inbox and a dead
letter, and never runs the handler. Publish the new major on a new topic; do not widen the accepted
version to clear the dead letter.

## Idempotent commands that never finished

A command that fails removes its idempotency claim, so a retry can run. A process that dies between
the claim and the completion leaves one row `InProgress`:

```sql
select institution_id, operation, initiator, idempotency_key, created_at
from integration.idempotency_records
where status = 'InProgress'
order by created_at;
```

Inspect the business state first. If the command did not take effect, release the claim so the
caller can retry:

```sql
delete from integration.idempotency_records
where status = 'InProgress'
  and idempotency_key = '<key>'
  and created_at < now() - interval '15 minutes';
```

If the command did take effect, complete the row with the recorded result instead of deleting it,
so a replay returns the original outcome. Never release a claim for a command whose effect is
unverified.

## Metrics to watch

Required by epic 04 and exported from `CoreBanking.Messaging`:

| Metric | Meaning |
| --- | --- |
| `messaging.outbox.unpublished` | unpublished records, the primary backlog signal |
| `messaging.outbox.oldest_age_seconds` | age of the oldest unpublished record |
| `messaging.outbox.published` / `publish_failures` / `retries` | publication health |
| `messaging.inbox.processed` / `duplicates` / `failures` | consumption health |
| `messaging.consumer.duration` | consumer latency |
| `messaging.dead_letters` | messages needing a human |
| `messaging.idempotency.hits` / `conflicts` / `in_progress` | command safety |

Labels are bounded identifiers only. No payload, correlation identifier or personal data is
exported.

## Retention

Retention for outbox, inbox, idempotency and dead-letter records is risk and business policy and is
still an open item in ADR-0007. Until it is decided, do not purge these tables: deleting outbox
evidence loses the audit trail, and deleting inbox evidence re-opens duplicate-delivery risk.

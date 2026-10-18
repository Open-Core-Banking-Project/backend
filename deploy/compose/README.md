# Local development infrastructure

Container-based local dependencies for the backend: the nine service-owned databases,
a Kafka-compatible broker, Redis, and an OpenTelemetry collector.

## Decisions this implements

- [ADR-0004](../adr/ADR-0004-database-per-service-and-tenant-isolation.md) - one database per service, one application and one migration principal per service, no cross-service access.
- [ADR-0007](../adr/ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) - Kafka-compatible broker; the production product remains an open decision.
- [ADR-0010](../adr/ADR-0010-observability-audit-evidence-redaction-and-retention.md) - OpenTelemetry pipeline; production backends remain open, so the collector prints telemetry locally.
- [ADR-0011](../adr/ADR-0011-secret-key-and-certificate-management.md) - no committed credentials; local values are generated and git-ignored.

## Quick start

```powershell
powershell -File scripts/infra.ps1 -Action up        # generate credentials if needed, start, wait for health
powershell -File scripts/infra.ps1 -Action status    # show containers and health
powershell -File scripts/infra-verify.ps1            # prove the environment works
powershell -File scripts/infra.ps1 -Action down      # stop, keep volumes
```

`-Action up` creates `deploy/compose/.env` with random development-only credentials if
the file does not exist. The file is git-ignored and its values are never printed.

## Components and pinned versions

| Component | Image | Purpose |
| --- | --- | --- |
| PostgreSQL | `postgres:16.15` | Nine service databases, each with an owner pair of principals |
| Broker | `apache/kafka:4.2.2` | Kafka-compatible messaging in single-node KRaft mode |
| Cache | `redis:7.4.11` | Non-authoritative caching and short-lived coordination |
| Collector | `otel/opentelemetry-collector:0.163.1` | OTLP gRPC on 4317 and HTTP on 4318, printing to standard output |

Every published port binds to `127.0.0.1`. Nothing is exposed on the local network.

In Development, APIs and workers export traces to `http://127.0.0.1:4317` by default. Outside
Development, set `OTEL_EXPORTER_OTLP_ENDPOINT` explicitly. The default protocol is `grpc`; set
`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` when using the collector's HTTP receiver on port 4318.
`OTEL_SERVICE_NAME` can override the service resource name; deployment environment and service
version are set by the process.

## Databases and principals

Nine databases carry the names fixed by the architecture document. Each has:

- `<service>_migrator`, which owns the database and may change schema;
- `<service>_app`, which may connect only to its own database and read or write data;
- `REVOKE CONNECT ... FROM PUBLIC`, so any other principal is refused even with valid credentials;
- default privileges so tables created by the migrator are readable and writable by the application principal.

`cb_admin` is the development administrator and must never be used by a service.

## Host ports

Defaults are the standard ports. Override any of them in `.env` when a native service
already owns a port:

| Variable | Default | Note |
| --- | --- | --- |
| `CB_POSTGRES_PORT` | 5432 | Commonly taken by a locally installed PostgreSQL service |
| `CB_BROKER_PORT` | 9092 | Also used as the broker's advertised listener |
| `CB_CACHE_PORT` | 6379 | Commonly taken by a locally installed Redis |
| `CB_OTLP_GRPC_PORT` | 4317 | |
| `CB_OTLP_HTTP_PORT` | 4318 | |

## Verification

`scripts/infra-verify.ps1` runs 36 checks and exits non-zero on any failure:

1. shell scripts mounted into containers use LF line endings, all containers run, health-checked containers are healthy, and ports bind to loopback only;
2. each application principal connects to its own database;
3. each application principal is refused on another service's database;
4. the broker creates a topic and round-trips three messages;
5. Redis answers and has persistence deliberately disabled;
6. the collector accepts an OTLP trace and emits it;
7. the migration principal may change schema while the application principal may not, and the migration principal is refused on another service's database;
8. a database backup produces a non-empty archive, restores into a scratch database, and the restored rows match.

## Migrations and database tests

Applied migrations are part of the environment, not of the service startup. The migration
principal owns schema change; the application principal only reads and writes data.

```powershell
powershell -File scripts/db-verify.ps1
```

The script restores the pinned tools from `.config/dotnet-tools.json`, applies every migration to
`identity_access_db` as `identityaccess_migrator`, lists the applied migrations, and then runs the
`Category=Database` tests as `identityaccess_app`. It never starts the stack: run
`scripts/infra.ps1 -Action up` first, and expect a clear error if PostgreSQL is not reachable.

Schema privileges for a service's own schemas are granted by the migration that creates them,
because the cluster initialisation script can only set default privileges for schemas that exist
when the cluster is created.

## Backup, restore and reset

```powershell
powershell -File scripts/infra.ps1 -Action backup                       # all nine databases into artifacts/backups/<timestamp>
powershell -File scripts/infra.ps1 -Action backup -Database core_ledger_db
powershell -File scripts/infra.ps1 -Action restore -Database core_ledger_db   # restores into a scratch database and verifies, leaving the source untouched
powershell -File scripts/infra.ps1 -Action reset -Force                 # destroys containers and the two named volumes only
```

`reset` refuses to run without `-Force` and deletes only `corebanking_pgdata` and
`corebanking_kafkadata`. It never touches other volumes, containers or images.

## Persistence expectations

- PostgreSQL and the broker have named volumes and survive `down` and restart.
- Redis has **no volume** and runs with `--save "" --appendonly no`: it is never durable financial truth, per `AGENTS.md` and ADR-0004.
- The collector stores nothing; telemetry is printed as it arrives.

## Safety rules

- Development data must be synthetic. Real customer data must never enter this environment.
- Local defaults cannot resolve to production endpoints; no production credential belongs in `.env`.
- Health checks and scripts never print secret values.
- Redis is never configured as a source of truth for balances, idempotency or audit evidence.

## Not configured here, by design

Production secret management, production telemetry backends, the production broker
product, object storage, and search remain open decisions in ADR-0010, ADR-0011 and
ADR-0013. Add them behind the documented abstractions when those decisions are approved.

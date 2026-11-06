# P1-E02-S14 identity observability review pack

## Review request

Review the completed application-owned observability slice: reconciliation, bounded metrics, OTLP collector registration, provider-neutral dashboard/alert contract and operational response guidance. No telemetry backend, SIEM product, production threshold or retention period was selected.

## Requirement and decision coverage

- IAM-027 and IAM-030: privileged identity activity remains append-only and correlated without emitting credentials or identity payloads.
- NFR-OBS-001 and SEC-017: Identity signals use the shared OpenTelemetry collector boundary and bounded, identifier-free metric dimensions.
- ADR-0010: OTLP is the single application egress; backend products, routing, thresholds and retention remain platform decisions.

## Delivered increment

- Retained the hourly `IdentityReconciliationWorker` and step-up-protected operator reconciliation endpoint.
- Moved the four reconciliation instruments to the Identity infrastructure boundary so workers and future Identity hosts share one stable meter.
- Added `CoreBankingMeterNames.IdentityAccess` as the single compile-time name used by the emitter and collector registration.
- Registered `CoreBanking.IdentityAccess` in the shared OTLP metrics pipeline. The existing counters now leave the process through the approved collector boundary.
- Added a provider-neutral dashboard contract covering entitlement integrity, messaging delivery, service identity, step-up/encryption and audit-pipeline health without identifier labels.

## Verification performed

- Identity test project build: passed with zero warnings and errors.
- Identity worker build: passed with zero warnings and errors.
- Focused meter publication test: 1 passed.
- Identity non-database suite: 198 passed.
- Affected project formatting verification: passed.
- `git diff --check`: passed.

## Migration and contract impact

- No database migration or external API/event contract change.
- Metric names remain unchanged; the change makes their meter observable by the configured OTLP provider.

## Remaining production gates

- ADR-0010 leaves telemetry backend products, SIEM routing, dashboard implementation, production alert thresholds, sampling and evidence retention open.
- The repository provides the application signals and response contract only. Deployment owners must supply backend-specific queries, destinations and evidence without adding secrets or unbounded identity labels.

## Rollout and forward fix

Deploy the normal Identity API/worker artifact with the existing OTLP endpoint configuration. Confirm the collector receives the four reconciliation instruments before enabling alerts. If the added meter causes an exporter issue, remove its registration in a forward fix; reconciliation and durable audit evidence remain authoritative and must not be deleted.

## Human action

Review the collector boundary, bounded metric catalogue and retained production gates.

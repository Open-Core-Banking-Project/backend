# Products and accounts operations

Operational contract for the Core Banking and Ledger product and account configuration areas
(`product_eligibility.v1`, `institution.currencies`). Decision: P1-E04 slice 14.

Account opening, restrictions, liens, dormancy and statements are not yet implemented, so this runbook
covers the configuration lifecycle, reference-fact consumption and the integrity signals that exist today.
Extend it in the same change that enables those features; do not document a capability ahead of the code.

## Signals

All instruments live on the `CoreBanking.ProductAccounts` meter, registered in the shared OTLP pipeline.
Tags are bounded: `area`, `command`, `outcome`. No customer, account, payload, hash of a personal field or
monetary value is ever a tag or a log field.

| Instrument | Type | Meaning |
| --- | --- | --- |
| `corebanking.product_account.configuration.commands` | counter | Lifecycle command by `area`, `command` (`draft.create`, `simulate`, `submit`, `publish`) and `outcome` (`succeeded`, `replayed`, `rejected`) |
| `corebanking.product_account.reference_facts.lookups` | counter | Authoritative customer-fact lookups by bounded `outcome` code |
| `corebanking.product_account.configurations.published` | gauge | Published configuration versions across both areas |
| `corebanking.product_account.configurations.awaiting_publication` | gauge | Approved versions not yet published |
| `corebanking.product_account.outbox.pending` | gauge | Outbox messages not yet relayed |
| `corebanking.product_account.outbox.oldest_pending_age_seconds` | gauge | Age of the oldest pending outbox message |

Existing institution instruments remain on `CoreBanking.Institutions` (provisioning and repair).

## Alerts

| Alert | Condition | First response |
| --- | --- | --- |
| Publication failing | `configuration.commands{command="publish",outcome="rejected"}` rising for 15 minutes | Read the stable rejection code in the API response; confirm the version is `approved` and the publisher is not the maker; do not bypass approval |
| Approval stuck | `configurations.awaiting_publication` above zero for longer than the approved approval window | Check the Workflow request and the decision-time entitlement evidence; the version cannot publish without a trusted decision |
| Simulation rejected | `configuration.commands{command="simulate",outcome="rejected"}` rising | Validate the currency set (`baseCurrency` must be a member, ISO alpha-3, no duplicates) or the product rules payload |
| Reference facts unavailable | `reference_facts.lookups{outcome="eligibility_facts_unavailable"}` or `eligibility_facts_stale` rising | Check the Customer and Onboarding host, the allow-list, the audience and the Keycloak client; eligibility must stay denied |
| Reference facts mismatched | `reference_facts.lookups{outcome="eligibility_facts_mismatch"}` above zero | Stop: a caller is receiving another institution's or malformed facts. Investigate the service token audience and claim mapping before anything else |
| Outbox backlog | `outbox.pending` above the approved threshold, or `oldest_pending_age_seconds` above the relay budget | Check the relay worker and broker; approvals and publications stay pending until the relay drains |
| Reconciliation findings | `GET /internal/v1/reconciliation/product-accounts` returns any finding | Follow the finding code below; findings are configuration-integrity failures, never warnings |

Suggested thresholds are deployment data. Set them with Platform/Security; do not hard-code them in code.

## Reconciliation

`GET /internal/v1/reconciliation/product-accounts` requires `institution.configuration.audit` with step-up (Epic 1's four-segment `institution.configuration.audit.view` is not expressible in the platform grammar)
and returns a tenant-scoped, identifier-only report:

| Finding | Meaning | Recovery |
| --- | --- | --- |
| `publication_version_missing` | A publication references a version that is absent | Restore the version or retire the publication through an approved change; never delete the publication row |
| `publication_hash_mismatch` | Publication and version hashes disagree | Treat as tampering or a failed migration; freeze the area, restore from backup evidence and reconcile |
| `published_version_without_publication` | A version claims `published` with no publication evidence | Do not serve it as effective; re-publish through the lifecycle or supersede it |
| `approved_without_approval_evidence` | An approved version lacks approval reference or approver | Re-open approval through Workflow; publication must keep failing closed |
| `overlapping_publication_periods` | Two publications in one set overlap or one is open-ended | Supersede the earlier publication with a closed period; account decisions must treat the set as ambiguous and deny |

Each code reports at most 20 findings so the response stays bounded; `IsHealthy` is false while any finding or
pending outbox message exists.

## Recovery procedures

**Product or currency publication failure.** Read the stable code, correct the cause, and re-submit with the
same idempotency key only when the payload is unchanged. A changed payload needs a new key. Never edit a
configuration version row directly: published versions are immutable and the hash is the evidence.

**Reference-fact outage.** Eligibility denies by design. Confirm `CustomerFacts:BaseUrl`, the workload client
id, `CustomerFacts:AllowedServiceClientIds`, the token audience `corebanking-customer-api` and the capability
`service.customer.eligibility_facts_read`. There is no cached fallback for an opening decision; do not add one
during an incident.

**Outbox backlog.** Restart or scale the relay, confirm broker connectivity, and watch `oldest_pending_age_seconds`
fall. Approval decisions and publication events are delivered at least once; consumers deduplicate by event id.

**Restore.** Hold traffic. Verify, in order: configuration version hashes against publications, no overlapping
effective periods, approval references present, account-number sequence state (once implemented), outbox/inbox
continuity, and idempotency records for the restored window. Resume only when the reconciliation endpoint is
healthy and the sequence evidence is restored.

**Manual repair.** Only through authorised commands or the lifecycle API, with reconciliation evidence and an
approval reference. Direct SQL edits to configuration, publication or outbox rows are prohibited.

## Evidence rules

- Audit rows are append-only and committed with the governed change: actor, permission, action, resource,
  outcome, correlation and before/after reference.
- Never place full account numbers, statement content, customer personal data or provider secrets in audit,
  telemetry, alerts or this runbook.
- A missing or invalid configuration fails closed; nothing here authorises a permissive default.

# Products and accounts operations

Operational contract for the Core Banking and Ledger product and account configuration areas
(`product_eligibility.v1`, `institution.currencies`, `institution.account-opening-policy`,
`institution.account-numbering`) and the account application and opening boundary.
Decisions: P1-E04 slice 14, P1-E04-D06, P1-E04-D07.

This runbook covers the configuration lifecycle, reference-fact consumption, account applications with their
eligibility evidence, number allocation, approval, account opening, activation, account controls, liens, dormancy and
reactivation, closure, account summaries, statement requests and the integrity signals that exist today. Extend it in
the same change that enables those features; do not document a capability ahead of the code.

## Signals

All instruments live on the `CoreBanking.ProductAccounts` meter, registered in the shared OTLP pipeline.
Tags are bounded: `area`, `command`, `outcome`. No customer, account, payload, hash of a personal field or
monetary value is ever a tag or a log field.

| Instrument | Type | Meaning |
| --- | --- | --- |
| `corebanking.product_account.configuration.commands` | counter | Lifecycle command by `area`, `command` (`draft.create`, `simulate`, `submit`, `publish`) and `outcome` (`succeeded`, `replayed`, `rejected`) |
| `corebanking.product_account.reference_facts.lookups` | counter | Authoritative customer-fact lookups by bounded `outcome` code |
| `corebanking.product_account.account.commands` | counter | Account-application commands by `command` (`create`, `evaluate`) and `outcome` (`succeeded`, `replayed`, `rejected`) |
| `corebanking.product_account.configurations.published` | gauge | Published configuration versions across the product, currency, opening-policy and numbering areas |
| `corebanking.product_account.configurations.awaiting_publication` | gauge | Approved versions not yet published |
| `corebanking.product_account.account_numbers.available` | gauge | Account numbers still issuable across the institution's scheme instances |
| `corebanking.product_account.account_numbers.depletion_band` | gauge | Highest depletion band reached: `0`, `80`, `90` or `95` |
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
| Applications refused by policy | `account.commands{command="evaluate",outcome="rejected"}` rising with `account_opening_policy_unavailable` | The institution has no effective opening ceiling, so opening stays blocked by design. Publish an approved `institution.account-opening-policy` version; never widen the code to allow an open-ended limit |
| Concurrent opening held | `account.commands{command="evaluate",outcome="rejected"}` with `account_opening_claim_busy` | One claim per customer and counting basis is expected; a sustained rate means a stuck evaluation. Claims expire after 60 seconds and an expired claim is released when the next evaluation runs, so investigate the failing path rather than deleting rows |
| Applications refused by limit | `account.commands{command="evaluate",outcome="rejected"}` rising with `account_limit_reached` | Read the recorded evaluation: the counted holdings and effective maximum are evidence. Do not edit the snapshot; a legitimate exception needs the approved override path, which is not enabled here |
| Account numbers depleting | `account_numbers.depletion_band` above zero, or `account_numbers.available` below the approved runway | Follow the band: at `80` plan a wider or newly prefixed scheme version, at `90` obtain the approval, at `95` publish it. Never widen a published scheme in place and never edit the reservation row |
| Scheme exhausted | `account.commands` or the opening response carries `account_number_scheme_exhausted` | The application stays approved and unassigned; publish an approved scheme version with a wider sequence or new prefix, then retry the opening. Numbers already issued are never renumbered |
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
| `account_without_ledger_initialisation` | An account exists without a proven ledger initialisation reference | Stop activation for that account; the ledger contract must return its own reference before the account can become active. Never set the reference by hand |
| `approved_application_without_account` | An application is approved but produced no account | Read the opening rejection code (scheme missing, exhausted or a segment that cannot be projected); the application stays approved and unassigned until the cause is fixed |

Each code reports at most 20 findings so the response stays bounded; `IsHealthy` is false while any finding or
pending outbox message exists.

## Recovery procedures

**Product or currency publication failure.** Read the stable code, correct the cause, and re-submit with the
same idempotency key only when the payload is unchanged. A changed payload needs a new key. Never edit a
configuration version row directly: published versions are immutable and the hash is the evidence.

**Account summary.** The summary is a read model, never a financial source. `ledger_unavailable` and an absent
balances block mean the ledger could not answer; nothing is inferred and no zero is shown, so read the ledger before
telling a customer their balance. A summary is never accepted as evidence in a dispute or as statement content.

**Statement requests.** A request is validated against the published `statement.policy`: the period must start on or
after the account opening date and end on or before the current business date, must not be inverted or exceed the
configured span, and the format and purpose must be on the allow-list. `statement_format_not_supported` and
`statement_purpose_not_supported` mean the code is not approved - never retry with a different code without an
approved policy change. `statement_policy_missing` means no policy is published for the institution, so every request
is refused until one is. `statement_period_invalid` names the period problem, and the current business date is the
upper bound, so a request cannot run ahead of the calendar.

**Statement source.** `statement_source_unavailable` is expected until the ledger's posted-history query exists: the
request is recorded as refused with no artifact and no content. A statement is never built from a summary, a cache or a
re-derived balance, so do not work around this code. Once the ledger query is available the request moves to
`awaiting_artifact` and reconciliation stops reporting it.

**Artifact references.** A reference is opaque, bound to the requesting party and the account, and expires within
minutes; re-download requires a new authorised step, and every issue and access writes an audit row that never contains
statement content. An expired artifact still answers with its request and an `expired` status, because the request
record outlives the artifact. `statement_request_without_artifact` in reconciliation means a request was accepted and no
artifact arrived inside the window: treat it as an incident, check the document capability and re-request rather than
editing the record.

**Dormancy assessment.** An assessment is recorded once per account, business date and rule version, and a repeated
run returns the recorded outcome rather than assessing the same day twice. The clock is the institution business date
and the last qualifying posted customer-initiated movement; a read, a fee, an interest capitalisation, a control change
or an unposted attempt never resets it. `dormancy_rule_missing` and `dormancy_activity_source_unavailable` mean the day
was recorded as `NotAssessable` and nothing transitioned: publish the rule or restore the ledger source, then re-run
the same business date. `dormancy_notice_missing` means the threshold was reached but the required notice was never
recorded, so the account stays Active until a notice fact exists. `dormancy_assessment_lease_held` means another worker
is assessing that account; the lease expires after two minutes and never needs manual clearance. Missing days are caught
up in order with the catch-up route using the oldest missed business date, so the oldest date is never skipped.

**Reactivation.** Only a dormant account can be reactivated, and the dormancy rule decides whether an approval
reference and a different actor are required (`dormancy_reactivation_requires_approval`,
`dormancy_reactivation_dual_control_required`). Reactivation clears dormancy alone: freezes, blocks and liens are still
in force afterwards, so the account can be active and still denied a debit by the control precedence.

**Closure.** Follow the six approved steps and never shortcut them. A proposal records the ledger condition snapshot;
`account_closure_conditions_not_met` with a refusal code means the account is not closable yet, and the refusal codes
are the plan: settle the balance, release holds or liens, wait for the dispute or notice, or clear the legal control.
Submission binds approval under `account.close.v1` to the exact proposal version and condition snapshot hash.
`balance_source_stale` means the ledger answer aged out of the 60-second ceiling, so re-propose. After approval the
account sits in `PendingClosure` with debits denied; if the final re-check does not settle, use the re-check route once
the condition is resolved, and re-check refusals are recorded on the proposal. A closed account never reopens and its
number is never reused: a returning customer opens a new account. Reconciliation reports
`closed_account_with_residual_balance` and `closure_without_condition_snapshot`, and either one is an incident because
closure is not reversible.

**Liens.** A lien reduces availability and never denies a posting by itself. Its claimed amount is immutable: a
partial release or a ledger satisfaction adds an entry and lowers the outstanding amount, and the lien stays active until
outstanding reaches zero. Outstanding amounts are never derived from a balance and a released lien is never deleted.

**Lien release.** Statutory, tax and institution releases need an approval reference and a releaser other than the maker
(`account_lien_release_requires_approval`, `account_lien_dual_control_required`). Every other release refusal
(`account_lien_over_release`, `account_lien_not_active`) means the request disagrees with the stored outstanding amount -
re-read the lien, never adjust it by hand.

**Control evaluation.** `allow`, `deny` and `review` are the only outcomes. `deny` names every denying control in
`control_denied:<type>` codes ordered by the approved precedence; `review` means either the caller's control version was stale
(`control_version_stale`) or the only prohibitions are manually releasable, in which case a human must release them first.
Never re-evaluate with a forged control version and never add a permissive control: no control grants an ability.

**Control release.** A manually releasable control (regulatory freeze, legal block) needs a release approval reference and a
releaser other than the applier; the response refuses with `account_control_release_requires_approval` otherwise. A released
control is retained with its history, never deleted.

**Approval and activation.** An application becomes `PendingApproval` only from `Eligible`, and the decision must match the exact
application version and eligibility snapshot hash; a mismatched decision is refused before anything changes. An approved decision opens
the account in the same transaction, so a caller never opens an approved application twice. Activation refuses with
`ledger_initialisation_unproven`, `account_number_scheme_not_effective`, `eligibility_facts_stale` or the product rule code; the account
stays open and unactivated, and the remedy is to fix the named cause - not to force the status.

**Number allocation contention.** `account_number_reservation_conflict` means two openings created the
reservation row at the same time; the retry succeeds once the row exists. A sustained conflict rate means the
reservation transaction is timing out; investigate before adding retries. The reservation row is guarded by a
row lock and an optimistic version, so never warm it by hand.

**Opening ceiling missing or invalid.** Every account application refuses with
`account_opening_policy_unavailable`, `account_opening_policy_integrity_failed` or `account_opening_policy_invalid`.
Confirm the `institution.account-opening-policy` set has exactly one version effective now, that its content hash
matches the publication and that the publisher was not the maker. Do not create an open-ended ceiling and do not
permit opening while the ceiling is unknown.

**Eligibility evidence.** The evaluation snapshot is immutable. `trace_json`, the counted holding ids, the
effective maximum, the facts versions and the product content hash answer a later dispute; a remediation is a new
evaluation under a corrected configuration, never an edited row.

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

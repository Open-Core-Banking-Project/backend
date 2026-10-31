# Institution configuration publication failure

This runbook covers a Savings eligibility configuration that was approved but did not become
effective or whose publication event is stuck.

1. Identify the exact institution, product, configuration set, version, content hash, approval
   request and publication ID. Do not copy configuration JSON into logs or tickets.
2. Confirm the source version is still `approved`, its simulation hash matches its content hash and
   the Workflow decision is for the same source identity and hash.
3. Distinguish a rejected source command from an outbox delivery failure. A rejected command must be
   corrected through a new draft/approval; it must not be repaired by SQL.
4. For an outbox backlog, follow [messaging recovery](messaging.md) and preserve the original event
   identity. Consumer replay is expected to be idempotent.
5. If an effective-period conflict or ambiguous as-of selection exists, stop publication and
   escalate to Product/Core Operations. Never shorten, overwrite or delete published history.

Verify the exact approved version is effective once, prior published history is unchanged, the
outbox is published, all consumers have one inbox outcome and telemetry contains no content or
customer facts. Automatic cut-off/timezone publication remains disabled until Operations and
Finance approve that policy.

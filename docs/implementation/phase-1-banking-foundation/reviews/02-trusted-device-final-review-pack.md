# P1-E02-S11 trusted-device final review pack

## Review request

Review the completed trusted-device lifecycle and the final privacy-minimised audit increment for authorised support-officer reads. The implementation does not configure a production passkey or attestation provider.

## Requirement and decision coverage

- IAM-021 / US-009: registration requires fresh device-bound step-up evidence and records the resulting trust.
- IAM-022 / IAM-023: revocation is effective-dated, terminates bound sessions and publishes durable device/session facts.
- P1-E02-D11: only a public-key thumbprint is authoritative; display metadata is untrusted; staff/administrative trust lasts 30 days and other trust lasts 180 days.
- Privacy and audit: an authorised officer read of another identity's devices appends operator evidence without copying a thumbprint, display name or other device detail into the audit record.

## Delivered increment

- The existing registration, listing, completion, self-revocation and administrative-revocation routes remain permission and institution scoped.
- `ListDevicesQuery` carries the trusted request correlation identifier for cross-identity reads.
- A successful officer read appends `identity_devices_viewed` evidence with actor, institution, target identity, bounded reason, correlation and timestamp.
- Cross-tenant reads still return no devices; a cross-identity read without correlation fails closed.
- Audit rows remain append-only. Database test cleanup deliberately does not delete them under the application principal.

## Verification performed

- Affected Identity and Access test project build: passed.
- Trusted-device database suite against local PostgreSQL as `identityaccess_app`: 8 passed, including sensitive-read evidence and cross-tenant isolation.
- Identity and Access non-database suite: 188 passed.
- Affected project formatting verification: passed.
- `git diff --check`: passed.

## Migration and contract impact

- No database migration is required; the existing append-only `audit.operator_actions` model is reused.
- The application query contract adds an optional correlation identifier. Existing self-list callers remain source compatible.
- No external event payload or public response schema changes.

## Remaining production gate and risk

- Passkey and device-attestation providers remain a production integration gate. No provider, credential, attestation policy or production configuration was selected or invented.
- The audit event proves that a scoped read occurred; it intentionally does not retain the returned device set. Reviewers should confirm that this data-minimisation boundary meets support and privacy expectations.

## Rollout and forward fix

This is an additive application change over an existing audit table. Deploy the Identity and Access service normally after review. If the officer-read evidence path must be withdrawn, disable the scoped officer route or ship a forward fix; no schema rollback or audit-row deletion is required.

## Human action

Review the sensitive-read evidence boundary, fail-closed correlation requirement and retained provider gate.

# P1-E02-S10 approval eligibility and Workflow review pack

## Review request

Review the completed maker-separation integration between Identity and Access and Operations Workflow. The implementation is additive and fail closed; it does not enable production service calls without the separately gated workload-token handler.

## Requirement and story coverage

- IAM-012 / US-007 / BR-008: a maker cannot approve the same restricted request through another role, session or channel.
- IAM-013: Identity owns approval eligibility and evaluates current permission, institution scope and entitlement freshness.
- ADR-0008: Workflow uses a separate service permission and never receives a human role.
- ADR-0009: permission evaluation remains versioned, source-authoritative and deny by default.

## Delivered increment

- Added `ServiceApprovalEligibilityRequestV1`, carrying only institution, source/request, permission, maker and candidate identifiers.
- Added `POST /internal/v1/approvals/eligibility`, protected by `service.identity.approval_eligibility` and service-only actor policy.
- Identity resolves the candidate's current human entitlement version and runs the existing audited restricted-approval policy; Workflow cannot submit grants or roles.
- Added `IdentityApprovalEligibilityVerifier` in Workflow. It validates exact source system, request and permission response binding and denies unavailable, timed-out, malformed or inconsistent answers.
- `ApprovalWorkflowService` performs the Identity check before storing a decision, requires the eligibility and entitlement checks to return the same version, and still performs its independent aggregate maker check.
- Development retains an explicit synthetic verifier; non-Development always uses the Identity HTTP boundary.

## Verification performed

- Identity API build: passed, 0 warnings/errors.
- Operations Workflow API build: passed, 0 warnings/errors.
- Identity approval-eligibility tests: 8 passed.
- Workflow HTTP adapter tests: 3 passed.
- Identity non-database suite: 188 passed.
- Operations Workflow non-database suite: 26 passed.
- Focused `ApprovalIngestionDatabaseTests`: 6 passed against local PostgreSQL, including Identity refusal of a self-approval and mismatched entitlement versions before any decision row is stored.
- Affected project formatting verification: passed. The first sandboxed test attempt was aborted because the test host could not open local IPC; the same tests passed outside the sandbox under the approved `dotnet test` rule.

## Remaining gate and risk

- The platform workload-token handler is not configured. Until slice 13 supplies provider-issued, audience-restricted service tokens, production HTTP calls fail closed.
- The service request trusts the authenticated Workflow service to supply the candidate derived from its validated human request and the maker from its stored record. The permission gives that narrowly scoped delegation; ordinary clients cannot call the internal route.
- No database migration is required for this increment.

## Rollout and forward fix

Deploy Identity's additive contract/route before the Workflow caller. Configure the Workflow service client with `service.identity.approval_eligibility` only when the provider token handler and exact Identity audience are ready. Removing the permission or handler causes decisions to fail closed without changing stored requests.

## Human action

Review the service delegation boundary, response-binding checks and fail-closed production gate.

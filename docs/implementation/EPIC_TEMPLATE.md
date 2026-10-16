---
phase: "Phase N - Name"
epic: "Epic name"
owning_service: "Microservice name"
modules: []
status: Draft
requirements: []
stories: []
dependencies: []
adrs: []
last_updated: YYYY-MM-DD
---

# Epic: Name

## 1. Objective

State the business or engineering outcome in measurable terms. Explain who benefits and what becomes possible when the epic is complete.

## 2. Success measures

- Define measurable functional outcomes.
- Define reliability, security, performance, or operational outcomes.
- Define evidence required to verify completion.

## 3. Scope

### Included

- List capabilities delivered by this epic.

### Excluded

- List adjacent capabilities intentionally deferred.
- Link the phase or epic that owns each deferred capability where known.

## 4. Actors, roles, and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Example actor | `domain.resource.action` | Institution/branch/resource/limit | When required | None/maker-checker/multi-level |

Rules:

- Application code checks permissions, not hard-coded role names.
- State which service owns each permission definition and which service enforces it.
- Identify segregation-of-duty and conflict-of-role rules.
- Identify temporary access, delegated access, and access-review requirements.

## 5. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| Example | Describe ownership | Strongly typed ID | References, not cross-service database foreign keys |

### Value objects

- List money, currency, date, status, reference, address, limit, or other value objects.

### Invariants

1. List rules that must always remain true.
2. Identify which invariants require database constraints or concurrency control.
3. Identify any applicable global financial invariants from `AGENTS.md`.

### State transitions

```text
Initial -> Pending -> Approved -> Active
                    -> Rejected
```

Define legal transitions, initiating permission, guards, emitted events, and terminal states.

## 6. Use cases

For each use case define:

### UC-01 - Name

- Actor:
- Preconditions:
- Permission and scope:
- Command/query:
- Main flow:
- Alternative flows:
- Failure outcomes:
- Idempotency scope:
- Transaction boundary:
- Audit evidence:
- Events:

## 7. API contracts

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/...` | `domain.resource.action` | Contract name | Contract name | Yes/No |

Define:

- Validation and stable error codes.
- Pagination and filtering.
- Authentication and step-up requirements.
- Compatibility and versioning expectations.
- Example requests and responses with synthetic data only.

## 8. Event contracts

### Produced events

| Event | Trigger | Key fields | Partition/ordering key | Consumers |
| --- | --- | --- | --- | --- |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |

Define schema versioning, outbox/inbox behaviour, retry, dead-letter, replay, and PII classification.

## 9. Database design

### Ownership

- Database:
- Schemas:
- Migration assembly:
- Database principal:

### Tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |

### Indexes and constraints

- Define uniqueness, foreign keys, check constraints, concurrency versions, and query indexes.
- Define decimal precision/scale and required currency for monetary values.
- Define immutable or append-only records.

### Migration and backfill

- Forward migration:
- Existing-data handling:
- Rollback or forward-fix:
- Verification query/evidence:

## 10. Consistency, concurrency, and idempotency

- Define local transaction boundaries.
- Define expected concurrent operations and lost-update prevention.
- Define idempotency key format, scope, retention, and repeated-request response.
- Define cross-service consistency and compensating/reconciliation behaviour.
- State why no distributed transaction is required.

## 11. Security and privacy

- Threats and abuse cases.
- Data classification.
- Encryption, masking, tokenisation, and secret-management requirements.
- Tenant-isolation checks.
- Input and output restrictions.
- Rate limiting and abuse prevention.
- Negative security tests.

## 12. Audit and observability

### Audit

- Actions and decisions requiring immutable evidence.
- Actor, permission, scope, reason, before/after, correlation, and policy/version fields.

### Logs, metrics, and traces

- Required structured events.
- Business and technical metrics.
- Trace boundaries and correlation.
- Alerts, dashboards, and runbooks.
- Prohibited sensitive values.

## 13. Failure, retry, and recovery

- Expected failures and stable outcomes.
- Retry-safe and retry-unsafe operations.
- Timeout and uncertain states.
- Dead-letter and manual repair.
- Restart and replay behaviour.
- Backup, restore, and evidence requirements.

## 14. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |

Financial or security behaviour must be versioned, effective-dated, approved, and audited where applicable.

## 15. Implementation slices

Implement in reviewable vertical slices:

1. Contracts and failing acceptance tests.
2. Domain model and unit tests.
3. Persistence and migrations with integration tests.
4. Application use cases and permission enforcement.
5. API/event adapters and contract tests.
6. Audit, observability, failure handling, and runbooks.
7. End-to-end and acceptance evidence.

Each slice must leave the repository buildable and must not expose an unsafe partial feature.

## 16. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |

Include success, validation, authorisation, cross-tenant, concurrency, idempotency, retry, failure, audit, and recovery scenarios.

## 17. Acceptance criteria

- [ ] Requirement and story outcomes are implemented.
- [ ] Roles, permissions, scopes, and maker-checker rules are enforced.
- [ ] Database constraints and migrations are verified.
- [ ] APIs and events are versioned and documented.
- [ ] Security, audit, observability, failure, and recovery paths are implemented.
- [ ] Required automated tests pass.
- [ ] Traceability and operational evidence are complete.

## 18. Definition of Done

Reference the repository Definition of Done and add epic-specific completion gates.

## 19. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |

## 20. Prohibited assumptions

- List decisions that implementers and coding agents must not invent.
- State safe behaviour while each decision remains unresolved.

## 21. Agent implementation instructions

- Required reading:
- Files expected to change:
- Files or boundaries that must not change:
- Required commands:
- Required evidence in the final response:
- Conditions that require stopping for a decision:


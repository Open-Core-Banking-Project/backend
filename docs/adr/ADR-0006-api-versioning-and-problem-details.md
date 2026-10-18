---
id: ADR-0006
title: "API versioning and problem-details conventions"
status: Proposed
date: 2026-10-16
owners: [Architecture, API owners]
required_by: "First externally consumed HTTP contract in Phase 1"
architecture_item: 6
referenced_by: []
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0006: API versioning and problem-details conventions

## Status

`Proposed` - not approved. No epic document references this identifier yet; the mapping to architecture list item 6 is inferred from the surrounding identifiers (see the [index](README.md)).

## Context

- `AGENTS.md` (`## APIs and contracts`) requires externally consumed APIs and events to be versioned, published contracts to be treated as compatibility commitments, server-side validation regardless of frontend validation, correlation identifiers on all requests and idempotency identifiers on financial commands, explicit status models for asynchronous and uncertain operations, pagination and bounded filters, no exposure of internal exception messages, stack traces, database keys or provider secrets, and an approved versioning and migration plan for breaking changes.
- `AGENTS.md` (`## C# coding standards` and HTTP guidance) requires consistent RFC 7807 problem details with stable machine-readable error codes.
- The Phase 1 epics already assume conventions in advance of this ADR: `/api/v1/...` and `/internal/v1/...` route shapes, stable error codes, cursor pagination in stable posting sequence, and explicit status vocabularies such as `Received`, `PendingAuthorisation`, `Posted`, `Rejected`, `FailedBeforeCommit`, `Reversed` and `Disputed`.
- `BACKEND_ARCHITECTURE.md` (`## API conventions`) and `README.md` (`## API standards`, `## Error response example`) capture the current expectations.
- `08-partner-apis-and-sandbox.md` additionally requires an API lifecycle and deprecation decision.
- Requirement and story traceability: `API-001` to `API-008`, `NFR-MNT-001`, `US-061`, `US-067`.

## Decision

Not yet decided. This ADR must settle:

- Versioning scheme and placement (URI path, header or media type), and the treatment of internal service-to-service contracts versus externally consumed contracts.
- The compatibility commitment: what may change within a version, what requires a new version, and the deprecation and sunset policy with notice periods.
- The problem-details profile: `type`, `title`, `status`, `detail`, `instance`, correlation identifier, and the catalogue and stability rules for machine-readable error codes.
- Idempotency key and correlation header names, scope, and behaviour on replay or mismatch.
- Pagination, filtering, sorting and maximum page size conventions.
- Where contracts are published (OpenAPI and event schema locations) and how generated clients stay in step.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| No versioning, additive changes only | Cannot express a breaking change safely for partner and corporate clients |
| Date-based versioning | Adds negotiation complexity without an approved consumer need |
| Free-form error payloads per service | Breaks client error handling and the stable-error-code requirement |

## Consequences

- Once accepted, this decision constrains every API and every epic that defines routes, error codes or pagination.
- Partner, corporate and sandbox epics inherit the deprecation policy and any consumer notice obligations.
- Generated or curated OpenAPI under `docs/api/` becomes a reviewed artifact rather than a convenience.

## Compliance and evidence

- Contract tests that assert the problem-details shape and stable error codes.
- Review checks for route versioning, pagination bounds and header usage.
- OpenAPI review evidence per epic and per release.

## Open questions

- Whether internal-only contracts are versioned differently from externally consumed ones.
- Whether API version lifecycle is documented in `docs/api/` or in this ADR plus the epic documents.
- Whether a gateway or route-convention enforcement point exists.

## Approval gate

Do not publish a contract that claims stability to an external consumer, and do not publish partner or corporate API documentation, until this ADR is `Accepted`. Internal Phase 1 routes may continue as draft contracts.

## Related documents

- [ADR index](README.md)
- [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) - event schema versioning, which should stay consistent with this decision
- [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) - authentication headers and service identity
- [AGENTS.md](../../AGENTS.md) - `## APIs and contracts`
- [BACKEND_ARCHITECTURE.md](../architecture/BACKEND_ARCHITECTURE.md) - `## API conventions`

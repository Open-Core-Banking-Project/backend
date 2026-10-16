# Backend Agent Instructions

## Scope

These instructions apply to the entire `backend` repository. A more specific `AGENTS.md` in a child directory may add or tighten rules for that bounded context, but it must not weaken the financial-integrity, security, tenant-isolation, audit, or testing requirements defined here.

This repository implements a multi-institution core-banking backend. Treat correctness, traceability, security, and recoverability as product features. Prefer an explicit, easily reviewed implementation over a clever abstraction.

## Authoritative context

Read the following before making architectural or domain changes:

1. `README.md` for backend scope, domain ownership, and the C#/.NET technology decision.
2. `docs/architecture/BACKEND_ARCHITECTURE.md` for the target project structure, nine-service topology, database ownership, contracts, infrastructure, and implementation order.
3. `docs/implementation/README.md`, the applicable phase overview, and the applicable epic instructions for executable scope, sequence, contracts, tests, and acceptance gates.
4. `CONTRIBUTING.md` for contribution, review, security, and documentation expectations.
5. The applicable requirement and user story in:
   - `../Core_Banking_Software_Requirements_Specification.pdf`
   - `../Core_Banking_User_Stories.pdf`
   - `../Core_Banking_Blueprint.pdf`
6. Relevant ADRs, API contracts, event contracts, accounting rules, migrations, and tests once those directories exist.

When sources conflict, use this precedence:

1. The current user request or approved work item.
2. This file and any applicable child `AGENTS.md`.
3. Approved ADRs and versioned contracts.
4. The SRS and its financial invariants.
5. User stories and acceptance criteria.
6. The architecture blueprint.
7. The README and contribution guide.

The backend is entirely C#/.NET. Multi-language backend recommendations in older documents are superseded. Do not introduce Java, Spring Boot, Node.js, NestJS, Python, FastAPI, or another backend runtime without an approved architecture decision and an explicit change to this policy.

Do not invent regulatory rules, provider behaviour, accounting treatments, or institution policy. If an unresolved detail can affect money, customer access, regulatory evidence, privacy, or production recovery, surface it as a documented decision instead of silently guessing.

## Work method

Before editing:

1. Read all applicable `AGENTS.md` files.
2. Inspect the affected bounded context, contracts, tests, migrations, and recent patterns.
3. Identify the requirement IDs, user-story IDs, and financial or security invariants involved.
4. Check the working tree and preserve unrelated or user-authored changes.
5. Define the smallest complete change that satisfies the acceptance criteria.

While editing:

- Keep changes focused on the assigned task.
- Follow existing patterns unless they violate an explicit rule here.
- Update tests and documentation in the same change as behaviour.
- Prefer compile-time constraints and executable tests over prose-only safeguards.
- Record consequential or cross-cutting decisions in an ADR.
- Do not perform broad refactors while implementing a narrow feature unless the refactor is necessary and explained.

Before completion:

- Build the affected projects.
- Run the narrowest relevant tests, then the broader affected suite.
- Run formatting and static analysis configured by the repository.
- Inspect the diff for secrets, sensitive data, accidental generated files, and unrelated edits.
- Report commands run, results, assumptions, and any remaining risk.

Never claim a build, test, migration, or provider flow passed unless it was actually run successfully.

## Technology baseline

All production backend code uses C# and .NET.

Use the repository-pinned SDK and centrally managed package versions. Until the repository provides more specific configuration, target these practices:

- Enable nullable reference types.
- Enable implicit usings only when consistent across the solution.
- Treat compiler warnings and configured analyzer findings as errors in CI.
- Use deterministic, repeatable builds.
- Use ASP.NET Core for APIs and hosted workers.
- Use Entity Framework Core with PostgreSQL for transactional persistence.
- Use OpenAPI for HTTP contracts.
- Use OpenTelemetry for logs, metrics, and traces.
- Use Kafka-compatible messaging for durable integration events where asynchronous communication is justified.
- Use Redis only for non-authoritative caching, rate limiting, and carefully designed short-lived coordination.
- Use xUnit and Testcontainers for automated testing.

Do not add a package until existing framework capabilities and approved dependencies have been considered. For every new dependency, evaluate maintenance, licence, security history, transitive dependencies, operational impact, and whether the functionality belongs in shared code at all.

## Architecture

The backend uses a microservices architecture with domain-driven design, Clean Architecture boundaries, service-owned data, and explicit bounded contexts. The initial topology consists of nine coarse-grained, independently deployable microservices: Core Banking and Ledger; Customer and Onboarding; Identity and Access; Payments and VTU; Channels and Engagement; Operations and Workflow; Risk and Reporting; Integration Hub; and Audit and Reconciliation.

Use progressive decomposition. The remaining logical domains may begin as modules inside one of the nine initial microservices, but their boundaries must remain explicit so they can be extracted when independent scaling, isolation, ownership, availability, data ownership, or release cadence justifies a separate microservice. Do not interpret the microservices decision as permission to create one deployable service per domain immediately.

Sharing C# does not permit microservices or bounded contexts to share internal models, bypass contracts, or access one another's databases.

Prefer this project shape for a bounded context unless an approved ADR establishes another pattern:

```text
CoreBanking.<Context>.Domain
CoreBanking.<Context>.Application
CoreBanking.<Context>.Infrastructure
CoreBanking.<Context>.Api
CoreBanking.<Context>.Contracts
CoreBanking.<Context>.Tests
```

Dependency direction:

- `Domain` contains business concepts and invariants and does not reference ASP.NET Core, EF Core, messaging clients, provider SDKs, or infrastructure projects.
- `Application` coordinates use cases, transactions, authorisation requirements, and ports. It depends on `Domain`, not concrete infrastructure.
- `Infrastructure` implements persistence, messaging, time, cryptography, document storage, and external-provider adapters.
- `Api` is a thin transport layer. It authenticates, validates transport concerns, invokes application use cases, and maps results to versioned contracts.
- `Contracts` contains deliberately public request, response, and event schemas. It must not expose persistence entities or internal domain objects.

Additional rules:

- Do not reference another bounded context's infrastructure or database.
- Do not reuse EF Core entities as API or event contracts.
- Keep shared building blocks small, stable, and domain-neutral.
- Do not create a generic repository, generic service layer, or global domain model merely to reduce repeated code.
- Do not create a tenth or subsequent deployable microservice without an approved boundary and operational justification.
- Use synchronous calls only when an immediate response is required. Use durable events for confirmed facts and asynchronous cross-context work.
- Avoid distributed transactions. Use local transactions, outbox/inbox patterns, idempotency, reconciliation, and compensating actions.

## Non-negotiable financial invariants

These rules apply to every implementation and test touching money:

1. Only the protected Core Banking and Ledger context may authoritatively change customer balances.
2. Every posted journal has total debits equal to total credits in the same currency and accounting scope.
3. A posted financial entry is immutable. Corrections use linked reversal or adjustment entries.
4. Every financial command has a scoped idempotency key. Replaying the same key must return the original outcome without a second posting.
5. Transaction, journal, correlation, and external-provider references are unique and traceable.
6. Ledger balance, available balance, holds, liens, and blocks are distinct concepts.
7. Provider timeouts and ambiguous responses produce an `Uncertain` or equivalent state and a status enquiry; they do not justify blind retry, failover, reversal, or duplicate submission.
8. Fees, commissions, taxes, interest, penalties, and waivers are explicit accounting components.
9. Business date, transaction timestamp, posting date, and value date remain distinct.
10. Manual financial adjustments require authority, a reason, approval where configured, and immutable audit evidence.
11. Reports, caches, search indexes, and analytical stores are never authoritative balance sources.
12. Reconciliation exceptions remain visible until resolved, authorised, or formally written off.

Do not implement balance mutation as a direct arithmetic update from a controller, payment handler, provider callback, workflow, or reporting process.

## Money, time, and identifiers

- Use `decimal` for monetary arithmetic. Never use `float` or `double` for money, rates that affect postings, or financial totals.
- Represent money with an amount and an explicit ISO currency. Never infer currency from a tenant or account when processing a financial command.
- Apply rounding only through an explicit, tested institution/product/currency rule. Do not rely on implicit framework rounding.
- Persist monetary columns with an explicitly configured PostgreSQL precision and scale.
- Prefer strongly typed identifiers for important domain identities instead of passing unrelated `Guid` or `string` values interchangeably.
- Generate identifiers once and keep them stable across retries.
- Use `DateTimeOffset` or an approved time abstraction for timestamps.
- Use `DateOnly` for business, posting, and value dates when time-of-day is not part of the concept.
- Obtain current time through an injected `TimeProvider`; do not call `DateTime.Now` or `DateTime.UtcNow` inside domain logic.

## Tenancy and data ownership

- Every tenant-owned record and operation must carry a trusted institution identifier.
- Resolve tenant identity from authenticated or otherwise trusted infrastructure context, not an untrusted request-body field alone.
- Enforce tenant isolation in application authorisation and persistence queries. A UI filter is not a security boundary.
- Include cross-tenant negative tests for every tenant-owned resource.
- Never use a fallback tenant in production code.
- Each bounded context owns its schema or database and publishes controlled APIs or events.
- Database constraints, unique indexes, concurrency tokens, and foreign keys should enforce invariants that must survive concurrent requests.
- Data corrections must preserve the previous value and audit evidence.

## Commands, queries, and transactions

- Commands express an intended state change and return a stable result or identifier.
- Queries do not mutate business state.
- Validate transport shape at the boundary and business invariants in the domain/application layers.
- Keep transaction boundaries explicit and as small as correctness permits.
- A ledger journal and all of its lines must commit atomically.
- Use optimistic concurrency by default and stronger isolation or locking only when the invariant requires it.
- Document the concurrency strategy for balances, limits, sequences, approval transitions, and idempotency records.
- Accept and propagate `CancellationToken` for I/O and long-running operations. Do not cancel after an authoritative financial commit in a way that misreports the outcome.

Expected business rejections should use typed results or domain errors. Unexpected failures may use exceptions and must be translated at the boundary without leaking internals. HTTP APIs should use consistent RFC 7807 problem details with stable machine-readable error codes.

## APIs and contracts

- Version externally consumed APIs and events.
- Treat published contracts as compatibility commitments.
- Validate requests server-side regardless of frontend validation.
- Require correlation identifiers for all requests and idempotency identifiers for financial commands.
- Do not trust client-supplied amount calculations, fees, ownership, roles, approval status, settlement accounts, or final provider outcomes.
- Return explicit status models for asynchronous and uncertain operations.
- Use pagination and bounded filters for collection endpoints.
- Do not expose internal exception messages, stack traces, database keys, or provider secrets.
- Breaking changes require an approved versioning and migration plan.

## Events and messaging

- Name events as facts in the past tense.
- Publish only after the source state is durably committed, normally through a transactional outbox.
- Consumers must be idempotent and maintain inbox or deduplication state where duplication is possible.
- Include event ID, event type, schema version, occurred time, institution ID, correlation ID, causation ID, and aggregate/business reference where applicable.
- Do not place secrets or unnecessary personal data in events.
- Preserve ordering only for the smallest required business scope; do not assume global broker ordering.
- Define retry, backoff, dead-letter, replay, and poison-message handling before enabling a consumer in production.
- Replay must not create duplicate financial outcomes, audit records, notifications, or reconciliation breaks.

## External providers

- Put every provider behind a canonical adapter owned by the relevant context.
- Keep provider request/response models out of the domain layer.
- Configure timeouts, retry eligibility, circuit breaking, authentication, signature verification, and status enquiry explicitly.
- Retry only operations proven safe by idempotency or provider semantics.
- Verify webhook signature, source when applicable, timestamp, event identity, and replay status before processing.
- A redirect or webhook alone does not prove a final financial outcome; verify it with the provider when required.
- Store raw provider evidence securely when required for audit, but redact it from ordinary logs.
- Provider sandbox fixtures must use synthetic data and non-production credentials.

## Channels, USSD, chatbots, and CMS

- Channels and Engagement owns USSD sessions and menus, WhatsApp and website conversation orchestration, CMS content, knowledge articles, notification templates, campaigns, consent-aware delivery, and human handoff.
- Keep provider-specific WhatsApp, USSD gateway, SMS, and email protocols behind Integration Hub adapters and canonical contracts.
- USSD session state and chatbot conversation state are not authoritative banking state.
- USSD and chatbots may initiate controlled application commands but must never post balances or bypass the owning service's validation.
- Financial and sensitive channel actions require allow-listed intents or menu actions, explicit confirmation, idempotency, server-side permission checks, and step-up authentication where policy requires it.
- CMS content is versioned, localised, approved, effective-dated, and audited. Separate author, approver, and publisher permissions where configured.
- Do not send unrestricted generated chatbot output into privileged APIs or treat a phone number/chat identity as sufficient authentication.

## Roles and permissions

- Every protected endpoint, command, query, workflow action, channel operation, and CMS operation declares a stable capability permission.
- Check permissions such as `payment.transfer.initiate` or `cms.content.publish`; do not hard-code business behaviour to role names.
- Roles are configurable permission bundles owned by Identity and Access.
- Enforce institution, branch, channel, customer/account, corporate mandate, amount, currency, transaction-limit, device, and step-up scopes where applicable.
- Identity and Access owns grants and entitlement versions; each business service enforces access to its own resources.
- Deny by default when identity, permission, scope, or entitlement freshness cannot be proven.
- Service identities use separate service permissions and never inherit human roles.
- A maker cannot approve their own restricted action, and conflict-of-duty rules apply even when a user holds multiple roles.
- Sensitive authorisation decisions must produce audit evidence containing the permission and applicable scope without exposing secrets or unnecessary PII.

## Security and privacy

- Deny by default and enforce authorisation in every business service.
- Enforce least privilege, segregation of duties, maker-checker rules, branch scope, account ownership, mandates, and transaction limits server-side.
- A maker must not approve their own restricted action.
- Use approved framework cryptography and central key/secret management. Never design custom cryptographic algorithms.
- Never commit or log passwords, PINs, OTPs, access tokens, refresh tokens, private keys, provider secrets, complete BVNs/NINs, full account/card numbers, or real customer data.
- Mask sensitive fields in logs, traces, reports, errors, and test snapshots.
- Prevent mass assignment by mapping accepted contract fields explicitly.
- Use parameterised database access through approved libraries.
- Production data must not enter tests or non-production environments unless formally approved and irreversibly masked.
- Security-sensitive behaviour requires negative tests, audit evidence, and review.

If a secret is found, stop exposing it, report it, and require rotation. Deleting the visible string alone is not sufficient remediation.

## Audit and observability

- Audit records are business evidence, not debug logs.
- Record actor, role, institution, branch where applicable, action, timestamp, source, reason, correlation ID, and before/after values where policy permits.
- Audit privileged actions, approvals, overrides, configuration publication, sensitive-data access, provider administration, and manual financial operations.
- Ordinary administrators must not be able to edit or delete audit evidence.
- Use structured logging with stable event names; avoid interpolated sensitive payloads.
- Propagate trace and correlation context across HTTP, messaging, workflows, ledger postings, and provider calls.
- Emit metrics for business failures and financial exceptions as well as technical health.
- Health checks must distinguish liveness from readiness and must not leak sensitive details.

## Persistence and migrations

- Use Entity Framework Core migrations owned by the context that owns the affected schema.
- Migrations are append-only once released. Correct a released migration with a new migration.
- Never delete, rewrite, or truncate financial/audit data as part of an ordinary migration.
- Destructive or long-running changes require an explicit rollout, backfill, verification, and rollback/forward-fix plan.
- Define indexes and uniqueness constraints from known query and invariant requirements.
- Avoid loading unbounded result sets.
- Do not make network calls while holding a database transaction open.
- Seed only reference or synthetic development data; never embed credentials or real personal data.

## Testing requirements

Every behavioural change requires proportionate automated evidence.

Use:

- Unit tests for domain invariants, calculations, state transitions, and policies.
- Integration tests against real PostgreSQL, Redis, and broker-compatible dependencies through Testcontainers where relevant.
- Architecture tests for forbidden project and namespace dependencies.
- Contract tests for HTTP APIs, events, and provider adapters.
- End-to-end tests for critical journeys without real customer data or live provider side effects.

Financial tests must cover, where applicable:

- Successful posting.
- Unbalanced journal rejection.
- Insufficient available balance.
- Duplicate idempotency key, including concurrent duplicates.
- Concurrent debit or limit consumption.
- Reversal linked to the original transaction.
- Provider timeout and uncertain outcome.
- Safe retry and replay.
- Partial failure between local persistence and messaging.
- Authorisation and tenant-isolation failure.
- Rounding, boundary amounts, currency mismatch, and business-date boundaries.

Tests must be deterministic. Control time and identifiers, avoid arbitrary sleeps, and isolate external networks. A test that only asserts an HTTP status code is insufficient for a financial state change; assert ledger, idempotency, audit, and emitted-event outcomes as relevant.

## Documentation and traceability

For material behaviour, preserve traceability:

```text
Requirement ID -> User Story -> ADR/design -> API/event -> implementation -> tests -> operational evidence
```

- Mention applicable SRS and story IDs in the work item, test metadata/name where practical, or supporting documentation.
- Update OpenAPI and event schemas with implementation changes.
- Document new configuration, environment variables, migrations, background jobs, failure modes, dashboards, and runbooks.
- Use ADRs for decisions affecting service boundaries, data ownership, consistency, security, deployment, provider semantics, or financial processing.
- Add a child `AGENTS.md` when a bounded context needs durable rules that do not belong globally, such as ledger posting policy or payment state transitions.

Do not copy entire source documents into code comments or context files. Link to authoritative material and capture only the local decision needed to understand and verify the code.

## C# coding standards

- Follow repository formatting and `.editorconfig` when present.
- Use clear domain language and avoid abbreviations that obscure banking meaning.
- Prefer small cohesive types and explicit composition over deep inheritance.
- Prefer immutable value objects and records where their semantics fit.
- Keep public APIs minimal; default implementation types to `internal` when external access is unnecessary.
- Use asynchronous APIs for I/O. Do not wrap synchronous work in `Task.Run` on request paths.
- Dispose async resources correctly with `await using` where applicable.
- Do not use `async void` except genuine event handlers.
- Do not block on tasks with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in application code.
- Use cancellation and timeouts deliberately.
- Comments explain intent, invariants, regulatory/accounting rationale, or surprising trade-offs. They do not restate obvious syntax.
- Remove dead code instead of commenting it out.

## Prohibited shortcuts

Do not:

- Mutate balances outside the ledger posting path.
- Edit or delete posted journals.
- Treat a cache, report, search index, or provider response as the ledger source of truth.
- Retry an ambiguous financial request blindly.
- Use floating-point types for money.
- Trust tenant IDs, roles, fees, balances, ownership, or approval state supplied by a client.
- Share database tables across bounded contexts as an integration mechanism.
- Publish an event before its source transaction commits.
- Log complete request/response bodies by default.
- Disable security, audit, validation, or tests to make a feature pass.
- Hide an unresolved accounting or regulatory assumption inside implementation code.
- Add speculative abstractions or infrastructure for an unapproved future requirement.

## Definition of done

A backend change is complete only when:

- The approved requirement and acceptance criteria are implemented.
- Financial, security, tenant, and domain invariants are preserved.
- Relevant unit, integration, architecture, contract, and/or end-to-end tests pass.
- Formatting, build, analyzers, and applicable security checks pass.
- API/event contracts and migrations are reviewed for compatibility.
- Audit, observability, failure handling, idempotency, and reconciliation paths are addressed where applicable.
- Documentation, traceability, configuration, and runbooks are updated.
- No secrets, real customer data, temporary files, or unrelated edits are included.
- Remaining limitations and risks are stated explicitly.

When the repository contains a solution, the normal verification sequence should be equivalent to:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```

Use the repository's actual scripts and solution paths when they exist. Do not invent successful verification for scaffolding that has not been created yet.

## Review priorities

When reviewing code, prioritise findings in this order:

1. Duplicate, missing, unbalanced, mutable, or incorrectly authorised financial outcomes.
2. Cross-tenant access, privilege escalation, sensitive-data exposure, or cryptographic misuse.
3. Lost updates, race conditions, unsafe retries, replay errors, and transaction/outbox inconsistencies.
4. Contract breakage, migration risk, data loss, and unrecoverable operational behaviour.
5. Missing audit, reconciliation, observability, or test evidence.
6. Maintainability, performance, and style issues with concrete impact.

Do not approve a money-moving change solely because its happy path works.

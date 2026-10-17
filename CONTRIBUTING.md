# Contributing to Core Banking Platform

Thank you for your interest in contributing to the Core Banking Platform.

This repository is the backend of a volunteer, collaborative project focused on designing and building a secure, modern and reusable banking platform. All production backend services, APIs, workers, integrations and business logic in this repository use C# and .NET.

By contributing, you agree to follow the project standards, communicate respectfully and submit work that supports the goals of the project.

## 1. Volunteer Project Notice

This is currently an unpaid volunteer project.

Participation does not create an employment relationship, salary entitlement or guarantee of future payment. Contributors participate to learn, gain practical experience, collaborate with others and help build the platform.

Any future commercial arrangement, employment opportunity or paid engagement must be communicated separately and agreed to in writing.

## 2. Before You Contribute

Before starting work, contributors should:

1. Join the approved project communication channel.
2. Provide their GitHub username.
3. Select or receive an assigned project role.
4. Read `AGENTS.md`, `README.md` and the documentation relevant to the intended change.
5. Review the applicable requirement IDs, user stories, architecture decisions and contracts.
6. Install the repository-pinned .NET SDK and required local infrastructure.
7. Confirm that the affected projects build and their tests run locally.
8. Receive or select an approved task.
9. Understand the task requirements, acceptance criteria, dependencies and affected financial or security invariants.
10. Inform the team lead before beginning significant implementation.

Do not begin work on an unassigned major feature without discussing it with the relevant team lead.

## 3. Areas of Contribution

Contributions may include:

* C# and ASP.NET Core services, APIs and hosted workers
* Domain modelling and financial business rules
* Entity Framework Core and PostgreSQL persistence
* Kafka-compatible messaging and Redis integration
* External-provider and partner integrations
* OpenAPI and event-contract development
* Automated unit, integration, architecture, contract and end-to-end testing
* Cybersecurity, privacy and threat-modelling reviews
* Observability, DevOps and infrastructure
* Documentation, runbooks and architecture decisions
* Business analysis and requirement traceability
* Banking-domain and regulatory research
* Bug fixes, performance improvements and code reviews

Contributors should work within their assigned area unless permission is given to work across multiple areas.

## 4. Task Assignment

All contributions should be connected to an approved task or GitHub Issue.

Each task should contain:

* Task or Issue ID
* Title
* Description
* Service, application or workstream
* Acceptance criteria
* Priority
* Assignee
* Reviewer
* Dependencies
* Status

Before starting a task:

1. Confirm that the task is not already assigned.
2. Ask to be assigned or assign yourself if permitted.
3. Read the complete requirement.
4. Ask questions where the requirement is unclear.
5. Confirm any dependency on another team.

Do not duplicate another contributor’s work.

## 5. Repository Structure

This repository contains a C#/.NET microservices backend, including bounded contexts, shared building blocks, contracts, tests, documentation and deployment assets. The initial topology has nine coarse-grained, independently deployable microservices, including a Channels and Engagement service for USSD, WhatsApp chatbot, CMS, notifications and conversational sessions. Additional microservices are created only through justified progressive decomposition. Frontend and mobile applications live in separate repositories and follow their own contribution instructions.

Follow the existing repository structure and `AGENTS.md`. Do not create a new top-level folder, bounded context, microservice or shared library without an approved architectural reason.

## 6. Branching Rules

Do not work directly on the `main` branch.

Create a separate branch for every task.

Recommended branch formats:

```text
feature/TASK-0102-customer-onboarding
fix/TASK-0210-payment-reversal
test/TASK-0415-ledger-integration-tests
docs/TASK-0450-api-documentation
devops/TASK-0480-kubernetes-ingress
```

Use the following prefixes where appropriate:

* `feature/` for new features
* `fix/` for bug fixes
* `test/` for testing work
* `docs/` for documentation
* `refactor/` for code improvements without behavioural changes
* `devops/` for infrastructure and deployment work
* `security/` for approved security improvements

Keep branch names short, descriptive and connected to a task.

## 7. Commit Message Guidelines

Write clear and focused commit messages.

Recommended format:

```text
type: short description
```

Examples:

```text
feat: add corporate customer onboarding
fix: prevent duplicate transfer posting
test: add ledger transaction integration tests
docs: document payment reversal workflow
refactor: simplify beneficiary validation
chore: configure code formatting
security: mask customer identifiers in logs
```

Common commit types:

* `feat`
* `fix`
* `docs`
* `test`
* `refactor`
* `chore`
* `build`
* `ci`
* `security`
* `perf`

Avoid unclear messages such as:

```text
update
changes
work done
final fix
new code
```

Make small and meaningful commits where possible.

## 8. Pull Request Process

All code and major documentation changes must be submitted through a pull request.

Before opening a pull request:

1. Pull the latest changes from the target branch.
2. Resolve merge conflicts.
3. Restore and build the affected .NET solution or projects.
4. Run the relevant automated tests, starting with the narrowest affected suite.
5. Run `dotnet format --verify-no-changes` and configured analyzers.
6. Remove temporary files and debugging code.
7. Review your own changes.
8. Update documentation where necessary.
9. Confirm that no password, token or secret has been committed.

A pull request should contain:

* Related task or Issue ID
* Related SRS requirement and user-story IDs
* Summary of the change
* Reason for the change
* Main implementation decisions
* Exact verification commands and results
* Screenshots for visual changes
* Database migration information
* API or event compatibility information
* Known limitations
* Security considerations
* Reviewer information

Example pull request description:

```text
## Related Issue

Closes #102

## Summary

Implements individual customer duplicate detection during onboarding.

## Changes

- Added duplicate-check API
- Added matching by BVN, NIN, email and phone number
- Added audit events
- Added unit and integration tests

## Testing

- Tested exact BVN match
- Tested phone-number match
- Tested no-match scenario
- Tested unauthorised access

## Notes

This change depends on the identity-verification connector.
```

Do not merge your own pull request unless you are authorised to do so.

## 9. Code Review

Every significant contribution should be reviewed.

Reviewers should check:

* Correctness
* Security
* Readability
* Test coverage
* Architecture compliance
* Performance
* Error handling
* Data protection
* Documentation
* Backward compatibility
* Acceptance criteria

Contributors should respond respectfully to review comments.

When changes are requested:

1. Read every comment carefully.
2. Ask for clarification where necessary.
3. Make the required changes.
4. Reply to the review comment.
5. Request another review.

A rejected or changed pull request is part of normal software development and should not be treated as a personal criticism.

## 10. Coding Standards

Contributors should:

* Write clear and maintainable code.
* Use meaningful names.
* Keep functions and classes focused.
* Avoid unnecessary duplication.
* Follow the bounded-context and dependency rules in `AGENTS.md`.
* Validate external input.
* Handle errors consistently.
* Use ASP.NET Core dependency injection and configuration conventions.
* Enable and respect nullable reference type analysis.
* Use asynchronous APIs for I/O and propagate cancellation tokens.
* Obtain current time through `TimeProvider` instead of calling the system clock in domain logic.
* Add relevant logging.
* Write tests for important business logic.
* Document public APIs.
* Remove unused code.
* Avoid premature optimisation.
* Keep changes within the scope of the assigned task.

Use the repository's .NET formatter, compiler warnings and configured analyzers. Use centrally managed package versions when that configuration is present.

Do not reformat unrelated files as part of a small contribution.

## 11. Financial-System Rules

This project handles banking and financial concepts. Contributions must respect the following rules:

* Use decimal types for money.
* Do not use floating-point types for monetary calculations.
* Do not directly update account balances outside the approved ledger process.
* Financial postings must follow double-entry accounting.
* Debits and credits must remain balanced.
* Financial operations must support auditability.
* Every financial command must use a scoped idempotency key and must not create a second accounting outcome when replayed.
* Reversals must not erase the original transaction.
* Financial history should not be silently modified or deleted.
* Concurrency must be considered when changing balances or limits.
* Financial transaction states must be explicit and traceable.
* Provider timeouts or ambiguous responses must enter an uncertain state and be queried before retry, failover or reversal.
* Core Banking and Ledger is the sole authoritative source for customer balances. Payment services orchestrate requests but do not directly mutate balances.
* Business date, transaction timestamp, posting date and value date must remain distinct.
* Fees, taxes, commissions, interest and penalties must be represented as explicit accounting components.

Any change affecting the ledger, balances, interest, fees, settlements or transaction posting requires additional review.

## 12. Backend Requirements

Backend contributions use C# and .NET. A typical bounded context separates Domain, Application, Infrastructure, API and Contracts responsibilities as described in `AGENTS.md`.

Backend contributions should include, where applicable:

* Request and response models
* Input validation
* Authentication
* Authorisation
* Explicit role and permission requirements for every protected action
* Business rules
* Consistent error handling
* Database migrations
* Audit events
* Idempotency
* Logging
* Unit tests
* Integration tests
* Architecture and contract tests
* OpenAPI documentation
* Example requests and responses
* Event documentation
* Health checks

Domain projects must not depend on ASP.NET Core, Entity Framework Core, messaging clients or provider SDKs. API contracts must not expose EF Core entities or internal domain objects.

Avoid exposing internal implementation details through public APIs.

Breaking API changes must be discussed and approved before implementation.

## 13. Database Guidelines

Database changes must:

* Use Entity Framework Core migrations owned by the bounded context that owns the schema.
* Include rollback or forward-fix considerations.
* Preserve existing financial records.
* Configure appropriate PostgreSQL data types, including explicit precision and scale for money.
* Include required indexes.
* Consider query performance.
* Consider concurrent operations.
* Respect bounded-context database ownership.
* Protect personal and sensitive data.
* Avoid destructive changes without approval.
* Include test data only when appropriate.

Do not manually change shared, staging or production databases without approval.

Do not store passwords or secrets directly in database migration files.

Do not edit a released migration. Correct it with a new migration. Destructive or long-running migrations require an explicit rollout, data verification and recovery plan.

## 14. Frontend Contract Boundaries

Frontend and mobile applications are maintained in separate repositories. Backend contributors must provide stable, versioned and documented HTTP or event contracts for those clients.

Backend code must never rely on a frontend to enforce authorisation, tenant isolation, ownership, limits, fee calculation, approval state or other security and financial rules. Do not place backend secrets in examples or expose them through client-facing configuration.

## 15. API and Design Coordination

Changes affecting a user journey should be coordinated with the relevant frontend or design workstream. Backend pull requests must document new fields, validation rules, permissions, asynchronous states, error codes, compatibility considerations and example requests/responses.

Do not silently change a published contract to match an unapproved screen. Breaking changes require an approved versioning and migration plan.

## 16. Testing Requirements

Contributors must test their work before submitting it.

Testing may include:

* Unit tests
* Integration tests
* Architecture tests
* Contract tests
* API tests
* End-to-end tests
* Security tests
* Performance tests
* Accessibility tests
* Manual testing
* User acceptance testing

Features involving payments should test scenarios such as:

* Successful transaction
* Invalid request
* Insufficient balance
* Duplicate request
* Provider timeout
* Network failure
* Retry
* Reversal
* Unauthorised access
* Partial failure
* Concurrent requests
* Cross-tenant access attempts
* Currency mismatch and rounding boundaries
* Outbox or event-publication failure after local persistence

Tests must be deterministic and repeatable, must control time where relevant, and must not depend on real customer data or uncontrolled external networks. Financial state-change tests should assert ledger, idempotency, audit and emitted-event outcomes as applicable, not only an HTTP status code.

When a solution exists, the normal verification sequence is:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build --filter "Category!=Database"
dotnet format --verify-no-changes
```

`scripts/verify.ps1` runs exactly that sequence. Database tests are excluded because they need
the local infrastructure stack; `scripts/db-verify.ps1` starts nothing itself, applies the
migrations with the migration principal and then runs them against the running stack.

Pinned command-line tools live in `.config/dotnet-tools.json`. Run `dotnet tool restore` once per
clone before using `dotnet-ef`, and invoke it as `dotnet tool run dotnet-ef` so the pinned version
is used instead of any globally installed one.

Use the repository's actual solution paths and scripts. Do not report a command as successful unless it was run successfully.

## 17. Security Guidelines

Security is everyone’s responsibility.

Never commit:

* Passwords
* API keys
* Access tokens
* Private certificates
* Database credentials
* Encryption keys
* Production configuration
* Real customer data

Use:

* Environment variables
* Approved secrets management
* Least-privilege access
* Authentication and authorisation
* Encryption in transit
* Encryption at rest
* Input validation
* Audit logging
* Data masking
* Secure error messages

Do not include full BVNs, NINs, card numbers, account numbers, passwords or personal information in application logs.

If a secret is accidentally committed:

1. Notify the maintainers immediately.
2. Revoke or rotate the secret.
3. Remove it from the repository history where necessary.
4. Document the incident.

Do not publicly disclose security vulnerabilities. Follow the instructions in `SECURITY.md`.

## 18. Test Data

Use only:

* Fictional data
* Generated data
* Properly anonymised data
* Approved sandbox credentials

Do not use real customer, employee or partner information.

Test account numbers, names, phone numbers and identity numbers should be clearly fictional.

## 19. Documentation Requirements

Update documentation whenever a contribution changes:

* APIs
* Database structure
* Events
* Configuration
* Deployment
* User behaviour
* Business rules
* Environment variables
* Security requirements

Documentation may include:

* README files
* API documentation
* Architecture decisions
* Sequence diagrams
* Database diagrams
* Event catalogues
* User guides
* Deployment guides
* Runbooks
* Release notes

Undocumented complex features may be returned for revision.

## 20. Definition of Done

A task is complete only when:

* The approved requirement has been implemented.
* Acceptance criteria have been satisfied.
* The affected .NET projects build and relevant tests have passed.
* Formatting and configured analyzers pass.
* The contribution has been reviewed.
* Requested changes have been addressed.
* Documentation has been updated.
* Security concerns have been addressed.
* Database migrations have been reviewed.
* API and event compatibility has been reviewed.
* Audit, idempotency, failure handling, observability and reconciliation paths have been addressed where applicable.
* No critical blocker remains.
* The task tracker has been updated.
* The change has been merged into the approved branch.

Code submission alone does not mean a task is complete.

## 21. Progress Updates

Contributors should update their task status regularly.

Recommended statuses:

* Not Started
* In Progress
* Blocked
* In Review
* Testing
* Completed
* On Hold

A useful progress update should include:

* What was completed
* What is currently being worked on
* What will be done next
* Any blocker
* Any decision required

Example:

```text
Completed the customer profile database migration and API models.

I am currently implementing corporate beneficial-owner validation.

I am blocked by the final list of mandatory corporate KYC fields and need confirmation from the business-analysis team.
```

Report blockers early. Do not wait until the due date before informing the team.

## 22. Communication and Conduct

All contributors must:

* Communicate respectfully.
* Give constructive feedback.
* Avoid harassment or discrimination.
* Respect different skill levels.
* Help other contributors where possible.
* Avoid insulting or dismissive comments.
* Credit the work of others.
* Avoid presenting another person’s work as their own.
* Resolve disagreements professionally.
* Follow the project’s `CODE_OF_CONDUCT.md`.

Repeated misconduct may result in removal from the project.

## 23. Inactive Contributors

This is a volunteer project, and contributors may sometimes become unavailable.

However, contributors should notify the team when they cannot continue or complete an assigned task.

A task may be reassigned when:

* The contributor is inactive without notice.
* Progress updates are repeatedly missing.
* The task remains blocked without communication.
* Deadlines are repeatedly missed.
* The contributor confirms they are no longer available.

Repository or project access may be reduced or removed after extended inactivity.

This is intended to keep the project organised and does not prevent someone from requesting to participate again later.

## 24. Ownership and Licensing

Unless otherwise agreed in writing, contributions submitted to this project will be licensed under the same licence as the repository.

By submitting a contribution, you confirm that:

* You created the contribution or have permission to submit it.
* The contribution does not knowingly violate another person’s copyright.
* The contribution may be distributed under the repository’s licence.
* You understand that the project may be used, modified and distributed according to that licence.

Do not submit proprietary code copied from an employer, client, paid course, private repository or commercial product.

Third-party libraries and copied code must have compatible licences and proper attribution.

## 25. Artificial Intelligence Tools

Contributors may use approved AI tools to assist with research, coding, testing or documentation.

However:

* You remain responsible for everything you submit.
* Give the tool the approved task, acceptance criteria and relevant repository context; do not ask it to invent missing banking policy.
* Ensure coding agents read the applicable `AGENTS.md` files before changing code.
* Review AI-generated code before committing it.
* Do not submit code you do not understand.
* Do not share secrets or confidential information with AI tools.
* Check AI-generated code for security issues.
* Check licences before using generated or suggested dependencies.
* Test all AI-assisted changes.
* Verify all claims about builds, tests, providers, migrations and external documentation.
* Disclose significant AI-generated contributions where required by the maintainers.

AI-generated output should not replace proper technical review.

## 26. Dependency Guidelines

Before adding a new library or package:

1. Confirm that the functionality is not already available.
2. Check whether the package is actively maintained.
3. Review its licence.
4. Review known security vulnerabilities.
5. Consider package size and performance.
6. Discuss major dependencies with maintainers.
7. Lock or pin versions according to repository standards.

Avoid adding a dependency for functionality that can be implemented safely with a small amount of existing code.

## 27. Reporting Bugs

When reporting a bug, include:

* Clear title
* Environment
* Service or application
* Steps to reproduce
* Expected behaviour
* Actual behaviour
* Screenshots or logs where safe
* Frequency
* Severity
* Possible cause, when known

Remove secrets and personal data from logs before sharing them.

## 28. Suggesting Features

Before proposing a major feature:

1. Search existing Issues.
2. Confirm that the feature has not already been planned.
3. Explain the banking or user problem.
4. Identify affected users.
5. Describe the expected outcome.
6. Identify affected services or applications.
7. Mention potential security or regulatory concerns.
8. Wait for approval before implementation.

Large architectural changes may require an architecture decision record.

## 29. Getting Help

Ask for help when:

* Requirements are unclear.
* You do not understand the banking concept.
* You cannot run the project locally.
* You encounter an unfamiliar error.
* Your task depends on another service.
* You discover a security concern.
* You believe the architecture needs to change.
* You cannot meet an agreed deadline.

Use the appropriate project communication channel and include enough information for someone to assist you.

## 30. Recognition

Contributions may be recognised through:

* GitHub contribution history
* Project documentation
* Contributor listings
* Release notes
* Demonstration opportunities
* Recommendation letters, where appropriate and deserved
* Leadership opportunities within the volunteer team

Recognition depends on meaningful, traceable and reviewed contributions.

## 31. Contributor Checklist

Before submitting a pull request, confirm:

* [ ] My work is linked to an approved task or Issue.
* [ ] I read the applicable `AGENTS.md` and linked the relevant requirement and user-story IDs.
* [ ] I used the correct branch naming format.
* [ ] My code follows the project standards.
* [ ] The affected .NET projects build locally.
* [ ] Relevant automated tests pass.
* [ ] I added or updated tests where required.
* [ ] Formatting and configured analyzers pass.
* [ ] I updated relevant documentation.
* [ ] I did not commit secrets or sensitive data.
* [ ] I reviewed my own changes.
* [ ] I documented migrations and configuration changes.
* [ ] I documented API or event compatibility where applicable.
* [ ] I described known limitations.
* [ ] I am ready to respond to review feedback.

## 32. Agreement

By contributing to the Core Banking Platform, you agree to:

* Follow this contribution guide.
* Follow the Code of Conduct.
* Follow the repository licence.
* Protect confidential information.
* Respect project security requirements.
* Communicate honestly about progress and blockers.
* Submit original or properly licensed work.
* Participate professionally and respectfully.

Thank you for contributing to the Core Banking Platform.

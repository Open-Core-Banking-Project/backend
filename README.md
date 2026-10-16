# Core Banking Platform — Backend Services

The Core Banking Platform backend uses a microservices architecture designed to support commercial banks, microfinance banks, fintech companies, agency banking operations and digital financial services. It begins with coarse-grained services and uses progressive decomposition so that domain boundaries are preserved without creating unnecessary operational complexity.

The platform covers customer onboarding, identity management, account management, ledger posting, payments, lending, cards, reconciliation, fraud monitoring, reporting, integrations and other banking operations.

This repository contains the backend services, shared libraries, infrastructure contracts, tests and development tooling required to build the platform.

---

## Project status

This project is currently under active development by a volunteer team.

The initial goal is to build a secure and functional minimum viable platform while keeping clear service boundaries that can support future commercial-bank deployments.

This project is not yet intended for production use with real customer funds or personal data.

---

## Objectives

The backend platform is designed to:

* Support multiple financial institutions
* Isolate each institution’s data and configuration
* Provide secure and auditable financial operations
* Maintain a double-entry general ledger
* Support account, payment, lending and settlement workflows
* Integrate with external banking and payment providers
* Support web, mobile, corporate and agent-banking channels
* Provide clear service ownership and independent deployment
* Support regulatory reporting and audit requirements
* Remain extensible across different countries and banking models

---

## Architecture principles

The project follows these principles:

* Domain-driven design
* Clean Architecture
* Microservices with coarse-grained initial service boundaries
* Clear bounded contexts
* Service-owned databases
* Event-driven communication
* API-first development
* Secure-by-default implementation
* Immutable financial audit trails
* Idempotent financial operations
* Strong authentication and authorisation
* Fine-grained role and permission enforcement in every service
* Automated testing
* Observability and operational readiness
* Infrastructure as code
* Independent service deployment

No service may directly alter balances outside the approved Core Banking and Ledger service.

---

## Technology strategy

All production backend services, APIs, workers, integrations and business logic are implemented in C# on .NET. Using one backend language gives the platform consistent architecture, security controls, observability, testing, deployment and operational practices across every banking domain.

The standard backend stack is:

* A repository-pinned, supported .NET SDK
* C# with nullable reference types enabled
* ASP.NET Core for HTTP APIs, authentication, authorisation and hosted services
* Entity Framework Core with PostgreSQL for transactional persistence
* OpenAPI for versioned API contracts
* OpenTelemetry for logs, metrics and distributed traces
* Kafka-compatible messaging for durable business events
* Redis for approved caching, rate limiting and short-lived coordination
* xUnit for automated tests
* Testcontainers for integration and contract tests against real infrastructure dependencies
* Built-in .NET configuration, dependency injection, health checks and resilience features

The C# requirement applies to core banking, ledger, identity, onboarding, payments, cards, lending, treasury, workflows, notifications, integrations, fraud and AML processing, reporting, reconciliation, settlement, audit and operational tooling.

Bounded contexts must remain explicit even though they share a language and runtime. They must not bypass service contracts, access another context's database directly or move financial posting outside the protected Core Banking and Ledger boundary.

---

## Backend services

The platform is organised into 20 logical service domains.

Not every logical domain must be deployed as a separate service in the first release.

---

### 1. Institution and Platform Configuration

Recommended framework:

* ASP.NET Core

Responsibilities:

* Institution setup
* Legal-entity configuration
* Country packs
* Licence configuration
* Branches and regions
* Supported currencies
* Holidays and working days
* Chart-of-accounts templates
* Institution branding
* Feature flags
* Product eligibility rules
* Policy configuration
* Tenant isolation
* Database provisioning references
* Encryption-key references
* API-client registration
* Integration credential registration

---

### 2. Identity and Access Management

Recommended framework:

* ASP.NET Core with an approved OpenID Connect and OAuth 2.0 identity provider

Responsibilities:

* Customer identities
* Staff identities
* Agent identities
* Corporate-user identities
* Administrator identities
* Login and password policies
* Multi-factor authentication
* Biometric registration
* Trusted-device registration
* Token issuance
* Session management
* Roles and permissions
* Role-permission assignments scoped by institution, branch, channel, account, mandate, action and transaction limit
* Branch access
* Institution access
* Corporate maker-checker roles
* Account lockout
* Suspicious-login detection
* Access reviews
* Consent management
* Service-to-service authentication

This service verifies identity.

Each business service must still enforce its own permissions.

---

### 3. Customer, Party and Onboarding

Recommended framework:

* ASP.NET Core

Responsibilities:

* Individual customer profiles
* Corporate customer profiles
* Contact information
* Addresses
* Next of kin
* Beneficiaries
* Customer relationships
* Directors
* Signatories
* Shareholders
* Beneficial owners
* Customer segmentation
* Risk classification
* Account-opening workflows
* KYC tiers
* Document collection
* BVN verification
* NIN verification
* Tax verification
* Address verification
* Customer lifecycle management
* Duplicate detection
* Customer merge workflows

---

### 4. Core Banking and Ledger

Recommended framework:

* ASP.NET Core

This is the protected financial core of the platform.

Responsibilities:

* Account lifecycle
* Savings accounts
* Current accounts
* Fixed-deposit accounts
* Wallet accounts
* Loan accounts
* Double-entry general ledger
* Journal posting
* Accounting rules
* Ledger balance
* Available balance
* Holds
* Liens
* Blocks
* Transaction authorisation
* Idempotency
* Reversals
* Corrections
* Chargebacks
* Fees
* Commissions
* Taxes
* Interest accrual
* Interest capitalisation
* Penalties
* Standing instructions
* Scheduled financial entries
* End-of-day processing
* End-of-period processing
* Account statements
* Transaction history
* Product rules
* Limits
* Pricing references

No external service may update an account balance directly.

All financial postings must be processed through this service.

---

### 5. Deposit Products and Pricing

Recommended framework:

* ASP.NET Core

Responsibilities:

* Savings products
* Current-account products
* Fixed deposits
* Term deposits
* Call deposits
* Notice deposits
* Target savings
* Children’s accounts
* Student accounts
* Cooperative savings
* Tiered interest rates
* Minimum balances
* Maximum balances
* Withdrawal restrictions
* Premature liquidation
* Deposit rollover
* Withholding tax
* Dormancy rules
* Reactivation rules
* Product pricing
* Fee rules
* Commission rules
* Customer-segment pricing
* Promotional pricing
* Waivers
* Product versioning
* Effective dates

This service uses the same C#/.NET engineering standards as the Core Banking and Ledger service while preserving its own domain boundary.

---

### 6. Payments, Bills, VTU and Virtual Accounts

Recommended framework:

* ASP.NET Core

Responsibilities:

* Internal transfers
* Interbank transfers
* International transfer orchestration
* NIP integration
* Payment-switch integration
* Beneficiary management
* Transfer limits
* Bulk payments
* Payroll payments
* Scheduled transfers
* Standing orders
* Direct debit
* Mandates
* Bill payments
* Airtime
* Mobile data
* Electricity tokens
* Cable television payments
* Internet payments
* Education payments
* QR payments
* Merchant payments
* Payment status tracking
* Timeouts
* Retries
* Reversals
* Provider settlement
* Provider failover
* VTU reconciliation
* Static virtual accounts
* Dynamic virtual accounts
* Collection matching
* Split settlement

This service requests financial postings from the Core Banking and Ledger service.

---

### 7. Cards, ATM, POS and Merchant Acquiring

Recommended framework:

* ASP.NET Core

Responsibilities:

* Debit cards
* Prepaid cards
* Virtual cards
* Credit cards
* Card issuance
* Card activation
* PIN management
* Card replacement
* Card controls
* Card limits
* Merchant-category restrictions
* Geographic restrictions
* ATM transactions
* POS transactions
* Online card payments
* Contactless payments
* Card authorisation
* Card settlement
* Chargebacks
* Disputes
* Card fraud cases
* Tokenised-wallet integrations
* Terminal management
* Merchant onboarding
* Merchant KYC
* Merchant discount rates
* Merchant settlement
* Split settlements
* Merchant risk monitoring
* Merchant statements

---

### 8. Lending, Collateral, Collections and Recovery

Recommended framework:

* ASP.NET Core

Responsibilities:

* Loan product configuration
* Loan applications
* Loan origination
* Credit-assessment requests
* Loan approval
* Maker-checker workflows
* Loan disbursement
* Repayment schedules
* Auto-debit
* Early settlement
* Interest calculation
* Penalties
* Restructuring
* Refinancing
* Write-offs
* Delinquency management
* Collections
* Recovery
* Collateral
* Guarantors
* Loan documentation
* Property collateral
* Vehicle collateral
* Cash collateral
* Valuation
* Revaluation
* Insurance tracking
* Loan-to-value calculation
* Promise-to-pay
* Field recovery
* Post-write-off recovery
* Microfinance group loans
* Savings-linked loans

Loan disbursement and repayment posting must go through the Core Banking and Ledger service.

---

### 9. Treasury, Liquidity, Foreign Exchange and Investments

Recommended framework:

* ASP.NET Core

Responsibilities:

* Daily liquidity positions
* Cash-flow forecasting
* Asset-liability maturity gaps
* Liquidity coverage monitoring
* Interest-rate risk
* Foreign-exchange positions
* Exchange rates
* Customer-specific rates
* Multi-currency accounts
* Currency conversion
* FX gains and losses
* FX revaluation
* Interbank placements
* Interbank borrowings
* Treasury bills
* Bonds
* Securities
* Investment portfolios
* Counterparty limits
* Funds-transfer pricing
* Nostro liquidity monitoring
* Treasury approval workflows
* Treasury settlement

---

### 10. Trade Finance and Correspondent Banking

Recommended framework:

* ASP.NET Core

Responsibilities:

* Letters of credit
* Bank guarantees
* Bills for collection
* Documentary collections
* Import finance
* Export finance
* Shipping-document tracking
* Trade limits
* Margin requirements
* Trade collateral
* Charges and commissions
* Amendments
* Expiry workflows
* SWIFT integration
* Trade settlement
* Correspondent-bank relationships
* Nostro accounts
* Vostro accounts
* Payment routing
* Intermediary banks
* Payment repair queues
* Payment investigations
* Payment recall
* Sanctions screening

---

### 11. Risk, Fraud, AML and Sanctions

Recommended framework:

* ASP.NET Core

Responsibilities:

* Transaction-monitoring rules
* Behavioural fraud detection
* Velocity-based fraud checks
* Device-risk scoring
* Login-risk scoring
* Sanctions screening
* PEP screening
* Watchlist screening
* AML alerts
* Case prioritisation
* Suspicious-activity investigation
* Credit-risk scoring
* Affordability models
* Fraud-case integration
* Explainable decisions
* Model monitoring
* Model drift detection
* False-positive analysis

Risk services may recommend, challenge or block an operation.

Final financial posting remains controlled by the Core Banking and Ledger service.

---

### 12. Operations, Workflow and Case Management

Recommended framework:

* ASP.NET Core

Responsibilities:

* Maker-checker workflows
* Approval workflows
* Exception requests
* Override requests
* Customer complaints
* Service tickets
* Account freeze requests
* Account unfreeze requests
* Disputes
* Chargeback cases
* Failed-payment investigations
* Fraud work queues
* AML work queues
* Loan-collection cases
* SLA tracking
* Escalation
* Staff assignment
* Internal notes
* Attachments
* Approval evidence

---

### 13. Reconciliation, Settlement and Finance Operations

Recommended framework:

* ASP.NET Core

Responsibilities:

* NIBSS reconciliation
* Payment-switch reconciliation
* Card reconciliation
* ATM reconciliation
* POS reconciliation
* Bill-payment settlement
* VTU reconciliation
* Suspense-account management
* Unmatched-entry investigation
* General-ledger balancing
* Nostro reconciliation
* External-bank reconciliation
* Settlement-file ingestion
* Settlement-file validation
* Trial balance
* Financial close
* Exception reporting
* Accounting-period controls
* Manual adjustment workflows

---

### 14. Cash, Branch, Agent and Microfinance Operations

Recommended framework:

* ASP.NET Core

Responsibilities:

* Branch tills
* Vault management
* Cash deposits
* Cash withdrawals
* Cash transfers
* Teller transactions
* End-of-day teller balancing
* Agent registration
* Agent limits
* Agent commissions
* Agent float
* Assisted agency transactions
* Field collections
* Field disbursement
* Offline agent submissions
* Microfinance groups
* Cooperatives
* Savings circles
* Cheque-book issuance
* Cheque serial management
* Inward clearing
* Outward clearing
* Stop-payment instructions
* Returned cheques
* Bank drafts

---

### 15. Corporate and Business Banking

Recommended framework:

* ASP.NET Core

Responsibilities:

* Corporate profiles
* Subsidiaries
* Authorised signatories
* Corporate mandates
* Maker-checker payments
* Payroll
* Bulk-payment files
* Payment templates
* Beneficiary management
* Virtual accounts
* Collection accounts
* Cash pooling
* Account sweeping
* Concentration accounts
* Escrow accounts
* Corporate limits
* Corporate APIs
* Host-to-host file integration
* Corporate statements
* Reconciliation files

---

### 16. Engagement, Notifications and Conversational Channels

Recommended framework:

* ASP.NET Core

Responsibilities:

* SMS notifications
* Email notifications
* Push notifications
* In-app notifications
* OTP delivery
* Delivery-status tracking
* Transaction alerts
* Statement alerts
* Campaigns
* Customer segmentation
* Consent-aware messaging
* Secure in-app chat
* USSD session orchestration
* USSD menu and journey management
* WhatsApp chatbot
* Website chatbot
* Content management for help, product, campaign, menu and knowledge-base content
* Content versioning, localisation, approval and publication
* FAQ search
* Knowledge-base search
* Human-agent handoff
* Conversation history
* Support ticket creation

Chatbots must use controlled APIs and must not have unrestricted access to banking data.

---

### 17. Documents, Statements and Records

Recommended framework:

* ASP.NET Core

Responsibilities:

* Account statements
* Receipts
* Account confirmations
* Certificates
* Loan agreements
* KYC document storage
* Document verification states
* Secure download links
* E-signature integration
* Retention policies
* Legal hold
* Document templates
* Audit-ready document retrieval
* Malware scanning
* Document classification

---

### 18. Reporting, Regulatory Intelligence and Data Governance

Recommended framework:

* ASP.NET Core

Responsibilities:

* Operational dashboards
* Finance reports
* Branch reports
* Agent reports
* Loan reports
* Payment reports
* Product profitability
* Customer profitability
* Regulatory reports
* Data-quality checks
* Data warehouse pipelines
* Scheduled report generation
* Secure report distribution
* Management analytics
* Risk reporting
* Model reporting
* Data dictionaries
* Business glossaries
* Data ownership
* Data lineage
* Data classification
* Master data
* Reference data
* Retention rules

Reporting services must consume approved events or replicated data.

They must not become the source of truth for account balances.

---

### 19. Integration Hub and Developer Platform

Recommended framework:

* ASP.NET Core

Responsibilities:

* External API gateway
* Partner APIs
* Open-banking APIs
* Webhooks
* Webhook delivery tracking
* NIBSS connectors
* KYC connectors
* BVN and NIN connectors
* Card connectors
* Payment-provider connectors
* SMS and email connectors
* Provider credential management
* API versioning
* API throttling
* Quotas
* API monitoring
* Partner sandbox
* SDKs
* Integration documentation
* Connector health checks
* Failover routing
* Partner onboarding
* Partner certification

---

### 20. Audit, Security, Resilience and Observability

Recommended framework:

* ASP.NET Core, with platform observability tools

Responsibilities:

* Immutable audit logs
* Staff-action logging
* Approval logging
* Sensitive-data access logging
* Security events
* Financial-event traceability
* Reconciliation alerts
* Operational alerts
* Monitoring dashboards
* Incident notifications
* Retention
* Archival
* Legal-evidence export
* Backup verification
* Disaster-recovery evidence
* Incident management
* Recovery-time objectives
* Recovery-point objectives
* Failover testing
* Operational resilience exercises

---

## Initial microservices topology

The backend is a microservices system, but the project must not begin with 20 independently deployed services. Logical business domains may remain modules inside a coarse-grained microservice until there is a justified reason to extract them.

The initial deployment target contains nine independently deployable microservices:

1. Core Banking and Ledger
2. Customer and Onboarding
3. Identity and Access
4. Payments and VTU
5. Channels and Engagement
6. Operations and Workflow
7. Risk and Reporting
8. Integration Hub
9. Audit and Reconciliation

The remaining domains initially exist as internal modules with explicit boundaries inside one of these microservices.

A module should become an independent microservice when it requires:

* Independent scaling
* Stronger security isolation
* Independent deployment
* A dedicated team
* Separate data ownership
* Separate release schedules
* Different availability requirements

---

## Suggested repository structure

The detailed project structure, microservice ownership, database topology, logical tables, contracts, infrastructure, testing model, and implementation order are defined in [`docs/architecture/BACKEND_ARCHITECTURE.md`](docs/architecture/BACKEND_ARCHITECTURE.md).

Phase and epic implementation instructions are maintained in [`docs/implementation/README.md`](docs/implementation/README.md). Begin with the Phase 0 engineering foundation and use the epic template before implementing Phase 1 banking capabilities.

```text
backend/
├── src/
│   ├── BuildingBlocks/
│   └── Services/
│       ├── CoreLedger/
│       ├── CustomerOnboarding/
│       ├── IdentityAccess/
│       ├── PaymentsVtu/
│       ├── ChannelsEngagement/
│       ├── OperationsWorkflow/
│       ├── RiskReporting/
│       ├── IntegrationHub/
│       └── AuditReconciliation/
├── tests/
├── docs/
├── deploy/
├── scripts/
├── .github/
├── AGENTS.md
├── CONTRIBUTING.md
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── CoreBanking.sln
├── LICENSE
└── README.md
```

Changes to this structure require an approved architectural reason and must preserve the nine-service topology and bounded-context rules.

---

## Shared platform infrastructure

The backend services may depend on the following shared infrastructure.

### PostgreSQL

Used for persistent relational data.

Guidelines:

* Each service should own its database or schema.
* Services must not directly query another service’s private tables.
* The Core Ledger should use a dedicated protected database.
* Database migrations must be version-controlled.
* Financial data must be backed up and tested for restoration.

### Kafka

Used for confirmed business events.

Examples:

* CustomerCreated
* AccountOpened
* FundsReserved
* TransactionPosted
* PaymentCompleted
* PaymentReversed
* LoanDisbursed
* RepaymentReceived
* FraudAlertCreated
* SettlementCompleted

Events must be versioned and documented.

### Redis

Used for:

* Caching
* Rate limiting
* Temporary sessions
* Distributed locks
* Idempotency keys
* Short-lived transaction reservations

Redis must not become the permanent source of truth for financial records.

### Object storage

Used for:

* KYC documents
* Statements
* Receipts
* Agreements
* Settlement files
* Regulatory files
* Approved attachments

### Search platform

May be used for:

* Customer search
* Operational search
* Document search
* Audit search

Search indexes must not be treated as the source of truth for balances or financial postings.

### Secrets and key management

Used for:

* Database credentials
* Provider credentials
* Encryption keys
* Signing keys
* Certificates
* API secrets

Secrets must never be committed to the repository.

---

## Communication between services

Services may communicate using:

* REST APIs for immediate request-response operations
* Kafka events for asynchronous business communication
* Webhooks for external partner notifications
* Scheduled files for providers that require batch processing

Use synchronous communication when an immediate response is required.

Use events when:

* Multiple services need to react
* The operation can be processed asynchronously
* Loose coupling is preferred
* Event replay may be useful
* The event represents a confirmed business fact

---

## Financial transaction rules

All financial implementations must follow these rules:

1. Use decimal types for money.
2. Do not use floating-point values for financial amounts.
3. Every ledger posting must balance.
4. Financial requests must support idempotency.
5. Original transactions must not be deleted during reversal.
6. Reversals must create traceable compensating entries.
7. Account balances must only be changed by the Core Ledger.
8. Every financial operation must produce an audit trail.
9. Concurrent balance updates must be handled safely.
10. Posting dates, transaction dates and value dates must be stored clearly.
11. External provider timeouts must not automatically mean failure.
12. Retry logic must not create duplicate postings.
13. Sensitive financial information must not appear in logs.
14. Ledger history must be immutable after posting, except through controlled adjustments.

---

## API standards

Every API should provide:

* Clear resource names
* Versioned routes
* Request validation
* Consistent response structures
* Consistent error structures
* Authentication
* Authorisation
* Correlation IDs
* Idempotency support where required
* Pagination for large collections
* OpenAPI documentation
* Health checks
* Readiness checks
* Structured logs
* Audit events where applicable

Example route structure:

```text
/api/v1/customers
/api/v1/accounts
/api/v1/payments
/api/v1/loans
/api/v1/transactions
```

Avoid exposing database table names directly through API routes.

---

## Error response example

```json
{
  "type": "https://errors.corebanking.example/insufficient-funds",
  "title": "Insufficient funds",
  "status": 422,
  "code": "INSUFFICIENT_FUNDS",
  "detail": "The available balance is insufficient for this transaction.",
  "correlationId": "7c6a8d22-16f3-43e7-b061-a222a48ca551",
  "timestamp": "2026-07-29T08:00:00Z"
}
```

Do not expose stack traces, database details or secrets in API responses.

---

## Authentication and authorisation

The platform uses authentication plus fine-grained, server-side roles and permissions. It supports:

* OAuth 2.0
* OpenID Connect
* JWT access tokens
* Refresh tokens
* Multi-factor authentication
* Device registration
* Fine-grained role-based access control
* Explicit permission checks for every protected action
* Role-permission assignments scoped by institution, branch, channel, account, mandate and transaction limit
* Institution-level access
* Branch-level access
* Customer ownership checks
* Maker-checker permissions
* Service-to-service credentials

Authentication confirms identity.

Authorisation determines whether that identity can perform a specific action.

Every protected endpoint, command and workflow action must declare and enforce its required permission. Every service must enforce authorisation independently; authentication at a gateway or frontend is not sufficient.

---

## Local development requirements

Recommended tools:

* Git
* Docker
* Docker Compose
* .NET SDK
* PostgreSQL client
* Redis client
* Kafka tools
* Postman or Bruno
* IDE or code editor

Suggested versions:

```text
.NET SDK: repository-pinned supported version
PostgreSQL: 16 or later
Redis: 7 or later
Kafka: current supported version
Docker: current stable version
```

---

## Running the platform locally

The final commands will depend on the repository structure.

A typical environment may be started with:

```bash
docker compose up -d
```

Check the running containers:

```bash
docker compose ps
```

View logs:

```bash
docker compose logs -f
```

Stop the environment:

```bash
docker compose down
```

Each service should provide its own local setup instructions.

---

## Environment configuration

Use environment variables for runtime configuration.

Example:

```env
APP_ENVIRONMENT=Development
APP_PORT=5000

DATABASE_HOST=localhost
DATABASE_PORT=5432
DATABASE_NAME=core_banking
DATABASE_USERNAME=core_banking_user
DATABASE_PASSWORD=change-me

REDIS_HOST=localhost
REDIS_PORT=6379

KAFKA_BOOTSTRAP_SERVERS=localhost:9092

IDENTITY_AUTHORITY=http://localhost:8080
IDENTITY_AUDIENCE=core-banking-api
```

Do not commit real environment files.

Provide a safe example file:

```text
.env.example
```

---

## Database migrations

Every database change must use Entity Framework Core migrations owned by the bounded context that owns the affected schema.

Do not make undocumented manual changes to shared databases.

Migration pull requests should explain:

* What changed
* Why it changed
* Whether existing data is affected
* Whether the migration is reversible
* Whether downtime is required
* Whether indexes are added or removed

---

## Testing strategy

Backend services should include:

### Unit tests

Used for:

* Domain rules
* Interest calculations
* Fee calculations
* Validation
* State transitions
* Permission logic

### Integration tests

Used for:

* API endpoints
* Database operations
* Kafka publishing
* Redis behaviour
* External connector behaviour

### Contract tests

Used to verify communication between services.

### End-to-end tests

Used for critical workflows such as:

* Customer onboarding
* Account opening
* Internal transfer
* Interbank payment
* Loan disbursement
* Loan repayment
* Payment reversal
* Settlement and reconciliation

### Performance tests

Used for:

* Account balance reads
* Transaction posting
* Payment APIs
* Batch processing
* Reporting queries

### Security tests

Used for:

* Authentication
* Authorisation
* Input validation
* Data exposure
* Rate limiting
* Privilege escalation
* Injection attacks

---

## Required payment test scenarios

Payment services should test:

* Successful payment
* Invalid beneficiary
* Insufficient balance
* Account restriction
* Transaction limit exceeded
* Duplicate request
* Concurrent requests
* Provider timeout
* Provider rejection
* Network failure
* Retry
* Reversal
* Partial failure
* Unauthorised request
* Expired authentication
* Reconciliation mismatch

---

## Observability

Every service should implement:

* Structured logging
* Correlation IDs
* Metrics
* Distributed tracing
* Health checks
* Readiness checks
* Error reporting
* Business-event monitoring
* Audit events

Recommended tools:

* OpenTelemetry
* Prometheus
* Grafana
* OpenSearch or Elasticsearch
* Loki
* Jaeger or Tempo

Sensitive customer or financial data must not be written to logs.

---

## Security

Security requirements include:

* Encryption in transit
* Encryption at rest
* Least-privilege access
* Input validation
* Secure authentication
* Strong authorisation
* Audit logging
* Data masking
* Secret rotation
* Dependency scanning
* Container scanning
* Static analysis
* Rate limiting
* Network policies
* Backup encryption
* Incident reporting

Never commit:

* Passwords
* API keys
* Tokens
* Certificates
* Private keys
* Production connection strings
* Real customer data

Report security issues privately according to `SECURITY.md`.

---

## Coding standards

Contributors should:

* Follow the architecture used by the service
* Write clear and maintainable code
* Use meaningful names
* Keep classes and methods focused
* Validate external input
* Handle errors consistently
* Write automated tests
* Document public APIs
* Avoid unnecessary dependencies
* Avoid duplicated business rules
* Use structured logging
* Keep changes within the assigned task
* Run formatting and linting tools before submitting work

---

## Branching convention

Do not commit directly to `main`.

Recommended branch patterns:

```text
feature/TASK-0102-customer-onboarding
fix/TASK-0210-payment-reversal
test/TASK-0315-ledger-integration-tests
docs/TASK-0410-payment-api
refactor/TASK-0512-customer-domain-model
devops/TASK-0610-kafka-configuration
security/TASK-0710-mask-sensitive-logs
```

---

## Commit convention

Recommended format:

```text
type: short description
```

Examples:

```text
feat: add customer duplicate detection
fix: prevent duplicate payment posting
test: add ledger posting integration tests
docs: document transfer reversal workflow
refactor: simplify beneficiary validation
security: mask account identifiers in logs
```

---

## Pull requests

Every contribution should be submitted through a pull request.

A pull request should include:

* Related task or Issue
* Summary
* Implementation details
* Testing performed
* Database changes
* API changes
* Event changes
* Security considerations
* Known limitations
* Screenshots or logs where appropriate

At least one review should be completed before merging.

Critical financial services may require additional reviewers.

---

## Definition of done

A backend task is complete only when:

* The requirement is implemented
* Acceptance criteria are satisfied
* Code is reviewed
* Tests pass
* Documentation is updated
* API documentation is updated
* Database migrations are included
* Security concerns are addressed
* Audit requirements are implemented
* Observability is implemented
* No critical blocker remains
* The task tracker is updated
* The pull request is approved and merged

---

## Contribution guide

Read `CONTRIBUTING.md` before starting work.

Contributors must follow:

* Branching rules
* Commit conventions
* Pull-request requirements
* Coding standards
* Security requirements
* Testing standards
* Documentation requirements
* Project Code of Conduct

---

## Volunteer participation

This is currently an unpaid volunteer project.

Participation does not create an employment relationship or guarantee future payment.

Contributors participate to:

* Learn
* Gain practical experience
* Build portfolio evidence
* Collaborate with other developers
* Understand financial systems
* Contribute to an open-source banking platform

All contributors must communicate honestly about availability, progress and blockers.

---

## Licence

This project is distributed under the licence included in the `LICENSE` file.

Unless otherwise agreed in writing, contributions submitted to this repository will be licensed under the same licence.

Do not contribute proprietary code or code copied from an employer, client, private repository or incompatible open-source project.

---

## Disclaimer

This project is under development and is provided for learning, research and engineering collaboration.

It must not be used to process real customer funds, production banking transactions or sensitive customer information without:

* Independent security assessment
* Regulatory review
* Legal review
* Financial audit
* Penetration testing
* Operational testing
* Disaster-recovery validation
* Production-readiness approval

---

## Contact and support

Use the project’s approved communication channels for:

* Onboarding
* Technical questions
* Task assignment
* Architecture discussions
* Progress updates
* Blockers
* Security concerns

Before asking a question, review:

* Existing documentation
* Open GitHub Issues
* Service README files
* Architecture decisions
* API documentation

When asking for help, include:

* Service name
* Task ID
* Expected behaviour
* Actual behaviour
* Relevant logs without sensitive information
* Steps already attempted

---

## Acknowledgements

Thank you to every volunteer contributing time, knowledge, design, testing, documentation and engineering effort to the Core Banking Platform.

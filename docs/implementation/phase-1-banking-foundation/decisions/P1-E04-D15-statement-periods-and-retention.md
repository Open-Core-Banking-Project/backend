---
decision: P1-E04-D15
title: Statement periods, formats, retention and delivery
epic: P1-E04 Products and Accounts
owners:
  - Product/Compliance
  - Security
required_by: P1-E04-S13
status: Ready for review
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D13, ADR-0009]
decisions: [P1-E04-D15]
---

# Decision brief: statements and receipts

`ACC-UC-07` and `US-023` require the account module to authorise a statement request, hand it to Documents and
Statements, and return a secure artifact reference built from authoritative posted history. Allowed periods, formats,
retention and delivery rules are unapproved, and the epic forbids building statements from summaries or caches.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D15-A | Which periods may be requested, and by whom? | Validation rules, authorisation and bounds |
| D15-B | Which formats are supported in Phase 1? | The allow-list and Documents integration scope |
| D15-C | What is the authoritative source of statement content? | The ledger query contract and its evidence |
| D15-D | How long are statement artifacts retained? | Retention configuration and deletion rules |
| D15-E | How is a statement delivered securely? | Artifact reference, access control and audit |
| D15-F | What is recorded about a request without recording its contents? | Audit, telemetry and PII rules |

## 2. Why the answer is needed now

- The contracts already fix the shape: `CreateStatementRequestV1` carries `PeriodFrom`, `PeriodTo`, `FormatCode` and
  `PurposeCode`, and `StatementRequestResponseV1` returns `LedgerQueryReference` plus a status. The allow-lists behind
  `FormatCode` and `PurposeCode` must exist before validation can be written.
- Statements are the most likely route for bulk personal data to leave the platform; retention and delivery rules are
  security requirements, not presentation preferences.
- The ledger does not exist yet in Phase 1, so the statement request must be designed to fail closed until the
  authoritative query is available, rather than fall back to summaries.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Statement contracts | Transport-only: create, response, `StatementRequestedV1`, `StatementDocumentReadyV1` | `StatementRequestsV1.cs` |
| Statement domain and request store | **Not implemented** | Core Ledger Products module |
| Ledger posted-history query | **Not implemented**; the Ledger module is `P1-E05` | — |
| Document rendering and storage | **Not implemented in this repo**; no document service exists to render or retain artifacts | repo search |
| Statement permission | Not defined; the epic names `account.statement.request` (three segments, valid) | Epic §9 |
| Account summaries | Contract-only (`AccountSummaryResponseV1`); explicitly not a financial source | `AccountLifecycleV1.cs` |

## 4. Recommended position

### D15-A Periods and authorisation

| Item | Recommendation |
| --- | --- |
| Allowed periods | Calendar month, quarter, year-to-date and a custom range; the set is an effective-dated configuration area per product (`statement.policy`) |
| Bounds | `PeriodFrom` ≥ account open date; `PeriodTo` ≤ current business date; a single request may not exceed the configured maximum span (recommended 12 months) |
| Minimum span | None; a receipt for a single transaction is a statement request with a one-day range and a receipt purpose code |
| Authorisation | The customer owner, a joint holder, or a staff role holding `account.statement.request` plus the resource scope of the account; the institution always comes from the validated token, never from the request |
| Sensitive accounts | Where an institution marks an account sensitive, staff access requires an additional permission and step-up; the epic's delivery policy is enforced before the request is created, not after |
| Prohibited | A future period, a period before opening, an unbounded range, and a request spanning two institutions |

### D15-B Formats

| Item | Recommendation |
| --- | --- |
| Allow-list | `pdf` (human-readable) and `csv` (machine-readable) in Phase 1. No other code is accepted |
| Rationale | Two formats prove both a rendered document and a data extract without inventing unvalidated rendering for MT940, camt.053 or bespoke regulator formats |
| Unknown code | Refused with `statement_format_not_supported` at request time, never defaulted to `pdf` |
| Format and purpose | `PurposeCode` allow-list: `periodic`, `on_demand`, `receipt`, `regulatory`. A receipt may use `pdf` only |
| Ownership | The document service owns rendering and template versions; the Account module never renders or stores statement content |

### D15-C Source of truth

| Item | Recommendation |
| --- | --- |
| Source | Posted ledger history for the account and period, read through a ledger statement query contract that returns movements or an opaque query reference plus integrity reference |
| Evidence | The response and the `StatementRequestedV1` fact carry `LedgerQueryReference`; the artifact carries the ledger's `IntegrityReference` |
| Prohibited | Account summaries, analytics projections, UI transactions, caches and re-derived balances as statement content |
| Unavailable source | Refuse with `statement_source_unavailable`; a statement is never generated from a partial read |
| Balances on a statement | Opening and closing balances must come from the ledger query response, not from an account summary call |

### D15-D Retention

| Item | Recommendation |
| --- | --- |
| Configuration | A retention period per institution and product in `statement.policy`, with an explicit minimum floor held by the platform |
| Recommended floor | Statements and their artifacts are retained for the legal retention period applicable to the institution, **which must be confirmed by Compliance**; the brief does not invent the number |
| Deletion | Only a scheduled retention job may delete an artifact, on or after the retention expiry, and it writes an audit row naming the artifact reference (not its content) |
| Request records | The request record and its audit trail are retained beyond artifact deletion; a request whose artifact has expired still answers with `StatementRequestResponseV1` and an `expired` status |
| Legal hold | An artifact under legal hold is never deleted, whatever the retention period says |

### D15-E Delivery

| Item | Recommendation |
| --- | --- |
| Mechanism | A secure, short-lived signed artifact reference returned to the requester; content is never returned in the API response body |
| Access control | The reference is bound to the requesting party and the account; an unauthorised party receives `403` with no existence detail |
| Sensitive delivery | Email or SMS carries a notification with a link, never an attachment or a balance; the epic's sensitive-delivery policy is configuration, not code branches |
| Expiry | The signed reference expires within minutes; re-download requires a new authorisation step |
| Audit | Request, generation, each download and each expiry write audit rows with actor, correlation id and artifact reference; statement content is never logged |
| Password protection | Where an institution requires a password-protected PDF, the password is delivered out of band and never stored or logged |

### D15-F Evidence without content

| Item | Recommendation |
| --- | --- |
| Audit row fields | Requester type and id, account id, period, format, purpose, artifact reference, integrity reference, outcome and correlation id |
| Prohibited in audit and telemetry | Statement lines, balances, counterparty names, account numbers and any document body |
| Metrics | Requests by outcome, generation latency, artifacts awaiting generation, expired artifacts |
| Reconciliation | A request without an artifact past the window is a finding: `statement_request_without_artifact` |

## 5. Constraints

- Do not generate a statement from a summary, a cache, an analytics projection or a re-derived balance.
- Do not invent a retention number, a template, a regulator format or a delivery channel policy.
- Do not accept an unknown format code, and do not default it.
- Do not return statement content in an API response, a log, a metric or an error message.
- Do not delete a request record or an audit row when an artifact expires, and do not delete an artifact under legal
  hold.
- This decision approves request, retention and delivery semantics only; it does not approve closure conditions
  (`P1-E04-D13`) or document rendering implementation.

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The concrete retention period per institution and product | Compliance | Legal requirement; the brief fixes only that it is configured with a floor |
| Regulator-specific formats (MT940, camt.053, custom) | Compliance/Product | Not approved; requires a rendering specification |
| The Documents and Statements service contract and storage backend | Architecture / that service | Does not exist in this repository |
| Retention of signed access references and download logs | Security | Depends on the audit retention decision |
| Charging for statements | Product/Finance | Pricing decision outside this epic |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending: approve, amend or reject the positions in §4 | |
| Product/Compliance | | Pending: confirm periods, purposes and the retention floor | |
| Security | | Pending: confirm delivery, signed-reference lifetime and PII rules | |

## 8. Implementation notes (for slice 13)

1. Add `statement.policy` as an effective-dated configuration area carrying allowed periods, maximum span, formats,
   purposes and retention; refuse a request when no policy is published.
2. Implement the request aggregate with idempotency by request id, period bounds validation and the format/purpose
   allow-lists.
3. Implement the ledger query port; until it exists, return `statement_source_unavailable` rather than substituting a
   summary source.
4. Implement the secure artifact reference with a short lifetime, party binding and audit on each access.
5. Emit `statement_format_not_supported`, `statement_period_invalid`, `statement_source_unavailable` and
   `statement_request_without_artifact`.
6. Prove with tests: a future or pre-opening period is refused; an unknown format is refused; content never appears in
   a response, log or metric; an expired artifact still answers with an `expired` status; a request without an artifact
   past the window is reported as a reconciliation finding.

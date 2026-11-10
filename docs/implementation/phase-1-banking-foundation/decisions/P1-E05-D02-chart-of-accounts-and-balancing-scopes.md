---
decision: P1-E05-D02
title: Chart of accounts, ledger-account roles and legal-entity balancing scopes
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Finance
  - Architecture
required_by: P1-E05-S1
status: Approved
approved_on: 2026-11-09
approved_by: Project owner
brief_date: 2026-11-08
related: [P1-E05-D01, P1-E05-D03, P1-E05-D09, ADR-0004]
decisions: [P1-E05-D02]
---

# Decision brief: chart of accounts and balancing scopes

Every journal has to balance, and it has to balance *within something*. The epic states that total debits equal total
credits in each approved accounting scope and currency, but the scope itself, the legal-entity dimension and the set of
account roles a mapping may resolve are unapproved. The epic also forbids inventing a chart of accounts.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D02-A | What is the balancing scope of a journal? | The balance assertion in code and in the database |
| D02-B | Is legal entity a Phase 1 dimension or a Phase 2 addition? | Schema, mapping resolvers and reporting lineage |
| D02-C | Which ledger-account roles must exist for Phase 1? | The resolvers a mapping may name |
| D02-D | Who authors the codes, and how are they approved? | Configuration lifecycle and maker-checker |
| D02-E | How does a customer ledger account acquire an accounting classification? | Account-opening handoff and posting admission |
| D02-F | What happens when a role cannot be resolved? | Fail-closed behaviour and error codes |

## 2. Why the answer is needed now

- Slice 3 builds the ledger-account and mapping-version lifecycle, and slice 6 posts the first journal. Both encode the
  scope and the role vocabulary.
- The account epic already created one `ledger_account` row per bank account with a currency and a status but **no
  accounting classification**; until that is decided, no account can receive a posting.
- Reporting, statements and reconciliation derive their lineage from this dimension, so adding it later means rewriting
  posted evidence.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Customer ledger account row | Implemented: one per bank account, currency, status, no monetary column | `account.ledger_accounts`, `P1-E04-S09` |
| Accounting classification on that row | **Absent** by design: the epic forbids inventing a chart | `LedgerAccountRecord` |
| Legal-entity dimension | **Not implemented** | No legal-entity table or column exists |
| Chart area and roles | **Not implemented** | No `ledger.chart` configuration area |
| Configuration lifecycle (draft, simulate, approve, publish) | Implemented and reused by four areas | `P1-E04` slices 3 to 6, `P1-E01` |

## 4. Recommended position

### D02-A Balancing scope

| Item | Recommendation |
| --- | --- |
| Scope | `AccountingScope = (institution, legal entity, currency)` |
| Assertion | Within one journal, `sum(debits) == sum(credits)` for every distinct scope in the journal, compared exactly with no epsilon |
| Currency | One currency per scope; a journal never mixes currencies, and cross-currency posting stays prohibited until FX accounting is approved |
| Institution-wide posting | Allowed only when the institution declares exactly one legal entity; otherwise every journal names one |
| Netting | Prohibited across legal entities and across currencies, including inside a single transaction |

### D02-B Legal entity

| Item | Recommendation |
| --- | --- |
| Phase 1 | A first-class dimension, stored as `legal_entity_code` on the ledger account and on every journal line |
| Population | Institutions are provisioned with at least one legal entity through the approved configuration lifecycle; the institution code is not reused as an implicit legal entity |
| Reporting | Lineage from every posted line to its legal entity, so regulatory reporting never infers it |
| Phase 2 | Additional entities are configuration, not a schema change |

### D02-C Phase 1 ledger-account roles

Only roles are approved here. Codes are data (see D02-D).

| Role | Purpose | Direction typical |
| --- | --- | --- |
| `customer_deposit` | The customer-facing liability for one bank account and currency | Credit |
| `cash_settlement` | The institution's settlement position for customer cash movements | Debit |
| `internal_transfer_clearing` | Offset between two customer ledger accounts in one institution | Either |
| `fee_income` | Fee charged to a customer | Credit |
| `commission_payable` | Commission owed onward to a provider or agent | Credit |
| `tax_payable` | Tax collected on behalf of an authority | Credit |
| `interest_expense` | Interest credited to a customer | Debit |
| `penalty_income` | Penalty charged to a customer | Credit |
| `suspense` | Explicit, approved holding account for a declared reconciliation case | Either, never a default |

A mapping may resolve only named roles. A role absent from the institution's published chart is a refusal, not a
fallback.

### D02-D Authorship and approval of codes

| Item | Recommendation |
| --- | --- |
| Area | `ledger.chart`, schema `ledger.chart.v1`, authored through the existing draft → simulate → submit → publish lifecycle |
| Content | Legal entity, role, code, name, normal side, currency treatment, allowed directions, status |
| Validation | Unique code per institution and legal entity; a code may not be reused across roles; a role may appear once per entity and currency treatment |
| Approval | Maker-checker with maker exclusion, effective-dated publication and no overlapping periods, exactly as the existing configuration engine already enforces |
| Simulation | Every published chart version proves that each declared role resolves to exactly one account per entity |

### D02-E Customer ledger account classification

| Item | Recommendation |
| --- | --- |
| When | At account opening, the ledger handoff resolves `customer_deposit` for the account's currency and legal entity and stores the resolved ledger-account code and chart version on the ledger account row |
| Immutability | The classification is fixed for the life of the ledger account; changing it requires a new ledger account, never an edit |
| Posting admission | A ledger account with no resolved classification, or with a chart version that is no longer effective, is refused with `ledger_account_unclassified` or `chart_version_not_effective` |
| Existing accounts | Accounts opened before the chart existed stay unclassified and therefore unpostable until the approved migration in `P1-E05-D16` backfills a classification |

### D02-F Failure behaviour

| Condition | Answer |
| --- | --- |
| Role missing from the chart | `ledger_role_unknown` and no posting |
| Role resolves to more than one account | `ledger_role_ambiguous` and no posting |
| Resolved account is inactive or closed | `ledger_account_inactive` and no posting |
| Legal entity missing on the institution | `legal_entity_missing` and no posting |
| Any of the above | Never substitute suspense, cash or another customer's account |

## 5. Constraints

- Do not invent GL codes in code, tests or migrations; the brief approves roles and structure only.
- Do not use suspense as a general fallback.
- Do not net debits and credits across legal entities or currencies.
- Do not infer the legal entity from the customer, the branch or the institution code.
- This brief does not approve the event catalogue (`P1-E05-D03`), precision (`D04`), the sign convention (`D05`), the
  available-funds formula (`D06`) or component treatment (`D09`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The actual code list for each role | Finance | Data, authored through the approved lifecycle |
| Which roles Phase 2 interest and FX need | Finance/Treasury | Phase 2 scope |
| Legal-entity-level regulatory mapping | Compliance | Reporting concern, `ADR-0014` |
| Chart retention and archival | Compliance/DBA | `P1-E05-D16` |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved recommended position | 2026-11-09 |
| Finance | | Pending: confirm the role set and the scope tuple | |
| Architecture | | Pending: confirm legal entity as a Phase 1 dimension | |

## 8. Implementation notes (for slices 3 and 6)

1. Store `legal_entity_code`, `chart_version_id` and `gl_account_code` on the ledger account, and the legal entity and
   scope on every journal line.
2. Assert the scope balance per journal before insert, and add a database guard that cannot be bypassed by a direct
   statement.
3. Resolve roles through the chart version effective for the business date; a stale version refuses.
4. Add the `ledger.chart` area to the shared configuration engine rather than a bespoke table and endpoint set.
5. Prove with tests: an unknown role refuses, an ambiguous role refuses, an unclassified account refuses, two legal
   entities cannot net within one journal, and a chart publication with an overlapping period is rejected.

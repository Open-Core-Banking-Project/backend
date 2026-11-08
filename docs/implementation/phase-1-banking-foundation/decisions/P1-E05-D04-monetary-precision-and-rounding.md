---
decision: P1-E05-D04
title: Monetary precision, rate scale and rounding
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Finance
required_by: P1-E05-S1
status: Proposed
brief_date: 2026-11-08
related: [P1-E05-D03, P1-E05-D05, P1-E05-D09]
decisions: [P1-E05-D04]
---

# Decision brief: monetary precision and rounding

The epic prohibits `double`, `float`, PostgreSQL `money` and implicit rounding, and requires an explicit
`numeric(precision, scale)` for every monetary and rate column. It does not fix the numbers, the rounding mode, or the
point at which rounding happens — and a rounding decision that is made per-service is how a ledger stops balancing.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D04-A | What precision and scale do amounts use? | Every amount column, contract and calculation |
| D04-B | What precision and scale do rates use? | Interest, fee and tax formulas |
| D04-C | Which rounding mode applies? | Every calculation result |
| D04-D | Where does rounding happen? | Component computation versus journal assembly |
| D04-E | How is a rounding difference kept balanced? | The journal's last line and its evidence |
| D04-F | Which currency minor units are approved? | Presentation and boundary tests |

## 2. Why the answer is needed now

- Slice 2 defines the `Money` value object and the canonical request hash; both encode scale.
- Fee, tax, commission and interest components are produced outside the ledger and compared inside it, so both sides
  must round identically or every posting with a fraction becomes a false imbalance.
- The epic's test plan requires rounding and boundary tests, which cannot be written without the rule.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| `Money` value object | **Not implemented** | No posting contracts exist |
| Monetary columns | Only account-side amounts exist: lien and closure amounts | `account.account_liens`, `account.account_closure_proposals` |
| Rate columns | **None exist** | No interest or fee rate storage |
| Currency metadata | Institution currency set published through configuration | `P1-E04-S6`, `institution.currencies` |
| JSON money transport | Statement and lien contracts use `decimal` in JSON | `AccountSummaryStatementV1` |

## 4. Recommended position

### D04-A Amount precision

| Item | Recommendation |
| --- | --- |
| Storage | `numeric(19,4)` for every monetary amount, in every ledger and account table |
| Range | Up to 999 999 999 999 999.9999; sufficient for Phase 1 balances and per-transaction limits |
| Scale rationale | Four decimal places hold the currency minor unit plus the two places a rate application needs before rounding, without storing a value the ledger cannot justify |
| Canonical value | An amount is stored and compared at four decimal places; trailing zeros are not significant |
| Prohibited | `float`, `double`, `money`, `real`, and any computation that passes a monetary value through a binary floating type |

### D04-B Rate precision

| Item | Recommendation |
| --- | --- |
| Storage | `numeric(9,8)` for every rate |
| Representation | A decimal fraction, not a percentage: `0.01250000` is 1.25% |
| Range | 0.00000001 to 9.99999999, sufficient for interest, fee and tax rates |
| Negative rates | Allowed only where a mapping declares them; the sign is explicit, never inferred |

### D04-C Rounding mode

| Item | Recommendation |
| --- | --- |
| Mode | Half away from zero (commercial rounding), applied to the absolute value with the sign reapplied |
| Why not banker's rounding | Phase 1 customer-facing amounts are conventionally rounded half-up; banker's rounding would make a customer-facing fee differ from the institution's published tariff |
| Prohibited | Truncation, ceiling or floor by default, and any per-service rounding mode |

### D04-D Rounding points

| Point | Rule |
| --- | --- |
| Component computation | The owning module rounds each component to the currency minor unit before submitting it |
| Ledger ingestion | The ledger accepts the submitted component value as authoritative and never re-rounds it |
| Journal assembly | The ledger sums submitted components and compares them exactly; it does not round the total |
| Intermediate calculation | Kept at four decimal places, never rounded mid-formula |
| Interest and accrual | Phase 2; the same rule applies when approved |

### D04-E Keeping the journal balanced

| Item | Recommendation |
| --- | --- |
| Case | Components rounded independently do not sum to the intended total |
| Answer | The submitting module supplies an explicit rounding component (approved role, reason code and amount) rather than letting the ledger absorb the difference |
| Ledger behaviour | If no rounding component is supplied and the journal does not balance, the posting is refused with `journal_unbalanced` |
| Prohibited | A hidden tolerance, an epsilon comparison, or a ledger-computed adjustment line |

### D04-F Currency minor units

| Item | Recommendation |
| --- | --- |
| Source | The published `institution.currencies` set declares the minor unit per currency |
| Phase 1 default | NGN and other two-decimal currencies use minor unit 2; a currency whose minor unit is not declared cannot be posted |
| Boundary tests | Each currency is tested at zero, at the smallest unit, at a one-unit rounding boundary and at the largest declared amount |

## 5. Constraints

- Do not widen or narrow scale per table; one amount scale and one rate scale.
- Do not compare monetary values with a tolerance.
- Do not let a service round differently from the institution's published tariff calculation.
- Do not store a rate as a percentage string or a formatted display value.
- This brief does not approve which fees or taxes apply (`P1-E05-D09`) or how the available balance is computed
  (`P1-E05-D06`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Interest basis conventions (actual/365, actual/actual) | Finance | Phase 2 accrual scope |
| FX rate scale and quotation convention | Treasury | FX is out of Phase 1 scope |
| Per-product fee schedules | Product | Configuration data, `P1-E04` |
| Rounding of statement display | Product | Presentation only; the stored value is unchanged |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: confirm `numeric(19,4)`, `numeric(9,8)` and half-up | |
| Architecture | | Pending: confirm the no-re-rounding ingestion rule | |

## 8. Implementation notes (for slice 2 and every posting slice)

1. Implement `Money` as a readonly value object holding a decimal amount at four places and an explicit ISO currency;
   construction at a finer scale is a refusal, not a silent round.
2. Add a database check on every monetary column that rejects more than four decimal places.
3. Serialise money to JSON as a string, never as a floating number.
4. Assert journal balance with exact decimal equality inside the transaction.
5. Prove with tests: a value at five decimal places is refused, a fractional fee with an explicit rounding component
   balances, a journal that is off by 0.0001 is refused, and the canonical request hash is stable across a
   round-trip.

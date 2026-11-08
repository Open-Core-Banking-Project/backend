---
decision: P1-E05-D09
title: Fee, tax, commission, interest and waiver accounting treatment
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Finance
  - Tax
  - Product
required_by: P1-E05-S1
status: Proposed
brief_date: 2026-11-08
related: [P1-E05-D02, P1-E05-D03, P1-E05-D04]
decisions: [P1-E05-D09]
---

# Decision brief: component accounting treatment

The epic's invariant 11 requires fees, commissions, taxes, interest, penalties and waivers to use separate components
and lines rather than being hidden in the principal amount. It does not say how a waiver is represented, whether a tax
is a component or an addition, or what a commission split looks like — and the wrong answer silently changes income
recognition.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D09-A | Which component types exist in Phase 1, and what does each mean? | The component vocabulary and contract |
| D09-B | How are components kept out of the principal? | Line construction and the mapping template |
| D09-C | How is a waiver represented? | Contra income versus reduction of the charge |
| D09-D | How is tax carried, and on what base? | Tax component, authority role and evidence |
| D09-E | How is a commission split between the institution and a provider? | Component cardinality and payable roles |
| D09-F | How are these components reversed? | Reversal scope per component |

## 2. Why the answer is needed now

- Slice 3 declares the components a mapping may carry, and slice 6 posts the first fee. Both encode the vocabulary.
- The fee and tax amounts are computed by the owning module (product, payments, VTU) and must be presented to the ledger
  as separate, auditable components.
- A waiver recorded as "a smaller fee" cannot be reconciled against the tariff that was charged, and a tax netted into a
  fee cannot be remitted or reported.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Component types | **Not implemented** | No posting contracts |
| Fee, tax and commission calculation | **Not implemented** | Product and payments epics |
| Ledger income and payable roles | Approved as roles only, no codes | `P1-E05-D02` |
| Waiver mechanism | **Not implemented** | — |
| Interest | Phase 2 scope; component is reserved here | Epic scope section |

## 4. Recommended position

### D09-A Phase 1 component types

| Component | Meaning | Typical ledger effect |
| --- | --- | --- |
| `principal` | The core economic amount of the event | Customer ledger account and the settlement or transfer role |
| `fee` | A charge for the service, per the published tariff | Debit customer, credit `fee_income` |
| `tax` | A levy on a declared base, owed to an authority | Debit customer, credit `tax_payable` |
| `commission` | An amount owed onward to a provider or agent | Debit customer, credit `commission_payable` |
| `penalty` | A charge for a breach or late action | Debit customer, credit `penalty_income` |
| `interest` | Interest credited to or charged to the customer | Credit or debit customer, offset by `interest_expense` |
| `waiver` | An approved release of a charge already raised | Debit the income role, credit customer |
| `rounding` | The explicit difference that keeps independently rounded components balanced | Declared role, declared reason |

Anything else is refused. A component carries its own amount, currency, direction and reason code.

### D09-B Keeping components out of the principal

| Item | Recommendation |
| --- | --- |
| Minimum lines | One line per component side; a fee-bearing transfer therefore has at least four lines |
| Netting | Prohibited: the principal line is never reduced by a fee, and a fee is never reported as a smaller principal |
| Mapping declaration | The mapping declares which components the event requires, which are optional, and which are forbidden |
| Validation | A missing required component refuses; a forbidden component refuses; a component with a zero amount refuses |
| Reporting | Every component appears with its type, so a tariff reconciliation is a query, not an inference |

### D09-C Waivers

| Item | Recommendation |
| --- | --- |
| Representation | A `waiver` component that debits the income role and credits the customer, referencing the original charge |
| Never | A reduced fee component, a negative fee, or a note on the transaction |
| Authority | A waiver requires a reason code and an authority or approval reference; a waiver without one is refused |
| Original reference | The waiver names the fee transaction or component it releases, so the pair reconciles |
| Partial waiver | Allowed, up to the un-waived remainder, asserted under the referent's lock |

### D09-D Tax

| Item | Recommendation |
| --- | --- |
| Representation | A `tax` component, separate from the fee it applies to, with the tax base recorded as evidence |
| Base | Declared by the producing module in the request evidence; the ledger never computes tax |
| Authority | Credited to `tax_payable` for the declared authority code |
| Multiple taxes | Allowed as distinct components with distinct authority codes; never merged |
| Remittance | Out of Phase 1 scope; the payable balance is the evidence |

### D09-E Commission

| Item | Recommendation |
| --- | --- |
| Phase 1 | One `commission` component credited to `commission_payable` |
| Split | If an institution and a provider share a commission, they are two components with distinct counterparties, never one net figure |
| Settlement | Out of Phase 1 scope; the payable is the evidence |
| Waiver | A commission waiver follows D09-C and names the commission it releases |

### D09-F Reversal of components

| Item | Recommendation |
| --- | --- |
| Scope | A reversal mirrors the component set of the original, component by component, in the opposite direction |
| Partial by component | Allowed only where the mapping's reversal eligibility declares it, for example a fee-only reversal with authority |
| Prohibited | Reversing the principal and leaving the fee, or reversing a fee by editing the original |
| Evidence | The reversal records which original components it reverses and the reversible remainder after each |

## 5. Constraints

- Do not net a fee, tax or commission into a principal amount.
- Do not represent a waiver as a fee reduction or a negative amount.
- Do not compute a fee, tax, interest or commission inside the ledger.
- Do not merge multiple taxes into one line.
- Do not assume a principal-only reversal is acceptable.
- This brief does not approve the tariff values (`P1-E04` product configuration), the event catalogue (`P1-E05-D03`) or
  interest accrual conventions (Phase 2).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Tariff and fee schedule values | Product | Configuration data |
| Tax rates and authority codes | Tax/Compliance | Jurisdiction-specific data |
| Interest accrual, capitalisation and day-count | Finance | Phase 2 scope |
| Provider commission agreements | Commercial | Contract data, not accounting mechanics |
| Tax remittance and reporting | Tax | Phase 2 operational scope |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: confirm component types and the waiver representation | |
| Tax | | Pending: confirm the tax component and authority role | |
| Product | | Pending: confirm commission split cardinality | |

## 8. Implementation notes (for slices 3, 6, 9 and 10)

1. Model the component type as an allow-listed enum with a database check constraint, and require a reason code on
   every non-principal component.
2. Build lines from the mapping template per component, never from a caller-supplied line list.
3. Require an authority reference on a waiver and on a penalty, and store the original component reference.
4. Assert that a reversal's component set matches the original's reversible set before inserting.
5. Prove with tests: a fee-bearing transfer posts four lines and balances, a fee cannot be netted into the principal, a
   waiver without authority is refused, two taxes stay distinct, a commission split posts two payables, and a fee-only
   reversal leaves the principal untouched.

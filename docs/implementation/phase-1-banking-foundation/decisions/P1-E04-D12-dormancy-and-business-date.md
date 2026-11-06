---
decision: P1-E04-D12
title: Dormancy qualifying activity and business-date policy
epic: P1-E04 Products and Accounts
owners:
  - Compliance/Product
  - Core operations
required_by: P1-E04-S12
status: Ready for review
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D10, P1-E04-D13]
decisions: [P1-E04-D12]
---

# Decision brief: dormancy

`ACC-UC-05` requires a scheduled assessment against an exact rule and business date and states that a read or a
non-qualifying event cannot reset dormancy. Which activities qualify, which clock is used and how reactivation is
authorised are unapproved, and the epic forbids inferring them.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D12-A | Which activities are qualifying activity? | The accumulator the assessment reads |
| D12-B | Which activities explicitly do not qualify? | The prohibition list tests must prove |
| D12-C | Which date is used: business date or wall clock? | Determinism, catch-up and replay |
| D12-D | How are missed assessments and repeated runs handled? | Idempotency and job evidence |
| D12-E | What does reactivation require? | Preconditions, approvals and residual restrictions |
| D12-F | What evidence is produced before and at transition? | Notices, audit and reconciliation |

## 2. Why the answer is needed now

- Dormancy changes the ledger outcome: a dormant account is denied debits by the control precedence in
  `P1-E04-D10`, so a wrong transition is a customer-visible failure, and a missed one is a control failure.
- An assessment driven by wall-clock time is not replayable, and a business-date-driven assessment must be idempotent
  under catch-up after a calendar advancement or an outage.
- The institution calendar and business-date advancement already exist (`P1-E01`), so the policy must bind to them
  rather than introduce a second notion of "today".

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Dormancy domain and assessment | **Not implemented** | Core Ledger Products module |
| Account status machine | Not implemented; the epic's lifecycle includes `Dormant → Active` | Epic §6 |
| Business calendar and business date | Implemented: versioned calendars and controlled business-date advancement | `BusinessCalendarV1`, Institution module |
| Posting history | **Not implemented**; the Ledger module is `P1-E05`, so no qualifying-activity source exists yet | — |
| Scheduled job pattern | Implemented pattern: bounded, idempotent jobs with recorded outcomes exist in Identity and Core Ledger | Prior slice evidence |

## 4. Recommended position

### D12-A Qualifying activity

| Item | Recommendation |
| --- | --- |
| Definition | A posted, customer-initiated monetary transaction on the account, in a direction and transaction type declared by an approved allow-list |
| Source | The ledger's posted movement history by business date; never the account summary, a cache or a channel log |
| Allow-list | `dormancy.qualifying_activity` in an effective-dated configuration area, listing transaction-type codes and directions |
| Counting | A qualifying transaction resets the inactivity clock as of its posting business date |
| Multi-account | One account per assessment; a qualifying transaction on account A never resets account B, even for the same customer |

### D12-B Non-qualifying activity

| Activity | Qualifies? | Reason |
| --- | --- | --- |
| Balance enquiry, statement request, login | No | Explicitly excluded by `ACC-UC-05`; access is not activity on the account |
| Interest capitalisation | No | Bank-initiated |
| Fee, charge or tax debit | No | Bank-initiated; a charge cannot keep an abandoned account alive |
| Bank-initiated correction or reversal | No | Bank-initiated |
| Inbound credit from a third party | Configurable per product; recommended **yes** when the account holder is identifiable as the beneficiary, otherwise no | Institutions differ; the allow-list carries the answer |
| Lien or control change | No | Not a customer monetary instruction |
| Failed or rejected posting attempt | No | Nothing posted |

### D12-C Date policy

| Item | Recommendation |
| --- | --- |
| Clock | The institution business date only, taken from the Institution module's current business date and the calendar version effective for that date |
| Wall clock | Never used to decide dormancy; a wall-clock timestamp may appear in job metadata only |
| Rule version | The dormancy rule version effective at the assessed business date, evaluated and recorded with the assessment |
| Threshold | Configured per product family as a count of days or months; **no default value is shipped** — a product without a threshold is not assessed and the gap is reported |
| Time zone | Business date is calendar-defined; no local-time conversions are introduced here |

### D12-D Execution and idempotency

| Item | Recommendation |
| --- | --- |
| Job shape | One assessment per `(account, businessDate, ruleVersion)`; a repeated run returns the recorded outcome |
| Catch-up | After a missed run, business dates are processed in order; the oldest missed date is not skipped |
| Concurrency | A per-account assessment lease prevents two workers assessing the same account; the optimistic account version protects the transition |
| Outcome recording | Every run writes an assessment row with the dates considered, the last qualifying activity, the rule version and the decision, including "not dormant" |
| Failure | A failed assessment writes a failed outcome and is retried with a bounded attempt count; no transition occurs on failure |
| Prohibited | Transitioning an account from a partial evaluation, from a cached activity date, or while a legal control is under review |

### D12-E Reactivation

| Precondition | Recommendation |
| --- | --- |
| Transition | `Dormant → Active` only; a dormant account never goes directly to `Closed` without passing the closure preconditions in `P1-E04-D13` |
| Eligibility | Current authoritative customer facts within the approved ceiling; a dormant reactivation re-evaluates eligibility only where the product policy requires it |
| KYC | If the institution's policy requires current KYC for reactivation, the requirement is configured, not assumed |
| Security | Channel credentials are re-established through Identity; a dormant account does not inherit an expanded channel scope |
| Approval | Reason required; approval required when the institution's policy declares it, and the maker cannot approve |
| Residual controls | Freezes, blocks and liens survive reactivation; reactivation clears dormancy only |
| Evidence | One audit row and one `AccountStatusChangedV1` fact with previous and new status, reason code and control version |

### D12-F Notices and observability

| Item | Recommendation |
| --- | --- |
| Pre-dormancy notice | A configured lead time before the threshold, recorded as a notice fact with its outcome; where the institution's policy requires notice and none was recorded, the transition is refused |
| Unassessable accounts | Counted in the reconciliation report: accounts with no threshold, no activity source or a stale evaluation |
| Metrics | Assessments run, transitions, failures and the age of the oldest unassessed account |
| Audit | Transition rows carry the assessment id, rule version and business date |

## 5. Constraints

- Do not invent a dormancy period, a notice lead time or a qualifying-activity list; each is configuration with no
  shipped default.
- Do not use wall-clock time, a job start time or a local time zone to decide dormancy.
- Do not let a read, a login, a fee or an interest capitalisation reset dormancy.
- Do not clear freezes, blocks or liens on reactivation.
- Do not assess from account summaries, analytics or channel logs.
- This decision approves dormancy policy only; it does not approve closure (`P1-E04-D13`) or control precedence
  beyond citing `P1-E04-D10`.

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The dormancy threshold per product family | Product/Compliance | Policy value; the brief fixes only that it is configuration |
| Whether an inbound third-party credit qualifies | Product/Compliance | Recommended yes when the holder is the beneficiary; institution choice |
| Regulatory notice content and channel | Compliance | Not a rule-semantics question and depends on Channels/Engagement |
| Statements and notices for dormant accounts | Product/Compliance | Ties to `P1-E04-D15` retention and delivery |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending: approve, amend or reject the positions in §4 | |
| Compliance/Product | | Pending: confirm the qualifying-activity list and notice requirement | |
| Core operations | | Pending: confirm the business-date catch-up policy and reactivation approvals | |

## 8. Implementation notes (for slice 12)

1. Add `dormancy.rules` as an effective-dated configuration area with threshold, notice lead and the qualifying
   allow-list; refuse a product with no published rule by reporting it, never by assuming a default.
2. Implement the assessment as a pure function over (last qualifying activity, business date, rule version) so it is
   replayable in tests.
3. Implement the `(account, businessDate, ruleVersion)` idempotency key, a per-account lease and ordered catch-up.
4. Emit `dormancy_rule_missing`, `dormancy_activity_source_unavailable`, `dormancy_notice_missing` and
   `dormancy_assessment_failed` as stable codes, and count them in product-account telemetry.
5. Prove with tests: an enquiry does not reset the clock; a fee does not reset it; a qualifying posting on the
   business date does; a repeated run produces one assessment and one transition; catch-up processes dates in order;
   reactivation keeps an active freeze.

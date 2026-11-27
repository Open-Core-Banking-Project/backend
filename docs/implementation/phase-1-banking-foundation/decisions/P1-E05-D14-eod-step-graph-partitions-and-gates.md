---
decision: P1-E05-D14
title: End-of-day step graph, partitions and completion gates
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Core Operations]
required_by: P1-E05-S12
status: Approved
brief_date: 2026-11-09
approved_on: 2026-11-26
approved_by: Project owner
selected_option: A - seven-step blocking graph with account-hash partitions
related: [P1-E05-D03, P1-E05-D13]
decisions: [P1-E05-D14]
---

# Decision brief: restartable end-of-day processing

The platform needs an institution-specific processing graph before it can advance business date. The mechanics may be
implemented generically, but the actual financial steps and completion gates cannot be invented by engineering.

## 1. Decision requested

| Question | Required answer |
| --- | --- |
| Steps | Which steps block Phase 1 business-date advancement? |
| Identity | How are runs, steps and partitions made deterministic? |
| Leases | How do workers claim work and what is the source of truth? |
| Partitions | How is parallel work proven disjoint and reconciled? |
| Completion | Which evidence and sign-off allow advancement? |
| Overrides | Who may retry, skip or force a step, and what may they never do? |

## 2. Approved position

Option A was approved on 2026-11-26.

### Phase 1 blocking graph

```text
prerequisite validation
  -> scheduled financial inputs
  -> approved accrual/fee steps
  -> balance and event reconciliation
  -> period checks
  -> completion summary
  -> business-date advancement
```

- Every link above is a blocking gate. A step may not start while a prerequisite is incomplete or failed.
- Dormancy, statement, payment and provider settlement steps are named **non-blocking handoffs** to their owning
  modules. They become blocking only through a later published configuration version, not through a code default.
- The graph is acyclic and validated when a run snapshot is published.

### Run, step and partition identity

- One immutable run snapshot exists per institution, run type and business date, recording the graph version, step
  catalogue and configuration versions in force.
- Step and partition identities are deterministic. Every financial item derives its idempotency key from run, step,
  partition, business reference and rule version, so a restart cannot create a second effect.
- A stale graph or configuration version is refused rather than silently upgraded mid-run.

### Leases, checkpoints and restart

- Workers claim work with a 60-second expiring lease and heartbeat. The database checkpoint — not a lease or a queue
  acknowledgement — is the source of truth.
- A checkpoint advances only in the same transaction as the effects and summary it covers. A crash before commit leaves
  the previous checkpoint intact; a crash after commit resumes from the new one.
- Restart resumes the original run and identities from the last verified checkpoint.
- An item is attempted three times; a fourth failure raises a blocking exception instead of retrying forever.

### Partitions

- Financial steps partition by a hash of the ledger-account identifier inside its accounting scope.
- Partitions must be disjoint; the merge step reconciles item counts and debit/credit totals per accounting scope and
  currency before the step may complete.

### Completion and advancement

- Completion requires zero unapproved blocking exceptions, balanced totals by accounting scope and currency, a drained
  outbox, and the `P1-E05-D13` period gate.
- The completion summary is immutable and records the run, graph version, totals, exception outcomes and evidence
  references.
- One Core Operations sign-off is recorded against the exact completion summary.
- Business-date advancement requires that completion, its sign-off and an open next period; a failed or overridden step
  cannot advance the date.

### Override authority

- `ledger.processing.override` is a separate permission held by a Core Operations supervisor and requires dual control.
- It may retry or skip **non-financial** steps only, with a reason and evidence.
- It may never manufacture a checkpoint, force-complete a financial step, skip reconciliation or period checks, change
  totals, or bypass any financial invariant.

## 3. Options and impact

| Option | Position | Impact |
| --- | --- | --- |
| A — seven-step blocking graph with account-hash partitions **(approved)** | Reconciliation and period checks are hard gates; module handoffs stay non-blocking until published. | Complete control over Phase 1 close while later modules can join without code changes. Requires a published step catalogue and per-step evidence. |
| B — include provider/payment and dormancy/statement gates now | Every downstream module becomes a hard prerequisite immediately. | Strongest end-to-end close, but business-date advancement blocks on slices that are not implemented yet. |
| C — reconciliation, period checks, summary and advancement only | Smallest graph. | Fast to build, but scheduled inputs and accrual steps have no restartable checkpoint before reconciliation. |

## 4. Required implementation evidence

1. Graph validation, including a cycle, an unknown dependency and a stale graph version.
2. Two-worker claims: one lease wins, the other observes the existing claim.
3. Crash before and after checkpoint commit, proving the previous checkpoint survives or the new one is resumed.
4. Deterministic replay with the same identities and no duplicate effect.
5. Partition disjointness and merge totals per accounting scope and currency.
6. A failed dependency blocks its dependants.
7. Mismatched merge totals block completion.
8. Restricted override: permission, dual control, non-financial steps only, and refusal to manufacture a checkpoint.
9. Business-date advancement refused without completion, sign-off or an open next period.

## 5. Approved policy boundary

- The step catalogue, per-step owners, partition sizes, exception severities, cut-offs and the completion/sign-off matrix
  are published configuration owned by Finance and Core Operations. Changing them is a configuration publication.
- Dormancy, statement, payment and provider steps remain handoffs until their owning decisions and slices publish them
  as gates.
- Until this policy is implemented and proven, no processing run may advance the business date.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved Option A | 2026-11-26 |
| Finance | Project-owner approval | Approved the seven blocking steps, reconciliation/period gates and balanced-total completion evidence | 2026-11-26 |
| Core Operations | Project-owner approval | Approved the 60-second lease, three-attempt limit, account-hash partitions, Operations sign-off and dual-control non-financial override | 2026-11-26 |

# Business-date advancement recovery

Use this procedure when an approved business-date request fails, is uncertain, or its event remains
unpublished. The last value in `institution.business_date_controls` is authoritative.

1. Fence any dependent EOD activity and record the institution, request, calendar version,
   processing checkpoint, expected date and requested date.
2. Read the source request, control and history. Never infer completion from wall-clock time, a
   timeout or an HTTP retry.
3. If history contains the request and the control points to its new date, the source transition
   committed. Repair only outbox delivery with the original event identity.
4. If neither source state nor history changed, retry the same command with the same idempotency key
   after the cause is fixed. If state is mixed or ambiguous, keep processing fenced and escalate to
   Core Operations/Engineering; do not edit the date directly.
5. A new attempt still requires a published calendar, the next explicitly classified processing
   day, an exact checkpoint and an independent approver.

Before reopening processing, verify one history row, one authoritative date transition, one event
identity, preserved skipped-day evidence, no duplicate consumer outcome and reconciliation of every
dependent job. Automatic timezone/cut-off advancement is not authorised by this runbook.

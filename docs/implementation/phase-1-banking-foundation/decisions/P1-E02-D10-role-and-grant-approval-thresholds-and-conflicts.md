---
decision: P1-E02-D10
title: Role and grant approval thresholds and conflict rules
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Security
  - Operations
required_by: Slice 7
status: Approved
approved_on: 2026-10-28
brief_date: 2026-10-28
related: [ADR-0009, ADR-0016]
---

# Decision brief: role and grant approval thresholds and conflict rules

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D10-A | Which role or grant changes need a second person? | Every grant that carries a privileged capability class, as defined below; read-only grants do not |
| D10-B | Which permission pairs may one actor never hold at once? | The eight seeded pairs below, evaluated before the grant becomes effective |
| D10-C | What monetary or value thresholds apply? | None are set here; no amount, limit or tier is invented by Identity |

## 2. Approval classes

Thresholds are expressed as capability classes, not money, because Identity owns identity-level
separation while business limits belong to the owning service.

| Class | Definition | Approval required |
| --- | --- | --- |
| Read-only | Every permission whose action is `view` | No; step-up and audit only |
| Operational | `create`, `update`, `submit`, `simulate` outside access administration | No; step-up and audit only |
| Privileged | `approve`, `publish`, `assign`, `manage`, `repair`, and every institution lifecycle action | Yes: a second, different actor must approve before publication |

The second actor is resolved through the approver-eligibility contract (`P1-E02-D08`, slice 10), so
separation is by stable actor identity, never by role or session.

## 3. Conflict rules

Seeded as institution data of type `permission_pair`. A grant that would make either pair active
for one actor is refused with `identity.conflict_of_duty`, and nothing is persisted.

| Permission A | Permission B | Why |
| --- | --- | --- |
| `product.version.update` | `product.version.publish` | The author must not publish unreviewed product rules |
| `product.version.update` | `product.version.approve` | Maker and approver of the same product version |
| `product.version.publish` | `product.version.approve` | The approver must not also publish |
| `customer.application.submit` | `customer.application.review` | Onboarding maker and reviewer |
| `platform.institution.create` | `platform.institution.activate` | Provisioning maker and activation approver |
| `institution.branch.create` | `institution.branch.activate` | Branch maker and approver |
| `identity.permission.assign` | `audit.event.view` | Access administration and independent audit |
| `integration.credential_reference.manage` | `audit.event.view` | Secret administration and independent audit |

## 4. Constraints

- Rules are data, versioned per institution. Identity never hard-codes a role name.
- A conflict check failure is a denial, not a warning; the grant is not written.
- Existing conflicting grants are not silently revoked; they are reported for an access review and require an explicit revocation decision.
- Money, tier, branch-size and risk thresholds remain the owning service's policy and are evaluated by Workflow. Identity must not invent them.
- An institution may add stricter pairs; it may not remove a seeded pair without a recorded Security decision.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the classes, the eight seeded pairs and the explicit refusal to invent monetary thresholds | 2026-10-28 |
| Security | | Named confirmation required before production grants | |
| Operations | | Named confirmation required before access-review procedures go live | |

`P1-E02-D10` is recorded as approved with those production gates. Conflict evaluation is implemented
in grant administration (slice 8); role publication through Workflow remains slice 7.

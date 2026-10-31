---
decision: P1-E02-D09
title: Initial system permission catalogue and role templates
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Domain owners
  - Security
required_by: Slice 7
status: Approved
approved_on: 2026-10-28
brief_date: 2026-10-28
related: [ADR-0009]
---

# Decision brief: initial system permission catalogue and role templates

The catalogue is the set of permanent identifiers the platform enforces. The templates are
starting points an institution may copy and narrow; they are not a role hierarchy, and no code may
branch on a role name.

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D09-A | Which permission identifiers exist in Phase 1? | Exactly the identifiers enforced by code today, listed below; new identifiers arrive with their slice and are never renamed |
| D09-B | Which role templates ship as inert starting points? | The thirteen templates below, each a fixed permission set with no wildcard |
| D09-C | Who may change them? | A Security Administrator publishes a new role version; the permission catalogue itself changes only by code and migration |

## 2. Approved catalogue

Every entry below is already parsed and enforced in code. `P1-E02-D09` makes the set explicit; it
does not add speculative permissions.

| Permission | Owner service | Enforced by |
| --- | --- | --- |
| `platform.foundation.execute` | Identity and Access | reference protected operation |
| `platform.institution.create` | Core Ledger | institution creation |
| `platform.institution.activate` | Core Ledger | institution activation |
| `platform.institution.suspend` | Core Ledger | institution suspension |
| `platform.provisioning.repair` | Core Ledger | provisioning repair |
| `institution.tenant.create` | Identity and Access | tenant provisioning |
| `institution.region.manage` | Core Ledger | region lifecycle |
| `institution.branch.create` | Core Ledger | branch creation |
| `institution.branch.activate` | Core Ledger | branch activation |
| `institution.branch.suspend` | Core Ledger | branch suspension |
| `institution.calendar.manage` | Core Ledger | calendar publication |
| `institution.business_date.advance` | Core Ledger | business-date advancement |
| `identity.role.create` | Identity and Access | role administration |
| `identity.permission.assign` | Identity and Access | grant administration |
| `identity.access.review` | Identity and Access | access review |
| `identity.identity.create` | Identity and Access | identity registration |
| `identity.identity.update` | Identity and Access | identity status change |
| `identity.identity.view` | Identity and Access | identity retrieval |
| `identity.approval.review` | Identity and Access | approver eligibility query |
| `identity.mfa.manage` | Identity and Access | factor enrolment and step-up |
| `audit.event.view` | Audit and Reconciliation | audit access |
| `product.version.update` | Core Ledger | product version draft |
| `product.version.simulate` | Core Ledger | product simulation |
| `product.version.publish` | Core Ledger | product publication |
| `product.version.approve` | Operations and Workflow | approval decision |
| `customer.application.create` | Customer and Onboarding | application capture |
| `customer.application.update` | Customer and Onboarding | application revision |
| `customer.application.submit` | Customer and Onboarding | application submission |
| `customer.application.review` | Customer and Onboarding | application review |
| `customer.application.view` | Customer and Onboarding | application retrieval |
| `integration.api_client.manage` | Integration Hub | API client administration |
| `integration.credential_reference.manage` | Integration Hub | credential reference administration |

## 3. Approved role templates

Templates are inert data. An institution must publish a role version before any grant can reference
it, and no template holds a wildcard or a permission outside the catalogue.

| Template | Permissions | Note |
| --- | --- | --- |
| Platform Operator | `platform.foundation.execute`, `platform.institution.create`, `platform.provisioning.repair` | Activation and suspension are deliberately excluded |
| Platform Approver | `platform.institution.activate`, `platform.institution.suspend` | Separated from creation by rule D10 |
| Institution Administrator | `institution.region.manage`, `institution.branch.create`, `institution.calendar.manage`, `institution.business_date.advance` | Branch activation is excluded |
| Institution Approver | `institution.branch.activate`, `institution.branch.suspend`, `institution.business_date.advance` | Maker-checker partner for the administrator |
| Security Administrator | `identity.role.create`, `identity.permission.assign`, `identity.access.review` | No audit access |
| Auditor | `audit.event.view` | Read-only |
| Onboarding Officer | `customer.application.create`, `customer.application.update`, `customer.application.submit`, `customer.application.view` | Review excluded |
| Onboarding Reviewer | `customer.application.review`, `customer.application.view` | Submission excluded |
| Product Manager | `product.version.update`, `product.version.simulate` | Publish and approve excluded |
| Product Publisher | `product.version.publish` | Approve excluded |
| Integration Administrator | `integration.api_client.manage`, `integration.credential_reference.manage` | No audit access |
| Self-service actor | `identity.identity.view`, `identity.mfa.manage` | Applies to every human actor |
| Service identity | none | Services never hold human permissions |

## 4. Constraints

- Identifiers are permanent; a replacement is added and the old one retired.
- A template change publishes a new immutable role version; existing grants keep the version they were made against until re-granted.
- No wildcard, no implicit `*`, no role-name checks in business code.
- Launch-institution role names, localisation and any additional institution-specific roles remain institution configuration, not part of this decision.
- Partner and corporate delegated roles remain behind their own open gate.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the catalogue and templates above | 2026-10-28 |
| Domain owners | | Named confirmation required before publishing institution roles | |
| Security | | Named confirmation required before production grants | |

`P1-E02-D09` is recorded as approved with those production gates. The register keeps the gate text,
and the ledger keeps slice 7 unstarted until catalogue seeding and role publication are implemented.

---
id: ADR-0012
title: "Channel security boundaries: USSD, WhatsApp, chatbot and CMS"
status: Proposed
date: 2026-10-16
owners: [Architecture, Security, Channels and Engagement]
required_by: "Phase 1 - Channels, CMS and Communications"
architecture_item: 10
referenced_by:
  - docs/implementation/phase-1-banking-foundation/09-channels-cms-and-communications.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0012: Channel security boundaries: USSD, WhatsApp, chatbot and CMS

## Status

`Proposed` - not approved. `09-channels-cms-and-communications.md` requires this decision by topic name. The identifier `ADR-0012` was assigned by the [ADR index](README.md) because identifiers `ADR-0001` to `ADR-0011` are already used by other decisions.

## Context

- `AGENTS.md` (`## Channels, USSD, chatbots, and CMS`) assigns Channels and Engagement ownership of USSD sessions and menus, WhatsApp and website conversation orchestration, CMS content, knowledge articles, notification templates, campaigns, consent-aware delivery and human handoff; requires provider-specific protocols to stay behind Integration Hub adapters and canonical contracts; states that USSD session state and chatbot conversation state are not authoritative banking state; permits channels to initiate controlled application commands but never to post balances or bypass the owning service's validation; requires allow-listed intents or menu actions, explicit confirmation, idempotency, server-side permission checks and step-up for financial and sensitive actions; requires CMS content to be versioned, localised, approved, effective-dated and audited with separated author, approver and publisher permissions; and prohibits sending unrestricted generated chatbot output into privileged APIs or treating a phone number or chat identity as sufficient authentication.
- `BACKEND_ARCHITECTURE.md` requires this decision as architecture item 10 and describes the USSD request, WhatsApp chatbot request and CMS publication flows in `## Primary cross-service flows`.
- `09-channels-cms-and-communications.md` requires six related decisions: channel identity, linking and step-up; USSD session and financial-command idempotency; controlled chatbot intent and tool execution; CMS content, localisation and publication governance; notification finality, routing, consent and failover; and conversation, message and campaign retention.
- Requirement and story traceability: `COM-001` to `COM-010`, `US-034` to `US-037`, `US-075`, `US-076`.

## Decision

Not yet decided. This ADR must settle:

- Channel identity and linking: how a phone number or chat identity is linked to a customer record, how the link is verified, and how session context is bound to an institution.
- Step-up authentication for financial or sensitive channel actions, and the allow-list of intents and menu actions that a channel may initiate.
- USSD session model: session storage, expiry, out-of-order and replayed step handling, tampering checks, and the idempotency scope for financial commands initiated from a menu.
- Chatbot intent and tool execution: which intents are permitted, how generated output is constrained, what tools a bot may call, and the human-handoff trigger and boundary.
- CMS publication governance: authoring, approval, effective dating, localisation and separation of publish permissions.
- Notification and campaign delivery: consent capture and enforcement, routing and failover between providers, and delivery-finality rules.
- Retention: conversation, message, campaign and CMS content retention, and how it interacts with audit evidence retention.
- The boundary between channel state and banking state, and the prohibition on channels posting balances or bypassing owning-service validation.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Treating the phone number or chat identity as authentication | Prohibited; it is a shared, spoofable identifier |
| Letting a chatbot call banking APIs directly with generated arguments | Prohibited; generated output must be constrained by allow-listed intents and tools |
| Storing authoritative balances or holds in session state | Channel state is not a banking source of truth |
| A single operator role that authors and publishes CMS content | Fails the separated author, approver and publisher requirement |

## Consequences

- Channel epics and the Integration Hub share the canonical contract set, so adapter and channel work must be sequenced together.
- Step-up and intent allow-lists constrain the menu and conversation design, and require tests per intent.
- Consent and retention decisions affect campaign tooling, provider selection and storage cost.

## Compliance and evidence

- Negative tests for replayed or tampered USSD steps, unlinked channel identity, out-of-allow-list intent, self-published CMS content and missing consent.
- Idempotency evidence for financial commands initiated from a channel.
- Audit evidence for CMS publication and privileged channel actions.

## Open questions

- Whether a single customer may link several channel identities and how conflicts are resolved.
- Whether chatbot knowledge sources require a separate governance decision (related to Phase 5 model governance).
- Which retention periods apply to conversations that contain sensitive data.

## Approval gate

Do not implement USSD session handling, chatbot intent execution, CMS publication or channel-initiated financial commands until this ADR is `Accepted`. Contracts, allow-list definitions and negative tests may be drafted.

## Related documents

- [ADR index](README.md)
- [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) - channel identity linking and step-up coordination
- [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) - permissions for CMS and channel operations
- [09-channels-cms-and-communications.md](../implementation/phase-1-banking-foundation/09-channels-cms-and-communications.md)

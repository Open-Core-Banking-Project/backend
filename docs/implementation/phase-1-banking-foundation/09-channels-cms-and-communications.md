---
phase: "Phase 1 - Banking Foundation"
epic: "Channels, CMS and Communications"
owning_service: "Channels and Engagement"
modules:
  - USSD
  - Conversations
  - WhatsApp
  - Website Chatbot
  - Content Management
  - Notifications
  - Human Handoff
  - Campaign Foundations
status: Draft
requirements:
  - COM-001
  - COM-002
  - COM-003
  - COM-004
  - COM-005
  - COM-006
  - COM-007
  - COM-008
  - COM-009
  - COM-010
stories: [US-034, US-035, US-036, US-037, US-075, US-076]
dependencies:
  - Phase 0 architecture, security, privacy and channel-scope decisions
  - 01 Institution and Tenancy
  - 02 Identity, Roles and Permissions
  - 03 Customer and Onboarding
  - 04 Products and Accounts
  - 06 Workflow and Maker-Checker
  - 07 Payments, Collections and VTU
  - 08 Integration Hub and Providers
adrs:
  - Channel identity, linking and step-up model
  - USSD session and financial-command idempotency model
  - Controlled chatbot intent and tool-execution model
  - CMS content, localisation and publication governance
  - Notification finality, routing, consent and failover model
  - Conversation, message and campaign retention model
last_updated: 2026-10-15
---

# Epic: Channels, CMS and Communications

## 1. Objective

Deliver the C#/.NET Channels and Engagement microservice with first-class USSD, WhatsApp chatbot, website chatbot, CMS, notification and human-handoff modules. The service provides controlled customer interaction and communication without becoming an authoritative source for identity, balances, payment outcomes, customer records or provider protocols.

The epic implements the formal Phase 1 communications requirements COM-001 through COM-010 and user stories US-034 through US-037. It also implements the user-directed and approved architecture foundations for USSD, WhatsApp, website chat and CMS. US-075 and US-076 are shared channel outcomes: this backend supplies controlled APIs, localisation metadata, content and notification behaviour, while frontend applications and owning banking services retain their responsibilities.

USSD and conversational channels may explain, collect input and initiate an allow-listed command. The owning banking service must still validate identity, permission, ownership, restrictions, limits, pricing, approval and idempotency. No channel session, message, CMS item or notification status proves a financial outcome.

## 2. Success measures

- USSD, WhatsApp, website chat, CMS and Notifications are explicit modules with independent domain/application boundaries inside one initial deployable microservice.
- Every protected channel action declares a stable capability permission and enforces institution, channel, customer/account, device, transaction and step-up scope as applicable.
- A phone number, MSISDN, WhatsApp account, session ID or conversational claim never establishes banking identity by itself.
- USSD financial commands use explicit confirmation and a stable idempotency identity that survives session timeout, retries and provider redelivery.
- Chatbots invoke only allow-listed, schema-validated intents. Generated or free-form text cannot call privileged APIs directly.
- CMS content is versioned, localised, sanitised, approved, effective-dated, published and auditable, with author/approver/publisher separation where configured.
- Notifications originate from confirmed or explicitly classified business facts and duplicate events do not create duplicate customer messages.
- Channel selection respects institution policy, country, event class, verified customer preference, consent, quiet hours, suppression and mandatory-security exceptions.
- Provider routing and delivery use Integration Hub canonical contracts; provider credentials, SDKs and raw webhooks never enter this service.
- Notification states distinguish requested, rendered, queued, provider-accepted, sent, delivered, bounced, failed, suppressed and uncertain.
- Operations can inspect provider-independent delivery state, failure queues and authorised replay without seeing OTPs, secrets or unrestricted message bodies.
- Security, privacy, accessibility, localisation, resilience, replay and restore evidence is complete for each enabled Phase 1 channel journey.

Numeric session, latency, delivery, retention and availability targets require approved institution/channel decisions. Agents must not invent them.

## 3. Scope and allocation

### Included

- Institution-scoped channel profiles, policies, enabled capabilities, limits and registered endpoint references.
- USSD service codes, short-code routes, versioned menus, menu nodes, session state, events and customer-channel links.
- Canonical USSD request/response handling after Integration Hub verifies the provider protocol.
- WhatsApp and website conversations, participants, messages, approved intents, allow-listed actions and delivery-status projection.
- Customer-to-channel account linking through Identity and Access controlled proof, step-up and revocation.
- Human-handoff request and assignment foundations integrated with Operations and Workflow/service requests.
- CMS content items, versions, categories, localisation, approval, schedules, FAQs, knowledge articles and channel bindings.
- Notification templates, versioning, approval, effective dates, localisation, placeholders and rendering.
- Notification requests, preferences, consent projection, quiet hours, suppression, routing, provider-independent delivery attempts and status history.
- SMS, email, push and in-app channel selection; provider transport remains in Integration Hub.
- Mandatory-security notification classification that cannot be suppressed where approved policy forbids suppression.
- OTP transport safeguards. OTP generation, validation, expiry and attempt policy remain with Identity and Access.
- Campaign domain/persistence foundations only; production marketing campaigns remain disabled until scope, consent and approval policies are authorised.
- Versioned APIs/events, outbox/inbox, idempotency, tenant isolation, audit, observability and recovery.
- Synthetic provider/channel adapters for development and contract tests.

### Conditionally enabled Phase 1 journeys

The launch institution must approve each journey before production enablement:

- USSD balance enquiry and mini statement.
- USSD internal transfer, NIP transfer, bill payment or VTU purchase.
- USSD support/service-request initiation.
- WhatsApp and website-chat approved knowledge responses.
- WhatsApp and website-chat service requests and human handoff.
- Selected authenticated banking actions through conversation channels.
- CMS categories, locales, fallback rules, approval levels and publication channels.

Until approved, implement contracts, state machines and synthetic journeys only. Feature flags do not substitute for security review, owner-service readiness or provider certification.

### Excluded

- Authoritative authentication, MFA, OTP verification, device trust, entitlements and token issuance; Identity and Access owns them.
- Account, balance, statement, payment, bill, VTU, card, loan or document business state; the relevant banking service owns it.
- Ledger posting, balance mutation or financial reversal under any channel condition.
- Provider webhook/signature verification, credential storage and provider-specific SMS/email/USSD/WhatsApp protocols; Integration Hub owns them.
- Payment-provider failover and financial route choice; Payments and VTU owns them.
- Workflow policy and approval decisions; Operations and Workflow owns them.
- Customer master contacts and identity evidence; Customer and Onboarding owns them.
- Client application UI implementation for internet/mobile/staff channels.
- Unrestricted generative chatbot answers, autonomous financial agents or natural-language-to-privileged-API execution.
- Production campaigns, segmentation and marketing automation until explicit institution/compliance scope is approved.
- Phase 3 enhanced digital-channel capabilities not explicitly approved for Phase 1.

### Requirement-source note

COM-001 through COM-010 and US-034 through US-037 are explicit SRS/backlog requirements. USSD, WhatsApp chatbot, website chatbot and CMS foundations are explicit user-directed architecture scope and are documented in the backend architecture, but do not have dedicated requirement IDs in the supplied SRS. Their production journey details therefore remain open decisions rather than inferred requirements.

## 4. Service and module boundaries

Channels and Engagement remains one of the nine initial deployable microservices. The modules are not tiny independent network services in Phase 1, but their boundaries must permit later extraction.

```text
CoreBanking.ChannelsEngagement.Domain
  Modules/Ussd
  Modules/Conversations
  Modules/ContentManagement
  Modules/Notifications
  Modules/Campaigns

CoreBanking.ChannelsEngagement.Application
  Modules/Ussd
  Modules/WhatsApp
  Modules/Chatbot
  Modules/ContentManagement
  Modules/Notifications
  Modules/HumanHandoff

CoreBanking.ChannelsEngagement.Infrastructure
CoreBanking.ChannelsEngagement.Api
CoreBanking.ChannelsEngagement.Contracts
CoreBanking.ChannelsEngagement.Workers
```

Boundary rules:

1. Modules communicate through application interfaces and local domain events, not by querying one another's EF Core tables.
2. USSD owns menu/session orchestration, not provider protocol or financial status.
3. Conversations owns conversation/message/intent/handoff orchestration, not authentication or customer-service case state.
4. CMS owns editorial content and publication; Notifications owns message templates and delivery requests.
5. Notifications owns customer communication intent/routing state, not provider transport state or the originating business outcome.
6. Campaigns cannot bypass preference, consent, suppression, approval, audience and retention controls.
7. All modules access other microservices only through versioned contracts; no cross-service database access is permitted.

## 5. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Channel administrator | `channel.profile.read` | Institution/channel/environment | No | None |
| Channel administrator | `channel.profile.configure` | Institution/channel/environment/capability | Yes | Maker-checker before production publication |
| USSD menu author | `channel.ussd.menu.manage` | Institution/service code/locale | No | Cannot publish own version where separation applies |
| USSD menu approver | `channel.ussd.menu.approve` | Institution/service code/version | Yes | Cannot approve own authored version |
| USSD publisher | `channel.ussd.menu.publish` | Institution/service code/environment | Yes | Approved version only |
| Conversation administrator | `conversation.intent.manage` | Institution/channel/intent/action | Yes | Maker-checker for privileged intents |
| Customer-service agent | `conversation.handoff.accept` | Institution/queue/customer scope | Step-up for sensitive conversation | Assignment required |
| Customer-service supervisor | `conversation.assignment.manage` | Institution/queue/team | Yes | Reason required for reassignment |
| Content author | `cms.content.create` | Institution/category/channel/locale | No | Cannot approve/publish restricted own content |
| Content approver | `cms.content.approve` | Institution/category/channel/locale | Yes for restricted content | Cannot approve own draft |
| Content publisher | `cms.content.publish` | Institution/channel/locale/schedule | Yes | Approved version only |
| Content operator | `cms.content.withdraw` | Institution/channel/content | Yes | Reason and approval where configured |
| Communication administrator | `notification.template.manage` | Institution/event/channel/locale | Yes | Maker-checker before publication |
| Communication administrator | `notification.route.manage` | Institution/event/country/channel | Yes | Maker-checker |
| Notification operator | `notification.delivery.read` | Institution/event/customer/reference/date | Step-up for sensitive detail | None |
| Notification operator | `notification.delivery.replay` | Institution/request/attempt | Yes | Reason; approval for sensitive/financial notices |
| Customer | `channel.preference.manage.self` | Own verified identity/preferences | Step-up for sensitive preference changes | None |
| Customer | `channel.action.execute.self` | Own linked channel and owner-service scope | Per action/policy | Owner service decides |
| Calling service identity | `notification.request.create` | Allow-listed event/template/data contract | Service authentication | No human role |
| Integration Hub identity | `channel.ingress.deliver` | Exact provider/channel/subscription | Service authentication | Provider already verified |

Identity and Access owns grants, roles and entitlement versions. Channels and Engagement enforces permissions and scopes for its resources. The owning banking service independently authorises every business command initiated by a channel.

A user holding multiple roles still cannot author, approve and publish the same restricted version or replay their own sensitive failed delivery without required approval. Temporary/delegated permissions are time-bound and auditable.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| ChannelProfile | Institution channel enablement and policy reference | `ChannelProfileId` | Channel type, environment and capability versions |
| CustomerChannelLink | Revocable link between channel identity and trusted customer identity | `CustomerChannelLinkId` | References Identity customer/session proof |
| UssdMenu | Stable menu identity and publication lineage | `UssdMenuId` | Has immutable versions and nodes |
| UssdMenuVersion | Localised, effective menu graph | `UssdMenuVersionId` | Pinned by a session |
| UssdSession | Short-lived menu state and action correlation | `UssdSessionId` | Provider session ref, menu version, customer link |
| Conversation | Channel conversation context and retention state | `ConversationId` | Participants, messages, channel account |
| ConversationMessage | Immutable inbound/outbound message metadata/content ref | `MessageId` | Status history and correlation |
| BotIntent | Versioned allow-listed intent and schema | `BotIntentId` | Maps to zero or one approved action definition |
| BotAction | Controlled owner-service command/query definition | `BotActionId` | Permission, confirmation, step-up and idempotency policy |
| HandoffRequest | Controlled transfer to a human queue | `HandoffRequestId` | Conversation, queue and service-request ref |
| ContentItem | Stable CMS content identity/category | `ContentItemId` | Has versions/localisations/bindings |
| ContentVersion | Immutable editorial snapshot | `ContentVersionId` | Approval, schedule and publication lineage |
| KnowledgeArticle | Structured approved knowledge content | `KnowledgeArticleId` | Search metadata and channel bindings |
| NotificationTemplate | Stable event/channel template identity | `NotificationTemplateId` | Has approved versions/localisations |
| NotificationRequest | One communication intent from an owner event/command | `NotificationRequestId` | Template, subject, classification and route |
| DeliveryAttempt | Provider-independent transport attempt projection | `DeliveryAttemptId` | Integration Hub delivery ref and status history |
| CustomerPreference | Verified preference/quiet-hour/consent projection | `PreferenceId` | Customer ref, event class and channel |
| SuppressionRule | Versioned policy/customer suppression | `SuppressionRuleId` | Exception classification and reason |
| Campaign | Disabled-by-default campaign definition | `CampaignId` | Audience, approval and schedule foundations |

Cross-service IDs remain references. There are no database foreign keys to Identity, Customer, Payments, Ledger, Workflow, Documents, Integration Hub or Audit/Reconciliation databases.

### Value objects

- `InstitutionId`, `ChannelType`, `ChannelAccountReference`, `CustomerReference` and `ServiceCode`.
- `MenuNodeKey`, `SessionState`, `SessionExpiry`, `Locale`, `CountryCode` and `TimeZoneId`.
- `ConversationChannel`, `ParticipantReference`, `IntentCode`, `ActionCode` and `ConfidencePolicyVersion`.
- `ContentSlug`, `ContentCategory`, `ContentClassification`, `PublicationWindow` and `SanitisedMarkup`.
- `TemplateCode`, `TemplateVersion`, `PlaceholderSchema`, `MessageClassification` and `EventType`.
- `RecipientReference`, `PreferenceVersion`, `ConsentVersion`, `QuietHours`, `SuppressionReason` and `MandatoryNoticeCode`.
- `NotificationStatus`, `DeliveryStatus`, `ProviderDeliveryReference`, `IdempotencyKey`, `CorrelationId` and `CausationId`.
- `BusinessReference`, `ConfirmationTokenReference`, `StepUpProofReference` and `OwnerCommandReference`.
- `DateTimeOffset` for events/timestamps and `DateOnly` only where a date has no time-of-day meaning.

### Invariants

1. Every tenant-owned record carries a trusted institution ID and all queries enforce it.
2. A customer-channel link is established only from trusted Identity proof and is revocable; a raw phone/chat identity is insufficient.
3. A session or conversation cannot grant permissions that are absent from the current trusted entitlement/step-up context.
4. USSD sessions pin an approved menu version. Publication cannot change an in-progress session graph.
5. A financial/sensitive channel action must be allow-listed, explicitly confirmed, idempotent and step-up authenticated where policy requires.
6. Session timeout/redelivery cannot create a second owner-service financial command.
7. Chatbot free-form/generated output cannot select an internal API, permission, customer/account or command payload.
8. A privileged `BotAction` contains a versioned fixed schema, owner service, permission, confirmation and step-up policy.
9. Content/template versions are immutable after submission; corrections create a new version.
10. A restricted author cannot approve/publish their own version when segregation policy applies.
11. Only an approved version within its effective window may be published/rendered.
12. Localisation fallback is explicit and versioned; absence of an approved locale fails safely instead of mixing arbitrary text.
13. Placeholder names/types/classification are allow-listed. Missing or extra protected data fails rendering.
14. OTP values and authentication secrets are never persisted in templates, message history, logs, events or retrieval APIs.
15. A notification request is unique by institution, owner event/business action and notification purpose/version.
16. Duplicate owner events and worker replay cannot create a duplicate notification request or known duplicate provider delivery.
17. Notification routing evaluates mandatory classification, consent, preference, quiet hours, suppression and current Integration readiness before transport.
18. Mandatory security notices bypass only the specifically approved suppression/quiet-hour rules; they do not bypass recipient verification or security controls.
19. Delivery state never changes the originating business or financial state.
20. Campaign delivery cannot use a customer without approved audience provenance and effective consent.
21. Messages/content are retained, redacted/anonymised or placed on legal hold only under approved policy.

Database constraints, unique indexes and concurrency tokens enforce version uniqueness, idempotency, session transition, one publication per scope/effective time, approval separation and delivery-attempt ordering.

## 7. State transitions

USSD session:

```text
Started -> AwaitingInput -> AwaitingConfirmation -> CommandSubmitted -> Completed
    |            |                  |                    |
    +----------> TimedOut <---------+--------------------+
    +----------> Cancelled
    +----------> Blocked
CommandSubmitted -> OutcomePending -> Completed
                                  -> InvestigationRequired
```

Conversation:

```text
Open -> BotHandling -> AwaitingCustomer -> BotHandling
                  \-> HandoffRequested -> Assigned -> HumanHandling -> Closed
Open/BotHandling/AwaitingCustomer -> Blocked | Expired | Closed
```

CMS/template version:

```text
Draft -> Submitted -> Approved -> Scheduled -> Published -> Superseded
   |         |           |           |            |
   +------> Withdrawn <- Rejected ----+------------+-> Withdrawn
```

Notification:

```text
Requested -> Validated -> Rendered -> Queued -> ProviderAccepted -> Sent -> Delivered
      |          |          |          |                  |          |
      +------> Rejected      +------> Suppressed          +------> Bounced
                                       |                  +------> Failed
                                       +-------------------------> Uncertain
```

Human handoff:

```text
Requested -> Queued -> Assigned -> Accepted -> Resolved -> Closed
     |          |          |          |
     +------> Cancelled     +------> Expired/Reassigned
```

Transitions use typed commands, expected versions and immutable status history. Provider delivery statuses arrive only through Integration Hub canonical events.

## 8. Channel identity and authorisation

- Integration Hub authenticates provider protocol ingress; it does not authenticate the banking customer.
- Channels resolves a channel account/session to a revocable `CustomerChannelLink` created through an approved Identity journey.
- Identity and Access issues or evaluates current authentication, device, step-up and entitlement evidence.
- Channels forwards trusted actor/customer, permission, scope, channel, session, step-up and idempotency context to the owning service.
- The owning service rechecks permission, ownership, restrictions, limits, approval and business invariants.
- Sensitive output is minimised/masked for the channel; full account identifiers, secrets and protected documents are not rendered into USSD/chat.
- Revoked links, stale entitlements, failed step-up or unknown identity deny protected actions by default.
- Public knowledge/support journeys may operate without banking identity only when the intent and content are explicitly marked public and contain no customer data.

## 9. Use cases

### UC-01 - Publish a USSD menu version

- Actors: USSD menu author, approver and publisher.
- Preconditions: Service code/channel profile exists; locale/fallback and allowed actions are approved.
- Permission: `channel.ussd.menu.manage`, `channel.ussd.menu.approve`, `channel.ussd.menu.publish` with separation.
- Main flow: Create immutable draft graph; validate reachability, cycles, depth, text/encoding, action bindings and locale coverage; preview/simulate; approve; schedule/publish; invalidate new-session cache only.
- Failure outcomes: Orphan node, ambiguous input, forbidden action, missing locale, author self-approval or overlapping publication.
- Idempotency: Menu ID + version command ID.
- Audit/events: Full version lineage and `UssdMenuVersionPublished`.

### UC-02 - Handle a USSD session step

- Actor: Integration Hub delivering a verified canonical request.
- Preconditions: Enabled route, approved menu version and bounded provider session identity.
- Permission: Trusted service ingress plus customer permission for protected action.
- Main flow: Deduplicate request; start/load session; pin menu version; validate expected node/input; render masked response; append session event; return canonical response.
- Failure outcomes: Unknown/expired/replayed session, invalid input, stale customer link, unavailable dependency or rate limit.
- Idempotency: Institution + provider + provider session + step sequence/request identity.
- Transaction: Session state, event and outbox commit locally.

### UC-03 - Initiate a protected action from USSD

- Actor: Linked/authenticated customer.
- Preconditions: Allow-listed action; required input; current identity/permission/step-up; explicit summary/confirmation.
- Main flow: Bind confirmation to session, actor, action, resource, amount/currency where applicable and expiry; create stable owner-command key; call owning service once; store only command reference/status; render authoritative response/pending state.
- Failure outcomes: Confirmation changed/expired, permission denied, owner rejection, dependency timeout or uncertain outcome.
- Idempotency: Session + confirmed action occurrence; reused on redelivery/recovery.
- Rule: Session timeout never creates a new command or assumes failure.

### UC-04 - Process a WhatsApp or website-chat message

- Actor: Integration Hub or controlled website channel.
- Preconditions: Verified canonical message, bounded content, channel profile and consent/retention context.
- Main flow: Deduplicate; resolve conversation/participant link; classify against approved intent registry; execute public knowledge response, ask clarification, request step-up, invoke a controlled action or hand off; persist outcome and queue response.
- Failure outcomes: Unknown intent, low confidence, harmful/unsupported content, identity mismatch, revoked consent or unavailable owner.
- Idempotency: Provider/channel message ID plus institution/channel account.
- Rule: Unknown/low-confidence privileged intent cannot fall through to a guessed action.

### UC-05 - Answer from approved CMS knowledge

- Actor: Chatbot/USSD/web channel module.
- Preconditions: Approved published article/content version for channel/locale/effective time and access classification.
- Main flow: Search only approved index/projection; apply explicit locale fallback; render cited content version; record safe content/version reference.
- Failure outcomes: No approved answer, restricted content without identity/permission or stale index.
- Alternative: Offer human handoff or safe generic response.
- Rule: Retrieval index is not publication authority; current CMS publication is revalidated.

### UC-06 - Request human handoff

- Actor: Customer/bot/support flow.
- Preconditions: Approved queue and minimal required context/consent.
- Main flow: Create handoff request; optionally create Operations service request; queue; assign authorised agent; expose bounded conversation summary; record acceptance/resolution.
- Failure outcomes: No eligible queue, SLA breach, customer leaves or sensitive context access denied.
- Idempotency: Conversation + active handoff purpose.
- Audit: Assignment, access, transfer, reason and linked case/service-request refs.

### UC-07 - Create, approve and publish CMS content

- Actors: Content author, approver and publisher.
- Preconditions: Approved category/schema/channels/locales/classification.
- Main flow: Create draft; sanitise/validate links/placeholders/access class; preview each channel/locale; submit; approve; schedule/publish; emit cache invalidation; supersede prior effective version.
- Failure outcomes: Unsafe markup/link, missing locale, classification mismatch, self-approval, schedule overlap or stale version.
- Idempotency: Content + version command/publication occurrence.
- Audit: Before/after metadata, immutable body digest, participants, reason and timestamps.

### UC-08 - Manage a notification template

- Actor: Communication administrator and approver.
- Preconditions: Event contract, channel, placeholder schema, classification and locale policy exist.
- Main flow: Create version; validate allowed placeholders/output limits/escaping; render only synthetic preview data; approve/effective-date/publish.
- Failure outcomes: Unknown/protected placeholder, secret/OTP logging risk, unsupported locale or self-approval.
- Rule: OTP placeholders use a dedicated secret value path that is never previewed, persisted or retrievable.

### UC-09 - Create notification from a business event

- Actor: Allow-listed internal service/event consumer.
- Preconditions: Event is a confirmed fact or explicitly approved pending/security classification; contract/schema version accepted.
- Main flow: Inbox-deduplicate; create request; resolve verified recipient ref/preferences/consent; choose approved template/localisation; render; evaluate suppression/quiet hours; select route; queue Integration Hub delivery; record canonical outcomes.
- Failure outcomes: Unconfirmed financial state, stale/missing contact, no template, suppressed, provider uncertain or invalid placeholders.
- Idempotency: Institution + source event/business ref + notification purpose + recipient/channel policy occurrence.
- Rule: A provider delivery event cannot change the business fact.

### UC-10 - Route and fail over a notification

- Actor: Notification worker.
- Preconditions: Approved routing policy and fresh Integration Hub readiness.
- Main flow: Select candidate by message class, institution, country, preference/consent, priority, cost and health; submit with stable key; observe delivery state; fail over only under approved safe conditions.
- Failure outcomes: All routes unavailable, unknown provider submission, quiet hours or expired message.
- Rule: An uncertain provider submission blocks duplicate failover unless provider semantics/status verification prove it safe.

### UC-11 - Manage customer preferences and consent

- Actor: Authenticated customer or authorised staff under policy.
- Preconditions: Current identity/step-up and allowed preference scope.
- Main flow: Show effective choices; record versioned preference/consent with provenance; publish change; keep mandatory notice rules visible.
- Failure outcomes: Attempt to suppress legally/security mandatory event, stale version, unauthorised proxy or invalid quiet hours/timezone.
- Audit: Actor, proof, old/new setting, purpose, policy version and time.

### UC-12 - Replay a failed notification

- Actor: Notification operator/approver.
- Preconditions: Immutable original request/attempt exists; business relevance/expiry still valid; consumer/customer impact reviewed.
- Main flow: Preview safe metadata; enter reason; obtain approval where required; create replay linked to original purpose; re-evaluate recipient, consent and route; reuse deduplication semantics; record outcome.
- Failure outcomes: Already delivered, expired, suppressed, recipient changed, provider uncertain or approval missing.
- Rule: Operators cannot edit status or manufacture a new source business event.

## 10. USSD implementation rules

- Receive and return canonical envelopes through Integration Hub; no USSD gateway SDK in this service.
- Persist a server-owned session ID and map provider session IDs under institution/provider/service-code scope.
- Pin menu version, locale, channel policy and applicable action schema for the session.
- Enforce step sequence/version to reject out-of-order or replayed input without corrupting state.
- Store minimal masked input. PINs, OTPs, secrets and full financial credentials are never stored.
- Validate menu graphs for unreachable nodes, unsafe loops, response-size/encoding limits and missing termination paths.
- Use explicit confirmation for sensitive/financial actions and bind confirmation to exact material fields.
- Generate the owner-service idempotency key before first command submission and reuse it after timeout/recovery.
- Query authoritative status using the original command/reference; never infer failure from session expiry.
- Keep balances/statements only in the response/session-safe transient projection; never make them authoritative or long-lived.
- Rate/velocity controls complement Identity/Risk/owner limits and do not replace them.
- Return generic, non-enumerating messages for identity/permission/resource failures.

## 11. WhatsApp and chatbot implementation rules

- Integration Hub verifies WhatsApp/provider signatures, event IDs and replay status before canonical delivery.
- Website-chat ingress uses the approved gateway/session protection and the same canonical message contract.
- Map provider/channel identity to a conversation participant, not directly to a customer account.
- Resolve banking identity only through a current customer-channel link and Identity proof.
- Intent definitions are versioned, allow-listed and scoped as `PublicKnowledge`, `AuthenticatedQuery`, `ControlledCommand`, `SupportHandoff` or `Unsupported`.
- Controlled commands use typed schemas, explicit confirmation, stable idempotency and owner-service authorisation.
- A model/classifier may propose an intent or response draft; deterministic policy validates intent, confidence, permissions and schema before any action.
- Generated text cannot provide tool names, routes, account IDs, permissions or command arguments to privileged execution.
- Knowledge answers must come from published, access-appropriate CMS content. If no grounded answer exists, respond safely or hand off.
- Mask sensitive content in transcripts and agent summaries. Restrict agent access to assigned conversations/customer scope.
- Commands and provider message deliveries are separate states. A sent chat message does not prove command success.
- Provide block/report/rate-limit flows and abuse evidence without exposing moderation/security rules.

## 12. CMS and knowledge governance

CMS content types include approved FAQ, knowledge article, USSD prompt/help text, chatbot answer, channel banner/notice and support guidance. Notification templates remain in the Notifications module because they have event/recipient/delivery semantics.

Each content version records institution, content type/category, classification, owner, locale, approved channels, sanitised body/structured fields, body digest, author, approval/publisher refs, effective window and superseded version.

Publication rules:

- Sanitise markup and validate links/media against approved policies.
- Do not allow scripts, active content, embedded credentials, arbitrary API calls or secret placeholders.
- Preview per channel and locale using synthetic data.
- Enforce author/approver/publisher separation for restricted content.
- Prevent overlapping published versions for the same content/channel/locale/effective instant.
- Publish through a local transaction and outbox event; consumers/cache/indexers update idempotently.
- Search indexes and caches are projections. Rendering rechecks current publication/access state.
- Withdrawal immediately prevents new rendering after cache invalidation; historical conversations retain the content-version reference according to policy.
- Changes create new immutable versions and preserve prior audit evidence.

## 13. Notification and communication rules

Message classifications include `AuthenticationSecret`, `SecurityMandatory`, `TransactionalMandatory`, `TransactionalOptional`, `Service`, and `Marketing`. Exact definitions and legal treatment require approval.

Routing order:

1. Validate trusted source event/command and message classification.
2. Resolve a verified recipient reference/contact through approved Customer/Identity contract or trusted tokenised address reference.
3. Load effective institution/event/channel policy and customer preference/consent versions.
4. Apply mandatory exceptions, suppression, quiet hours, expiry and channel eligibility.
5. Select an approved template version/locale and validate placeholder schema.
6. Render with contextual output encoding and protected-value handling.
7. Select channel/provider route using approved policy and fresh Integration Hub readiness.
8. Persist request/attempt/outbox before transport.
9. Track canonical provider states without collapsing them into business status.
10. Retry/fail over only under operation-specific safe rules and the same notification identity.

OTP/authentication-secret handling:

- Identity and Access owns generation, hashing/verification, expiry, attempt counters and authentication decision.
- Channels may receive a one-time protected delivery value through an approved in-memory/ephemeral contract.
- The value is never stored in message tables, outbox payloads, traces, preview APIs, dead letters or audit bodies.
- Workers/loggers/errors must redact the value and its direct derivatives.
- Replay of an expired authentication-secret notification is denied; a new OTP requires a new Identity flow.

## 14. API contracts

### Channel administration

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/channels/profiles` | `channel.profile.read` | Filters/page | Profile summaries | Query |
| POST | `/api/v1/channels/profiles` | `channel.profile.configure` | `CreateChannelProfileRequest` | Draft profile | Yes |
| POST | `/api/v1/channels/profiles/{id}/submit` | `channel.profile.configure` | Version/reason | Workflow ref | Yes |
| POST | `/api/v1/ussd/menus` | `channel.ussd.menu.manage` | `CreateUssdMenuRequest` | Menu/draft version | Yes |
| POST | `/api/v1/ussd/menus/{id}/versions` | `channel.ussd.menu.manage` | Graph/locales/actions | Draft version | Yes |
| POST | `/api/v1/ussd/menu-versions/{id}/approve` | `channel.ussd.menu.approve` | Expected version/reason | Approval result | Yes |
| POST | `/api/v1/ussd/menu-versions/{id}/publish` | `channel.ussd.menu.publish` | Effective window | Publication result | Yes |
| POST | `/api/v1/conversation/intents` | `conversation.intent.manage` | Intent/action schema | Draft intent | Yes |
| GET | `/api/v1/conversations` | Scoped conversation read | Bounded filters/page | Masked conversation summaries | Query |
| POST | `/api/v1/conversations/{id}/handoff` | Handoff permission/self | Queue/purpose | Handoff ref | Yes |
| POST | `/api/v1/handoffs/{id}/accept` | `conversation.handoff.accept` | Assignment version | Assignment | Yes |

### CMS and notifications

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/cms/content` | `cms.content.create` | Content metadata/draft | Content/version | Yes |
| POST | `/api/v1/cms/content/{id}/versions` | `cms.content.create` | Localised structured content | Draft version | Yes |
| POST | `/api/v1/cms/versions/{id}/submit` | `cms.content.create` | Expected version/reason | Workflow ref | Yes |
| POST | `/api/v1/cms/versions/{id}/approve` | `cms.content.approve` | Decision/reason | Approval result | Yes |
| POST | `/api/v1/cms/versions/{id}/publish` | `cms.content.publish` | Channels/effective window | Publication result | Yes |
| POST | `/api/v1/cms/versions/{id}/withdraw` | `cms.content.withdraw` | Reason/approval ref | Withdrawal result | Yes |
| GET | `/api/v1/cms/public/{slug}` | Public/authorised by classification | Locale/channel | Published content projection | Query |
| POST | `/api/v1/notifications/templates` | `notification.template.manage` | Template/schema/locales | Draft template | Yes |
| POST | `/api/v1/notifications/templates/{id}/preview` | Template preview permission | Synthetic placeholder data | Redacted preview | No state change |
| GET | `/api/v1/notifications/deliveries` | `notification.delivery.read` | Bounded filters/page | Masked delivery summaries | Query |
| POST | `/api/v1/notifications/{id}/replays` | `notification.delivery.replay` | Reason/approval ref | Replay ref | Yes |
| GET | `/api/v1/channel-preferences/me` | `channel.preference.manage.self` | None | Effective preferences | Query |
| PUT | `/api/v1/channel-preferences/me` | `channel.preference.manage.self` | Expected version/preferences | Updated preferences | Yes |

### Internal and ingress APIs

| Method | Route | Authentication | Request | Response |
| --- | --- | --- | --- | --- |
| POST | `/internal/v1/notification-requests` | Scoped service identity | `CreateNotificationRequest` | Request/status ref |
| GET | `/internal/v1/notification-requests/{id}` | Scoped service identity | None | Provider-independent status |
| POST | `/internal/v1/channel-ingress/ussd` | Integration Hub identity | Canonical USSD envelope | Canonical session response |
| POST | `/internal/v1/channel-ingress/messages` | Integration Hub/approved gateway identity | Canonical message envelope | Accepted/conversation ref |
| POST | `/internal/v1/delivery-status` | Integration Hub identity | Canonical delivery status | Applied/duplicate result |

Stable errors use RFC 7807 on trusted APIs, including `channel.identity_unlinked`, `channel.step_up_required`, `channel.session_expired`, `channel.replay_detected`, `channel.intent_unsupported`, `cms.version_conflict`, `cms.locale_unavailable`, `notification.suppressed`, `notification.recipient_unverified`, `notification.provider_uncertain` and `channel.cross_tenant_denied`.

No API accepts a client-computed balance, business outcome, permission, approval, provider route, template publication state or arbitrary owner-service URL. Search/list APIs are bounded and masked. Public CMS endpoints expose only published public fields.

## 15. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `ChannelProfilePublished` | Approved profile becomes effective | Institution/channel/version/capabilities | Channel profile | Integration, Audit |
| `CustomerChannelLinked` | Trusted link established | Institution/customer/channel/link/proof ref | Customer/channel | Identity, Audit |
| `CustomerChannelLinkRevoked` | Link revoked | Link/reason/time | Link | Identity, Audit |
| `UssdMenuVersionPublished` | Approved menu effective | Service code/menu/version/locales/effective time | Menu | Channel cache, Audit |
| `ChannelActionInitiated` | Confirmed controlled action submitted | Channel/session/conversation/owner command/action refs | Owner command | Audit/Operations |
| `ConversationHandoffRequested` | Bot/customer requests agent | Conversation/queue/purpose/customer ref | Conversation | Workflow/Operations |
| `CmsContentPublished` | Content version effective | Content/version/category/locales/channels/digest | Content | USSD, chatbot, web, Audit |
| `CmsContentWithdrawn` | Published version withdrawn | Content/version/reason/time | Content | Channel caches/indexers |
| `NotificationRequested` | Request durably accepted | Request/source/recipient ref/class/template purpose | Request | Notification worker/Audit |
| `NotificationSuppressed` | Policy prevents delivery | Request/reason/policy version | Request | Owner/Operations |
| `NotificationDeliveryStatusChanged` | Canonical provider state applied | Request/attempt/channel/status/provider ref | Request | Owner/Operations/Audit |
| `NotificationDeliveryDeadLettered` | Processing exhausted | Request/attempt/safe reason | Request | Operations |
| `ChannelPreferenceChanged` | Effective preference/consent changes | Customer/preference/version/provenance | Customer | Channels/Audit |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| Confirmed business events | Ledger/Payments/Customer/Workflow/others | Map allow-listed fact to notification purpose | Event ID + purpose + recipient ref | Inbox/DLQ; no guessed finality |
| `IntegrationDeliveryStatusRecorded` | Integration Hub | Append provider-independent delivery state | Provider delivery event ID | Inbox; conflicts investigated |
| `ProviderReadinessChanged` | Integration Hub | Refresh routing input with freshness | Event ID + provider/service | Inbox; stale becomes Unknown |
| `EntitlementVersionChanged` | Identity | Invalidate cached channel permissions | Event ID + subject/version | Inbox; deny on stale proof |
| `CustomerContactChanged` | Customer | Refresh/re-resolve verified contact projection | Event ID + customer/version | Inbox; never overwrite local history |
| `WorkflowDecisionRecorded` | Workflow | Apply exact CMS/menu/template/profile decision | Event ID + target/version | Inbox; mismatches rejected |
| `ServiceRequestStatusChanged` | Operations | Update linked handoff outcome | Event ID + handoff/case | Inbox |

Events carry event ID/type/schema version, institution ID, occurred time, correlation/causation IDs and business/aggregate reference. They exclude OTPs, secrets, unrestricted message bodies, full contact values and unnecessary PII. Outbox/inbox and consumer idempotency are mandatory.

## 16. Database design

### Ownership

- Database: `channels_engagement_db`.
- Schemas: `ussd`, `conversation`, `channel`, `cms`, `notification`, `campaign`, `integration`.
- Migration assembly: `CoreBanking.ChannelsEngagement.Infrastructure`.
- Database principal: Channels and Engagement runtime/migration identities only, separated by privilege.

### Tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `ussd.service_codes` | Institution service/short-code definitions | Unique institution + environment + service code | Yes | Configuration policy |
| `ussd.menu_definitions` | Stable menu identities | Unique institution + menu code | Yes | Life + evidence |
| `ussd.menu_versions` | Immutable menu/version/publication metadata | Unique menu + version | Yes | Evidence policy |
| `ussd.menu_nodes` | Versioned graph nodes/action refs | Unique version + node key | Yes | Version lifetime |
| `ussd.session_states` | Current short-lived session state | Unique provider/session scope; concurrency version | Yes | Short approved period |
| `ussd.session_events` | Append-only transition/action evidence | Unique session + request/sequence | Yes | Channel evidence policy |
| `ussd.customer_channel_links` | Trusted/revocable identity link projection | Unique customer + channel account + active scope | Yes | Identity/privacy policy |
| `ussd.short_code_routes` | Versioned service-code/menu route | Unique scope + effective window | Yes | Configuration policy |
| `conversation.channel_accounts` | Institution provider/channel accounts | Unique institution + channel + account ref | Yes | Life + evidence |
| `conversation.conversations` | Conversation lifecycle/retention | Unique institution + channel conversation ref | Yes | Privacy/consent policy |
| `conversation.conversation_participants` | Participant/link/access refs | Unique conversation + participant ref | Yes | Privacy policy |
| `conversation.messages` | Immutable safe content/ref and direction | Unique channel account + provider message ID | Yes | Privacy/legal-hold policy |
| `conversation.message_status_history` | Append-only message/delivery state | Unique message + event ID/status occurrence | Yes | Evidence policy |
| `conversation.bot_intents` | Versioned approved intent definitions | Unique institution + intent code + version | Yes | Configuration evidence |
| `conversation.bot_actions` | Typed owner action bindings/policies | Unique intent version + action code | Yes | Configuration evidence |
| `conversation.handoff_requests` | Handoff lifecycle and case refs | One active handoff per conversation/purpose | Yes | Case/privacy policy |
| `conversation.agent_assignments` | Append-only assignment history | Unique handoff + assignment version | Yes | Audit policy |
| `conversation.consent_records` | Conversation-specific consent evidence | Unique subject/purpose/version | Yes | Compliance policy |
| `channel.channel_profiles` | Enabled channel capability/version | Unique institution + channel + environment + version | Yes | Configuration policy |
| `channel.channel_policies` | Versioned identity/action/localisation policy | Unique profile + policy type + version | Yes | Evidence policy |
| `channel.channel_limits` | Channel guardrail refs, not financial authority | Unique profile + limit type/version | Yes | Policy evidence |
| `channel.registered_endpoints` | Opaque Integration endpoint refs | Unique profile + endpoint purpose | Yes | Configuration policy |
| `channel.customer_preferences` | Effective preference/consent projection | Unique customer + event class + version | Yes | Privacy policy |
| `cms.content_items` | Stable content/category identity | Unique institution + content code | Yes | Life + evidence |
| `cms.content_versions` | Immutable content metadata/digest/state | Unique item + version | Yes | Content evidence policy |
| `cms.content_localisations` | Sanitised locale bodies/fields | Unique version + locale | Yes | Version lifetime |
| `cms.content_categories` | Versioned category/classification rules | Unique institution + category code/version | Yes | Policy evidence |
| `cms.content_approvals` | Append-only decision/publication evidence | Unique version + decision occurrence | Yes | Audit policy |
| `cms.publication_schedules` | Effective channel/locale windows | No overlapping publication per scope | Yes | Evidence policy |
| `cms.knowledge_articles` | Structured knowledge metadata | Unique content/version/article ref | Yes | Content policy |
| `cms.faq_entries` | Structured FAQ entries | Unique content version + question key | Yes | Content policy |
| `cms.ussd_content_bindings` | Menu node/content version references | Unique menu version + node + purpose | Yes | Version lifetime |
| `cms.chatbot_content_bindings` | Intent/content classification refs | Unique intent/version + content/purpose | Yes | Version lifetime |
| `notification.templates` | Stable event/channel template identity | Unique institution + template code | Yes | Life + evidence |
| `notification.template_versions` | Immutable schema/localisation/publication | Unique template + version | Yes | Evidence policy |
| `notification.notification_requests` | One communication intent/idempotency | Unique source/purpose/recipient occurrence | Yes | Communication policy |
| `notification.delivery_attempts` | Route/provider-independent attempt projection | Unique request + attempt ordinal/channel | Yes | Evidence policy |
| `notification.delivery_status_history` | Append-only canonical status | Unique provider event + attempt | Yes | Evidence policy |
| `notification.suppression_rules` | Versioned policy/customer suppression | Unique scope + type + effective version | Yes | Compliance policy |
| `notification.quiet_hours` | Timezone-aware effective quiet hours | Unique subject/scope/version | Yes | Preference policy |
| `campaign.campaigns` | Disabled-by-default campaign definitions | Unique institution + campaign code | Yes | Marketing policy |
| `campaign.audiences` | Approved audience provenance/query digest | Unique campaign + audience version | Yes | Consent/evidence policy |
| `campaign.campaign_approvals` | Approval lineage | Unique campaign version + decision | Yes | Audit policy |
| `campaign.campaign_schedules` | Effective schedule/expiry | Unique campaign + occurrence | Yes | Evidence policy |
| `campaign.delivery_results` | Notification refs/results | Unique campaign occurrence + subject ref | Yes | Marketing policy |
| `integration.idempotency_records` | Command/request deduplication | Unique scope + key; request digest | Yes | Replay-window policy |
| `integration.outbox_messages` | Durable event publication | Unique event ID | Yes | Operational policy |
| `integration.inbox_messages` | Consumer deduplication | Unique producer + event ID + consumer | Yes | Replay-window policy |
| `integration.webhook_receipts` | Canonical ingress receipt refs only | Unique Integration receipt/event + consumer | Yes | Evidence policy |

### Indexes and constraints

- Tenant-owned indexes begin with `institution_id`; cross-tenant lookups are impossible through repository methods and tested negatively.
- Partial uniqueness prevents overlapping published menu/content/template/policy versions for the same scope/effective instant.
- State checks and optimistic concurrency guard sessions, versions, handoffs, requests and attempts.
- Unique provider/channel message IDs prevent duplicate conversation/USSD ingress effects.
- Notification request uniqueness includes source event/business ref, purpose, recipient ref and applicable occurrence.
- Approval constraints and application rules prevent author self-approval/publication where required.
- Message/content fields have classification, size and sanitisation flags; protected secret values have no persistence column.
- Session/message/status/event histories are append-only.
- Retention/legal-hold state prevents unauthorised purge and records anonymisation evidence.
- All time-window queries use explicit timezone/context and indexed effective/expiry timestamps.

### Migration and backfill

- Create module schemas/tables/constraints before enabling ingress or consumers.
- Seed only synthetic channel/menu/content/template reference data; no real contacts, conversations, OTPs or credentials.
- Import existing content/templates only through versioned draft, sanitisation, review and publication workflows.
- Released migrations are append-only; corrections use forward migrations with privacy/retention/replay verification.
- Backfills are bounded/idempotent and cannot send messages, publish content or execute channel commands as a side effect.
- Verification proves no overlapping publications, duplicate message/event identities, persisted OTP-shaped data or cross-tenant references.

## 17. Consistency, concurrency and idempotency

- Each aggregate commits state/status history plus outbox atomically in its local transaction.
- Inbound canonical USSD/message/delivery events are inbox-deduplicated before state transition.
- Session steps use expected sequence/concurrency version; duplicate identical steps return the stored response, while conflicting payloads are rejected/audited.
- Owner-service command keys are generated once from the confirmed channel action and remain stable across timeout/redelivery/restart.
- Notification request creation deduplicates on source fact/purpose/recipient occurrence before rendering or transport.
- Provider delivery submission uses a stable Integration idempotency key. An uncertain provider result blocks unsafe failover/retry.
- CMS/menu/template publication uses compare-and-swap against current version and non-overlapping effective-window constraints.
- Preference/consent changes use optimistic concurrency and immutable provenance/history.
- Conversation message order is scoped to a conversation/channel account; no global ordering is assumed.
- Outbox/inbox retries can duplicate delivery but not business effects. Consumers are explicitly idempotent.
- No distributed transaction is required. Cross-service inconsistency remains visible through references, retries, status enquiry and audit/reconciliation.

## 18. Security and privacy

Threats include account takeover through SIM/chat identity, session fixation, replay, menu/action tampering, command substitution, prompt injection, unsafe model/tool use, content/script injection, malicious links, template injection, recipient substitution, OTP leakage, enumeration, cross-tenant access, transcript over-retention, agent overreach, consent bypass and duplicate financial commands.

Controls:

- Trusted institution/customer/actor context never comes solely from request content, phone number or provider display name.
- Apply short session expiry, replay/sequence checks, rate controls and step-up for protected actions.
- Bind confirmation to all material command fields; any change invalidates it.
- Use typed allow-listed actions and explicit field mapping. Prevent mass assignment of customer/account, status, permission, approval, route or financial outcome.
- Validate/sanitise CMS markup, links, localisation and placeholders; apply contextual output encoding.
- Treat all inbound chat/USSD/customer text and retrieved documents as untrusted data, never instructions to the system.
- Models/classifiers cannot access secrets or unrestricted APIs; privileged execution remains deterministic and policy-gated.
- Resolve recipients from verified references; do not let an event/caller silently substitute an arbitrary phone/email for account alerts.
- Encrypt protected message/transcript/content data at rest; minimise events and telemetry; mask contact/account/identity values.
- Never store/log OTPs, PINs, passwords, tokens, full account/card numbers, complete identity numbers or provider credentials.
- Enforce agent assignment, customer scope, purpose and sensitive-access audit for conversation viewing.
- Enforce consent, suppression, quiet hours, legal hold, retention, deletion/anonymisation and data-subject policies.
- Non-production uses synthetic contacts/content/conversations and certified sandbox provider accounts.

Negative tests cover cross-tenant IDs, revoked links, SIM/chat impersonation, stale step-up, session replay/fixation, changed confirmation fields, intent/prompt/tool injection, arbitrary API/action selection, unsafe HTML/link/template placeholders, recipient substitution, OTP logging/storage, enumeration, consent bypass and transcript access by an unassigned agent.

## 19. Audit and observability

Audit channel/profile/menu/intent/policy configuration, link/revocation, protected action confirmation/submission, CMS/template authoring/approval/publication/withdrawal, preference/consent change, handoff/assignment/access, notification route/suppression/replay and sensitive conversation/content access.

Evidence includes actor/service, permission/audience, institution/channel, customer/session/conversation/request refs, menu/content/template/intent/policy versions, action/owner command/business/provider refs, step-up proof reference, old/new state, reason, approval, correlation/causation and timestamp. Do not include OTPs, full message bodies, contact values, secrets or unnecessary PII.

Minimum metrics:

- USSD sessions/steps by state, menu version, safe failure, expiry and action type.
- Duplicate/out-of-order/replayed session requests and owner-command idempotent replays.
- Conversations/messages by channel/intent/outcome/handoff/age and unknown/low-confidence rate.
- Handoff queue/assignment/acceptance/resolution age and access-denial count.
- CMS/menu/template versions by state, locale coverage, publication/withdrawal and cache/index lag.
- Notification requests by class/channel/status/suppression/quiet-hour/expiry and safe failure reason.
- Rendering/schema/locale failures and mandatory-notice policy decisions.
- Delivery attempts by route/status/latency, uncertain age, bounce/complaint and dead-letter/replay.
- Preference/consent version conflicts and suppressed marketing attempts.
- Inbox/outbox lag, poison messages and restore/recovery checkpoints.

Traces span provider ingress through Integration Hub, Channels, Identity/owner service, response delivery and audit. Logs carry stable masked references and safe classifications only. Alerts/runbooks cover ingress attack/replay, session error surge, chatbot intent anomalies, CMS publication failure, notification backlog, provider uncertainty, OTP-redaction failure, dead letters and consent-policy failure.

## 20. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Integration ingress unavailable | Provider retries per certified semantics; no local state guessed | Deduplicate canonical redelivery |
| USSD session lost/expired | TimedOut; submitted owner command remains separately traceable | Query original owner command; never resubmit blindly |
| Identity/entitlement unavailable | Protected action denied/pending safely | Retry authentication/query; no cached privilege elevation |
| Owner query unavailable | Session/conversation shows safe temporary/pending message | Retry bounded query if safe |
| Owner command times out | `CommandSubmitted`/`OutcomePending` with original key | Query status; no new command key |
| Unknown chatbot intent | No privileged action | Clarify, safe fallback or handoff |
| CMS search/index unavailable | No unapproved/stale answer | Read authoritative published projection or safe fallback |
| Publication outbox/cache failure | Database publication authoritative; consumers may remain stale and detect version | Retry event/cache invalidation; withdraw if unsafe |
| Template/render failure | Notification rejected/dead-lettered before provider | Correct version/data; authorised replay if still relevant |
| Provider pre-submit failure | Attempt failed before submission | Apply approved channel/route failover |
| Provider submission uncertain | Notification remains Uncertain | Integration status/receipt resolution; block unsafe duplicate failover |
| Delivery event duplicated/out of order | Inbox/status transition rejects duplicate/regression | Preserve conflicting evidence; investigate |
| Consumer/outbox failure | Local state authoritative | Idempotent retry/DLQ |
| Process restart | Durable session/action/request/status survives | Resume workers with same keys/versions |
| Restore/PITR | Ingress/send workers paused until consistency check | Reconcile events, requests, attempts and external state before resume |

Manual recovery uses typed authorised commands. Operators cannot edit a session command ref, message identity, publication status, notification source fact or delivery result directly. Restore verifies idempotency, publication windows, link/revocation state, submitted actions, notification requests, uncertain deliveries, consent versions and outbox/inbox continuity.

## 21. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Enabled channels/journeys | Product/Channel/Security | No | Owner readiness, provider certification, risk review | Versioned/effective-dated |
| Channel identity/link/step-up policy | Identity/Security/Channel | No | Per intent/action/risk/channel | Versioned; deny on stale |
| USSD service code/menu/timeout/input limits | Channel/Product | No | Gateway constraints, graph, security | Versioned; sessions pin version |
| Chatbot intent/action/confidence policy | Channel/Domain owner/Risk | No | Typed schema, permission, confirmation | Versioned/approved |
| CMS category/classification/locales/fallback | Content/Compliance | No | Channel/access/sanitisation rules | Versioned/effective-dated |
| Notification template/placeholder schema | Communication/Domain owner | No | Event class/data classification/channel output | Versioned/effective-dated |
| Preference/consent/mandatory notice policy | Compliance/Product | No | Purpose/country/customer/channel | Versioned/effective-dated |
| Quiet hours/timezone/expiry | Product/Compliance | No | Event class and mandatory exceptions | Versioned |
| Notification route/failover policy | Communication/Operations | No | Channel/country/cost/health/uncertainty | Versioned/approved |
| Provider transport/capability refs | Integration Hub | Credential refs only outside service | Certification/readiness/freshness | Versioned projection |
| Message/content retention/legal hold | Privacy/Compliance | No | Record class/country/institution | Versioned; controlled jobs |
| Campaign enablement/audience rules | Marketing/Compliance | No | Consent, approval and Phase scope | Disabled until approved |

Missing/stale critical identity, publication, consent, route or owner policy disables the affected action. Feature flags cannot bypass permission, step-up, confirmation, idempotency, approval, consent, audit or provider verification.

## 22. Implementation slices

1. Approve Phase 1 channel journeys, identity/link/step-up, retention, CMS/localisation and notification classification/finality decisions.
2. Create C# module boundaries, architecture tests and shared canonical channel contracts without live providers.
3. Implement channel profiles/policies, permissions, Workflow publication and tenant-isolated migrations.
4. Implement CMS content/version/localisation/approval/publication plus safe public/authorised projections.
5. Implement notification template/version/placeholder/preview/publication with OTP-secret-path tests.
6. Implement notification request/idempotency, trusted recipient resolution, preference/consent/quiet-hours/suppression and confirmed-event consumers.
7. Implement Integration Hub delivery contract, route decisions, delivery status, uncertainty and safe failover using synthetic transports.
8. Implement USSD menu graph/version/publication/session state with synthetic canonical ingress.
9. Implement one approved read-only USSD journey end to end before any financial command.
10. Implement controlled USSD command confirmation/idempotency/status recovery for each separately approved owner journey.
11. Implement conversation/message/participant/link state and WhatsApp/website canonical ingress.
12. Implement public CMS knowledge intent, deterministic controlled intents and explicit safe fallback.
13. Implement authenticated conversational action/step-up pattern one owner action at a time.
14. Implement human handoff/assignment/service-request integration and access controls.
15. Implement campaign persistence only and keep production execution disabled until approved.
16. Add audit, dashboards, alerts, provider/session/chatbot/publication/delivery runbooks and privacy jobs.
17. Complete security, accessibility/localisation contract, concurrency, resilience, performance and restore evidence.

Each slice starts with traceable failing tests and keeps unapproved channels/intents/actions/providers disabled. Do not implement all financial journeys as a generic action after proving one.

## 23. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| CHN-001 | Architecture/user scope | Architecture | Inspect module/project dependencies | USSD/Conversation/CMS/Notification boundaries enforced |
| CHN-002 | Security | Security | Cross-tenant channel/menu/session/content/message ID | Denied without existence disclosure |
| CHN-003 | Security | Security | Phone/WhatsApp identity without customer link | Protected action denied |
| CHN-004 | Security | Security | Revoked link or stale entitlement | Protected action denied and audited |
| CHN-005 | USSD scope | Domain | Publish invalid/unreachable/looping menu graph | Rejected before publication |
| CHN-006 | USSD scope | Concurrency | Duplicate USSD step concurrently | One transition and stored response |
| CHN-007 | USSD scope | Security | Out-of-order/replayed/tampered session step | Rejected; state unchanged |
| CHN-008 | USSD scope | Integration | Session pins old menu during new publication | Existing flow stable; new session gets new version |
| CHN-009 | USSD scope | Security | Change amount/account after confirmation | Confirmation invalidated; no owner command |
| CHN-010 | USSD scope / financial invariants | Resilience | Session times out after command submission | Same command key/status query; no second debit |
| CHN-011 | Chatbot scope | Security | Prompt requests arbitrary privileged API/tool | No execution; safe response/audit |
| CHN-012 | Chatbot scope | Domain | Unknown/low-confidence intent | Clarification/handoff; no guessed action |
| CHN-013 | Chatbot scope | Integration | Approved knowledge query | Published access-appropriate CMS version cited |
| CHN-014 | Chatbot scope | Security | Restricted article requested anonymously | Not disclosed |
| CHN-015 | Chatbot scope | Integration | Authenticated controlled action | Step-up/confirmation/schema/owner authorisation enforced |
| CHN-016 | Handoff scope | Security | Unassigned agent reads conversation | Denied and audited |
| CHN-017 | Handoff scope | Concurrency | Two agents accept same handoff | One assignment wins; history retained |
| CHN-018 | CMS scope | Workflow | Author approves/publishes own restricted content | Denied by separation policy |
| CHN-019 | CMS scope | Security | Script/unsafe link/template injection in content | Rejected/sanitised per policy |
| CHN-020 | CMS scope | Concurrency | Overlapping publication windows | Constraint/conflict; one effective version |
| CHN-021 | CMS scope / US-076 | Domain | Requested locale unavailable | Explicit approved fallback or safe unavailable result |
| CHN-022 | CMS scope | Messaging | Replay publication event | Caches/indexes converge without duplicate side effect |
| CHN-023 | COM-001 / US-034/035 | Contract | SMS/email route catalogue changes | Provider-independent configuration; no redeploy of domain code |
| CHN-024 | COM-002 | Architecture | Scan repository/database/contracts | No provider credentials/SDKs/raw protocols in service |
| CHN-025 | COM-003 / US-036 | Domain | Choose channel by event/institution/country/preference/consent | Exact recorded policy/version outcome |
| CHN-026 | COM-004 / US-034/037 | Resilience | Primary transport proven pre-submit failure | Approved secondary route uses same notification identity |
| CHN-027 | COM-004 / COM-009 | Resilience | Primary provider outcome uncertain | No blind failover/duplicate message |
| CHN-028 | COM-005 | Integration | Accepted/sent/delivered/failed/bounced/suppressed events | Append-only legal transitions and visibility |
| CHN-029 | COM-005 | Concurrency | Duplicate/out-of-order receipt | No status regression or duplicate owner event |
| CHN-030 | COM-006 / US-036 | Domain | Template version/localisation/effective date | Exact approved version rendered |
| CHN-031 | COM-006 | Security | Unknown/extra/protected placeholder | Rendering rejected without leakage |
| CHN-032 | COM-007 | Security | OTP through render/send/error/DLQ/log paths | Value absent from persistence/telemetry/retrieval |
| CHN-033 | COM-007 | Security | Operator replays expired OTP delivery | Denied; new Identity flow required |
| CHN-034 | COM-008 / US-036 | Domain | Marketing during quiet hours/no consent | Suppressed/deferred per policy |
| CHN-035 | COM-008 / US-036 | Domain | Mandatory security notice with opt-out | Approved mandatory exception applied and evidenced |
| CHN-036 | COM-009 / US-037 | Idempotency | Replay confirmed business event | One notification request/customer purpose |
| CHN-037 | COM-009 | Recovery | Worker restarts after provider submission | Same delivery key/status resolution; no known duplicate |
| CHN-038 | COM-010 | Security | Operator searches/replays delivery | Scoped/masked view; reason/approval/audit required |
| CHN-039 | US-034 | Contract | SMS priority/cost/country/type/health routing | Versioned deterministic route decision |
| CHN-040 | US-035 | Integration | SMTP/API bounce/complaint/failure | Canonical statuses recorded without raw provider leakage |
| CHN-041 | US-036 | Concurrency | Two preference updates with stale version | Conflict; no lost consent update |
| CHN-042 | US-037 | Integration | Confirmed transaction event | Accurate message purpose/ref; no claimed outcome beyond source |
| CHN-043 | US-037 | Security | Pending/uncertain payment event arrives | Template/classification cannot claim success |
| CHN-044 | US-075 | Contract | Channel calls business API | Gateway/BFF/versioned contract; no database access |
| CHN-045 | US-075 | Security | Sensitive action lacks strong auth/device proof | Denied/step-up required |
| CHN-046 | US-075 | Domain | Pending/error response | Does not misrepresent transaction success |
| CHN-047 | US-076 | Contract | Locale/currency/date/timezone context | Approved context used consistently |
| CHN-048 | Privacy | Security | Retention/anonymisation/legal hold job | Policy enforced; evidence retained; held record preserved |
| CHN-049 | Messaging | Replay | Replay inbox/outbox/delivery events | No duplicate command, message or publication |
| CHN-050 | Recovery | Recovery | Restore during active sessions/publication/delivery | Safe expiry/reconciliation; no duplicate action/message |
| CHN-051 | End-to-end | End-to-end | Verified USSD read journey | Correlated identity/menu/owner/response/audit evidence |
| CHN-052 | End-to-end | End-to-end | Confirmed event to SMS/email failover | One intent, safe route/delivery history and audit |
| CHN-053 | End-to-end | End-to-end | WhatsApp knowledge then handoff | Verified ingress, CMS version, assignment and response refs |

Contract tests use synthetic channel identities, contacts, conversations, content, provider events and business facts. Fault injection covers every point before and after session/action/request commit, provider submission, delivery receipt, publication event and consumer acknowledgement.

## 24. Acceptance criteria

- [ ] COM-001 through COM-010 and US-034 through US-037 trace to decisions, contracts, code and tests.
- [ ] USSD, WhatsApp, website chatbot, CMS, Notifications and Human Handoff are explicit C# module boundaries within Channels and Engagement.
- [ ] US-075/076 backend allocations support controlled/versioned APIs, strong authentication evidence, honest pending/error states and locale/timezone context.
- [ ] Roles, capability permissions, institution/channel/customer scopes, step-up and segregation of duties are enforced server-side.
- [ ] Channel identity linking never treats a phone/chat account as sufficient banking authentication.
- [ ] Sensitive/financial channel actions are allow-listed, confirmed, idempotent and re-authorised by the owning service.
- [ ] Chatbot generated/free-form text cannot invoke unrestricted privileged APIs.
- [ ] CMS/menu/template content is immutable by version, localised, sanitised, approved, effective-dated and auditable.
- [ ] Notification source finality, preferences, consent, quiet hours, suppression and mandatory exceptions are versioned and evidenced.
- [ ] OTPs/secrets/protected identifiers are absent from persistence, logs, traces, events, errors, previews and replay payloads.
- [ ] Provider transport uses Integration Hub contracts and uncertain submission blocks unsafe duplicate failover.
- [ ] Delivery status remains separate from business/financial status.
- [ ] Unapproved Phase 1 journeys, production campaigns and Phase 3 enhancements cannot be enabled accidentally.
- [ ] Restore, replay, privacy, provider outage and session-timeout evidence proves no duplicate financial command or known duplicate notification.

## 25. Definition of Done

- Build, analyzers, formatting, architecture tests and affected suites pass with the repository-pinned .NET SDK.
- EF Core migrations apply to empty/supported upgrade databases; tenant, version, publication, message/session identity, idempotency and status constraints pass PostgreSQL/Testcontainers tests.
- OpenAPI/internal/event contracts pass compatibility, privacy, accessibility/localisation and security review.
- Owner-service contracts prove Channels cannot bypass Identity, permissions, limits, confirmation, approval or financial invariants.
- Synthetic Integration Hub contract tests cover success, replay, pre-submit failure, uncertainty, late receipt, bounce/complaint and recovery.
- Each enabled USSD/chatbot journey has approved identity, intent/action, step-up, masking, error/pending and runbook evidence.
- CMS/template publication, cache/index invalidation, withdrawal and restore are demonstrated.
- Consent/quiet-hour/suppression/mandatory-notice and OTP non-persistence tests pass.
- Dashboards/alerts/runbooks cover channel ingress, sessions, intents, handoff, publication, notification backlog, provider uncertainty and privacy jobs.
- Traceability includes source/architecture scope, permissions, versions, APIs/events, migrations, tests, provider certification, privacy and operational evidence.

## 26. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Production Phase 1 USSD journeys | Scope/security/certification | Product/Channel/Risk | Slice 1 | Open |
| Production WhatsApp/website-chat journeys | Scope/security/privacy | Product/Channel/Security | Slice 1 | Open |
| Channel identity linking and revocation proof | Account takeover risk | Identity/Security/Channel | Slice 1 | Open |
| Per-intent step-up/confirmation/permission policy | Sensitive action safety | Risk/Domain owners | Before protected action |
| USSD provider constraints, session timeout and encoding/size limits | Menu/session correctness | Integration/Channel owner | Slice 8 | Open |
| Chatbot classifier/model use and confidence/fallback policy | Unsafe action/answer risk | Product/Security/AI governance | Slice 12 | Open |
| CMS content types, categories and access classifications | Publication governance | Content/Compliance | Slice 4 | Open |
| Supported locales and fallback rules | US-076/customer experience | Product/Institution | Slice 4 | Open |
| Author/approver/publisher separation matrix | Segregation of duties | Compliance/Content | Slice 4 | Open |
| Notification event finality/classification catalogue | Incorrect customer claims | Domain owners/Communication | Slice 5/6 | Open |
| Trusted recipient resolution and contact-change handling | Misdelivery/privacy | Customer/Security/Channel | Slice 6 | Open |
| Consent, mandatory notices, suppression and quiet-hour policy | Compliance/delivery | Compliance/Legal/Product | Slice 6 | Open |
| SMS/email/push/in-app Phase 1 provider/channel catalogue | Delivery scope | Product/Integration | Slice 7 | Open |
| Provider retry/failover/uncertainty semantics | Duplicate messages | Integration/Communication | Slice 7 | Open |
| OTP protected-delivery mechanism | Authentication-secret safety | Identity/Security/Channel | Slice 5 | Open |
| Conversation/message/content/notification retention and legal hold | Privacy/evidence | Compliance/Privacy | Before production |
| Human-handoff queues, SLAs and agent access | Support operations | Operations/Product | Slice 14 | Open |
| Production campaign scope and approval | Marketing/consent risk | Marketing/Compliance | Deferred/approval required | Open |
| Capacity, latency, availability, RTO/RPO | Release gates | Product/Platform | Before release testing | Open |

## 27. Prohibited assumptions

- Do not invent production USSD, WhatsApp, chatbot or CMS journeys because the foundations exist.
- Do not treat a phone number, MSISDN, WhatsApp account, device/session or provider signature as sufficient customer authentication.
- Do not let USSD/chatbot/channel code write Ledger/payment/account tables or infer authoritative balance/payment status.
- Do not create a generic natural-language-to-API executor or allow generated output to invoke privileged commands.
- Do not bypass the owning service's permission, ownership, limit, approval, restriction, pricing or idempotency validation.
- Do not create a new financial-command key after channel/session/provider timeout.
- Do not expose provider SDKs, credentials, webhook verification or raw provider DTOs in Channels and Engagement.
- Do not trust caller-supplied recipient addresses, template IDs, permissions, provider routes or business outcomes.
- Do not send a success notification from a pending, uncertain, callback-only or unverified business state.
- Do not treat provider-accepted/sent/delivered notification state as proof of financial success.
- Do not persist/log OTPs, PINs, passwords, tokens, full identifiers or unrestricted transcripts/message bodies.
- Do not permit author self-approval/publication where separation is required or edit a published version in place.
- Do not use a CMS/search cache/index as publication authority.
- Do not silently fall back to an unapproved locale, content version, template or channel.
- Do not bypass consent/quiet hours/suppression except for exact approved mandatory classifications.
- Do not fail over an uncertain provider submission unless verified safe.
- Do not enable production campaigns or Phase 3 channel features as incidental Phase 1 scope.

Until identity, journey, content, notification and provider decisions are approved, use synthetic ingress/providers and keep the feature disabled.

## 28. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, Epics 01-08, this epic, COM requirements, US-034..US-037/US-075/US-076 and relevant owner-service contracts.
- Expected C# boundaries: `CoreBanking.ChannelsEngagement.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts`, `.Workers` and corresponding test projects.
- Within Domain/Application, preserve explicit `Ussd`, `Conversations`, `ContentManagement`, `Notifications`, `Campaigns`, `WhatsApp`, `Chatbot` and `HumanHandoff` module namespaces/folders.
- Do not create extra deployable microservices for modules without an approved boundary/operational ADR.
- Domain code must not reference ASP.NET Core, EF Core, provider SDKs, broker clients, model-provider SDKs or another service implementation.
- Keep provider protocols/credentials/signatures in Integration Hub and business decisions in owning services; Channels stores stable refs/status projections only.
- Use strongly typed IDs, explicit state machines, `DateTimeOffset`, `TimeProvider`, cancellation tokens, optimistic concurrency and append-only histories.
- Use typed intent/action/template/placeholder contracts, explicit field maps and allow-lists; reject dictionary/object-based privileged execution.
- Generate stable idempotency keys before owner command/provider submission and preserve them across retries/restart.
- Add architecture tests preventing provider SDK/DTO leakage, cross-module persistence access, direct Ledger/payment infrastructure references and unrestricted chatbot tools.
- Add PostgreSQL/Testcontainers tests for tenant isolation, publication overlap, session/message deduplication, consent concurrency and notification idempotency.
- Test logging, tracing, errors, snapshots, dead letters and audit for OTP/PII/secret leakage.
- Begin with synthetic canonical Integration Hub ingress/delivery; enable one reviewed journey at a time.
- Stop when work requires an unapproved identity/link proof, channel journey, content/access classification, notification finality, mandatory/consent rule, provider retry semantic, retention rule or Phase 3 scope.
- Completion report must list source/story allocation, enabled journeys/providers/locales, permissions, intent/action/template/content versions, APIs/events, migrations, tests/commands, security/privacy/accessibility, resilience/replay/restore evidence, assumptions, risks and rollout/rollback notes.

## 29. Traceability summary

| Source | Primary implementation evidence |
| --- | --- |
| COM-001 / US-034/035 | Provider-independent SMS/email/push/in-app routes through Integration Hub |
| COM-002 | Versioned provider-capability refs; credentials/timeouts/retries remain governed by Integration Hub |
| COM-003 / US-036 | Event/institution/country/preference/consent channel-selection policy |
| COM-004 / US-034/037 | Safe configured failover with uncertainty and duplicate controls |
| COM-005 / US-035/037 | Provider-independent accepted/sent/delivered/failed/bounced/suppressed histories |
| COM-006 / US-036 | Versioned, approved, effective-dated, localised templates and placeholder schemas |
| COM-007 | Dedicated non-persistent OTP delivery path and leakage tests |
| COM-008 / US-036 | Quiet hours, marketing consent, suppression and mandatory-security exceptions |
| COM-009 / US-037 | Source-event/request/provider idempotency and restart-safe delivery |
| COM-010 | Scoped performance/failure/dead-letter views and approved replay |
| US-075 | Controlled versioned backend APIs, strong-auth evidence and honest pending/error state |
| US-076 | Locale/currency/timezone context and versioned channel schemas; UI accessibility remains shared frontend scope |
| User-directed architecture scope | First-class USSD, WhatsApp, website chatbot, CMS and human-handoff module foundations |

Supporting stories such as US-025, US-026, US-032, US-033 and US-049 may be initiated or surfaced through an approved channel journey, but their owning services retain the business outcome and acceptance authority.

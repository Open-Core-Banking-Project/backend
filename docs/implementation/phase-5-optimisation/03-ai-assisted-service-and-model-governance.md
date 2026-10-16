---
phase: "Phase 5 - Optimisation"
epic: "AI-Assisted Service and Model Governance"
owning_service: "Channels and Engagement for assistance; Risk and Reporting for model governance"
modules: [AI Assistance, Knowledge Retrieval, Safety Policy, Human Handoff, Model Registry, Evaluation]
status: Draft
requirements: [ARC-001..ARC-006, RSK-004..RSK-007, RPT-004..RPT-007, DAT-001..DAT-008, SEC-001..SEC-018, AUD-001..AUD-004]
stories: []
dependencies: [Epic 01 Data Governance, Approved CMS Knowledge, Channel Security, Human Handoff, Model Risk Policy]
adrs: [AI use-case boundary, Model lifecycle and validation, AI provider and data residency, Tool and knowledge access]
last_updated: 2026-10-16
---

# Epic 03: AI-Assisted Service and Model Governance

## 1. Objective

Introduce narrowly approved AI assistance for service users while establishing a governed model lifecycle that makes purpose, data, evaluation, limitations, approval, deployment, monitoring, human oversight and rollback explicit.

## 2. Success measures

- Every enabled use case/model is registered, risk-tiered, independently validated, approved and monitored.
- Assistance uses approved versioned knowledge, provides evidence references where required and hands off when policy/confidence demands.
- Generated output cannot directly execute privileged or financial commands or alter customer/model/governance state.
- Safety, privacy, security, fairness, quality and operational guardrails meet approved thresholds on representative synthetic/authorised evaluations.
- Unsafe output, drift or provider failure triggers deterministic fallback/suspension and auditable response.

## 3. Scope

Included: approved FAQ/knowledge assistance, response drafting/summarisation for authorised staff if approved, disclosure, consent, retrieval, citations, redaction, safety policy, feedback, human handoff, model/use-case registry, validation, deployment approval, monitoring and rollback. Excluded: autonomous financial advice, transaction execution, KYC/AML/credit approval, unrestricted tool use, model training on unapproved production data, silent profiling and self-modifying models.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Approval |
| --- | --- | --- | --- |
| Customer/service user | `assistant.interact` | Approved channel/use case | Consent/step-up by content/action |
| Service agent | `assistant.draft.use` | Assigned conversation/case | Human owns final response |
| Knowledge curator | `assistant.knowledge.manage` | Approved corpus | CMS publication control |
| Model owner/validator | `model.register` / `model.validate` | Use case/model | Independent validation |
| Model approver/operator | `model.deploy` / `model.suspend` | Environment/version | Maker-checker |

Model developers cannot solely validate and approve their own version. Service identities receive only retrieval/inference permissions required by the approved use case.

## 5. Domain model

| Concept | Responsibility | Important rules |
| --- | --- | --- |
| `AiUseCase` | Purpose, audience, allowed inputs/outputs/actions | Owner, risk tier and expiry required |
| `ModelVersion` | Provider/artifact/configuration identity | Immutable approved version/digest |
| `Dataset/FeatureVersion` | Governed evaluation/training input | Purpose/classification/lineage required |
| `EvaluationSuite/Result` | Quality and guardrail evidence | Thresholds fixed before approval |
| `Deployment` | Environment, routing and policy | Approved version only; rollback target |
| `AssistanceInteraction` | Input classification, retrieval, output and decision | Minimise/redact; retention by policy |
| `SafetyDecision/Handoff` | Allow/refuse/rewrite/escalate | Reason/policy version retained |

Invariants: models cannot grant authority; output is untrusted until owning application validates it; prompts/tools/knowledge are allow-listed and versioned; tenant/customer context never crosses scope; high-impact decisions remain with approved human/service rules; raw sensitive input/output is not logged by default.

## 6. Use cases

### UC-01 - Register and approve a model use case

Document purpose, data, risk tier, limitations, owners, evaluation/guardrails, oversight, fallback and expiry; independently validate and approve exact digest.

### UC-02 - Produce a grounded assistance response

Authenticate context, classify/minimise input, retrieve authorised published knowledge, generate, apply safety policy, attach evidence/disclosure and return or hand off.

### UC-03 - Draft for a human agent

Create an explicitly labelled draft from authorised case/conversation data; the agent reviews/edits/sends and owns the final decision.

### UC-04 - Monitor, suspend and rollback

Compare quality/safety/privacy/latency/cost/outcome metrics to thresholds; alert, stop routing or restore an approved version with evidence.

## 7. API contracts

| Method | Route | Permission | Idempotent |
| --- | --- | --- | --- |
| POST | `/api/v1/assistant/interactions` | `assistant.interact` | Interaction key |
| POST | `/api/v1/assistant/interactions/{id}/feedback` | `assistant.feedback.submit` | Feedback key |
| POST | `/api/v1/assistant/interactions/{id}/handoff` | `assistant.handoff.request` | Interaction/reason |
| POST | `/api/v1/model-governance/use-cases` | `model.register` | Request key |
| POST | `/api/v1/model-governance/versions/{id}/evaluations` | `model.validate` | Suite/version key |
| POST | `/api/v1/model-governance/deployments/{id}/decisions` | `model.deploy` | Decision key |
| POST | `/api/v1/model-governance/deployments/{id}/suspend` | `model.suspend` | Action key |

Assistant responses expose assisted status, evidence references, limitations/handoff and no internal prompt. Stable errors cover purpose/consent, unsafe input/output, missing grounding, model unapproved/unavailable, stale knowledge, scope, rate and human-review requirement.

## 8. Event contracts

Produce `AiInteractionCompleted/Refused/HandedOff`, `ModelVersionValidated/Approved/Deployed/Suspended/RolledBack`, `ModelGuardrailBreached`. Consume CMS knowledge publication, consent/preference, entitlement, governance classification and provider health events. Events exclude raw prompts/responses and sensitive retrieved content; order by interaction/deployment and deduplicate by stable IDs.

## 9. Database design

Channels and Engagement owns assistance interaction metadata, retrieval references/digests, safety decisions, feedback and handoffs. Risk and Reporting owns use cases, model versions/artifact references, dataset/feature references, evaluation suites/results, approvals, deployments, monitoring snapshots and incidents. Integration Hub owns provider attempts/configuration references. Encrypt classified fields, retain only approved minimum, make approvals/evidence append-only, enforce tenant/use-case/version uniqueness and no cross-service foreign keys.

## 10. Consistency, concurrency and idempotency

Interaction metadata/safety/outbox commit locally; provider inference is external and cannot share a transaction. The same interaction key returns the prior accepted outcome or explicit current status. Deployment decisions bind model/config/prompt/tool/knowledge policy digests and use optimistic versions. Provider timeout returns unavailable/uncertain and fallback; it never retries an unsafe non-idempotent tool action because tool execution is outside this epic.

## 11. Security and privacy

Threat-model prompt injection, indirect injection from knowledge, exfiltration, cross-tenant retrieval, model extraction, denial of wallet/service, poisoned content, malicious files, output injection and provider retention. Use input/output classification, redaction, allow-listed retrieval, content boundaries, egress controls, quotas, secrets isolation and provider contractual controls. Never expose system prompts, tokens or unrestricted conversation history.

## 12. Audit and observability

Audit use-case/model/data registration, evaluation, approval, deployment, routing/policy change, suspension, override, sensitive retrieval, refusal and handoff. Metrics include grounded/citation rate, refusal/handoff, safety events, human correction, quality samples, latency, errors, tokens/cost, drift and provider health. Logs use IDs/policy versions, not prompt or customer content.

## 13. Failure, retry and recovery

Missing consent, entitlement, approved knowledge or model approval fails closed or hands off. Provider failure uses approved deterministic content/handoff; do not fabricate. Unsafe or ungrounded output is withheld. Replay deduplicates interactions and never sends duplicate outbound messages. Restore verifies registry, approvals, deployments, policies, interaction evidence and open safety incidents before routing resumes.

## 14. Configuration

Version/approve use cases, channel/audience, disclosures, allowed knowledge/tools/actions, models/providers, prompt templates, retrieval/filtering, safety rules, confidence/handoff, token/rate/cost limits, retention, evaluation thresholds, rollout, fallback and suspension. Secrets stay in secret management; feature flags cannot bypass approval/safety.

## 15. Implementation slices

1. Baseline use cases, harms, provider/privacy and model-governance ADRs.
2. Model/use-case registry, evaluation evidence and approval workflow.
3. Approved-knowledge retrieval with tenant/classification enforcement.
4. Assistance orchestration, safety, disclosure and human handoff.
5. Controlled deployment, shadow/canary routing, monitoring and rollback.
6. Adversarial evaluation, recovery rehearsal and stakeholder acceptance.

## 16. Test plan

| ID | Scenario | Expected evidence |
| --- | --- | --- |
| AIG-01 | Unapproved model/use case | Deployment/inference denied |
| AIG-02 | Grounded approved question | Evidence-linked safe response |
| AIG-03 | Missing/stale knowledge | Refusal or handoff, no fabrication |
| AIG-04 | Prompt/indirect injection | No secret, data or tool access |
| AIG-05 | Cross-tenant retrieval | Non-disclosing denial |
| AIG-06 | Sensitive input/output | Minimised/redacted/withheld by policy |
| AIG-07 | Privileged/financial instruction | No execution; controlled handoff/API journey |
| AIG-08 | Provider timeout/replay | Explicit fallback; no duplicate message |
| AIG-09 | Guardrail/drift breach | Alert and suspend/rollback |
| AIG-10 | Restore | Registry/deployment/evidence integrity |

## 17. Acceptance criteria

- [ ] Detailed AI-assisted service requirements and approved use cases exist.
- [ ] Models/data/prompts/tools/knowledge are versioned, governed, evaluated and approved.
- [ ] Assistance is grounded, disclosed, privacy-preserving and human-controlled.
- [ ] Generated output cannot bypass application authorisation or execute privileged/financial action.
- [ ] Monitoring, incident response, suspension, rollback and recovery are verified.

## 18. Definition of Done

Repository DoD plus approved model/provider/privacy/security ADRs, contracts, registry, evaluation suites, adversarial and tenant tests, provider certification, human-handoff evidence, controlled rollout/rollback rehearsal, dashboards/runbooks and Product/Model Risk/Compliance/Privacy/Security approval.

## 19. Open decisions

Stories/use cases, users/channels, knowledge corpus, disclosures/consent, human review, model/provider/hosting/residency, prompt/tool boundary, evaluation datasets/metrics/thresholds, fairness/explainability, retention, cost/latency/availability, rollout and incident ownership remain open.

## 20. Prohibited assumptions

Do not train on production data by default, claim sentience/certainty, provide unapproved advice, execute model-generated commands, let model output decide access/risk/credit/compliance, expose prompts/secrets, retain raw content without policy, or deploy from benchmark quality alone.

## 21. Agent implementation instructions

Read Phase 5 overview, Epic 01, Channels/CMS/chatbot, IAM, workflow, risk and audit epics plus approved AI/model ADRs. Use C#/.NET; provider SDKs remain infrastructure adapters. Do not implement production inference until detailed requirements and approvals exist. Run build, contract, permission, tenant/privacy, adversarial, evaluation, load/cost, resilience, rollback and restore tests. Stop on unresolved use case, data purpose, human authority or provider terms.


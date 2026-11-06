---
decision: P1-E04-D17
title: Authoritative customer, branch and currency fact transport contracts
epic: P1-E04 Products and Accounts
owners:
  - Architecture
  - Product
  - Security
required_by: P1-E04-S6
status: Approved
approved_on: 2026-11-06
brief_date: 2026-11-06
related: [ADR-0015, ADR-0009, ADR-0008, P1-E04-D08, P1-E03-D13]
decisions: [P1-E04-D17]
---

# Decision brief: reference-fact transport contracts

Slice 6 is blocked because the platform cannot yet prove *which* customer, branch and currency facts an account
decision used. The eligibility boundary that exists today evaluates facts the caller supplies, so it is evidence of
a rule engine, not of an authoritative decision.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D17-A | Who is authoritative for each fact set, and may the decision path trust caller-supplied facts? | Ownership and the trust boundary between simulation and an authoritative decision |
| D17-B | How does Core Ledger obtain customer facts, and what shape is the contract? | The V1 transport contract, service authentication and the consumer wire copy |
| D17-C | What minimum fields cross the boundary, and what must never cross it? | Data minimisation and prohibited PII |
| D17-D | How are fact versions, freshness and staleness expressed? | Version handshake, validity window, caching and fail-closed behaviour |
| D17-E | Where does the currency reference come from? | Institution configuration area versus platform reference data |
| D17-F | What happens on timeout, mismatch, stale or unavailable facts? | Stable failure codes and the no-fallback rule |

## 2. Why the answer is needed now

- `P1-E04-S6` exists to build these consumers, and its recorded note says the transport contracts are not approved.
- ADR-0015 requires the final account decision to re-fetch and re-evaluate current authoritative customer, branch
  and currency facts, and to fail closed when they are stale.
- The internal Savings eligibility boundary already resolves exactly one effective published version and revalidates
  its hash, but it evaluates `command.Facts` supplied in-process. Nothing yet proves those facts are authoritative.
- Customer events cannot close the gap on their own: `CustomerCreatedV1`, `CustomerStatusChangedV1`,
  `CustomersMergedV1` and `CustomerProfileChangedV1` carry statuses and field codes only. No event carries the
  classification facts (tier, segment, risk class, enhanced-due-diligence flag) that eligibility rules read.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Savings eligibility evaluation | Implemented; resolves one effective published version, revalidates hash and typed rules, evaluates supplied facts | `SavingsEligibilityEvaluationService`, `ProductEligibilityApprovalPolicy` |
| Authoritative facts | Not implemented; `AuthoritativeProductEligibilityFacts` is constructed by the caller | `ProductEligibility.cs` |
| Customer facts for consumers | No internal read route exists. `CoreBanking.CustomerOnboarding.Api` hosts health endpoints only | `Program.cs` (customer host) |
| Customer classification | Held on the customer aggregate with an effective version; codes are approved configuration | `Customer.cs`, `CustomerClassificationView` |
| Customer age | Birth date is protected; the party exposes a masked form, a lookup token and `BirthYear` only | `ProtectedBirthDetails` |
| Customer residency | **Not stored.** Only `CountryOfBirth` exists | `ProtectedBirthDetails` |
| Branch reference | Owned by the institution module in the same service; branch id, code, region and status exist | `Branch`, `OrganisationLifecycleV1` |
| Currency reference | Only the institution base currency exists; there is no supported-currency set and no ISO 4217 table | `Institution.BaseCurrency` |
| Service-to-service authentication | Client-credentials workload tokens with audience binding exist and are proven in Core Ledger and Workflow | P1-E02-S13 |

## 4. Decision

### D17-A Ownership and trust

| Fact set | Authoritative owner | Consumer |
| --- | --- | --- |
| Customer eligibility facts: status, effective classification version, tier, segment, risk class, enhanced due diligence, age in years, customer type | Customer and Onboarding | Product and Account modules in Core Ledger |
| Branch reference: branch id, code, region, institution, status | Institution module in Core Ledger | Product and Account modules, in process |
| Currency reference: supported currency codes per institution | Institution module in Core Ledger | Product and Account modules, in process |

Rules:

1. On the authoritative path (opening, approval, activation) Core Ledger resolves every fact itself. Caller-supplied
   facts remain simulation input only and are never accepted as the authoritative basis for an account decision.
2. A fact the owning service cannot prove is unavailable. The platform does not substitute an approximation, a
   default or a last-known value.
3. Events are invalidation hints, never the authority for a decision.

### D17-B Transport

| Item | Answer |
| --- | --- |
| Customer facts | One service-authenticated read: `GET /internal/v1/customers/{customerId}/eligibility-facts?asOf=<iso8601>` |
| Authority | The institution comes from the validated token context, never from the path or body |
| Authentication | Keycloak client credentials, audience `corebanking-customer-api`, capability `service.customer.eligibility_facts_read` |
| Contract owner | `CoreBanking.CustomerOnboarding.Contracts` publishes `CustomerEligibilityFactsV1` |
| Consumer copy | Core Ledger keeps a private wire record and maps it to its domain facts, with compatibility tests for camel-case names, additive tolerance and required fields |
| Branch and currency | In-process module contracts with the same version and freshness semantics; no network hop |
| Contract discovery | No shared mutable DTO project between services; each consumer owns its mapping |

Approved shape:

```csharp
public sealed record CustomerEligibilityFactsV1(
    string InstitutionId,
    string CustomerId,
    string CustomerStatus,
    int? EffectiveClassificationVersion,
    string KycTier,
    string Segment,
    string RiskClass,
    bool RequiresEnhancedDueDiligence,
    string CustomerType,
    int AgeYears,
    string FactsVersion,
    DateTimeOffset AsOf,
    DateTimeOffset ValidUntil);
```

### D17-C Data minimisation

Included: the opaque customer identifier, status, classification version and codes, customer type, derived age in
years, and the evidence markers above.

Excluded, and refused by contract review: names, contact points, addresses, birth date or birth-date tokens,
national identifiers, documents, account numbers, balances, images, free text and any reason narrative.

Age is computed by Customer and Onboarding at `asOf` from protected birth data and returned as an integer. Birth
date never crosses the service boundary.

`CustomerType` is derived from the party kind. Phase 1 captures `Individual` only, so the value is stable but still
carried explicitly rather than assumed.

### D17-D Versioning and freshness

| Item | Answer |
| --- | --- |
| `FactsVersion` | The customer aggregate's monotonic concurrency version, formatted `c{version}`. Classification changes advance it |
| `BranchReferenceVersion` | The branch record's concurrency version plus its institution configuration version |
| `CurrencyReferenceVersion` | The effective institution configuration version that carries the supported-currency set |
| Validity window | The source sets `ValidUntil`; the approved default is `AsOf + 60 seconds`, aligned with the ADR-0009 propagation ceiling |
| Consumer behaviour | A fact older than `ValidUntil`, or a `ValidUntil` the consumer cannot honour, fails closed. The consumer may never extend the window |
| Caching | Optional and bounded by `ValidUntil`; invalidated by the customer, branch and institution-configuration events. Caching stays disabled for classification until `CustomerClassificationChanged.v1` is produced |
| Final decision | Re-fetch and re-evaluate immediately before the authoritative decision, per ADR-0015 and P1-E04-D08 |

### D17-E Currency reference

| Item | Answer |
| --- | --- |
| Supported set | New effective-dated, versioned institution configuration area `institution.currencies`, applied through the approved configuration lifecycle with maker-checker |
| Base currency | Must be a member of the supported set; removal of the base currency is refused |
| Reference version | The effective configuration version identifier |
| Eligibility use | Currency criteria compare membership in the supported set and the account's requested currency only |
| Amount arithmetic | Unchanged by this decision. Minor units and rounding remain P1-E04-D03 and must not be inferred here |

### D17-F Failure and security

| Condition | Result |
| --- | --- |
| Fact source unreachable or times out | Deny with `eligibility_facts_unavailable`; no cached fallback on the authoritative path |
| Facts older than `ValidUntil` | Deny with `eligibility_facts_stale` |
| Response version or institution does not match the request | Deny with `eligibility_facts_mismatch` |
| Source answers for a different institution | Deny with `cross_tenant_access_denied` and disclose no existence detail |
| Classification missing or not effective | Deny with `customer_classification_unavailable` |
| Branch not active or unknown | Deny with `branch_not_available` |
| Currency not in the supported set | Deny with `currency_not_supported` |

Additional controls: a bounded timeout of two seconds with no retry storm; one structured evidence record per
authoritative decision naming source versions and outcome; no PII, token or reason text in telemetry; service
tokens are audience-bound and never logged.

## 5. Constraints

- Do not invent customer fact values, KYC codes, risk classes, branch semantics or currency codes.
- Do not resolve the residency criterion from `CountryOfBirth`. The fact is not held, so a rule set that uses
  residency must be refused at publication and the gap is recorded for Customer and Onboarding.
- Do not treat a read model, cache or event payload as authority for an account decision.
- Simulation keeps its labelled synthetic facts; it never becomes an approval path.
- A missing, invalid or contradictory fact source fails closed and never falls back to a permissive default.
- This decision approves contracts and semantics only. It does not enable account opening, restrictions, liens or
  statement routes.

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| `CustomerClassificationChanged.v1` production | Customer and Onboarding | The epic lists it as consumed; it is not implemented. Caching stays off until it exists |
| Residency attribute and its privacy review | Customer and Onboarding / Privacy | Residency is not stored today; a rule set needing it stays unpublishable |
| Keycloak client and audience inventory for the new customer API client | Security / Platform | Production gate; synthetic credentials only in development |
| Capacity, latency and timeouts beyond the two-second bound | Platform | P1-E04-D16 remains open |
| Supporting evidence retention | Security / Compliance | No retention period is invented here |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the recommended positions: a service-authenticated internal customer-facts read with events as invalidation hints only, residency criteria refused until the fact is stored, a new `institution.currencies` configuration area, the trust rule that caller-supplied facts never decide an account, and fail-closed freshness with a 60-second validity ceiling | 2026-11-06 |
| Architecture | | Named confirmation required before implementation merges | |
| Security | | Named confirmation required before the internal route is enabled outside Development | |

## 8. Implementation notes (for slice 6)

1. Publish `CustomerEligibilityFactsV1` in `CoreBanking.CustomerOnboarding.Contracts`.
2. Add the internal route and service authentication to the Customer and Onboarding host, which currently serves
   health endpoints only.
3. Add the authoritative fact provider and wire copy in Core Ledger, using the existing workload-token mechanism.
4. Add in-process branch and currency reference providers with explicit versions.
5. Add the `institution.currencies` configuration area and its maker-checker lifecycle.
6. Refuse publication of rule sets that use unevaluable criteria such as residency.
7. Remove caller-supplied facts from the authoritative path; keep simulation inputs explicitly synthetic.
8. Tests: stale, unavailable, mismatched, cross-tenant, unevaluable criterion, PII absence in response and
   telemetry, and a final-approval re-fetch.
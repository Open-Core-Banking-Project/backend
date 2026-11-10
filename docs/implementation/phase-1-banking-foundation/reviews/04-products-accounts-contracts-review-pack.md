# P1-E04-S2 products and accounts contracts review pack

## Review request

Review the completed contract-foundation slice for Products and Accounts. The increment defines versioned Account, Control and Statement transport/event schemas around the existing Product contracts and proves their module and financial boundaries. It does not register account-opening, control or statement endpoints and does not resolve any open product, number, control, lien, dormancy or statement policy.

## Requirement and story coverage

- ACC-001 and ACC-002: retains the existing typed, versioned Savings product configuration and publication contracts.
- ACC-003, ACC-004 and US-016: defines account application, lifecycle, activation and event shapes without allowing clients to assign tenant, account number, approval, status, ledger reference or balances.
- ACC-003, ACC-005 and US-017: defines restriction, lien and ledger control-evaluation shapes with typed amount/currency structure, explicit effective periods and monotonic control versions, while leaving policy code values unresolved.
- ACC-005: account summaries keep ledger balance, available balance, holds, liens and blocks separate and identify the authoritative Ledger snapshot source and as-of time.
- LED-013 and US-023: statement requests hand off an opaque authoritative-Ledger query reference and carry no transaction entries, document content or download URL.
- NFR-MNT-001, SEC-009, SEC-010, SEC-011 and SEC-013: contracts are V1 schemas, transport-only, explicitly mapped and data-minimised.

## Delivered increment

- Added V1 account application, submission, activation, response, summary, status-proposal, posting-context and lifecycle event contracts.
- Added V1 restriction, lien, control-evaluation and changed-event contracts.
- Added V1 statement request, response, requested-event and document-ready handoff contracts.
- Added contract tests for mass-assignment resistance, distinct balance concepts, authoritative source metadata and statement data minimisation.
- Added an architecture-category guard proving the new contracts expose only transport types and no implementation-layer model.

## Verification performed

- Focused Products/Accounts contract tests: 5 passed.
- Complete Core Ledger unit suite: 107 passed.
- Repository architecture suite: 15 passed.
- Affected-file `dotnet format --verify-no-changes`: passed.

## Migration and compatibility impact

- No database migration.
- The change adds new V1 types only; it does not alter an existing published Product contract.
- No API route or message consumer is activated by this slice.

## Remaining gates

- Account-number format/allocation (P1-E04-D06), final eligibility policy (P1-E04-D07), ledger initialisation (P1-E04-D09), controls/liens (P1-E04-D10/D11), dormancy/closure (P1-E04-D12/D13) and statement formats/periods/delivery (P1-E04-D15) remain open.
- String code fields are intentionally policy-neutral. Supported values must come from approved configuration/decisions and server-side validation before any route is enabled.
- Account balances remain nullable/unavailable until supplied by an authoritative Ledger snapshot; Account code must never fabricate zero or calculate availability.

## Rollout and forward fix

This is a compile-time contract addition with no runtime registration. Downstream implementation should introduce one owner-validated route/consumer at a time and pin it to these V1 shapes. If review identifies an incompatible field, revise the unconsumed V1 contract before route/event activation; after publication, compatibility rules require an additive change or a new version.

## Human action

Review the contract shapes, financial boundaries and deliberately unresolved policy fields before approving P1-E04-S2.

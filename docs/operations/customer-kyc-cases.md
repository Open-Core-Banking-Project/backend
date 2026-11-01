# Customer KYC cases and canonical verifications

This note records what slice 7 of `P1-E03 Customer and Onboarding` implements, the rules it enforces and
what deliberately stays unavailable. Decision record:
[P1-E03-D06](../implementation/phase-1-banking-foundation/decisions/P1-E03-D06-kyc-checks-providers-and-canonical-outcomes.md).

## What exists now

| Area | Implementation |
| --- | --- |
| Domain | `KycCase`, `KycRequirement`, `IdentityVerification` and `VerificationAttempt` in `CoreBanking.CustomerOnboarding.Domain.Kyc` |
| Application | `IKycCaseService` with open, request, attempt, attempt-result, resolve, waive and read operations, plus canonical request/result contracts |
| Infrastructure | `KycCaseService` over `kyc.kyc_cases`, `kyc.kyc_requirements`, `kyc.identity_verifications`, `kyc.verification_attempts`, `kyc.verification_status_history` and `kyc.kyc_requirement_history` |
| Migration | `AddKycCasesAndVerifications`; the `kyc` schema grants already exist from `AddKycSchemaPrivileges` |

## Rules the code enforces

- **The snapshot decides the work.** A case opens only for a submitted application revision that has a
  requirement snapshot, and it contains exactly the snapshot's mandatory requirements. With no snapshot the
  operation is refused with `kyc_requirement_snapshot_missing`.
- **One case per application.** Enforced by a unique index; a second attempt reports `kyc_case_exists`.
- **Outcomes are canonical or absent.** A succeeded attempt must carry a canonical outcome **and** the
  provider-mapping version that produced it: `kyc_verification_outcome_required`,
  `kyc_mapping_version_required`.
- **Uncertain is not a decision.** `uncertain` leaves the requirement in progress; only a status enquiry or a
  reviewer moves it.
- **Accepted outcomes are final.** A resolved verification cannot be re-resolved or cancelled
  (`kyc_verification_closed`), and a passed or failed requirement cannot be overwritten
  (`kyc_requirement_transition_invalid`).
- **Attempts are evidence.** A retry is a new attempt row with its own identifier; attempts are never edited.
  An active verification per requirement is enforced by a partial unique index.
- **Reviewers resolve, authorities waive.** Manual review and inconclusive items are resolved by a recorded
  reviewer reason; a waiver stores the authority and reason, and there is no default waiver.
- **History is append-only.** Every requirement and verification state change appends a sequenced row with
  the actor and, for reviews and waivers, the reason.
- **Completion is derived.** A case completes only when every requirement is passed or waived.

## Deliberately unavailable

- No provider adapter, retry policy, failover or status-enquiry scheduler: those are slice 8 and depend on
  `P1-E03-D07` and `P1-E03-D08`.
- No check list or mapping content. Check codes enter as approved configuration data; unknown provider
  statuses are mapped to `inconclusive` by that data, never guessed by code.
- No customer activation. Activation still needs every requirement passed or waived plus the approved
  classification (`P1-E03-D09`).
- No internal HTTP endpoints yet: the contracts exist, and the surface waits for the service authorisation
  port used by the rest of this service.
- No document content access from KYC: `kyc.document_references` holds metadata and scan state only.

## Verification

```powershell
dotnet build CoreBanking.sln --no-restore
dotnet test src/Services/CustomerOnboarding/tests/CoreBanking.CustomerOnboarding.UnitTests --filter "Category!=Database"
powershell -File scripts/db-verify.ps1
```

`KycCaseDatabaseTests` run under `Category=Database` as the application principal, so a missing `kyc` grant
fails there rather than in review.

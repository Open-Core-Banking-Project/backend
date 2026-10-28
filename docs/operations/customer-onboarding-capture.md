# Customer onboarding capture (epic 03, slice 4)

Slice 4 of [epic 03](../implementation/phase-1-banking-foundation/03-customer-and-onboarding.md)
implements individual party and application capture with explicit field mapping, the
encryption/tokenisation port and immutable revisions. This note records what was built, the
decisions taken where the epic leaves an open question, and what deliberately stays unavailable.

## What exists now

| Area | Implementation |
| --- | --- |
| Domain | `PersonName`, `BirthDetails`, `ContactPoint`, `Address`, `Party`, `OnboardingCapture`, `OnboardingCaptureSnapshot`, `OnboardingApplication` in `CoreBanking.CustomerOnboarding.Domain` |
| Application | `CustomerOnboardingPermissions`, `IProtectedValueProtector`, capture commands/query/view and `IOnboardingApplicationService` |
| Infrastructure | `AesGcmProtectedValueProtector`, `ProtectedValueProtectionOptions`, `CapturePayloadFactory`, `OnboardingApplicationService`, records and EF configurations for the `party` and `onboarding` schemas |
| Database | `20261028053812_AddPartyAndOnboardingCapture` (tables, constraints, indexes, append-only trigger) and `20261028054500_AddPartyAndOnboardingPrivileges` (schema grants) |
| Tests | `Domain/OnboardingDomainTests`, `Protection/AesGcmProtectedValueProtectorTests`, `Persistence/OnboardingModelTests` (default run) and `Persistence/OnboardingCaptureDatabaseTests` (`Category=Database`, run by `scripts/db-verify.ps1`) |

`onboarding.applications` holds the current protected capture; `onboarding.application_revisions`
holds every capture that was ever recorded. `party.parties` is created only by a successful
submission, and it starts `pending`: onboarding never activates a customer.

## Protection decisions

The epic leaves the encryption/tokenisation design open (open decision "Field classification and
encryption/tokenisation design"). Slice 4 therefore implements the minimum that keeps the
special-case decisions reversible, and states exactly what it protects:

- **Protected (encrypted + lookup token + masked display):** contact point values, address lines and
  date of birth. Each value is encrypted with AES-256-GCM using a fresh 96-bit nonce, so two captures
  of the same value never produce the same stored token. The deterministic HMAC-SHA256 lookup token
  exists only for future exact-match duplicate detection (slice 6) and is never exposed by an API.
- **Not protected, on purpose:** the legal name and its normalised comparison form, and the
  structured address parts (city, region, country). Names are needed for operational display and for
  the duplicate work in slice 6; treating them as secret would silently make that work impossible.
  A future field-classification decision can move them behind the same port without a schema change,
  because every personal value already flows through `IProtectedValueProtector`.
- **Keys:** `Privacy:Protection:EncryptionKey` and `Privacy:Protection:LookupKey`, base64, each 32
  bytes and different from each other. `ProtectedValueProtectionOptions.Create` throws at startup when
  a key is missing, wrongly sized or reused, so the service cannot store weakly protected data. No key
  is defaulted, and no plaintext appears in an exception message.
- **Masking:** the tail of a value stays visible (four characters when the value is long enough) so an
  officer can confirm a value the caller already holds; everything else is asterisks.

## Revision and concurrency rules

- The application row carries `revision` and `concurrency_version`. A start records revision 1;
  every revise, submit and withdraw appends the next revision and increments the concurrency version.
- A revision row stores the exact protected payload, its SHA-256 payload hash, the reason
  (`captured`, `submitted`, `withdrawn`), the actor and the recording time.
- `onboarding.application_revisions` is append-only through the trigger
  `trg_application_revisions_append_only`, not through the absence of a grant. The application
  principal keeps the same uniform rights as on every other table, and an update or delete raises
  `restrict_violation`. Decision: history tables are evidence.
- A stale update or withdraw is rejected with `onboarding_revision_conflict` before any write, and a
  `DbUpdateConcurrencyException` from a concurrent writer is reported with the same code. Submission
  compares the confirmed revision, so the officer always submits what they saw.
- Completeness is evaluated on the **stored** snapshot, never on the request. `onboarding_capture_in_progress`
  and `idempotency_key_reused_with_different_request` come from the shared idempotency store
  (ADR-0007); the request fingerprint is a hash of the plaintext capture, so a retry of the same
  capture replays and a changed capture under the same key conflicts.

Failure codes emitted by slice 4: `onboarding_required_field_missing`, `onboarding_field_invalid`,
`onboarding_field_too_long`, `onboarding_revision_conflict`, `onboarding_reference_conflict`,
`onboarding_capture_in_progress`, `idempotency_key_reused_with_different_request`. The epic's
`onboarding_policy_unavailable` and `onboarding_duplicate_review_required` belong to slices 5 and 6.

## Migration and privilege order

`AddPartyAndOnboardingCapture` creates the `party` and `onboarding` schemas. PostgreSQL default
privileges are per schema, so the earlier `AddApplicationPrincipalPrivileges` migration cannot cover
them; `AddPartyAndOnboardingPrivileges` grants schema usage, table and sequence rights and future
defaults for both schemas. `tests/CoreBanking.SecurityTests/ServiceCatalogue.cs` now lists them as
mutable schemas for Customer Onboarding, so the effective-privilege suite asserts the grants rather
than trusting the statements.

Both migrations are additive. `Down` removes the trigger and function before dropping the tables, so
a rollback leaves no orphaned evidence function behind.

## Deliberately unavailable

- **No onboarding API endpoints yet.** The service is reachable only from tests until the request
  contracts, actor resolution and authorisation filter are wired the way Core Ledger does it. An
  unwired endpoint that answers would be worse than an absent one.
- No requirement snapshots, policy resolution, duplicate checks, KYC cases, classification,
  activation, merge or Customer 360. Those are slices 5 onwards and must not be inferred from this
  slice.
- Address verification status, document references and `party.relationships` are not modelled yet.

## Verification

```powershell
dotnet build CoreBanking.sln -c Release
powershell -File scripts/migrations-verify.ps1 -Configuration Release
powershell -File scripts/verify.ps1
powershell -File scripts/infra.ps1 -Action up
powershell -File scripts/db-verify.ps1
```

`db-verify` applies both migrations as `customeronboarding_migrator` and then runs
`OnboardingCaptureDatabaseTests` as `customeronboarding_app`, so a missing grant or a missing trigger
fails there rather than in review.

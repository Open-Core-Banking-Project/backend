# Validates the approved P1-E04-D16 performance profile and, when supplied, gates a captured result.
#
# This script deliberately does not generate load and never treats workstation timings as acceptance evidence.
# A load runner in the dedicated acceptance environment writes a result JSON containing one observation for every
# journey, concurrency, mode and repetition in the versioned profile. This gate then proves that the result is
# comparable and evaluates it against the approved thresholds.
#
# Usage:
#   powershell -File scripts/performance-profile-gate.ps1
#   powershell -File scripts/performance-profile-gate.ps1 -SelfTest
#   powershell -File scripts/performance-profile-gate.ps1 -ResultsPath artifacts/p1-e04-performance-result.json

[CmdletBinding()]
param(
    [string]$ProfilePath = 'tests/performance/profiles/p1-e04-initial-launch.v1.json',
    [string]$ResultsPath,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Read-JsonDocument {
    param([string]$Path)

    $resolved = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $repositoryRoot $Path }
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "JSON document does not exist: $resolved"
    }

    return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
}

function Test-Profile {
    param([object]$Profile)

    $errors = [System.Collections.Generic.List[string]]::new()
    $requiredJourneys = @(
        'account-opening-end-to-end',
        'account-control-change',
        'account-lien-lifecycle',
        'account-summary-read',
        'statement-request-handoff',
        'dormancy-closure-assessment',
        'concurrent-account-allocation-and-control-update'
    )

    if ([string]::IsNullOrWhiteSpace($Profile.profileId)) { $errors.Add('profileId is required') }
    if ($Profile.profileVersion -lt 1) { $errors.Add('profileVersion must be positive') }
    if ($Profile.decisionId -ne 'P1-E04-D16') { $errors.Add('decisionId must be P1-E04-D16') }
    if ($Profile.environment.kind -ne 'dedicated-acceptance') { $errors.Add('environment.kind must be dedicated-acceptance') }
    if ($Profile.environment.syntheticDataOnly -ne $true) { $errors.Add('environment.syntheticDataOnly must be true') }
    if ($Profile.environment.workstationClaimsAllowed -ne $false) { $errors.Add('workstationClaimsAllowed must be false') }
    if ($Profile.dataset.activeAccountsPerInstitution -lt 1) { $errors.Add('activeAccountsPerInstitution must be positive') }
    if ($Profile.run.warmupSeconds -lt 1) { $errors.Add('warmupSeconds must be positive') }
    if ($Profile.run.steadyStateSeconds -lt 1) { $errors.Add('steadyStateSeconds must be positive') }
    if ($Profile.run.repetitions -lt 1) { $errors.Add('repetitions must be positive') }

    $concurrency = @($Profile.run.concurrency)
    if ($concurrency.Count -eq 0 -or @($concurrency | Where-Object { $_ -lt 1 }).Count -gt 0) {
        $errors.Add('concurrency must contain positive values')
    }
    if (@($concurrency | Sort-Object -Unique).Count -ne $concurrency.Count) {
        $errors.Add('concurrency values must be unique')
    }

    $modes = @($Profile.run.modes)
    foreach ($mode in @('cold', 'warm')) {
        if ($mode -notin $modes) { $errors.Add("run.modes must include $mode") }
    }
    if (@($modes | Where-Object { $_ -notin @('cold', 'warm') }).Count -gt 0) {
        $errors.Add('run.modes contains an unsupported value')
    }

    if ($Profile.reporting.maxTechnicalErrorRatePercent -lt 0 -or $Profile.reporting.maxTechnicalErrorRatePercent -gt 100) {
        $errors.Add('maxTechnicalErrorRatePercent must be between 0 and 100')
    }
    if ($Profile.reporting.p50Required -ne $true -or $Profile.reporting.sampleCountRequired -ne $true) {
        $errors.Add('p50 and sample count reporting must be required')
    }

    $journeys = @($Profile.journeys)
    $journeyIds = @($journeys | ForEach-Object { $_.id })
    if (@($journeyIds | Sort-Object -Unique).Count -ne $journeyIds.Count) {
        $errors.Add('journey ids must be unique')
    }
    foreach ($requiredJourney in $requiredJourneys) {
        if ($requiredJourney -notin $journeyIds) { $errors.Add("approved journey is missing: $requiredJourney") }
    }
    if (@($journeyIds | Where-Object { $_ -notin $requiredJourneys }).Count -gt 0) {
        $errors.Add('profile contains an unapproved journey')
    }
    foreach ($journey in $journeys) {
        if ($journey.p95Milliseconds -le 0 -or $journey.p99Milliseconds -le 0) {
            $errors.Add("journey $($journey.id) must have positive latency targets")
        }
        elseif ($journey.p95Milliseconds -gt $journey.p99Milliseconds) {
            $errors.Add("journey $($journey.id) has p95 above p99")
        }
    }

    if ($Profile.recovery.rtoSeconds -lt 1) { $errors.Add('recovery.rtoSeconds must be positive') }
    if ($Profile.recovery.maxLostCommittedTransactions -ne 0) {
        $errors.Add('maxLostCommittedTransactions must be zero')
    }
    $requiredPrerequisites = @(
        'durable-acknowledged-commit-recovery',
        'continuous-wal-archiving',
        'retained-base-backup',
        'off-site-copy',
        'fresh-cluster-restore',
        'service-start-and-verification'
    )
    foreach ($prerequisite in $requiredPrerequisites) {
        if ($prerequisite -notin @($Profile.recovery.claimPrerequisites)) {
            $errors.Add("recovery prerequisite is missing: $prerequisite")
        }
    }

    return @($errors)
}

function Get-ObservationKey {
    param([string]$JourneyId, [int]$Concurrency, [string]$Mode, [int]$Repetition)
    return "$JourneyId|$Concurrency|$Mode|$Repetition"
}

function Test-Results {
    param([object]$Profile, [object]$Results)

    $errors = [System.Collections.Generic.List[string]]::new()
    if ($Results.profileId -ne $Profile.profileId) { $errors.Add('result profileId does not match the approved profile') }
    if ([string]::IsNullOrWhiteSpace($Results.environment.id)) { $errors.Add('result environment identity is required') }
    if ($Results.environment.kind -ne $Profile.environment.kind) { $errors.Add('result environment kind is not approved') }
    if ($Results.environment.workstation -ne $false) { $errors.Add('workstation results cannot pass the acceptance gate') }
    if ($Results.dataset.activeAccountsPerInstitution -ne $Profile.dataset.activeAccountsPerInstitution) {
        $errors.Add('result dataset does not match the approved active-account volume')
    }

    $expected = @{}
    foreach ($journey in @($Profile.journeys)) {
        foreach ($concurrency in @($Profile.run.concurrency)) {
            foreach ($mode in @($Profile.run.modes)) {
                for ($repetition = 1; $repetition -le $Profile.run.repetitions; $repetition++) {
                    $key = Get-ObservationKey $journey.id $concurrency $mode $repetition
                    $expected[$key] = $journey
                }
            }
        }
    }

    $seen = @{}
    foreach ($observation in @($Results.observations)) {
        $key = Get-ObservationKey $observation.journeyId $observation.concurrency $observation.mode $observation.repetition
        if (-not $expected.ContainsKey($key)) {
            $errors.Add("unexpected observation: $key")
            continue
        }
        if ($seen.ContainsKey($key)) {
            $errors.Add("duplicate observation: $key")
            continue
        }
        $seen[$key] = $true
        $target = $expected[$key]

        if ($observation.sampleCount -lt 1) { $errors.Add("$key has no samples") }
        if ($observation.p50Milliseconds -lt 0 -or
            $observation.p50Milliseconds -gt $observation.p95Milliseconds -or
            $observation.p95Milliseconds -gt $observation.p99Milliseconds) {
            $errors.Add("$key has invalid percentile ordering")
        }
        if ($observation.p95Milliseconds -gt $target.p95Milliseconds) {
            $errors.Add("$key exceeds p95: $($observation.p95Milliseconds) > $($target.p95Milliseconds)")
        }
        if ($observation.p99Milliseconds -gt $target.p99Milliseconds) {
            $errors.Add("$key exceeds p99: $($observation.p99Milliseconds) > $($target.p99Milliseconds)")
        }
        if ($observation.technicalErrorRatePercent -lt 0 -or
            $observation.technicalErrorRatePercent -gt $Profile.reporting.maxTechnicalErrorRatePercent) {
            $errors.Add("$key exceeds the technical-error ceiling")
        }
    }

    foreach ($key in $expected.Keys) {
        if (-not $seen.ContainsKey($key)) { $errors.Add("missing observation: $key") }
    }

    return @($errors)
}

function New-PassingResult {
    param([object]$Profile)

    $observations = [System.Collections.Generic.List[object]]::new()
    foreach ($journey in @($Profile.journeys)) {
        foreach ($concurrency in @($Profile.run.concurrency)) {
            foreach ($mode in @($Profile.run.modes)) {
                for ($repetition = 1; $repetition -le $Profile.run.repetitions; $repetition++) {
                    $observations.Add([pscustomobject]@{
                        journeyId = $journey.id
                        concurrency = $concurrency
                        mode = $mode
                        repetition = $repetition
                        sampleCount = 100
                        p50Milliseconds = [math]::Round($journey.p95Milliseconds * 0.5, 3)
                        p95Milliseconds = [math]::Round($journey.p95Milliseconds * 0.9, 3)
                        p99Milliseconds = [math]::Round($journey.p99Milliseconds * 0.9, 3)
                        technicalErrorRatePercent = 0
                    })
                }
            }
        }
    }

    return [pscustomobject]@{
        profileId = $Profile.profileId
        environment = [pscustomobject]@{ id = 'self-test'; kind = 'dedicated-acceptance'; workstation = $false }
        dataset = [pscustomobject]@{ activeAccountsPerInstitution = $Profile.dataset.activeAccountsPerInstitution }
        observations = @($observations)
    }
}

$profile = Read-JsonDocument -Path $ProfilePath
$profileErrors = @(Test-Profile -Profile $profile)
if ($profileErrors.Count -gt 0) {
    Write-Error ("Performance profile is invalid: {0}" -f ($profileErrors -join '; '))
    exit 1
}

if ($SelfTest) {
    $tests = [System.Collections.Generic.List[string]]::new()
    $passing = New-PassingResult -Profile $profile
    if (@(Test-Results -Profile $profile -Results $passing).Count -ne 0) { $tests.Add('passing result was rejected') }

    $missing = New-PassingResult -Profile $profile
    $missing.observations = @($missing.observations | Select-Object -Skip 1)
    if (@(Test-Results -Profile $profile -Results $missing).Count -eq 0) { $tests.Add('missing observation was accepted') }

    $breach = New-PassingResult -Profile $profile
    $firstTarget = @($profile.journeys)[0]
    $breach.observations[0].p95Milliseconds = $firstTarget.p95Milliseconds + 1
    $breach.observations[0].p99Milliseconds = $firstTarget.p99Milliseconds + 1
    if (@(Test-Results -Profile $profile -Results $breach).Count -eq 0) { $tests.Add('latency breach was accepted') }

    $workstation = New-PassingResult -Profile $profile
    $workstation.environment.workstation = $true
    if (@(Test-Results -Profile $profile -Results $workstation).Count -eq 0) { $tests.Add('workstation result was accepted') }

    $wrongDataset = New-PassingResult -Profile $profile
    $wrongDataset.dataset.activeAccountsPerInstitution--
    if (@(Test-Results -Profile $profile -Results $wrongDataset).Count -eq 0) { $tests.Add('wrong dataset was accepted') }

    if ($tests.Count -gt 0) {
        Write-Error ("Performance gate self-test failed: {0}" -f ($tests -join '; '))
        exit 1
    }

    Write-Host 'Performance profile gate self-test: pass (baseline plus 4 negative cases).'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ResultsPath)) {
    Write-Host "Performance profile is valid: $($profile.profileId)."
    Write-Host 'No measurement result was supplied; no performance or capacity claim was evaluated.'
    exit 0
}

$results = Read-JsonDocument -Path $ResultsPath
$resultErrors = @(Test-Results -Profile $profile -Results $results)
if ($resultErrors.Count -gt 0) {
    Write-Error ("Performance result failed the gate: {0}" -f ($resultErrors -join '; '))
    exit 1
}

Write-Host "Performance result passed the approved profile: $($profile.profileId)."
exit 0

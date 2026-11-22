# Validates recovery-drill evidence against the approved P1-E04-D16 recovery objectives.
#
# The gate reads the same versioned profile as the performance gate. It never creates a backup, changes PostgreSQL,
# configures WAL archiving or claims that the local dump/restore drill meets RTO or RPO. A dedicated acceptance
# environment supplies a result JSON after a fresh-cluster point-in-time recovery and service verification.
#
# Usage:
#   powershell -File scripts/recovery-evidence-gate.ps1
#   powershell -File scripts/recovery-evidence-gate.ps1 -SelfTest
#   powershell -File scripts/recovery-evidence-gate.ps1 -ResultsPath artifacts/p1-e04-recovery-result.json

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
    if (-not (Test-Path -LiteralPath $resolved)) { throw "JSON document does not exist: $resolved" }
    return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
}

function Add-RequiredStringError {
    param(
        [System.Collections.Generic.List[string]]$Errors,
        [string]$Name,
        [object]$Value
    )

    if ([string]::IsNullOrWhiteSpace([string]$Value)) { $Errors.Add("$Name is required") }
}

function Convert-RecoveryTime {
    param(
        [System.Collections.Generic.List[string]]$Errors,
        [string]$Name,
        [object]$Value
    )

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$Value, [ref]$parsed)) {
        $Errors.Add("$Name must be an ISO-8601 timestamp")
        return $null
    }
    return $parsed
}

function Test-RecoveryProfile {
    param([object]$Profile)

    $errors = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($Profile.profileId)) { $errors.Add('profileId is required') }
    if ($Profile.decisionId -ne 'P1-E04-D16') { $errors.Add('decisionId must be P1-E04-D16') }
    if ($Profile.environment.kind -ne 'dedicated-acceptance') { $errors.Add('profile environment must be dedicated-acceptance') }
    if ($Profile.environment.workstationClaimsAllowed -ne $false) { $errors.Add('workstation claims must be prohibited') }
    if ($Profile.recovery.rtoSeconds -lt 1) { $errors.Add('recovery.rtoSeconds must be positive') }
    if ($Profile.recovery.maxLostCommittedTransactions -ne 0) { $errors.Add('maxLostCommittedTransactions must be zero') }

    $requiredEvidence = @('journals', 'idempotency-outcomes', 'audit-records')
    foreach ($item in $requiredEvidence) {
        if ($item -notin @($Profile.recovery.protectedEvidence)) { $errors.Add("protected evidence is missing: $item") }
    }

    $requiredPrerequisites = @(
        'durable-acknowledged-commit-recovery',
        'continuous-wal-archiving',
        'retained-base-backup',
        'off-site-copy',
        'fresh-cluster-restore',
        'service-start-and-verification'
    )
    foreach ($item in $requiredPrerequisites) {
        if ($item -notin @($Profile.recovery.claimPrerequisites)) { $errors.Add("claim prerequisite is missing: $item") }
    }

    return @($errors)
}

function Test-RecoveryResults {
    param([object]$Profile, [object]$Results)

    $errors = [System.Collections.Generic.List[string]]::new()
    if ($Results.profileId -ne $Profile.profileId) { $errors.Add('result profileId does not match the approved profile') }
    Add-RequiredStringError $errors 'environment.id' $Results.environment.id
    if ($Results.environment.kind -ne $Profile.environment.kind) { $errors.Add('result environment kind is not approved') }
    if ($Results.environment.workstation -ne $false) { $errors.Add('workstation recovery evidence cannot pass') }
    if ($Results.environment.freshCluster -ne $true) { $errors.Add('recovery must use a fresh cluster') }

    Add-RequiredStringError $errors 'evidence.baseBackupId' $Results.evidence.baseBackupId
    Add-RequiredStringError $errors 'evidence.walArchiveIdentity' $Results.evidence.walArchiveIdentity
    Add-RequiredStringError $errors 'evidence.offSiteCopyIdentity' $Results.evidence.offSiteCopyIdentity
    if ($Results.evidence.restoredToTimestamp -ne $true) { $errors.Add('point-in-time restoration must be proven') }
    Add-RequiredStringError $errors 'evidence.targetRestoreTimestamp' $Results.evidence.targetRestoreTimestamp

    $requiredPrerequisiteFields = @(
        'durableAcknowledgedCommitRecovery',
        'continuousWalArchiving',
        'retainedBaseBackup',
        'offSiteCopy',
        'freshClusterRestore',
        'serviceStartAndVerification'
    )
    foreach ($field in $requiredPrerequisiteFields) {
        if ($Results.prerequisites.$field -ne $true) { $errors.Add("recovery prerequisite was not proven: $field") }
    }

    $timeNames = @(
        'detectedAt',
        'decisionAt',
        'environmentReadyAt',
        'restoreStartedAt',
        'restoreCompletedAt',
        'serviceStartedAt',
        'verifiedAt'
    )
    $times = @{}
    foreach ($name in $timeNames) {
        $times[$name] = Convert-RecoveryTime $errors "timeline.$name" $Results.timeline.$name
    }
    for ($index = 1; $index -lt $timeNames.Count; $index++) {
        $previous = $times[$timeNames[$index - 1]]
        $current = $times[$timeNames[$index]]
        if ($null -ne $previous -and $null -ne $current -and $current -lt $previous) {
            $errors.Add("recovery timeline is not monotonic at $($timeNames[$index])")
        }
    }
    if ($null -ne $times.detectedAt -and $null -ne $times.verifiedAt) {
        $elapsed = ($times.verifiedAt - $times.detectedAt).TotalSeconds
        if ($elapsed -gt $Profile.recovery.rtoSeconds) {
            $errors.Add("recovery exceeded RTO: $elapsed > $($Profile.recovery.rtoSeconds) seconds")
        }
    }

    if ($Results.durability.lostCommittedTransactions -gt $Profile.recovery.maxLostCommittedTransactions) {
        $errors.Add('recovery lost committed transactions')
    }
    if ($Results.durability.acknowledgedCommittedTransactions -lt 0 -or
        $Results.durability.recoveredCommittedTransactions -ne $Results.durability.acknowledgedCommittedTransactions) {
        $errors.Add('recovered committed-transaction count does not match acknowledged commits')
    }
    foreach ($field in @('journalsVerified', 'idempotencyOutcomesVerified', 'auditRecordsVerified')) {
        if ($Results.durability.$field -ne $true) { $errors.Add("protected evidence was not verified: $field") }
    }

    $requiredServices = @(
        'CoreLedger',
        'CustomerOnboarding',
        'IdentityAccess',
        'PaymentsVtu',
        'ChannelsEngagement',
        'OperationsWorkflow',
        'RiskReporting',
        'IntegrationHub',
        'AuditReconciliation'
    )
    $serviceNames = @($Results.databases | ForEach-Object { $_.service })
    if (@($serviceNames | Sort-Object -Unique).Count -ne $serviceNames.Count) {
        $errors.Add('database service evidence contains duplicates')
    }
    foreach ($service in $requiredServices) {
        $database = @($Results.databases | Where-Object { $_.service -eq $service })
        if ($database.Count -ne 1) {
            $errors.Add("database evidence is missing: $service")
        }
        elseif ($database[0].restored -ne $true -or $database[0].verified -ne $true) {
            $errors.Add("database restore was not verified: $service")
        }
    }
    if (@($serviceNames | Where-Object { $_ -notin $requiredServices }).Count -gt 0) {
        $errors.Add('database evidence contains an unknown service')
    }

    return @($errors)
}

function Copy-RecoveryResult {
    param([object]$Result)
    return $Result | ConvertTo-Json -Depth 20 | ConvertFrom-Json
}

function New-PassingRecoveryResult {
    param([object]$Profile)

    $services = @(
        'CoreLedger', 'CustomerOnboarding', 'IdentityAccess', 'PaymentsVtu', 'ChannelsEngagement',
        'OperationsWorkflow', 'RiskReporting', 'IntegrationHub', 'AuditReconciliation'
    )
    return [pscustomobject]@{
        profileId = $Profile.profileId
        environment = [pscustomobject]@{
            id = 'self-test-acceptance'
            kind = 'dedicated-acceptance'
            workstation = $false
            freshCluster = $true
        }
        evidence = [pscustomobject]@{
            baseBackupId = 'base-backup-self-test'
            walArchiveIdentity = 'wal-archive-self-test'
            offSiteCopyIdentity = 'off-site-self-test'
            targetRestoreTimestamp = '2026-11-21T12:30:00Z'
            restoredToTimestamp = $true
        }
        prerequisites = [pscustomobject]@{
            durableAcknowledgedCommitRecovery = $true
            continuousWalArchiving = $true
            retainedBaseBackup = $true
            offSiteCopy = $true
            freshClusterRestore = $true
            serviceStartAndVerification = $true
        }
        timeline = [pscustomobject]@{
            detectedAt = '2026-11-21T13:00:00Z'
            decisionAt = '2026-11-21T13:10:00Z'
            environmentReadyAt = '2026-11-21T13:30:00Z'
            restoreStartedAt = '2026-11-21T13:35:00Z'
            restoreCompletedAt = '2026-11-21T14:30:00Z'
            serviceStartedAt = '2026-11-21T14:40:00Z'
            verifiedAt = '2026-11-21T15:00:00Z'
        }
        durability = [pscustomobject]@{
            acknowledgedCommittedTransactions = 1000
            recoveredCommittedTransactions = 1000
            lostCommittedTransactions = 0
            journalsVerified = $true
            idempotencyOutcomesVerified = $true
            auditRecordsVerified = $true
        }
        databases = @($services | ForEach-Object { [pscustomobject]@{ service = $_; restored = $true; verified = $true } })
    }
}

$profile = Read-JsonDocument -Path $ProfilePath
$profileErrors = @(Test-RecoveryProfile -Profile $profile)
if ($profileErrors.Count -gt 0) {
    Write-Error ("Recovery profile is invalid: {0}" -f ($profileErrors -join '; '))
    exit 1
}

if ($SelfTest) {
    $failures = [System.Collections.Generic.List[string]]::new()
    $passing = New-PassingRecoveryResult -Profile $profile
    if (@(Test-RecoveryResults $profile $passing).Count -ne 0) { $failures.Add('passing recovery result was rejected') }

    $rtoBreach = Copy-RecoveryResult $passing
    $rtoBreach.timeline.verifiedAt = '2026-11-21T17:00:01Z'
    if (@(Test-RecoveryResults $profile $rtoBreach).Count -eq 0) { $failures.Add('RTO breach was accepted') }

    $lostCommit = Copy-RecoveryResult $passing
    $lostCommit.durability.lostCommittedTransactions = 1
    $lostCommit.durability.recoveredCommittedTransactions = 999
    if (@(Test-RecoveryResults $profile $lostCommit).Count -eq 0) { $failures.Add('committed-transaction loss was accepted') }

    $missingPrerequisite = Copy-RecoveryResult $passing
    $missingPrerequisite.prerequisites.offSiteCopy = $false
    if (@(Test-RecoveryResults $profile $missingPrerequisite).Count -eq 0) { $failures.Add('missing off-site copy was accepted') }

    $workstation = Copy-RecoveryResult $passing
    $workstation.environment.workstation = $true
    if (@(Test-RecoveryResults $profile $workstation).Count -eq 0) { $failures.Add('workstation recovery was accepted') }

    $missingDatabase = Copy-RecoveryResult $passing
    $missingDatabase.databases = @($missingDatabase.databases | Select-Object -Skip 1)
    if (@(Test-RecoveryResults $profile $missingDatabase).Count -eq 0) { $failures.Add('incomplete database set was accepted') }

    if ($failures.Count -gt 0) {
        Write-Error ("Recovery evidence gate self-test failed: {0}" -f ($failures -join '; '))
        exit 1
    }

    Write-Host 'Recovery evidence gate self-test: pass (baseline plus 5 negative cases).'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ResultsPath)) {
    Write-Host "Recovery profile is valid: $($profile.profileId)."
    Write-Host 'No recovery result was supplied; no RTO or RPO claim was evaluated.'
    exit 0
}

$results = Read-JsonDocument -Path $ResultsPath
$resultErrors = @(Test-RecoveryResults -Profile $profile -Results $results)
if ($resultErrors.Count -gt 0) {
    Write-Error ("Recovery result failed the gate: {0}" -f ($resultErrors -join '; '))
    exit 1
}

Write-Host "Recovery result passed the approved profile: $($profile.profileId)."
exit 0

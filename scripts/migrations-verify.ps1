# EF Core migration-drift verification for the backend.
#
# Every service owns its own database and its own migrations, so drift is verified per context
# rather than for the solution as a whole. For each context the gate proves two things:
#   1. the migration assembly loads and the committed migrations are discoverable, and
#   2. the committed model snapshot matches the current EF Core model.
#
# The checks are read-only. They never connect to a database (`--no-connect`), never generate a
# migration and never apply one. Decisions:
#   docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
#   docs/adr/ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md
#
# Exit codes:
#   0  every context is consistent
#   1  migration drift: the model has changed and no migration records it
#   2  tooling, SDK, project or design-time factory failure (no reliable drift result)
#   3  drift and a tooling failure were both observed
#
# Usage (PowerShell 7 as `pwsh`, or Windows PowerShell 5.1 as `powershell`):
#   pwsh scripts/migrations-verify.ps1
#   pwsh scripts/migrations-verify.ps1 -Configuration Debug -ContextName IdentityAccess
#   pwsh scripts/migrations-verify.ps1 -NoBuild   # after `dotnet build` already ran

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Service names from the table below. Omit to verify all nine contexts.
    [string[]]$ContextName = @(),

    # Skip restore and build. Use when the caller already built this configuration.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

# One entry per service-owned DbContext. The project and the startup project are the same
# Infrastructure project because each one hosts its own IDesignTimeDbContextFactory.
$contexts = @(
    [pscustomobject]@{ Service = 'CoreLedger'; Project = 'src/Services/CoreLedger/src/CoreBanking.CoreLedger.Infrastructure'; Context = 'CoreLedgerDbContext' },
    [pscustomobject]@{ Service = 'CustomerOnboarding'; Project = 'src/Services/CustomerOnboarding/src/CoreBanking.CustomerOnboarding.Infrastructure'; Context = 'CustomerOnboardingDbContext' },
    [pscustomobject]@{ Service = 'IdentityAccess'; Project = 'src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure'; Context = 'IdentityAccessDbContext' },
    [pscustomobject]@{ Service = 'PaymentsVtu'; Project = 'src/Services/PaymentsVtu/src/CoreBanking.PaymentsVtu.Infrastructure'; Context = 'PaymentsVtuDbContext' },
    [pscustomobject]@{ Service = 'ChannelsEngagement'; Project = 'src/Services/ChannelsEngagement/src/CoreBanking.ChannelsEngagement.Infrastructure'; Context = 'ChannelsEngagementDbContext' },
    [pscustomobject]@{ Service = 'OperationsWorkflow'; Project = 'src/Services/OperationsWorkflow/src/CoreBanking.OperationsWorkflow.Infrastructure'; Context = 'OperationsWorkflowDbContext' },
    [pscustomobject]@{ Service = 'RiskReporting'; Project = 'src/Services/RiskReporting/src/CoreBanking.RiskReporting.Infrastructure'; Context = 'RiskReportingDbContext' },
    [pscustomobject]@{ Service = 'IntegrationHub'; Project = 'src/Services/IntegrationHub/src/CoreBanking.IntegrationHub.Infrastructure'; Context = 'IntegrationHubDbContext' },
    [pscustomobject]@{ Service = 'AuditReconciliation'; Project = 'src/Services/AuditReconciliation/src/CoreBanking.AuditReconciliation.Infrastructure'; Context = 'AuditReconciliationDbContext' }
)

$driftMessage = 'Changes have been made to the model since the last migration'
$cleanMessage = 'No changes have been made to the model since the last migration'

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    # Native tools write diagnostics to stderr. Capturing them must not turn a nonzero exit code
    # into an exception, because the exit code and the text are classified separately.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $text = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    }
    finally {
        $ErrorActionPreference = $previous
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

function Get-FirstFailureLine {
    param([string]$Output)

    $lines = $Output -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $errorLine = $lines | Where-Object { $_ -match '(?i)\berror\b|exception|could not|not found|failed' } | Select-Object -First 1
    if ($errorLine) {
        return $errorLine.Trim()
    }

    $last = $lines | Select-Object -Last 1
    if ($last) {
        return $last.Trim()
    }

    return 'no diagnostic output was produced'
}

function Assert-EveryContextIsRegistered {
    # A new service must not silently escape the gate. Every design-time factory under the service
    # tree has to have an entry above.
    $factoryFiles = Get-ChildItem -Path (Join-Path $repositoryRoot 'src/Services') -Recurse -File -Filter '*DbContextFactory.cs'
    $registered = @{}
    foreach ($entry in $contexts) {
        $registered[$entry.Context] = $true
    }

    $unregistered = @()
    foreach ($file in $factoryFiles) {
        $expectedContext = $file.BaseName -replace 'Factory$', ''
        if (-not $registered.ContainsKey($expectedContext)) {
            $unregistered += $file.FullName.Substring($repositoryRoot.Length + 1)
        }
    }

    if ($unregistered.Count -gt 0) {
        throw ("Migration verification does not cover: {0}. Add one entry per context to `$contexts in scripts/migrations-verify.ps1." -f ($unregistered -join ', '))
    }
}

function Assert-ProjectExists {
    param([pscustomobject]$Entry)

    $projectPath = Join-Path $repositoryRoot $Entry.Project
    if (-not (Test-Path $projectPath)) {
        throw "The project for $($Entry.Service) was not found at '$($Entry.Project)'. Update scripts/migrations-verify.ps1."
    }

    $factory = Get-ChildItem -Path $projectPath -Recurse -File -Filter "$($Entry.Context)Factory.cs" -ErrorAction SilentlyContinue
    if (-not $factory) {
        throw "The project for $($Entry.Service) has no design-time factory for $($Entry.Context). Migration tooling cannot build the model."
    }

    $migrations = Get-ChildItem -Path (Join-Path $projectPath 'Persistence/Migrations') -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch '\.Designer\.cs$' -and $_.Name -notmatch 'ModelSnapshot\.cs$' }
    if (-not $migrations) {
        throw "The project for $($Entry.Service) has no EF Core migrations under 'Persistence/Migrations'."
    }
}

Push-Location $repositoryRoot

$results = @()
$driftFound = $false
$toolingFailed = $false
$fatalMessage = $null

try {
    Assert-EveryContextIsRegistered

    $selected = $contexts
    if ($ContextName.Count -gt 0) {
        $selected = @()
        foreach ($name in $ContextName) {
            $match = $contexts | Where-Object { $_.Service -eq $name -or $_.Context -eq $name }
            if (-not $match) {
                throw "Unknown context '$name'. Known contexts: $(($contexts | ForEach-Object { $_.Service }) -join ', ')."
            }

            $selected += $match
        }
    }

    foreach ($entry in $selected) {
        Assert-ProjectExists -Entry $entry
    }

    # The model-only design-time connection strings exist so the tools can build the model without
    # configuration. Clearing any inherited connection string keeps this gate unable to reach a
    # database even when a developer or a previous script exported one.
    foreach ($entry in $contexts) {
        Remove-Item -Path "Env:ConnectionStrings__$($entry.Service)" -ErrorAction SilentlyContinue
    }

    Write-Host '==> restore tools' -ForegroundColor Cyan
    $toolRestore = Invoke-Tool -FilePath 'dotnet' -Arguments @('tool', 'restore')
    if ($toolRestore.ExitCode -ne 0) {
        throw "dotnet tool restore failed. $(Get-FirstFailureLine -Output $toolRestore.Output)"
    }

    if (-not $NoBuild) {
        Write-Host '==> restore' -ForegroundColor Cyan
        $restore = Invoke-Tool -FilePath 'dotnet' -Arguments @('restore')
        if ($restore.ExitCode -ne 0) {
            throw "dotnet restore failed. $(Get-FirstFailureLine -Output $restore.Output)"
        }

        Write-Host "==> build ($Configuration)" -ForegroundColor Cyan
        $build = Invoke-Tool -FilePath 'dotnet' -Arguments @('build', '--no-restore', '--configuration', $Configuration)
        if ($build.ExitCode -ne 0) {
            throw "dotnet build failed, so migration drift cannot be evaluated. $(Get-FirstFailureLine -Output $build.Output)"
        }
    }

    foreach ($entry in $selected) {
        Write-Host "==> $($entry.Service) ($($entry.Context))" -ForegroundColor Cyan

        $commonArguments = @(
            '--project', $entry.Project,
            '--startup-project', $entry.Project,
            '--context', $entry.Context,
            '--configuration', $Configuration,
            '--no-build',
            '--no-color'
        )

        $list = Invoke-Tool -FilePath 'dotnet' -Arguments (@('tool', 'run', 'dotnet-ef', 'migrations', 'list', '--no-connect') + $commonArguments)
        if ($list.ExitCode -ne 0) {
            $toolingFailed = $true
            $results += [pscustomobject]@{
                Service = $entry.Service
                Context = $entry.Context
                Status  = 'Tooling failure'
                Detail  = "migrations list failed: $(Get-FirstFailureLine -Output $list.Output)"
            }
            continue
        }

        $pending = Invoke-Tool -FilePath 'dotnet' -Arguments (@('tool', 'run', 'dotnet-ef', 'migrations', 'has-pending-model-changes') + $commonArguments)
        switch ($pending.ExitCode) {
            0 {
                if ($pending.Output -notmatch [regex]::Escape($cleanMessage)) {
                    $toolingFailed = $true
                    $results += [pscustomobject]@{
                        Service = $entry.Service
                        Context = $entry.Context
                        Status  = 'Tooling failure'
                        Detail  = 'has-pending-model-changes reported success without the expected confirmation.'
                    }
                }
                else {
                    $results += [pscustomobject]@{
                        Service = $entry.Service
                        Context = $entry.Context
                        Status  = 'Consistent'
                        Detail  = 'model snapshot matches the model'
                    }
                }
            }
            1 {
                if ($pending.Output -match [regex]::Escape($driftMessage)) {
                    $driftFound = $true
                    $results += [pscustomobject]@{
                        Service = $entry.Service
                        Context = $entry.Context
                        Status  = 'Drift'
                        Detail  = 'the model changed without a migration: add one with dotnet ef migrations add'
                    }
                }
                else {
                    $toolingFailed = $true
                    $results += [pscustomobject]@{
                        Service = $entry.Service
                        Context = $entry.Context
                        Status  = 'Tooling failure'
                        Detail  = "has-pending-model-changes failed: $(Get-FirstFailureLine -Output $pending.Output)"
                    }
                }
            }
            default {
                $toolingFailed = $true
                $results += [pscustomobject]@{
                    Service = $entry.Service
                    Context = $entry.Context
                    Status  = 'Tooling failure'
                    Detail  = "has-pending-model-changes exited with $($pending.ExitCode): $(Get-FirstFailureLine -Output $pending.Output)"
                }
            }
        }
    }
}
catch {
    $fatalMessage = $_.Exception.Message
}
finally {
    Pop-Location
}

if ($fatalMessage) {
    Write-Host ''
    Write-Host "Migration verification could not run: $fatalMessage" -ForegroundColor Red
    exit 2
}

Write-Host ''
Write-Host 'Migration verification summary' -ForegroundColor Cyan
$results | Format-Table -AutoSize -Property Service, Context, Status, Detail | Out-String -Width 200 | Write-Host

foreach ($failure in $results | Where-Object { $_.Status -eq 'Drift' }) {
    Write-Host "DRIFT: $($failure.Service) ($($failure.Context)) - $($failure.Detail)" -ForegroundColor Red
}

foreach ($failure in $results | Where-Object { $_.Status -eq 'Tooling failure' }) {
    Write-Host "TOOLING FAILURE: $($failure.Service) ($($failure.Context)) - $($failure.Detail)" -ForegroundColor Yellow
}

if ($driftFound -and $toolingFailed) {
    Write-Host 'Migration verification failed: drift was found and at least one context could not be verified.' -ForegroundColor Red
    exit 3
}

if ($driftFound) {
    Write-Host 'Migration verification failed: committed migrations do not match the model.' -ForegroundColor Red
    exit 1
}

if ($toolingFailed) {
    Write-Host 'Migration verification could not complete: fix the tooling or configuration failure above and re-run.' -ForegroundColor Red
    exit 2
}

Write-Host 'Migration verification passed for every context.' -ForegroundColor Green
exit 0

# Database verification for the backend.
# Applies every service's migrations with its migration principal, then runs the database and
# integration tests as the application principal. Requires the local infrastructure stack:
#   powershell -File scripts/infra.ps1 -Action up
#
# Usage:
#   powershell -File scripts/db-verify.ps1
#
# Credentials are read from deploy/compose/.env, which is git-ignored, and are passed to the tools
# only through environment variables. They are never printed. No database outside the local stack
# is touched: every endpoint is 127.0.0.1 and the port comes from that file.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $repositoryRoot 'deploy/compose/.env'

# One entry per service: the migration project, the context and the database the migrations own.
$services = @(
    [pscustomobject]@{ Service = 'CoreLedger'; Database = 'core_ledger_db'; Project = 'src/Services/CoreLedger/src/CoreBanking.CoreLedger.Infrastructure'; Context = 'CoreLedgerDbContext' },
    [pscustomobject]@{ Service = 'CustomerOnboarding'; Database = 'customer_onboarding_db'; Project = 'src/Services/CustomerOnboarding/src/CoreBanking.CustomerOnboarding.Infrastructure'; Context = 'CustomerOnboardingDbContext' },
    [pscustomobject]@{ Service = 'IdentityAccess'; Database = 'identity_access_db'; Project = 'src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure'; Context = 'IdentityAccessDbContext' },
    [pscustomobject]@{ Service = 'PaymentsVtu'; Database = 'payments_vtu_db'; Project = 'src/Services/PaymentsVtu/src/CoreBanking.PaymentsVtu.Infrastructure'; Context = 'PaymentsVtuDbContext' },
    [pscustomobject]@{ Service = 'ChannelsEngagement'; Database = 'channels_engagement_db'; Project = 'src/Services/ChannelsEngagement/src/CoreBanking.ChannelsEngagement.Infrastructure'; Context = 'ChannelsEngagementDbContext' },
    [pscustomobject]@{ Service = 'OperationsWorkflow'; Database = 'operations_workflow_db'; Project = 'src/Services/OperationsWorkflow/src/CoreBanking.OperationsWorkflow.Infrastructure'; Context = 'OperationsWorkflowDbContext' },
    [pscustomobject]@{ Service = 'RiskReporting'; Database = 'risk_reporting_db'; Project = 'src/Services/RiskReporting/src/CoreBanking.RiskReporting.Infrastructure'; Context = 'RiskReportingDbContext' },
    [pscustomobject]@{ Service = 'IntegrationHub'; Database = 'integration_hub_db'; Project = 'src/Services/IntegrationHub/src/CoreBanking.IntegrationHub.Infrastructure'; Context = 'IntegrationHubDbContext' },
    [pscustomobject]@{ Service = 'AuditReconciliation'; Database = 'audit_reconciliation_db'; Project = 'src/Services/AuditReconciliation/src/CoreBanking.AuditReconciliation.Infrastructure'; Context = 'AuditReconciliationDbContext' }
)

function Read-EnvironmentFile {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "$Path does not exist. Run scripts/infra.ps1 -Action up first."
    }

    $values = @{}
    foreach ($line in Get-Content -Path $Path) {
        if ($line -match '^\s*([A-Za-z0-9_]+)\s*=\s*(.*)$') {
            $values[$Matches[1]] = $Matches[2].Trim()
        }
    }

    return $values
}

function New-ServiceConnectionString {
    param([string]$Port, [string]$Database, [string]$Username, [string]$Password)

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "No password is configured for '$Username' in deploy/compose/.env."
    }

    return "Host=127.0.0.1;Port=$Port;Database=$Database;Username=$Username;Password=$Password"
}

function Set-ServiceConnectionString {
    param([string]$Name, [string]$Value)

    Set-Item -Path "Env:ConnectionStrings__$Name" -Value $Value
}

Push-Location $repositoryRoot

try {
    $environment = Read-EnvironmentFile -Path $environmentFile

    if ([string]::IsNullOrWhiteSpace($environment['CB_POSTGRES_PORT'])) {
        throw 'CB_POSTGRES_PORT is not set in deploy/compose/.env.'
    }

    $port = $environment['CB_POSTGRES_PORT']

    $probe = New-Object System.Net.Sockets.TcpClient
    try {
        $probe.Connect('127.0.0.1', [int]$port)
    }
    catch {
        throw "PostgreSQL is not reachable on 127.0.0.1:$port. Run scripts/infra.ps1 -Action up first."
    }
    finally {
        $probe.Dispose()
    }

    $passwords = @{}
    foreach ($service in $services) {
        $prefix = $service.Service.ToUpperInvariant()
        $applicationPassword = $environment["${prefix}_APP_PASSWORD"]
        $migrationPassword = $environment["${prefix}_MIGRATOR_PASSWORD"]

        if ([string]::IsNullOrWhiteSpace($applicationPassword) -or [string]::IsNullOrWhiteSpace($migrationPassword)) {
            throw "${prefix}_APP_PASSWORD or ${prefix}_MIGRATOR_PASSWORD is not set in deploy/compose/.env."
        }

        $passwords[$service.Service] = [pscustomobject]@{
            Application = $applicationPassword
            Migration   = $migrationPassword
        }
    }

    Write-Host '==> restore tools' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }

    # Vulnerability metadata is fetched from nuget.org; local verification must also work offline.
    Write-Host '==> restore from available package sources' -ForegroundColor Cyan
    dotnet restore --ignore-failed-sources -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Solution restore failed with exit code $LASTEXITCODE." }

    Write-Host "==> build ($Configuration)" -ForegroundColor Cyan
    dotnet build --no-restore --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }

    foreach ($service in $services) {
        $migrationConnection = New-ServiceConnectionString -Port $port -Database $service.Database `
            -Username ($service.Service.ToLowerInvariant() + '_migrator') -Password $passwords[$service.Service].Migration
        Set-ServiceConnectionString -Name $service.Service -Value $migrationConnection

        Write-Host "==> apply $($service.Service) migrations as $($service.Service.ToLowerInvariant())_migrator" -ForegroundColor Cyan
        dotnet tool run dotnet-ef database update --project $service.Project --startup-project $service.Project `
            --context $service.Context --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "$($service.Service) database migration failed with exit code $LASTEXITCODE." }

        Write-Host "==> $($service.Service) applied migrations" -ForegroundColor Cyan
        dotnet tool run dotnet-ef migrations list --project $service.Project --startup-project $service.Project `
            --context $service.Context --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "$($service.Service) migrations list failed with exit code $LASTEXITCODE." }
    }

    if (-not [string]::IsNullOrWhiteSpace($environment['CB_BROKER_PORT'])) {
        $env:CB_BROKER_BOOTSTRAP_SERVERS = '127.0.0.1:' + $environment['CB_BROKER_PORT']
    }

    # The tests run as the application principal, so a missing grant fails here rather than in
    # production. Each service database is verified separately; a service without database tests
    # reports that no test matched and still succeeds.
    foreach ($service in $services) {
        $testProject = "src/Services/$($service.Service)/tests/CoreBanking.$($service.Service).UnitTests"
        if (-not (Test-Path (Join-Path $repositoryRoot $testProject))) {
            Write-Host "==> $($service.Service) has no test project; skipping database tests" -ForegroundColor Yellow
            continue
        }

        $applicationConnection = New-ServiceConnectionString -Port $port -Database $service.Database `
            -Username ($service.Service.ToLowerInvariant() + '_app') -Password $passwords[$service.Service].Application
        Set-ServiceConnectionString -Name $service.Service -Value $applicationConnection

        Write-Host "==> $($service.Service) database tests as $($service.Service.ToLowerInvariant())_app" -ForegroundColor Cyan
        dotnet test $testProject --no-build --configuration $Configuration --filter 'Category=Database|Category=Integration'
        if ($LASTEXITCODE -ne 0) { throw "$($service.Service) database tests failed with exit code $LASTEXITCODE." }
    }

    # Every principal is connected as itself, so the assertions cover role membership, ownership and
    # PUBLIC grants rather than the GRANT statements alone.
    Write-Host '==> effective privilege tests for every application principal' -ForegroundColor Cyan
    foreach ($service in $services) {
        $applicationConnection = New-ServiceConnectionString -Port $port -Database $service.Database `
            -Username ($service.Service.ToLowerInvariant() + '_app') -Password $passwords[$service.Service].Application
        Set-ServiceConnectionString -Name $service.Service -Value $applicationConnection
    }

    dotnet test 'tests/CoreBanking.SecurityTests' --no-build --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Effective privilege tests failed with exit code $LASTEXITCODE." }

    Write-Host 'Database verification passed.' -ForegroundColor Green
}
finally {
    foreach ($service in $services) {
        Remove-Item -Path "Env:ConnectionStrings__$($service.Service)" -ErrorAction SilentlyContinue
    }

    Remove-Item Env:\CB_BROKER_BOOTSTRAP_SERVERS -ErrorAction SilentlyContinue
    Pop-Location
}

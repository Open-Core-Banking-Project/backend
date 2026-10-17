# Database verification for the backend.
# Applies the Identity and Access migrations with the migration principal, then runs the
# database tests as the application principal. Requires the local infrastructure stack:
#   powershell -File scripts/infra.ps1 -Action up
#
# Usage:
#   powershell -File scripts/db-verify.ps1
#
# Credentials are read from deploy/compose/.env, which is git-ignored, and are passed to the
# tools only through environment variables. They are never printed.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $repositoryRoot 'deploy/compose/.env'
$infrastructureProject = Join-Path $repositoryRoot 'src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure'
$testProject = Join-Path $repositoryRoot 'src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests'

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

function New-ConnectionString {
    param([string]$Port, [string]$Username, [string]$Password)

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "No password is configured for '$Username' in deploy/compose/.env."
    }

    return "Host=127.0.0.1;Port=$Port;Database=identity_access_db;Username=$Username;Password=$Password"
}

Push-Location $repositoryRoot

try {
    $environment = Read-EnvironmentFile -Path $environmentFile

    if ([string]::IsNullOrWhiteSpace($environment['CB_POSTGRES_PORT'])) {
        throw 'CB_POSTGRES_PORT is not set in deploy/compose/.env.'
    }

    $migratorConnection = New-ConnectionString -Port $environment['CB_POSTGRES_PORT'] -Username 'identityaccess_migrator' -Password $environment['IDENTITYACCESS_MIGRATOR_PASSWORD']
    $applicationConnection = New-ConnectionString -Port $environment['CB_POSTGRES_PORT'] -Username 'identityaccess_app' -Password $environment['IDENTITYACCESS_APP_PASSWORD']

    $probe = New-Object System.Net.Sockets.TcpClient
    try {
        $probe.Connect('127.0.0.1', [int]$environment['CB_POSTGRES_PORT'])
    }
    catch {
        throw "PostgreSQL is not reachable on 127.0.0.1:$($environment['CB_POSTGRES_PORT']). Run scripts/infra.ps1 -Action up first."
    }
    finally {
        $probe.Dispose()
    }

    Write-Host '==> restore tools' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }

    Write-Host '==> apply migrations as identityaccess_migrator' -ForegroundColor Cyan
    $env:ConnectionStrings__IdentityAccess = $migratorConnection
    dotnet tool run dotnet-ef database update --project $infrastructureProject --startup-project $infrastructureProject
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed with exit code $LASTEXITCODE." }

    Write-Host '==> applied migrations' -ForegroundColor Cyan
    dotnet tool run dotnet-ef migrations list --project $infrastructureProject --startup-project $infrastructureProject --no-connect
    dotnet tool run dotnet-ef migrations list --project $infrastructureProject --startup-project $infrastructureProject
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations list failed with exit code $LASTEXITCODE." }

    Write-Host '==> database tests as identityaccess_app' -ForegroundColor Cyan
    $env:ConnectionStrings__IdentityAccess = $applicationConnection
    $env:CB_BROKER_BOOTSTRAP_SERVERS = '127.0.0.1:' + $environment['CB_BROKER_PORT']
    dotnet test $testProject --configuration $Configuration --filter 'Category=Database|Category=Integration'
    if ($LASTEXITCODE -ne 0) { throw "Database tests failed with exit code $LASTEXITCODE." }

    Write-Host 'Database verification passed.' -ForegroundColor Green
}
finally {
    Remove-Item Env:\ConnectionStrings__IdentityAccess -ErrorAction SilentlyContinue
    Remove-Item Env:\CB_BROKER_BOOTSTRAP_SERVERS -ErrorAction SilentlyContinue
    Pop-Location
}

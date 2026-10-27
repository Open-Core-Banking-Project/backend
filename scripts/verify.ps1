# Repository verification gate for the backend.
# Mirrors the sequence documented in AGENTS.md: restore, build, migration drift, test, format.
# Database tests need the local infrastructure stack and are excluded here; run
# scripts/db-verify.ps1 to apply migrations and execute them.
#
# Usage (PowerShell 7 as `pwsh`, or Windows PowerShell 5.1 as `powershell`):
#   pwsh scripts/verify.ps1
#   powershell -File scripts/verify.ps1 -Configuration Debug

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot

# scripts/migrations-verify.ps1 reports drift through its process exit code, so it is invoked in a
# child host. Running it in this process would end this gate before the remaining steps.
function Invoke-MigrationVerification {
    param([string]$Configuration)

    if (Get-Command -Name 'pwsh' -ErrorAction SilentlyContinue) {
        $hostExecutable = 'pwsh'
    }
    else {
        $hostExecutable = Join-Path $PSHOME 'powershell.exe'
    }

    & $hostExecutable -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'migrations-verify.ps1') -Configuration $Configuration -NoBuild
    return $LASTEXITCODE
}

try {
    $steps = [ordered]@{
        'restore'    = { dotnet restore }
        'build'      = { dotnet build --no-restore --configuration $Configuration }
        'migrations' = { Invoke-MigrationVerification -Configuration $Configuration }
        'test'       = { dotnet test --no-build --configuration $Configuration --filter 'Category!=Database&Category!=Integration' }
        'format'     = { dotnet format --verify-no-changes }
    }

    foreach ($step in $steps.GetEnumerator()) {
        Write-Host "==> $($step.Key)" -ForegroundColor Cyan
        & $step.Value

        if ($LASTEXITCODE -ne 0) {
            throw "Verification step '$($step.Key)' failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host 'All verification steps passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}

# Repository verification gate for the backend.
# Mirrors the sequence documented in AGENTS.md: restore, build, test, format.
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

try {
    $steps = [ordered]@{
        'restore' = { dotnet restore }
        'build'   = { dotnet build --no-restore --configuration $Configuration }
        'test'    = { dotnet test --no-build --configuration $Configuration --filter 'Category!=Database' }
        'format'  = { dotnet format --verify-no-changes }
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

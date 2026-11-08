# Phase 1 evidence harness for everything that does not need the containerised database.
#
# P1-E04 slice 15 asks for security, performance, backup/restore and end-to-end evidence. Three of those
# need the live stack (scripts/db-verify.ps1) or an approved capacity profile (P1-E04-D16), so this
# harness collects the evidence that is reproducible on a workstation and records exactly what is
# missing. It never claims a database, performance or recovery result it did not observe.
#
# It runs, in order:
#   1. the repository gate (scripts/verify.ps1: restore, build, migration drift, non-database tests, format)
#   2. every unit-test project with its own Passed/Failed counts, so a per-service figure is recorded
#   3. the permission-grammar scan: every PermissionIdentifier.Parse literal must have exactly the
#      approved domain.resource.action segments, and no capability may be declared outside it
#   4. the supply-chain scan: vulnerable packages, direct and transitive
#
# Output: a markdown report under artifacts/ (git-ignored) plus a console summary. Exit code is 0 only
# when the gate, the grammar scan and every test project pass; the supply-chain scan is reported but a
# NuGet feed that cannot be reached is recorded as unknown rather than as a pass.
#
# Usage:
#   powershell -File scripts/phase1-evidence.ps1
#   powershell -File scripts/phase1-evidence.ps1 -Configuration Debug -SkipGate

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Skip the repository gate when it was already run for this commit.
    [switch]$SkipGate,

    # Directory for the report. Created when missing.
    [string]$ReportDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-ChildHost {
    param([string]$ScriptPath, [string[]]$Arguments)

    if (Get-Command -Name 'pwsh' -ErrorAction SilentlyContinue) {
        $hostExecutable = 'pwsh'
    }
    else {
        $hostExecutable = Join-Path $PSHOME 'powershell.exe'
    }

    $output = & $hostExecutable -NoProfile -NonInteractive -File $ScriptPath @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

try {
    $report = [System.Collections.Generic.List[string]]::new()
    $startedAt = (Get-Date).ToUniversalTime()
    $commit = (& git rev-parse HEAD 2>$null)
    $sdk = (& dotnet --version)

    $report.Add('# Phase 1 evidence (database-free)')
    $report.Add('')
    $report.Add("- Generated: $($startedAt.ToString('u'))")
    $report.Add("- Commit: $commit")
    $report.Add("- .NET SDK: $sdk")
    $report.Add("- Configuration: $Configuration")
    $report.Add('')

    # 1. Repository gate.
    $report.Add('## Repository gate')
    $report.Add('')
    if ($SkipGate) {
        $report.Add('Skipped by request (-SkipGate).')
    }
    elseif (Test-Path (Join-Path $PSScriptRoot 'verify.ps1')) {
        $gate = Invoke-ChildHost -ScriptPath (Join-Path $PSScriptRoot 'verify.ps1') -Arguments @('-Configuration', $Configuration)
        $gateResult = if ($gate.ExitCode -eq 0) { 'pass' } else { 'FAIL' }
        if ($gate.ExitCode -ne 0) { $failures.Add("verify.ps1 exited $($gate.ExitCode)") }
        $report.Add("scripts/verify.ps1: $gateResult (exit $($gate.ExitCode))")
        foreach ($line in $gate.Output | Select-Object -Last 20) { $report.Add("    $line") }
    }
    else {
        $report.Add('scripts/verify.ps1 is missing, so the gate was not run.')
        $failures.Add('scripts/verify.ps1 missing')
    }
    $report.Add('')

    # 2. Per-service non-database suites.
    $report.Add('## Per-service non-database suites')
    $report.Add('')
    $testProjects = Get-ChildItem -Path (Join-Path $repositoryRoot 'src') -Recurse -File -Filter '*UnitTests.csproj' |
        Sort-Object -Property FullName
    foreach ($project in $testProjects) {
        $relative = $project.FullName.Replace($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, '')
        $result = & dotnet test $project.FullName --configuration $Configuration --no-build `
            --filter 'Category!=Database&Category!=Integration' 2>&1
        $summary = ($result | Select-String -Pattern 'Passed!\s+-|Failed!\s+-' | Select-Object -Last 1)
        $text = if ($null -eq $summary) { 'no summary line' } else { $summary.Line.Trim() }
        $status = if ($LASTEXITCODE -eq 0) { 'pass' } else { 'FAIL' }
        if ($LASTEXITCODE -ne 0) { $failures.Add("$relative failed") }
        $report.Add("- $relative : $status : $text")
    }
    $report.Add('')

    # 3. Permission grammar scan.
    $report.Add('## Permission grammar')
    $report.Add('')
    $parsePattern = 'PermissionIdentifier\.Parse\("([^"]+)"\)'
    $literals = Get-ChildItem -Path (Join-Path $repositoryRoot 'src') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|Migrations' } |
        Select-String -Pattern $parsePattern -AllMatches |
        ForEach-Object { $_.Matches } |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    $invalid = @($literals | Where-Object { ($_ -split '\.').Count -ne 3 })
    if ($invalid.Count -eq 0) {
        $report.Add("Every PermissionIdentifier.Parse literal uses three segments ($($literals.Count) capabilities): pass")
    }
    else {
        $report.Add("Capabilities outside the domain.resource.action grammar: $($invalid -join ', ')")
        $failures.Add('permission grammar violation')
    }
    $report.Add('')
    foreach ($literal in $literals) { $report.Add("- ``$literal``") }
    $report.Add('')

    # 4. Supply chain.
    $report.Add('## Supply chain')
    $report.Add('')
    $vulnerable = & dotnet list package --vulnerable --include-transitive 2>&1
    if ($LASTEXITCODE -ne 0) {
        $report.Add('The vulnerable-package scan could not complete, so the result is unknown rather than clean.')
        $report.Add('')
        foreach ($line in $vulnerable | Select-Object -Last 5) { $report.Add("    $line") }
    }
    else {
        $report.Add('```')
        foreach ($line in $vulnerable) { $report.Add($line) }
        $report.Add('```')
    }
    $report.Add('')

    # 5. What this harness cannot prove.
    $report.Add('## Not covered here')
    $report.Add('')
    $report.Add('- PostgreSQL integration, migration and concurrency evidence: `scripts/db-verify.ps1` needs the local stack.')
    $report.Add('- Performance, capacity and recovery figures: blocked by `P1-E04-D16` (capacity, latency, RTO and RPO).')
    $report.Add('- Trace propagation, statement handoff and restore/replay: need the live stack.')
    $report.Add('')

    $report.Add('## Result')
    $report.Add('')
    if ($failures.Count -eq 0) {
        $report.Add('Every database-free evidence step passed. The gaps above still block slice 15 acceptance.')
    }
    else {
        $report.Add('Failures:')
        foreach ($failure in $failures) { $report.Add("- $failure") }
    }

    if (-not (Test-Path $ReportDirectory)) { New-Item -ItemType Directory -Path $ReportDirectory | Out-Null }
    $reportPath = Join-Path $ReportDirectory ("phase1-evidence-{0}.md" -f $startedAt.ToString('yyyyMMddTHHmmssZ'))
    Set-Content -Path $reportPath -Value $report -Encoding utf8

    Write-Host "Evidence written to $reportPath"
    if ($failures.Count -eq 0) {
        Write-Host 'Database-free evidence: pass. Live-stack, performance and recovery evidence is still outstanding.'
        exit 0
    }

    Write-Host ("Database-free evidence: FAIL - {0}" -f ($failures -join '; '))
    exit 1
}
finally {
    Pop-Location
}

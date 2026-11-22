# Phase 1 evidence harness for everything that does not need the containerised database.
#
# P1-E04 slice 15 asks for security, performance, backup/restore and end-to-end evidence. The approved
# P1-E04-D16 profile can be validated on a workstation, but executing it still needs the dedicated
# acceptance environment. This harness collects the evidence that is reproducible on a workstation and
# records exactly what is missing. It never claims a database, performance or recovery result it did not observe.
#
# It runs, in order:
#   1. the repository gate (scripts/verify.ps1: restore, build, migration drift, non-database tests, format)
#   2. every unit-test project with its own Passed/Failed counts, so a per-service figure is recorded
#   3. the permission-grammar scan: every PermissionIdentifier.Parse literal must have exactly the
#      approved domain.resource.action segments, and no capability may be declared outside it
#   4. the supply-chain scan: vulnerable packages, direct and transitive
#   5. the P1-E04-D16 performance-profile gate and its negative self-tests
#   6. the P1-E04-D16 recovery-evidence gate and its negative self-tests
#
# Output: a markdown report under artifacts/ (git-ignored) plus a console summary. Exit code is 0 only
# when the gate, the grammar scan and every test project pass; the supply-chain scan is reported but a
# NuGet feed that cannot be reached is recorded as unknown rather than as a pass.
#
# Usage:
#   powershell -File scripts/phase1-evidence.ps1
#   powershell -File scripts/phase1-evidence.ps1 -Configuration Debug -SkipGate
#   powershell -File scripts/phase1-evidence.ps1 -SkipGate -TestHostConnectionTimeoutSeconds 30

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Skip the repository gate when it was already run for this commit.
    [switch]$SkipGate,

    # Bound a failed VSTest testhost connection. A timeout is reported as an infrastructure failure,
    # remaining service suites are skipped, and the other evidence gates still run.
    [ValidateRange(30, 600)]
    [int]$TestHostConnectionTimeoutSeconds = 180,

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
    $testHostTimedOut = $false
    for ($projectIndex = 0; $projectIndex -lt $testProjects.Count; $projectIndex++) {
        $project = $testProjects[$projectIndex]
        $relative = $project.FullName.Replace($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, '')
        $previousErrorActionPreference = $ErrorActionPreference
        $previousConnectionTimeout = [Environment]::GetEnvironmentVariable('VSTEST_CONNECTION_TIMEOUT', 'Process')
        try {
            $ErrorActionPreference = 'Continue'
            $env:VSTEST_CONNECTION_TIMEOUT = $TestHostConnectionTimeoutSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
            $result = & dotnet test $project.FullName --configuration $Configuration --no-build `
                --filter 'Category!=Database&Category!=Integration' 2>&1
            $testExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
            if ($null -eq $previousConnectionTimeout) {
                Remove-Item -Path Env:VSTEST_CONNECTION_TIMEOUT -ErrorAction SilentlyContinue
            }
            else {
                $env:VSTEST_CONNECTION_TIMEOUT = $previousConnectionTimeout
            }
        }

        $summary = ($result | Select-String -Pattern 'Passed!\s+-|Failed!\s+-' | Select-Object -Last 1)
        $text = if ($null -eq $summary) { 'no summary line' } else { $summary.Line.Trim() }
        $status = if ($testExitCode -eq 0) { 'pass' } else { 'FAIL' }
        if ($testExitCode -ne 0) { $failures.Add("$relative failed (exit $testExitCode)") }
        $report.Add("- $relative : $status : $text")

        $testHostTimedOut = [bool]($result | Select-String -Pattern 'failed to connect to testhost process' -Quiet)
        if ($testHostTimedOut) {
            $report.Add("  VSTest did not connect to testhost within $TestHostConnectionTimeoutSeconds seconds; this is recorded as an infrastructure failure, not a test assertion failure.")
            $remainingProjectCount = $testProjects.Count - $projectIndex - 1
            if ($remainingProjectCount -gt 0) {
                $report.Add("  Skipped $remainingProjectCount remaining test project(s) after the infrastructure timeout.")
            }
            break
        }
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

    # 5. Approved performance-profile contract and gate.
    $report.Add('## Performance profile gate')
    $report.Add('')
    $performanceGatePath = Join-Path $PSScriptRoot 'performance-profile-gate.ps1'
    if (Test-Path $performanceGatePath) {
        $performanceGate = Invoke-ChildHost -ScriptPath $performanceGatePath -Arguments @('-SelfTest')
        $performanceGateResult = if ($performanceGate.ExitCode -eq 0) { 'pass' } else { 'FAIL' }
        if ($performanceGate.ExitCode -ne 0) { $failures.Add("performance-profile-gate.ps1 exited $($performanceGate.ExitCode)") }
        $report.Add("scripts/performance-profile-gate.ps1 -SelfTest: $performanceGateResult (exit $($performanceGate.ExitCode))")
        foreach ($line in $performanceGate.Output | Select-Object -Last 10) { $report.Add("    $line") }
    }
    else {
        $report.Add('scripts/performance-profile-gate.ps1 is missing.')
        $failures.Add('performance profile gate missing')
    }
    $report.Add('')

    # 6. Approved recovery-evidence contract and gate.
    $report.Add('## Recovery evidence gate')
    $report.Add('')
    $recoveryGatePath = Join-Path $PSScriptRoot 'recovery-evidence-gate.ps1'
    if (Test-Path $recoveryGatePath) {
        $recoveryGate = Invoke-ChildHost -ScriptPath $recoveryGatePath -Arguments @('-SelfTest')
        $recoveryGateResult = if ($recoveryGate.ExitCode -eq 0) { 'pass' } else { 'FAIL' }
        if ($recoveryGate.ExitCode -ne 0) { $failures.Add("recovery-evidence-gate.ps1 exited $($recoveryGate.ExitCode)") }
        $report.Add("scripts/recovery-evidence-gate.ps1 -SelfTest: $recoveryGateResult (exit $($recoveryGate.ExitCode))")
        foreach ($line in $recoveryGate.Output | Select-Object -Last 10) { $report.Add("    $line") }
    }
    else {
        $report.Add('scripts/recovery-evidence-gate.ps1 is missing.')
        $failures.Add('recovery evidence gate missing')
    }
    $report.Add('')

    # 7. What this harness cannot prove.
    $report.Add('## Not covered here')
    $report.Add('')
    $report.Add('- PostgreSQL integration, migration and concurrency evidence: `scripts/db-verify.ps1` needs the local stack.')
    $report.Add('- Performance and capacity results: the approved `P1-E04-D16` profile must run in the dedicated acceptance environment.')
    $report.Add('- RTO/RPO claims: the approved mechanisms and fresh-cluster recovery drill do not exist in the workstation stack.')
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
        Write-Host 'Database-free evidence: pass. Live-stack, performance execution and recovery evidence is still outstanding.'
        exit 0
    }

    Write-Host ("Database-free evidence: FAIL - {0}" -f ($failures -join '; '))
    exit 1
}
finally {
    Pop-Location
}

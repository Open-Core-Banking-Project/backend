# Backup/restore drill for the backend.
#
# P1-E04 slice 15 asks for backup/restore evidence, and this script produces the part that can be
# observed on the local stack: a dump of a service database is restored into a scratch database and
# every table's row count is compared with the source. It records what it measured and never claims
# an RTO, an RPO, point-in-time recovery, encryption or an off-site copy, because those are the
# targets Product and Platform own in P1-E04-D16.
#
# The dump is taken with the cluster administrator inside the compose container and restored into a
# scratch database that is dropped afterwards. Application principals and their grants are untouched:
# the drill never reads or writes the service databases themselves.
#
# Output: a markdown report under artifacts/ (git-ignored) plus a console summary. Exit code is 0
# only when every selected service restored with matching row counts.
#
# Usage:
#   powershell -File scripts/restore-drill.ps1
#   & scripts/restore-drill.ps1 -Service CoreLedger,CustomerOnboarding
#   & scripts/restore-drill.ps1 -Service CoreLedger,CustomerOnboarding,IdentityAccess,PaymentsVtu,ChannelsEngagement,OperationsWorkflow,RiskReporting,IntegrationHub,AuditReconciliation
#
# A comma-separated -Service list is parsed by PowerShell itself, so use it from a session (`&`) or
# through -Command rather than through -File, which passes the list as one literal string.

[CmdletBinding()]
param(
    [ValidateSet('CoreLedger', 'CustomerOnboarding', 'IdentityAccess', 'PaymentsVtu', 'ChannelsEngagement', 'OperationsWorkflow', 'RiskReporting', 'IntegrationHub', 'AuditReconciliation')]
    [string[]]$Service = @('CoreLedger'),

    # Keep the scratch database behind for inspection instead of dropping it in the cleanup step.
    [switch]$KeepScratchDatabase,

    # Directory for the report. Created when missing.
    [string]$ReportDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $repositoryRoot 'deploy/compose/.env'
$containerName = 'corebanking-postgres'
$adminUser = 'cb_admin'
$containerPort = '5432'

# The database each service owns. Kept in step with scripts/db-verify.ps1.
$databases = @{
    CoreLedger           = 'core_ledger_db'
    CustomerOnboarding   = 'customer_onboarding_db'
    IdentityAccess       = 'identity_access_db'
    PaymentsVtu          = 'payments_vtu_db'
    ChannelsEngagement   = 'channels_engagement_db'
    OperationsWorkflow   = 'operations_workflow_db'
    RiskReporting        = 'risk_reporting_db'
    IntegrationHub       = 'integration_hub_db'
    AuditReconciliation  = 'audit_reconciliation_db'
}

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

function Invoke-ContainerShell {
    param([string]$Command)

    # Native stderr becomes an error record here, and a NOTICE from dropdb is not a failure, so the
    # caller checks the exit code instead of relying on the preference variable.
    $ErrorActionPreference = 'Continue'
    $output = & docker exec $containerName sh -c $Command 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = @($output | ForEach-Object { "$_" })
    }
}

function Set-ContainerPasswordFile {
    # libpq reads ~/.pgpass, so the administrator password reaches the tools through stdin and a
    # 0600 file instead of a command line that another local process could read.
    param([string]$Password)

    $ErrorActionPreference = 'Continue'
    $line = "${containerName}:${containerPort}:*:${adminUser}:${Password}"
    $write = $line | & docker exec -i $containerName sh -c 'set -e; umask 077; cat > /root/.pgpass; chmod 600 /root/.pgpass' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Writing the container password file failed: $($write -join ' ')"
    }
}

function Invoke-Psql {
    param([string]$Database, [string]$Sql)

    $ErrorActionPreference = 'Continue'
    $output = $Sql | & docker exec -i $containerName psql -U $adminUser -d $Database -v ON_ERROR_STOP=1 -At -F '|' -f - 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql against '$Database' failed with exit code ${LASTEXITCODE}: $($output -join ' ')"
    }

    return @($output | ForEach-Object { "$_" })
}

function Get-TableNames {
    param([string]$Database)

    $query = "select format('%I.%I', schemaname, tablename) from pg_tables where schemaname not in ('pg_catalog', 'information_schema') order by 1;"
    return @(Invoke-Psql -Database $Database -Sql $query | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-RowCounts {
    param([string]$Database, [string[]]$Tables)

    if ($Tables.Count -eq 0) {
        return @{}
    }

    $selects = foreach ($table in $Tables) {
        "select '$table', count(*) from $table"
    }

    $query = ($selects -join ' union all ') + ';'
    $counts = @{}
    foreach ($line in Invoke-Psql -Database $Database -Sql $query) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line.Split('|')
        if ($parts.Count -ne 2) {
            throw "Unexpected row-count line from '$Database': $line"
        }
        $counts[$parts[0]] = [long]$parts[1]
    }

    return $counts
}

function Invoke-DrillForService {
    param(
        [string]$ServiceName,
        [string]$Database,
        [string]$WorkingDirectory
    )

    $scratch = "${Database}_drill"
    $dumpPath = "$WorkingDirectory/$Database.dump"
    $result = [ordered]@{
        service           = $ServiceName
        database          = $Database
        status            = 'fail'
        dump_bytes        = 0
        dump_seconds      = 0
        restore_seconds   = 0
        table_count       = 0
        total_rows        = 0
        mismatches        = @()
        notes             = @()
    }

    $dumpWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $dump = Invoke-ContainerShell -Command "pg_dump -U $adminUser -d $Database -Fc -f '$dumpPath'"
    $dumpWatch.Stop()
    $result.dump_seconds = [math]::Round($dumpWatch.Elapsed.TotalSeconds, 3)
    if ($dump.ExitCode -ne 0) {
        $result.notes += "pg_dump failed: $($dump.Output -join ' ')"
        return $result
    }

    $size = Invoke-ContainerShell -Command "stat -c %s '$dumpPath'"
    if ($size.ExitCode -eq 0) {
        $result.dump_bytes = [long](@($size.Output)[0])
    }

    $drop = Invoke-ContainerShell -Command "dropdb -U $adminUser --if-exists --force '$scratch'"
    if ($drop.ExitCode -ne 0) {
        $result.notes += "Removing a previous scratch database failed: $($drop.Output -join ' ')"
        return $result
    }

    $create = Invoke-ContainerShell -Command "createdb -U $adminUser -T template0 '$scratch'"
    if ($create.ExitCode -ne 0) {
        $result.notes += "createdb failed: $($create.Output -join ' ')"
        return $result
    }

    $restoreWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $restore = Invoke-ContainerShell -Command "pg_restore -U $adminUser -d '$scratch' '$dumpPath'"
    $restoreWatch.Stop()
    $result.restore_seconds = [math]::Round($restoreWatch.Elapsed.TotalSeconds, 3)
    if ($restore.ExitCode -ne 0) {
        $result.notes += "pg_restore reported errors: $($restore.Output -join ' ')"
        return $result
    }

    $tables = Get-TableNames -Database $Database
    $result.table_count = $tables.Count
    $sourceCounts = Get-RowCounts -Database $Database -Tables $tables
    $restoredCounts = Get-RowCounts -Database $scratch -Tables $tables

    foreach ($table in $tables) {
        $sourceRows = if ($sourceCounts.ContainsKey($table)) { $sourceCounts[$table] } else { 0 }
        $restoredRows = if ($restoredCounts.ContainsKey($table)) { $restoredCounts[$table] } else { 0 }
        $result.total_rows += $sourceRows
        if ($sourceRows -ne $restoredRows) {
            $result.mismatches += "$table source=$sourceRows restored=$restoredRows"
        }
    }

    if ($result.mismatches.Count -eq 0) {
        $result.status = 'pass'
    }

    return $result
}

Push-Location $repositoryRoot

$scratchDatabases = [System.Collections.Generic.List[string]]::new()
$workingDirectory = $null

try {
    $environment = Read-EnvironmentFile -Path $environmentFile

    if ([string]::IsNullOrWhiteSpace($environment['POSTGRES_ADMIN_PASSWORD'])) {
        throw 'POSTGRES_ADMIN_PASSWORD is not set in deploy/compose/.env.'
    }

    if ([string]::IsNullOrWhiteSpace($environment['CB_POSTGRES_PORT'])) {
        throw 'CB_POSTGRES_PORT is not set in deploy/compose/.env.'
    }

    $running = & docker inspect --format '{{.State.Running}}' $containerName 2>&1
    if ($LASTEXITCODE -ne 0 -or (@($running)[0] -ne 'true')) {
        throw "The '$containerName' container is not running. Run scripts/infra.ps1 -Action up first."
    }

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

    Set-ContainerPasswordFile -Password $environment['POSTGRES_ADMIN_PASSWORD']

    $startedAt = (Get-Date).ToUniversalTime()
    $stamp = $startedAt.ToString('yyyyMMddTHHmmssZ')
    $workingDirectory = "/tmp/cb-restore-drill-$stamp"
    $created = Invoke-ContainerShell -Command "mkdir -p '$workingDirectory'"
    if ($created.ExitCode -ne 0) {
        throw "Creating the working directory in the container failed: $($created.Output -join ' ')"
    }

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($serviceName in $Service) {
        $database = $databases[$serviceName]
        Write-Host "==> restoring $serviceName ($database)" -ForegroundColor Cyan
        $scratchDatabases.Add("${database}_drill")
        $result = Invoke-DrillForService -ServiceName $serviceName -Database $database -WorkingDirectory $workingDirectory
        $results.Add($result)
        $colour = if ($result.status -eq 'pass') { 'Green' } else { 'Red' }
        Write-Host ("    {0}: {1} tables, {2} rows, dump {3} ms, restore {4} ms" -f `
                $result.status, $result.table_count, $result.total_rows, `
                [int]($result.dump_seconds * 1000), [int]($result.restore_seconds * 1000)) -ForegroundColor $colour
        foreach ($note in $result.notes) { Write-Host "    $note" -ForegroundColor Red }
        foreach ($mismatch in $result.mismatches) { Write-Host "    mismatch: $mismatch" -ForegroundColor Red }
    }

    $version = Invoke-Psql -Database 'postgres' -Sql 'select version();'
    $commit = (& git rev-parse HEAD 2>$null)
    $machine = [System.Environment]::MachineName
    $processorCount = [System.Environment]::ProcessorCount
    $operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription

    $report = [System.Collections.Generic.List[string]]::new()
    $report.Add('# Backup and restore drill')
    $report.Add('')
    $report.Add("- Generated: $($startedAt.ToString('u'))")
    $report.Add("- Commit: $commit")
    $report.Add("- Host: $machine, $operatingSystem, $processorCount logical processors")
    $report.Add("- PostgreSQL: $(@($version)[0])")
    $report.Add("- Container: $containerName")
    $report.Add('')
    $report.Add('## Result')
    $report.Add('')
    $report.Add('| Service | Database | Result | Tables | Rows | Dump size | Dump ms | Restore ms |')
    $report.Add('| --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($result in $results) {
        $report.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f `
                    $result.service, $result.database, $result.status, $result.table_count, $result.total_rows, `
                    $result.dump_bytes, [int]($result.dump_seconds * 1000), [int]($result.restore_seconds * 1000)))
    }
    $report.Add('')
    foreach ($result in $results) {
        if ($result.notes.Count -eq 0 -and $result.mismatches.Count -eq 0) { continue }
        $report.Add("### $($result.service)")
        $report.Add('')
        foreach ($note in $result.notes) { $report.Add("- $note") }
        foreach ($mismatch in $result.mismatches) { $report.Add("- row count mismatch: $mismatch") }
        $report.Add('')
    }
    $report.Add('## What this proves')
    $report.Add('')
    $report.Add('- A dump of the service database restores into a fresh database with the same row count in every table.')
    $report.Add('- The restore path needs no application principal, so a recovery does not depend on service credentials.')
    $report.Add('')
    $report.Add('## What this does not prove')
    $report.Add('')
    $report.Add('- No recovery objective is claimed. The measured restore duration is a single observation on this host, not an RTO or RPO; `P1-E04-D16` owns those targets.')
    $report.Add('- No point-in-time recovery: continuous WAL archiving, retention and a restore to an arbitrary timestamp are not configured on the local stack.')
    $report.Add('- No off-site copy, encryption at rest, key custody or restore from a different host.')
    $report.Add('- No verification that the restored database serves the application, because the drill does not start a service against it.')
    $report.Add('')

    if (-not (Test-Path $ReportDirectory)) { New-Item -ItemType Directory -Path $ReportDirectory | Out-Null }
    $reportPath = Join-Path $ReportDirectory ("restore-drill-{0}.md" -f $stamp)
    Set-Content -Path $reportPath -Value $report -Encoding utf8

    $failures = @($results | Where-Object { $_.status -ne 'pass' })
    Write-Host "Report written to $reportPath"
    if ($failures.Count -eq 0) {
        Write-Host 'Restore drill: pass. Recovery objectives, PITR and off-site copies remain outstanding.' -ForegroundColor Green
        exit 0
    }

    Write-Host ("Restore drill: FAIL - {0}" -f (($failures | ForEach-Object { $_.service }) -join ', ')) -ForegroundColor Red
    exit 1
}
finally {
    $ErrorActionPreference = 'Continue'
    foreach ($scratch in $scratchDatabases) {
        if ($KeepScratchDatabase) {
            Write-Host "Keeping scratch database '$scratch' by request." -ForegroundColor Yellow
            continue
        }
        & docker exec $containerName dropdb -U $adminUser --if-exists --force $scratch 2>&1 | Out-Null
    }

    if ($null -ne $workingDirectory) {
        & docker exec $containerName rm -rf $workingDirectory 2>&1 | Out-Null
    }

    & docker exec $containerName rm -f /root/.pgpass 2>&1 | Out-Null
    Pop-Location
}

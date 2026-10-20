# Verifies the local development infrastructure against the Phase 0 epic 02 list.
#
# Usage:
#   powershell -File scripts/infra-verify.ps1
#
# Checks performed:
#   1. every container reports healthy and every published port is bound to loopback
#   2. each service application principal connects to its own database over TCP
#   3. cross-service access is denied for every application principal
#   4. the broker can round-trip a message and retain it across restart
#   5. Redis answers, and persistence is deliberately disabled
#   6. the collector accepts an OTLP trace and emits it
#
# Exits non-zero if any check fails. Credentials are never printed.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeDirectory = Join-Path $repositoryRoot 'deploy/compose'
$environmentFile = Join-Path $composeDirectory '.env'

$services = @(
    @{ Service = 'coreledger'; Database = 'core_ledger_db' }
    @{ Service = 'customeronboarding'; Database = 'customer_onboarding_db' }
    @{ Service = 'identityaccess'; Database = 'identity_access_db' }
    @{ Service = 'paymentsvtu'; Database = 'payments_vtu_db' }
    @{ Service = 'channelsengagement'; Database = 'channels_engagement_db' }
    @{ Service = 'operationsworkflow'; Database = 'operations_workflow_db' }
    @{ Service = 'riskreporting'; Database = 'risk_reporting_db' }
    @{ Service = 'integrationhub'; Database = 'integration_hub_db' }
    @{ Service = 'auditreconciliation'; Database = 'audit_reconciliation_db' }
)

$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Read-EnvironmentValue {
    param([string]$Name)

    $line = Select-String -Path $environmentFile -Pattern "^$Name=(.+)$" | Select-Object -First 1
    if (-not $line) {
        throw "Missing $Name in deploy/compose/.env."
    }

    return $line.Matches[0].Groups[1].Value.Trim()
}

function Read-EnvironmentValueOrDefault {
    param([string]$Name, [string]$Default)

    if (-not (Test-Path $environmentFile)) {
        return $Default
    }

    $line = Select-String -Path $environmentFile -Pattern "^$Name=(.+)$" | Select-Object -First 1
    if (-not $line) {
        return $Default
    }

    $value = $line.Matches[0].Groups[1].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Invoke-External {
    # Native commands that are expected to fail (a denied connection, for example)
    # write to stderr. Under $ErrorActionPreference = 'Stop' that would terminate the
    # script, so the preference is relaxed for the duration of the call and the exit
    # code is returned to the caller instead.
    param([Parameter(Mandatory = $true)][scriptblock]$Command)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command 2>&1
        return @{
            Output   = (($output | ForEach-Object { $_.ToString() }) -join "`n").Trim()
            ExitCode = $LASTEXITCODE
        }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Add-Result {
    param([string]$Name, [bool]$Passed, [string]$Detail)

    $script:checks++
    if ($Passed) {
        Write-Host ("  PASS  {0,-58} {1}" -f $Name, $Detail) -ForegroundColor Green
    }
    else {
        Write-Host ("  FAIL  {0,-58} {1}" -f $Name, $Detail) -ForegroundColor Red
        $script:failures.Add("$Name - $Detail")
    }
}

Write-Host 'Local infrastructure verification' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $environmentFile)) {
    throw 'deploy/compose/.env is missing. Run scripts/infra.ps1 -Action up first.'
}

Write-Host '1. Container health and port exposure' -ForegroundColor Cyan
$shellScripts = @(Get-ChildItem (Join-Path $repositoryRoot 'deploy/compose') -Recurse -Filter *.sh -ErrorAction SilentlyContinue)
$scriptsWithCarriageReturn = @($shellScripts | Where-Object { [System.IO.File]::ReadAllText($_.FullName).Contains("`r") })
Add-Result 'mounted shell scripts use LF line endings' ($scriptsWithCarriageReturn.Count -eq 0) (($scriptsWithCarriageReturn | ForEach-Object { $_.Name }) -join ', ')

$psLines = @(docker compose --project-directory $composeDirectory ps --format '{{.Name}}|{{.State}}|{{.Health}}')
$notRunning = @($psLines | Where-Object { ($_ -split '\|')[1] -ne 'running' })
$notHealthy = @($psLines | Where-Object { $health = ($_ -split '\|')[2]; $health -and $health -ne 'healthy' })
$withoutHealthcheck = @($psLines | Where-Object { -not ($_ -split '\|')[2] } | ForEach-Object { ($_ -split '\|')[0] })
Add-Result 'all containers running' ($notRunning.Count -eq 0) "$($psLines.Count) containers; without a healthcheck: $($withoutHealthcheck -join ', ')"
Add-Result 'all health-checked containers healthy' ($notHealthy.Count -eq 0) ($notHealthy -join '; ')

$portLines = @(docker compose --project-directory $composeDirectory ps --format '{{.Name}} {{.Ports}}')
$exposed = @($portLines | Where-Object { $_ -match '0\.0\.0\.0:' -or $_ -match '\[::\]:' })
Add-Result 'published ports bound to loopback only' ($exposed.Count -eq 0) (($exposed -join '; '))

Write-Host ''
Write-Host '2. Each application principal connects to its own database' -ForegroundColor Cyan
foreach ($entry in $services) {
    $passwordName = "$($entry.Service.ToUpper())_APP_PASSWORD"
    $password = Read-EnvironmentValue $passwordName

    $result = Invoke-External { docker exec -e "PGPASSWORD=$password" corebanking-postgres psql --host 127.0.0.1 --username "$($entry.Service)_app" --dbname $entry.Database --tuples-only --no-align -c 'SELECT current_database();' }
    $ok = ($result.ExitCode -eq 0) -and ($result.Output -eq $entry.Database)
    Add-Result "$($entry.Service)_app -> $($entry.Database)" $ok $result.Output
}

Write-Host ''
Write-Host '3. Cross-service access is denied' -ForegroundColor Cyan
$targetIndex = 0
foreach ($entry in $services) {
    $passwordName = "$($entry.Service.ToUpper())_APP_PASSWORD"
    $password = Read-EnvironmentValue $passwordName

    # Target the next database in the list, which belongs to another service.
    $other = $services[($targetIndex + 1) % $services.Count]
    $targetIndex++

    $result = Invoke-External { docker exec -e "PGPASSWORD=$password" corebanking-postgres psql --host 127.0.0.1 --username "$($entry.Service)_app" --dbname $other.Database --tuples-only --no-align -c 'SELECT 1;' }
    $denied = ($result.ExitCode -ne 0) -and ($result.Output -match 'permission denied')
    Add-Result "$($entry.Service)_app denied on $($other.Database)" $denied ($result.Output -split "`n" | Select-Object -First 1)
}

Write-Host ''
Write-Host '4. Broker publish and consume round trip' -ForegroundColor Cyan
$topic = 'corebanking.smoke.v1'
Invoke-External { docker exec corebanking-broker /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --if-not-exists --topic $topic --partitions 1 --replication-factor 1 } | Out-Null
$listed = Invoke-External { docker exec corebanking-broker /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list }
Add-Result 'topic created and listed' ($listed.Output -match [regex]::Escape($topic)) $topic

$payload = "smoke-1`nsmoke-2`nsmoke-3`n"
Invoke-External { $payload | docker exec -i corebanking-broker /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server localhost:9092 --topic $topic } | Out-Null
$consumed = Invoke-External { docker exec corebanking-broker /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 --topic $topic --from-beginning --max-messages 3 }
$received = @($consumed.Output -split "`n" | Where-Object { $_ -match '^smoke-' })
Add-Result 'three messages published and consumed' ($received.Count -eq 3) ("received " + $received.Count)

$restartSuffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$restartTopic = "corebanking.smoke.restart-$restartSuffix.v1"
$restartPayload = "restart-smoke-$restartSuffix"
Invoke-External { docker exec corebanking-broker /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --topic $restartTopic --partitions 1 --replication-factor 1 } | Out-Null
Invoke-External { $restartPayload | docker exec -i corebanking-broker /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server localhost:9092 --topic $restartTopic } | Out-Null

$restart = Invoke-External { docker restart corebanking-broker }
$brokerHealthy = $false
if ($restart.ExitCode -eq 0) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $health = Invoke-External { docker inspect --format '{{.State.Health.Status}}' corebanking-broker }
        if ($health.ExitCode -eq 0 -and $health.Output -eq 'healthy') {
            $brokerHealthy = $true
            break
        }

        Start-Sleep -Seconds 2
    }
}

if ($brokerHealthy) {
    $afterRestart = Invoke-External { docker exec corebanking-broker /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 --topic $restartTopic --from-beginning --max-messages 1 }
    Add-Result 'committed broker message survives restart' ($afterRestart.Output -match [regex]::Escape($restartPayload)) $restartTopic
}
else {
    Add-Result 'committed broker message survives restart' $false 'broker did not return healthy within 60 seconds'
}

Write-Host ''
Write-Host '5. Cache is available and explicitly non-durable' -ForegroundColor Cyan
$ping = Invoke-External { docker exec corebanking-cache redis-cli ping }
Add-Result 'redis responds to PING' ($ping.Output.Trim() -eq 'PONG') $ping.Output.Trim()

$appendonly = (Invoke-External { docker exec corebanking-cache redis-cli config get appendonly }).Output
$save = (Invoke-External { docker exec corebanking-cache redis-cli config get save }).Output
Add-Result 'redis append-only file disabled' ($appendonly -match '(?m)^no$') 'appendonly=no'
Add-Result 'redis snapshot rules empty' ($save -match '(?m)^save$') 'save=""'

Write-Host ''
Write-Host '6. Collector accepts and emits OTLP telemetry' -ForegroundColor Cyan
$spanName = 'corebanking-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$traceId = ([guid]::NewGuid().ToString('N')) + ([guid]::NewGuid().ToString('N')).Substring(0, 0)
$traceId = $traceId.Substring(0, 32)
$spanId = [guid]::NewGuid().ToString('N').Substring(0, 16)
$start = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() * 1000000

$payloadJson = @{
    resourceSpans = @(
        @{
            resource   = @{ attributes = @(@{ key = 'service.name'; value = @{ stringValue = 'corebanking.smoke' } }) }
            scopeSpans = @(
                @{
                    scope = @{ name = 'smoke' }
                    spans = @(
                        @{
                            traceId           = $traceId
                            spanId            = $spanId
                            name              = $spanName
                            kind              = 1
                            startTimeUnixNano = "$start"
                            endTimeUnixNano   = "$($start + 1000000)"
                        }
                    )
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $otlpHttpPort = Read-EnvironmentValueOrDefault 'CB_OTLP_HTTP_PORT' '4318'
    Invoke-RestMethod -Uri "http://127.0.0.1:$otlpHttpPort/v1/traces" -Method Post -ContentType 'application/json' -Body $payloadJson -TimeoutSec 20 | Out-Null
    Add-Result 'collector accepted the OTLP trace' $true $spanName
}
catch {
    Add-Result 'collector accepted the OTLP trace' $false $_.Exception.Message
}

Start-Sleep -Seconds 4
$logs = (Invoke-External { docker logs corebanking-telemetry --since 5m }).Output
Add-Result 'collector emitted the span name' ($logs -match [regex]::Escape($spanName)) $spanName

Write-Host ''
Write-Host '7. Principal privilege model' -ForegroundColor Cyan
$adminPassword = Read-EnvironmentValue 'POSTGRES_ADMIN_PASSWORD'
$migratorPassword = Read-EnvironmentValue 'CORELEDGER_MIGRATOR_PASSWORD'
$appPassword = Read-EnvironmentValue 'CORELEDGER_APP_PASSWORD'
$smokeTable = 'ledger_smoke_check'

$create = Invoke-External { docker exec -e "PGPASSWORD=$migratorPassword" corebanking-postgres psql --host 127.0.0.1 --username coreledger_migrator --dbname core_ledger_db -c "CREATE TABLE IF NOT EXISTS public.$smokeTable (id integer PRIMARY KEY, note text NOT NULL);" }
Add-Result 'migrator may create schema objects in its own database' ($create.ExitCode -eq 0) 'CREATE TABLE core_ledger_db'

$insert = Invoke-External { docker exec -e "PGPASSWORD=$appPassword" corebanking-postgres psql --host 127.0.0.1 --username coreledger_app --dbname core_ledger_db -c "INSERT INTO public.$smokeTable (id, note) VALUES (1, 'smoke') ON CONFLICT (id) DO NOTHING;" }
Add-Result 'application principal may write data in its own database' ($insert.ExitCode -eq 0) 'INSERT ledger_smoke_check'

$deniedDdl = Invoke-External { docker exec -e "PGPASSWORD=$appPassword" corebanking-postgres psql --host 127.0.0.1 --username coreledger_app --dbname core_ledger_db -c 'CREATE TABLE public.should_not_exist (id integer);' }
Add-Result 'application principal may not change schema' (($deniedDdl.ExitCode -ne 0) -and ($deniedDdl.Output -match 'permission denied')) 'CREATE TABLE denied'

$migratorCross = Invoke-External { docker exec -e "PGPASSWORD=$migratorPassword" corebanking-postgres psql --host 127.0.0.1 --username coreledger_migrator --dbname payments_vtu_db -c 'SELECT 1;' }
Add-Result 'migrator is denied on another service database' (($migratorCross.ExitCode -ne 0) -and ($migratorCross.Output -match 'permission denied')) 'coreledger_migrator on payments_vtu_db'

Write-Host ''
Write-Host '8. Backup and restore round trip' -ForegroundColor Cyan
$scratchDatabase = 'smoke_restore_check'
$dumpInContainer = '/tmp/infra-verify.dump'
$dumpOnHost = Join-Path ([System.IO.Path]::GetTempPath()) 'infra-verify.dump'

Invoke-External { docker exec -e "PGPASSWORD=$adminPassword" corebanking-postgres pg_dump --format=custom --file=$dumpInContainer --username cb_admin --dbname core_ledger_db } | Out-Null
$copyOut = Invoke-External { docker cp "corebanking-postgres:$dumpInContainer" $dumpOnHost }
$dumpSize = 0
if (Test-Path $dumpOnHost) { $dumpSize = (Get-Item $dumpOnHost).Length }
Add-Result 'backup of core_ledger_db produced a non-empty archive' (($copyOut.ExitCode -eq 0) -and ($dumpSize -gt 0)) "$dumpSize bytes"

Invoke-External { docker exec -e "PGPASSWORD=$adminPassword" corebanking-postgres psql --host 127.0.0.1 --username cb_admin --dbname postgres -c "DROP DATABASE IF EXISTS $scratchDatabase;" } | Out-Null
Invoke-External { docker exec -e "PGPASSWORD=$adminPassword" corebanking-postgres psql --host 127.0.0.1 --username cb_admin --dbname postgres -c "CREATE DATABASE $scratchDatabase;" } | Out-Null
Invoke-External { docker cp $dumpOnHost "corebanking-postgres:$dumpInContainer" } | Out-Null
$restore = Invoke-External { docker exec -e "PGPASSWORD=$adminPassword" corebanking-postgres pg_restore --dbname $scratchDatabase --username cb_admin --host 127.0.0.1 --no-owner --no-privileges $dumpInContainer }
Add-Result 'restore into a scratch database succeeded' ($restore.ExitCode -eq 0) ($restore.Output -split "`n" | Select-Object -First 1)

$rowCount = (Invoke-External { docker exec -e "PGPASSWORD=$adminPassword" corebanking-postgres psql --tuples-only --no-align --host 127.0.0.1 --username cb_admin --dbname $scratchDatabase -c "SELECT count(*) FROM public.$smokeTable;" }).Output
Add-Result 'restored data matches the source' ($rowCount -eq '1') "rows=$rowCount"

Invoke-External { docker exec -e "PGPASSWORD=$adminPassword" corebanking-postgres psql --host 127.0.0.1 --username cb_admin --dbname postgres -c "DROP DATABASE $scratchDatabase;" } | Out-Null
Invoke-External { docker exec -e "PGPASSWORD=$migratorPassword" corebanking-postgres psql --host 127.0.0.1 --username coreledger_migrator --dbname core_ledger_db -c "DROP TABLE IF EXISTS public.$smokeTable;" } | Out-Null
Remove-Item $dumpOnHost -Force -ErrorAction SilentlyContinue

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host "All $checks checks passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($failures.Count) of $checks checks failed:" -ForegroundColor Red
foreach ($failure in $failures) {
    Write-Host "  - $failure" -ForegroundColor Red
}

exit 1

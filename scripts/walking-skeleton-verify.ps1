# Verifies the complete Phase 0 reference flow against the local infrastructure stack.
# Prerequisites:
#   powershell -File scripts/infra.ps1 -Action up
#   powershell -File scripts/db-verify.ps1 -Configuration Release
#
# The verifier seeds one synthetic development administrator, launches the Identity Access API,
# Identity Access outbox worker and Audit Reconciliation consumer, then proves that one command:
#   - commits source state and an outbox event;
#   - is published and consumed into append-only audit evidence;
#   - carries one W3C trace through the API, publisher and consumer; and
#   - does not export a sensitive request-body canary or raw URL/SQL tags.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(1024, 65535)]
    [int]$ApiPort = 5085
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $repositoryRoot 'deploy/compose/.env'
$artifactDirectory = Join-Path $repositoryRoot ('artifacts/walking-skeleton/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$processes = New-Object System.Collections.Generic.List[System.Diagnostics.Process]
$environmentNames = @(
    'ASPNETCORE_URLS',
    'ConnectionStrings__IdentityAccess',
    'ConnectionStrings__AuditReconciliation',
    'DOTNET_ENVIRONMENT',
    'Messaging__Kafka__BootstrapServers',
    'OTEL_EXPORTER_OTLP_ENDPOINT',
    'OTEL_EXPORTER_OTLP_PROTOCOL'
)
$previousEnvironment = @{}

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

function Require-EnvironmentValue {
    param([hashtable]$Values, [string]$Name)

    $value = $Values[$Name]
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is missing from deploy/compose/.env."
    }

    return $value
}

function Invoke-DatabaseScalar {
    param(
        [string]$Database,
        [string]$Username,
        [string]$Password,
        [string]$Sql
    )

    $output = docker exec -e "PGPASSWORD=$Password" corebanking-postgres psql `
        --host 127.0.0.1 --username $Username --dbname $Database `
        --set ON_ERROR_STOP=1 --tuples-only --no-align --command $Sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Database command failed for $Database as $Username."
    }

    return (($output | ForEach-Object { $_.ToString() }) -join "`n").Trim()
}

function Read-CollectorLogs {
    param([DateTimeOffset]$Since)

    # Docker writes container logs to stderr even when the command succeeds. Windows PowerShell
    # promotes that stream to an error record under Stop, so relax the preference for this call.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = docker logs corebanking-telemetry --since $Since.ToString('o') 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to read the local telemetry collector output.'
        }

        return (($output | ForEach-Object { $_.ToString() }) -join "`n")
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Start-ServiceHost {
    param([string]$Name, [string]$Project)

    $standardOutput = Join-Path $artifactDirectory "$Name.stdout.log"
    $standardError = Join-Path $artifactDirectory "$Name.stderr.log"
    $start = @{
        FilePath               = 'dotnet'
        ArgumentList           = @('run', '--project', $Project, '--no-build', '--no-launch-profile', '--configuration', $Configuration)
        PassThru               = $true
        RedirectStandardOutput = $standardOutput
        RedirectStandardError  = $standardError
    }

    if ($env:OS -eq 'Windows_NT') {
        $start['WindowStyle'] = 'Hidden'
    }
    else {
        $start['NoNewWindow'] = $true
    }

    $process = Start-Process @start
    $script:processes.Add($process)
    return $process
}

function Wait-ForApi {
    param([System.Diagnostics.Process]$Process, [string]$Uri)

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($Process.HasExited) {
            throw "Identity Access API exited before becoming ready. See $artifactDirectory."
        }

        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    throw "Identity Access API did not become ready at $Uri within 60 seconds."
}

function Wait-ForValue {
    param([scriptblock]$Read, [string]$Expected, [string]$Description)

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $actual = & $Read
        if ($actual -eq $Expected) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "$Description did not reach '$Expected' within 60 seconds."
}

function Assert-TraceEvidence {
    param([string]$Logs, [string]$TraceId, [string]$CorrelationId, [string]$SensitiveCanary)

    $traceLines = @($Logs -split "`r?`n" | Where-Object { $_ -match [regex]::Escape($TraceId) })
    foreach ($spanName in @('POST /api/v1/identity/foundation-records', 'messaging.publish', 'messaging.process')) {
        if (-not ($traceLines | Where-Object { $_ -match [regex]::Escape($spanName) })) {
            throw "Collector output does not contain '$spanName' on trace $TraceId."
        }
    }

    $requiredServices = @(
        'corebanking.identity-access.api',
        'corebanking.identity-access.worker',
        'corebanking.audit-reconciliation.worker'
    )
    $resourceSections = [regex]::Split($Logs, 'ResourceTraces #\d+')
    foreach ($serviceName in $requiredServices) {
        $found = $resourceSections | Where-Object {
            ($_ -match "service\.name=$([regex]::Escape($serviceName))") `
                -and ($_ -match [regex]::Escape($TraceId))
        }

        if (-not $found) {
            throw "Collector output does not associate trace $TraceId with service $serviceName."
        }
    }

    if (-not ($traceLines | Where-Object { $_ -match [regex]::Escape("correlation.id=$CorrelationId") })) {
        throw "The publisher span does not carry correlation ID $CorrelationId."
    }

    foreach ($prohibited in @($SensitiveCanary, 'url.full=', 'url.path=', 'url.query=', 'db.statement=', 'db.query.text=')) {
        if ($Logs -match [regex]::Escape($prohibited)) {
            throw "Collector output contains prohibited telemetry value '$prohibited'."
        }
    }
}

Push-Location $repositoryRoot
try {
    $environment = Read-EnvironmentFile -Path $environmentFile
    $postgresPort = Require-EnvironmentValue -Values $environment -Name 'CB_POSTGRES_PORT'
    $brokerPort = Require-EnvironmentValue -Values $environment -Name 'CB_BROKER_PORT'
    $identityPassword = Require-EnvironmentValue -Values $environment -Name 'IDENTITYACCESS_APP_PASSWORD'
    $auditPassword = Require-EnvironmentValue -Values $environment -Name 'AUDITRECONCILIATION_APP_PASSWORD'

    foreach ($name in $environmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

    Write-Host '==> build walking-skeleton hosts' -ForegroundColor Cyan
    dotnet build --no-restore --configuration $Configuration --maxcpucount:1
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    $institutionId = 'phase0-walking-skeleton'
    $actorId = 'phase0-admin'
    $roleId = 'phase0-admin-role'
    $seedSql = @"
insert into identity.identities
    (institution_id, identity_id, actor_type, display_name, status, created_at, updated_at)
values
    ('$institutionId', '$actorId', 'Human', 'Phase 0 Administrator', 'Active', now(), now())
on conflict (institution_id, identity_id) do update
set status = 'Active', updated_at = now();

insert into access.roles
    (institution_id, role_id, role_code, role_name, version, status, created_at, updated_at)
values
    ('$institutionId', '$roleId', 'phase0-administrator', 'Phase 0 Administrator', 1, 'Active', now(), now())
on conflict (institution_id, role_id) do update
set status = 'Active', updated_at = now();

insert into access.role_permissions (institution_id, role_id, permission_code)
values ('$institutionId', '$roleId', 'platform.foundation.execute')
on conflict do nothing;

insert into access.identity_roles
    (institution_id, identity_id, role_id, region_id, branch_id, channel_id, valid_from, valid_to, assigned_by)
values
    ('$institutionId', '$actorId', '$roleId', null, null, null, now() - interval '1 minute', null, '$actorId')
on conflict (institution_id, identity_id, role_id) do update
set valid_from = excluded.valid_from, valid_to = null;

insert into access.entitlement_versions (institution_id, identity_id, version, updated_at)
values ('$institutionId', '$actorId', 1, now())
on conflict (institution_id, identity_id) do update
set version = 1, updated_at = now();
"@
    Invoke-DatabaseScalar -Database 'identity_access_db' -Username 'identityaccess_app' -Password $identityPassword -Sql $seedSql | Out-Null

    $env:ConnectionStrings__IdentityAccess = "Host=127.0.0.1;Port=$postgresPort;Database=identity_access_db;Username=identityaccess_app;Password=$identityPassword"
    $env:ConnectionStrings__AuditReconciliation = "Host=127.0.0.1;Port=$postgresPort;Database=audit_reconciliation_db;Username=auditreconciliation_app;Password=$auditPassword"
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://127.0.0.1:4317'
    $env:OTEL_EXPORTER_OTLP_PROTOCOL = 'grpc'
    $env:Messaging__Kafka__BootstrapServers = "127.0.0.1:$brokerPort"
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"

    $collectorSince = [DateTimeOffset]::UtcNow
    $api = Start-ServiceHost -Name 'identity-api' -Project 'src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Api'
    Start-ServiceHost -Name 'identity-worker' -Project 'src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Workers' | Out-Null
    Start-ServiceHost -Name 'audit-worker' -Project 'src/Services/AuditReconciliation/src/CoreBanking.AuditReconciliation.Workers' | Out-Null

    Wait-ForApi -Process $api -Uri "http://127.0.0.1:$ApiPort/health/ready"

    $suffix = [guid]::NewGuid().ToString('N')
    $traceId = ([guid]::NewGuid().ToString('N')).ToLowerInvariant()
    $parentSpanId = ([guid]::NewGuid().ToString('N').Substring(0, 16)).ToLowerInvariant()
    $correlationId = "phase0-correlation-$suffix"
    $sensitiveCanary = "phase0-sensitive-$suffix"
    $claims = @{
        actorId               = $actorId
        actorType             = 'Human'
        institutionId         = $institutionId
        sessionId             = "phase0-session-$suffix"
        deviceId              = "phase0-device-$suffix"
        entitlementVersion    = 1
        authenticationStrength = 'MultiFactor'
    } | ConvertTo-Json -Compress
    $actorMaterial = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($claims))
    $headers = @{
        'X-Development-Actor' = $actorMaterial
        'X-Institution-Id'    = $institutionId
        'X-Correlation-Id'    = $correlationId
        'Idempotency-Key'     = "phase0-idempotency-$suffix"
        'traceparent'         = "00-$traceId-$parentSpanId-01"
    }
    $body = @{ description = $sensitiveCanary } | ConvertTo-Json -Compress

    Write-Host '==> execute reference command' -ForegroundColor Cyan
    $response = Invoke-RestMethod `
        -Uri "http://127.0.0.1:$ApiPort/api/v1/identity/foundation-records" `
        -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 30
    $eventId = $response.eventId
    if ([string]::IsNullOrWhiteSpace($eventId)) {
        throw 'The reference API response did not contain an event ID.'
    }

    Wait-ForValue -Expected 'Published' -Description 'outbox status' -Read {
        Invoke-DatabaseScalar -Database 'identity_access_db' -Username 'identityaccess_app' -Password $identityPassword `
            -Sql "select status from integration.outbox_messages where event_id = '$eventId';"
    }
    Wait-ForValue -Expected '1' -Description 'audit evidence count' -Read {
        Invoke-DatabaseScalar -Database 'audit_reconciliation_db' -Username 'auditreconciliation_app' -Password $auditPassword `
            -Sql "select count(*) from audit.events where audit_event_id = '$eventId' and correlation_id = '$correlationId';"
    }
    Wait-ForValue -Expected '1' -Description 'authorisation evidence count' -Read {
        Invoke-DatabaseScalar -Database 'identity_access_db' -Username 'identityaccess_app' -Password $identityPassword `
            -Sql "select count(*) from audit.authorisation_events where correlation_id = '$correlationId' and decision = 'allowed';"
    }

    $traceVerified = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 2
        $collectorLogs = Read-CollectorLogs -Since $collectorSince

        try {
            Assert-TraceEvidence -Logs $collectorLogs -TraceId $traceId -CorrelationId $correlationId -SensitiveCanary $sensitiveCanary
            $traceVerified = $true
            break
        }
        catch {
            if ($attempt -eq 29) {
                throw
            }
        }
    }

    if (-not $traceVerified) {
        throw "Trace $traceId was not verified."
    }

    $responseJson = $response | ConvertTo-Json -Compress
    if ($responseJson -match [regex]::Escape($sensitiveCanary)) {
        throw 'The reference API response contains the sensitive request-body canary.'
    }

    foreach ($logFile in Get-ChildItem -LiteralPath $artifactDirectory -Filter '*.log') {
        if ((Get-Content -LiteralPath $logFile.FullName -Raw) -match [regex]::Escape($sensitiveCanary)) {
            throw "Host log $($logFile.Name) contains the sensitive request-body canary."
        }
    }

    Write-Host 'Walking-skeleton verification passed.' -ForegroundColor Green
    Write-Host "  event:       $eventId"
    Write-Host "  correlation: $correlationId"
    Write-Host "  trace:       $traceId"
    Write-Host "  host logs:   $artifactDirectory"
}
finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
            $process.WaitForExit(5000) | Out-Null
        }

        $process.Dispose()
    }

    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }

    Pop-Location
}

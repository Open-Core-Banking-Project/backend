# Local development infrastructure control for the backend.
# Decision: docs/adr/ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md,
# section 1, and Phase 0 epic 02.
#
# Usage (PowerShell 7 as `pwsh`, or Windows PowerShell 5.1 as `powershell`):
#   powershell -File scripts/infra.ps1 -Action up
#   powershell -File scripts/infra.ps1 -Action status
#   powershell -File scripts/infra.ps1 -Action backup
#   powershell -File scripts/infra.ps1 -Action restore -Database core_ledger_db
#   powershell -File scripts/infra.ps1 -Action down
#   powershell -File scripts/infra.ps1 -Action reset -Force
#
# Safety: reset requires -Force and deletes only the two named volumes belonging to
# the corebanking compose project. No other volume, container or image is touched.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('up', 'down', 'status', 'reset', 'backup', 'restore')]
    [string]$Action,

    [string]$Database = '',

    [string]$BackupPath = '',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeDirectory = Join-Path $repositoryRoot 'deploy/compose'
$environmentFile = Join-Path $composeDirectory '.env'
$environmentExample = Join-Path $composeDirectory '.env.example'
$projectName = 'corebanking'
$managedVolumes = @("${projectName}_pgdata", "${projectName}_kafkadata")
$databases = @(
    'core_ledger_db'
    'customer_onboarding_db'
    'identity_access_db'
    'payments_vtu_db'
    'channels_engagement_db'
    'operations_workflow_db'
    'risk_reporting_db'
    'integration_hub_db'
    'audit_reconciliation_db'
)

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    Push-Location $composeDirectory
    try {
        & docker compose @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Initialize-EnvironmentFile {
    if (Test-Path $environmentFile) {
        return
    }

    if (-not (Test-Path $environmentExample)) {
        throw "Neither $environmentFile nor $environmentExample exists."
    }

    # Passwords use an unambiguous alphanumeric alphabet so they survive being passed
    # through shell interpolation into the PostgreSQL initialisation script.
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $content = Get-Content -Path $environmentExample -Raw
        $content = [regex]::Replace($content, '__GENERATE__', {
                param($match)
                $bytes = New-Object byte[] 32
                $generator.GetBytes($bytes)
                -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
            })

        Set-Content -Path $environmentFile -Value $content -NoNewline -Encoding UTF8
        Write-Host "Generated $environmentFile with random development-only credentials." -ForegroundColor Green
        Write-Host 'The file is git-ignored. Values are never printed.' -ForegroundColor DarkGray
    }
    finally {
        $generator.Dispose()
    }
}

function Get-AdminPassword {
    $line = Select-String -Path $environmentFile -Pattern '^POSTGRES_ADMIN_PASSWORD=(.+)$' | Select-Object -First 1
    if (-not $line) {
        throw 'POSTGRES_ADMIN_PASSWORD is not set in deploy/compose/.env.'
    }

    return $line.Matches[0].Groups[1].Value.Trim()
}

function Assert-ManagedVolumes {
    $existing = @(docker volume ls --format '{{.Name}}' | Where-Object { $_ -in $managedVolumes })

    foreach ($volume in $existing) {
        if ($managedVolumes -notcontains $volume) {
            throw "Refusing to continue: $volume is not a managed corebanking volume."
        }
    }

    return $existing
}

function Invoke-DatabaseBackup {
    param([string]$Name, [string]$Target)

    $dumpName = "$Name.dump"
    docker exec corebanking-postgres rm -f "/tmp/$dumpName" | Out-Null

    docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres pg_dump --format=custom --file="/tmp/$dumpName" --username cb_admin --dbname $Name
    if ($LASTEXITCODE -ne 0) {
        throw "pg_dump failed for $Name."
    }

    docker cp "corebanking-postgres:/tmp/$dumpName" (Join-Path $Target $dumpName) | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "docker cp failed for $Name."
    }

    $size = (Get-Item (Join-Path $Target $dumpName)).Length
    if ($size -le 0) {
        throw "Backup of $Name is empty."
    }

    Write-Host ("  {0,-28} {1,8} bytes" -f $Name, $size)
}

function Invoke-DatabaseRestoreCheck {
    param([string]$DumpFile, [string]$SourceDatabase, [string]$Password)

    $scratch = "${SourceDatabase}_restore_check"

    $existing = docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres psql --tuples-only --no-align --username cb_admin --dbname postgres -c "SELECT 1 FROM pg_database WHERE datname = '$scratch';"
    if ($existing -match '1') {
        docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres psql --username cb_admin --dbname postgres -c "DROP DATABASE $scratch;" | Out-Null
    }

    docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres psql --username cb_admin --dbname postgres -c "CREATE DATABASE $scratch;" | Out-Null

    docker cp $DumpFile "corebanking-postgres:/tmp/restore-check.dump" | Out-Null
    docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres pg_restore --dbname $scratch --username cb_admin --host 127.0.0.1 --no-owner --no-privileges /tmp/restore-check.dump
    if ($LASTEXITCODE -ne 0) {
        throw "pg_restore failed for $DumpFile."
    }

    $tables = docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres psql --tuples-only --no-align --username cb_admin --dbname $scratch -c "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';"
    if ($LASTEXITCODE -ne 0) {
        throw "Verification query failed for $scratch."
    }

    docker exec -e "PGPASSWORD=$script:adminPassword" corebanking-postgres psql --username cb_admin --dbname postgres -c "DROP DATABASE $scratch;" | Out-Null

    Write-Host ("  {0,-28} restored into {1}, public tables verified: {2}" -f $SourceDatabase, $scratch, $tables.Trim())
}

Push-Location $repositoryRoot
try {
    switch ($Action) {
        'up' {
            Initialize-EnvironmentFile
            Invoke-Compose up -d --wait
            Invoke-Compose ps
        }

        'down' {
            Invoke-Compose down
            Write-Host 'Containers removed. Volumes were kept.' -ForegroundColor Green
        }

        'status' {
            Invoke-Compose ps
        }

        'reset' {
            if (-not $Force) {
                throw 'Reset deletes local database and broker volumes. Re-run with -Force to confirm.'
            }

            $volumes = Assert-ManagedVolumes
            Write-Host "Removing compose containers and these volumes only: $($managedVolumes -join ', ')" -ForegroundColor Yellow
            Invoke-Compose down --volumes
            Write-Host 'Local infrastructure state destroyed. Run -Action up to recreate it.' -ForegroundColor Green
        }

        'backup' {
            $script:adminPassword = Get-AdminPassword
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $target = Join-Path $repositoryRoot "artifacts/backups/$stamp"
            New-Item -ItemType Directory -Force -Path $target | Out-Null

            $selection = if ($Database) { @($Database) } else { $databases }
            Write-Host "Backing up to $target" -ForegroundColor Cyan

            foreach ($name in $selection) {
                Invoke-DatabaseBackup -Name $name -Target $target
            }

            Write-Host 'Backup complete.' -ForegroundColor Green
        }

        'restore' {
            $script:adminPassword = Get-AdminPassword

            if (-not $Database) {
                throw 'Restore requires -Database, for example -Database core_ledger_db.'
            }

            if (-not $BackupPath) {
                $latest = Get-ChildItem (Join-Path $repositoryRoot 'artifacts/backups') -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending | Select-Object -First 1
                if (-not $latest) {
                    throw 'No backup directory found under artifacts/backups. Run -Action backup first.'
                }

                $BackupPath = Join-Path $latest.FullName "$Database.dump"
            }

            if (-not (Test-Path $BackupPath)) {
                throw "Backup file not found: $BackupPath"
            }

            Write-Host "Restoring $Database from $BackupPath into a scratch database" -ForegroundColor Cyan
            Invoke-DatabaseRestoreCheck -DumpFile $BackupPath -SourceDatabase $Database -Password ''
        }
    }
}
finally {
    Pop-Location
}

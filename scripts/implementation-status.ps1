[CmdletBinding()]
param(
    [ValidateSet('Text', 'Json')]
    [string] $Format = 'Text',

    [string] $Phase = 'phase-1-banking-foundation'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$implementationRoot = Join-Path $repositoryRoot 'docs\implementation'
$phaseRoot = Join-Path $implementationRoot $Phase
$ledgerPath = Join-Path $phaseRoot 'slice-ledger.csv'

if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) {
    throw "Slice ledger not found: $ledgerPath"
}

$rows = @(Import-Csv -LiteralPath $ledgerPath)
if ($rows.Count -eq 0) {
    throw "Slice ledger is empty: $ledgerPath"
}

function Get-SliceNumber {
    param([object] $Row)

    $number = 0
    if (-not [int]::TryParse([string] $Row.slice, [ref] $number)) {
        return [int]::MaxValue
    }

    return $number
}

function Test-ImplementationEligibleDocumentStatus {
    param([string] $Status)

    return $Status -in @('Approved', 'In Progress', 'In progress', 'Implemented')
}

$epics = @(
    $rows |
        Group-Object epic_id |
        ForEach-Object {
            $group = @($_.Group | Sort-Object @{ Expression = { Get-SliceNumber $_ } })
            $first = $group[0]
            [pscustomobject]@{
                epic_id = $first.epic_id
                epic = $first.epic
                epic_doc = $first.epic_doc
                epic_doc_status = $first.epic_doc_status
                total = $group.Count
                implemented = @($group | Where-Object status -eq 'implemented').Count
                in_progress = @($group | Where-Object status -eq 'in progress').Count
                not_started = @($group | Where-Object status -eq 'not started').Count
                blocked = @($group | Where-Object status -eq 'blocked').Count
                pending_approval = @($group | Where-Object approval -eq 'pending').Count
            }
        }
)

$activeEpic = $null
$candidate = $null
$action = 'complete'
$reason = 'No unfinished slice was found in an implementation-eligible epic.'

foreach ($epic in $epics) {
    if (-not (Test-ImplementationEligibleDocumentStatus $epic.epic_doc_status)) {
        continue
    }

    $epicRows = @($rows | Where-Object epic_id -eq $epic.epic_id | Sort-Object @{ Expression = { Get-SliceNumber $_ } })
    # A human may approve a bounded increment while named epic or production gaps remain.
    # Treat that approval as the slice checkpoint being complete so automation can advance.
    $unfinished = @($epicRows | Where-Object {
            $_.status -in @('in progress', 'not started') -and
            $_.approval -notlike 'approved*'
        })
    if ($unfinished.Count -eq 0) {
        continue
    }

    $activeEpic = $epic
    $unblocked = @($unfinished | Where-Object { [string]::IsNullOrWhiteSpace($_.blocked_by_decisions) })
    if ($unblocked.Count -gt 0) {
        $candidate = $unblocked[0]
        $action = 'implement_slice'
        $reason = 'Continue the lowest-numbered unfinished slice without recorded decision blockers in the active eligible epic.'
    }
    else {
        $candidate = $unfinished[0]
        $action = 'resolve_decision'
        $reason = 'The next unfinished slice is blocked by recorded decisions.'
    }

    break
}

if ($null -eq $activeEpic) {
    $nextDraft = $epics | Where-Object { $_.not_started -gt 0 -or $_.in_progress -gt 0 -or $_.blocked -gt 0 } | Select-Object -First 1
    if ($null -ne $nextDraft) {
        $activeEpic = $nextDraft
        $action = 'review_contract'
        $reason = "The next unfinished epic document is '$($nextDraft.epic_doc_status)' and requires human approval before autonomous implementation."
    }
}

$result = [ordered]@{
    generated_at = (Get-Date).ToString('o')
    phase = $Phase
    ledger = $ledgerPath
    action = $action
    reason = $reason
    epic = if ($null -eq $activeEpic) { $null } else { $activeEpic }
    slice = if ($null -eq $candidate) {
        $null
    }
    else {
        [ordered]@{
            id = "$($candidate.epic_id)-S$($candidate.slice)"
            number = [int]$candidate.slice
            status = $candidate.status
            approval = $candidate.approval
            blocked_by_decisions = $candidate.blocked_by_decisions
            scope = $candidate.scope
            evidence = $candidate.evidence
            notes = $candidate.notes
            verification = $candidate.verification
        }
    }
    epics = $epics
}

if ($Format -eq 'Json') {
    $result | ConvertTo-Json -Depth 8
    exit 0
}

Write-Output "Phase: $Phase"
Write-Output "Action: $action"
if ($null -ne $activeEpic) {
    Write-Output "Epic: $($activeEpic.epic_id) - $($activeEpic.epic) [$($activeEpic.epic_doc_status)]"
}
if ($null -ne $candidate) {
    Write-Output "Slice: $($candidate.slice) [$($candidate.status)]"
    Write-Output "Scope: $($candidate.scope)"
    if (-not [string]::IsNullOrWhiteSpace($candidate.blocked_by_decisions)) {
        Write-Output "Blocked by: $($candidate.blocked_by_decisions)"
    }
}
Write-Output "Reason: $reason"


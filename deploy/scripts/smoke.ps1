param(
    [Parameter(Mandatory)] [uri] $ApiBaseUrl,
    [Parameter(Mandatory)] [uri] $WorkerUrl,
    [Parameter(Mandatory)] [uri] $ReferenceTargetUrl,
    [switch] $ApproveStagingSmoke,
    [ValidateRange(30, 210)] [int] $TimeoutSeconds = 210,
    [string] $ProgressPath,
    [string] $ResultPath,
    [string] $RequiredExistingHuntId,
    [string] $RequiredExistingPlanVersion,
    [switch] $ResetExpiredDeadlineForExistingHunt
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StagingHttp.psm1') -Force
if (-not $ApproveStagingSmoke) {
    throw 'The deployed smoke contacts a Google Cloud staging environment. Re-run only after explicit approval with -ApproveStagingSmoke.'
}

$deadline = [DateTimeOffset]::MinValue
$startedAt = [DateTimeOffset]::MinValue
function ApiUri([string] $Path) { [uri]::new($ApiBaseUrl, $Path) }
function Remaining-TimeoutSeconds {
    $remaining = [int][Math]::Floor(($deadline - [DateTimeOffset]::UtcNow).TotalSeconds)
    if ($remaining -lt 1) { throw 'The staging smoke deadline was exhausted.' }
    return $remaining
}
function Assert-UnauthenticatedDenied([uri] $ServiceUrl, [string] $Name, [string] $Path) {
    try {
        Invoke-WebRequest -Method Get -Uri ([uri]::new($ServiceUrl, $Path)) -UseBasicParsing -TimeoutSec (Remaining-TimeoutSeconds) | Out-Null
        throw "$Name accepted an unauthenticated invocation."
    }
    catch {
        $status = Get-StagingResponseStatusCode $_
        if ($status -notin @(401, 403)) { throw "$Name application route did not return an authoritative IAM denial (received $status)." }
    }
}
function Wait-Json([string] $Path, [scriptblock] $Ready) {
    do {
        try {
            $result = Invoke-RestMethod -Method Get -Uri (ApiUri $Path) -TimeoutSec (Remaining-TimeoutSeconds)
            if (& $Ready $result) { return $result }
        }
        catch {
            if ((Get-StagingResponseStatusCode $_) -notin @(202, 404)) { throw }
        }
        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Path"
}
function Write-JsonAtomic([string] $Path, [object] $Value) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $temporary = "$fullPath.tmp"
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporary -Destination $fullPath -Force
}

$progress = [ordered]@{
    schemaVersion = '1.0'
    status = 'Ready'
    startedAtUtc = $null
    deadlineAtUtc = $null
    huntCreateStarted = $false
    huntId = $null
    planVersion = $null
    runId = $null
    findingId = $null
    replayComplete = $false
}
if (-not [string]::IsNullOrWhiteSpace($ProgressPath) -and (Test-Path -LiteralPath $ProgressPath -PathType Leaf)) {
    $loaded = Get-Content -Raw -LiteralPath $ProgressPath | ConvertFrom-Json -AsHashtable -Depth 30
    foreach ($name in @($progress.Keys)) { if ($loaded.ContainsKey($name)) { $progress[$name] = $loaded[$name] } }
}
function Save-Progress([string] $Status) {
    $progress.status = $Status
    Write-JsonAtomic -Path $ProgressPath -Value $progress
}
if ($ResetExpiredDeadlineForExistingHunt -and (
    [string]::IsNullOrWhiteSpace($RequiredExistingHuntId) -or
    [string]::IsNullOrWhiteSpace($RequiredExistingPlanVersion))) {
    throw 'An expired-deadline recovery requires exact non-empty existing hunt and plan identifiers.'
}
if (-not [string]::IsNullOrWhiteSpace($RequiredExistingHuntId)) {
    if ([string]::IsNullOrWhiteSpace($RequiredExistingPlanVersion)) { throw 'Existing-hunt recovery requires an exact existing plan version.' }
    if ([string]$progress.huntId -cne $RequiredExistingHuntId -or [bool]$progress.huntCreateStarted) {
        throw 'RecoveryCompletion requires the exact durable existing hunt and forbids hunt creation.'
    }
    if (-not [string]::IsNullOrWhiteSpace($RequiredExistingPlanVersion)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$progress.planVersion) -and [string]$progress.planVersion -cne $RequiredExistingPlanVersion) {
            throw 'The durable plan version drifted from the approved recovery binding.'
        }
        $progress.planVersion = $RequiredExistingPlanVersion
        Save-Progress 'SmokeStarted'
    }
}
if ([string]::IsNullOrWhiteSpace([string]$progress.startedAtUtc) -or [string]::IsNullOrWhiteSpace([string]$progress.deadlineAtUtc)) {
    $startedAt = [DateTimeOffset]::UtcNow
    $deadline = $startedAt.AddSeconds($TimeoutSeconds)
    $progress.startedAtUtc = $startedAt.ToString('O')
    $progress.deadlineAtUtc = $deadline.ToString('O')
    Save-Progress 'Ready'
}
else {
    $startedAt = [DateTimeOffset]::Parse([string]$progress.startedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    $deadline = [DateTimeOffset]::Parse([string]$progress.deadlineAtUtc, [Globalization.CultureInfo]::InvariantCulture)
}
if ([DateTimeOffset]::UtcNow -ge $deadline) {
    if ($ResetExpiredDeadlineForExistingHunt -and -not [string]::IsNullOrWhiteSpace($RequiredExistingHuntId)) {
        $startedAt = [DateTimeOffset]::UtcNow
        $deadline = $startedAt.AddSeconds($TimeoutSeconds)
        $progress.startedAtUtc = $startedAt.ToString('O')
        $progress.deadlineAtUtc = $deadline.ToString('O')
        Save-Progress 'SmokeStarted'
    }
    else { throw 'The staging smoke absolute deadline was exhausted; only an exact existing-hunt recovery approval may reset it.' }
}

Assert-UnauthenticatedDenied $WorkerUrl 'Worker' '/internal/replays'
Assert-UnauthenticatedDenied $ReferenceTargetUrl 'Reference target' '/api/inventory'

$capabilities = Invoke-WebRequest -Method Get -Uri (ApiUri '/api/capabilities') -UseBasicParsing -TimeoutSec (Remaining-TimeoutSeconds)
if ($capabilities.StatusCode -ne 200) { throw "API capabilities check failed with $($capabilities.StatusCode)." }

if ([string]::IsNullOrWhiteSpace([string]$progress.huntId)) {
    if (-not [string]::IsNullOrWhiteSpace($RequiredExistingHuntId)) { throw 'RecoveryCompletion forbids POST /api/hunts.' }
    if ([bool]$progress.huntCreateStarted) {
        Save-Progress 'AmbiguousMutation'
        throw 'AmbiguousMutation: hunt creation may have occurred without a durable hunt ID; a second new smoke run is forbidden.'
    }
    $progress.huntCreateStarted = $true
    Save-Progress 'SmokeStarted'
    $hunt = Invoke-RestMethod -Method Post -Uri (ApiUri '/api/hunts') -ContentType 'application/json' -TimeoutSec (Remaining-TimeoutSeconds) -Body (@{
        objective = 'Successful orders must not exceed available inventory.'
        maxActors = 10
        maxConcurrency = 10
        maxRequests = 40
        maxModelCalls = 5
        maxDurationSeconds = 90
        maxRetries = 1
    } | ConvertTo-Json)
    $progress.huntId = [string]$hunt.id
    $progress.huntCreateStarted = $false
    Save-Progress 'SmokeStarted'
}
$huntId = [string]$progress.huntId

if ([string]::IsNullOrWhiteSpace([string]$progress.planVersion)) {
    Invoke-RestMethod -Method Post -Uri (ApiUri "/api/hunts/$huntId/plan") -TimeoutSec (Remaining-TimeoutSeconds) | Out-Null
    $plan = Wait-Json "/api/hunts/$huntId/plan" { param($value) $null -ne (Get-StagingPropertyValue -InputObject $value -Name 'planVersion') }
    $progress.planVersion = [string]$plan.planVersion
    Save-Progress 'SmokeStarted'
}

if ([string]::IsNullOrWhiteSpace([string]$progress.runId)) {
    $approval = Invoke-RestMethod -Method Post -Uri (ApiUri "/api/hunts/$huntId/runs") -ContentType 'application/json' -TimeoutSec (Remaining-TimeoutSeconds) -Body (@{
        planVersion = [string]$progress.planVersion
        idempotencyKey = "cloud-smoke-$huntId"
    } | ConvertTo-Json)
    $progress.runId = [string]$approval.runId
    Save-Progress 'SmokeStarted'
}
$runId = [string]$progress.runId
$run = Wait-Json "/api/runs/$runId" { param($value) $null -ne $value.findingId -or $value.status -in @('Failed', 'Cancelled') }
if ($run.status -in @('Failed', 'Cancelled') -or $null -eq $run.findingId) { throw "Campaign ended as $($run.status) without a finding." }
$progress.findingId = [string]$run.findingId
Save-Progress 'SmokeStarted'

$finding = Invoke-RestMethod -Method Get -Uri (ApiUri "/api/findings/$($progress.findingId)") -TimeoutSec (Remaining-TimeoutSeconds)
if ($finding.successMessage -ne 'Race condition verified — reproduced 3/3 and minimized to 2 actors.') { throw 'Golden-path finding proof did not match.' }
if ($finding.reproductions.Count -ne 3 -or ($finding.reproductions | Where-Object outcome -ne 'Fail').Count -ne 0) { throw 'Measured 3/3 reproduction proof is incomplete.' }
if ($finding.replayArtifact.actorCount -ne 2) { throw 'Replay artifact was not minimized to two actors.' }

$comparison = Invoke-RestMethod -Method Post -Uri (ApiUri "/api/findings/$($progress.findingId)/replays") -ContentType 'application/json' -TimeoutSec (Remaining-TimeoutSeconds) -Body (@{
    idempotencyKey = "cloud-smoke-fix-$($progress.findingId)"
} | ConvertTo-Json)
if ($comparison.vulnerableOutcome -ne 'Fail' -or $comparison.fixedOutcome -ne 'Pass') { throw 'Vulnerable/fixed replay comparison failed.' }
if ($comparison.artifactFingerprint -ne $finding.replayArtifact.fingerprint) { throw 'Verify Fix changed the immutable replay artifact.' }
$progress.replayComplete = $true
Save-Progress 'SmokeStarted'

$proof = Invoke-RestMethod -Method Get -Uri (ApiUri "/api/cloud-proof?runId=$runId") -TimeoutSec (Remaining-TimeoutSeconds)
if ($proof.workerAuthentication -ne 'OIDC ID token' -or [string]::IsNullOrWhiteSpace($proof.apiRevision)) { throw 'Cloud execution proof is incomplete.' }
$elapsedSeconds = ([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds
if ($elapsedSeconds -gt $TimeoutSeconds) { throw "Golden path exceeded its $TimeoutSeconds-second absolute deadline." }

$result = [ordered]@{
    schemaVersion = '1.0'
    status = 'SmokeComplete'
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    elapsedSeconds = [math]::Round($elapsedSeconds, 1)
    huntId = $huntId
    runId = $runId
    findingId = [string]$progress.findingId
    apiRevision = [string]$proof.apiRevision
    artifactFingerprint = [string]$finding.replayArtifact.fingerprint
}
Write-JsonAtomic -Path $ResultPath -Value $result
Save-Progress 'SmokeComplete'

Write-Host "Cloud golden path passed in $($result.elapsedSeconds)s: run=$runId, finding=$($progress.findingId), revision=$($proof.apiRevision)."

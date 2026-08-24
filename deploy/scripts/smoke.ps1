param(
    [Parameter(Mandatory)] [uri] $ApiBaseUrl,
    [Parameter(Mandatory)] [uri] $WorkerUrl,
    [Parameter(Mandatory)] [uri] $ReferenceTargetUrl,
    [switch] $ApproveStagingSmoke,
    [ValidateRange(30, 230)] [int] $TimeoutSeconds = 210
)

$ErrorActionPreference = 'Stop'
if (-not $ApproveStagingSmoke) {
    throw 'The deployed smoke contacts a Google Cloud staging environment. Re-run only after explicit approval with -ApproveStagingSmoke.'
}

$timer = [System.Diagnostics.Stopwatch]::StartNew()
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
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
        $status = $_.Exception.Response.StatusCode.value__
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
            if ($_.Exception.Response.StatusCode.value__ -notin @(202, 404)) { throw }
        }
        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Path"
}

Assert-UnauthenticatedDenied $WorkerUrl 'Worker' '/internal/replays'
Assert-UnauthenticatedDenied $ReferenceTargetUrl 'Reference target' '/api/inventory'

$capabilities = Invoke-WebRequest -Method Get -Uri (ApiUri '/api/capabilities') -UseBasicParsing -TimeoutSec (Remaining-TimeoutSeconds)
if ($capabilities.StatusCode -ne 200) { throw "API capabilities check failed with $($capabilities.StatusCode)." }

$hunt = Invoke-RestMethod -Method Post -Uri (ApiUri '/api/hunts') -ContentType 'application/json' -TimeoutSec (Remaining-TimeoutSeconds) -Body (@{
    objective = 'Successful orders must not exceed available inventory.'
    maxActors = 10
    maxConcurrency = 10
    maxRequests = 40
    maxModelCalls = 5
    maxDurationSeconds = 90
    maxRetries = 1
} | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri (ApiUri "/api/hunts/$($hunt.id)/plan") -TimeoutSec (Remaining-TimeoutSeconds) | Out-Null
$plan = Wait-Json "/api/hunts/$($hunt.id)/plan" { param($value) $null -ne $value.planVersion }
$approval = Invoke-RestMethod -Method Post -Uri (ApiUri "/api/hunts/$($hunt.id)/runs") -ContentType 'application/json' -TimeoutSec (Remaining-TimeoutSeconds) -Body (@{
    planVersion = $plan.planVersion
    idempotencyKey = "cloud-smoke-$($hunt.id)"
} | ConvertTo-Json)
$run = Wait-Json "/api/runs/$($approval.runId)" { param($value) $null -ne $value.findingId -or $value.status -in @('Failed', 'Cancelled') }
if ($run.status -in @('Failed', 'Cancelled') -or $null -eq $run.findingId) { throw "Campaign ended as $($run.status) without a finding." }

$finding = Invoke-RestMethod -Method Get -Uri (ApiUri "/api/findings/$($run.findingId)") -TimeoutSec (Remaining-TimeoutSeconds)
if ($finding.successMessage -ne 'Race condition verified — reproduced 3/3 and minimized to 2 actors.') { throw 'Golden-path finding proof did not match.' }
if ($finding.reproductions.Count -ne 3 -or ($finding.reproductions | Where-Object outcome -ne 'Fail').Count -ne 0) { throw 'Measured 3/3 reproduction proof is incomplete.' }
if ($finding.replayArtifact.actorCount -ne 2) { throw 'Replay artifact was not minimized to two actors.' }

$comparison = Invoke-RestMethod -Method Post -Uri (ApiUri "/api/findings/$($run.findingId)/replays") -ContentType 'application/json' -TimeoutSec (Remaining-TimeoutSeconds) -Body (@{
    idempotencyKey = "cloud-smoke-fix-$($run.findingId)"
} | ConvertTo-Json)
if ($comparison.vulnerableOutcome -ne 'Fail' -or $comparison.fixedOutcome -ne 'Pass') { throw 'Vulnerable/fixed replay comparison failed.' }
if ($comparison.artifactFingerprint -ne $finding.replayArtifact.fingerprint) { throw 'Verify Fix changed the immutable replay artifact.' }

$proof = Invoke-RestMethod -Method Get -Uri (ApiUri "/api/cloud-proof?runId=$($approval.runId)") -TimeoutSec (Remaining-TimeoutSeconds)
if ($proof.workerAuthentication -ne 'OIDC ID token' -or [string]::IsNullOrWhiteSpace($proof.apiRevision)) { throw 'Cloud execution proof is incomplete.' }
$timer.Stop()
if ($timer.Elapsed.TotalMinutes -ge 4) { throw "Golden path exceeded four minutes: $($timer.Elapsed)." }

Write-Host "Cloud golden path passed in $([math]::Round($timer.Elapsed.TotalSeconds, 1))s: run=$($approval.runId), finding=$($run.findingId), revision=$($proof.apiRevision)."

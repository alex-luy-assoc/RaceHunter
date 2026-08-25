[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ApprovalRequestPath,
    [Parameter(Mandatory)] [ValidatePattern('^[a-f0-9]{64}$')] [string] $ApprovedRequestHash
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StagingHttp.psm1') -Force
function Get-FileSha([string] $Path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
function Write-JsonAtomic([string] $Path, [object] $Value) {
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $temporary = "$Path.tmp"
    $Value | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Read-Json([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable -Depth 50
}
function Get-ReadOnlyDiagnostic([uri] $Uri) {
    try {
        $response = Invoke-WebRequest -Method Get -Uri $Uri -UseBasicParsing -TimeoutSec 15
        return [ordered]@{ uri = $Uri.AbsoluteUri; statusCode = [int]$response.StatusCode; bodySha256 = Get-StagingTextSha ([string]$response.Content) }
    }
    catch {
        $statusCode = Get-StagingResponseStatusCode $_
        return [ordered]@{ uri = $Uri.AbsoluteUri; statusCode = $(if ($null -eq $statusCode) { 0 } else { $statusCode }); bodySha256 = $null }
    }
}
function Get-StagingTextSha([string] $Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$requestPath = [IO.Path]::GetFullPath($ApprovalRequestPath)
if (-not (Test-Path -LiteralPath $requestPath -PathType Leaf) -or (Get-FileSha $requestPath) -cne $ApprovedRequestHash) {
    throw 'ReleaseCompletion request bytes do not match the exact approved SHA-256.'
}
$request = Read-Json $requestPath
if ([string]$request.schemaVersion -cne '1.0' -or [string]$request.stage -cnotin @('ReleaseCompletion', 'RecoveryCompletion', 'ExistingFindingCompletionResume', 'DemoReplacementCompletion', 'DemoReplacementCompletion2', 'ExistingDemoRunCompletion') -or $request.valid -isnot [bool] -or -not $request.valid) {
    throw 'ReleaseCompletion request is invalid or default denied.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactPaths = [ordered]@{
    coordinator = [IO.Path]::GetFullPath($PSCommandPath)
    http = Join-Path $repositoryRoot 'deploy\scripts\StagingHttp.psm1'
    smoke = Join-Path $repositoryRoot 'deploy\scripts\smoke.ps1'
    browser = Join-Path $repositoryRoot 'tests\RaceHunter.AcceptanceTests\staging-demo.spec.ts'
    browserConfig = Join-Path $repositoryRoot 'tests\RaceHunter.AcceptanceTests\playwright.staging-demo.config.ts'
    browserPackage = Join-Path $repositoryRoot 'tests\RaceHunter.AcceptanceTests\package.json'
    demoScript = Join-Path $repositoryRoot 'docs\demo\demo-script.md'
}
foreach ($name in $artifactPaths.Keys) {
    if ([string]$request.artifactHashes[$name] -cne (Get-FileSha $artifactPaths[$name])) { throw "ReleaseCompletion artifact '$name' drifted from the approved bytes." }
}
if ([int]$request.smokeTimeoutSeconds -ne 360 -or [int]$request.demoTimeoutSeconds -ne 239) { throw 'ReleaseCompletion time bounds drifted.' }

$artifactDirectory = [IO.Path]::GetFullPath([string]$request.artifactDirectory)
$statePath = Join-Path $artifactDirectory 'release-completion-state.json'
$smokeProgressPath = Join-Path $artifactDirectory 'smoke-progress.json'
$smokeResultPath = Join-Path $artifactDirectory 'smoke-result.json'
$demoProgressPath = Join-Path $artifactDirectory 'demo-progress.json'
$demoResultPath = Join-Path $artifactDirectory 'demo-result.json'
$demoArtifactDirectory = Join-Path $artifactDirectory 'demo-artifacts'
$isExistingFindingResume = [string]$request.stage -ceq 'ExistingFindingCompletionResume'
$isExistingDemoRunCompletion = [string]$request.stage -ceq 'ExistingDemoRunCompletion'
$isRecovery = [string]$request.stage -ceq 'RecoveryCompletion' -or $isExistingFindingResume
if ($isExistingDemoRunCompletion) {
    if ([string]$request.demoRecovery.existingHuntId -notmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or
        [string]$request.demoRecovery.existingRunId -notmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
        throw 'ExistingDemoRunCompletion requires one exact existing hunt and run ID.'
    }
}
if ($isRecovery) {
    if ([string]$request.recovery.existingHuntId -notmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
        throw 'RecoveryCompletion requires one exact existing hunt ID.'
    }
    if ([string]$request.recovery.existingPlanVersion -notmatch '^plan-[a-f0-9]{16}$') {
        throw 'RecoveryCompletion requires one exact existing plan version.'
    }
    if ($isExistingFindingResume -and [string]$request.recovery.existingRunId -notmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
        throw 'ExistingFindingCompletionResume requires one exact existing run ID.'
    }
    if ($isExistingFindingResume -and [string]$request.recovery.existingFindingId -notmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
        throw 'ExistingFindingCompletionResume requires one exact existing finding ID.'
    }
}
if ($isRecovery -and -not (Test-Path -LiteralPath $smokeProgressPath -PathType Leaf)) {
    $sourcePath = [IO.Path]::GetFullPath([string]$request.recovery.sourceProgressPath)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or (Get-FileSha $sourcePath) -cne [string]$request.recovery.sourceProgressSha256) {
        throw 'RecoveryCompletion source progress drifted from the approved bytes.'
    }
    $source = Read-Json $sourcePath
    if ([string]$source.huntId -cne [string]$request.recovery.existingHuntId -or [bool]$source.huntCreateStarted) {
        throw 'RecoveryCompletion source hunt identity drifted.'
    }
    if ($isExistingFindingResume) {
        if ([string]$source.planVersion -cne [string]$request.recovery.existingPlanVersion -or
            [string]$source.runId -cne [string]$request.recovery.existingRunId -or
            [string]$source.findingId -cne [string]$request.recovery.existingFindingId) {
            throw 'ExistingFindingCompletionResume source plan, run, or finding identity drifted.'
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$source.runId)) {
        throw 'RecoveryCompletion may resume only the exact existing pre-run hunt.'
    }
    $source.planVersion = [string]$request.recovery.existingPlanVersion
    if ($isExistingFindingResume) {
        $source.runId = [string]$request.recovery.existingRunId
        $source.findingId = [string]$request.recovery.existingFindingId
    }
    Write-JsonAtomic -Path $smokeProgressPath -Value $source
}
if ($isExistingFindingResume) {
    $destination = Read-Json $smokeProgressPath
    if ($null -eq $destination -or [bool]$destination.huntCreateStarted -or
        [string]$destination.huntId -cne [string]$request.recovery.existingHuntId -or
        [string]$destination.planVersion -cne [string]$request.recovery.existingPlanVersion -or
        [string]$destination.runId -cne [string]$request.recovery.existingRunId -or
        [string]$destination.findingId -cne [string]$request.recovery.existingFindingId) {
        throw 'ExistingFindingCompletionResume destination progress identity drifted.'
    }
}
function Save-State([string] $Status, [string] $Failure = $null) {
    $state.status = $Status
    $state.updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $state.failure = $Failure
    Write-JsonAtomic -Path $statePath -Value $state
}

$state = Read-Json $statePath
if ($null -eq $state) {
    $state = [ordered]@{
        schemaVersion = '1.0'; requestHash = $ApprovedRequestHash; commitSha = [string]$request.commitSha; bindingHash = [string]$request.bindingHash
        apiBaseUrl = [string]$request.apiBaseUrl; workerUrl = [string]$request.workerUrl; referenceTargetUrl = [string]$request.referenceTargetUrl
        status = 'Ready'; smokeEvidencePath = $smokeResultPath; demoEvidencePath = $demoResultPath
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); failure = $null
    }
    Write-JsonAtomic -Path $statePath -Value $state
}
foreach ($name in @('requestHash', 'commitSha', 'bindingHash', 'apiBaseUrl', 'workerUrl', 'referenceTargetUrl')) {
    $expected = if ($name -eq 'requestHash') { $ApprovedRequestHash } else { [string]$request[$name] }
    if ([string]$state[$name] -cne $expected) { throw "ReleaseCompletion state identity '$name' drifted; resume is forbidden." }
}
if ([string]$state.status -eq 'ReleaseComplete') { return }
if ([string]$state.status -eq 'AmbiguousMutation') { throw 'AmbiguousMutation: a second new smoke or demo run is forbidden.' }

$smokeResult = Read-Json $smokeResultPath
if ($null -eq $smokeResult -or [string]$smokeResult.status -ne 'SmokeComplete') {
    Save-State 'SmokeStarted'
    try {
        $smokeArguments = @{
            ApiBaseUrl = [uri]$request.apiBaseUrl; WorkerUrl = [uri]$request.workerUrl; ReferenceTargetUrl = [uri]$request.referenceTargetUrl
            ApproveStagingSmoke = $true; TimeoutSeconds = 360; ProgressPath = $smokeProgressPath; ResultPath = $smokeResultPath
        }
        if ($isRecovery) {
            $smokeArguments.RequiredExistingHuntId = [string]$request.recovery.existingHuntId
            $smokeArguments.RequiredExistingPlanVersion = [string]$request.recovery.existingPlanVersion
            $smokeArguments.ResetExpiredDeadlineForExistingHunt = $true
        }
        & $artifactPaths.smoke @smokeArguments
    }
    catch {
        $progress = Read-Json $smokeProgressPath
        $ambiguous = $null -ne $progress -and [string]$progress.status -eq 'AmbiguousMutation'
        Save-State $(if ($ambiguous) { 'AmbiguousMutation' } else { 'SmokeIncomplete' }) $_.Exception.Message
        throw
    }
}
$smokeResult = Read-Json $smokeResultPath
if ($null -eq $smokeResult -or [string]$smokeResult.status -ne 'SmokeComplete') { throw 'Smoke did not produce complete durable evidence.' }
Save-State 'SmokeComplete'

$demoResult = Read-Json $demoResultPath
if ($null -eq $demoResult -or [string]$demoResult.status -ne 'DemoComplete') {
    $demoProgress = Read-Json $demoProgressPath
    if ($isExistingDemoRunCompletion) {
        $destinationRunCreateStarted = Get-StagingPropertyValue -InputObject $demoProgress -Name 'runCreateStarted'
        $destinationFindingId = [string](Get-StagingPropertyValue -InputObject $demoProgress -Name 'findingId')
        if ($null -eq $demoProgress -or [string]$demoProgress.status -cne 'DemoStarted' -or
            -not [bool]$demoProgress.demoAttemptStarted -or [bool]$destinationRunCreateStarted -or
            [string]$demoProgress.huntId -cne [string]$request.demoRecovery.existingHuntId -or
            [string]$demoProgress.runId -cne [string]$request.demoRecovery.existingRunId -or
            -not [string]::IsNullOrWhiteSpace($destinationFindingId)) {
            throw 'ExistingDemoRunCompletion destination progress identity drifted.'
        }
    }
    elseif ($null -ne $demoProgress -and [string]$demoProgress.status -ne 'Ready') {
        $demoRunId = [string](Get-StagingPropertyValue -InputObject $demoProgress -Name 'runId')
        $demoFindingId = [string](Get-StagingPropertyValue -InputObject $demoProgress -Name 'findingId')
        $diagnostics = @()
        if (-not [string]::IsNullOrWhiteSpace($demoRunId)) {
            $diagnostics += Get-ReadOnlyDiagnostic ([uri]::new([uri]$request.apiBaseUrl, "/api/runs/$demoRunId"))
            $diagnostics += Get-ReadOnlyDiagnostic ([uri]::new([uri]$request.apiBaseUrl, "/api/cloud-proof?runId=$demoRunId"))
        }
        if (-not [string]::IsNullOrWhiteSpace($demoFindingId)) {
            $diagnostics += Get-ReadOnlyDiagnostic ([uri]::new([uri]$request.apiBaseUrl, "/api/findings/$demoFindingId"))
        }
        Write-JsonAtomic -Path (Join-Path $artifactDirectory 'demo-read-only-diagnostic.json') -Value ([ordered]@{
            schemaVersion = '1.0'; observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); requestHash = $ApprovedRequestHash
            runId = $demoRunId; findingId = $demoFindingId; observations = @($diagnostics)
        })
        Save-State 'DemoIncomplete' 'An interrupted browser recording cannot qualify as one fresh unedited demo; a second new demo is forbidden.'
        throw 'DemoIncomplete: same-run diagnostics remain allowed, but this authorization forbids a second fresh demo.'
    }
    if ((Test-Path -LiteralPath $demoArtifactDirectory) -and @(Get-ChildItem -LiteralPath $demoArtifactDirectory -Recurse -File).Count -ne 0) {
        throw 'Demo artifact directory is not empty before the one fresh recording.'
    }
    Save-State 'DemoStarted'
    $env:RACEHUNTER_BASE_URL = [string]$request.apiBaseUrl
    $env:RACEHUNTER_DEMO_PROGRESS_PATH = $demoProgressPath
    $env:RACEHUNTER_DEMO_ARTIFACT_DIR = $demoArtifactDirectory
    $env:RACEHUNTER_DEMO_DEADLINE_UTC = [DateTimeOffset]::UtcNow.AddSeconds(239).ToString('O')
    try {
        $acceptanceRoot = Join-Path $repositoryRoot 'tests\RaceHunter.AcceptanceTests'
        $process = Start-Process -FilePath 'npm.cmd' -ArgumentList @('run', 'test:staging-demo') -WorkingDirectory $acceptanceRoot -NoNewWindow -PassThru
        if (-not $process.WaitForExit(240000)) {
            Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', [string]$process.Id, '/T', '/F') -NoNewWindow -Wait | Out-Null
            throw 'Demo exceeded its 240-second process-tree boundary.'
        }
        if ($process.ExitCode -ne 0) { throw "Demo exited $($process.ExitCode)." }
        $progress = Read-Json $demoProgressPath
        if ($null -eq $progress -or [string]$progress.status -ne 'DemoComplete') { throw 'Demo did not produce complete durable evidence.' }
        if ([string]$progress.runId -ceq [string]$smokeResult.runId -or [string]$progress.huntId -ceq [string]$smokeResult.huntId) { throw 'The demo must be fresh and distinct from smoke.' }
        $videos = @(Get-ChildItem -LiteralPath $demoArtifactDirectory -Recurse -File -Filter '*.webm')
        if ($videos.Count -ne 1) { throw 'The one fresh unedited demo must produce exactly one video artifact.' }
        $result = [ordered]@{
            schemaVersion = '1.0'; status = 'DemoComplete'; completedAtUtc = [string]$progress.completedAtUtc; elapsedSeconds = $progress.elapsedSeconds
            huntId = [string]$progress.huntId; runId = [string]$progress.runId; findingId = [string]$progress.findingId
            videoArtifact = $videos[0].FullName; videoSha256 = Get-FileSha $videos[0].FullName
        }
        Write-JsonAtomic -Path $demoResultPath -Value $result
    }
    catch {
        $progress = Read-Json $demoProgressPath
        $ambiguous = $null -ne $progress -and [string]$progress.status -eq 'AmbiguousMutation'
        Save-State $(if ($ambiguous) { 'AmbiguousMutation' } else { 'DemoIncomplete' }) $_.Exception.Message
        throw
    }
    finally {
        Remove-Item Env:RACEHUNTER_BASE_URL, Env:RACEHUNTER_DEMO_PROGRESS_PATH, Env:RACEHUNTER_DEMO_ARTIFACT_DIR, Env:RACEHUNTER_DEMO_DEADLINE_UTC -ErrorAction SilentlyContinue
    }
}

Save-State 'DemoComplete'
Save-State 'ReleaseComplete'
Write-Host 'RELEASE_COMPLETION_APPLICATION_EVIDENCE_READY'

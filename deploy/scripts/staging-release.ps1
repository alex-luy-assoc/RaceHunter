[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Initialize', 'Status', 'RecordFailure', 'Reconcile', 'Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo')]
    [string] $Stage,
    [Parameter(Mandatory)] [string] $ProjectId,
    [Parameter(Mandatory)] [string] $Region,
    [Parameter(Mandatory)] [string] $CommitSha,
    [string] $ApiImageDigest,
    [string] $WorkerImageDigest,
    [string] $ReferenceTargetImageDigest,
    [string] $FoundationInputsPath,
    [string] $TerraformInputsPath,
    [string] $SavedPlanPath,
    [string] $ApprovalPath,
    [ValidateSet('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo')]
    [string] $FailedStage,
    [string] $FailureReason,
    [switch] $AmbiguousMutation,
    [switch] $VerifiedReadOnlyInspection,
    [string] $StatePath = (Join-Path $PSScriptRoot '..\..\memory-bank\.local\staging-release\release-state.json')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StagingRelease.psm1') -Force

$imageDigests = @{}
if (-not [string]::IsNullOrWhiteSpace($ApiImageDigest)) { $imageDigests.api = $ApiImageDigest }
if (-not [string]::IsNullOrWhiteSpace($WorkerImageDigest)) { $imageDigests.worker = $WorkerImageDigest }
if (-not [string]::IsNullOrWhiteSpace($ReferenceTargetImageDigest)) { $imageDigests.referenceTarget = $ReferenceTargetImageDigest }

$terraformInputs = @{}
if (-not [string]::IsNullOrWhiteSpace($TerraformInputsPath)) {
    if (-not (Test-Path -LiteralPath $TerraformInputsPath -PathType Leaf)) {
        throw "Input record was not found at '$TerraformInputsPath'."
    }
    $terraformInputs = Get-Content -Raw -LiteralPath $TerraformInputsPath | ConvertFrom-Json -AsHashtable -Depth 100
    if ($terraformInputs -isnot [System.Collections.IDictionary]) {
        throw 'Input record must be a JSON object.'
    }
}

$foundationInputs = @{}
if (-not [string]::IsNullOrWhiteSpace($FoundationInputsPath)) {
    if (-not (Test-Path -LiteralPath $FoundationInputsPath -PathType Leaf)) {
        throw "Foundation input record was not found at '$FoundationInputsPath'."
    }
    $foundationInputs = Get-Content -Raw -LiteralPath $FoundationInputsPath | ConvertFrom-Json -AsHashtable -Depth 100
    if ($foundationInputs -isnot [System.Collections.IDictionary]) {
        throw 'Foundation input record must be a JSON object.'
    }
}

$bindingParameters = @{
    CommitSha = $CommitSha
    ProjectId = $ProjectId
    Region = $Region
    FoundationInputs = $foundationInputs
    ImageDigests = $imageDigests
    TerraformInputs = $terraformInputs
}
if (-not [string]::IsNullOrWhiteSpace($SavedPlanPath)) { $bindingParameters.SavedPlanPath = $SavedPlanPath }
$binding = New-StagingReleaseBinding @bindingParameters

if ($Stage -eq 'Initialize') {
    Initialize-StagingReleaseState -Path $StatePath -Binding $binding
    return
}

if ($Stage -eq 'Status') {
    Get-StagingReleaseState -Path $StatePath
    return
}

if ($Stage -eq 'RecordFailure') {
    if ([string]::IsNullOrWhiteSpace($FailedStage) -or [string]::IsNullOrWhiteSpace($FailureReason)) {
        throw 'RecordFailure requires -FailedStage and -FailureReason.'
    }
    Set-StagingReleaseFailure -Path $StatePath -Stage $FailedStage -Reason $FailureReason -AmbiguousMutation:$AmbiguousMutation
    return
}

if ($Stage -eq 'Reconcile') {
    Complete-StagingReleaseReconciliation -Path $StatePath -VerifiedReadOnlyInspection:$VerifiedReadOnlyInspection
    return
}

$approval = $null
if (-not [string]::IsNullOrWhiteSpace($ApprovalPath)) {
    if (-not (Test-Path -LiteralPath $ApprovalPath -PathType Leaf)) {
        throw "Approval record was not found at '$ApprovalPath'."
    }
    $approval = Get-Content -Raw -LiteralPath $ApprovalPath | ConvertFrom-Json -Depth 100
}

Assert-StagingReleaseApproval -Stage $Stage -Binding $binding -Approval $approval
throw "Stage '$Stage' passed its exact approval contract, but external stage execution is intentionally unavailable in Phase 1."

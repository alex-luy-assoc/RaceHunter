Set-StrictMode -Version Latest

$script:ExternalStages = @('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo')
$script:StateStages = @(
    'Initialized',
    'LocalQualified',
    'PreflightApproved',
    'PreflightComplete',
    'FoundationApproved',
    'FoundationComplete',
    'ImagesPublished',
    'PlanReviewed',
    'DeploymentApproved',
    'Deployed',
    'Validated',
    'SmokeComplete',
    'DemoComplete'
)
$script:ApprovalStages = @('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo')
$script:EvidenceClassifications = @('local', 'local-emulated', 'cloud-read-only', 'deployed-staging', 'live-gemini', 'timed-staging-demo')

function Get-StagingObjectValue {
    param(
        [Parameter(Mandatory)] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($InputObject -is [System.Collections.IDictionary]) {
        $value = $InputObject[$Name]
        if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string] -and $value -isnot [System.Collections.IDictionary]) {
            Write-Output -NoEnumerate $value
            return
        }
        return $value
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    if ($property.Value -is [System.Collections.IEnumerable] -and $property.Value -isnot [string] -and $property.Value -isnot [System.Collections.IDictionary]) {
        Write-Output -NoEnumerate $property.Value
        return
    }
    return $property.Value
}

function Get-StagingObjectPropertyNames {
    param([Parameter(Mandatory)] [object] $InputObject)

    if ($InputObject -is [System.Collections.IDictionary]) {
        return @($InputObject.Keys | ForEach-Object { [string] $_ })
    }
    if ($InputObject -is [pscustomobject]) {
        return @($InputObject.PSObject.Properties | ForEach-Object { $_.Name })
    }
    throw 'Expected a structured object.'
}

function ConvertTo-StagingCanonicalNode {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($key in @($Value.Keys | ForEach-Object { [string] $_ } | Sort-Object -CaseSensitive)) {
            $ordered[$key] = ConvertTo-StagingCanonicalNode -Value $Value[$key]
        }
        return $ordered
    }
    if ($Value -is [pscustomobject]) {
        $ordered = [ordered]@{}
        foreach ($property in @($Value.PSObject.Properties | Sort-Object -Property Name -CaseSensitive)) {
            $ordered[$property.Name] = ConvertTo-StagingCanonicalNode -Value $property.Value
        }
        return $ordered
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = @()
        foreach ($item in $Value) { $items += ,(ConvertTo-StagingCanonicalNode -Value $item) }
        return ,$items
    }
    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime().ToString('O') }
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().ToString('O') }
    return $Value
}

function Get-StagingSha256 {
    param([Parameter(Mandatory)] [string] $Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-StagingCanonicalHash {
    param([AllowNull()] [object] $Value)

    # Approval identity must not depend on PowerShell object or JSON property ordering.
    $canonical = ConvertTo-StagingCanonicalNode -Value $Value
    $json = ConvertTo-Json -InputObject $canonical -Compress -Depth 100
    return Get-StagingSha256 -Value $json
}

function Assert-StagingReleaseStage {
    param([Parameter(Mandatory)] [string] $Stage)

    if ($Stage -notin $script:ExternalStages) {
        throw "Unknown staging release approval stage '$Stage'."
    }
}

function Test-StagingReleaseBindingForStage {
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding
    )

    if ($Stage -in @('Foundation', 'PublishImages')) {
        $foundation = Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputs'
        if ($null -eq $foundation) { return $false }
        try { $names = @(Get-StagingObjectPropertyNames -InputObject $foundation | Sort-Object -CaseSensitive) }
        catch { return $false }
        $requiredNames = @(
            'apiMaxInstances', 'artifactRegistryLocation', 'artifactRegistryRepository', 'budgetCurrency',
            'monthlyBudgetAmount', 'referenceTargetMaxInstances', 'scopedApis', 'stateBucketName',
            'stateBucketPublicAccessPrevention', 'stateBucketVersioning', 'workerMaxInstances'
        ) | Sort-Object -CaseSensitive
        if (($names -join ',') -cne ($requiredNames -join ',')) { return $false }

        $apis = Get-StagingObjectValue -InputObject $foundation -Name 'scopedApis'
        if ($apis -is [string] -or $apis -isnot [System.Collections.IEnumerable] -or $apis -is [System.Collections.IDictionary]) { return $false }
        $apiValues = @($apis)
        if ($apiValues.Count -eq 0 -or @($apiValues | Sort-Object -Unique).Count -ne $apiValues.Count) { return $false }
        foreach ($api in $apiValues) {
            if ($api -isnot [string] -or $api -notmatch '^[a-z][a-z0-9.-]*\.googleapis\.com$') { return $false }
        }

        $bucketName = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketName'
        $repository = Get-StagingObjectValue -InputObject $foundation -Name 'artifactRegistryRepository'
        $location = Get-StagingObjectValue -InputObject $foundation -Name 'artifactRegistryLocation'
        $publicAccessPrevention = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketPublicAccessPrevention'
        $versioning = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketVersioning'
        if ($bucketName -isnot [string] -or $bucketName -notmatch '^[a-z0-9][a-z0-9._-]{1,61}[a-z0-9]$') { return $false }
        if ($repository -isnot [string] -or $repository -notmatch '^[a-z][a-z0-9-]{2,62}$') { return $false }
        if ($location -isnot [string] -or $location -cne (Get-StagingObjectValue -InputObject $Binding -Name 'region')) { return $false }
        if ($publicAccessPrevention -isnot [bool] -or $publicAccessPrevention -cne $true) { return $false }
        if ($versioning -isnot [bool] -or $versioning -cne $true) { return $false }

        $apiMaximum = Get-StagingObjectValue -InputObject $foundation -Name 'apiMaxInstances'
        $workerMaximum = Get-StagingObjectValue -InputObject $foundation -Name 'workerMaxInstances'
        $targetMaximum = Get-StagingObjectValue -InputObject $foundation -Name 'referenceTargetMaxInstances'
        $budget = Get-StagingObjectValue -InputObject $foundation -Name 'monthlyBudgetAmount'
        $currency = Get-StagingObjectValue -InputObject $foundation -Name 'budgetCurrency'
        foreach ($maximum in @($apiMaximum, $workerMaximum, $targetMaximum)) {
            if ($maximum -is [bool] -or $maximum -isnot [ValueType] -or [decimal]$maximum -ne [Math]::Truncate([decimal]$maximum)) { return $false }
        }
        if ([int64]$apiMaximum -lt 1 -or [int64]$apiMaximum -gt 10) { return $false }
        if ([int64]$workerMaximum -ne 1) { return $false }
        if ([int64]$targetMaximum -lt 1 -or [int64]$targetMaximum -gt 10) { return $false }
        if ($budget -is [bool] -or $budget -isnot [ValueType] -or [decimal]$budget -le 0 -or [decimal]$budget -gt 1000) { return $false }
        if ($currency -isnot [string] -or $currency -cne 'USD') { return $false }

        $foundationHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputHash')
        if ($foundationHash -notmatch '^[a-f0-9]{64}$' -or $foundationHash -cne (Get-StagingCanonicalHash -Value $foundation)) { return $false }
    }
    elseif ($Stage -cne 'Deploy') { return $true }

    $images = Get-StagingObjectValue -InputObject $Binding -Name 'imageDigests'
    $inputs = Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputs'
    if ($Stage -ceq 'Deploy' -and ($null -eq $images -or $null -eq $inputs)) { return $false }

    if ($Stage -ceq 'Deploy') {
        try {
            $imageNames = @(Get-StagingObjectPropertyNames -InputObject $images | Sort-Object -CaseSensitive)
            $inputNames = @(Get-StagingObjectPropertyNames -InputObject $inputs)
        }
        catch { return $false }

        if (($imageNames -join ',') -cne 'api,referenceTarget,worker' -or $inputNames.Count -eq 0) { return $false }
        foreach ($imageName in $imageNames) {
            if ([string](Get-StagingObjectValue -InputObject $images -Name $imageName) -notmatch '@sha256:[a-fA-F0-9]{64}$') { return $false }
        }

        $inputHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputHash')
        $savedPlanHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'savedPlanHash')
        if ($inputHash -notmatch '^[a-f0-9]{64}$' -or $savedPlanHash -notmatch '^[a-f0-9]{64}$') { return $false }
        if ($inputHash -cne (Get-StagingCanonicalHash -Value $inputs)) { return $false }
    }

    $bindingMaterial = [ordered]@{
        commitSha = Get-StagingObjectValue -InputObject $Binding -Name 'commitSha'
        projectId = Get-StagingObjectValue -InputObject $Binding -Name 'projectId'
        region = Get-StagingObjectValue -InputObject $Binding -Name 'region'
        foundationInputs = Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputs'
        foundationInputHash = Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputHash'
        imageDigests = Get-StagingObjectValue -InputObject $Binding -Name 'imageDigests'
        terraformInputs = Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputs'
        terraformInputHash = Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputHash'
        savedPlanHash = Get-StagingObjectValue -InputObject $Binding -Name 'savedPlanHash'
    }
    return (Get-StagingCanonicalHash -Value $bindingMaterial) -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash')
}

function Assert-StagingReleaseBindingForStage {
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding
    )

    if (-not (Test-StagingReleaseBindingForStage -Stage $Stage -Binding $Binding)) {
        throw "Stage '$Stage' is missing its exact protected foundation, immutable images, verified inputs, ceilings, or saved-plan binding."
    }
}

function New-StagingReleaseBinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidatePattern('^[a-fA-F0-9]{40}$')] [string] $CommitSha,
        [Parameter(Mandatory)] [ValidatePattern('^[a-z][a-z0-9-]{4,28}[a-z0-9]$')] [string] $ProjectId,
        [Parameter(Mandatory)] [ValidatePattern('^[a-z]+-[a-z]+[0-9]$')] [string] $Region,
        [System.Collections.IDictionary] $FoundationInputs = @{},
        [System.Collections.IDictionary] $ImageDigests = @{},
        [System.Collections.IDictionary] $TerraformInputs = @{},
        [string] $SavedPlanPath
    )

    foreach ($entry in $ImageDigests.GetEnumerator()) {
        if ([string]$entry.Value -notmatch '@sha256:[a-fA-F0-9]{64}$') {
            throw "Image '$($entry.Key)' must be bound to an immutable digest."
        }
    }

    $savedPlanHash = $null
    if (-not [string]::IsNullOrWhiteSpace($SavedPlanPath)) {
        if (-not (Test-Path -LiteralPath $SavedPlanPath -PathType Leaf)) {
            throw "Saved plan does not exist at '$SavedPlanPath'."
        }
        $savedPlanHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $SavedPlanPath).Hash.ToLowerInvariant()
    }

    $canonicalFoundation = ConvertTo-StagingCanonicalNode -Value $FoundationInputs
    $foundationInputHash = Get-StagingCanonicalHash -Value $canonicalFoundation
    $canonicalImages = ConvertTo-StagingCanonicalNode -Value $ImageDigests
    $canonicalInputs = ConvertTo-StagingCanonicalNode -Value $TerraformInputs
    $terraformInputHash = Get-StagingCanonicalHash -Value $canonicalInputs
    $bindingMaterial = [ordered]@{
        commitSha = $CommitSha.ToLowerInvariant()
        projectId = $ProjectId
        region = $Region
        foundationInputs = $canonicalFoundation
        foundationInputHash = $foundationInputHash
        imageDigests = $canonicalImages
        terraformInputs = $canonicalInputs
        terraformInputHash = $terraformInputHash
        savedPlanHash = $savedPlanHash
    }

    return [pscustomobject][ordered]@{
        commitSha = $bindingMaterial.commitSha
        projectId = $bindingMaterial.projectId
        region = $bindingMaterial.region
        foundationInputs = $bindingMaterial.foundationInputs
        foundationInputHash = $bindingMaterial.foundationInputHash
        imageDigests = $bindingMaterial.imageDigests
        terraformInputs = $bindingMaterial.terraformInputs
        terraformInputHash = $bindingMaterial.terraformInputHash
        savedPlanHash = $bindingMaterial.savedPlanHash
        bindingHash = Get-StagingCanonicalHash -Value $bindingMaterial
    }
}

function New-StagingReleaseApproval {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding
    )

    Assert-StagingReleaseStage -Stage $Stage
    Assert-StagingReleaseBindingForStage -Stage $Stage -Binding $Binding
    return [pscustomobject][ordered]@{
        stage = $Stage
        bindingHash = Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash'
        commitSha = Get-StagingObjectValue -InputObject $Binding -Name 'commitSha'
        projectId = Get-StagingObjectValue -InputObject $Binding -Name 'projectId'
        region = Get-StagingObjectValue -InputObject $Binding -Name 'region'
        approvedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        valid = $true
        invalidationReason = $null
    }
}

function Test-StagingReleaseApprovalRecord {
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Approval
    )

    try { $names = @(Get-StagingObjectPropertyNames -InputObject $Approval | Sort-Object -CaseSensitive) }
    catch { return $false }
    $requiredNames = @('approvedAtUtc', 'bindingHash', 'commitSha', 'invalidationReason', 'projectId', 'region', 'stage', 'valid') | Sort-Object -CaseSensitive
    if (($names -join ',') -cne ($requiredNames -join ',')) { return $false }

    $valid = Get-StagingObjectValue -InputObject $Approval -Name 'valid'
    $approvalStage = Get-StagingObjectValue -InputObject $Approval -Name 'stage'
    $bindingHash = Get-StagingObjectValue -InputObject $Approval -Name 'bindingHash'
    $commitSha = Get-StagingObjectValue -InputObject $Approval -Name 'commitSha'
    $projectId = Get-StagingObjectValue -InputObject $Approval -Name 'projectId'
    $region = Get-StagingObjectValue -InputObject $Approval -Name 'region'
    $approvedAtUtc = Get-StagingObjectValue -InputObject $Approval -Name 'approvedAtUtc'
    $invalidationReason = Get-StagingObjectValue -InputObject $Approval -Name 'invalidationReason'

    if ($valid -isnot [bool] -or $valid -cne $true) { return $false }
    if ($approvalStage -isnot [string] -or $approvalStage -cne $Stage -or $approvalStage -notin $script:ExternalStages) { return $false }
    if ($bindingHash -isnot [string] -or $bindingHash -notmatch '^[a-f0-9]{64}$') { return $false }
    if ($commitSha -isnot [string] -or $commitSha -notmatch '^[a-f0-9]{40}$') { return $false }
    if ($projectId -isnot [string] -or $projectId -notmatch '^[a-z][a-z0-9-]{4,28}[a-z0-9]$') { return $false }
    if ($region -isnot [string] -or $region -notmatch '^[a-z]+-[a-z]+[0-9]$') { return $false }
    if ($null -ne $invalidationReason) { return $false }
    if ($approvedAtUtc -isnot [string] -or ($approvedAtUtc -notmatch '(?:Z|\+00:00)$')) { return $false }
    $parsedTimestamp = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        $approvedAtUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsedTimestamp) -or $parsedTimestamp.Offset -ne [TimeSpan]::Zero) {
        return $false
    }
    return $true
}

function Test-StagingReleaseApproval {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding,
        [AllowNull()] [object] $Approval
    )

    Assert-StagingReleaseStage -Stage $Stage
    if (-not (Test-StagingReleaseBindingForStage -Stage $Stage -Binding $Binding)) { return $false }
    if ($null -eq $Approval) { return $false }
    if (-not (Test-StagingReleaseApprovalRecord -Stage $Stage -Approval $Approval)) { return $false }
    return (
        (Get-StagingObjectValue -InputObject $Approval -Name 'stage') -ceq $Stage -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'bindingHash') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash') -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'commitSha') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'commitSha') -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'projectId') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'projectId') -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'region') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'region')
    )
}

function Assert-StagingReleaseApproval {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding,
        [AllowNull()] [object] $Approval
    )

    if (-not (Test-StagingReleaseApproval -Stage $Stage -Binding $Binding -Approval $Approval)) {
        throw "Stage '$Stage' is default denied. Supply a fresh approval bound to this exact stage and release identity."
    }
}

function Write-StagingReleaseState {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $State
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $directory = [IO.Path]::GetDirectoryName($fullPath)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = [IO.Path]::Combine($directory, ".$([IO.Path]::GetFileName($fullPath)).$([Guid]::NewGuid().ToString('N')).tmp")
    $json = ConvertTo-Json -InputObject $State -Depth 100
    try {
        # A partially written state or evidence manifest must never become a resumable boundary.
        [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $fullPath, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) }
    }
}

function Get-StagingReleaseState {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Staging release state does not exist at '$Path'."
    }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -Depth 100
}

function Initialize-StagingReleaseState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Binding
    )

    if (Test-Path -LiteralPath $Path) {
        $existing = Get-StagingReleaseState -Path $Path
        if ($existing.binding.bindingHash -cne (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash')) {
            throw 'Existing release state is bound to a different release identity; reconcile drift explicitly.'
        }
        return $existing
    }

    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $state = [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        binding = $Binding
        currentStage = 'Initialized'
        transitions = @([pscustomobject][ordered]@{ stage = 'Initialized'; observedAtUtc = $now })
        approvals = [pscustomobject]@{}
        evidence = @()
        failures = @()
        recovery = [pscustomobject][ordered]@{
            requiresReadOnlyInspection = $false
            ambiguousMutation = $false
            failedStage = $null
            failureReason = $null
            reconciledAtUtc = $null
        }
    }
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Set-StagingReleaseStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [ValidateSet('LocalQualified', 'PreflightApproved', 'PreflightComplete', 'FoundationApproved', 'FoundationComplete', 'ImagesPublished', 'PlanReviewed', 'DeploymentApproved', 'Deployed', 'Validated', 'SmokeComplete', 'DemoComplete')] [string] $Stage
    )

    $state = Get-StagingReleaseState -Path $Path
    if ([bool]$state.recovery.requiresReadOnlyInspection -or [bool]$state.recovery.ambiguousMutation) {
        throw 'Forward transition blocked until explicit verified read-only reconciliation completes.'
    }
    $currentIndex = [Array]::IndexOf($script:StateStages, [string]$state.currentStage)
    $requestedIndex = [Array]::IndexOf($script:StateStages, $Stage)
    if ($currentIndex -lt 0 -or $requestedIndex -ne ($currentIndex + 1)) {
        throw "Invalid release transition from '$($state.currentStage)' to '$Stage'. Resume only at the next verified boundary."
    }

    $state.currentStage = $Stage
    $state.transitions = @($state.transitions) + [pscustomobject][ordered]@{ stage = $Stage; observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Set-StagingApprovalsInvalid {
    param(
        [Parameter(Mandatory)] [object] $State,
        [Parameter(Mandatory)] [string] $FromStage,
        [Parameter(Mandatory)] [string] $Reason
    )

    $changedRank = [Array]::IndexOf($script:ApprovalStages, $FromStage)
    foreach ($property in @($State.approvals.PSObject.Properties)) {
        $approvalRank = [Array]::IndexOf($script:ApprovalStages, $property.Name)
        if ($approvalRank -ge $changedRank) {
            $property.Value.valid = $false
            $property.Value.invalidationReason = $Reason
        }
    }
}

function Set-StagingReleaseFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [ValidateSet('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo')] [string] $Stage,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Reason,
        [switch] $AmbiguousMutation
    )

    $state = Get-StagingReleaseState -Path $Path
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    Set-StagingApprovalsInvalid -State $state -FromStage $Stage -Reason "Release failure at $Stage requires verified read-only reconciliation and fresh downstream approval."
    $state.failures = @($state.failures) + [pscustomobject][ordered]@{
        stage = $Stage
        observedAtUtc = $now
        reason = $Reason
        ambiguousMutation = [bool]$AmbiguousMutation
    }
    $state.recovery.requiresReadOnlyInspection = $true
    $state.recovery.ambiguousMutation = [bool]$AmbiguousMutation
    $state.recovery.failedStage = $Stage
    $state.recovery.failureReason = $Reason
    $state.recovery.reconciledAtUtc = $null
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Complete-StagingReleaseReconciliation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [switch] $VerifiedReadOnlyInspection
    )

    if (-not $VerifiedReadOnlyInspection) {
        throw 'Reconciliation is default denied until verified read-only inspection is explicitly confirmed.'
    }
    $state = Get-StagingReleaseState -Path $Path
    # Ambiguous provider outcomes fail closed: only an explicit, observed read-only reconciliation reopens progress.
    $state.recovery.requiresReadOnlyInspection = $false
    $state.recovery.ambiguousMutation = $false
    $state.recovery.reconciledAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Add-StagingReleaseApproval {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Approval
    )

    $state = Get-StagingReleaseState -Path $Path
    $stage = [string](Get-StagingObjectValue -InputObject $Approval -Name 'stage')
    Assert-StagingReleaseStage -Stage $stage
    if (-not (Test-StagingReleaseApproval -Stage $stage -Binding $state.binding -Approval $Approval)) {
        throw 'Approval record is malformed, invalid, or does not match the durable release state.'
    }
    $state.approvals | Add-Member -NotePropertyName $stage -NotePropertyValue $Approval -Force
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Add-StagingReleaseEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Evidence
    )

    $state = Get-StagingReleaseState -Path $Path
    $state.evidence = @($state.evidence) + $Evidence
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Update-StagingReleaseBinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [ValidateSet('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo')] [string] $ChangedAtStage
    )

    $state = Get-StagingReleaseState -Path $Path
    if ($state.binding.bindingHash -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash')) { return $state }

    Set-StagingApprovalsInvalid -State $state -FromStage $ChangedAtStage -Reason "Binding drift detected at $ChangedAtStage; review inputs and issue a fresh approval."
    $state.binding = $Binding
    $state.recovery.requiresReadOnlyInspection = $true
    $state.recovery.ambiguousMutation = $false
    $state.recovery.failedStage = $ChangedAtStage
    $state.recovery.failureReason = 'Release binding drift detected.'
    $state.recovery.reconciledAtUtc = $null
    Write-StagingReleaseState -Path $Path -State $state
    return $state
}

function Test-StagingSensitiveValue {
    param(
        [AllowNull()] [object] $Value,
        [string] $Path = '$'
    )

    if ($null -eq $Value) { return }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            $name = [string]$key
            if ($name -match '(?i)(authorization|cookie|password|secret|token|connectionstring|databaseurl|democontrolkey|privatekey)') {
                throw "Evidence contains a forbidden sensitive field at $Path.$name."
            }
            Test-StagingSensitiveValue -Value $Value[$key] -Path "$Path.$name"
        }
        return
    }
    if ($Value -is [pscustomobject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -match '(?i)(authorization|cookie|password|secret|token|connectionstring|databaseurl|democontrolkey|privatekey)') {
                throw "Evidence contains a forbidden sensitive field at $Path.$($property.Name)."
            }
            Test-StagingSensitiveValue -Value $property.Value -Path "$Path.$($property.Name)"
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($item in $Value) { Test-StagingSensitiveValue -Value $item -Path $Path }
        return
    }
    if ($Value -is [string]) {
        $forbiddenPatterns = @(
            '(?i)(?:^|[\{,])\s*"[A-Za-z0-9_.-]+"\s*:',
            '(?i)authorization\s*[:=]',
            '(?i)bearer\s+[A-Za-z0-9._~+/=-]+',
            '(?i)(?:set-)?cookie\s*[:=]',
            '(?i)(?:password|pwd)\s*[:=]',
            '(?i)(?:access|refresh|id)[_-]?token\s*[:=]',
            '(?i)(?:client[_-]?secret|api[_-]?key|demo[_-]?(?:control[_-]?)?key)\s*[:=]',
            '(?i)\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]+\b',
            '\bAIza[0-9A-Za-z_-]{35}\b',
            '(?i)https?://\S+[?&](?:X-Goog-Signature|X-Amz-Signature|sig|signature|token)=',
            '(?i)(?:Host|Server|Data Source)\s*=.*;.*(?:Password|Pwd)\s*=',
            '(?i)(?:postgres(?:ql)?|mysql|sqlserver)://',
            '(?i)[a-z][a-z0-9+.-]*://[^\s/:]+:[^\s/@]+@',
            '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        )
        foreach ($pattern in $forbiddenPatterns) {
            if ($Value -match $pattern) { throw "Evidence contains forbidden secret-bearing or raw-provider material at $Path." }
        }
    }
}

function ConvertTo-StagingRedactedText {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Text)

    $redacted = $Text
    $redacted = $redacted -replace '(?i)(authorization\s*[:=]\s*)([^\s,;]+(?:\s+[^\s,;]+)?)', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)(bearer\s+)[A-Za-z0-9._~+/=-]+', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)((?:set-)?cookie\s*[:=]\s*)[^\r\n]+', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)((?:password|client[_-]?secret|demo[_-]?(?:control[_-]?)?key)\s*[:=]\s*)[^\s,;]+', '$1[REDACTED]'
    return $redacted
}

function Assert-StagingEvidenceProperties {
    param(
        [Parameter(Mandatory)] [object] $InputObject,
        [Parameter(Mandatory)] [string[]] $Allowed,
        [Parameter(Mandatory)] [string[]] $Required,
        [Parameter(Mandatory)] [string] $Path
    )

    $names = @(Get-StagingObjectPropertyNames -InputObject $InputObject)
    foreach ($name in $names) {
        if ($name -cnotin $Allowed) { throw "Evidence contains unknown property '$name' at $Path." }
    }
    foreach ($name in $Required) {
        if ($name -cnotin $names) { throw "Evidence is missing required property '$name' at $Path." }
    }
}

function Assert-StagingEvidenceString {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Path
    )

    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "Evidence value at $Path must be a non-empty string."
    }
}

function Assert-StagingEvidenceSummary {
    param(
        [Parameter(Mandatory)] [object] $Value,
        [Parameter(Mandatory)] [ValidateSet('expected', 'observed')] [string] $Kind
    )

    if ($Kind -ceq 'expected') {
        Assert-StagingEvidenceProperties -InputObject $Value -Allowed @('summary') -Required @('summary') -Path '$.expected'
    }
    else {
        Assert-StagingEvidenceProperties -InputObject $Value -Allowed @('summary', 'status') -Required @('summary', 'status') -Path '$.observed'
        $status = Get-StagingObjectValue -InputObject $Value -Name 'status'
        if ($status -isnot [string] -or $status -cnotin @('passed', 'failed', 'blocked')) {
            throw 'Evidence observed status must be passed, failed, or blocked.'
        }
    }
    Assert-StagingEvidenceString -Value (Get-StagingObjectValue -InputObject $Value -Name 'summary') -Path "`$.$Kind.summary"
}

function Protect-StagingEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Evidence)

    $properties = @('schemaVersion', 'classification', 'observedAtUtc', 'environment', 'method', 'expected', 'observed', 'commitSha', 'imageDigests', 'identifiers', 'artifactReference')
    Assert-StagingEvidenceProperties -InputObject $Evidence -Allowed $properties -Required $properties -Path '$'

    $schemaVersion = Get-StagingObjectValue -InputObject $Evidence -Name 'schemaVersion'
    $classification = Get-StagingObjectValue -InputObject $Evidence -Name 'classification'
    $observedAtUtc = Get-StagingObjectValue -InputObject $Evidence -Name 'observedAtUtc'
    $commitSha = Get-StagingObjectValue -InputObject $Evidence -Name 'commitSha'
    if ($schemaVersion -isnot [string] -or $schemaVersion -cne '1.0') { throw 'Unsupported evidence schema version.' }
    if ($classification -isnot [string] -or $classification -cnotin $script:EvidenceClassifications) { throw 'Evidence classification is not environment-qualified.' }
    Assert-StagingEvidenceString -Value $observedAtUtc -Path '$.observedAtUtc'
    $parsedTimestamp = [DateTimeOffset]::MinValue
    $timestampValid = [DateTimeOffset]::TryParse(
        $observedAtUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsedTimestamp)
    if (-not $timestampValid -or -not $observedAtUtc.EndsWith('Z', [StringComparison]::Ordinal) -or $parsedTimestamp.Offset -ne [TimeSpan]::Zero) {
        throw 'Evidence timestamp must be a valid UTC date-time ending in Z.'
    }
    if ($commitSha -isnot [string] -or $commitSha -notmatch '^[a-fA-F0-9]{40}$') { throw 'Evidence commit identity is invalid.' }

    foreach ($name in @('environment', 'method', 'artifactReference')) {
        Assert-StagingEvidenceString -Value (Get-StagingObjectValue -InputObject $Evidence -Name $name) -Path "`$.$name"
    }
    Assert-StagingEvidenceSummary -Value (Get-StagingObjectValue -InputObject $Evidence -Name 'expected') -Kind expected
    Assert-StagingEvidenceSummary -Value (Get-StagingObjectValue -InputObject $Evidence -Name 'observed') -Kind observed

    $imageDigests = Get-StagingObjectValue -InputObject $Evidence -Name 'imageDigests'
    foreach ($name in @(Get-StagingObjectPropertyNames -InputObject $imageDigests)) {
        $digest = Get-StagingObjectValue -InputObject $imageDigests -Name $name
        if ($name -notmatch '^[A-Za-z][A-Za-z0-9._-]{0,63}$' -or $digest -isnot [string] -or $digest -notmatch '@sha256:[a-fA-F0-9]{64}$') {
            throw "Evidence image digest '$name' is invalid."
        }
    }

    $identifiers = Get-StagingObjectValue -InputObject $Evidence -Name 'identifiers'
    foreach ($name in @(Get-StagingObjectPropertyNames -InputObject $identifiers)) {
        if ($name -notmatch '^[A-Za-z][A-Za-z0-9._-]{0,63}$') { throw "Evidence identifier name '$name' is invalid." }
        Assert-StagingEvidenceString -Value (Get-StagingObjectValue -InputObject $identifiers -Name $name) -Path "`$.identifiers.$name"
    }

    # Reject secret-shaped material even after callers have applied their own redaction.
    Test-StagingSensitiveValue -Value $Evidence
    $json = ConvertTo-Json -InputObject $Evidence -Depth 100
    return $json | ConvertFrom-Json -Depth 100 -DateKind String
}

function Publish-StagingEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Evidence
    )

    $protected = Protect-StagingEvidence -Evidence $Evidence
    $manifest = [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        records = @($protected)
    }
    Test-StagingSensitiveValue -Value $manifest
    Write-StagingReleaseState -Path $Path -State $manifest
}

Export-ModuleMember -Function @(
    'New-StagingReleaseBinding',
    'New-StagingReleaseApproval',
    'Test-StagingReleaseApproval',
    'Assert-StagingReleaseApproval',
    'Initialize-StagingReleaseState',
    'Get-StagingReleaseState',
    'Set-StagingReleaseStage',
    'Add-StagingReleaseApproval',
    'Add-StagingReleaseEvidence',
    'Update-StagingReleaseBinding',
    'Set-StagingReleaseFailure',
    'Complete-StagingReleaseReconciliation',
    'ConvertTo-StagingRedactedText',
    'Protect-StagingEvidence',
    'Publish-StagingEvidence'
)

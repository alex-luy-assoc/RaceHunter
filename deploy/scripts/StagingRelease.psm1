Set-StrictMode -Version Latest

$script:ExternalStages = @('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo', 'ReleaseCompletion', 'RecoveryCompletion')
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
$script:ApprovalStages = @('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo', 'ReleaseCompletion', 'RecoveryCompletion')
$script:EvidenceClassifications = @('local', 'local-emulated', 'cloud-read-only', 'deployed-staging', 'live-gemini', 'timed-staging-demo')
$script:RequiredFoundationApis = @(
    'aiplatform.googleapis.com',
    'artifactregistry.googleapis.com',
    'billingbudgets.googleapis.com',
    'cloudresourcemanager.googleapis.com',
    'cloudtrace.googleapis.com',
    'iam.googleapis.com',
    'iamcredentials.googleapis.com',
    'logging.googleapis.com',
    'monitoring.googleapis.com',
    'pubsub.googleapis.com',
    'run.googleapis.com',
    'secretmanager.googleapis.com',
    'serviceusage.googleapis.com',
    'sqladmin.googleapis.com',
    'storage.googleapis.com'
)
$script:TerraformApplicationInputNames = @(
    'api_max_instance_count',
    'billing_account_id',
    'deletion_protection',
    'manual_target_secret_ids',
    'monthly_budget_usd',
    'reference_target_max_instance_count',
    'worker_max_instance_count'
)
$script:PreflightApprovalLifetime = [TimeSpan]::FromMinutes(15)
$script:PreflightApprovalFutureSkew = [TimeSpan]::FromMinutes(2)

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

function Get-StagingTerraformVariables {
    param([Parameter(Mandatory)] [object] $Binding)

    $foundation = Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputs'
    $inputs = Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputs'
    $images = Get-StagingObjectValue -InputObject $Binding -Name 'imageDigests'
    $inputNames = @(Get-StagingObjectPropertyNames -InputObject $inputs | Sort-Object -CaseSensitive)
    if (($inputNames -join ',') -cne (@($script:TerraformApplicationInputNames | Sort-Object -CaseSensitive) -join ',')) {
        throw 'Terraform inputs must contain exactly the reviewed application input schema.'
    }

    $billingAccount = Get-StagingObjectValue -InputObject $inputs -Name 'billing_account_id'
    $foundationBillingAccount = Get-StagingObjectValue -InputObject $foundation -Name 'billingAccountId'
    if ($billingAccount -isnot [string] -or $billingAccount -notmatch '^[A-Z0-9]{6}-[A-Z0-9]{6}-[A-Z0-9]{6}$' -or $billingAccount -cne $foundationBillingAccount) {
        throw 'billing_account_id must be present and match the approved foundation billing account.'
    }

    $matches = [ordered]@{
        monthly_budget_usd = 'monthlyBudgetAmount'
        api_max_instance_count = 'apiMaxInstances'
        worker_max_instance_count = 'workerMaxInstances'
        reference_target_max_instance_count = 'referenceTargetMaxInstances'
    }
    foreach ($inputName in $matches.Keys) {
        $inputValue = Get-StagingObjectValue -InputObject $inputs -Name $inputName
        $foundationValue = Get-StagingObjectValue -InputObject $foundation -Name $matches[$inputName]
        if ($inputValue -is [bool] -or $inputValue -isnot [ValueType] -or [decimal]$inputValue -ne [decimal]$foundationValue) {
            throw "Terraform input '$inputName' does not match its approved foundation value."
        }
    }

    $deletionProtection = Get-StagingObjectValue -InputObject $inputs -Name 'deletion_protection'
    if ($deletionProtection -isnot [bool] -or $deletionProtection -cne $true -or $deletionProtection -cne (Get-StagingObjectValue -InputObject $foundation -Name 'deletionProtection')) {
        throw 'deletion_protection must remain true and match the approved foundation.'
    }

    $manualTargetSecretIds = Get-StagingObjectValue -InputObject $inputs -Name 'manual_target_secret_ids'
    if ($manualTargetSecretIds -is [string] -or $manualTargetSecretIds -is [System.Collections.IDictionary] -or $manualTargetSecretIds -isnot [System.Collections.IEnumerable]) {
        throw 'manual_target_secret_ids must be an array of secret resource IDs.'
    }
    $secretIds = @($manualTargetSecretIds)
    if (@($secretIds | Sort-Object -Unique).Count -ne $secretIds.Count) { throw 'manual_target_secret_ids must not contain duplicates.' }
    foreach ($secretId in $secretIds) {
        if ($secretId -isnot [string] -or $secretId -notmatch '^[A-Za-z0-9_-]{1,255}$') { throw 'manual_target_secret_ids contains an invalid secret resource ID.' }
    }

    return [ordered]@{
        project_id = Get-StagingObjectValue -InputObject $Binding -Name 'projectId'
        region = Get-StagingObjectValue -InputObject $Binding -Name 'region'
        billing_account_id = $billingAccount
        api_image = Get-StagingObjectValue -InputObject $images -Name 'api'
        worker_image = Get-StagingObjectValue -InputObject $images -Name 'worker'
        reference_target_image = Get-StagingObjectValue -InputObject $images -Name 'referenceTarget'
        monthly_budget_usd = Get-StagingObjectValue -InputObject $inputs -Name 'monthly_budget_usd'
        api_max_instance_count = Get-StagingObjectValue -InputObject $inputs -Name 'api_max_instance_count'
        worker_max_instance_count = Get-StagingObjectValue -InputObject $inputs -Name 'worker_max_instance_count'
        reference_target_max_instance_count = Get-StagingObjectValue -InputObject $inputs -Name 'reference_target_max_instance_count'
        deletion_protection = $deletionProtection
        manual_target_secret_ids = @($secretIds)
    }
}

function Test-StagingReleaseBindingForStage {
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding
    )

    if ($Stage -in @('Foundation', 'PublishImages', 'Plan', 'Deploy')) {
        $foundation = Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputs'
        if ($null -eq $foundation) { return $false }
        try { $names = @(Get-StagingObjectPropertyNames -InputObject $foundation | Sort-Object -CaseSensitive) }
        catch { return $false }
        $requiredNames = @(
            'apiMaxInstances', 'artifactRegistryImmutableTags', 'artifactRegistryLocation', 'artifactRegistryRepository', 'billingAccountId', 'budgetCurrency',
            'deletionProtection',
            'monthlyBudgetAmount', 'referenceTargetMaxInstances', 'scopedApis', 'stateBucketName',
            'stateBucketPublicAccessPrevention', 'stateBucketRetentionDays', 'stateBucketUniformAccess',
            'stateBucketVersioning', 'workerMaxInstances'
        ) | Sort-Object -CaseSensitive
        if (($names -join ',') -cne ($requiredNames -join ',')) { return $false }

        $apis = Get-StagingObjectValue -InputObject $foundation -Name 'scopedApis'
        if ($apis -is [string] -or $apis -isnot [System.Collections.IEnumerable] -or $apis -is [System.Collections.IDictionary]) { return $false }
        $apiValues = @($apis)
        if ($apiValues.Count -eq 0 -or @($apiValues | Sort-Object -Unique).Count -ne $apiValues.Count) { return $false }
        foreach ($api in $apiValues) {
            if ($api -isnot [string] -or $api -notmatch '^[a-z][a-z0-9.-]*\.googleapis\.com$') { return $false }
        }
        if ((@($apiValues | Sort-Object) -join ',') -cne (@($script:RequiredFoundationApis | Sort-Object) -join ',')) { return $false }

        $bucketName = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketName'
        $repository = Get-StagingObjectValue -InputObject $foundation -Name 'artifactRegistryRepository'
        $location = Get-StagingObjectValue -InputObject $foundation -Name 'artifactRegistryLocation'
        $publicAccessPrevention = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketPublicAccessPrevention'
        $uniformAccess = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketUniformAccess'
        $versioning = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketVersioning'
        $retentionDays = Get-StagingObjectValue -InputObject $foundation -Name 'stateBucketRetentionDays'
        $immutableTags = Get-StagingObjectValue -InputObject $foundation -Name 'artifactRegistryImmutableTags'
        $billingAccountId = Get-StagingObjectValue -InputObject $foundation -Name 'billingAccountId'
        $deletionProtection = Get-StagingObjectValue -InputObject $foundation -Name 'deletionProtection'
        if ($bucketName -isnot [string] -or $bucketName -notmatch '^[a-z0-9][a-z0-9._-]{1,61}[a-z0-9]$') { return $false }
        if ($repository -isnot [string] -or $repository -notmatch '^[a-z][a-z0-9-]{2,62}$') { return $false }
        if ($location -isnot [string] -or $location -cne (Get-StagingObjectValue -InputObject $Binding -Name 'region')) { return $false }
        if ($publicAccessPrevention -isnot [bool] -or $publicAccessPrevention -cne $true) { return $false }
        if ($uniformAccess -isnot [bool] -or $uniformAccess -cne $true) { return $false }
        if ($versioning -isnot [bool] -or $versioning -cne $true) { return $false }
        if ($retentionDays -is [bool] -or $retentionDays -isnot [ValueType] -or [decimal]$retentionDays -ne [Math]::Truncate([decimal]$retentionDays) -or [int64]$retentionDays -lt 7 -or [int64]$retentionDays -gt 365) { return $false }
        if ($immutableTags -isnot [bool] -or $immutableTags -cne $true) { return $false }
        if ($billingAccountId -isnot [string] -or $billingAccountId -notmatch '^[A-Z0-9]{6}-[A-Z0-9]{6}-[A-Z0-9]{6}$') { return $false }
        if ($deletionProtection -isnot [bool] -or $deletionProtection -cne $true) { return $false }

        $apiMaximum = Get-StagingObjectValue -InputObject $foundation -Name 'apiMaxInstances'
        $workerMaximum = Get-StagingObjectValue -InputObject $foundation -Name 'workerMaxInstances'
        $targetMaximum = Get-StagingObjectValue -InputObject $foundation -Name 'referenceTargetMaxInstances'
        $budget = Get-StagingObjectValue -InputObject $foundation -Name 'monthlyBudgetAmount'
        $currency = Get-StagingObjectValue -InputObject $foundation -Name 'budgetCurrency'
        foreach ($maximum in @($apiMaximum, $workerMaximum, $targetMaximum)) {
            if ($maximum -is [bool] -or $maximum -isnot [ValueType] -or [decimal]$maximum -ne [Math]::Truncate([decimal]$maximum)) { return $false }
        }
        if ([int64]$apiMaximum -lt 1 -or [int64]$apiMaximum -gt 2) { return $false }
        if ([int64]$workerMaximum -ne 1) { return $false }
        if ([int64]$targetMaximum -lt 1 -or [int64]$targetMaximum -gt 2) { return $false }
        if ($budget -is [bool] -or $budget -isnot [ValueType] -or [decimal]$budget -ne [Math]::Truncate([decimal]$budget) -or [decimal]$budget -le 0 -or [decimal]$budget -gt 100) { return $false }
        if ($currency -isnot [string] -or $currency -cne 'USD') { return $false }

        $foundationHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'foundationInputHash')
        if ($foundationHash -notmatch '^[a-f0-9]{64}$' -or $foundationHash -cne (Get-StagingCanonicalHash -Value $foundation)) { return $false }
    }
    elseif ($Stage -notin @('Plan', 'Deploy')) { return $true }

    $images = Get-StagingObjectValue -InputObject $Binding -Name 'imageDigests'
    $inputs = Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputs'
    if ($null -eq $images -or $null -eq $inputs) { return $false }

    if ($Stage -in @('Plan', 'Deploy')) {
        try {
            $imageNames = @(Get-StagingObjectPropertyNames -InputObject $images | Sort-Object -CaseSensitive)
            $inputNames = @(Get-StagingObjectPropertyNames -InputObject $inputs)
        }
        catch { return $false }

        if (($imageNames -join ',') -cne 'api,referenceTarget,worker' -or $inputNames.Count -eq 0) { return $false }
        foreach ($imageName in $imageNames) {
            if ([string](Get-StagingObjectValue -InputObject $images -Name $imageName) -notmatch '@sha256:[a-fA-F0-9]{64}$') { return $false }
        }
        $repositoryPrefix = "$(Get-StagingObjectValue -InputObject $Binding -Name 'region')-docker.pkg.dev/$(Get-StagingObjectValue -InputObject $Binding -Name 'projectId')/$(Get-StagingObjectValue -InputObject $foundation -Name 'artifactRegistryRepository')"
        $expectedImages = [ordered]@{
            api = "$repositoryPrefix/racehunter-api"
            worker = "$repositoryPrefix/racehunter-worker"
            referenceTarget = "$repositoryPrefix/racehunter-reference-target"
        }
        foreach ($imageName in $expectedImages.Keys) {
            $digest = [string](Get-StagingObjectValue -InputObject $images -Name $imageName)
            if ($digest -notmatch "^$([Regex]::Escape($expectedImages[$imageName]))@sha256:[a-fA-F0-9]{64}$") { return $false }
        }

        try { $plannedVariables = Get-StagingTerraformVariables -Binding $Binding }
        catch { return $false }
        $inputHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'terraformInputHash')
        $savedPlanHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'savedPlanHash')
        if ($inputHash -notmatch '^[a-f0-9]{64}$') { return $false }
        if ($Stage -ceq 'Deploy' -and $savedPlanHash -notmatch '^[a-f0-9]{64}$') { return $false }
        $plannedVariablesJson = ConvertTo-Json -InputObject (ConvertTo-StagingCanonicalNode -Value $plannedVariables) -Depth 100
        if ($inputHash -cne (Get-StagingSha256 -Value $plannedVariablesJson)) { return $false }
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
    $preliminaryBinding = [pscustomobject][ordered]@{
        commitSha = $CommitSha.ToLowerInvariant()
        projectId = $ProjectId
        region = $Region
        foundationInputs = $canonicalFoundation
        imageDigests = $canonicalImages
        terraformInputs = $canonicalInputs
    }
    try {
        $plannedVariablesJson = ConvertTo-Json -InputObject (ConvertTo-StagingCanonicalNode -Value (Get-StagingTerraformVariables -Binding $preliminaryBinding)) -Depth 100
        $terraformInputHash = Get-StagingSha256 -Value $plannedVariablesJson
    }
    catch {
        # Incomplete earlier-stage bindings remain constructible, but Plan/Deploy validation
        # rejects them until the exact application input schema can be materialized.
    }
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

function New-StagingImagePublicationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidatePattern('^[a-fA-F0-9]{40}$')] [string] $CommitSha,
        [Parameter(Mandatory)] [ValidatePattern('^[a-z][a-z0-9-]{4,28}[a-z0-9]$')] [string] $ProjectId,
        [Parameter(Mandatory)] [ValidatePattern('^[a-z]+-[a-z]+[0-9]$')] [string] $Region,
        [Parameter(Mandatory)] [ValidatePattern('^[a-z][a-z0-9-]{2,62}$')] [string] $Repository
    )

    $registry = "$Region-docker.pkg.dev/$ProjectId/$Repository"
    $definitions = @(
        [ordered]@{ name = 'api'; image = 'racehunter-api'; dockerfile = 'src/RaceHunter.Api/Dockerfile' },
        [ordered]@{ name = 'worker'; image = 'racehunter-worker'; dockerfile = 'src/RaceHunter.Worker/Dockerfile' },
        [ordered]@{ name = 'referenceTarget'; image = 'racehunter-reference-target'; dockerfile = 'src/RaceHunter.ReferenceTarget/Dockerfile' }
    )
    $images = foreach ($definition in $definitions) {
        $repositoryReference = "$registry/$($definition.image)"
        [pscustomobject][ordered]@{
            name = $definition.name
            dockerfile = $definition.dockerfile
            taggedPushReference = "$repositoryReference`:$($CommitSha.ToLowerInvariant())"
            requiredDigestPrefix = "$repositoryReference@sha256:"
        }
    }

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        operation = 'PublishImages'
        requiresApprovalStage = 'PublishImages'
        projectId = $ProjectId
        region = $Region
        repository = $Repository
        commitSha = $CommitSha.ToLowerInvariant()
        images = @($images)
    }
}

function New-StagingBackendMigrationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,61}[a-z0-9]$')] [string] $StateBucketName,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $BootstrapDirectory,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ApplicationDirectory
    )

    $resolvedBootstrapDirectory = [IO.Path]::GetFullPath($BootstrapDirectory)
    $resolvedApplicationDirectory = [IO.Path]::GetFullPath($ApplicationDirectory)
    foreach ($directory in @($resolvedBootstrapDirectory, $resolvedApplicationDirectory)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Terraform directory does not exist at '$directory'." }
    }
    $backendTemplatePath = Join-Path $resolvedBootstrapDirectory 'backend.gcs.tf.example'
    if (-not (Test-Path -LiteralPath $backendTemplatePath -PathType Leaf)) {
        throw "Bootstrap GCS backend template does not exist at '$backendTemplatePath'."
    }
    $generatedBackendPath = Join-Path $resolvedBootstrapDirectory 'backend.gcs.tf'

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        operation = 'BackendMigration'
        requiresApprovalStage = 'Foundation'
        initialBackend = 'local'
        remoteBackend = 'gcs'
        executesMigration = $false
        stateBucketName = $StateBucketName
        backendTemplatePath = $backendTemplatePath
        backendMaterialization = [pscustomobject][ordered]@{
            action = 'MaterializeBackendTemplate'
            sourcePath = $backendTemplatePath
            destinationPath = $generatedBackendPath
            executesAction = $false
            requiredBeforeStep = 'MigrateBootstrapAndConfigureApplicationState'
        }
        steps = @(
            [pscustomobject][ordered]@{
                name = 'InitializeLocalBootstrapState'
                arguments = @("-chdir=$resolvedBootstrapDirectory", 'init')
            },
            [pscustomobject][ordered]@{
                name = 'MigrateBootstrapAndConfigureApplicationState'
                backendMaterializationRequired = $true
                requiredBackendPath = $generatedBackendPath
                arguments = @("-chdir=$resolvedBootstrapDirectory", 'init', '-migrate-state', "-backend-config=bucket=$StateBucketName", '-backend-config=prefix=bootstrap')
                applicationArguments = @("-chdir=$resolvedApplicationDirectory", 'init', '-reconfigure', "-backend-config=bucket=$StateBucketName", '-backend-config=prefix=application')
            }
        )
    }
}

function New-StagingTerraformPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $SavedPlanPath,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $TerraformVariablesPath,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $TerraformDirectory
    )

    Assert-StagingReleaseBindingForStage -Stage 'Plan' -Binding $Binding
    $resolvedTerraformDirectory = [IO.Path]::GetFullPath($TerraformDirectory)
    if (-not (Test-Path -LiteralPath $resolvedTerraformDirectory -PathType Container)) {
        throw "Terraform directory does not exist at '$resolvedTerraformDirectory'."
    }
    $resolvedPlanPath = [IO.Path]::GetFullPath($SavedPlanPath)
    $planParent = Split-Path -Parent $resolvedPlanPath
    if (-not (Test-Path -LiteralPath $planParent -PathType Container)) {
        throw "Saved-plan parent directory does not exist at '$planParent'."
    }
    $terraformVariables = ConvertTo-StagingCanonicalNode -Value (Get-StagingTerraformVariables -Binding $Binding)
    $resolvedTerraformVariablesPath = [IO.Path]::GetFullPath($TerraformVariablesPath)
    if (-not $resolvedTerraformVariablesPath.EndsWith('.tfvars.json', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Terraform variables must be materialized to a gitignored .tfvars.json path.'
    }
    Write-StagingReleaseState -Path $resolvedTerraformVariablesPath -State $terraformVariables
    $terraformVariablesFileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedTerraformVariablesPath).Hash.ToLowerInvariant()
    if ($terraformVariablesFileHash -cne [string]$Binding.terraformInputHash) {
        throw 'Materialized Terraform variable bytes do not match the reviewed release binding.'
    }

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        operation = 'Plan'
        requiresApprovalStage = 'Plan'
        bindingHash = $Binding.bindingHash
        terraformInputHash = $Binding.terraformInputHash
        imageDigests = $Binding.imageDigests
        terraformVariables = $terraformVariables
        terraformVariablesPath = $resolvedTerraformVariablesPath
        terraformVariablesFileHash = $terraformVariablesFileHash
        savedPlanPath = $resolvedPlanPath
        planArguments = @("-chdir=$resolvedTerraformDirectory", 'plan', '-input=false', "-var-file=$resolvedTerraformVariablesPath", "-out=$resolvedPlanPath")
    }
}

function New-StagingDeploymentPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $SavedPlanPath,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $TerraformDirectory
    )

    Assert-StagingReleaseBindingForStage -Stage 'Deploy' -Binding $Binding
    $resolvedTerraformDirectory = [IO.Path]::GetFullPath($TerraformDirectory)
    if (-not (Test-Path -LiteralPath $resolvedTerraformDirectory -PathType Container)) {
        throw "Terraform directory does not exist at '$resolvedTerraformDirectory'."
    }
    $resolvedPlanPath = [IO.Path]::GetFullPath($SavedPlanPath)
    if (-not (Test-Path -LiteralPath $resolvedPlanPath -PathType Leaf)) {
        throw "Reviewed saved plan does not exist at '$resolvedPlanPath'."
    }
    $actualPlanHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPlanPath).Hash.ToLowerInvariant()
    if ($actualPlanHash -cne [string]$Binding.savedPlanHash) {
        throw 'Reviewed saved-plan bytes do not match the approved release binding.'
    }

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        operation = 'Deploy'
        requiresApprovalStage = 'Deploy'
        bindingHash = $Binding.bindingHash
        terraformInputHash = $Binding.terraformInputHash
        savedPlanHash = $actualPlanHash
        savedPlanPath = $resolvedPlanPath
        regeneratesPlan = $false
        applyArguments = @("-chdir=$resolvedTerraformDirectory", 'apply', '-input=false', '-auto-approve', $resolvedPlanPath)
    }
}

function New-StagingReleaseApproval {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding,
        [AllowNull()] [object] $PreflightRequest,
        [string] $ApprovedAtUtc = ([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ', [Globalization.CultureInfo]::InvariantCulture))
    )

    Assert-StagingReleaseStage -Stage $Stage
    Assert-StagingReleaseBindingForStage -Stage $Stage -Binding $Binding
    $qualificationHash = $null
    $preflightRequestHash = $null
    if ($Stage -ceq 'Preflight') {
        if ($null -eq $PreflightRequest) { throw 'Preflight approval requires the exact generated preflight request.' }
        $qualificationHash = Get-StagingObjectValue -InputObject $PreflightRequest -Name 'qualificationHash'
        $preflightRequestHash = Get-StagingObjectValue -InputObject $PreflightRequest -Name 'requestHash'
        $requestQualification = [pscustomobject][ordered]@{
            passed = $true
            bindingHash = Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash'
            qualificationHash = $qualificationHash
            observedAtUtc = Get-StagingObjectValue -InputObject $PreflightRequest -Name 'requestedAtUtc'
        }
        if (-not (Test-StagingPreflightRequest -Request $PreflightRequest -Binding $Binding -Qualification $requestQualification)) {
            throw 'Preflight approval requires the exact validated request content and request hash.'
        }
    }
    return [pscustomobject][ordered]@{
        stage = $Stage
        bindingHash = Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash'
        commitSha = Get-StagingObjectValue -InputObject $Binding -Name 'commitSha'
        projectId = Get-StagingObjectValue -InputObject $Binding -Name 'projectId'
        region = Get-StagingObjectValue -InputObject $Binding -Name 'region'
        qualificationHash = $qualificationHash
        preflightRequestHash = $preflightRequestHash
        approvedAtUtc = $ApprovedAtUtc
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
    $requiredNames = @('approvedAtUtc', 'bindingHash', 'commitSha', 'invalidationReason', 'preflightRequestHash', 'projectId', 'qualificationHash', 'region', 'stage', 'valid') | Sort-Object -CaseSensitive
    if (($names -join ',') -cne ($requiredNames -join ',')) { return $false }

    $valid = Get-StagingObjectValue -InputObject $Approval -Name 'valid'
    $approvalStage = Get-StagingObjectValue -InputObject $Approval -Name 'stage'
    $bindingHash = Get-StagingObjectValue -InputObject $Approval -Name 'bindingHash'
    $commitSha = Get-StagingObjectValue -InputObject $Approval -Name 'commitSha'
    $projectId = Get-StagingObjectValue -InputObject $Approval -Name 'projectId'
    $region = Get-StagingObjectValue -InputObject $Approval -Name 'region'
    $approvedAtUtc = Get-StagingObjectValue -InputObject $Approval -Name 'approvedAtUtc'
    $qualificationHash = Get-StagingObjectValue -InputObject $Approval -Name 'qualificationHash'
    $preflightRequestHash = Get-StagingObjectValue -InputObject $Approval -Name 'preflightRequestHash'
    $invalidationReason = Get-StagingObjectValue -InputObject $Approval -Name 'invalidationReason'

    if ($valid -isnot [bool] -or $valid -cne $true) { return $false }
    if ($approvalStage -isnot [string] -or $approvalStage -cne $Stage -or $approvalStage -notin $script:ExternalStages) { return $false }
    if ($bindingHash -isnot [string] -or $bindingHash -notmatch '^[a-f0-9]{64}$') { return $false }
    if ($commitSha -isnot [string] -or $commitSha -notmatch '^[a-f0-9]{40}$') { return $false }
    if ($projectId -isnot [string] -or $projectId -notmatch '^[a-z][a-z0-9-]{4,28}[a-z0-9]$') { return $false }
    if ($region -isnot [string] -or $region -notmatch '^[a-z]+-[a-z]+[0-9]$') { return $false }
    if ($null -ne $invalidationReason) { return $false }
    if ($Stage -ceq 'Preflight') {
        if ($qualificationHash -isnot [string] -or $qualificationHash -notmatch '^[a-f0-9]{64}$') { return $false }
        if ($preflightRequestHash -isnot [string] -or $preflightRequestHash -notmatch '^[a-f0-9]{64}$') { return $false }
    }
    elseif ($null -ne $qualificationHash -or $null -ne $preflightRequestHash) { return $false }
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
        [AllowNull()] [object] $Approval,
        [AllowNull()] [object] $PreflightRequest,
        [AllowNull()] [object] $Qualification,
        [string] $CurrentTimeUtc = ([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ', [Globalization.CultureInfo]::InvariantCulture))
    )

    Assert-StagingReleaseStage -Stage $Stage
    if (-not (Test-StagingReleaseBindingForStage -Stage $Stage -Binding $Binding)) { return $false }
    if ($null -eq $Approval) { return $false }
    if (-not (Test-StagingReleaseApprovalRecord -Stage $Stage -Approval $Approval)) { return $false }
    $matchesBinding = (
        (Get-StagingObjectValue -InputObject $Approval -Name 'stage') -ceq $Stage -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'bindingHash') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash') -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'commitSha') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'commitSha') -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'projectId') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'projectId') -and
        (Get-StagingObjectValue -InputObject $Approval -Name 'region') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'region')
    )
    if (-not $matchesBinding) { return $false }
    if ($Stage -cne 'Preflight') { return $true }
    if ($null -eq $PreflightRequest -or $null -eq $Qualification) { return $false }
    if (-not (Test-StagingPreflightRequest -Request $PreflightRequest -Binding $Binding -Qualification $Qualification)) { return $false }
    if (
        (Get-StagingObjectValue -InputObject $Approval -Name 'qualificationHash') -cne (Get-StagingObjectValue -InputObject $Qualification -Name 'qualificationHash') -or
        (Get-StagingObjectValue -InputObject $Approval -Name 'preflightRequestHash') -cne (Get-StagingObjectValue -InputObject $PreflightRequest -Name 'requestHash')) {
        return $false
    }

    $approvedAt = [DateTimeOffset]::MinValue
    $qualificationAt = [DateTimeOffset]::MinValue
    $requestedAt = [DateTimeOffset]::MinValue
    $currentTime = [DateTimeOffset]::MinValue
    foreach ($candidate in @(
        @{ value = Get-StagingObjectValue -InputObject $Approval -Name 'approvedAtUtc'; target = [ref]$approvedAt },
        @{ value = Get-StagingObjectValue -InputObject $Qualification -Name 'observedAtUtc'; target = [ref]$qualificationAt },
        @{ value = Get-StagingObjectValue -InputObject $PreflightRequest -Name 'requestedAtUtc'; target = [ref]$requestedAt },
        @{ value = $CurrentTimeUtc; target = [ref]$currentTime }
    )) {
        if ($candidate.value -isnot [string] -or -not [DateTimeOffset]::TryParse(
            $candidate.value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            $candidate.target) -or $candidate.target.Value.Offset -ne [TimeSpan]::Zero) {
            return $false
        }
    }
    if ($approvedAt -lt $qualificationAt -or $approvedAt -lt $requestedAt) { return $false }
    if ($approvedAt -gt $currentTime.Add($script:PreflightApprovalFutureSkew)) { return $false }
    if (($currentTime - $approvedAt) -gt $script:PreflightApprovalLifetime) { return $false }
    return $true
}

function Assert-StagingReleaseApproval {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [object] $Binding,
        [AllowNull()] [object] $Approval,
        [AllowNull()] [object] $PreflightRequest,
        [AllowNull()] [object] $Qualification
    )

    if (-not (Test-StagingReleaseApproval -Stage $Stage -Binding $Binding -Approval $Approval -PreflightRequest $PreflightRequest -Qualification $Qualification)) {
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
    # Preserve timestamp bytes as strings so request hashes remain stable across resume.
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -Depth 100 -DateKind String
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
        [Parameter(Mandatory)] [ValidateSet('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo', 'ReleaseCompletion', 'RecoveryCompletion')] [string] $Stage,
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
    $approvalValid = if ($stage -ceq 'Preflight') {
        Test-StagingReleaseApproval -Stage $stage -Binding $state.binding -Approval $Approval -PreflightRequest $state.preflightRequest -Qualification $state.localQualification
    }
    else {
        Test-StagingReleaseApproval -Stage $stage -Binding $state.binding -Approval $Approval
    }
    if (-not $approvalValid) {
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
        [Parameter(Mandatory)] [ValidateSet('Preflight', 'Foundation', 'PublishImages', 'Plan', 'Deploy', 'Validate', 'Smoke', 'Demo', 'ReleaseCompletion', 'RecoveryCompletion')] [string] $ChangedAtStage
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

function New-StagingLocalQualificationGate {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [ValidateSet('local', 'local-emulated')] [string] $Classification,
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [int[]] $ExpectedExitCodes = @(0)
    )

    return [pscustomobject][ordered]@{
        name = $Name
        classification = $Classification
        filePath = $FilePath
        argumentList = @($ArgumentList)
        workingDirectory = $WorkingDirectory
        expectedExitCodes = @($ExpectedExitCodes)
    }
}

function Get-StagingLocalQualificationGates {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [ValidatePattern('^[a-fA-F0-9]{40}$')] [string] $CommitSha
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $root 'RaceHunter.slnx') -PathType Leaf)) {
        throw "Repository root '$root' does not contain RaceHunter.slnx."
    }
    $npm = if ($IsWindows) { 'npm.cmd' } else { 'npm' }
    $volume = "${root}:/workspace"
    $localTag = $CommitSha.ToLowerInvariant()
    $secretPattern = 'AIza[0-9A-Za-z_-]{35}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|Bearer [A-Za-z0-9._~+/=-]{20,}'

    return @(
        (New-StagingLocalQualificationGate -Name 'clean-checkout' -Classification local -FilePath git -ArgumentList @('status', '--porcelain=v1', '--untracked-files=all') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'release-candidate-commit' -Classification local -FilePath git -ArgumentList @('rev-parse', '--verify', 'HEAD') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'dotnet-restore' -Classification local -FilePath dotnet -ArgumentList @('restore', 'RaceHunter.slnx') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'dotnet-tests' -Classification local -FilePath dotnet -ArgumentList @('test', 'RaceHunter.slnx', '-c', 'Release', '--no-restore') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'web-install' -Classification local -FilePath $npm -ArgumentList @('ci', '--prefix', 'src/RaceHunter.Web') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'web-tests' -Classification local -FilePath $npm -ArgumentList @('test', '--prefix', 'src/RaceHunter.Web') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'web-lint' -Classification local -FilePath $npm -ArgumentList @('run', 'lint', '--prefix', 'src/RaceHunter.Web') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'web-build' -Classification local -FilePath $npm -ArgumentList @('run', 'build', '--prefix', 'src/RaceHunter.Web') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'acceptance-install' -Classification local -FilePath $npm -ArgumentList @('ci', '--prefix', 'tests/RaceHunter.AcceptanceTests') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'fresh-volume-real-playwright' -Classification local-emulated -FilePath pwsh -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $root 'scripts/run-real-playwright.ps1')) -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'api-image-build' -Classification local -FilePath docker -ArgumentList @('build', '--file', 'src/RaceHunter.Api/Dockerfile', '--tag', "racehunter-api:local-$localTag", '.') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'worker-image-build' -Classification local -FilePath docker -ArgumentList @('build', '--file', 'src/RaceHunter.Worker/Dockerfile', '--tag', "racehunter-worker:local-$localTag", '.') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'reference-target-image-build' -Classification local -FilePath docker -ArgumentList @('build', '--file', 'src/RaceHunter.ReferenceTarget/Dockerfile', '--tag', "racehunter-reference-target:local-$localTag", '.') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'compose-config' -Classification local -FilePath docker -ArgumentList @('compose', 'config', '--quiet') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'nuget-dependency-audit' -Classification local -FilePath dotnet -ArgumentList @('list', 'RaceHunter.slnx', 'package', '--vulnerable', '--include-transitive') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'web-dependency-audit' -Classification local -FilePath $npm -ArgumentList @('audit', '--audit-level=high', '--prefix', 'src/RaceHunter.Web') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'acceptance-dependency-audit' -Classification local -FilePath $npm -ArgumentList @('audit', '--audit-level=high', '--prefix', 'tests/RaceHunter.AcceptanceTests') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'repository-secret-scan' -Classification local -FilePath git -ArgumentList @('grep', '--quiet', '-I', '-E', $secretPattern, '--', '.') -WorkingDirectory $root -ExpectedExitCodes @(1)),
        (New-StagingLocalQualificationGate -Name 'terraform-format' -Classification local -FilePath docker -ArgumentList @('run', '--rm', '-v', $volume, '-w', '/workspace/deploy/terraform', 'hashicorp/terraform:1.14.4', 'fmt', '-check', '-diff', '-recursive') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'terraform-bootstrap-init' -Classification local -FilePath docker -ArgumentList @('run', '--rm', '-v', $volume, '-w', '/workspace/deploy/terraform/bootstrap', 'hashicorp/terraform:1.14.4', 'init', '-backend=false') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'terraform-bootstrap-validate' -Classification local -FilePath docker -ArgumentList @('run', '--rm', '-v', $volume, '-w', '/workspace/deploy/terraform/bootstrap', 'hashicorp/terraform:1.14.4', 'validate') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'terraform-application-init' -Classification local -FilePath docker -ArgumentList @('run', '--rm', '-v', $volume, '-w', '/workspace/deploy/terraform', 'hashicorp/terraform:1.14.4', 'init', '-backend=false') -WorkingDirectory $root),
        (New-StagingLocalQualificationGate -Name 'terraform-application-validate' -Classification local -FilePath docker -ArgumentList @('run', '--rm', '-v', $volume, '-w', '/workspace/deploy/terraform', 'hashicorp/terraform:1.14.4', 'validate') -WorkingDirectory $root)
    )
}

function Invoke-StagingLocalQualificationCommand {
    param([Parameter(Mandatory)] [object] $Gate)

    $requestedFilePath = [string]$Gate.filePath
    if ([IO.Path]::IsPathRooted($requestedFilePath)) {
        $resolvedFilePath = [IO.Path]::GetFullPath($requestedFilePath)
        if (-not (Test-Path -LiteralPath $resolvedFilePath -PathType Leaf)) {
            throw "Local qualification executable does not exist for gate '$($Gate.name)'."
        }
    }
    else {
        $application = @(Get-Command -Name $requestedFilePath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
        if ($application.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$application[0].Source)) {
            throw "Local qualification executable could not be resolved for gate '$($Gate.name)'."
        }
        $resolvedFilePath = [IO.Path]::GetFullPath([string]$application[0].Source)
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    # Windows command shims require an absolute path so %~dp0 resolves to the
    # installed runtime rather than the repository working directory.
    $startInfo.FileName = $resolvedFilePath
    $startInfo.WorkingDirectory = [string]$Gate.workingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @($Gate.argumentList)) { $startInfo.ArgumentList.Add([string]$argument) }

    # Start from an allowlisted environment so future Google/Cloud SDK credential variables
    # cannot silently cross the local-only boundary. Discovery roots are isolated as well.
    $safeEnvironment = [ordered]@{}
    foreach ($name in @('PATH', 'PATHEXT', 'SystemRoot', 'WINDIR', 'COMSPEC', 'OS', 'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432', 'LANG', 'LC_ALL', 'TERM')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $safeEnvironment[$name] = $value }
    }
    $isolationRoot = [IO.Path]::Combine([string]$Gate.workingDirectory, 'memory-bank', '.local', 'staging-release', 'credential-free-home')
    $cloudSdkRoot = [IO.Path]::Combine($isolationRoot, 'gcloud')
    $dockerRoot = [IO.Path]::Combine($isolationRoot, 'docker')
    $nugetRoot = [IO.Path]::Combine($isolationRoot, 'nuget-packages')
    $npmRoot = [IO.Path]::Combine($isolationRoot, 'npm-cache')
    foreach ($directory in @($isolationRoot, $cloudSdkRoot, $dockerRoot, $nugetRoot, $npmRoot)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $safeEnvironment.HOME = $isolationRoot
    $safeEnvironment.USERPROFILE = $isolationRoot
    $safeEnvironment.APPDATA = [IO.Path]::Combine($isolationRoot, 'app-data')
    $safeEnvironment.LOCALAPPDATA = [IO.Path]::Combine($isolationRoot, 'local-app-data')
    $safeEnvironment.XDG_CONFIG_HOME = [IO.Path]::Combine($isolationRoot, 'xdg-config')
    $safeEnvironment.XDG_CACHE_HOME = [IO.Path]::Combine($isolationRoot, 'xdg-cache')
    $safeEnvironment.XDG_DATA_HOME = [IO.Path]::Combine($isolationRoot, 'xdg-data')
    $safeEnvironment.CLOUDSDK_CONFIG = $cloudSdkRoot
    $safeEnvironment.DOCKER_CONFIG = $dockerRoot
    $safeEnvironment.NUGET_PACKAGES = $nugetRoot
    $safeEnvironment.npm_config_cache = $npmRoot
    $startInfo.Environment.Clear()
    foreach ($entry in $safeEnvironment.GetEnumerator()) { $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw "Local qualification gate '$($Gate.name)' could not start." }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        return [pscustomobject][ordered]@{
            exitCode = $process.ExitCode
            standardOutput = $standardOutput
            standardError = $standardError
        }
    }
    finally {
        $process.Dispose()
    }
}

function New-StagingPreflightRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [object] $Qualification
    )

    $requestedAtUtc = [string](Get-StagingObjectValue -InputObject $Qualification -Name 'observedAtUtc')
    $requestMaterial = [ordered]@{
        schemaVersion = '1.0'
        stage = 'Preflight'
        commitSha = [string](Get-StagingObjectValue -InputObject $Binding -Name 'commitSha')
        projectId = [string](Get-StagingObjectValue -InputObject $Binding -Name 'projectId')
        region = [string](Get-StagingObjectValue -InputObject $Binding -Name 'region')
        bindingHash = [string](Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash')
        qualificationHash = [string](Get-StagingObjectValue -InputObject $Qualification -Name 'qualificationHash')
        requestedAtUtc = $requestedAtUtc
        approvalRequired = $true
        authorizesMutation = $false
        allowedChecks = @('active-principal', 'project', 'billing-link', 'quotas', 'permissions', 'region-availability', 'existing-resources')
    }
    $request = [pscustomobject]$requestMaterial
    $request | Add-Member -NotePropertyName requestHash -NotePropertyValue (Get-StagingCanonicalHash -Value $requestMaterial)
    if (-not (Test-StagingPreflightRequest -Request $request -Binding $Binding -Qualification $Qualification)) {
        throw 'Cannot prepare preflight request from incomplete or drifted local qualification.'
    }
    return $request
}

function Test-StagingPreflightRequest {
    [CmdletBinding()]
    param(
        [AllowNull()] [object] $Request,
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [object] $Qualification
    )

    if ($null -eq $Request) { return $false }
    try { $names = @(Get-StagingObjectPropertyNames -InputObject $Request | Sort-Object -CaseSensitive) }
    catch { return $false }
    $expectedNames = @('allowedChecks', 'approvalRequired', 'authorizesMutation', 'bindingHash', 'commitSha', 'projectId', 'qualificationHash', 'region', 'requestHash', 'requestedAtUtc', 'schemaVersion', 'stage') | Sort-Object -CaseSensitive
    if (($names -join ',') -cne ($expectedNames -join ',')) { return $false }
    $allowedChecks = Get-StagingObjectValue -InputObject $Request -Name 'allowedChecks'
    if ($allowedChecks -is [string] -or $allowedChecks -isnot [Collections.IEnumerable]) { return $false }
    $expectedChecks = @('active-principal', 'project', 'billing-link', 'quotas', 'permissions', 'region-availability', 'existing-resources')
    $requestMaterial = [ordered]@{
        schemaVersion = Get-StagingObjectValue -InputObject $Request -Name 'schemaVersion'
        stage = Get-StagingObjectValue -InputObject $Request -Name 'stage'
        commitSha = Get-StagingObjectValue -InputObject $Request -Name 'commitSha'
        projectId = Get-StagingObjectValue -InputObject $Request -Name 'projectId'
        region = Get-StagingObjectValue -InputObject $Request -Name 'region'
        bindingHash = Get-StagingObjectValue -InputObject $Request -Name 'bindingHash'
        qualificationHash = Get-StagingObjectValue -InputObject $Request -Name 'qualificationHash'
        requestedAtUtc = Get-StagingObjectValue -InputObject $Request -Name 'requestedAtUtc'
        approvalRequired = Get-StagingObjectValue -InputObject $Request -Name 'approvalRequired'
        authorizesMutation = Get-StagingObjectValue -InputObject $Request -Name 'authorizesMutation'
        allowedChecks = @($Request.allowedChecks)
    }
    return (
        (Get-StagingObjectValue -InputObject $Request -Name 'schemaVersion') -is [string] -and
        (Get-StagingObjectValue -InputObject $Request -Name 'schemaVersion') -ceq '1.0' -and
        (Get-StagingObjectValue -InputObject $Request -Name 'stage') -is [string] -and
        (Get-StagingObjectValue -InputObject $Request -Name 'stage') -ceq 'Preflight' -and
        (Get-StagingObjectValue -InputObject $Request -Name 'approvalRequired') -is [bool] -and
        (Get-StagingObjectValue -InputObject $Request -Name 'approvalRequired') -ceq $true -and
        (Get-StagingObjectValue -InputObject $Request -Name 'authorizesMutation') -is [bool] -and
        (Get-StagingObjectValue -InputObject $Request -Name 'authorizesMutation') -ceq $false -and
        (@($allowedChecks) -join ',') -ceq ($expectedChecks -join ',') -and
        (Get-StagingObjectValue -InputObject $Request -Name 'requestHash') -is [string] -and
        (Get-StagingObjectValue -InputObject $Request -Name 'requestHash') -ceq (Get-StagingCanonicalHash -Value $requestMaterial) -and
        (Get-StagingObjectValue -InputObject $Request -Name 'requestedAtUtc') -ceq (Get-StagingObjectValue -InputObject $Qualification -Name 'observedAtUtc') -and
        (Get-StagingObjectValue -InputObject $Qualification -Name 'passed') -is [bool] -and
        (Get-StagingObjectValue -InputObject $Qualification -Name 'passed') -ceq $true -and
        (Get-StagingObjectValue -InputObject $Qualification -Name 'bindingHash') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash') -and
        (Get-StagingObjectValue -InputObject $Request -Name 'bindingHash') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash') -and
        (Get-StagingObjectValue -InputObject $Request -Name 'qualificationHash') -ceq (Get-StagingObjectValue -InputObject $Qualification -Name 'qualificationHash') -and
        (Get-StagingObjectValue -InputObject $Request -Name 'commitSha') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'commitSha') -and
        (Get-StagingObjectValue -InputObject $Request -Name 'projectId') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'projectId') -and
        (Get-StagingObjectValue -InputObject $Request -Name 'region') -ceq (Get-StagingObjectValue -InputObject $Binding -Name 'region')
    )
}

function Assert-StagingPreflightRequest {
    [CmdletBinding()]
    param(
        [AllowNull()] [object] $Request,
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [object] $Qualification
    )

    if (-not (Test-StagingPreflightRequest -Request $Request -Binding $Binding -Qualification $Qualification)) {
        throw 'Preflight request is missing, incomplete, or invalidated by release-candidate drift.'
    }
}

function Invoke-StagingLocalQualification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [object] $Binding,
        [scriptblock] $CommandRunner,
        [string] $ObservedAtUtc = ([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ', [Globalization.CultureInfo]::InvariantCulture))
    )

    $commitSha = [string](Get-StagingObjectValue -InputObject $Binding -Name 'commitSha')
    $gates = @(Get-StagingLocalQualificationGates -RepositoryRoot $RepositoryRoot -CommitSha $commitSha)
    if ($null -eq $CommandRunner) {
        $CommandRunner = { param($gate) Invoke-StagingLocalQualificationCommand -Gate $gate }
    }

    $results = @()
    $evidence = @()
    foreach ($gate in $gates) {
        $outcome = & $CommandRunner $gate
        $exitCode = Get-StagingObjectValue -InputObject $outcome -Name 'exitCode'
        if ($exitCode -is [bool] -or $exitCode -isnot [ValueType] -or [int]$exitCode -notin @($gate.expectedExitCodes)) {
            throw "Local qualification gate '$($gate.name)' failed with exit code '$exitCode'."
        }
        $standardOutput = [string](Get-StagingObjectValue -InputObject $outcome -Name 'standardOutput')
        if ($gate.name -ceq 'clean-checkout' -and -not [string]::IsNullOrWhiteSpace($standardOutput)) {
            throw 'Local qualification requires a clean checkout with no tracked or untracked release-candidate drift.'
        }
        if ($gate.name -ceq 'release-candidate-commit' -and $standardOutput.Trim() -cne $commitSha) {
            throw 'Checked-out commit does not match the immutable release-candidate commit.'
        }

        $result = [pscustomobject][ordered]@{
            name = $gate.name
            classification = $gate.classification
            status = 'passed'
        }
        $results += $result
        $identifiers = [ordered]@{ gate = $gate.name; gateSetVersion = '1.0' }
        if ($gate.name -in @('api-image-build', 'worker-image-build', 'reference-target-image-build')) {
            $identifiers.localImageTag = "local-$commitSha"
        }
        $record = [pscustomobject][ordered]@{
            schemaVersion = '1.0'
            classification = $gate.classification
            observedAtUtc = $ObservedAtUtc
            environment = 'local'
            method = $gate.name
            expected = [pscustomobject]@{ summary = "Gate '$($gate.name)' succeeds for the exact release candidate." }
            observed = [pscustomobject]@{ summary = "Gate '$($gate.name)' passed without credentialed cloud access."; status = 'passed' }
            commitSha = $commitSha
            imageDigests = [pscustomobject]@{}
            identifiers = [pscustomobject]$identifiers
            artifactReference = "memory-bank/.local/staging-release/local-qualification/$($gate.name).json"
        }
        $evidence += Protect-StagingEvidence -Evidence $record
    }

    $qualificationMaterial = [ordered]@{
        gateSetVersion = '1.0'
        bindingHash = Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash'
        commitSha = $commitSha
        gates = @($results)
    }
    $qualification = [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        gateSetVersion = '1.0'
        observedAtUtc = $ObservedAtUtc
        commitSha = $commitSha
        bindingHash = Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash'
        qualificationHash = Get-StagingCanonicalHash -Value $qualificationMaterial
        passed = $true
        gates = @($results)
        evidence = @($evidence)
        preflightRequest = $null
    }
    $qualification.preflightRequest = New-StagingPreflightRequest -Binding $Binding -Qualification $qualification
    return $qualification
}

function Save-StagingLocalQualification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Binding,
        [Parameter(Mandatory)] [object] $Qualification
    )

    Assert-StagingPreflightRequest -Request (Get-StagingObjectValue -InputObject $Qualification -Name 'preflightRequest') -Binding $Binding -Qualification $Qualification
    $state = if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Get-StagingReleaseState -Path $Path
    }
    else {
        Initialize-StagingReleaseState -Path $Path -Binding $Binding
    }
    if ($state.binding.bindingHash -cne (Get-StagingObjectValue -InputObject $Binding -Name 'bindingHash')) {
        throw 'Local qualification cannot be saved against a drifted release binding.'
    }
    if ($state.currentStage -ceq 'LocalQualified') {
        if ($state.localQualification.qualificationHash -cne (Get-StagingObjectValue -InputObject $Qualification -Name 'qualificationHash')) {
            throw 'Stored local qualification belongs to different release-candidate evidence.'
        }
        return $state
    }
    if ($state.currentStage -cne 'Initialized' -or [bool]$state.recovery.requiresReadOnlyInspection) {
        throw "Local qualification cannot be saved from release stage '$($state.currentStage)'."
    }

    $protectedEvidence = @()
    foreach ($record in @($Qualification.evidence)) {
        $protectedEvidence += Protect-StagingEvidence -Evidence $record
    }
    $durableQualification = [pscustomobject][ordered]@{
        schemaVersion = Get-StagingObjectValue -InputObject $Qualification -Name 'schemaVersion'
        gateSetVersion = Get-StagingObjectValue -InputObject $Qualification -Name 'gateSetVersion'
        observedAtUtc = Get-StagingObjectValue -InputObject $Qualification -Name 'observedAtUtc'
        commitSha = Get-StagingObjectValue -InputObject $Qualification -Name 'commitSha'
        bindingHash = Get-StagingObjectValue -InputObject $Qualification -Name 'bindingHash'
        qualificationHash = Get-StagingObjectValue -InputObject $Qualification -Name 'qualificationHash'
        passed = $true
        gates = @(Get-StagingObjectValue -InputObject $Qualification -Name 'gates')
    }
    $state | Add-Member -NotePropertyName localQualification -NotePropertyValue $durableQualification -Force
    $state | Add-Member -NotePropertyName preflightRequest -NotePropertyValue (Get-StagingObjectValue -InputObject $Qualification -Name 'preflightRequest') -Force
    $state.evidence = @($state.evidence) + @($protectedEvidence)
    $state.currentStage = 'LocalQualified'
    $state.transitions = @($state.transitions) + [pscustomobject][ordered]@{ stage = 'LocalQualified'; observedAtUtc = Get-StagingObjectValue -InputObject $Qualification -Name 'observedAtUtc' }
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
    'New-StagingImagePublicationPlan',
    'New-StagingBackendMigrationPlan',
    'New-StagingTerraformPlan',
    'New-StagingDeploymentPlan',
    'New-StagingReleaseApproval',
    'Test-StagingReleaseApproval',
    'Assert-StagingReleaseApproval',
    'Initialize-StagingReleaseState',
    'Get-StagingReleaseState',
    'Set-StagingReleaseStage',
    'Add-StagingReleaseApproval',
    'Add-StagingReleaseEvidence',
    'Update-StagingReleaseBinding',
    'Get-StagingLocalQualificationGates',
    'Invoke-StagingLocalQualificationCommand',
    'Invoke-StagingLocalQualification',
    'Save-StagingLocalQualification',
    'New-StagingPreflightRequest',
    'Test-StagingPreflightRequest',
    'Assert-StagingPreflightRequest',
    'Set-StagingReleaseFailure',
    'Complete-StagingReleaseReconciliation',
    'ConvertTo-StagingRedactedText',
    'Protect-StagingEvidence',
    'Publish-StagingEvidence'
)

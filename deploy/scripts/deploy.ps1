param(
    [Parameter(Mandatory)] [string] $ProjectId,
    [Parameter(Mandatory)] [string] $ApiImage,
    [Parameter(Mandatory)] [string] $WorkerImage,
    [Parameter(Mandatory)] [string] $ReferenceTargetImage,
    [switch] $ApproveBillableResources
)

$ErrorActionPreference = 'Stop'
if (-not $ApproveBillableResources) {
    throw 'Deployment creates billable Google Cloud resources. Re-run only after explicit approval with -ApproveBillableResources.'
}

foreach ($image in @($ApiImage, $WorkerImage, $ReferenceTargetImage)) {
    if ($image -notmatch '@sha256:[a-fA-F0-9]{64}$') {
        throw "Every deployment image must be pinned by immutable @sha256: digest. Invalid image: $image"
    }
}

$terraformDirectory = Join-Path $PSScriptRoot '..\terraform'
Push-Location $terraformDirectory
try {
    terraform fmt -check
    terraform init
    terraform validate
    terraform plan -out racehunter.tfplan `
        -var "project_id=$ProjectId" `
        -var "api_image=$ApiImage" `
        -var "worker_image=$WorkerImage" `
        -var "reference_target_image=$ReferenceTargetImage"
    terraform apply racehunter.tfplan
}
finally {
    Pop-Location
}

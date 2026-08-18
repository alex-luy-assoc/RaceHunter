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

$terraformDirectory = Join-Path $PSScriptRoot '..\terraform'
Push-Location $terraformDirectory
try {
    terraform init
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

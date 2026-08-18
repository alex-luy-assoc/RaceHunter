param(
    [Parameter(Mandatory)] [uri] $ApiBaseUrl
)

$ErrorActionPreference = 'Stop'
$health = Invoke-WebRequest -Uri ([uri]::new($ApiBaseUrl, '/healthz')) -UseBasicParsing
if ($health.StatusCode -ne 200) { throw "API health check failed with $($health.StatusCode)." }

$project = Invoke-RestMethod -Method Post -Uri ([uri]::new($ApiBaseUrl, '/api/projects')) -ContentType 'application/json' -Body '{"name":"Cloud smoke"}'
$loaded = Invoke-RestMethod -Method Get -Uri ([uri]::new($ApiBaseUrl, "/api/projects/$($project.id)"))
if ($loaded.id -ne $project.id) { throw 'Persisted project could not be read back.' }

Write-Host "Cloud smoke passed for project $($project.id)."

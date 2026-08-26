[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $JsonOutputPath,
    [ValidateRange(1024, 10485760)] [int] $MaximumBlobBytes = 2097152
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Full-history basis: git rev-list --objects --all. Findings are metadata-only;
# matched credential material is never written to stdout or JSON.

function Invoke-Git([string[]] $Arguments) {
    $output = @(& git -C $RepositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed without publishing scanner content." }
    return $output
}

function Invoke-GitGrep([string] $Pattern, [string[]] $Revisions) {
    # -l returns revision/path metadata only. It never emits a matched line.
    $output = @(& git -C $RepositoryRoot grep -l -P -e $Pattern @Revisions 2>&1)
    if ($LASTEXITCODE -gt 1) { throw 'git grep failed without publishing scanner content.' }
    return $output
}

function Invoke-GitIndexGrep([string] $Pattern) {
    $output = @(& git -C $RepositoryRoot grep --cached -l -P -e $Pattern 2>&1)
    if ($LASTEXITCODE -gt 1) { throw 'git index grep failed without publishing scanner content.' }
    return $output
}

$secretPatterns = [ordered]@{
    google_api_key = ('AI' + 'za[0-9A-Za-z_-]{35}')
    aws_access_key = ('A' + '[KS]IA[0-9A-Z]{16}')
    github_fine_grained = ('github' + '_pat_[0-9A-Za-z_]{60,}')
    github_classic = ('gh' + '[pousr]_[0-9A-Za-z]{36,}')
    slack_token = ('xo' + 'x[baprs]-[0-9A-Za-z-]{20,}')
    private_key = ('-----BEGIN (?:RSA |EC |OPENSSH |DSA )?' + 'PRIVATE KEY-----')
    google_service_account_key = ('"private_key"\s*:\s*"-----BEGIN ' + 'PRIVATE KEY-----')
}
$sensitivePathPattern = '(?i)(^|/)(\.env($|\.)|credentials?\.json$|service[-_]?account.*\.json$|.*\.(pem|p12|pfx|key|tfstate|tfplan)$|.*\.tfvars\.json$)|(^|/)memory-bank/\.local/'

$findings = [Collections.Generic.List[object]]::new()
$scannedObjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$objects = @(Invoke-Git @('rev-list', '--objects', '--all'))
$revisions = @(Invoke-Git @('rev-list', '--all'))
$historyPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($row in $objects) {
    if ([string]::IsNullOrWhiteSpace($row)) { continue }
    $parts = $row -split ' ', 2
    $objectId = $parts[0]
    $path = if ($parts.Count -eq 2) { $parts[1].Replace('\', '/') } else { '' }
    [void]$scannedObjects.Add($objectId)
}

# `rev-list --objects` may report only one name for a reused blob. Git log walks
# every committed tree name so a deleted/renamed sensitive path cannot hide
# behind an identical blob at a benign path.
foreach ($path in @(Invoke-Git @('log', '--all', '--name-only', '--pretty=format:'))) {
    if ([string]::IsNullOrWhiteSpace($path)) { continue }
    $normalized = $path.Replace('\', '/')
    [void]$historyPaths.Add($normalized)
    if ($normalized -match $sensitivePathPattern) {
        $findings.Add([ordered]@{ kind = 'sensitive-path'; rule = 'history-path'; object = 'history'; path = $normalized })
    }
}

foreach ($rule in $secretPatterns.GetEnumerator()) {
    foreach ($match in @(Invoke-GitGrep -Pattern $rule.Value -Revisions $revisions)) {
        if ([string]::IsNullOrWhiteSpace($match)) { continue }
        $parts = $match -split ':', 2
        $revision = $parts[0]
        $path = if ($parts.Count -eq 2) { $parts[1].Replace('\', '/') } else { '' }
        $findings.Add([ordered]@{ kind = 'credential-shape'; rule = $rule.Key; object = $revision.Substring(0, 12); path = $path })
    }
}

$commitMessageCount = 0
foreach ($revision in $revisions) {
    $commitMessageCount++
    $message = (Invoke-Git @('show', '-s', '--format=%B', $revision)) -join "`n"
    foreach ($rule in $secretPatterns.GetEnumerator()) {
        if ($message -match $rule.Value) {
            $findings.Add([ordered]@{ kind = 'credential-shape'; rule = $rule.Key; object = $revision.Substring(0, 12); path = '<commit-message>' })
        }
    }
}

$annotatedTagCount = 0
foreach ($tag in @(Invoke-Git @('for-each-ref', '--format=%(objectname)|%(objecttype)', 'refs/tags'))) {
    if ([string]::IsNullOrWhiteSpace($tag)) { continue }
    $parts = $tag -split '\|', 2
    if ($parts.Count -ne 2 -or $parts[1] -cne 'tag') { continue }
    $annotatedTagCount++
    $objectId = $parts[0]
    $message = (Invoke-Git @('cat-file', 'tag', $objectId)) -join "`n"
    foreach ($rule in $secretPatterns.GetEnumerator()) {
        if ($message -match $rule.Value) {
            $findings.Add([ordered]@{ kind = 'credential-shape'; rule = $rule.Key; object = $objectId.Substring(0, 12); path = '<annotated-tag-message>' })
        }
    }
}

$indexPaths = @(Invoke-Git @('ls-files', '--cached'))
$workingTreePaths = @(Invoke-Git @('ls-files', '--cached', '--others', '--exclude-standard'))

foreach ($candidate in $indexPaths) {
    $normalized = $candidate.Replace('\', '/')
    if ($normalized -match $sensitivePathPattern) {
        $findings.Add([ordered]@{ kind = 'sensitive-path'; rule = 'index-path'; object = 'index'; path = $normalized })
    }
}
foreach ($rule in $secretPatterns.GetEnumerator()) {
    foreach ($path in @(Invoke-GitIndexGrep -Pattern $rule.Value)) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $findings.Add([ordered]@{ kind = 'credential-shape'; rule = $rule.Key; object = 'index'; path = $path.Replace('\', '/') })
    }
}

foreach ($candidate in $workingTreePaths) {
    $normalized = $candidate.Replace('\', '/')
    if ($normalized -match $sensitivePathPattern) {
        $findings.Add([ordered]@{ kind = 'sensitive-path'; rule = 'working-tree-path'; object = 'working-tree'; path = $normalized })
        continue
    }
    $fullPath = Join-Path $RepositoryRoot $candidate
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
    if ((Get-Item -LiteralPath $fullPath).Length -gt $MaximumBlobBytes) {
        $findings.Add([ordered]@{ kind = 'unscanned-candidate'; rule = 'size-limit'; object = 'working-tree'; path = $normalized })
        continue
    }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    $content = [Text.Encoding]::UTF8.GetString($bytes)
    foreach ($rule in $secretPatterns.GetEnumerator()) {
        if ($content -match $rule.Value) {
            $findings.Add([ordered]@{ kind = 'credential-shape'; rule = $rule.Key; object = 'working-tree'; path = $normalized })
        }
    }
}

$uniqueByKey = [ordered]@{}
foreach ($finding in $findings) {
    $key = '{0}|{1}|{2}|{3}' -f $finding['kind'], $finding['rule'], $finding['object'], $finding['path']
    if (-not $uniqueByKey.Contains($key)) { $uniqueByKey[$key] = $finding }
}
$uniqueFindings = @($uniqueByKey.Values | Sort-Object { $_['kind'] }, { $_['rule'] }, { $_['object'] }, { $_['path'] })
$result = [ordered]@{
    schemaVersion = '1.0'
    status = if ($uniqueFindings.Count -eq 0) { 'passed' } else { 'failed' }
    historyObjectCount = $scannedObjects.Count
    historyPathCount = $historyPaths.Count
    commitMessageCount = $commitMessageCount
    annotatedTagCount = $annotatedTagCount
    indexCandidateCount = $indexPaths.Count
    workingTreeCandidateCount = $workingTreePaths.Count
    findingCount = $uniqueFindings.Count
    findings = $uniqueFindings
    redacted = $true
}

if (-not [string]::IsNullOrWhiteSpace($JsonOutputPath)) {
    $output = [IO.Path]::GetFullPath($JsonOutputPath)
    $directory = Split-Path -Parent $output
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $output -Encoding utf8NoBOM
}

$result | ConvertTo-Json -Depth 10
if ($uniqueFindings.Count -ne 0) { exit 1 }

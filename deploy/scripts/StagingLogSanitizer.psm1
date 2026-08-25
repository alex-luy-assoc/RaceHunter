Set-StrictMode -Version Latest

function Get-StagingLogValue {
    param([AllowNull()] [object] $InputObject, [Parameter(Mandatory)] [string] $Name)
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) { return $InputObject[$Name] }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Protect-StagingLogText {
    param([AllowNull()] [object] $Value, [int] $Maximum = 4000)
    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    $text = [regex]::Replace($text, '(?i)"(password|token|secret|cookie|connectionstrings?)"\s*:\s*"[^"]*"', '"$1":"[REDACTED]"')
    $text = [regex]::Replace($text, '(?i)\b(authorization|proxy-authorization)\s*[:=]\s*(?:bearer\s+)?[^\s,;"}]+', '$1=[REDACTED]')
    $text = [regex]::Replace($text, '(?i)\b(password|token|secret|cookie|connectionstrings?)\b\s*[:=]\s*(?:"[^"]*"|[^\s,;"}]+)', '$1=[REDACTED]')
    if ($text.Length -gt $Maximum) { return $text.Substring(0, $Maximum) + ' [TRUNCATED]' }
    return $text
}

function ConvertTo-StagingSafeLabels {
    param([AllowNull()] [object[]] $LabelSets)
    $safe = [ordered]@{}
    $allowedNames = @('project_id', 'service_name', 'revision_name', 'location', 'configuration_name', 'execution_id')
    foreach ($set in @($LabelSets)) {
        if ($null -eq $set) { continue }
        $names = if ($set -is [System.Collections.IDictionary]) { @($set.Keys) } else { @($set.PSObject.Properties.Name) }
        foreach ($nameValue in $names) {
            $name = [string]$nameValue
            if ($allowedNames -cnotcontains $name) { continue }
            $value = Get-StagingLogValue $set $name
            if ($null -eq $value -or $value -isnot [string] -and $value -isnot [ValueType]) { continue }
            $safe[$name] = Protect-StagingLogText $value 512
        }
    }
    return $safe
}

function ConvertFrom-StagingLoggingEntriesResponse {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Response)

    $entriesValue = Get-StagingLogValue $Response 'entries'
    if ($null -eq $entriesValue) { return }
    foreach ($entry in @($entriesValue)) {
        if ($null -eq $entry) { continue }
        $json = Get-StagingLogValue $entry 'jsonPayload'
        $resource = Get-StagingLogValue $entry 'resource'
        $resourceLabels = Get-StagingLogValue $resource 'labels'
        $entryLabels = Get-StagingLogValue $entry 'labels'
        $message = Get-StagingLogValue $entry 'textPayload'
        if ($null -eq $message) { $message = Get-StagingLogValue $json 'message' }
        $exceptionValue = Get-StagingLogValue $json 'exception'
        $exceptionType = Get-StagingLogValue $json 'exceptionType'
        $exceptionMessage = $exceptionValue
        $stackTrace = Get-StagingLogValue $json 'stackTrace'
        if ($null -eq $stackTrace) { $stackTrace = Get-StagingLogValue $json 'stack_trace' }
        if ($null -ne $exceptionValue -and $exceptionValue -isnot [string] -and $exceptionValue -isnot [ValueType]) {
            if ($null -eq $exceptionType) { $exceptionType = Get-StagingLogValue $exceptionValue 'type' }
            $exceptionMessage = Get-StagingLogValue $exceptionValue 'message'
            if ($null -eq $stackTrace) { $stackTrace = Get-StagingLogValue $exceptionValue 'stackTrace' }
        }
        $category = Get-StagingLogValue $json 'category'
        if ($null -eq $category) { $category = Get-StagingLogValue $json 'CategoryName' }
        if ($null -eq $category -or [string]$category -notmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$') { $category = $null }
        $safeLabels = ConvertTo-StagingSafeLabels @($resourceLabels, $entryLabels)
        [pscustomobject][ordered]@{
            timestamp = [string](Get-StagingLogValue $entry 'timestamp')
            severity = [string](Get-StagingLogValue $entry 'severity')
            insertId = [string](Get-StagingLogValue $entry 'insertId')
            logName = Protect-StagingLogText (Get-StagingLogValue $entry 'logName') 512
            revision = [string](Get-StagingLogValue $resourceLabels 'revision_name')
            category = Protect-StagingLogText $category 512
            labels = [pscustomobject]$safeLabels
            trace = Protect-StagingLogText (Get-StagingLogValue $entry 'trace') 512
            spanId = Protect-StagingLogText (Get-StagingLogValue $entry 'spanId') 128
            message = Protect-StagingLogText $message 4000
            exceptionType = Protect-StagingLogText $exceptionType 512
            exception = Protect-StagingLogText $exceptionMessage 6000
            stackTrace = Protect-StagingLogText $stackTrace 12000
        }
    }
}

Export-ModuleMember -Function 'ConvertFrom-StagingLoggingEntriesResponse'

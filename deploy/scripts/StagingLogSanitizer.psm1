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

function ConvertFrom-StagingLoggingEntriesResponse {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Response)

    $entriesValue = Get-StagingLogValue $Response 'entries'
    if ($null -eq $entriesValue) { return }
    foreach ($entry in @($entriesValue)) {
        if ($null -eq $entry) { continue }
        $json = Get-StagingLogValue $entry 'jsonPayload'
        $resource = Get-StagingLogValue $entry 'resource'
        $labels = Get-StagingLogValue $resource 'labels'
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
        [pscustomobject][ordered]@{
            timestamp = [string](Get-StagingLogValue $entry 'timestamp')
            severity = [string](Get-StagingLogValue $entry 'severity')
            insertId = [string](Get-StagingLogValue $entry 'insertId')
            logName = Protect-StagingLogText (Get-StagingLogValue $entry 'logName') 512
            revision = [string](Get-StagingLogValue $labels 'revision_name')
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

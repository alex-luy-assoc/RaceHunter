Set-StrictMode -Version Latest

function Get-StagingResponseStatusCode {
    param([Parameter(Mandatory)] [object] $ErrorRecord)

    $exceptionProperty = $ErrorRecord.PSObject.Properties['Exception']
    if ($null -eq $exceptionProperty -or $null -eq $exceptionProperty.Value) { return $null }
    $responseProperty = $exceptionProperty.Value.PSObject.Properties['Response']
    if ($null -eq $responseProperty -or $null -eq $responseProperty.Value) { return $null }
    $statusProperty = $responseProperty.Value.PSObject.Properties['StatusCode']
    if ($null -eq $statusProperty -or $null -eq $statusProperty.Value) { return $null }
    $numericProperty = $statusProperty.Value.PSObject.Properties['value__']
    if ($null -ne $numericProperty -and $null -ne $numericProperty.Value) { return [int]$numericProperty.Value }
    try { return [int]$statusProperty.Value } catch { return $null }
}

function Get-StagingPropertyValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

Export-ModuleMember -Function 'Get-StagingResponseStatusCode', 'Get-StagingPropertyValue'

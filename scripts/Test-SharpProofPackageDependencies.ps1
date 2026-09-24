Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')

function Get-SharpProofThirdPartyComponentGraph {
    param(
        [Parameter()]
        [string]$ContractPath = (Join-Path `
            (Join-Path $PSScriptRoot '..') `
            'eng/release/third-party-components.json')
    )

    $contract = Get-Content -LiteralPath $ContractPath -Raw |
        ConvertFrom-Json
    if ($contract.schemaVersion -ne 1 -or
        $null -eq $contract.PSObject.Properties['packages']) {
        throw 'Unsupported third-party component license authority.'
    }
    return @($contract.packages.PSObject.Properties | ForEach-Object {
        $packageId = $_.Name
        @($_.Value) | ForEach-Object {
            $entries = @(@($_.entries) |
                ForEach-Object { [string]$_ } |
                Sort-Object)
            $entrySha256 = @()
            if ($null -ne $_.PSObject.Properties['entrySha256']) {
                $entrySha256 = @($_.entrySha256 | ForEach-Object {
                    [pscustomobject][ordered]@{
                        path = [string]$_.path
                        sha256 = [string]$_.sha256
                    }
                } | Sort-Object path)
            }
            $entrySha256Paths = @(
                $entrySha256 | ForEach-Object { [string]$_.path }
            )
            if (@($entrySha256Paths | Sort-Object -Unique).Count -ne
                    $entrySha256Paths.Count -or
                @($entrySha256 | Where-Object {
                    $_.path -notin $entries -or
                    $_.sha256 -cnotmatch '^[0-9a-f]{64}$'
                }).Count -ne 0 -or
                ($_.id -ceq 'Microsoft.Z3' -and
                 ((@($entrySha256Paths | Sort-Object) -join '|') -cne
                    (@($entries | Sort-Object) -join '|')))) {
                throw "Third-party component '$($_.id)' has an invalid entry digest inventory."
            }
            [pscustomobject][ordered]@{
                packageId = $packageId
                id = [string]$_.id
                version = [string]$_.version
                license = [string]$_.license
                entries = $entries
                entrySha256 = $entrySha256
            }
        }
    })
}

function Test-SharpProofThirdPartyComponentProjection {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ActualComponents,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ExpectedComponents
    )

    $propertyNames = @(
        'entries', 'entrySha256', 'id', 'license', 'packageId', 'version'
    )
    function ConvertTo-ComponentRecord {
        param(
            [Parameter(Mandatory = $true)][object]$Component,
            [Parameter()][switch]$ValidateShape)

        if ($ValidateShape) {
            $actualPropertyNames = @($Component.PSObject.Properties.Name |
                Sort-Object)
            if (($actualPropertyNames -join '|') -cne
                ($propertyNames -join '|')) {
                throw 'Third-party component inventory has an invalid schema.'
            }
        }
        return [pscustomobject][ordered]@{
            packageId = [string]$Component.packageId
            id = [string]$Component.id
            version = [string]$Component.version
            license = [string]$Component.license
            entries = @(@($Component.entries) |
                ForEach-Object { [string]$_ } |
                Sort-Object)
            entrySha256 = @($Component.entrySha256 | ForEach-Object {
                [pscustomobject][ordered]@{
                    path = [string]$_.path
                    sha256 = [string]$_.sha256
                }
            } | Sort-Object path)
        }
    }
    $actual = @($ActualComponents |
        ForEach-Object { ConvertTo-ComponentRecord $_ -ValidateShape } |
        Sort-Object packageId, id, version)
    $expected = @($ExpectedComponents |
        ForEach-Object { ConvertTo-ComponentRecord $_ } |
        Sort-Object packageId, id, version)
    Assert-SharpProofCanonicalMatch `
        -Actual $actual -Expected $expected -Depth 4 `
        -Message 'Third-party component inventory does not match the authenticated catalog projection.'
}

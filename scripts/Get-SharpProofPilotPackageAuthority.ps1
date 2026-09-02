Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Get-SharpProofPilotPackageAuthority {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PackageSource,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit
    )

    $expectedIds = @('SharpProof.Attributes', 'SharpProof', 'SharpProof.Verifier')
    $expectedNames = @($expectedIds | ForEach-Object {
            "$_.${ExpectedVersion}.nupkg"; "$_.${ExpectedVersion}.snupkg"
        })
    $files = @(Get-ChildItem -LiteralPath $PackageSource -File |
        Where-Object Extension -in @('.nupkg', '.snupkg') | Sort-Object Name)
    if ($files.Count -ne 6 -or
        @($files.Name | Where-Object { $expectedNames -cnotcontains $_ }).Count -ne 0 -or
        @($files.Name | Select-Object -Unique).Count -ne 6) {
        throw 'Pilot qualification requires the exact six candidate package files.'
    }
    return @($files | ForEach-Object {
        $identity = Get-SharpProofPackageIdentity -Path $_.FullName
        $id = [string]$identity.Id
        $version = [string]$identity.Version
        $commit = [string]$identity.RepositoryCommit
        if ($expectedIds -cnotcontains $id -or $version -cne $ExpectedVersion -or
            $commit -cne $ExpectedCommit -or
            $_.Name -cne "$id.$ExpectedVersion$($_.Extension)") {
            throw "Package '$($_.Name)' does not match candidate identity: id='$id', version='$version', commit='$commit'."
        }
        [ordered]@{
            fileName = $_.Name
            packageId = $id
            version = $version
            repositoryCommit = $commit
            bytes = [int64]$_.Length
        }
    })
}

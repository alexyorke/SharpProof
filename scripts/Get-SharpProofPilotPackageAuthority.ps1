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
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    return @($files | ForEach-Object {
        $archive = [IO.Compression.ZipFile]::OpenRead($_.FullName)
        try {
            $entries = @($archive.Entries | Where-Object {
                    $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)
                })
            if ($entries.Count -ne 1) { throw "Package '$($_.Name)' must contain one nuspec." }
            $reader = [IO.StreamReader]::new($entries[0].Open())
            try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $ns = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
            $ns.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
            $metadata = $nuspec.SelectSingleNode('/n:package/n:metadata', $ns)
            $id = [string]$metadata.id
            $version = [string]$metadata.version
            $repository = $metadata.SelectSingleNode('n:repository', $ns)
            $commit = if ($null -eq $repository) { '' } else { $repository.GetAttribute('commit') }
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
        }
        finally { $archive.Dispose() }
    })
}

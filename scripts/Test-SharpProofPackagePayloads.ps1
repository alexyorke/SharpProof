Set-StrictMode -Version Latest

function Get-SharpProofArchiveAssemblyName {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchiveEntry]$Entry
    )

    $temporary = Join-Path `
        ([IO.Path]::GetTempPath()) `
        ('sharpproof-assembly-' + [Guid]::NewGuid().ToString('N') + '.dll')
    try {
        $input = $Entry.Open()
        try {
            $output = [IO.File]::Create($temporary)
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
            }
        }
        finally {
            $input.Dispose()
        }
        return [Reflection.AssemblyName]::GetAssemblyName($temporary).Name
    }
    finally {
        if ([IO.File]::Exists($temporary)) {
            [IO.File]::Delete($temporary)
        }
    }
}

function Get-SharpProofPayloadSpecifications {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$PackageId
    )

    if ($PackageId -eq 'SharpProof.Attributes') {
        return @(
            [pscustomobject][ordered]@{
                Entry = 'lib/netstandard2.0/SharpProof.Attributes.dll'
                Source = Join-Path `
                    $RepositoryRoot `
                    'SharpProof.Attributes/bin/Release/netstandard2.0/SharpProof.Attributes.dll'
            }
            [pscustomobject][ordered]@{
                Entry = 'lib/netstandard2.0/SharpProof.Attributes.xml'
                Source = Join-Path `
                    $RepositoryRoot `
                    'SharpProof.Attributes/bin/Release/netstandard2.0/SharpProof.Attributes.xml'
            }
            [pscustomobject][ordered]@{
                Entry = 'LICENSE'
                Source = Join-Path $RepositoryRoot 'LICENSE'
            }
            [pscustomobject][ordered]@{
                Entry = 'README.md'
                Source = Join-Path $RepositoryRoot 'README.md'
            }
        )
    }
    $nuspecRelativePath = switch ($PackageId) {
        'SharpProof' { 'SharpProof.Package/SharpProof.nuspec' }
        'SharpProof.Verifier' {
            'SharpProof.Verifier/SharpProof.Verifier.nuspec'
        }
        default { throw "Unsupported package payload owner '$PackageId'." }
    }
    $nuspecPath = Join-Path $RepositoryRoot $nuspecRelativePath
    [xml]$nuspec = Get-Content -LiteralPath $nuspecPath -Raw
    $namespace = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
    $namespace.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
    $nuspecDirectory = [IO.Path]::GetDirectoryName($nuspecPath)
    return @(
        $nuspec.SelectNodes('/n:package/n:files/n:file', $namespace) |
            Where-Object {
                # Every nuspec-declared file is part of the authenticated
                # payload, including executable MSBuild props/targets and
                # catalogs.  Restricting this to binaries leaves behavioral
                # package inputs mutable without changing the evidence.
                $_.GetAttribute('src') -notmatch '\.pdb$' -and (
                    $_.GetAttribute('src') -notmatch '\$nativeroot\$' -or
                    $_.GetAttribute('src') -match '\.(?:dll|so)$')
            } |
            ForEach-Object {
                $source = ([string]$_.GetAttribute('src')).
                    Replace('$configuration$', 'Release').
                    Replace('\', [IO.Path]::DirectorySeparatorChar)
                $target = ([string]$_.GetAttribute('target')).
                    Replace('\', '/')
                [pscustomobject][ordered]@{
                    Entry = if ([string]::IsNullOrEmpty($target)) {
                        [IO.Path]::GetFileName($source)
                    }
                    else {
                        $target.TrimEnd('/') + '/' +
                            [IO.Path]::GetFileName($source)
                    }
                    Source = if ($source.Contains('$nativeroot$')) {
                        $null
                    }
                    else {
                        [IO.Path]::GetFullPath((Join-Path $nuspecDirectory $source))
                    }
                }
            }
    )
}

function Test-SharpProofPackagePayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Components,

        [Parameter()]
        [AllowEmptyCollection()]
        [object[]]$ExpectedPayloads,

        [Parameter()]
        [hashtable]$ValidationCache,

        [Parameter()]
        [AllowNull()]
        [IO.Compression.ZipArchive]$Archive
    )

    $useEvidence = $null -ne $ExpectedPayloads
    if ($null -eq $ValidationCache) {
        $ValidationCache = @{}
    }
    if (-not $ValidationCache.ContainsKey('FirstPartyNames')) {
        $ownership = Get-Content `
            -LiteralPath (Join-Path `
                $RepositoryRoot `
                'eng/release/first-party-assemblies.json') `
            -Raw |
            ConvertFrom-Json
        if ($ownership.schemaVersion -ne 1) {
            throw 'Unsupported first-party assembly inventory schema.'
        }
        $ValidationCache['FirstPartyNames'] = @(
            $ownership.assemblyNames |
                ForEach-Object { [string]$_ })
    }
    $firstPartyNames = @($ValidationCache['FirstPartyNames'])
    $specifications = if ($useEvidence) {
        @($ExpectedPayloads | ForEach-Object {
            [pscustomobject][ordered]@{
                Entry = [string]$_.path
                Evidence = $_
            }
        })
    }
    else {
        @(Get-SharpProofPayloadSpecifications `
            -RepositoryRoot $RepositoryRoot `
            -PackageId $PackageId)
    }
    $declaredThirdParty = @(
        $Components |
            ForEach-Object { @($_.entries) } |
            ForEach-Object { [string]$_ } |
            Sort-Object
    )
    $ownsArchive = $null -eq $Archive
    if ($ownsArchive) {
        $Archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    }
    try {
        $payloadEntries = @(
            $Archive.Entries |
                Where-Object {
                    $_.FullName -ne ($PackageId.ToLowerInvariant() + '.nuspec') -and
                    $_.FullName -ne '_rels/.rels' -and
                    $_.FullName -ne '[Content_Types].xml' -and
                    -not $_.FullName.StartsWith(
                        'package/services/metadata/core-properties/',
                        [StringComparison]::Ordinal)
                }
        )
        $duplicate = @(
            $payloadEntries |
                Group-Object FullName |
                Where-Object Count -ne 1
        )
        if ($duplicate.Count -ne 0) {
            throw "Package '$PackageId' has a duplicate managed/native payload: '$($duplicate[0].Name)'."
        }
        $actualPaths = @($payloadEntries.FullName | Sort-Object)
        $expectedPaths = @($specifications.Entry | Sort-Object)
        if (($actualPaths -join '|') -ne ($expectedPaths -join '|')) {
            throw "Package '$PackageId' does not have the exact managed/native payload closure. Actual: $($actualPaths -join ', '). Expected: $($expectedPaths -join ', ')."
        }

        $actualThirdParty = [Collections.Generic.List[string]]::new()
        $payloadEvidence = [Collections.Generic.List[object]]::new()
        $toolchain = $null
        foreach ($specification in $specifications) {
            $entry = $Archive.GetEntry([string]$specification.Entry)
            if ($null -eq $entry) {
                throw "Package '$PackageId' is missing payload '$($specification.Entry)'."
            }
            if (-not $useEvidence -and
                $entry.FullName -in @(
                    'tools/native/linux-x64/libz3.so',
                    'tools/net9/Microsoft.Z3.dll')) {
                if (-not $ValidationCache.ContainsKey('Toolchain')) {
                    $ValidationCache['Toolchain'] = Get-Content `
                        -LiteralPath (Join-Path `
                            $RepositoryRoot 'eng/container/toolchain.json') `
                        -Raw |
                        ConvertFrom-Json
                }
                $toolchain = $ValidationCache['Toolchain']
            }
            $assemblyName = $null
            if ($useEvidence) {
                $expected = $specification.Evidence
                if ($entry.Length -ne [int64]$expected.bytes) {
                    throw "Package '$PackageId' payload size does not match release evidence: '$($entry.FullName)'."
                }
                if ([string]$expected.owner -eq 'thirdParty') {
                    $actualThirdParty.Add($entry.FullName)
                }
                if ($entry.FullName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                    $actualName = Get-SharpProofArchiveAssemblyName -Entry $entry
                    if ($actualName -ne [string]$expected.assemblyName) {
                        throw "Package '$PackageId' assembly identity does not match release evidence: '$($entry.FullName)'."
                    }
                }
            }
            elseif ($entry.FullName -eq 'tools/native/linux-x64/libz3.so') {
                if ($entry.Length -ne [int64]$toolchain.z3.libraryBytes) {
                    throw "Package '$PackageId' native payload size is invalid: '$($entry.FullName)'."
                }
                $actualThirdParty.Add($entry.FullName)
            }
            elseif ($entry.FullName -eq 'tools/net9/Microsoft.Z3.dll') {
                if ($entry.Length -ne [int64]$toolchain.z3.managedAssemblyBytes) {
                    throw "Package '$PackageId' managed Z3 payload size is invalid."
                }
                $actualThirdParty.Add($entry.FullName)
                $assemblyName = Get-SharpProofArchiveAssemblyName -Entry $entry
            }
            else {
                $sourcePath = [string]$specification.Source
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw "Authoritative package output is missing: '$sourcePath'."
                }
                if ($entry.FullName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                    $assemblyName = Get-SharpProofArchiveAssemblyName -Entry $entry
                    $expectedName = [Reflection.AssemblyName]::GetAssemblyName($sourcePath).Name
                    if ($assemblyName -ne $expectedName) {
                        throw "Package '$PackageId' assembly identity is invalid for '$($entry.FullName)'."
                    }
                    if ($firstPartyNames -notcontains $expectedName) {
                        $actualThirdParty.Add($entry.FullName)
                    }
                }
            }
            if (-not $useEvidence) {
                $payloadEvidence.Add([pscustomobject][ordered]@{
                    path = $entry.FullName
                    owner = if ($actualThirdParty.Contains($entry.FullName)) {
                        'thirdParty'
                    }
                    else {
                        'firstParty'
                    }
                    assemblyName = $assemblyName
                    bytes = [int64]$entry.Length
                })
            }
        }
        if ((@($actualThirdParty | Sort-Object) -join '|') -ne
            ($declaredThirdParty -join '|')) {
            throw "Third-party inventory for '$PackageId' does not match the authoritative package payload closure."
        }
        if (-not $useEvidence) {
            return @($payloadEvidence | Sort-Object path)
        }
    }
    finally {
        if ($ownsArchive) {
            $Archive.Dispose()
        }
    }
}

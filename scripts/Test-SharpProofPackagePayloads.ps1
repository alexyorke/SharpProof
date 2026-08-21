Set-StrictMode -Version Latest

function Get-SharpProofArchiveEntryHash {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchiveEntry]$Entry
    )

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = $Entry.Open()
        try {
            return [Convert]::ToHexString(
                $algorithm.ComputeHash($stream)).ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $algorithm.Dispose()
    }
}

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
        return ,([pscustomobject][ordered]@{
            Entry = 'lib/netstandard2.0/SharpProof.Attributes.dll'
            Source = Join-Path `
                $RepositoryRoot `
                'SharpProof.Attributes/bin/Release/netstandard2.0/SharpProof.Attributes.dll'
        })
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
                $_.src -match '\.(?:dll|so)$'
            } |
            ForEach-Object {
                $source = ([string]$_.src).
                    Replace('$configuration$', 'Release').
                    Replace('\', [IO.Path]::DirectorySeparatorChar)
                $target = ([string]$_.target).
                    Replace('\', '/')
                [pscustomobject][ordered]@{
                    Entry = $target.TrimEnd('/') + '/' +
                        [IO.Path]::GetFileName($source)
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
        [object[]]$ExpectedPayloads
    )

    $ownership = Get-Content `
        -LiteralPath (Join-Path `
            $RepositoryRoot `
            'eng/release/first-party-assemblies.json') `
        -Raw |
        ConvertFrom-Json
    if ($ownership.schemaVersion -ne 1) {
        throw 'Unsupported first-party assembly inventory schema.'
    }
    $firstPartyNames = @($ownership.assemblyNames | ForEach-Object { [string]$_ })
    $useEvidence = $null -ne $ExpectedPayloads
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
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $payloadEntries = @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
                    $_.FullName.EndsWith('.so', [StringComparison]::OrdinalIgnoreCase)
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
        $toolchain = Get-Content `
            -LiteralPath (Join-Path $RepositoryRoot 'eng/container/toolchain.json') `
            -Raw |
            ConvertFrom-Json
        foreach ($specification in $specifications) {
            $entry = $archive.GetEntry([string]$specification.Entry)
            if ($null -eq $entry) {
                throw "Package '$PackageId' is missing payload '$($specification.Entry)'."
            }
            $actualHash = Get-SharpProofArchiveEntryHash -Entry $entry
            if ($useEvidence) {
                $expected = $specification.Evidence
                $expectedHash = [string]$expected.sha256
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
                $expectedHash = [string]$toolchain.z3.librarySha256
                if ($entry.Length -ne [int64]$toolchain.z3.libraryBytes) {
                    throw "Package '$PackageId' native payload size is invalid: '$($entry.FullName)'."
                }
                $actualThirdParty.Add($entry.FullName)
            }
            elseif ($entry.FullName -eq 'tools/net9/Microsoft.Z3.dll') {
                $expectedHash = [string]$toolchain.z3.managedAssemblySha256
                if ($entry.Length -ne [int64]$toolchain.z3.managedAssemblyBytes) {
                    throw "Package '$PackageId' managed Z3 payload size is invalid."
                }
                $actualThirdParty.Add($entry.FullName)
            }
            else {
                $sourcePath = [string]$specification.Source
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw "Authoritative package output is missing: '$sourcePath'."
                }
                $expectedHash = (Get-FileHash `
                    -LiteralPath $sourcePath `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($entry.FullName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                    $actualName = Get-SharpProofArchiveAssemblyName -Entry $entry
                    $expectedName = [Reflection.AssemblyName]::GetAssemblyName($sourcePath).Name
                    if ($actualName -ne $expectedName) {
                        throw "Package '$PackageId' assembly identity is invalid for '$($entry.FullName)'."
                    }
                    if ($firstPartyNames -notcontains $expectedName) {
                        $actualThirdParty.Add($entry.FullName)
                    }
                }
            }
            if ($actualHash -ne $expectedHash) {
                throw "Package '$PackageId' payload hash does not match its authoritative output: '$($entry.FullName)'."
            }
            if (-not $useEvidence) {
                $assemblyName = if ($entry.FullName.EndsWith(
                        '.dll',
                        [StringComparison]::OrdinalIgnoreCase)) {
                    Get-SharpProofArchiveAssemblyName -Entry $entry
                }
                else {
                    $null
                }
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
                    sha256 = $actualHash
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
        $archive.Dispose()
    }
}

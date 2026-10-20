Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.EmbeddedSymbols.psm1') -Force

function Test-SharpProofFacadePackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][string]$RepositoryCommit
    )

    foreach ($value in [Collections.Immutable.ImmutableArray]::Create[string]($PackagePath, $PackageVersion, $RepositoryCommit)) {
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Facade validation requires nonblank package path, version, and repository commit.' }
    }
    if ($RepositoryCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'The expected repository commit is not a 40-character lowercase hash.' }
    if ([IO.Path]::GetFileName($PackagePath) -cne "SharpProof.$PackageVersion.nupkg") { throw 'The facade package filename does not match its exact identity.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $names = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            if (-not $names.Add($entry.FullName)) { throw 'The facade package contains duplicate ZIP entries.' }
            if ([IO.Path]::GetExtension($entry.FullName) -in [Collections.Immutable.ImmutableArray]::Create[string]('.dll', '.exe', '.pdb')) { throw 'The metadata-only facade must not contain executable payloads or PDBs.' }
        }
        $nuspecs = [Collections.Immutable.ImmutableArray]::CreateRange[IO.Compression.ZipArchiveEntry]([IO.Compression.ZipArchiveEntry[]]@($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) }))
        if ($nuspecs.Length -ne 1) { throw 'The facade package must contain exactly one nuspec.' }
        $stream = $nuspecs[0].Open()
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.Load($reader)
        }
        finally { $reader.Dispose(); $stream.Dispose() }
        $namespaces = [Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespaces.AddNamespace('n', [string]$document.DocumentElement.NamespaceURI)
        $repositories = [Collections.Immutable.ImmutableArray]::CreateRange[Xml.XmlNode]([Xml.XmlNode[]]@($document.SelectNodes('/n:package/n:metadata/n:repository', $namespaces)))
        if ($repositories.Length -ne 1 -or $repositories[0].GetAttribute('commit') -cne $RepositoryCommit) { throw 'The facade repository commit does not match its exact source authority.' }
        $dependencies = [Collections.Immutable.ImmutableArray]::CreateRange[Xml.XmlNode]([Xml.XmlNode[]]@($document.SelectNodes('/n:package/n:metadata/n:dependencies//n:dependency', $namespaces)))
        if ($dependencies.Length -ne 1 -or $dependencies[0].GetAttribute('id') -cne 'SharpProof.Verifier' -or
            $dependencies[0].GetAttribute('version') -cne "[$PackageVersion]") { throw 'The facade must have exactly one dependency on the exact matching SharpProof.Verifier version.' }
    }
    finally { $archive.Dispose() }
    Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
    $identity = Get-SharpProofPackageIdentity -Path $PackagePath -RequireSingleIdentity -RequireRepository
    if ($identity.Id -cne 'SharpProof' -or $identity.Version -cne $PackageVersion) { throw 'The facade nuspec ID or version does not match its exact identity.' }
}

function Test-SharpProofEmbeddedPdbPair {
    param(
        [Parameter(Mandatory = $true)][string]$AssemblyPath,
        [Parameter(Mandatory = $true)][string]$PdbPath,
        [Parameter(Mandatory = $true)][bool]$RequireEmbedded
    )

    Import-Module (Join-Path $PSScriptRoot 'SharpProof.EmbeddedSymbols.psm1') -Force
    $embeddedImage = Get-SharpProofEmbeddedPdb -AssemblyPath $AssemblyPath
    $embedded = if ($null -eq $embeddedImage) { , [Collections.Immutable.ImmutableArray[byte]]::Empty } else { , [Collections.Immutable.ImmutableArray]::CreateRange[byte]($embeddedImage) }
    $portable = [Collections.Immutable.ImmutableArray]::CreateRange[byte]([IO.File]::ReadAllBytes($PdbPath))
    if ($RequireEmbedded) {
        if ($embedded.Length -eq 0) { throw 'Shipping tooling is missing its embedded portable PDB.' }
        $embeddedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]@($embedded)))
        $portableHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]@($portable)))
        if ($embeddedHash -cne $portableHash) { throw 'The embedded PDB does not match the exact external portable PDB bytes.' }
    }
    elseif ($embedded.Length -ne 0) { throw 'The Attributes runtime assembly must retain its external portable PDB format.' }
    Test-SharpProofEmbeddedSources -PdbBytes $portable
}

function Test-SharpProofEmbeddedSources {
    param([Parameter(Mandatory = $true)][Collections.Immutable.ImmutableArray[byte]]$PdbBytes)

    if ($PdbBytes.IsDefault) { throw [ArgumentNullException]::new('PdbBytes') }
    $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbImage($PdbBytes)
    try {
        $reader = $provider.GetMetadataReader()
        $embeddedSourceBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, Collections.Immutable.ImmutableArray[byte]]()
        foreach ($handle in $reader.CustomDebugInformation) {
            $information = $reader.GetCustomDebugInformation($handle)
            if ($reader.GetGuid($information.Kind) -ne [guid]'0e8a571b-6926-466e-b4ad-8ab04611f5fe') { continue }
            if ($information.Parent.Kind.ToString() -cne 'Document') { throw 'Embedded source is attached to an invalid PDB parent.' }
            $row = [System.Reflection.Metadata.Ecma335.MetadataTokens]::GetRowNumber($information.Parent)
            if ($embeddedSourceBuilder.ContainsKey($row)) { throw 'A PDB document contains duplicate embedded-source records.' }
            $embeddedSourceBuilder.Add($row, [Collections.Immutable.ImmutableArray]::CreateRange[byte]($reader.GetBlobBytes($information.Value)))
        }
        $embeddedSources = $embeddedSourceBuilder.ToImmutable()
        $row = 0
        foreach ($documentHandle in $reader.Documents) {
            $row++
            $document = $reader.GetDocument($documentHandle)
            if (-not $embeddedSources.ContainsKey($row)) { throw 'A PDB source document has no embedded source.' }
            if ($reader.GetGuid($document.HashAlgorithm) -ne [guid]'8829d00f-11b8-4213-878b-770e8597ac16') { throw 'A PDB source document does not use SHA256.' }
            [byte[]]$blob = @($embeddedSources[$row])
            if ($blob.Length -lt 4) { throw 'Embedded source is malformed.' }
            $expectedLength = [BitConverter]::ToInt32($blob, 0)
            if ($expectedLength -lt 0) { throw 'Embedded source declares an invalid length.' }
            $compressed = [IO.MemoryStream]::new($blob, 4, $blob.Length - 4, $false)
            $source = [IO.MemoryStream]::new()
            try {
                if ($expectedLength -eq 0) { $compressed.CopyTo($source) }
                else {
                    $deflate = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress, $true)
                    try {
                        $buffer = [byte[]]::new(81920)
                        while (($count = $deflate.Read($buffer, 0, $buffer.Length)) -ne 0) {
                            if ($source.Length + $count -gt $expectedLength) { throw 'Embedded source exceeds its declared length.' }
                            $source.Write($buffer, 0, $count)
                        }
                    }
                    finally { $deflate.Dispose() }
                    if ($source.Length -ne $expectedLength) { throw 'Embedded source does not match its declared length.' }
                }
                $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($source.ToArray()))
                $expectedHash = [Convert]::ToHexString($reader.GetBlobBytes($document.Hash))
                if ($actualHash -cne $expectedHash) { throw 'Embedded source does not match its PDB document SHA256.' }
            }
            finally { $source.Dispose(); $compressed.Dispose() }
        }
        if ($row -eq 0 -or $embeddedSources.Count -ne $row) { throw 'The PDB embedded-source inventory does not match its document inventory.' }
    }
    finally { $provider.Dispose() }
}

function Test-SharpProofPackagedEmbeddedSymbols {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$SymbolPackagePath,
        [Parameter(Mandatory = $true)][string]$PackageId
    )

    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $temporary = Join-Path $temporaryParent ('sharpproof-symbol-pair-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    $package = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    $symbols = [IO.Compression.ZipFile]::OpenRead($SymbolPackagePath)
    try {
        foreach ($assembly in $package.Entries) {
            if (-not $assembly.FullName.EndsWith('.dll', [StringComparison]::Ordinal) -or
                -not [IO.Path]::GetFileName($assembly.FullName).StartsWith('SharpProof.', [StringComparison]::Ordinal)) { continue }
            $pdbName = $assembly.FullName.Substring(0, $assembly.FullName.Length - 4) + '.pdb'
            $pdb = $symbols.GetEntry($pdbName)
            if ($null -eq $pdb) { throw 'The exact tooling symbol package PDB is missing.' }
            $assemblyPath = Join-Path $temporary 'assembly.dll'
            $pdbPath = Join-Path $temporary 'assembly.pdb'
            [IO.Compression.ZipFileExtensions]::ExtractToFile($assembly, $assemblyPath, $true)
            [IO.Compression.ZipFileExtensions]::ExtractToFile($pdb, $pdbPath, $true)
            Test-SharpProofEmbeddedPdbPair -AssemblyPath $assemblyPath -PdbPath $pdbPath -RequireEmbedded ($PackageId -cne 'SharpProof.Attributes')
        }
    }
    finally {
        $symbols.Dispose(); $package.Dispose()
        $fullTemporary = [IO.Path]::GetFullPath($temporary)
        if (-not $fullTemporary.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
            [IO.Path]::GetFileName($fullTemporary) -notlike 'sharpproof-symbol-pair-*') { throw 'Refusing to remove an unexpected symbol validation directory.' }
        Remove-Item -LiteralPath $fullTemporary -Recurse -Force
    }
}

function Get-SharpProofSymbolPackageValidatorType {
    $validatorType = 'SharpProofSymbolPackageValidator' -as [type]
    if ($null -eq $validatorType) {
        $validatorType = @(
            Add-Type `
                -Path (Join-Path `
                    $PSScriptRoot `
                    'SharpProof.SymbolPackageValidator.cs') `
                -PassThru |
                Where-Object Name -EQ 'SharpProofSymbolPackageValidator'
        )[0]
    }
    return $validatorType
}

function Test-SharpProofSymbolPackagePair {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$SymbolPackagePath,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$PackageVersion,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryCommit
    )

    $validatorType = Get-SharpProofSymbolPackageValidatorType
    try {
        $validatorType.GetMethod('Validate').Invoke(
            $null,
            @(
                $PackagePath,
                $SymbolPackagePath,
                $PackageId,
                $PackageVersion,
                $RepositoryCommit))
    }
    catch [Reflection.TargetInvocationException] {
        throw $_.Exception.InnerException
    }
    Test-SharpProofPackagedEmbeddedSymbols -PackagePath $PackagePath -SymbolPackagePath $SymbolPackagePath -PackageId $PackageId
}

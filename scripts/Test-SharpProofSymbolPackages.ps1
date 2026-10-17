Set-StrictMode -Version Latest

function Test-SharpProofFacadePackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][string]$RepositoryCommit
    )

    foreach ($value in @($PackagePath, $PackageVersion, $RepositoryCommit)) {
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Facade validation requires nonblank package path, version, and repository commit.' }
    }
    if ($RepositoryCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'The expected repository commit is not a 40-character lowercase hash.' }
    if ([IO.Path]::GetFileName($PackagePath) -cne "SharpProof.$PackageVersion.nupkg") { throw 'The facade package filename does not match its exact identity.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            if (-not $names.Add($entry.FullName)) { throw 'The facade package contains duplicate ZIP entries.' }
            if ([IO.Path]::GetExtension($entry.FullName) -in @('.dll', '.exe', '.pdb')) { throw 'The metadata-only facade must not contain executable payloads or PDBs.' }
        }
        $nuspecs = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
        if ($nuspecs.Count -ne 1) { throw 'The facade package must contain exactly one nuspec.' }
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
        $repositories = @($document.SelectNodes('/n:package/n:metadata/n:repository', $namespaces))
        if ($repositories.Count -ne 1 -or $repositories[0].GetAttribute('commit') -cne $RepositoryCommit) { throw 'The facade repository commit does not match its exact source authority.' }
        $dependencies = @($document.SelectNodes('/n:package/n:metadata/n:dependencies//n:dependency', $namespaces))
        if ($dependencies.Count -ne 1 -or $dependencies[0].GetAttribute('id') -cne 'SharpProof.Verifier' -or
            $dependencies[0].GetAttribute('version') -cne "[$PackageVersion]") { throw 'The facade must have exactly one dependency on the exact matching SharpProof.Verifier version.' }
    }
    finally { $archive.Dispose() }
    Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
    $identity = Get-SharpProofPackageIdentity -Path $PackagePath -RequireSingleIdentity -RequireRepository
    if ($identity.Id -cne 'SharpProof' -or $identity.Version -cne $PackageVersion) { throw 'The facade nuspec ID or version does not match its exact identity.' }
}

function Get-SharpProofSymbolPackageValidatorType {
    $validatorType = 'SharpProofSymbolPackageValidator' -as [type]
    if ($null -eq $validatorType) {
        $validatorType = @(
            Add-Type -Path (Join-Path $PSScriptRoot 'SharpProof.SymbolPackageValidator.cs') -PassThru |
                Where-Object Name -eq 'SharpProofSymbolPackageValidator'
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
}

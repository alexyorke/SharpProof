[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string]$BaselinePath,

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
$resolvedBaselinePath = (Resolve-Path -LiteralPath $BaselinePath -ErrorAction Stop).Path
$pathSeparator = [IO.Path]::DirectorySeparatorChar
$repositoryPrefix = $resolvedRepositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + $pathSeparator

function Invoke-GitText {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = @(& git -C $resolvedRepositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Coverage authority could not resolve Git metadata: ' +
            ($output -join "`n"))
    }
    return ($output -join "`n").Trim()
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Coverage authority input is missing: $Path"
    }
    return ([Security.Cryptography.SHA256]::HashData(
        [IO.File]::ReadAllBytes($Path)) |
        ForEach-Object { $_.ToString('x2') }) -join ''
}

function Resolve-RepositoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate,

        [Parameter(Mandatory = $true)]
        [string]$DocumentDescription
    )

    $normalized = $Candidate.Replace('\', '/')
    $full = if ([IO.Path]::IsPathRooted($normalized)) {
        [IO.Path]::GetFullPath($normalized)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $resolvedRepositoryRoot $normalized))
    }
    if (-not $full.StartsWith($repositoryPrefix, [StringComparison]::Ordinal)) {
        throw (
            "Coverage authority document '$DocumentDescription' is foreign " +
            "to the repository: '$Candidate'.")
    }
    $relative = $full.Substring($repositoryPrefix.Length).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or
        $relative.Contains('//') -or
        $relative.Split('/') -contains '.' -or
        $relative.Split('/') -contains '..') {
        throw "Coverage authority document path is not canonical: '$Candidate'."
    }
    return $relative
}

function Get-PdbDocumentPath {
    param(
        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.MetadataReader]$Reader,

        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.DocumentHandle]$Handle
    )

    if ($Handle.IsNil) {
        throw 'Coverage authority sequence point has no source document.'
    }
    $document = $Reader.GetDocument($Handle)
    $name = $Reader.GetString($document.Name)
    if ([string]::IsNullOrWhiteSpace($name)) {
        throw 'Coverage authority PDB contains a blank source document.'
    }
    return $name
}

function Get-PortablePdbModule {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectName,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath,

        [Parameter(Mandatory = $true)]
        [string]$PdbPath
    )

    $assemblyStream = [IO.File]::OpenRead($AssemblyPath)
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
    $metadataProvider = $null
    $pdbStream = $null
    $pdbProvider = $null
    try {
        if (-not $peReader.HasMetadata) {
            throw "Coverage authority assembly has no metadata: $AssemblyPath"
        }
        $metadataProvider = [System.Reflection.Metadata.MetadataReaderProvider]::FromMetadataImage(
            $peReader.GetMetadata().GetContent())
        $metadata = $metadataProvider.GetMetadataReader()
        $assembly = $metadata.GetAssemblyDefinition()
        $assemblyName = $metadata.GetString($assembly.Name)
        if ([string]::IsNullOrWhiteSpace($assemblyName)) {
            throw "Coverage authority assembly has no name: $AssemblyPath"
        }
        $module = $metadata.GetModuleDefinition()
        $mvid = $metadata.GetGuid($module.Mvid).ToString('D')

        $codeView = @(
            $peReader.ReadDebugDirectory() |
                Where-Object {
                    $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView
                })
        if ($codeView.Count -ne 1) {
            throw (
                "Coverage authority assembly must contain exactly one " +
                "portable CodeView entry: $AssemblyPath")
        }
        $codeViewData = $peReader.ReadCodeViewDebugDirectoryData($codeView[0])
        if (-not $codeView[0].IsPortableCodeView) {
            throw "Coverage authority assembly has a non-portable PDB: $AssemblyPath"
        }

        $pdbStream = [IO.File]::OpenRead($PdbPath)
        $pdbProvider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $pdb = $pdbProvider.GetMetadataReader()
        $sourceLines = [Collections.Generic.Dictionary[string,
            Collections.Generic.HashSet[int]]]::new([StringComparer]::Ordinal)
        foreach ($debugHandle in $pdb.MethodDebugInformation) {
            $debug = $pdb.GetMethodDebugInformation($debugHandle)
            foreach ($sequencePoint in $debug.GetSequencePoints()) {
                if ($sequencePoint.IsHidden) {
                    continue
                }
                $documentHandle = $sequencePoint.Document
                if ($documentHandle.IsNil) {
                    $documentHandle = $debug.Document
                }
                $sourceName = Get-PdbDocumentPath -Reader $pdb -Handle $documentHandle
                $relativePath = Resolve-RepositoryPath `
                    -Candidate $sourceName `
                    -DocumentDescription "${AssemblyPath}:$($sequencePoint.StartLine)"
                if (-not $relativePath.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
                    throw (
                        "Coverage authority PDB source document is not C#: " +
                        "'$relativePath'.")
                }
                if ($relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or
                    $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) {
                    continue
                }
                $sourcePath = Join-Path $resolvedRepositoryRoot (
                    $relativePath.Replace('/', $pathSeparator))
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw (
                        "Coverage authority source document is missing: " +
                        "'$relativePath'.")
                }
                if ($sequencePoint.StartLine -le 0) {
                    throw (
                        "Coverage authority PDB has an invalid sequence-point " +
                        "line for '$relativePath'.")
                }
                if (-not $sourceLines.ContainsKey($relativePath)) {
                    $sourceLines[$relativePath] =
                        [Collections.Generic.HashSet[int]]::new()
                }
                [void]$sourceLines[$relativePath].Add($sequencePoint.StartLine)
            }
        }
        if ($sourceLines.Count -eq 0) {
            throw "Coverage authority PDB has no production sequence points: $PdbPath"
        }

        $documents = foreach ($path in @($sourceLines.Keys | Sort-Object)) {
            [pscustomobject][ordered]@{
                path = $path
                sequencePoints = @(
                    $sourceLines[$path] | Sort-Object)
                sourceSha256 = Get-Sha256Hex -Path (
                    Join-Path $resolvedRepositoryRoot (
                        $path.Replace('/', $pathSeparator)))
            }
        }
        return [pscustomobject][ordered]@{
            project = $ProjectName
            assemblyName = $assemblyName
            assemblyPath = [IO.Path]::GetRelativePath(
                $resolvedRepositoryRoot,
                $AssemblyPath).Replace('\', '/')
            assemblySha256 = Get-Sha256Hex -Path $AssemblyPath
            moduleMvid = $mvid
            pdbPath = [IO.Path]::GetRelativePath(
                $resolvedRepositoryRoot,
                $PdbPath).Replace('\', '/')
            pdbSha256 = Get-Sha256Hex -Path $PdbPath
            pdbCodeViewGuid = $codeViewData.Guid.ToString('D')
            documents = @($documents)
        }
    }
    finally {
        if ($null -ne $pdbProvider) { $pdbProvider.Dispose() }
        if ($null -ne $pdbStream) { $pdbStream.Dispose() }
        if ($null -ne $metadataProvider) { $metadataProvider.Dispose() }
        $peReader.Dispose()
        $assemblyStream.Dispose()
    }
}

function Get-CoverageAuthorityHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Commit,

        [Parameter(Mandatory = $true)]
        [object[]]$Modules
    )

    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append("SharpProof.coverage-authority.v1`0")
    [void]$builder.Append($Commit).Append([char]0)
    foreach ($module in @($Modules | Sort-Object project)) {
        [void]$builder.Append($module.project).Append([char]0)
        [void]$builder.Append($module.assemblyName).Append([char]0)
        [void]$builder.Append($module.assemblyPath).Append([char]0)
        [void]$builder.Append($module.assemblySha256).Append([char]0)
        [void]$builder.Append($module.moduleMvid).Append([char]0)
        [void]$builder.Append($module.pdbPath).Append([char]0)
        [void]$builder.Append($module.pdbSha256).Append([char]0)
        [void]$builder.Append($module.pdbCodeViewGuid).Append([char]0)
        foreach ($document in @($module.documents | Sort-Object path)) {
            [void]$builder.Append($document.path).Append([char]0)
            [void]$builder.Append($document.sourceSha256).Append([char]0)
            foreach ($line in @($document.sequencePoints | Sort-Object)) {
                [void]$builder.Append([int]$line).Append([char]0)
            }
        }
    }
    return ([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($builder.ToString())) |
        ForEach-Object { $_.ToString('x2') }) -join ''
}

$baseline = Get-Content -LiteralPath $resolvedBaselinePath -Raw | ConvertFrom-Json
if ($baseline.schemaVersion -ne 1 -or
    $null -eq $baseline.projects -or
    @($baseline.projects.PSObject.Properties).Count -eq 0) {
    throw 'Unsupported or invalid coverage baseline for authority derivation.'
}
$commit = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
if ($commit -notmatch '^[0-9a-f]{40}$') {
    throw "Coverage authority commit is not an exact SHA-1: '$commit'."
}

$modules = [Collections.Generic.List[object]]::new()
foreach ($property in $baseline.projects.PSObject.Properties | Sort-Object Name) {
    $projectName = [string]$property.Name
    $projectRoot = Join-Path $resolvedRepositoryRoot $projectName
    if (-not (Test-Path -LiteralPath $projectRoot -PathType Container)) {
        throw "Coverage authority project directory is missing: '$projectName'."
    }
    $assemblyCandidates = @(
        Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Filter "$projectName.dll" |
            Where-Object {
                $_.FullName.Contains(
                    "${pathSeparator}bin${pathSeparator}${Configuration}${pathSeparator}",
                    [StringComparison]::Ordinal) -and
                -not $_.FullName.Contains(
                    "${pathSeparator}ref${pathSeparator}",
                    [StringComparison]::Ordinal) -and
                -not $_.FullName.Contains(
                    "${pathSeparator}refint${pathSeparator}",
                    [StringComparison]::Ordinal)
            })
    if ($assemblyCandidates.Count -ne 1) {
        throw (
            "Coverage authority requires exactly one Release assembly for " +
            "project '$projectName'; found $($assemblyCandidates.Count).")
    }
    $assemblyPath = $assemblyCandidates[0].FullName
    $pdbPath = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
    if (-not (Test-Path -LiteralPath $pdbPath -PathType Leaf)) {
        throw "Coverage authority PDB is missing for '$assemblyPath'."
    }
    $modules.Add((Get-PortablePdbModule `
        -ProjectName $projectName `
        -AssemblyPath $assemblyPath `
        -PdbPath $pdbPath))
}

$authority = [pscustomobject][ordered]@{
    schemaVersion = 1
    commit = $commit
    configuration = $Configuration
    modules = @($modules | Sort-Object project)
    universeSha256 = Get-CoverageAuthorityHash `
        -Commit $commit `
        -Modules @($modules)
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $authority | ConvertTo-Json -Depth 12
}
else {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = [IO.Path]::GetDirectoryName($fullOutputPath)
    if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
        throw "Coverage authority output has no parent: '$OutputPath'."
    }
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $json = ($authority | ConvertTo-Json -Depth 12) -replace "`r`n", "`n"
    [IO.File]::WriteAllText(
        $fullOutputPath,
        $json + "`n",
        [Text.UTF8Encoding]::new($false))
    $authority
}

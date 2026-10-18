[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [Parameter()]
    [switch]$RequirePdb,
    [Parameter()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0
$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PEMetadata.psm1') -Force
. (Join-Path $PSScriptRoot 'Resolve-SharpProofSourceDocument.ps1')
$pathSeparator = [IO.Path]::DirectorySeparatorChar
$repositoryPrefix = [IO.Path]::GetFullPath($resolvedRepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + $pathSeparator)

function Get-PropertyValue {
    param([Parameter(Mandatory = $true)] $Properties, [Parameter(Mandatory = $true)] [string]$Name)
    $property = $Properties.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    return [string]$property.Value
}

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)] [string]$Candidate, [Parameter(Mandatory = $true)] [string]$Description)
    if ([string]::IsNullOrWhiteSpace($Candidate)) { throw "Production inventory path is blank: '$Description'." }
    $normalized = $Candidate.Replace('\', '/')
    $full = if ([IO.Path]::IsPathRooted($normalized)) { [IO.Path]::GetFullPath($normalized) } else { [IO.Path]::GetFullPath((Join-Path $resolvedRepositoryRoot $normalized)) }
    if (-not $full.StartsWith($repositoryPrefix, [StringComparison]::Ordinal)) { throw "Production inventory path is foreign to the repository: '$Description' -> '$Candidate'." }
    $relative = $full.Substring($repositoryPrefix.Length).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('//') -or $relative.Split('/') -contains '.' -or $relative.Split('/') -contains '..') { throw "Production inventory path is not canonical: '$Candidate'." }
    return $relative
}

function Get-RepositoryFilePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$MissingMessage
    )

    $fullPath = Join-Path $resolvedRepositoryRoot (
        $RelativePath.Replace('/', $pathSeparator))
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw $MissingMessage
    }
    return $fullPath
}

function Get-CanonicalFileRecord {
    param([Parameter(Mandatory = $true)] [string]$RelativePath, [Parameter()] [bool]$Generated = $false, [Parameter()] [string]$GeneratedReason = '')
    Get-RepositoryFilePath `
        -RelativePath $RelativePath `
        -MissingMessage "Production inventory source file is missing: '$RelativePath'." |
        Out-Null
    return [pscustomobject][ordered]@{ path = $RelativePath; generated = $Generated; generatedReason = $GeneratedReason }
}

function Get-InventoryParallelism {
    $executionModule = Join-Path $resolvedRepositoryRoot 'eng/scripts/SharpProof.ContainerExecution.psm1'
    $contractPath = Join-Path $resolvedRepositoryRoot 'eng/acceptance/contract.json'
    if (-not (Test-Path -LiteralPath $executionModule -PathType Leaf) -or
        -not (Test-Path -LiteralPath $contractPath -PathType Leaf)) {
        return 1
    }

    $contract = Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json
    $automation = $contract.PSObject.Properties['automation']
    if ($null -eq $automation -or
        $null -eq $automation.Value -or
        $null -eq $automation.Value.PSObject.Properties[
        'productionInventoryMaxParallelism']) {
        return 1
    }

    Import-Module $executionModule -Force
    $available = Get-SharpProofTestProjectParallelism `
        -RepositoryRoot $resolvedRepositoryRoot
    $maximum = [int]$contract.automation.productionInventoryMaxParallelism
    if ($maximum -lt 1) {
        throw 'The production-inventory parallelism cap must be positive.'
    }

    return [Math]::Min($available, $maximum)
}

function Get-MsBuildQueries {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ProjectRelativePaths
    )

    $null = Get-InventoryParallelism
    $dotnetItem = Get-Item -LiteralPath (
        Get-Command dotnet -CommandType Application -ErrorAction Stop |
            Select-Object -First 1).Source
    $dotnetTarget = $dotnetItem.ResolveLinkTarget($true)
    $dotnetPath = if ($null -eq $dotnetTarget) {
        $dotnetItem.FullName
    }
    else { $dotnetTarget.FullName }
    $sdkVersionOutput = @(& $dotnetPath --version)
    if ($LASTEXITCODE -ne 0 -or $sdkVersionOutput.Count -ne 1) {
        throw 'Production inventory could not resolve the active .NET SDK.'
    }
    $sdkVersion = ([string]$sdkVersionOutput[0]).Trim()
    $pin = Get-Content -LiteralPath (Join-Path $resolvedRepositoryRoot 'global.json') -Raw |
        ConvertFrom-Json
    if ($sdkVersion -ne [string]$pin.sdk.version) {
        throw 'Production inventory requires the repository-pinned SDK.'
    }
    $dotnetRoot = [IO.Path]::GetDirectoryName($dotnetPath)
    $sdkPath = Join-Path $dotnetRoot "sdk/$sdkVersion"
    $runtimeConfigPath = Join-Path $sdkPath 'MSBuild.runtimeconfig.json'
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $framework = $runtimeConfig.runtimeOptions.framework
    $referencePath = Join-Path $dotnetRoot (
        'packs/' + $framework.name + '.Ref/' + $framework.version + '/ref/' +
        $runtimeConfig.runtimeOptions.tfm)
    $compilerPath = Join-Path $sdkPath 'Roslyn/bincore/csc.dll'
    $sourcePath = Join-Path $PSScriptRoot 'SharpProof.ProductionMsBuildHost.cs'
    foreach ($required in @($compilerPath, $sourcePath, (Join-Path $sdkPath 'MSBuild.dll'),
            (Join-Path $sdkPath 'dotnet.dll'))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Production inventory SDK host input is missing: '$required'."
        }
    }
    $references = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-ChildItem -LiteralPath $referencePath -Filter '*.dll' -File |
                Sort-Object Name | ForEach-Object { '-reference:' + $_.FullName }))
    if ($references.Length -eq 0) {
        throw 'Production inventory SDK reference pack is empty.'
    }
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $temporaryPath = Join-Path $temporaryRoot ('SharpProofInventory.' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($temporaryPath) | Out-Null
    try {
        $hostPath = Join-Path $temporaryPath 'SharpProof.ProductionMsBuildHost.dll'
        $compilerArguments = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        [void]$compilerArguments.AddRange([string[]]@($compilerPath, '-nologo', '-noconfig', '-nostdlib+',
                '-target:exe', ('-out:' + $hostPath)))
        foreach ($reference in $references) { [void]$compilerArguments.Add($reference) }
        [void]$compilerArguments.Add($sourcePath)
        [void]$compilerArguments.Add((Join-Path $PSScriptRoot '../logging/RepositoryLogger.cs'))
        $nativeCompilerArguments = @($compilerArguments.ToImmutable())
        $compilerOutput = @(& $dotnetPath @nativeCompilerArguments 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw ('Production inventory SDK host compilation failed: ' +
                ($compilerOutput -join [Environment]::NewLine))
        }
        $queries = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
        foreach ($projectRelativePath in $ProjectRelativePaths) {
            $projectPath = Join-Path $resolvedRepositoryRoot (
                $projectRelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
            $queryArguments = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    $projectPath, '-nologo', "-p:Configuration=$Configuration",
                    '-p:DesignTimeBuild=false',
                    '-getProperty:MSBuildProjectName,SharpProofProductionProject,AssemblyName,TargetFramework,TargetPath,LangVersion,DefineConstants,Nullable,ImplicitUsings,AllowUnsafeBlocks,DebugType,EnableDefaultCompileItems',
                    '-getItem:Compile,Analyzer,AdditionalFiles'))
            [void]$queries.Add([pscustomobject][ordered]@{
                    projectRelativePath = $projectRelativePath
                    projectPath         = $projectPath
                    arguments           = $queryArguments
                })
        }
        $requestPath = Join-Path $temporaryPath 'request.json'
        $responsePath = Join-Path $temporaryPath 'response.json'
        [IO.File]::WriteAllText($requestPath, ([pscustomobject]@{
                    sdkPath = $sdkPath; dotnetPath = $dotnetPath; queries = $queries.ToImmutable()
                } | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        $hostOutput = @(& $dotnetPath exec --runtimeconfig $runtimeConfigPath `
                $hostPath $requestPath $responsePath 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw ('Production inventory MSBuild evaluation process failed: ' +
                ($hostOutput -join [Environment]::NewLine))
        }
        $response = Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
        $results = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($response.queries))
        if ($results.Length -ne $ProjectRelativePaths.Count) {
            throw 'Production inventory MSBuild evaluation returned an incomplete query set.'
        }
        return @($results | Sort-Object projectRelativePath)
    }
    finally {
        [IO.Directory]::Delete($temporaryPath, $true)
    }
}

function Get-ItemPath {
    param([Parameter(Mandatory = $true)] $Item, [Parameter(Mandatory = $true)] [string]$ProjectDirectory)
    $fullPath = Get-PropertyValue -Properties $Item -Name 'FullPath'
    if ([string]::IsNullOrWhiteSpace($fullPath)) {
        $identity = Get-PropertyValue -Properties $Item -Name 'Identity'
        $fullPath = if ([IO.Path]::IsPathRooted($identity)) { $identity } else { Join-Path $ProjectDirectory $identity }
    }
    return Resolve-RepositoryPath -Candidate $fullPath -Description (Get-PropertyValue -Properties $Item -Name 'Identity')
}

function Get-GeneratorSourceRecords {
    $paths = [Collections.Immutable.ImmutableHashSet[string]]::Empty.WithComparer([StringComparer]::Ordinal).ToBuilder()
    $scriptDirectory = Join-Path $resolvedRepositoryRoot 'eng/scripts'
    foreach ($file in @(Get-ChildItem -LiteralPath $scriptDirectory -Filter 'Generate-*.ps1' -File -ErrorAction Stop)) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    foreach ($file in @(Get-ChildItem -LiteralPath $resolvedRepositoryRoot -Filter '*.catalog.json' -File -ErrorAction Stop)) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    $engDirectory = Join-Path $resolvedRepositoryRoot 'eng'
    foreach ($file in @(Get-ChildItem -LiteralPath $engDirectory -Recurse -File -ErrorAction Stop | Where-Object { $_.Name -match '(?i)(catalog|schema|generator).*\.json$' })) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    return @($paths | Sort-Object | ForEach-Object { [pscustomobject][ordered]@{ path = $_ } })
}

function Get-SolutionProjectPaths {
    $solutionPath = Join-Path $resolvedRepositoryRoot 'SharpProof.slnx'
    try {
        [xml]$solution = [IO.File]::ReadAllText($solutionPath)
    }
    catch {
        throw "SharpProof.slnx is not valid XML: $($_.Exception.Message)"
    }
    $paths = [Collections.Immutable.ImmutableHashSet[string]]::Empty.WithComparer([StringComparer]::Ordinal).ToBuilder()
    foreach ($project in @($solution.SelectNodes('/Solution//Project'))) {
        $pathAttribute = $project.Attributes['Path']
        if ($null -eq $pathAttribute -or
            [string]::IsNullOrWhiteSpace($pathAttribute.Value)) {
            throw 'SharpProof.slnx contains a project without a Path.'
        }
        if ([IO.Path]::GetExtension($pathAttribute.Value) -cne '.csproj') {
            continue
        }
        $path = Resolve-RepositoryPath `
            -Candidate $pathAttribute.Value `
            -Description $pathAttribute.Value
        if (-not $paths.Add($path)) {
            throw "SharpProof.slnx contains duplicate project path '$path'."
        }
    }
    if ($paths.Count -eq 0) { throw 'SharpProof.slnx contains no project paths.' }
    return @($paths | Sort-Object)
}

function Get-PdbDocumentPath {
    param([Parameter(Mandatory = $true)] [System.Reflection.Metadata.MetadataReader]$Reader, [Parameter(Mandatory = $true)] [System.Reflection.Metadata.DocumentHandle]$Handle)
    if ($Handle.IsNil) { throw 'Production inventory sequence point has no source document.' }
    $document = $Reader.GetDocument($Handle)
    $name = $Reader.GetString($document.Name)
    if ([string]::IsNullOrWhiteSpace($name)) { throw 'Production inventory PDB contains a blank source document.' }
    return $name
}

function Get-MetadataTypeIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.MetadataReader]$Reader,
        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.EntityHandle]$Handle
    )

    if ($Handle.Kind -eq [System.Reflection.Metadata.HandleKind]::TypeDefinition) {
        $type = $Reader.GetTypeDefinition(
            [System.Reflection.Metadata.TypeDefinitionHandle]$Handle)
    }
    elseif ($Handle.Kind -eq [System.Reflection.Metadata.HandleKind]::TypeReference) {
        $type = $Reader.GetTypeReference(
            [System.Reflection.Metadata.TypeReferenceHandle]$Handle)
    }
    else {
        return ''
    }
    return $Reader.GetString($type.Namespace) + '.' + $Reader.GetString($type.Name)
}

function Test-CompilerGeneratedMethod {
    param(
        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.MetadataReader]$Reader,
        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.MethodDefinitionHandle]$Handle
    )

    $method = $Reader.GetMethodDefinition($Handle)
    foreach ($attributeHandle in $method.GetCustomAttributes()) {
        $attribute = $Reader.GetCustomAttribute($attributeHandle)
        $constructor = $attribute.Constructor
        $attributeType = ''
        if ($constructor.Kind -eq
            [System.Reflection.Metadata.HandleKind]::MemberReference) {
            $member = $Reader.GetMemberReference(
                [System.Reflection.Metadata.MemberReferenceHandle]$constructor)
            $attributeType = Get-MetadataTypeIdentity `
                -Reader $Reader `
                -Handle $member.Parent
        }
        elseif ($constructor.Kind -eq
            [System.Reflection.Metadata.HandleKind]::MethodDefinition) {
            $attributeMethod = $Reader.GetMethodDefinition(
                [System.Reflection.Metadata.MethodDefinitionHandle]$constructor)
            $attributeType = Get-MetadataTypeIdentity `
                -Reader $Reader `
                -Handle $attributeMethod.GetDeclaringType()
        }
        if ($attributeType -ceq
            'System.Runtime.CompilerServices.CompilerGeneratedAttribute') {
            return $true
        }
    }
    return $false
}

function Get-PortablePdbModule {
    param([Parameter(Mandatory = $true)] [string]$ProjectName, [Parameter(Mandatory = $true)] [string]$AssemblyPath, [Parameter(Mandatory = $true)] [string]$PdbPath, [Parameter(Mandatory = $true)] [System.Collections.Generic.IReadOnlySet[string]]$CompilePaths, [Parameter(Mandatory = $true)] [ref]$SequencePointCount)
    if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) { throw "Production inventory assembly is missing: '$AssemblyPath'." }
    if (-not (Test-Path -LiteralPath $PdbPath -PathType Leaf)) { throw "Production inventory PDB is missing: '$PdbPath'." }
    $assemblyStream = [IO.File]::OpenRead($AssemblyPath)
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
    $metadataProvider = $null
    $pdbStream = $null
    $pdbProvider = $null
    try {
        if (-not $peReader.HasMetadata) { throw "Production inventory assembly has no metadata: '$AssemblyPath'." }
        $metadataProvider = [System.Reflection.Metadata.MetadataReaderProvider]::FromMetadataImage($peReader.GetMetadata().GetContent())
        $metadata = $metadataProvider.GetMetadataReader()
        $assembly = $metadata.GetAssemblyDefinition()
        $assemblyName = $metadata.GetString($assembly.Name)
        $mvid = Get-SharpProofMetadataModuleVersionId -Reader $metadata
        $codeViewEntries = [Collections.Immutable.ImmutableArray]::CreateRange[Reflection.PortableExecutable.DebugDirectoryEntry]([Reflection.PortableExecutable.DebugDirectoryEntry[]]@($peReader.ReadDebugDirectory() | Where-Object { $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView }))
        if ($codeViewEntries.Length -ne 1 -or -not $codeViewEntries[0].IsPortableCodeView) { throw "Production inventory assembly must contain exactly one portable CodeView entry: '$AssemblyPath'." }
        $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViewEntries[0])
        $pdbStream = [IO.File]::OpenRead($PdbPath)
        $pdbProvider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $pdb = $pdbProvider.GetMetadataReader()
        $documentStates = [Collections.Immutable.ImmutableDictionary[string, object]]::Empty.WithComparers([StringComparer]::Ordinal).ToBuilder()
        $documentPaths = [Collections.Immutable.ImmutableDictionary[System.Reflection.Metadata.DocumentHandle, string]]::Empty.ToBuilder()
        foreach ($debugHandle in $pdb.MethodDebugInformation) {
            $methodHandle = [System.Reflection.Metadata.Ecma335.MetadataTokens]::MethodDefinitionHandle(
                [System.Reflection.Metadata.Ecma335.MetadataTokens]::GetRowNumber(
                    $debugHandle))
            $isCompilerGenerated = Test-CompilerGeneratedMethod `
                -Reader $metadata `
                -Handle $methodHandle
            $debug = $pdb.GetMethodDebugInformation($debugHandle)
            foreach ($point in $debug.GetSequencePoints()) {
                if ($point.IsHidden) { continue }
                $documentHandle = $point.Document
                if ($documentHandle.IsNil) { $documentHandle = $debug.Document }
                $relativePath = $null
                if (-not $documentPaths.TryGetValue($documentHandle, [ref]$relativePath)) {
                    $sourceName = Get-PdbDocumentPath -Reader $pdb -Handle $documentHandle
                    $relativePath = Resolve-SharpProofSourceDocument `
                        -RepositoryRoot $resolvedRepositoryRoot -DocumentPath $sourceName -RequireFile $false
                    $documentPaths[$documentHandle] = $relativePath
                }
                if (-not $relativePath.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { throw "Production inventory PDB source is not C#: '$relativePath'." }
                if ($relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) { continue }
                Get-RepositoryFilePath -RelativePath $relativePath `
                    -MissingMessage "Production inventory PDB source file is missing: '$relativePath'." | Out-Null
                Assert-SharpProofSourceDocumentCompileMembership `
                    -RelativePath $relativePath -CompilePaths $CompilePaths
                if ($point.StartLine -le 0 -or $point.EndLine -lt $point.StartLine) { throw "Production inventory PDB has an invalid sequence-point range for '$relativePath'." }
                $documentState = $null
                $pdbDocument = $pdb.GetDocument($documentHandle)
                $sourceHashAlgorithm = $pdb.GetGuid($pdbDocument.HashAlgorithm).ToString('D')
                $sourceHash = [Convert]::ToHexString($pdb.GetBlobBytes($pdbDocument.Hash)).ToLowerInvariant()
                if (-not $documentStates.TryGetValue($relativePath, [ref]$documentState)) {
                    $documentState = [Collections.Immutable.ImmutableDictionary[string, object]]::Empty.WithComparers([StringComparer]::Ordinal).ToBuilder()
                    [void]$documentState.Add('SequencePoints', [Collections.Immutable.ImmutableHashSet[int]]::Empty.ToBuilder())
                    [void]$documentState.Add('SequencePointRanges', [Collections.Immutable.ImmutableDictionary[string, object]]::Empty.WithComparers([StringComparer]::Ordinal).ToBuilder())
                    [void]$documentState.Add('SourceHashAlgorithm', $sourceHashAlgorithm)
                    [void]$documentState.Add('SourceHash', $sourceHash)
                    $documentStates.Add($relativePath, $documentState)
                }
                elseif ($documentState['SourceHashAlgorithm'] -cne $sourceHashAlgorithm -or
                    $documentState['SourceHash'] -cne $sourceHash) {
                    # Conflicting PDB documents for one physical source cannot
                    # authorize declaration omission. Preserve ordinary ranges.
                    $documentState['SourceHashAlgorithm'] = ''
                    $documentState['SourceHash'] = ''
                }
                if (-not $isCompilerGenerated -and
                    $documentState['SequencePoints'].Add($point.StartLine)) {
                    $SequencePointCount.Value++
                }
                $rangeKey = ([string]$point.StartLine) + ':' + ([string]$point.EndLine)
                if (-not $documentState['SequencePointRanges'].ContainsKey($rangeKey)) {
                    $documentState['SequencePointRanges'][$rangeKey] = [pscustomobject][ordered]@{ startLine = $point.StartLine; endLine = $point.EndLine }
                }
            }
        }
        if ($SequencePointCount.Value -eq 0) { throw "Production inventory PDB has no production sequence points: '$PdbPath'." }
        $documents = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(foreach ($path in @($documentStates.Keys | Sort-Object)) {
                    $documentState = $documentStates[$path]
                    [pscustomobject][ordered]@{
                        path                = $path
                        sourceHashAlgorithm = $documentState['SourceHashAlgorithm']
                        sourceHash          = $documentState['SourceHash']
                        sequencePoints      = [Collections.Immutable.ImmutableArray]::CreateRange[int]([int[]]@($documentState['SequencePoints'].ToImmutable() | Sort-Object))
                        sequencePointRanges = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($documentState['SequencePointRanges'].ToImmutable().Values | Sort-Object startLine, endLine))
                    }
                }))
        return [pscustomobject][ordered]@{
            project         = $ProjectName
            assemblyName    = $assemblyName
            assemblyPath    = Resolve-RepositoryPath -Candidate $AssemblyPath -Description 'assembly'
            moduleMvid      = $mvid
            pdbPath         = Resolve-RepositoryPath -Candidate $PdbPath -Description 'PDB'
            pdbCodeViewGuid = $codeView.Guid.ToString('D')
            documents       = $documents
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

$commit = Invoke-SharpProofGitText `
    -RepositoryRoot $resolvedRepositoryRoot `
    -Arguments @('rev-parse', 'HEAD') `
    -FailureMessage 'Production inventory Git query failed:' `
    -MergeErrorOutput `
    -TrimOutput
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Production inventory commit is not an exact SHA-1: '$commit'." }
$generatorSourceRecords = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(Get-GeneratorSourceRecords))
$projects = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$seenProjectNames = [Collections.Immutable.ImmutableHashSet[string]]::Empty.WithComparer([StringComparer]::Ordinal).ToBuilder()
$analyzerRecords = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$additionalFileRecords = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()

foreach ($result in Get-MsBuildQueries -ProjectRelativePaths @(Get-SolutionProjectPaths)) {
    $projectRelativePath = [string]$result.projectRelativePath
    $projectPath = [string]$result.projectPath
    if ([int]$result.exitCode -ne 0) {
        throw ('Production inventory MSBuild query failed: ' + [string]$result.output)
    }
    try {
        $query = [string]$result.output | ConvertFrom-Json
    }
    catch {
        throw "Production inventory received malformed MSBuild JSON for '$projectPath': $($_.Exception.Message)"
    }
    $projectName = Get-PropertyValue -Properties $query.Properties -Name 'MSBuildProjectName'
    $production = Get-PropertyValue -Properties $query.Properties -Name 'SharpProofProductionProject'
    if ($production -ne 'true') { continue }
    if (-not $seenProjectNames.Add($projectName)) { throw "Production inventory has duplicate project '$projectName'." }
    $projectDirectory = Split-Path -Parent $projectPath
    $compilePaths = [Collections.Immutable.ImmutableHashSet[string]]::Empty.WithComparer([StringComparer]::Ordinal).ToBuilder()
    foreach ($item in @($query.Items.Compile)) {
        $path = Get-ItemPath -Item $item -ProjectDirectory $projectDirectory
        if (-not $compilePaths.Add($path)) { throw "Production inventory Compile item is duplicated: '$path'." }
    }
    if ($compilePaths.Count -eq 0) { throw "Production inventory project has no evaluated Compile items: '$projectName'." }
    $compileRecords = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(foreach ($path in @($compilePaths.ToImmutable() | Sort-Object)) {
                $fullPath = Join-Path $resolvedRepositoryRoot ($path.Replace('/', $pathSeparator))
                $source = Get-Content -LiteralPath $fullPath -Raw
                $headerGenerated = $source -match '(?im)^\s*//\s*<auto-generated(?:\s*/>|>)'
                $nameGenerated = $path -match '\.(g|generated)\.cs$'
                $generated = $headerGenerated -or $nameGenerated
                $reason = if ($generated) { 'generated-marker' } else { '' }
                Get-CanonicalFileRecord -RelativePath $path -Generated $generated -GeneratedReason $reason
            }))
    $constants = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@((Get-PropertyValue -Properties $query.Properties -Name 'DefineConstants').Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique))
    $projectRecord = [pscustomobject][ordered]@{
        name             = $projectName
        projectPath      = Resolve-RepositoryPath -Candidate $projectPath -Description 'project'
        assemblyName     = Get-PropertyValue -Properties $query.Properties -Name 'AssemblyName'
        targetFramework  = Get-PropertyValue -Properties $query.Properties -Name 'TargetFramework'
        parseOptions     = [pscustomobject][ordered]@{
            languageVersion     = Get-PropertyValue -Properties $query.Properties -Name 'LangVersion'
            preprocessorSymbols = $constants
            nullable            = Get-PropertyValue -Properties $query.Properties -Name 'Nullable'
            implicitUsings      = Get-PropertyValue -Properties $query.Properties -Name 'ImplicitUsings'
            allowUnsafeBlocks   = Get-PropertyValue -Properties $query.Properties -Name 'AllowUnsafeBlocks'
        }
        compile          = $compileRecords
        generatorOptions = [pscustomobject][ordered]@{
            debugType                 = Get-PropertyValue -Properties $query.Properties -Name 'DebugType'
            enableDefaultCompileItems = Get-PropertyValue -Properties $query.Properties -Name 'EnableDefaultCompileItems'
        }
        targetPath       = Get-PropertyValue -Properties $query.Properties -Name 'TargetPath'
    }
    [void]$projects.Add($projectRecord)
    foreach ($item in @($query.Items.Analyzer)) {
        $identity = Get-PropertyValue -Properties $item -Name 'Identity'
        $fullPath = Get-PropertyValue -Properties $item -Name 'FullPath'
        $relative = ''
        if (-not [string]::IsNullOrWhiteSpace($fullPath)) {
            $normalizedAnalyzerPath = $fullPath.Replace('\', '/')
            $resolvedAnalyzerPath = if (
                [IO.Path]::IsPathRooted($normalizedAnalyzerPath)) {
                [IO.Path]::GetFullPath($normalizedAnalyzerPath)
            }
            else {
                [IO.Path]::GetFullPath((Join-Path `
                            $resolvedRepositoryRoot $normalizedAnalyzerPath))
            }
            if ($resolvedAnalyzerPath.StartsWith(
                    $repositoryPrefix,
                    [StringComparison]::Ordinal)) {
                $relative = Resolve-RepositoryPath -Candidate $fullPath -Description ("analyzer '" + $identity + "'")
                if (-not (Test-Path -LiteralPath $resolvedAnalyzerPath -PathType Leaf)) {
                    throw "Production inventory analyzer is missing: '$relative'."
                }
            }
        }
        if ([IO.Path]::IsPathRooted($identity)) { $identity = [IO.Path]::GetFileName($identity) }
        [void]$analyzerRecords.Add([pscustomobject][ordered]@{ project = $projectName; identity = $identity; path = $relative })
    }
    foreach ($item in @($query.Items.AdditionalFiles)) {
        $path = Get-ItemPath -Item $item -ProjectDirectory $projectDirectory
        [void]$additionalFileRecords.Add([pscustomobject][ordered]@{ project = $projectName; path = $path })
    }
}
$sortedProjects = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($projects.ToImmutable() | Sort-Object name))
$generatorInputs = [pscustomobject][ordered]@{
    sourceScripts   = $generatorSourceRecords
    analyzers       = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($analyzerRecords.ToImmutable() | Sort-Object project, identity, path))
    additionalFiles = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($additionalFileRecords.ToImmutable() | Sort-Object project, path))
}
$modules = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$sequencePointCount = 0
if ($RequirePdb) {
    foreach ($project in $sortedProjects) {
        $targetPath = [string]$project.targetPath
        if ([string]::IsNullOrWhiteSpace($targetPath)) { throw "Production inventory project has no TargetPath: '$($project.name)'." }
        if (-not [IO.Path]::IsPathRooted($targetPath)) { $targetPath = Join-Path $resolvedRepositoryRoot $targetPath }
        $compilePaths = [Collections.Immutable.ImmutableHashSet[string]]::Empty.WithComparer([StringComparer]::Ordinal).ToBuilder()
        foreach ($file in @($project.compile)) { [void]$compilePaths.Add([string]$file.path) }
        [void]$modules.Add((Get-PortablePdbModule -ProjectName ([string]$project.name) -AssemblyPath $targetPath -PdbPath ([IO.Path]::ChangeExtension($targetPath, '.pdb')) -CompilePaths $compilePaths.ToImmutable() -SequencePointCount ([ref]$sequencePointCount)))
    }
}
$sortedModules = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($modules.ToImmutable() | Sort-Object project))
$authority = [pscustomobject][ordered]@{ schemaVersion = 1; commit = $commit; configuration = $Configuration; generatorInputs = $generatorInputs; projects = $sortedProjects; modules = $sortedModules; sequencePointCount = $sequencePointCount }
$json = $authority | ConvertTo-Json -Depth 30
if ([string]::IsNullOrWhiteSpace($OutputPath)) { Write-Output $json } else {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $fullOutputPath
    if ([string]::IsNullOrWhiteSpace($directory)) { throw "Production inventory output has no parent: '$OutputPath'." }
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText($fullOutputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

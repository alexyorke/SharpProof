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

function Get-CanonicalFileRecord {
    param([Parameter(Mandatory = $true)] [string]$RelativePath, [Parameter()] [bool]$Generated = $false, [Parameter()] [string]$GeneratedReason = '')
    $fullPath = Join-Path $resolvedRepositoryRoot ($RelativePath.Replace('/', $pathSeparator))
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Production inventory source file is missing: '$RelativePath'." }
    return [pscustomobject][ordered]@{ path = $RelativePath; generated = $Generated; generatedReason = $GeneratedReason }
}

function Get-InventoryParallelism {
    $executionModule = Join-Path $resolvedRepositoryRoot 'scripts/SharpProof.ContainerExecution.psm1'
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

    $parallelism = Get-InventoryParallelism
    $results = @(
        $ProjectRelativePaths |
            ForEach-Object -Parallel {
                $projectRelativePath = [string]$_
                $projectPath = Join-Path `
                    $using:resolvedRepositoryRoot `
                    ($projectRelativePath.Replace(
                        '/',
                        [IO.Path]::DirectorySeparatorChar))
                $arguments = @(
                    'msbuild',
                    $projectPath,
                    '-nologo',
                    "-p:Configuration=$using:Configuration",
                    '-p:DesignTimeBuild=false',
                    '-getProperty:MSBuildProjectName,SharpProofProductionProject,AssemblyName,TargetFramework,TargetPath,LangVersion,DefineConstants,Nullable,ImplicitUsings,AllowUnsafeBlocks,DebugType,EnableDefaultCompileItems',
                    '-getItem:Compile,Analyzer,AdditionalFiles')
                $output = @(& dotnet @arguments 2>&1)
                [pscustomobject][ordered]@{
                    projectRelativePath = $projectRelativePath
                    projectPath = $projectPath
                    exitCode = $LASTEXITCODE
                    output = ($output -join [Environment]::NewLine).Trim()
                }
            } -ThrottleLimit $parallelism)

    return @($results | Sort-Object projectRelativePath)
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

function Get-GeneratedManifest {
    $path = Join-Path $resolvedRepositoryRoot 'eng/generated/approved-outputs.v1.json'
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $null -eq $manifest.outputs) { throw 'The approved generated-output manifest is invalid.' }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in @($manifest.outputs)) {
        $item = ([string]$value).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($item) -or [IO.Path]::IsPathRooted($item) -or $item.Contains('//') -or $item.Split('/') -contains '.' -or $item.Split('/') -contains '..' -or -not $paths.Add($item)) { throw "The approved generated-output manifest contains an invalid or duplicate path: '$item'." }
        $full = Join-Path $resolvedRepositoryRoot ($item.Replace('/', $pathSeparator))
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Approved generated output is missing: '$item'." }
    }
    return [pscustomobject][ordered]@{ paths = $paths }
}

function Get-GeneratorSourceRecords {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $scriptDirectory = Join-Path $resolvedRepositoryRoot 'scripts'
    foreach ($file in @(Get-ChildItem -LiteralPath $scriptDirectory -Filter 'Generate-*.ps1' -File -ErrorAction Stop)) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    foreach ($file in @(Get-ChildItem -LiteralPath $resolvedRepositoryRoot -Filter '*.catalog.json' -File -ErrorAction Stop)) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    $engDirectory = Join-Path $resolvedRepositoryRoot 'eng'
    foreach ($file in @(Get-ChildItem -LiteralPath $engDirectory -Recurse -File -ErrorAction Stop | Where-Object { $_.Name -match '(?i)(catalog|schema|generator).*\.json$' })) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    return @($paths | Sort-Object | ForEach-Object { [pscustomobject][ordered]@{ path = $_ } })
}

function Get-SolutionProjectPaths {
    $solution = Get-Content -LiteralPath (Join-Path $resolvedRepositoryRoot 'SharpProof.sln') -Raw
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($solution, 'Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"(?<path>[^"]+\.csproj)"', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) { [void]$paths.Add($match.Groups['path'].Value.Replace('\', '/')) }
    if ($paths.Count -eq 0) { throw 'SharpProof.sln contains no project paths.' }
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
    param([Parameter(Mandatory = $true)] [string]$ProjectName, [Parameter(Mandatory = $true)] [string]$AssemblyPath, [Parameter(Mandatory = $true)] [string]$PdbPath, [Parameter(Mandatory = $true)] [System.Collections.Generic.HashSet[string]]$CompilePaths)
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
        $module = $metadata.GetModuleDefinition()
        $mvid = $metadata.GetGuid($module.Mvid).ToString('D')
        $codeViewEntries = @($peReader.ReadDebugDirectory() | Where-Object { $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView })
        if ($codeViewEntries.Count -ne 1 -or -not $codeViewEntries[0].IsPortableCodeView) { throw "Production inventory assembly must contain exactly one portable CodeView entry: '$AssemblyPath'." }
        $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViewEntries[0])
        $pdbStream = [IO.File]::OpenRead($PdbPath)
        $pdbProvider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $pdb = $pdbProvider.GetMetadataReader()
        $sourceLines = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[int]]]::new([StringComparer]::Ordinal)
        $sourceRanges = [Collections.Generic.Dictionary[string, Collections.Generic.Dictionary[string, object]]]::new([StringComparer]::Ordinal)
        $documentPaths = [Collections.Generic.Dictionary[System.Reflection.Metadata.DocumentHandle, string]]::new()
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
                $sourceName = $null
                if (-not $documentPaths.TryGetValue($documentHandle, [ref]$sourceName)) {
                    $sourceName = Get-PdbDocumentPath -Reader $pdb -Handle $documentHandle
                    $documentPaths[$documentHandle] = $sourceName
                }
                $relativePath = Resolve-RepositoryPath -Candidate $sourceName -Description ($AssemblyPath + ':' + $point.StartLine)
                if (-not $relativePath.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { throw "Production inventory PDB source is not C#: '$relativePath'." }
                if ($relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) { continue }
                if (-not $CompilePaths.Contains($relativePath)) { throw "Production inventory PDB source is not an evaluated Compile item: '$relativePath'." }
                if ($point.StartLine -le 0 -or $point.EndLine -lt $point.StartLine) { throw "Production inventory PDB has an invalid sequence-point range for '$relativePath'." }
                if (-not $isCompilerGenerated) {
                    if (-not $sourceLines.ContainsKey($relativePath)) { $sourceLines[$relativePath] = [Collections.Generic.HashSet[int]]::new() }
                    [void]$sourceLines[$relativePath].Add($point.StartLine)
                }
                if (-not $sourceRanges.ContainsKey($relativePath)) { $sourceRanges[$relativePath] = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal) }
                $rangeKey = ([string]$point.StartLine) + ':' + ([string]$point.EndLine)
                if (-not $sourceRanges[$relativePath].ContainsKey($rangeKey)) {
                    $sourceRanges[$relativePath][$rangeKey] = [pscustomobject][ordered]@{ startLine = $point.StartLine; endLine = $point.EndLine }
                }
            }
        }
        if ($sourceLines.Count -eq 0) { throw "Production inventory PDB has no production sequence points: '$PdbPath'." }
        $documents = foreach ($path in @($sourceRanges.Keys | Sort-Object)) {
            $documentSequencePoints = if ($sourceLines.ContainsKey($path)) {
                @($sourceLines[$path] | Sort-Object)
            }
            else {
                @()
            }
            [pscustomobject][ordered]@{
                path = $path
                sequencePoints = @($documentSequencePoints)
                sequencePointRanges = @($sourceRanges[$path].Values | Sort-Object startLine, endLine)
            }
        }
        return [pscustomobject][ordered]@{
            project = $ProjectName
            assemblyName = $assemblyName
            assemblyPath = Resolve-RepositoryPath -Candidate $AssemblyPath -Description 'assembly'
            moduleMvid = $mvid
            pdbPath = Resolve-RepositoryPath -Candidate $PdbPath -Description 'PDB'
            pdbCodeViewGuid = $codeView.Guid.ToString('D')
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

$commit = Invoke-SharpProofGitText `
    -RepositoryRoot $resolvedRepositoryRoot `
    -Arguments @('rev-parse', 'HEAD') `
    -FailureMessage 'Production inventory Git query failed:' `
    -MergeErrorOutput `
    -TrimOutput
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Production inventory commit is not an exact SHA-1: '$commit'." }
$manifest = Get-GeneratedManifest
$generatorSourceRecords = Get-GeneratorSourceRecords
$projects = [Collections.Generic.List[object]]::new()
$compileUnion = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenProjectNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$analyzerRecords = [Collections.Generic.List[object]]::new()
$additionalFileRecords = [Collections.Generic.List[object]]::new()

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
    $compilePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in @($query.Items.Compile)) {
        $path = Get-ItemPath -Item $item -ProjectDirectory $projectDirectory
        if (-not $compilePaths.Add($path)) { throw "Production inventory Compile item is duplicated: '$path'." }
        [void]$compileUnion.Add($path)
    }
    if ($compilePaths.Count -eq 0) { throw "Production inventory project has no evaluated Compile items: '$projectName'." }
    $compileRecords = foreach ($path in @($compilePaths | Sort-Object)) {
        $fullPath = Join-Path $resolvedRepositoryRoot ($path.Replace('/', $pathSeparator))
        $source = Get-Content -LiteralPath $fullPath -Raw
        $manifestGenerated = $manifest.paths.Contains($path)
        $headerGenerated = $source -match '(?im)^\s*//\s*<auto-generated(?:\s*/>|>)'
        $nameGenerated = $path -match '\.(g|generated)\.cs$'
        $generated = $manifestGenerated -or $headerGenerated -or $nameGenerated
        if ($generated -and -not $manifestGenerated) { throw "Evaluated generated Compile item is not approved by the generated-output manifest: '$path'." }
        $reason = if ($manifestGenerated) { 'approved-manifest' } elseif ($headerGenerated) { 'auto-generated-header' } elseif ($nameGenerated) { 'generated-name' } else { '' }
        Get-CanonicalFileRecord -RelativePath $path -Generated $generated -GeneratedReason $reason
    }
    $constants = @((Get-PropertyValue -Properties $query.Properties -Name 'DefineConstants').Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $projectRecord = [pscustomobject][ordered]@{
        name = $projectName
        projectPath = Resolve-RepositoryPath -Candidate $projectPath -Description 'project'
        assemblyName = Get-PropertyValue -Properties $query.Properties -Name 'AssemblyName'
        targetFramework = Get-PropertyValue -Properties $query.Properties -Name 'TargetFramework'
        parseOptions = [pscustomobject][ordered]@{
            languageVersion = Get-PropertyValue -Properties $query.Properties -Name 'LangVersion'
            preprocessorSymbols = @($constants)
            nullable = Get-PropertyValue -Properties $query.Properties -Name 'Nullable'
            implicitUsings = Get-PropertyValue -Properties $query.Properties -Name 'ImplicitUsings'
            allowUnsafeBlocks = Get-PropertyValue -Properties $query.Properties -Name 'AllowUnsafeBlocks'
        }
        compile = @($compileRecords)
        generatorOptions = [pscustomobject][ordered]@{
            debugType = Get-PropertyValue -Properties $query.Properties -Name 'DebugType'
            enableDefaultCompileItems = Get-PropertyValue -Properties $query.Properties -Name 'EnableDefaultCompileItems'
        }
        targetPath = Get-PropertyValue -Properties $query.Properties -Name 'TargetPath'
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
foreach ($path in @($manifest.paths)) {
    if (-not $compileUnion.Contains($path)) { throw "Approved generated output is not an evaluated Compile item: '$path'." }
}
$sortedProjects = @($projects | Sort-Object name)
$generatorInputs = [pscustomobject][ordered]@{
    sourceScripts = @($generatorSourceRecords)
    analyzers = @($analyzerRecords | Sort-Object project, identity, path)
    additionalFiles = @($additionalFileRecords | Sort-Object project, path)
}
$modules = [Collections.Generic.List[object]]::new()
if ($RequirePdb) {
    foreach ($project in $sortedProjects) {
        $targetPath = [string]$project.targetPath
        if ([string]::IsNullOrWhiteSpace($targetPath)) { throw "Production inventory project has no TargetPath: '$($project.name)'." }
        if (-not [IO.Path]::IsPathRooted($targetPath)) { $targetPath = Join-Path $resolvedRepositoryRoot $targetPath }
        $compilePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in @($project.compile)) { [void]$compilePaths.Add([string]$file.path) }
        [void]$modules.Add((Get-PortablePdbModule -ProjectName ([string]$project.name) -AssemblyPath $targetPath -PdbPath ([IO.Path]::ChangeExtension($targetPath, '.pdb')) -CompilePaths $compilePaths))
    }
}
$sortedModules = @($modules | Sort-Object project)
$sequencePointCount = 0
foreach ($module in $sortedModules) { foreach ($document in @($module.documents)) { $sequencePointCount += @($document.sequencePoints).Count } }
$authority = [pscustomobject][ordered]@{ schemaVersion = 1; commit = $commit; configuration = $Configuration; generatorInputs = $generatorInputs; projects = $sortedProjects; modules = $sortedModules; sequencePointCount = $sequencePointCount }
$json = $authority | ConvertTo-Json -Depth 30
if ([string]::IsNullOrWhiteSpace($OutputPath)) { Write-Output $json } else {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $fullOutputPath
    if ([string]::IsNullOrWhiteSpace($directory)) { throw "Production inventory output has no parent: '$OutputPath'." }
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText($fullOutputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

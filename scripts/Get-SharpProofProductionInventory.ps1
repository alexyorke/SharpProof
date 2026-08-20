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
$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
$pathSeparator = [IO.Path]::DirectorySeparatorChar
$repositoryPrefix = [IO.Path]::GetFullPath($resolvedRepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + $pathSeparator)

function Get-PropertyValue {
    param([Parameter(Mandatory = $true)] $Properties, [Parameter(Mandatory = $true)] [string]$Name)
    $property = $Properties.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    return [string]$property.Value
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)] [string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Production inventory input is missing: '$Path'." }
    return ([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)] [string[]]$Arguments)
    $output = @(& git -C $resolvedRepositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ('Production inventory Git query failed: ' + ($output -join [Environment]::NewLine)) }
    return ($output -join [Environment]::NewLine).Trim()
}

function Invoke-DotNetText {
    param([Parameter(Mandatory = $true)] [string[]]$Arguments)
    $output = @(& dotnet @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ('Production inventory MSBuild query failed: ' + ($output -join [Environment]::NewLine)) }
    return ($output -join [Environment]::NewLine).Trim()
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
    return [pscustomobject][ordered]@{ path = $RelativePath; sha256 = Get-Sha256Hex -Path $fullPath; generated = $Generated; generatedReason = $GeneratedReason }
}

function Get-MsBuildQuery {
    param([Parameter(Mandatory = $true)] [string]$ProjectPath)
    $json = Invoke-DotNetText -Arguments @('msbuild', $ProjectPath, '-nologo', "-p:Configuration=$Configuration", '-p:DesignTimeBuild=false', '-getProperty:MSBuildProjectName,SharpProofProductionProject,AssemblyName,TargetFramework,TargetPath,LangVersion,DefineConstants,Nullable,ImplicitUsings,AllowUnsafeBlocks,DebugType,EnableDefaultCompileItems', '-getItem:Compile,Analyzer,AdditionalFiles')
    try { return $json | ConvertFrom-Json } catch { throw "Production inventory received malformed MSBuild JSON for '$ProjectPath': $($_.Exception.Message)" }
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
    return [pscustomobject][ordered]@{ paths = $paths; sha256 = Get-Sha256Hex -Path $path }
}

function Get-GeneratorSourceRecords {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $scriptDirectory = Join-Path $resolvedRepositoryRoot 'scripts'
    foreach ($file in @(Get-ChildItem -LiteralPath $scriptDirectory -Filter 'Generate-*.ps1' -File -ErrorAction Stop)) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    foreach ($file in @(Get-ChildItem -LiteralPath $resolvedRepositoryRoot -Filter '*.catalog.json' -File -ErrorAction Stop)) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    $engDirectory = Join-Path $resolvedRepositoryRoot 'eng'
    foreach ($file in @(Get-ChildItem -LiteralPath $engDirectory -Recurse -File -ErrorAction Stop | Where-Object { $_.Name -match '(?i)(catalog|schema|generator).*\.json$' })) { [void]$paths.Add((Resolve-RepositoryPath -Candidate $file.FullName -Description $file.Name)) }
    return @($paths | Sort-Object | ForEach-Object { [pscustomobject][ordered]@{ path = $_; sha256 = Get-Sha256Hex -Path (Join-Path $resolvedRepositoryRoot ($_.Replace('/', $pathSeparator))) } })
}

function Get-SolutionProjectPaths {
    $solution = Get-Content -LiteralPath (Join-Path $resolvedRepositoryRoot 'SharpProof.sln') -Raw
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($solution, 'Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"(?<path>[^"]+\.csproj)"', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) { [void]$paths.Add($match.Groups['path'].Value.Replace('\', '/')) }
    if ($paths.Count -eq 0) { throw 'SharpProof.sln contains no project paths.' }
    return @($paths | Sort-Object)
}

function Get-CoverageExtraProjectNames {
    [xml]$settings = Get-Content -LiteralPath (Join-Path $resolvedRepositoryRoot 'eng/coverage/SharpProof.Gates.runsettings') -Raw
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($modulePath in @($settings.SelectNodes('//ModulePath') | ForEach-Object { [string]$_.InnerText })) {
        $match = [regex]::Match($modulePath, 'SharpProof\.(?<name>[A-Za-z0-9.]+)\.dll', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ($match.Success) { [void]$names.Add('SharpProof.' + $match.Groups['name'].Value) }
    }
    return @($names | Sort-Object)
}

function Get-PdbDocumentPath {
    param([Parameter(Mandatory = $true)] [System.Reflection.Metadata.MetadataReader]$Reader, [Parameter(Mandatory = $true)] [System.Reflection.Metadata.DocumentHandle]$Handle)
    if ($Handle.IsNil) { throw 'Production inventory sequence point has no source document.' }
    $document = $Reader.GetDocument($Handle)
    $name = $Reader.GetString($document.Name)
    if ([string]::IsNullOrWhiteSpace($name)) { throw 'Production inventory PDB contains a blank source document.' }
    return $name
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
        foreach ($debugHandle in $pdb.MethodDebugInformation) {
            $debug = $pdb.GetMethodDebugInformation($debugHandle)
            foreach ($point in $debug.GetSequencePoints()) {
                if ($point.IsHidden) { continue }
                $documentHandle = $point.Document
                if ($documentHandle.IsNil) { $documentHandle = $debug.Document }
                $sourceName = Get-PdbDocumentPath -Reader $pdb -Handle $documentHandle
                $relativePath = Resolve-RepositoryPath -Candidate $sourceName -Description ($AssemblyPath + ':' + $point.StartLine)
                if (-not $relativePath.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) { throw "Production inventory PDB source is not C#: '$relativePath'." }
                if ($relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) { continue }
                if (-not $CompilePaths.Contains($relativePath)) { throw "Production inventory PDB source is not an evaluated Compile item: '$relativePath'." }
                if ($point.StartLine -le 0 -or $point.EndLine -lt $point.StartLine) { throw "Production inventory PDB has an invalid sequence-point range for '$relativePath'." }
                if (-not $sourceLines.ContainsKey($relativePath)) { $sourceLines[$relativePath] = [Collections.Generic.HashSet[int]]::new() }
                [void]$sourceLines[$relativePath].Add($point.StartLine)
                if (-not $sourceRanges.ContainsKey($relativePath)) { $sourceRanges[$relativePath] = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal) }
                $rangeKey = ([string]$point.StartLine) + ':' + ([string]$point.EndLine)
                if (-not $sourceRanges[$relativePath].ContainsKey($rangeKey)) {
                    $sourceRanges[$relativePath][$rangeKey] = [pscustomobject][ordered]@{ startLine = $point.StartLine; endLine = $point.EndLine }
                }
            }
        }
        if ($sourceLines.Count -eq 0) { throw "Production inventory PDB has no production sequence points: '$PdbPath'." }
        $documents = foreach ($path in @($sourceLines.Keys | Sort-Object)) {
            [pscustomobject][ordered]@{
                path = $path
                sourceSha256 = Get-Sha256Hex -Path (Join-Path $resolvedRepositoryRoot ($path.Replace('/', $pathSeparator)))
                sequencePoints = @($sourceLines[$path] | Sort-Object)
                sequencePointRanges = @($sourceRanges[$path].Values | Sort-Object startLine, endLine)
            }
        }
        return [pscustomobject][ordered]@{
            project = $ProjectName
            assemblyName = $assemblyName
            assemblyPath = Resolve-RepositoryPath -Candidate $AssemblyPath -Description 'assembly'
            assemblySha256 = Get-Sha256Hex -Path $AssemblyPath
            moduleMvid = $mvid
            pdbPath = Resolve-RepositoryPath -Candidate $PdbPath -Description 'PDB'
            pdbSha256 = Get-Sha256Hex -Path $PdbPath
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

function Get-HashForObject {
    param([Parameter(Mandatory = $true)] [string]$Domain, [Parameter(Mandatory = $true)] $Value)
    $json = $Value | ConvertTo-Json -Depth 30 -Compress
    return ([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Domain + [char]0 + $json)) | ForEach-Object { $_.ToString('x2') }) -join ''
}

$commit = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Production inventory commit is not an exact SHA-1: '$commit'." }
$manifest = Get-GeneratedManifest
$generatorSourceRecords = Get-GeneratorSourceRecords
$extraCoverageProjects = Get-CoverageExtraProjectNames
$projects = [Collections.Generic.List[object]]::new()
$compileUnion = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenProjectNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$analyzerRecords = [Collections.Generic.List[object]]::new()
$additionalFileRecords = [Collections.Generic.List[object]]::new()

foreach ($projectRelativePath in Get-SolutionProjectPaths) {
    $projectPath = Join-Path $resolvedRepositoryRoot ($projectRelativePath.Replace('/', $pathSeparator))
    $query = Get-MsBuildQuery -ProjectPath $projectPath
    $projectName = Get-PropertyValue -Properties $query.Properties -Name 'MSBuildProjectName'
    $production = Get-PropertyValue -Properties $query.Properties -Name 'SharpProofProductionProject'
    if ($production -ne 'true' -and $projectName -notin $extraCoverageProjects) { continue }
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
        projectSha256 = Get-Sha256Hex -Path $projectPath
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
        $sha = ''
        if (-not [string]::IsNullOrWhiteSpace($fullPath)) {
            try {
                $relative = Resolve-RepositoryPath -Candidate $fullPath -Description ("analyzer '" + $identity + "'")
                $sha = Get-Sha256Hex -Path (Join-Path $resolvedRepositoryRoot ($relative.Replace('/', $pathSeparator)))
            }
            catch { $relative = '' }
        }
        if ([IO.Path]::IsPathRooted($identity)) { $identity = [IO.Path]::GetFileName($identity) }
        [void]$analyzerRecords.Add([pscustomobject][ordered]@{ project = $projectName; identity = $identity; path = $relative; sha256 = $sha })
    }
    foreach ($item in @($query.Items.AdditionalFiles)) {
        $path = Get-ItemPath -Item $item -ProjectDirectory $projectDirectory
        [void]$additionalFileRecords.Add([pscustomobject][ordered]@{ project = $projectName; path = $path; sha256 = Get-Sha256Hex -Path (Join-Path $resolvedRepositoryRoot ($path.Replace('/', $pathSeparator))) })
    }
}
foreach ($projectName in $extraCoverageProjects) {
    if (-not $seenProjectNames.Contains($projectName)) { throw "Coverage runsettings names an absent project: '$projectName'." }
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
$sourcePayload = [pscustomobject][ordered]@{ schemaVersion = 1; commit = $commit; configuration = $Configuration; generatedManifestSha256 = $manifest.sha256; generatorInputs = $generatorInputs; projects = $sortedProjects }
$sourceUniverseSha256 = Get-HashForObject -Domain 'SharpProof.production-inventory.source.v1' -Value $sourcePayload
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
$pdbPayload = [pscustomobject][ordered]@{ schemaVersion = 1; commit = $commit; configuration = $Configuration; sourceUniverseSha256 = $sourceUniverseSha256; modules = $sortedModules }
$pdbUniverseSha256 = if ($RequirePdb) { Get-HashForObject -Domain 'SharpProof.production-inventory.pdb.v1' -Value $pdbPayload } else { '' }
$sequencePointCount = 0
foreach ($module in $sortedModules) { foreach ($document in @($module.documents)) { $sequencePointCount += @($document.sequencePoints).Count } }
$authority = [pscustomobject][ordered]@{ schemaVersion = 1; commit = $commit; configuration = $Configuration; sourceUniverseSha256 = $sourceUniverseSha256; pdbUniverseSha256 = $pdbUniverseSha256; generatedManifestSha256 = $manifest.sha256; generatorInputs = $generatorInputs; projects = $sortedProjects; modules = $sortedModules; sequencePointCount = $sequencePointCount }
$json = $authority | ConvertTo-Json -Depth 30
if ([string]::IsNullOrWhiteSpace($OutputPath)) { Write-Output $json } else {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $fullOutputPath
    if ([string]::IsNullOrWhiteSpace($directory)) { throw "Production inventory output has no parent: '$OutputPath'." }
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText($fullOutputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

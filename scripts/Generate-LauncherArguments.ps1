[CmdletBinding()]
param(
    [Parameter()][string]$CatalogPath,
    [Parameter()][string]$OutputPath,
    [Parameter()][string]$BuildTasksOutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'SharpProof.Worker.Launcher\LauncherArguments.catalog.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Worker.Launcher\LauncherArguments.generated.cs')
$BuildTasksOutputPath = Resolve-SharpProofPath $BuildTasksOutputPath (
    Join-Path $repositoryRoot 'SharpProof.BuildTasks\LauncherRuntimeCompanionInventory.generated.cs')

function Assert-Choice(
    [object]$Value,
    [string[]]$Allowed,
    [string]$Context) {
    if ($Value -isnot [string] -or [string]$Value -notin $Allowed) {
        throw "$Context must be one of: $($Allowed -join ', ')."
    }
    return [string]$Value
}

$catalogJson = Get-Content -LiteralPath $CatalogPath -Raw
$document = [System.Text.Json.JsonDocument]::Parse($catalogJson)
try {
    Assert-UniqueJsonProperties $document.RootElement `
        'launcher argument catalog'
}
finally {
    $document.Dispose()
}
$catalog = $catalogJson | ConvertFrom-Json
Assert-Properties $catalog @(
    'schemaVersion', 'runtimeCompanionExtensions', 'runtimeCompanionFiles',
    'runtimeCompanionAssemblyTypes', 'options', 'budgets', 'cache') `
    'launcher argument catalog'
if ($catalog.schemaVersion -ne 1) {
    throw 'Launcher argument catalog schemaVersion must be 1.'
}
$runtimeCompanionExtensions = @($catalog.runtimeCompanionExtensions)
$expectedRuntimeCompanionExtensions = @(
    '.deps.json', '.runtimeconfig.json')
if (($runtimeCompanionExtensions -join '|') -ne
    ($expectedRuntimeCompanionExtensions -join '|')) {
    throw 'Launcher runtime companion extensions are invalid.'
}
$runtimeCompanionFiles = @($catalog.runtimeCompanionFiles)
$runtimeCompanionFileNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($file in $runtimeCompanionFiles) {
    if ($file -isnot [string] -or
        [string]::IsNullOrWhiteSpace($file) -or
        [IO.Path]::GetFileName([string]$file) -cne [string]$file -or
        -not $runtimeCompanionFileNames.Add([string]$file)) {
        throw "Launcher runtime companion file is invalid or duplicated: '$file'."
    }
}
if ($runtimeCompanionFiles.Count -eq 0) {
    throw 'Launcher runtime companion files are incomplete.'
}
$runtimeCompanionAssemblyTypes = $catalog.runtimeCompanionAssemblyTypes
foreach ($property in $runtimeCompanionAssemblyTypes.PSObject.Properties) {
    if ($property.Name -notin $runtimeCompanionFiles -or
        $property.Value -isnot [string] -or
        [string]$property.Value -cnotmatch
            '\A[A-Za-z_][A-Za-z0-9_.]*\z') {
        throw "Launcher runtime companion assembly type is invalid: '$($property.Name)'."
    }
}

$optionKeys = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$propertyNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($option in @($catalog.options)) {
    Assert-Properties $option `
        @('key', 'category', 'accessor', 'property', 'fallback') `
        'launcher option'
    if ($option.key -isnot [string] -or
        [string]$option.key -cnotmatch '\A[a-z][a-z0-9-]*\z' -or
        -not $optionKeys.Add([string]$option.key)) {
        throw "Launcher option key is invalid or duplicated: '$($option.key)'."
    }
    $category = Assert-Choice $option.category `
        @('required', 'publication', 'optional') `
        "launcher option '$($option.key)' category"
    $accessor = Assert-Choice $option.accessor `
        @('none', 'fullPath', 'optionalFullPath', 'integer') `
        "launcher option '$($option.key)' accessor"
    if ($category -eq 'publication' -and $accessor -ne 'optionalFullPath') {
        throw "Publication option '$($option.key)' must project an optional path."
    }
    if ($accessor -eq 'none') {
        if ($option.property -ne '' -or $option.fallback -ne '') {
            throw "Non-projecting option '$($option.key)' has projection metadata."
        }
        continue
    }
    if ($option.property -isnot [string] -or
        [string]$option.property -cnotmatch '\A[A-Z][A-Za-z0-9]*\z' -or
        -not $propertyNames.Add([string]$option.property)) {
        throw "Launcher property is invalid or duplicated: '$($option.property)'."
    }
    if ($accessor -eq 'integer') {
        if ($option.fallback -ne 'terminationGraceMilliseconds') {
            throw "Integer option '$($option.key)' has an unknown fallback."
        }
    }
    elseif ($option.fallback -ne '') {
        throw "Path option '$($option.key)' cannot have a fallback."
    }
}

$budgetDefaults = @{
    QueryRlimit = 'WorkerBudgets.DefaultQueryRlimit'
    MethodRlimit = 'WorkerBudgets.DefaultMethodRlimit'
    MethodWallTimeMilliseconds = `
        'WorkerBudgets.DefaultMethodWallTimeMilliseconds'
    ProjectWallTimeMilliseconds = `
        'WorkerBudgets.DefaultProjectWallTimeMilliseconds'
    MaxParallelism = 'WorkerBudgets.MaximumParallelism'
    MaximumExpressionDepth = `
        'WorkerBudgets.DefaultMaximumExpressionDepth'
}
$budgetFallbacks = @{
    QueryRlimit = 'queryRlimit'
    MethodRlimit = 'methodRlimit'
    MethodWallTimeMilliseconds = 'methodWallTimeMilliseconds'
    ProjectWallTimeMilliseconds = 'projectWallTimeMilliseconds'
    MaxParallelism = 'maximumParallelism'
    MaximumExpressionDepth = 'maximumExpressionDepth'
}
$seenBudgets = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($budget in @($catalog.budgets)) {
    Assert-Properties $budget @('property', 'key', 'fallback') `
        'launcher budget projection'
    $property = [string]$budget.property
    if (-not $budgetDefaults.ContainsKey($property) -or
        -not $seenBudgets.Add($property) -or
        $budget.fallback -ne $budgetFallbacks[$property] -or
        -not $optionKeys.Contains([string]$budget.key)) {
        throw "Launcher budget projection '$property' is invalid."
    }
}
if ($seenBudgets.Count -ne $budgetDefaults.Count) {
    throw 'Launcher budget projections are incomplete.'
}

$cacheDefaults = @{
    Enabled = 'true'
    Directory = ''
    MaximumBytes = 'WorkerCacheOptions.DefaultMaximumBytes'
}
$cacheKinds = @{
    Enabled = 'boolean'
    Directory = 'optional'
    MaximumBytes = 'integer'
}
$cacheFallbacks = @{
    Enabled = 'cacheEnabled'
    Directory = 'none'
    MaximumBytes = 'cacheMaximumBytes'
}
$seenCache = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($entry in @($catalog.cache)) {
    Assert-Properties $entry @('property', 'key', 'projection', 'fallback') `
        'launcher cache projection'
    $property = [string]$entry.property
    if (-not $cacheDefaults.ContainsKey($property) -or
        -not $seenCache.Add($property) -or
        $entry.projection -ne $cacheKinds[$property] -or
        $entry.fallback -ne $cacheFallbacks[$property] -or
        -not $optionKeys.Contains([string]$entry.key)) {
        throw "Launcher cache projection '$property' is invalid."
    }
}
if ($seenCache.Count -ne $cacheDefaults.Count) {
    throw 'Launcher cache projections are incomplete.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-LauncherArguments.ps1' `
    -Source 'SharpProof.Worker.Launcher/LauncherArguments.catalog.json.' `
    -Notes @(
        'Declarative option inventories and request projections only.',
        'Parsing, validation, manifest I/O, and hashing remain handwritten.') `
    -Nullable
$lines.Add('')
$lines.Add('using SharpProof.Worker.Protocol;')
$lines.Add('')
$lines.Add('namespace SharpProof.Worker.Launcher;')
$lines.Add('')
$lines.Add('internal sealed partial class LauncherArguments')
$lines.Add('{')
$required = @($catalog.options | Where-Object category -eq 'required')
$publication = @($catalog.options | Where-Object category -eq 'publication')
$lines.Add('    private static readonly string[] s_required = [')
foreach ($entry in $required) {
    $lines.Add("        $(ConvertTo-CSharpString $entry.key),")
}
$lines.Add('    ];')
$lines.Add('    private static readonly string[] s_publication = [')
foreach ($entry in $publication) {
    $lines.Add("        $(ConvertTo-CSharpString $entry.key),")
}
$lines.Add('    ];')
$lines.Add('    private static readonly HashSet<string> s_allowed = [')
foreach ($entry in @($catalog.options)) {
    $lines.Add("        $(ConvertTo-CSharpString $entry.key),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    private static readonly System.Lazy<string[]> s_launcherRuntimePaths = new(')
$lines.Add('        static () =>')
$lines.Add('        {')
$lines.Add('            var path = typeof(LauncherArguments).Assembly.Location;')
$lines.Add('            var directory = System.IO.Path.GetDirectoryName(path)!;')
$lines.Add('            return [')
$lines.Add('                path,')
foreach ($extension in $runtimeCompanionExtensions) {
    $lines.Add(
        "                System.IO.Path.ChangeExtension(path, " +
        "$(ConvertTo-CSharpString $extension)),")
}
foreach ($file in $runtimeCompanionFiles) {
    $assemblyProperty = $runtimeCompanionAssemblyTypes.PSObject.Properties[
        $file]
    $assemblyType = if ($null -ne $assemblyProperty) {
        $assemblyProperty.Value
    }
    else {
        $null
    }
    $fileExpression = if ($null -ne $assemblyType) {
        "System.IO.Path.GetFileName(typeof($assemblyType).Assembly.Location)"
    }
    else {
        ConvertTo-CSharpString $file
    }
    $lines.Add(
        "                System.IO.Path.Combine(directory, " +
        "$fileExpression),")
}
$lines[$lines.Count - 1] = $lines[$lines.Count - 1].TrimEnd(',')
$lines.Add('            ];')
$lines.Add('        });')
$lines.Add('')
$lines.Add('    internal static System.Collections.Generic.IReadOnlyList<string> LauncherRuntimePaths =>')
$lines.Add('        s_launcherRuntimePaths.Value;')
$lines.Add('')
foreach ($entry in @($catalog.options | Where-Object accessor -ne 'none')) {
    $key = ConvertTo-CSharpString $entry.key
    $declaration = switch ($entry.accessor) {
        'fullPath' { "internal string $($entry.property) => FullPath($key);" }
        'optionalFullPath' {
            "internal string? $($entry.property) => OptionalFullPath($key);"
        }
        'integer' {
            "internal int $($entry.property) => Number($key, " +
                'WorkerLauncherDefaults.TerminationGraceMilliseconds);'
        }
    }
    $lines.Add("    $declaration")
}
$lines.Add('')
$lines.Add('    internal WorkerVerifyRequest ProjectRequest(')
$lines.Add('        WorkerFileReference compilerManifest)')
$lines.Add('    {')
$lines.Add('        return new()')
$lines.Add('        {')
$lines.Add('            CompilerManifest = compilerManifest,')
$lines.Add('            VerifyPolicy = LauncherPresentation.ParseVerifyPolicy(')
$lines.Add('                Required("verify-policy")),')
$lines.Add('            AssumptionPolicy = LauncherPresentation.ParseAssumptionPolicy(')
$lines.Add('                Required("assumption-policy")),')
$lines.Add('            Budgets = CreateBudgets(),')
$lines.Add('            Cache = CreateCache()')
$lines.Add('        };')
$lines.Add('    }')
$lines.Add('')
$lines.Add('    private WorkerBudgets CreateBudgets()')
$lines.Add('    {')
$lines.Add('        return new()')
$lines.Add('        {')
foreach ($entry in @($catalog.budgets)) {
    $lines.Add("            $($entry.property) = Number(" +
        "$(ConvertTo-CSharpString $entry.key), " +
        "$($budgetDefaults[[string]$entry.property])),")
}
$lines[$lines.Count - 1] = $lines[$lines.Count - 1].TrimEnd(',')
$lines.Add('        };')
$lines.Add('    }')
$lines.Add('')
$lines.Add('    private WorkerCacheOptions CreateCache()')
$lines.Add('    {')
$lines.Add('        return new()')
$lines.Add('        {')
foreach ($entry in @($catalog.cache)) {
    $key = ConvertTo-CSharpString $entry.key
    $value = switch ($entry.projection) {
        'boolean' { "Boolean($key, true)" }
        'optional' { "Optional($key)" }
        'integer' {
            "Number($key, WorkerCacheOptions.DefaultMaximumBytes)"
        }
    }
    $lines.Add("            $($entry.property) = $value,")
}
$lines[$lines.Count - 1] = $lines[$lines.Count - 1].TrimEnd(',')
$lines.Add('        };')
$lines.Add('    }')
$lines.Add('}')

$buildTaskLines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-LauncherArguments.ps1' `
    -Source 'SharpProof.Worker.Launcher/LauncherArguments.catalog.json.' `
    -Nullable
$buildTaskLines.Add('')
$buildTaskLines.Add('namespace SharpProof.BuildTasks;')
$buildTaskLines.Add('')
$buildTaskLines.Add('internal static class LauncherRuntimeCompanionInventory')
$buildTaskLines.Add('{')
$buildTaskLines.Add('    internal static string[] FileNames { get; } = [')
foreach ($file in $runtimeCompanionFiles) {
    $buildTaskLines.Add("        $(ConvertTo-CSharpString $file),")
}
$buildTaskLines.Add('    ];')
$buildTaskLines.Add('}')

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath $OutputPath `
    -GeneratorCommand '.\scripts\Generate-LauncherArguments.ps1' `
    -Verify:$Verify
Update-SharpProofGeneratedFile `
    -Path $BuildTasksOutputPath `
    -Content ($buildTaskLines -join "`n") `
    -DisplayPath $BuildTasksOutputPath `
    -GeneratorCommand '.\scripts\Generate-LauncherArguments.ps1' `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic launcher argument projections."

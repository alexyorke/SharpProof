[CmdletBinding()]
param(
    [Parameter()][string]$CatalogPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'eng\diagnostics\diagnostic-descriptors.v1.json')
if (-not [IO.File]::Exists($CatalogPath)) {
    throw "Diagnostic catalog not found: $CatalogPath"
}

function Assert-ExactMembers {
    param(
        [object]$Object,
        [string[]]$Names,
        [string]$Context
    )

    $actual = @($Object.PSObject.Properties.Name | Sort-Object)
    $expected = @($Names | Sort-Object)
    if (($actual -join '|') -ne ($expected -join '|')) {
        throw "$Context must define exactly: $($Names -join ', ')."
    }
}

function Resolve-RepositoryOutputPath {
    param([string]$RelativePath)

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Diagnostic output path must be repository-relative: $RelativePath"
    }
    return Resolve-SharpProofContainedPath `
        -Root $repositoryRoot -Path $RelativePath `
        -ParameterName 'Diagnostic output path'
}

$catalog = Get-Content -LiteralPath $CatalogPath -Raw |
    ConvertFrom-Json -Depth 100
Assert-ExactMembers $catalog @('schemaVersion', 'outputs') 'Catalog'
if ([int](Get-RequiredMember $catalog 'schemaVersion' 'Catalog') -ne 1) {
    throw 'Only diagnostic catalog schema version 1 is supported.'
}

$outputs = @(Get-RequiredMember $catalog 'outputs' 'Catalog')
if ($outputs.Count -eq 0) {
    throw 'Diagnostic catalog must define at least one output.'
}

$outputNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$outputPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$allIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)

foreach ($output in $outputs) {
    Assert-ExactMembers $output @(
        'name',
        'namespace',
        'className',
        'outputPath',
        'usings',
        'supportedDiagnosticsMember',
        'diagnostics'
    ) 'Diagnostic output'
    $name = [string](Get-RequiredMember $output 'name' 'Diagnostic output')
    Assert-Identifier $name 'Diagnostic output name'
    if (-not $outputNames.Add($name)) {
        throw "Duplicate diagnostic output '$name'."
    }
    $namespace = [string](
        Get-RequiredMember $output 'namespace' "Output '$name'")
    if ($namespace -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') {
        throw "Output '$name' has invalid namespace '$namespace'."
    }
    $className = [string](
        Get-RequiredMember $output 'className' "Output '$name'")
    Assert-Identifier $className "Output '$name' class"
    $relativeOutputPath = [string](
        Get-RequiredMember $output 'outputPath' "Output '$name'")
    if (-not $relativeOutputPath.EndsWith(
            '.generated.cs',
            [StringComparison]::Ordinal)) {
        throw "Output '$name' must target a .generated.cs file."
    }
    $fullOutputPath = Resolve-RepositoryOutputPath $relativeOutputPath
    if (-not $outputPaths.Add($fullOutputPath)) {
        throw "Duplicate diagnostic output path '$relativeOutputPath'."
    }

    $supportedMemberProperty =
        $output.PSObject.Properties['supportedDiagnosticsMember']
    $supportedMember = if ($null -eq $supportedMemberProperty.Value) {
        $null
    }
    else {
        [string]$supportedMemberProperty.Value
    }
    if ($null -ne $supportedMember) {
        Assert-Identifier $supportedMember "Output '$name' supported member"
    }

    $diagnostics = @(
        Get-RequiredMember $output 'diagnostics' "Output '$name'")
    if ($diagnostics.Count -eq 0) {
        throw "Output '$name' must define at least one diagnostic."
    }
    $symbols = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $orderedSymbols = [Collections.Generic.List[string]]::new()
    $lines = New-SharpProofGeneratedHeader `
        -Generator 'scripts/Generate-DiagnosticDescriptors.ps1' `
        -Source 'eng/diagnostics/diagnostic-descriptors.v1.json.' `
        -Nullable
    foreach ($using in @(
            Get-RequiredMember $output 'usings' "Output '$name'")) {
        $usingName = [string]$using
        if ($usingName -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') {
            throw "Output '$name' has invalid using '$usingName'."
        }
        $lines.Add("using $usingName;")
    }
    $lines.Add('')
    $lines.Add("namespace $namespace;")
    $lines.Add('')
    $lines.Add("internal static class $className {")

    for ($index = 0; $index -lt $diagnostics.Count; $index++) {
        $diagnostic = $diagnostics[$index]
        Assert-ExactMembers $diagnostic @(
            'order',
            'symbol',
            'id',
            'title',
            'messageFormat',
            'category',
            'defaultSeverity',
            'isEnabledByDefault',
            'description',
            'helpLinkUri',
            'customTags'
        ) "Output '$name' diagnostic $index"
        $order = [int](
            Get-RequiredMember $diagnostic 'order' "Output '$name' diagnostic")
        if ($order -ne $index) {
            throw "Output '$name' diagnostic order must be contiguous at $index."
        }
        $symbol = [string](
            Get-RequiredMember $diagnostic 'symbol' "Output '$name' diagnostic")
        Assert-Identifier $symbol "Output '$name' diagnostic symbol"
        if (-not $symbols.Add($symbol)) {
            throw "Output '$name' repeats diagnostic symbol '$symbol'."
        }
        $orderedSymbols.Add($symbol)
        $id = [string](
            Get-RequiredMember $diagnostic 'id' "Diagnostic '$symbol'")
        if ($id -notmatch '^SP(?:(?:CF)\d{4}|(?:META)\d{3}|\d{4})$' -or
            -not $allIds.Add($id)) {
            throw "Diagnostic '$symbol' has invalid or duplicate ID '$id'."
        }
        $severity = [string](
            Get-RequiredMember $diagnostic 'defaultSeverity' "Diagnostic '$id'")
        if ($severity -notin @('Hidden', 'Info', 'Warning', 'Error')) {
            throw "Diagnostic '$id' has invalid severity '$severity'."
        }
        $enabled = [bool](
            Get-RequiredMember $diagnostic 'isEnabledByDefault' "Diagnostic '$id'")
        $helpProperty = $diagnostic.PSObject.Properties['helpLinkUri']
        $helpLink = if ($null -eq $helpProperty.Value) {
            $null
        }
        else {
            [string]$helpProperty.Value
        }
        if ($null -ne $helpLink -and
            -not [Uri]::IsWellFormedUriString(
                $helpLink,
                [UriKind]::Absolute)) {
            throw "Diagnostic '$id' has invalid help URI '$helpLink'."
        }
        $customTags = @(
            Get-RequiredMember $diagnostic 'customTags' "Diagnostic '$id'")
        if (@($customTags | Where-Object { $_ -isnot [string] }).Count -ne 0) {
            throw "Diagnostic '$id' custom tags must be strings."
        }
        foreach ($textMember in @(
                'title',
                'messageFormat',
                'category',
                'description')) {
            $value = [string](
                Get-RequiredMember $diagnostic $textMember "Diagnostic '$id'")
            if ([string]::IsNullOrWhiteSpace($value)) {
                throw "Diagnostic '$id' has blank '$textMember'."
            }
        }

        $lines.Add('')
        $lines.Add(
            "    internal static readonly DiagnosticDescriptor $symbol = new(")
        $lines.Add("        id: $(ConvertTo-CSharpString $id),")
        $lines.Add(
            '        title: ' +
            (ConvertTo-CSharpString ([string]$diagnostic.title)) +
            ',')
        $lines.Add(
            '        messageFormat: ' +
            (ConvertTo-CSharpString ([string]$diagnostic.messageFormat)) +
            ',')
        $lines.Add(
            '        category: ' +
            (ConvertTo-CSharpString ([string]$diagnostic.category)) +
            ',')
        $lines.Add(
            "        defaultSeverity: DiagnosticSeverity.$severity,")
        $enabledSource = if ($enabled) { 'true' } else { 'false' }
        $lines.Add("        isEnabledByDefault: $enabledSource,")
        $lines.Add(
            '        description: ' +
            (ConvertTo-CSharpString ([string]$diagnostic.description)) +
            ',')
        $lines.Add(
            '        helpLinkUri: ' +
            (ConvertTo-CSharpString $helpLink) +
            ',')
        $tagSource = if ($customTags.Count -eq 0) {
            '[]'
        }
        else {
            '[' + (@(
                $customTags |
                    ForEach-Object {
                        ConvertTo-CSharpString ([string]$_)
                    }
            ) -join ', ') + ']'
        }
        $lines.Add("        customTags: $tagSource);")
    }

    if ($null -ne $supportedMember) {
        $lines.Add('')
        $lines.Add(
            "    internal static readonly ImmutableArray<DiagnosticDescriptor> " +
            "$supportedMember = [")
        for ($index = 0; $index -lt $diagnostics.Count; $index++) {
            $symbol = $orderedSymbols[$index]
            $comma = if ($index -lt $diagnostics.Count - 1) { ',' } else { '' }
            $lines.Add("        $symbol$comma")
        }
        $lines.Add('    ];')
    }
    $lines.Add('}')

    $content = $lines -join "`n"
    [IO.Directory]::CreateDirectory(
        [IO.Path]::GetDirectoryName($fullOutputPath)) |
        Out-Null
    Update-SharpProofGeneratedFile `
        -Path $fullOutputPath `
        -Content $content `
        -DisplayPath $relativeOutputPath `
        -GeneratorCommand '.\scripts\Generate-DiagnosticDescriptors.ps1' `
        -Verify:$Verify
}

$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic diagnostic descriptors."

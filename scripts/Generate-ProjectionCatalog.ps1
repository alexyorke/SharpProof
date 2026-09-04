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
    Join-Path $repositoryRoot 'SharpProof.Projection.catalog.json')
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json -Depth 100

function Snippet([string]$Value, [string]$Context) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '[\r\n]') {
        throw "$Context must be a nonblank single-line C# snippet."
    }
    if ($Value -match '\b(if|for|foreach|while|catch)\b') {
        throw "$Context cannot contain an algorithmic control-flow keyword."
    }
    return $Value
}

if ([string](Required $catalog 'schema' 'Projection catalog') -ne
    'SharpProof.ProjectionCatalog' -or
    [int](Required $catalog 'schemaVersion' 'Projection catalog') -ne 1) {
    throw 'Projection catalog schema is unsupported.'
}

foreach ($output in @(Required $catalog 'outputs' 'Projection catalog')) {
    $relativePath = [string](Required $output 'path' 'Projection output')
    if ($relativePath -notmatch '^[^:]+\.generated\.cs$') {
        throw "Projection output path is not a generated C# file: '$relativePath'."
    }
    $path = Resolve-SharpProofContainedPath `
        -Root $repositoryRoot -Path $relativePath `
        -ParameterName 'Projection output path'
    $namespace = NamespaceName ([string](Required $output 'namespace' "output '$relativePath'") ) "output '$relativePath' namespace"
    $accessibility = Identifier ([string](Required $output 'accessibility' "output '$relativePath'") ) "output '$relativePath' accessibility"
    $modifiers = @(
        @(Required $output 'modifiers' "output '$relativePath'") | ForEach-Object {
            Identifier ([string]$_) "output '$relativePath' modifier"
        })
    $name = Identifier ([string](Required $output 'name' "output '$relativePath'") ) "output '$relativePath' name"
    $lines = New-SharpProofGeneratedHeader `
        -Generator 'Generate-ProjectionCatalog.ps1' `
        -Source 'SharpProof.Projection.catalog.json.' `
        -Notes @('Declarative projection tables only; analysis remains handwritten.') `
        -Nullable
    $lines.Add("namespace $namespace;")
    $lines.Add('')
    $modifierSource = if ($modifiers.Count -eq 0) { '' } else { ($modifiers -join ' ') + ' ' }
    $lines.Add("$accessibility $modifierSource`class $name")
    $lines.Add('{')
    foreach ($method in @(Required $output 'methods' "output '$relativePath'")) {
        $methodAccess = Identifier ([string](Required $method 'accessibility' "output '$relativePath' method") ) 'projection method accessibility'
        $staticProperty = $method.PSObject.Properties['static']
        $staticSource = if ($null -ne $staticProperty -and [bool]$staticProperty.Value) {
            'static '
        }
        else {
            ''
        }
        $returnType = TypeName ([string](Required $method 'returnType' "output '$relativePath' method") ) 'projection return type'
        $methodName = Identifier ([string](Required $method 'name' "output '$relativePath' method") ) 'projection method name'
        $parameters = @(Required $method 'parameters' "method '$methodName'")
        $lines.Add("    $methodAccess $staticSource$returnType $methodName(")
        for ($index = 0; $index -lt $parameters.Count; $index++) {
            $parameter = $parameters[$index]
            $parameterType = TypeName ([string](Required $parameter 'type' "method '$methodName' parameter") ) "method '$methodName' parameter type"
            $parameterName = Identifier ([string](Required $parameter 'name' "method '$methodName' parameter") ) "method '$methodName' parameter name"
            $comma = if ($index -lt $parameters.Count - 1) { ',' } else { '' }
            $lines.Add("        $parameterType $parameterName$comma")
        }
        $lines.Add('    )')
        $lines.Add('    {')
        $mode = [string](Required $method 'switchMode' "method '$methodName'")
        if ($mode -notin @('expression', 'type')) {
            throw "Method '$methodName' has unsupported switch mode '$mode'."
        }
        $target = Snippet ([string](Required $method 'target' "method '$methodName'") ) "method '$methodName' target"
        $lines.Add("        return $target switch")
        $lines.Add('        {')
        foreach ($case in @(Required $method 'cases' "method '$methodName'")) {
            $pattern = Snippet ([string](Required $case 'pattern' "method '$methodName' case") ) "method '$methodName' case pattern"
            $expression = Snippet ([string](Required $case 'expression' "method '$methodName' case") ) "method '$methodName' case expression"
            $lines.Add("            $pattern => $expression,")
        }
        $fallback = Snippet ([string](Required $method 'fallback' "method '$methodName'") ) "method '$methodName' fallback"
        $lines.Add("            _ => $fallback")
        $lines.Add('        };')
        $lines.Add('    }')
        $lines.Add('')
    }
    if ($lines[$lines.Count - 1] -eq '') {
        $lines.RemoveAt($lines.Count - 1)
    }
    $lines.Add('}')
    Update-SharpProofGeneratedFile -Path $path -Content ($lines -join "`n") `
        -DisplayPath $relativePath -GeneratorCommand '.\scripts\Generate-ProjectionCatalog.ps1' `
        -Verify:$Verify
}

$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic projection catalog."

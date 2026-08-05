[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$schemaPath = Join-Path $repositoryRoot `
    'SharpProof.CompilerArtifact\CompilerArtifactModel.schema.json'
$generatorPath = Join-Path $PSScriptRoot `
    'Generate-CompilerArtifactModel.ps1'
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$temporaryBase = Join-Path `
    ([IO.Path]::GetTempPath()) `
    'SharpProof-compiler-artifact-generator-tests'
$temporaryRoot = Join-Path `
    $temporaryBase `
    ('run-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporaryRoot)

function Invoke-GeneratorCase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Schema,

        [Parameter(Mandatory = $true)]
        [bool]$ShouldPass,

        [string]$ExpectedMessage = ''
    )

    $caseRoot = Join-Path $temporaryRoot $Name
    [void][IO.Directory]::CreateDirectory($caseRoot)
    $caseSchema = Join-Path $caseRoot 'schema.json'
    [IO.File]::WriteAllText(
        $caseSchema,
        $Schema,
        [Text.UTF8Encoding]::new($false))
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $generatorPath,
        '-SchemaPath',
        $caseSchema,
        '-ModelOutputPath',
        (Join-Path $caseRoot 'model.generated.cs'),
        '-PortableOutputPath',
        (Join-Path $caseRoot 'portable.generated.cs'),
        '-CompilationOutputPath',
        (Join-Path $caseRoot 'compilation.generated.cs'),
        '-CollectorOutputPath',
        (Join-Path $caseRoot 'collector.generated.cs'))
    & $pwsh @arguments *> (Join-Path $caseRoot 'generator.log')
    $exitCode = $LASTEXITCODE
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "Canonical generator case '$Name' failed with exit code $exitCode."
    }
    if (-not $ShouldPass -and $exitCode -eq 0) {
        throw "Malformed generator case '$Name' was accepted."
    }
    if (-not $ShouldPass -and
        -not [IO.File]::ReadAllText((Join-Path $caseRoot 'generator.log')).Contains(
            $ExpectedMessage,
            [StringComparison]::Ordinal)) {
        throw (
            "Malformed generator case '$Name' did not fail for the " +
            "expected reason '$ExpectedMessage'.")
    }
}

try {
    $canonical = [IO.File]::ReadAllText($schemaPath)
    Invoke-GeneratorCase `
        -Name 'canonical' `
        -Schema $canonical `
        -ShouldPass $true

    $unknownProperty = $canonical.Replace(
        '      "method": "TypeRow",',
        "      `"unexpected`": true,`n      `"method`": `"TypeRow`",")
    Invoke-GeneratorCase `
        -Name 'unknown-property' `
        -Schema $unknownProperty `
        -ShouldPass $false `
        -ExpectedMessage "unsupported property 'unexpected'"

    $unknownRole = $canonical.Replace(
        '{ "role": "direct", "member": "Kind" }',
        '{ "role": "unsupported", "member": "Kind" }')
    Invoke-GeneratorCase `
        -Name 'unknown-role' `
        -Schema $unknownRole `
        -ShouldPass $false `
        -ExpectedMessage "Unsupported metadata-row projection role 'unsupported'"

    $unknownSlotRole = $canonical.Replace(
        '{ "kind": "Boolean", "slots": ["booleanValue",',
        '{ "kind": "Boolean", "slots": ["unsupported",')
    Invoke-GeneratorCase `
        -Name 'unknown-slot-role' `
        -Schema $unknownSlotRole `
        -ShouldPass $false `
        -ExpectedMessage "has unsupported role 'unsupported'"

    $duplicateMethod = $canonical.Replace(
        '      "method": "VariableRow",',
        '      "method": "TypeRow",')
    Invoke-GeneratorCase `
        -Name 'duplicate-method' `
        -Schema $duplicateMethod `
        -ShouldPass $false `
        -ExpectedMessage "Duplicate portable IR metadata-row method 'TypeRow'"

    $missingArgument = $canonical.Replace(
        '        { "role": "optionalStringValue", "member": "Description" }',
        '')
    Invoke-GeneratorCase `
        -Name 'missing-argument' `
        -Schema $missingArgument `
        -ShouldPass $false `
        -ExpectedMessage 'at least one argument'

    Write-Host 'Compiler-artifact metadata-row generator validation passed.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedBase = [IO.Path]::GetFullPath($temporaryBase)
    if (-not $resolvedRoot.StartsWith(
            $resolvedBase + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove generator test directory: $resolvedRoot"
    }
    if (Test-Path -LiteralPath $resolvedRoot) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}

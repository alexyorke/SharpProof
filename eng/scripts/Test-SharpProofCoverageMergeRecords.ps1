[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
. (Join-Path $PSScriptRoot 'Resolve-SharpProofSourceDocument.ps1')
$source = Join-Path $PSScriptRoot 'Test-SharpProofCoverage.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$null)
foreach ($function in $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Resolve-CoverageSourcePath'
        }, $true)) { . ([scriptblock]::Create($function.Extent.Text)) }
$loops = @($ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.ForEachStatementAst] -and $node.Variable.VariablePath.UserPath -ceq 'report'
        }, $true) | Where-Object { $_.Condition.Extent.Text -ceq '$reports' })
if ($loops.Count -ne 1) { throw 'Expected one actual coverage report accumulator.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-merge-records-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $fixture 'src')) | Out-Null
[IO.File]::WriteAllLines((Join-Path $fixture 'src/Subject.cs'), [string[]]@(1..25 | ForEach-Object { '// fixture' }))
function Write-Report([string]$Name, [string]$Module, [string]$Lines, [string]$Authority = 'fixture') {
    $path = Join-Path $fixture ($Name + '.cobertura.xml')
    [IO.File]::WriteAllText($path, "<coverage><sharpProofAuthority schemaVersion='1' commit='$Authority' modules='A,B' /><sources><source>$fixture</source></sources><packages><package name='$Module'><classes><class name='Fixture' filename='src/Subject.cs'><lines>$Lines</lines></class></classes></package></packages></coverage>")
    return Get-Item -LiteralPath $path
}
function Invoke-MergeFixture {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '',
        Justification = 'The extracted production loop consumes this explicit post-authentication unit state through its script scope.')]
    param([object[]]$Reports)
    # Post-authentication unit state, not a substitute for an authority build.
    $repositoryRoot = $fixture
    $resolvedCoverageRoot = $fixture
    $reports = $Reports
    $recomputedAuthority = [pscustomobject]@{ commit = 'fixture' }
    $expectedModuleIdentityText = 'A,B'
    $expectedAssemblyNames = [Collections.Immutable.ImmutableHashSet]::Create[string]('ProductionA', 'ProductionB')
    $expectedLineHits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    $permittedLineCredits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    $reportedLineHits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    $permittedLineRanges = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    foreach ($module in @('ProductionA', 'ProductionB')) {
        $key = $module + ':src/Subject.cs'
        $hits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, int](); $hits[10] = 0; $hits[20] = 0
        $expectedLineHits[$key] = $hits
        $reportedLineHits[$key] = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, int]()
        $permittedLineCredits[$key] = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, object]()
        $permittedLineRanges[$key] = @(
            [pscustomobject]@{ startLine = 10; endLine = 20; creditLine = 10 },
            [pscustomobject]@{ startLine = 20; endLine = 20; creditLine = 20 })
    }
    $lineHits = $expectedLineHits
    $reportedModuleDocuments = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    $validatedClassRecords = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, int]([StringComparer]::Ordinal)
    $reusedClassRecords = 0; $validatedClassCount = 0
    $reportFiles = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    . ([scriptblock]::Create($loops[0].Extent.Text))
    return [pscustomobject]@{ reused = $reusedClassRecords; validated = $validatedClassCount; hits = $lineHits }
}
function Assert-MergeFailure([object[]]$Reports, [string]$Expected) {
    try { Invoke-MergeFixture -Reports $Reports | Out-Null }
    catch { if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }; return }
    throw "Coverage merge accepted invalid records: $Expected"
}
try {
    $lines = "<line number='10' hits='0' /><line number='20' hits='2' />"
    $first = Write-Report 'first' 'ProductionA' $lines
    $same = Write-Report 'same' 'ProductionA' $lines
    $otherModule = Write-Report 'other' 'ProductionB' $lines
    $zero = Write-Report 'zero' 'ProductionA' "<line number='10' hits='0' /><line number='20' hits='0' />"
    $result = Invoke-MergeFixture -Reports @($first, $same, $otherModule, $zero)
    if ($result.reused -ne 1 -or $result.validated -ne 3 -or
        $result.hits['ProductionA:src/Subject.cs'][10] -ne 2 -or $result.hits['ProductionB:src/Subject.cs'][10] -ne 2 -or
        $result.hits['ProductionA:src/Subject.cs'][20] -ne 2) { throw 'Coverage reuse changed MAX, module ownership, zero hits, or overlapping credits.' }
    $badAuthority = Write-Report 'authority' 'ProductionA' $lines 'other'
    Assert-MergeFailure @($first, $badAuthority) 'Coverage report authority does not match'
    foreach ($bad in @("<line number='999' hits='0' />", "<line number='10' hits='-1' />", "<line number='10' hits='invalid' />", "<line number='invalid' hits='0' />", "<line number='10' />")) {
        $report = Write-Report 'invalid' 'ProductionA' $bad
        $message = if ($bad.Contains("number='999'")) { 'outside the authenticated PDB universe' } else { 'malformed sequence point' }
        Assert-MergeFailure @($first, $report) $message
    }
    $empty = Write-Report 'empty' 'ProductionA' ''
    Assert-MergeFailure @($first, $empty) 'no production sequence points'
    Write-Host 'Coverage class reuse regressions passed: identical records, module isolation, MAX and zero hits, overlapping credits, authority mismatch, malformed records, and out-of-range zero hits.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::Ordinal) -or $resolved -eq [IO.Path]::GetFullPath([IO.Path]::GetTempPath())) { throw 'Invalid coverage fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

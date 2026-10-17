using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class CoverageChangedModuleTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangedLinesRequireEachOwningModulesOwnExecution(bool compilerGenerated)
    {
        // Explicit post-authentication unit state, never an authentication fixture.
        // Execute the actual changed-path loop with both ordinary-start evidence
        // and compiler-generated direct evidence that has no ordinary starts.
        const string script = """
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            $repositoryRoot = $env:SHARPPROOF_CHANGED_ROOT
            $path = 'eng/RoslynSymbolFacts.cs'
            $tokens = $null; $parseErrors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                (Join-Path $repositoryRoot 'scripts/Test-SharpProofCoverage.ps1'),
                [ref]$tokens, [ref]$parseErrors)
            if ($parseErrors.Count -ne 0) { throw 'Coverage script did not parse.' }
            foreach ($function in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -in @('Test-ClearlyNonSemanticSourceLine', 'Test-HasAddedSemanticSourceLines', 'Test-OmitDeclarationLine')
            }, $true)) { Invoke-Expression $function.Extent.Text }
            $loops = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.ForEachStatementAst] -and
                $node.Variable.VariablePath.UserPath -ceq 'changedPath'
            }, $true))
            if ($loops.Count -ne 1) { throw 'Expected exactly one production changed-path loop.' }
            $changedTcbFiles = @($path)
            $coverageTcbFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            [void]$coverageTcbFiles.Add($path)
            $numbers = [Collections.Generic.HashSet[int]]::new(); [void]$numbers.Add(15)
            $changedLines = @{}; $changedLines[$path] = $numbers
            $compileModuleKeys = @{}; $compileModuleKeys[$path] = @(('ProductionA:' + $path), ('ProductionB:' + $path))
            $lineHits = @{}; $reportedLineHits = @{}; $permittedLineRanges = @{}
            foreach ($assembly in @('ProductionA', 'ProductionB')) {
                $key = $assembly + ':' + $path
                $hits = if ($assembly -ceq 'ProductionA') { 7 } else { 0 }
                $lineHits[$key] = [Collections.Generic.Dictionary[int,int]]::new()
                if ($env:SHARPPROOF_CHANGED_GENERATED -ceq 'false') { $lineHits[$key][15] = $hits }
                $reportedLineHits[$key] = [Collections.Generic.Dictionary[int,int]]::new()
                $reportedLineHits[$key][15] = $hits
                $permittedLineRanges[$key] = @([pscustomobject]@{
                    startLine=15; endLine=15; creditLine= $(if ($env:SHARPPROOF_CHANGED_GENERATED -ceq 'false') {15} else {0})
                })
            }
            # Supply the old global view too, so the baseline exposes borrowed
            # heat rather than failing because its expected state is absent.
            $lineHits[$path] = $lineHits['ProductionA:' + $path]
            $reportedLineHits[$path] = $reportedLineHits['ProductionA:' + $path]
            $permittedLineRanges[$path] = $permittedLineRanges['ProductionA:' + $path]
            $declarationOnlyTcbFiles = [Collections.Generic.HashSet[string]]::new()
            $declarationLinesByPath = @{}
            $changedCovered = 0; $changedCoverable = 0
            $nonCoverableChangedFiles = [Collections.Generic.List[string]]::new()
            $declarationOnlyChangedFiles = [Collections.Generic.List[string]]::new()
            $uncoveredChangedLines = [Collections.Generic.List[string]]::new()
            $omittedDeclarationLines = [Collections.Generic.List[string]]::new()
            $qualifiedUnmappedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $unmappedLines = [Collections.Generic.List[string]]::new()
            . ([scriptblock]::Create($loops[0].Extent.Text))
            [pscustomobject]@{ covered=$changedCovered;coverable=$changedCoverable
                uncovered=@($uncoveredChangedLines);nonCoverable=@($nonCoverableChangedFiles)
                omitted=@($omittedDeclarationLines);unmapped=@($unmappedLines) } | ConvertTo-Json -Compress
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_CHANGED_ROOT"] = root;
        invocation.Environment["SHARPPROOF_CHANGED_GENERATED"] = compilerGenerated ? "true" : "false";
        var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.Output);
        var value = document.RootElement;
        Assert.That(value.GetProperty("covered").GetInt32(), Is.EqualTo(1));
        Assert.That(value.GetProperty("coverable").GetInt32(), Is.EqualTo(2));
        Assert.That(value.GetProperty("uncovered").GetArrayLength(), Is.EqualTo(1));
        Assert.That(value.GetProperty("uncovered")[0].GetString(),
            Is.EqualTo("ProductionB:eng/RoslynSymbolFacts.cs:15"));
        Assert.That(value.GetProperty("nonCoverable").GetArrayLength(), Is.Zero);
        Assert.That(value.GetProperty("omitted").GetArrayLength(), Is.Zero);
        Assert.That(value.GetProperty("unmapped").GetArrayLength(), Is.Zero);
    }

    [TestCase("unmapped", 0, 0, 2, 0, 0)]
    [TestCase("ordinary-overlap", 2, 2, 0, 0, 0)]
    // A generated PDB range cannot make a pure declaration coverable.
    [TestCase("generated-overlap", 0, 0, 0, 0, 2)]
    [TestCase("direct-zero", 2, 2, 0, 0, 0)]
    [TestCase("linked-mixed", 1, 1, 1, 0, 0)]
    [TestCase("unqualified", 2, 2, 0, 0, 0)]
    [TestCase("missing-own-pdb", 0, 0, 1, 1, 0)]
    [TestCase("declaration", 0, 0, 0, 0, 2)]
    [TestCase("outside-source", 2, 2, 2, 0, 0)]
    public async Task ReportsOnlyQualifiedUnmeasurableLinesOutsideThePercentage(
        string scenario, int coverable, int uncovered, int unmapped, int nonCoverable, int omitted)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        // Explicit post-authentication unit state. The real production loop
        // decides the accounting; these inputs do not authenticate a report.
        const string script = """
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            $repositoryRoot = $env:SHARPPROOF_CHANGED_ROOT
            $path = 'eng/RoslynSymbolFacts.cs'
            $scenario = $env:SHARPPROOF_CHANGED_SCENARIO
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                (Join-Path $repositoryRoot 'scripts/Test-SharpProofCoverage.ps1'),
                [ref]$null, [ref]$null)
            foreach ($function in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -in @('Test-ClearlyNonSemanticSourceLine', 'Test-HasAddedSemanticSourceLines',
                    'Test-OmitDeclarationLine', 'Get-OrdinalSortedUniqueStrings')
            }, $true)) { Invoke-Expression $function.Extent.Text }
            $loops = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.ForEachStatementAst] -and
                $node.Variable.VariablePath.UserPath -ceq 'changedPath'
            }, $true))
            if ($loops.Count -ne 1) { throw 'Expected exactly one production changed-path loop.' }
            $changedTcbFiles = @($path)
            $coverageTcbFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            [void]$coverageTcbFiles.Add($path)
            $numbers = [Collections.Generic.HashSet[int]]::new(); [void]$numbers.Add(15)
            if ($scenario -ceq 'outside-source') { [void]$numbers.Add(999999) }
            $changedLines = @{}; $changedLines[$path] = $numbers
            $compileModuleKeys = @{}; $compileModuleKeys[$path] = @(('ProductionA:' + $path), ('ProductionB:' + $path))
            $lineHits = @{}; $reportedLineHits = @{}; $permittedLineRanges = @{}
            foreach ($assembly in @('ProductionA', 'ProductionB')) {
                $key = $assembly + ':' + $path
                $lineHits[$key] = [Collections.Generic.Dictionary[int,int]]::new()
                $reportedLineHits[$key] = [Collections.Generic.Dictionary[int,int]]::new()
                $permittedLineRanges[$key] = @()
                if ($scenario -ceq 'ordinary-overlap') {
                    $lineHits[$key][14] = 0
                    $permittedLineRanges[$key] = @([pscustomobject]@{startLine=14;endLine=16;creditLine=14})
                }
                if ($scenario -ceq 'generated-overlap' -or
                    ($scenario -ceq 'linked-mixed' -and $assembly -ceq 'ProductionB')) {
                    $permittedLineRanges[$key] = @([pscustomobject]@{startLine=14;endLine=16;creditLine=0})
                }
                # The accumulator normally rejects records outside PDB ranges.
                # This explicit state also proves an exact zero record cannot
                # be mistaken for absence by the changed-line loop.
                if ($scenario -ceq 'direct-zero') { $reportedLineHits[$key][15] = 0 }
            }
            if ($scenario -ceq 'missing-own-pdb') { [void]$lineHits.Remove('ProductionB:' + $path) }
            $qualifiedUnmappedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            if ($scenario -cne 'unqualified') { [void]$qualifiedUnmappedPaths.Add($path) }
            $declarationOnlyTcbFiles = [Collections.Generic.HashSet[string]]::new()
            $declarationLinesByPath = @{}
            if ($scenario -in @('declaration', 'ordinary-overlap', 'generated-overlap')) {
                $declarations = [Collections.Generic.HashSet[int]]::new(); [void]$declarations.Add(15)
                $declarationLinesByPath[$path] = $declarations
            }
            $changedCovered = 0; $changedCoverable = 0
            $nonCoverableChangedFiles = [Collections.Generic.List[string]]::new()
            $declarationOnlyChangedFiles = [Collections.Generic.List[string]]::new()
            $uncoveredChangedLines = [Collections.Generic.List[string]]::new()
            $omittedDeclarationLines = [Collections.Generic.List[string]]::new()
            $unmappedLines = [Collections.Generic.List[string]]::new()
            . ([scriptblock]::Create($loops[0].Extent.Text))
            $reportExpressions = @()
            foreach ($table in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.HashtableAst]
            }, $true)) {
                $keys = @($table.KeyValuePairs | ForEach-Object { $_.Item1.Extent.Text })
                if ($keys -notcontains 'comparisonRef') { continue }
                foreach ($pair in $table.KeyValuePairs) {
                    if ($pair.Item1.Extent.Text -ceq 'unmappedLines') { $reportExpressions += $pair.Item2.Extent.Text }
                }
            }
            if ($reportExpressions.Count -ne 2) { throw 'Expected default and measured unmappedLines report fields.' }
            $defaultReported = @(Invoke-Expression $reportExpressions[0])
            $measuredReported = @(Invoke-Expression $reportExpressions[1])
            [pscustomobject]@{covered=$changedCovered;coverable=$changedCoverable
                uncovered=@($uncoveredChangedLines);unmapped=@($unmappedLines)
                nonCoverable=@($nonCoverableChangedFiles);omitted=@($omittedDeclarationLines)
                defaultReported=@($defaultReported);measuredReported=@($measuredReported)} | ConvertTo-Json -Compress
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_CHANGED_ROOT"] = root;
        invocation.Environment["SHARPPROOF_CHANGED_SCENARIO"] = scenario;
        var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.Output);
        var value = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.GetProperty("covered").GetInt32(), Is.Zero);
            Assert.That(value.GetProperty("coverable").GetInt32(), Is.EqualTo(coverable));
            Assert.That(value.GetProperty("uncovered").GetArrayLength(), Is.EqualTo(uncovered));
            Assert.That(value.GetProperty("unmapped").GetArrayLength(), Is.EqualTo(unmapped));
            Assert.That(value.GetProperty("nonCoverable").GetArrayLength(), Is.EqualTo(nonCoverable));
            Assert.That(value.GetProperty("omitted").GetArrayLength(), Is.EqualTo(omitted));
            Assert.That(value.GetProperty("defaultReported").GetArrayLength(), Is.Zero);
            Assert.That(value.GetProperty("measuredReported").EnumerateArray().Select(static row => row.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ? throw new System.ArgumentException("Coverage entries must be strings or null.", nameof(row)) : (row.GetString())),
                Is.EquivalentTo(value.GetProperty("unmapped").EnumerateArray().Select(static row => row.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ? throw new System.ArgumentException("Coverage entries must be strings or null.", nameof(row)) : (row.GetString()))));
            if (scenario is "linked-mixed" or "missing-own-pdb")
            {
                Assert.That(value.GetProperty("unmapped")[0].GetString(), Is.EqualTo("ProductionA:eng/RoslynSymbolFacts.cs:15"));
                var retained = scenario == "linked-mixed" ? "uncovered" : "nonCoverable";
                Assert.That(value.GetProperty(retained)[0].GetString(),
                    Is.EqualTo("ProductionB:eng/RoslynSymbolFacts.cs" + (scenario == "linked-mixed" ? ":15" : "")));
            }
        }
    }
}

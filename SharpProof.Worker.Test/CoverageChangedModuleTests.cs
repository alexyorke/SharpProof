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
            . ([scriptblock]::Create($loops[0].Extent.Text))
            [pscustomobject]@{ covered=$changedCovered;coverable=$changedCoverable
                uncovered=@($uncoveredChangedLines);nonCoverable=@($nonCoverableChangedFiles)
                omitted=@($omittedDeclarationLines) } | ConvertTo-Json -Compress
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_CHANGED_ROOT"] = root;
        invocation.Environment["SHARPPROOF_CHANGED_GENERATED"] = compilerGenerated ? "true" : "false";
        var result = await ProcessRunner.RunCapturedAsync(invocation, CancellationToken.None);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        using var document = JsonDocument.Parse(result.Output);
        var value = document.RootElement;
        Assert.That(value.GetProperty("covered").GetInt32(), Is.EqualTo(1));
        Assert.That(value.GetProperty("coverable").GetInt32(), Is.EqualTo(2));
        Assert.That(value.GetProperty("uncovered").EnumerateArray().Select(static x => x.GetString()),
            Is.EquivalentTo(new[] { "ProductionB:eng/RoslynSymbolFacts.cs:15" }));
        Assert.That(value.GetProperty("nonCoverable").GetArrayLength(), Is.Zero);
        Assert.That(value.GetProperty("omitted").GetArrayLength(), Is.Zero);
    }
}

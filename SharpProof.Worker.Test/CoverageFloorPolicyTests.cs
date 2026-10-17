using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class CoverageFloorPolicyTests
{
    [TestCase(20000, 20001, 100.0, false)]
    [TestCase(1000000, 1000001, 100.0, false)]
    [TestCase(19999, 20000, 100.0, false)]
    [TestCase(20001, 20001, 100.0, true)]
    [TestCase(0, 1, 100.0, false)]
    [TestCase(0, 0, 100.0, true)]
    [TestCase(20000, 20001, 99.99, true)]
    [TestCase(9998, 10000, 99.99, false)]
    [TestCase(9999, 10000, 99.99, true)]
    [TestCase(8999, 10000, 90.0, false)]
    [TestCase(9000, 10000, 90.0, true)]
    [TestCase(0, 1, 0.0, true)]
    public async Task AppliesTheFloorToProjectAggregateAndChangedTcbCounts(
        int covered, int coverable, double minimum, bool expected)
    {
        // Execute the production predicates without supplying or modifying any
        // authenticated coverage artifacts. Extract optional pure helpers too.
        const string script = """
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $parseErrors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                $env:SHARPPROOF_COVERAGE_POLICY_SCRIPT, [ref]$tokens, [ref]$parseErrors)
            if ($parseErrors.Count -ne 0) { throw 'Coverage script did not parse.' }
            foreach ($function in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq 'Test-CoverageFloor'
            }, $true)) {
                Invoke-Expression $function.Extent.Text
            }
            $covered = [int]$env:SHARPPROOF_POLICY_COVERED
            $coverable = [int]$env:SHARPPROOF_POLICY_COVERABLE
            $minimum = [double]::Parse($env:SHARPPROOF_POLICY_MINIMUM,
                [Globalization.CultureInfo]::InvariantCulture)
            $percent = if ($coverable -eq 0) { 100.0 } else { 100.0 * $covered / $coverable }
            $measurement = [pscustomobject]@{
                coveredLines = $covered; coverableLines = $coverable
                linePercent = [Math]::Round($percent, 2)
            }
            $aggregate = $measurement
            $aggregateMinimum = $minimum
            $changedCovered = $covered
            $changedCoverable = $coverable
            $changedPercent = $measurement.linePercent
            $nonCoverableChangedFiles = @()
            $baseline = [pscustomobject]@{ minimumChangedTcbLinePercent = $minimum }
            $predicates = @{}
            foreach ($table in $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.HashtableAst]
            }, $true)) {
                $keys = @($table.KeyValuePairs | ForEach-Object { $_.Item1.Extent.Text })
                $policy = if ($keys -contains 'name') { 'project' }
                    elseif ($keys -contains 'comparisonRef') { 'changedTcb' }
                    else { $null }
                foreach ($pair in $table.KeyValuePairs) {
                    if ($null -ne $policy -and $pair.Item1.Extent.Text -eq 'passed') {
                        $predicates[$policy] = $pair.Item2.Extent.Text
                    }
                }
            }
            if ($predicates.Count -ne 2 -or -not $predicates.ContainsKey('project') -or
                -not $predicates.ContainsKey('changedTcb')) {
                throw 'Expected project and changed-TCB predicates.'
            }
            $aggregateAssignment = @($ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left.Extent.Text -eq '$aggregatePassed'
            }, $true))
            if ($aggregateAssignment.Count -ne 1) { throw 'Expected one aggregate predicate.' }
            [pscustomobject]@{
                project = [bool](Invoke-Expression $predicates.project)
                aggregate = [bool](Invoke-Expression $aggregateAssignment[0].Right.Extent.Text)
                changedTcb = [bool](Invoke-Expression $predicates.changedTcb)
            } | ConvertTo-Json -Compress
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_COVERAGE_POLICY_SCRIPT"] = Path.Combine(
            root, "scripts", "Test-SharpProofCoverage.ps1");
        invocation.Environment["SHARPPROOF_POLICY_COVERED"] = covered.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        invocation.Environment["SHARPPROOF_POLICY_COVERABLE"] = coverable.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        invocation.Environment["SHARPPROOF_POLICY_MINIMUM"] = minimum.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var result = await ProcessRunner.RunCapturedAsync(invocation, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        using var results = JsonDocument.Parse(result.Output);
        using (Assert.EnterMultipleScope())
        {
            foreach (var policy in new[] { "project", "aggregate", "changedTcb" })
            {
                Assert.That(results.RootElement.GetProperty(policy).GetBoolean(),
                    Is.EqualTo(expected), $"{policy}: {covered}/{coverable}, floor {minimum}");
            }
        }
    }
}

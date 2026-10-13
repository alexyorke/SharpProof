using System.Text;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class CoverageShardEvidenceTests
{
    [TestCase("complete", true)]
    [TestCase("deployed-complete", true)]
    [TestCase("missing-task", false)]
    [TestCase("duplicate-task", false)]
    [TestCase("failed-task", false)]
    [TestCase("empty-trx", false)]
    [TestCase("failed-trx", false)]
    [TestCase("missing-report", false)]
    [TestCase("deleted-report", false)]
    [TestCase("altered-report", false)]
    [TestCase("added-report", false)]
    [TestCase("wrong-phase", false)]
    [TestCase("altered-authority", false)]
    [TestCase("missing-project", false)]
    public async Task RequiresCompleteSuccessfulCohortsAndUnmodifiedEvidence(string scenario, bool accepted)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            . $env:SHARPPROOF_SHARD_EVIDENCE_SCRIPT
            $root = Join-Path ([IO.Path]::GetTempPath()) ('shard-evidence-' + [Guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory((Join-Path $root 'campaign/task')) | Out-Null
            $scenario = $env:SHARPPROOF_SHARD_SCENARIO
            try {
                $taskRoot = Join-Path $root 'campaign/task'
                $report = Join-Path $taskRoot 'coverage.cobertura.xml'
                '<coverage />' | Set-Content -LiteralPath $report
                '{}' | Set-Content -LiteralPath (Join-Path $root 'coverage-authority.json')
                $total = if ($scenario -eq 'empty-trx') { 0 } else { 1 }
                $failed = if ($scenario -eq 'failed-trx') { 1 } else { 0 }
                $deployment = ''
                if ($scenario -eq 'deployed-complete') {
                    [IO.Directory]::CreateDirectory((Join-Path $taskRoot 'deployment/In')) | Out-Null
                    $deployment = '<TestSettings><Deployment runDeploymentRoot="deployment" /></TestSettings>'
                    Move-Item -LiteralPath $report -Destination (Join-Path $taskRoot 'deployment/In/coverage.cobertura.xml')
                }
                @"
            <TestRun>$deployment<ResultSummary><Counters total="$total" failed="$failed" error="0" /></ResultSummary><CollectorDataEntries><Collector><UriAttachments><UriAttachment><A href="coverage.cobertura.xml" /></UriAttachment></UriAttachments></Collector></CollectorDataEntries></TestRun>
            "@ | Set-Content -LiteralPath (Join-Path $taskRoot 'receipt.trx')
                $expected = @([pscustomobject]@{ Name = 'task' })
                if ($scenario -eq 'missing-project') { $expected[0] | Add-Member Target '/missing/Subject.Test.csproj' }
                $completed = @([pscustomobject]@{ Test = $expected[0]; ExitCode = 0 })
                switch ($scenario) {
                    'missing-task' { $completed = @() }
                    'duplicate-task' { $completed += $completed[0] }
                    'failed-task' { $completed[0].ExitCode = 1 }
                    'missing-report' { Remove-Item -LiteralPath $report }
                }
                $valid = $false
                try {
                    Save-SharpProofCoverageCohort -Root (Join-Path $root 'campaign') -Expected $expected -Completed $completed
                    Save-SharpProofCoverageShardManifest -Root $root -Phase package
                    switch ($scenario) {
                        'deleted-report' { Remove-Item -LiteralPath $report }
                        'altered-report' { '<coverage altered="true" />' | Set-Content -LiteralPath $report }
                        'added-report' { '<coverage />' | Set-Content -LiteralPath (Join-Path $root 'extra.cobertura.xml') }
                        'altered-authority' { '{"commit":"different"}' | Set-Content -LiteralPath (Join-Path $root 'coverage-authority.json') }
                    }
                    $phase = if ($scenario -eq 'wrong-phase') { 'semantic' } else { 'package' }
                    Assert-SharpProofCoverageShardManifest -Root $root -Phase $phase
                    $valid = $true
                }
                catch { }
                Write-Output $valid
            }
            finally { [IO.Directory]::Delete($root, $true) }
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_SHARD_EVIDENCE_SCRIPT"] = Path.Combine(root,
            "scripts", "SharpProof.CoverageShardEvidence.ps1");
        invocation.Environment["SHARPPROOF_SHARD_SCENARIO"] = scenario;
        var result = await ProcessRunner.RunCapturedAsync(invocation, CancellationToken.None);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        Assert.That(bool.Parse(result.Output.Trim()), Is.EqualTo(accepted), result.CombinedOutput);
    }
}

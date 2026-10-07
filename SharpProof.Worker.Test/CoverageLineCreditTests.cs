using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class CoverageLineCreditTests
{
    [Test]
    public async Task PreservesEveryOverlappingStartAndPermittedEmptyCredit()
    {
        using var results = await ReadResultsAsync();
        var expected = new Dictionary<int, int[]>
        {
            [10] = [10],
            [15] = [10, 15],
            [20] = [10, 15],
            [25] = [15],
            [30] = [],
            [35] = [35]
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (number, credits) in expected)
            {
                var result = FindResult(results, number);
                Assert.That(result.GetProperty("permitted").GetBoolean(), Is.True,
                    $"Line {number} is authenticated even when it has no credit.");
                Assert.That(result.GetProperty("credits").EnumerateArray()
                    .Select(static value => value.GetInt32()), Is.EquivalentTo(credits),
                    $"Line {number} must credit all overlapping starts exactly once.");
            }
        }
    }

    [Test]
    public async Task RejectsGapsAndLinesOutsideTheAuthenticatedRanges()
    {
        using var results = await ReadResultsAsync();
        using (Assert.EnterMultipleScope())
        {
            foreach (var number in new[] { 9, 26, 36 })
            {
                var result = FindResult(results, number);
                Assert.That(result.GetProperty("permitted").GetBoolean(), Is.False);
                Assert.That(result.GetProperty("error").GetString(), Does.Contain(
                    $"outside the authenticated PDB universe: 'Subject.cs:{number}'."));
            }
        }
    }

    private static JsonElement FindResult(JsonDocument results, int number)
    {
        return results.RootElement.EnumerateArray().Single(result =>
            result.GetProperty("number").GetInt32() == number);
    }

    private static async Task<JsonDocument> ReadResultsAsync()
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            Import-Module $env:SHARPPROOF_COVERAGE_TEST_MODULE -Force
            $ranges = @(
                [pscustomobject]@{ startLine = 10; endLine = 20; creditLine = 10 },
                [pscustomobject]@{ startLine = 15; endLine = 25; creditLine = 15 },
                [pscustomobject]@{ startLine = 15; endLine = 25; creditLine = 15 },
                [pscustomobject]@{ startLine = 30; endLine = 35; creditLine = 0 },
                [pscustomobject]@{ startLine = 35; endLine = 35; creditLine = 35 }
            )
            $rows = foreach ($number in @(9, 10, 15, 20, 25, 26, 30, 35, 36)) {
                try {
                    [int[]]$credits = Resolve-SharpProofCoverageLineCredits -Ranges $ranges -Number $number -SourcePath 'Subject.cs'
                    [pscustomobject]@{ number = $number; permitted = $true; credits = @($credits); error = $null }
                }
                catch {
                    [pscustomobject]@{ number = $number; permitted = $false; credits = @(); error = $_.Exception.Message }
                }
            }
            ConvertTo-Json -InputObject @($rows) -Depth 4 -Compress
            """;
        var root = TestRepository.FindRoot();
        var invocation = ProcessRunner.CreateStartInfo(root, "pwsh", [
            "-NoLogo", "-NoProfile", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        invocation.Environment["SHARPPROOF_COVERAGE_TEST_MODULE"] = Path.Combine(
            root, "scripts", "SharpProof.ContainerExecution.psm1");
        var result = await ProcessRunner.RunCapturedAsync(
            invocation, CancellationToken.None);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        return JsonDocument.Parse(result.Output);
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FuzzResultValidationTests
{
    [Test]
    public async Task CampaignValidatorRequiresCompleteTotalProgramEvidence()
    {
        TestRepository.RequireCanonicalContainer();
        using var temporaryDirectory = new TempDirectory("SharpProof.FuzzValidation.");
        var summary = new FuzzSummary(7, 1000, 23063, 4, 1000, 0, 1000, 1000, 1000,
            new(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            new(1000, 1000, 1000, 900, 200, 200, 200, 100, 100, 100, 100, 100, 8191, 40), new(1000, 1000, 1000, 1000, 1023, 127), true, []);
        var valid = JsonSerializer.Serialize(summary);
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, "valid.json"), valid);
        Action<JsonObject>[] mutations =
        [
            root => root["SchemaVersion"] = 4,
            root => root["SchemaVersion"] = 5,
            root => root["SchemaVersion"] = 6,
            root => root.Remove("MetadataProgramCoverage"),
            root => root["MetadataProgramCoverage"] = null,
            root => root["MetadataProgramCoverage"]!["Cases"] = 999,
            root => root["MetadataProgramCoverage"]!["Agreements"] = 999,
            root => root["MetadataProgramCoverage"]!["NativeProofs"] = 999,
            root => root["MetadataProgramCoverage"]!["NativeRefutations"] = 999,
            root => root["MetadataProgramCoverage"]!["TypeMask"] = 1022,
            root => root["MetadataProgramCoverage"]!["RecipeMask"] = 63,
            root => root["MetadataProgramCoverage"]!["Cases"] = "1000",
            root => root["MetadataProgramCoverage"]!["Unknown"] = 1,
            root => root.Remove("TotalProgramCoverage"),
            root => root["TotalProgramCoverage"] = null,
            root => root["TotalProgramCoverage"]!["Agreements"] = 999,
            root => root["TotalProgramCoverage"]!["Cases"] = 999,
            root => root["TotalProgramCoverage"]!["NativeProofs"] = 999,
            root => root["TotalProgramCoverage"]!["NativeRefutations"] = 901,
            root => root["TotalProgramCoverage"]!["CheckedBodies"] = -1,
            root => root["TotalProgramCoverage"]!["LoopBodies"] = 0,
            root => root["TotalProgramCoverage"]!["TypeMask"] = 1023,
            root => root["TotalProgramCoverage"]!["ArrayReadBodies"] = 0,
            root => root["TotalProgramCoverage"]!["ArrayReadBodies"] = -1,
            root => root["TotalProgramCoverage"]!["ArrayReadBodies"] = 101,
            root => root["TotalProgramCoverage"]!["ExceptionalExits"] = 101,
            root => root["TotalProgramCoverage"]!["Cases"] = "1000",
            root => root["TotalProgramCoverage"]!["Unknown"] = 1
        ];
        for (var index = 0; index < mutations.Length; index++)
        {
            var root = JsonNode.Parse(valid)!.AsObject();
            mutations[index](root);
            await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, $"invalid{index}.json"), root.ToJsonString());
        }
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, "invalid-duplicate.json"),
            valid.Replace("\"TypeMask\":8191", "\"TypeMask\":8191,\"TypeMask\":8191", StringComparison.Ordinal));
        var script = Path.Combine(temporaryDirectory.FullName, "validate.ps1");
        await File.WriteAllTextAsync(script, """
            param([string]$Validator, [string]$Inputs)
            $ErrorActionPreference = 'Stop'
            . $Validator
            $arguments = @{ ExpectedCases = 1000; ExpectedSeed = 23063; ExpectedMaximumParallelism = 4 }
            $valid = Assert-SharpProofFuzzRunnerResult -Path (Join-Path $Inputs 'valid.json') @arguments
            if ($valid.TotalProgramCoverage.Agreements -ne 1000) { throw 'Validated Total coverage was lost.' }
            if ($valid.MetadataProgramCoverage.Agreements -ne 1000) { throw 'Validated metadata coverage was lost.' }
            $rejected = 0
            foreach ($file in Get-ChildItem -LiteralPath $Inputs -Filter 'invalid*.json') {
                try {
                    $null = Assert-SharpProofFuzzRunnerResult -Path $file.FullName @arguments
                } catch {
                    if ($_.Exception.Message -notlike 'Invalid fuzz runner result:*') { throw }
                    $rejected++
                    continue
                }
                throw "Accepted malformed result: $($file.Name)"
            }
            Write-Output $rejected
            """);
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", script, "-Validator",
            Path.Combine(TestRepository.FindRoot(), "scripts", "Assert-SharpProofFuzzRunnerResult.ps1"), "-Inputs", temporaryDirectory.FullName })
        { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.That(process.ExitCode, Is.Zero, await errors);
        Assert.That((await output).Trim(), Is.EqualTo((mutations.Length + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}

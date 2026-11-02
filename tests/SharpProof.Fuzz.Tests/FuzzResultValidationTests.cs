using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
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
            // Generated casts take string operands, so real runs report no InvalidCast results.
            new(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0),
            new(1000, 1000, 1000, 900, 200, 200, 200, 100, 100, 100, 100, 100, 8191, 40), new(1000, 1000, 1000, 1000, 1023, 127), true, []);
        var valid = JsonSerializer.Serialize(summary);
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, "valid.json"), valid);
        ImmutableArray<Action<JsonObject>> mutations =
        [
            root => { System.ArgumentNullException.ThrowIfNull(root); root["SchemaVersion"] = 4; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["SchemaVersion"] = 5; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["SchemaVersion"] = 6; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["FrontendCoverage"]!["IndexOutOfRangeExceptions"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["FrontendCoverage"]!["TextParameters"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["FrontendCoverage"]!["InvalidCastExceptions"] = -1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root.Remove("MetadataProgramCoverage"); },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"] = null; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["Cases"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["Agreements"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["NativeProofs"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["NativeRefutations"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["TypeMask"] = 1022; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["RecipeMask"] = 63; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["Cases"] = "1000"; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["MetadataProgramCoverage"]!["Unknown"] = 1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root.Remove("TotalProgramCoverage"); },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"] = null; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["Agreements"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["Cases"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["NativeProofs"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["NativeRefutations"] = 901; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["CheckedBodies"] = -1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["LoopBodies"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["TypeMask"] = 1023; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["ArrayReadBodies"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["ArrayReadBodies"] = -1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["ArrayReadBodies"] = 101; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["ExceptionalExits"] = 101; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["Cases"] = "1000"; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["TotalProgramCoverage"]!["Unknown"] = 1; }
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
        Assert.That(await RunValidatorAsync(script, temporaryDirectory.FullName), Is.EqualTo((mutations.Length + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    // The first input is what the runner really printed, so a property added on
    // one side only fails here rather than in the nightly campaign.
    [Test]
    public async Task ResourceBoundValidatorAcceptsRealRunnerOutputAndRejectsIncompleteEvidence()
    {
        TestRepository.RequireCanonicalContainer();
        using var temporaryDirectory = new TempDirectory("SharpProof.ResourceBoundValidation.");
        var cancellation = TestContext.CurrentContext.CancellationToken;
        using var runnerOutput = new StringWriter(CultureInfo.InvariantCulture);
        using var runnerError = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = await FuzzRunner.RunCliAsync(["--oracle", FuzzOptions.ResourceBoundsOracle, "--cases", "20", "--seed", "12345"],
            runnerOutput, runnerError, cancellation);
        Assert.That(exitCode, Is.Zero, runnerError.ToString());
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, "real.json"), runnerOutput.ToString(), cancellation);
        var summary = new ResourceBoundFuzzSummary(1, 1000, 23063, 4, new(1000, 400, 600, 0, 350, 400, 4000, 300, 0, 1023, 15), true, []);
        Assert.That(summary.Passed, Is.True);
        var valid = JsonSerializer.Serialize(summary);
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, "valid.json"), valid, cancellation);
        ImmutableArray<Action<JsonObject>> mutations =
        [
            root => { System.ArgumentNullException.ThrowIfNull(root); root["SchemaVersion"] = 2; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Unknown"] = 1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Passed"] = false; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["CoverageSatisfied"] = false; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root.Remove("Coverage"); },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"] = null; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["Cases"] = 999; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["Cases"] = "1000"; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ProvenCases"] = 399; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ViolatingCases"] = 1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["TightCases"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["TightCases"] = 401; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["NativeBounds"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["NativeBounds"] = 401; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["Executions"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ExceptionalExecutions"] = 0; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ExceptionalExecutions"] = 4001; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["SkippedInputs"] = -1; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ShapeMask"] = 511; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ProvenShapeMask"] = 7; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["ProvenShapeMask"] = 2047; },
            root => { System.ArgumentNullException.ThrowIfNull(root); root["Coverage"]!["Unknown"] = 1; }
        ];
        for (var index = 0; index < mutations.Length; index++)
        {
            var root = JsonNode.Parse(valid)!.AsObject();
            mutations[index](root);
            await File.WriteAllTextAsync(Path.Combine(temporaryDirectory.FullName, $"invalid{index}.json"), root.ToJsonString(), cancellation);
        }
        var script = Path.Combine(temporaryDirectory.FullName, "validate.ps1");
        await File.WriteAllTextAsync(script, """
            param([string]$Validator, [string]$Inputs)
            $ErrorActionPreference = 'Stop'
            . $Validator
            $real = Assert-SharpProofResourceBoundFuzzRunnerResult -Path (Join-Path $Inputs 'real.json') `
                -ExpectedCases 20 -ExpectedSeed 12345 -ExpectedMaximumParallelism 4
            if ($real.Coverage.Cases -ne 20) { throw 'Real runner coverage was lost.' }
            $arguments = @{ ExpectedCases = 1000; ExpectedSeed = 23063; ExpectedMaximumParallelism = 4 }
            $valid = Assert-SharpProofResourceBoundFuzzRunnerResult -Path (Join-Path $Inputs 'valid.json') @arguments
            if ($valid.Coverage.ProvenCases -ne 400) { throw 'Validated coverage was lost.' }
            $rejected = 0
            foreach ($file in Get-ChildItem -LiteralPath $Inputs -Filter 'invalid*.json') {
                try {
                    $null = Assert-SharpProofResourceBoundFuzzRunnerResult -Path $file.FullName @arguments
                } catch {
                    if ($_.Exception.Message -notlike 'Invalid resource bound fuzz result:*') { throw }
                    $rejected++
                    continue
                }
                throw "Accepted malformed result: $($file.Name)"
            }
            Write-Output $rejected
            """, cancellation);
        Assert.That(await RunValidatorAsync(script, temporaryDirectory.FullName), Is.EqualTo(mutations.Length.ToString(CultureInfo.InvariantCulture)));
    }

    private static async Task<string> RunValidatorAsync(string script, string inputs)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(inputs);
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in ImmutableArray.Create(
            "-NoLogo",
            "-NoProfile",
            "-File",
            script,
            "-Validator",
            Path.Combine(TestRepository.FindRoot(), "eng/scripts", "Assert-SharpProofFuzzRunnerResult.ps1"),
            "-Inputs",
            inputs))
        { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.That(process.ExitCode, Is.Zero, await errors);
        return (await output).Trim();
    }
}

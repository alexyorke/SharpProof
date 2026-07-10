using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryScalabilityTests
{
    [Test]
    public async Task EffectSummaryTool_ResumesCompletedArtifactSpecOutputs()
    {
        const string source = """
using System;

public static class ResumableSummaryFixture
{
    public static void Root() => throw new InvalidOperationException();
}
""";

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryResumableArtifactSpec",
            source);
        var specPath = Path.Combine(fixture.DirectoryPath, "artifact-spec.json");
        var firstOutputPath = Path.Combine(fixture.DirectoryPath, "first.SharpProof.EffectSummary.json");
        var secondOutputPath = Path.Combine(fixture.DirectoryPath, "second.SharpProof.EffectSummary.json");
        var progressPath = Path.Combine(fixture.DirectoryPath, "artifact-progress.json");
        var spec = new
        {
            SchemaVersion = 1,
            Artifacts = new[]
            {
                new
                {
                    OutputPath = Path.GetFileName(firstOutputPath),
                    AssemblyPaths = new[] { fixture.AssemblyPath },
                    SymbolPrefixes = new[] { "ResumableSummaryFixture.Root" },
                    IncludeTransitiveRoots = true,
                },
                new
                {
                    OutputPath = Path.GetFileName(secondOutputPath),
                    AssemblyPaths = new[] { fixture.AssemblyPath },
                    SymbolPrefixes = new[] { "ResumableSummaryFixture.Root" },
                    IncludeTransitiveRoots = true,
                },
            },
        };
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec));

        var initial = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--artifact-spec",
            specPath,
            "--progress",
            progressPath);
        Assert.That(initial.ExitCode, Is.EqualTo(0), initial.StandardError);
        Assert.That(File.Exists(firstOutputPath), Is.True);
        Assert.That(File.Exists(secondOutputPath), Is.True);
        Assert.That(File.Exists(progressPath), Is.False);

        var artifactSpecSha256 = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(specPath)))
            .ToLowerInvariant();
        var firstOutputHash = await ComputeSha256Async(firstOutputPath);
        await File.WriteAllTextAsync(
            progressPath,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                ArtifactSpecSha256 = artifactSpecSha256,
                CompletedOutputPaths = new[] { Path.GetFullPath(firstOutputPath) },
            }));
        File.Delete(secondOutputPath);

        var resumed = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--artifact-spec",
            specPath,
            "--progress",
            progressPath,
            "--resume");
        Assert.That(resumed.ExitCode, Is.EqualTo(0), resumed.StandardError);
        Assert.That(File.Exists(secondOutputPath), Is.True);
        Assert.That(File.Exists(progressPath), Is.False);
        var resumedFirstOutputHash = await ComputeSha256Async(firstOutputPath);
        Assert.That(resumedFirstOutputHash, Is.EqualTo(firstOutputHash));
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    [Test]
    public async Task EffectSummaryTool_RejectsNonPositiveExceptionEdgeCap()
    {
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--max-exception-edges",
            "0");

        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(result.StandardError, Does.Contain("must be greater than zero"));
    }

    [Test]
    public async Task EffectSummaryTool_BoundsTransitiveExceptionEdgesForUnboundedCalleeRuns()
    {
        var source = """
using System;

public static class ExceptionFanout
{
    public static void Root()
    {
        Throw0();
        Throw1();
        Throw2();
        Throw3();
    }

    public static void Throw0() => throw new InvalidOperationException();
    public static void Throw1() => throw new InvalidOperationException();
    public static void Throw2() => throw new InvalidOperationException();
    public static void Throw3() => throw new InvalidOperationException();
}
""";

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryExceptionEdgeCap",
            source);
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--assembly",
            fixture.AssemblyPath,
            "--symbol-prefix",
            "ExceptionFanout.Root",
            "--include-callees",
            "--max-depth",
            "-1",
            "--transitive-roots",
            "--max-exception-edges",
            "2");

        Assert.That(
            result.ExitCode,
            Is.EqualTo(0),
            result.StandardError + Environment.NewLine + result.StandardOutput);

        using var summary = JsonDocument.Parse(result.StandardOutput);
        var root = summary.RootElement
            .GetProperty("Assemblies")[0]
            .GetProperty("Methods")
            .EnumerateArray()
            .Single(method => string.Equals(
                method.GetProperty("Symbol").GetString(),
                "ExceptionFanout.Root()",
                StringComparison.Ordinal));

        Assert.That(
            root.GetProperty("TransitiveThrownExceptionEdges").GetArrayLength(),
            Is.EqualTo(2));
        Assert.That(
            root.GetProperty("TransitiveThrownExceptionEdgesTruncated").GetBoolean(),
            Is.True);
    }
}

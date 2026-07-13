using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryScalabilityTests
{
    [Test]
    public async Task EffectSummaryTool_ShardOutputWritesOneDocumentPerAssembly()
    {
        const string source = """
                              using System;

                              public static class ShardFixture
                              {
                                  public static void Root() => Throw();

                                  public static void Throw() => throw new InvalidOperationException();
                              }
                              """;

        await using var firstFixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryShardOne",
            source);
        await using var secondFixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryShardTwo",
            source);
        var outputDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-shards-" + Guid.NewGuid().ToString("N"));
        var progressPath = Path.Combine(outputDirectory, "shard-progress.json");
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--assembly",
            firstFixture.AssemblyPath,
            "--assembly",
            secondFixture.AssemblyPath,
            "--include-callees",
            "--max-depth",
            "-1",
            "--transitive-roots",
            "--max-exception-edges",
            "2",
            "--shard-output",
            outputDirectory,
            "--progress",
            progressPath);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        var shardPaths = Directory
            .EnumerateFiles(outputDirectory, "*.SharpProof.EffectSummary.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.That(shardPaths, Has.Length.EqualTo(2));
        Assert.That(File.Exists(progressPath), Is.False);

        foreach (var shardPath in shardPaths)
        {
            using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(shardPath));
            Assert.That(summary.RootElement.GetProperty("Assemblies").GetArrayLength(), Is.EqualTo(1));
        }
    }

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
                    IncludeTransitiveRoots = true
                },
                new
                {
                    OutputPath = Path.GetFileName(secondOutputPath),
                    AssemblyPaths = new[] { fixture.AssemblyPath },
                    SymbolPrefixes = new[] { "ResumableSummaryFixture.Root" },
                    IncludeTransitiveRoots = true
                }
            }
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
                CompletedOutputPaths = new[] { Path.GetFullPath(firstOutputPath) }
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

    [Test]
    public async Task EffectSummaryTool_ResumeWithoutProgressStartsFresh()
    {
        const string source = """
                              public static class FreshResumeFixture
                              {
                                  public static int Value() => 42;
                              }
                              """;

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryFreshResume",
            source);
        var outputPath = Path.Combine(fixture.DirectoryPath, "output.SharpProof.EffectSummary.json");
        var specPath = Path.Combine(fixture.DirectoryPath, "artifact-spec.json");
        var missingProgressPath = Path.Combine(fixture.DirectoryPath, "missing-progress.json");
        await File.WriteAllTextAsync(
            specPath,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Artifacts = new[]
                {
                    new
                    {
                        OutputPath = Path.GetFileName(outputPath),
                        AssemblyPaths = new[] { fixture.AssemblyPath }
                    }
                }
            }));

        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--artifact-spec",
            specPath,
            "--progress",
            missingProgressPath,
            "--resume");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        Assert.That(File.Exists(outputPath), Is.True);
    }

    [Test]
    public async Task EffectSummaryTool_ReviewedImpureCategoriesOverrideReanalysis()
    {
        const string source = """
                              public static class ReviewedCategoryFixture
                              {
                                  private static int _value;

                                  public static int Touch()
                                  {
                                      _value++;
                                      return _value;
                                  }
                              }
                              """;

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryReviewedCategories",
            source);
        var seedPath = Path.Combine(fixture.DirectoryPath, "seed.SharpProof.EffectSummary.json");
        var outputPath = Path.Combine(fixture.DirectoryPath, "output.SharpProof.EffectSummary.json");
        var specPath = Path.Combine(fixture.DirectoryPath, "artifact-spec.json");
        var progressPath = Path.Combine(fixture.DirectoryPath, "artifact-progress.json");
        var seedResult = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--assembly",
            fixture.AssemblyPath,
            "--classify-purity");
        Assert.That(seedResult.ExitCode, Is.EqualTo(0), seedResult.StandardError);

        var seed = JsonNode.Parse(seedResult.StandardOutput)!.AsObject();
        var seedEntry = seed["GeneratedPurityCatalog"]!["Entries"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(entry => string.Equals(
                entry["DisplayName"]!.GetValue<string>(),
                "ReviewedCategoryFixture.Touch()",
                StringComparison.Ordinal));
        seedEntry["Categories"] = new JsonArray("reviewed_category");
        seedEntry["PrimaryCategory"] = "reviewed_category";
        await File.WriteAllTextAsync(seedPath, seed.ToJsonString());

        await File.WriteAllTextAsync(
            specPath,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Artifacts = new object[]
                {
                    new
                    {
                        OutputPath = Path.GetFileName(seedPath),
                        AssemblyPaths = new[] { fixture.AssemblyPath },
                        IncludePurityClassification = true
                    },
                    new
                    {
                        OutputPath = Path.GetFileName(outputPath),
                        AssemblyPaths = new[] { fixture.AssemblyPath },
                        IncludePurityClassification = true
                    }
                }
            }));
        var artifactSpecSha256 = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(specPath)))
            .ToLowerInvariant();
        await File.WriteAllTextAsync(
            progressPath,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                ArtifactSpecSha256 = artifactSpecSha256,
                CompletedOutputPaths = new[] { Path.GetFullPath(seedPath) }
            }));

        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--artifact-spec",
            specPath,
            "--progress",
            progressPath,
            "--resume");
        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);

        using var output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var method = output.RootElement.GetProperty("Assemblies")[0]
            .GetProperty("Methods")
            .EnumerateArray()
            .Single(entry => string.Equals(
                entry.GetProperty("DisplayName").GetString(),
                "ReviewedCategoryFixture.Touch()",
                StringComparison.Ordinal));
        Assert.That(
            method.GetProperty("PurityClassification")
                .GetProperty("Categories")
                .EnumerateArray()
                .Select(static category => category.GetString()),
            Is.EqualTo(new[] { "reviewed_category" }));
    }

    [Test]
    public async Task EffectSummaryTool_ShardedProgressRecordsToolIdentity()
    {
        const string source = """
                              public static class ToolIdentityFixture
                              {
                                  public static int Value() => 42;
                              }
                              """;

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryToolIdentity",
            source);
        var invalidAssemblyPath = Path.Combine(fixture.DirectoryPath, "invalid.dll");
        await File.WriteAllTextAsync(invalidAssemblyPath, "not an assembly");
        var outputDirectory = Path.Combine(fixture.DirectoryPath, "shards");
        var progressPath = Path.Combine(fixture.DirectoryPath, "shard-progress.json");

        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--assembly",
            fixture.AssemblyPath,
            "--assembly",
            invalidAssemblyPath,
            "--shard-output",
            outputDirectory,
            "--progress",
            progressPath);

        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(File.Exists(progressPath), Is.True);
        using var progress = JsonDocument.Parse(await File.ReadAllTextAsync(progressPath));
        var toolPath = EffectSummaryToolTests.GetEffectSummaryToolDllPath();
        var expectedModuleVersionId = Assembly.LoadFrom(toolPath)
            .ManifestModule.ModuleVersionId.ToString("D");
        Assert.That(
            progress.RootElement.GetProperty("ToolModuleVersionId").GetString(),
            Is.EqualTo(expectedModuleVersionId));
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

    [TestCase("--max-depth")]
    [TestCase("--max-exception-edges")]
    [TestCase("--limit")]
    public async Task EffectSummaryTool_RejectsMalformedIntegerOptionsCleanly(string option)
    {
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(option, "not-an-integer");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.StandardError, Does.Contain("requires an integer value"));
        Assert.That(result.StandardError, Does.Not.Contain("System.FormatException"));
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
                method.GetProperty("DisplayName").GetString(),
                "ExceptionFanout.Root()",
                StringComparison.Ordinal));

        Assert.That(
            root.GetProperty("TransitiveThrownExceptionEdges").GetArrayLength(),
            Is.EqualTo(2));
        Assert.That(
            root.GetProperty("TransitiveThrownExceptionEdgesTruncated").GetBoolean(),
            Is.True);
    }

    [Test]
    public async Task EffectSummaryTool_CyclicExceptionGraph_PropagatesAndTerminatesPerScc()
    {
        const string source = """
                              using System;

                              public static class CycleFixture
                              {
                                  public static void A()
                                  {
                                      B();
                                      Throw();
                                  }

                                  public static void B() => A();

                                  private static void Throw() => throw new InvalidOperationException();
                              }
                              """;

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummarySccCycle",
            source);
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--assembly",
            fixture.AssemblyPath,
            "--include-callees",
            "--max-depth",
            "-1",
            "--transitive-roots");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        using var summary = JsonDocument.Parse(result.StandardOutput);
        var methodB = summary.RootElement
            .GetProperty("Assemblies")[0]
            .GetProperty("Methods")
            .EnumerateArray()
            .Single(method => string.Equals(
                method.GetProperty("DisplayName").GetString(),
                "CycleFixture.B()",
                StringComparison.Ordinal));
        Assert.That(
            methodB.GetProperty("TransitiveThrownExceptionTypes")
                .EnumerateArray()
                .Select(static value => value.GetString()),
            Does.Contain("System.InvalidOperationException"));
    }
}

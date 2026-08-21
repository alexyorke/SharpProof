using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class CoverageScriptTests
{
    private static readonly string[] s_trustedPaths =
    [
        "Project/Trusted.cs"
    ];

    [Test]
    public void ContainerCoverageRequiresExplicitComparisonAuthority()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "coverage.yml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Not.Contain("'HEAD^'"));
            Assert.That(
                script,
                Does.Contain(
                    "SHARPPROOF_COVERAGE_COMPARISON_REF is required"));
            Assert.That(workflow, Does.Not.Contain("comparison='HEAD^'"));
            Assert.That(workflow, Does.Contain("comparison_ref:"));
        }
    }

    [Test]
    public async Task RelativeHeadComparisonCannotHideEarlierTcbCommit()
    {
        var repository = await CreateMultiCommitFixtureAsync();
        try
        {
            var result = await RunCoverageAsync(
                repository,
                comparisonRef: "HEAD^",
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(
                result.Output + result.Error,
                Does.Contain("durable explicit comparison authority"));
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task ExplicitComparisonCoversEarlierTcbCommit()
    {
        var repository = await CreateMultiCommitFixtureAsync();
        try
        {
            var result = await RunCoverageAsync(
                repository,
                comparisonRef: "comparison",
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Zero, result.Error);
            using var document = JsonDocument.Parse(result.Output);
            Assert.That(
                document.RootElement
                    .GetProperty("changedTcb")
                    .GetProperty("changedFiles")
                    .GetInt32(),
                Is.EqualTo(1));
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task MissingAndUnusableComparisonAuthoritiesFailClosed()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            var missing = await RunCoverageAsync(
                repository,
                comparisonRef: null,
                reportOnly: false);
            var unusable = await RunCoverageAsync(
                repository,
                comparisonRef: "missing-comparison-ref",
                reportOnly: false,
                includeWorkingTree: true);
            var localReport = await RunCoverageAsync(
                repository,
                comparisonRef: null,
                reportOnly: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(missing.ExitCode, Is.Not.Zero);
                Assert.That(
                    missing.Output + missing.Error,
                    Does.Contain("ComparisonRef is required"));
                Assert.That(unusable.ExitCode, Is.Not.Zero);
                Assert.That(localReport.ExitCode, Is.Zero, localReport.Error);
            }
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task OneCommitRepositoryRejectsRelativeHeadAuthority()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            var result = await RunCoverageAsync(
                repository,
                comparisonRef: "HEAD^",
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(
                result.Output + result.Error,
                Does.Contain("durable explicit comparison authority"));
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task ExplicitComparisonCoversTcbChangeThroughMergeCommit()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "branch",
                "comparison"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "switch",
                "-c",
                "trusted-change"));
            await WriteTrustedSourceAsync(repository, value: 1);
            await CommitAllAsync(repository, "trusted change");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "switch",
                "-c",
                "integration",
                "comparison"));
            await File.WriteAllTextAsync(
                Path.Combine(repository, "unrelated.txt"),
                "unrelated\n");
            await CommitAllAsync(repository, "unrelated change");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "merge",
                "--no-ff",
                "trusted-change",
                "-m",
                "merge trusted change"));

            var result = await RunCoverageAsync(
                repository,
                comparisonRef: "comparison",
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Zero, result.Error);
            using var document = JsonDocument.Parse(result.Output);
            Assert.That(
                document.RootElement
                    .GetProperty("changedTcb")
                    .GetProperty("changedFiles")
                    .GetInt32(),
                Is.EqualTo(1));
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task WorkingTreeKeepsIdenticalFeatureTcbEdit()
    {
        await AssertChangedFilesAsync(
            featureChangesTcb: true,
            expectedChangedFiles: 1);
    }

    [Test]
    public async Task WorkingTreeDoesNotInventComparisonOnlyTcbEdit()
    {
        await AssertChangedFilesAsync(
            featureChangesTcb: false,
            expectedChangedFiles: 0);
    }

    [TestCaseSource(nameof(UnmappedSemanticDeclarationCases))]
    public async Task UnmappedSemanticDeclarationFailsClosed(
        string originalLine,
        string changedLine,
        bool generated)
    {
        var result = await RunUnmappedChangedLineFixtureAsync(
            originalLine,
            changedLine,
            generated,
            targetClosesMethod: false);

        Assert.That(result.Process.ExitCode, Is.Zero, result.Process.Error);
        using var document = JsonDocument.Parse(result.Process.Output);
        var changed = document.RootElement.GetProperty("changedTcb");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                changed.GetProperty("uncoveredLines")
                    .EnumerateArray()
                    .Select(static value => value.GetString())
                    .ToArray(),
                Is.EqualTo(new[]
                {
                    "Project/Trusted.cs:" + result.ChangedLine
                }));
            Assert.That(
                changed.GetProperty("passed").GetBoolean(),
                Is.False);
        }
    }

    [Test]
    public async Task UnmappedTriviaOnlyChangeRemainsAdmissible()
    {
        var result = await RunUnmappedChangedLineFixtureAsync(
            "    // explanation before",
            "    // explanation after",
            generated: false,
            targetClosesMethod: false);

        Assert.That(result.Process.ExitCode, Is.Zero, result.Process.Error);
        using var document = JsonDocument.Parse(result.Process.Output);
        var changed = document.RootElement.GetProperty("changedTcb");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                changed.GetProperty("uncoveredLines").GetArrayLength(),
                Is.Zero);
            Assert.That(changed.GetProperty("passed").GetBoolean(), Is.True);
        }
    }

    [Test]
    public async Task UnmappedBraceOnlyChangeRemainsAdmissible()
    {
        var result = await RunUnmappedChangedLineFixtureAsync(
            "    }",
            "}",
            generated: false,
            targetClosesMethod: true);

        Assert.That(result.Process.ExitCode, Is.Zero, result.Process.Error);
        using var document = JsonDocument.Parse(result.Process.Output);
        var changed = document.RootElement.GetProperty("changedTcb");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                changed.GetProperty("uncoveredLines").GetArrayLength(),
                Is.Zero);
            Assert.That(changed.GetProperty("passed").GetBoolean(), Is.True);
        }
    }

    [Test]
    public async Task CaseDistinctTcbPathsKeepIndependentCoverage()
    {
        RequireLinuxFileNames();
        var upper = "Project/Trusted.cs";
        var lower = "Project/trusted-lower.cs";
        var result = await RunCoverageIdentityFixtureAsync(
            [
                new CoverageEntry(upper, upper, 1),
                new CoverageEntry(lower, lower, 0)
            ]);

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var aggregate = document.RootElement.GetProperty("aggregate");
        var changed = document.RootElement.GetProperty("changedTcb");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                aggregate.GetProperty("coverableLines").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                aggregate.GetProperty("coveredLines").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                changed.GetProperty("changedFiles").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                changed.GetProperty("coverableLines").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                changed.GetProperty("coveredLines").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                changed.GetProperty("uncoveredLines")
                    .EnumerateArray()
                    .Select(static value => value.GetString())
                    .ToArray(),
                Is.EqualTo(new[] { lower + ":3" }));
            Assert.That(
                changed.GetProperty("passed").GetBoolean(),
                Is.False);
        }
    }

    [TestCase(
        "Project/Ordinary.cs",
        "Project/Ordinary.cs",
        TestName = "ExactGitAndCoveragePathControl")]
    [TestCase(
        "Project/Trüsted-Case.cs",
        "Project/Trüsted-Case.cs",
        TestName = "GitQuotedUnicodePathIsDecoded")]
    [TestCase(
        "Project/WindowsReport.cs",
        "Project\\WindowsReport.cs",
        TestName = "WindowsCoverageSeparatorsAreNormalized")]
    public async Task ChangedTcbUsesExactDecodedPath(
        string sourcePath,
        string reportPath)
    {
        RequireLinuxFileNames();
        var result = await RunCoverageIdentityFixtureAsync(
            [new CoverageEntry(sourcePath, reportPath, 0)]);

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var changed = document.RootElement.GetProperty("changedTcb");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                changed.GetProperty("changedFiles").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                changed.GetProperty("coverageFiles").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                changed.GetProperty("coverableLines").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                changed.GetProperty("uncoveredLines")
                    .EnumerateArray()
                    .Select(static value => value.GetString())
                    .ToArray(),
                Is.EqualTo(new[] { sourcePath + ":3" }));
            Assert.That(
                changed.GetProperty("passed").GetBoolean(),
                Is.False);
        }
    }

    [TestCase("one-line")]
    [TestCase("truncated")]
    [TestCase("missing-project")]
    [TestCase("wrong-assembly")]
    [TestCase("foreign-source")]
    [TestCase("duplicate-report")]
    public async Task AuthenticatedCoverageRejectsReportMutations(string mutation)
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await PrepareCoverageFixtureAsync(repository);
            var reportPath = Path.Combine(
                repository,
                "coverage",
                "fixture.cobertura.xml");
            ApplyCoverageMutation(reportPath, mutation);

            var result = await RunCoverageScriptOnlyAsync(
                repository,
                comparisonRef: null,
                reportOnly: true);

            Assert.That(
                result.ExitCode,
                Is.Not.Zero,
                mutation + ": " + result.Error + result.Output);
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task AuthenticatedCoverageAllowsLinesInsidePdbSequencePointSpans()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Project", "Trusted.cs"),
                "public static class Trusted\n" +
                "{\n" +
                "    public static int Covered() =>\n" +
                "        1 +\n" +
                "        0;\n" +
                "}\n");
            await CommitAllAsync(repository, "multiline sequence point");
            await PrepareCoverageFixtureAsync(repository);

            var coverage = Path.Combine(repository, "coverage");
            using var authority = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(
                    coverage,
                    "coverage-authority.json")));
            var sourceDocument = authority.RootElement
                .GetProperty("modules")[0]
                .GetProperty("documents")[0];
            var startLines = sourceDocument
                .GetProperty("sequencePoints")
                .EnumerateArray()
                .Select(static value => value.GetInt32())
                .ToHashSet();
            var interiorLine = sourceDocument
                .GetProperty("sequencePointRanges")
                .EnumerateArray()
                .SelectMany(static range => Enumerable.Range(
                    range.GetProperty("startLine").GetInt32(),
                    range.GetProperty("endLine").GetInt32() -
                    range.GetProperty("startLine").GetInt32() + 1))
                .First(line => !startLines.Contains(line));

            var reportPath = Path.Combine(
                coverage,
                "fixture.cobertura.xml");
            var report = XDocument.Load(reportPath);
            report.Descendants("class").First().Element("lines")!.Add(
                new XElement(
                    "line",
                    new XAttribute("number", interiorLine),
                    new XAttribute("hits", 1)));
            report.Save(reportPath, SaveOptions.DisableFormatting);

            var result = await RunCoverageScriptOnlyAsync(
                repository,
                comparisonRef: null,
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task AuthenticatedCoverageCountsOmittedSequencePointsAsUncovered()
    {
        var result = await RunCoverageIdentityFixtureAsync(
            [
                new CoverageEntry("Project/First.cs", "Project/First.cs", 1),
                new CoverageEntry(
                    "Project/Second.cs",
                    "Project/Second.cs",
                    0,
                    IncludeInReport: false)
            ]);

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var aggregate = document.RootElement.GetProperty("aggregate");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                aggregate.GetProperty("coverableLines").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                aggregate.GetProperty("coveredLines").GetInt32(),
                Is.EqualTo(1));
        }
    }

    [Test]
    public async Task AuthenticatedCoverageIgnoresGeneratedObjDocuments()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await PrepareCoverageFixtureAsync(repository);
            var reportPath = Path.Combine(
                repository,
                "coverage",
                "fixture.cobertura.xml");
            var report = XDocument.Load(reportPath);
            report.Descendants("classes").First().Add(
                new XElement(
                    "class",
                    new XAttribute(
                        "name",
                        "GeneratedLibraryImports"),
                    new XAttribute(
                        "filename",
                        "Project/obj/Release/net8.0/Generator/LibraryImports.g.cs"),
                    new XElement(
                        "lines",
                        new XElement(
                            "line",
                            new XAttribute("number", 1),
                            new XAttribute("hits", 1)))));
            report.Save(reportPath, SaveOptions.DisableFormatting);

            var result = await RunCoverageScriptOnlyAsync(
                repository,
                comparisonRef: null,
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task AuthenticatedCoverageIgnoresVstestArchiveCopies()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await PrepareCoverageFixtureAsync(repository);
            var coverage = Path.Combine(repository, "coverage");
            var reportPath = Path.Combine(
                coverage,
                "fixture.cobertura.xml");
            var archive = Path.Combine(coverage, "archive", "In", "host");
            Directory.CreateDirectory(archive);
            File.Copy(
                reportPath,
                Path.Combine(archive, "fixture.cobertura.xml"));

            var result = await RunCoverageScriptOnlyAsync(
                repository,
                comparisonRef: null,
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task AuthenticatedCoverageIgnoresNonProductionPackages()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await PrepareCoverageFixtureAsync(repository);
            var reportPath = Path.Combine(
                repository,
                "coverage",
                "fixture.cobertura.xml");
            var report = XDocument.Load(reportPath);
            report.Descendants("packages").First().Add(
                new XElement(
                    "package",
                    new XAttribute("name", "Project.TestSupport"),
                    new XElement(
                        "classes",
                        new XElement(
                            "class",
                            new XAttribute("name", "ForeignSupport"),
                            new XAttribute("filename", "ForeignSupport.cs"),
                            new XElement(
                                "lines",
                                new XElement(
                                    "line",
                                    new XAttribute("number", 1),
                                    new XAttribute("hits", 1)))))));
            report.Save(reportPath, SaveOptions.DisableFormatting);

            var result = await RunCoverageScriptOnlyAsync(
                repository,
                comparisonRef: null,
                reportOnly: true);

            Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task ProductionInventoryExcludesCompilerGeneratedAccessors()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Project", "Trusted.cs"),
                "public static class Trusted\n" +
                "{\n" +
                "    public static int Generated\n" +
                "    {\n" +
                "        get;\n" +
                "    } = 1;\n" +
                "    public static int Covered() => 1;\n" +
                "}\n");
            await PrepareCoverageFixtureAsync(repository);
            using var authority = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(
                        repository,
                        "coverage",
                        "coverage-authority.json")));
            var sourceDocument = authority.RootElement
                .GetProperty("modules")[0]
                .GetProperty("documents")[0];
            var sequencePoints = sourceDocument
                .GetProperty("sequencePoints")
                .EnumerateArray()
                .Select(static value => value.GetInt32())
                .ToArray();
            var permittedRangeStarts = sourceDocument
                .GetProperty("sequencePointRanges")
                .EnumerateArray()
                .Select(static value => value.GetProperty("startLine").GetInt32())
                .ToArray();

            Assert.That(sequencePoints, Does.Contain(7));
            Assert.That(sequencePoints, Does.Not.Contain(5));
            Assert.That(permittedRangeStarts, Does.Contain(5));
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    private static async Task AssertChangedFilesAsync(
        bool featureChangesTcb,
        int expectedChangedFiles)
    {
        var root = RepositoryRoot();
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-coverage-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "init",
                "--object-format=sha1"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "user.email",
                "coverage-script@example.invalid"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "user.name",
                "Coverage Script Test"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "core.autocrlf",
                "false"));

            await WriteFixtureAsync(root, repository);
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "add",
                "--",
                "."));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "root"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "branch",
                "feature"));

            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "switch",
                "-c",
                "comparison"));
            await WriteTrustedSourceAsync(repository, value: 1);
            await CommitAllAsync(repository, "comparison TCB change");

            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "switch",
                "feature"));
            if (featureChangesTcb)
            {
                await WriteTrustedSourceAsync(repository, value: 1);
            }
            else
            {
                await File.WriteAllTextAsync(
                    Path.Combine(repository, "feature.txt"),
                    "feature branch\n");
            }
            await CommitAllAsync(repository, "feature change");

            await File.WriteAllTextAsync(
                Path.Combine(repository, "README.md"),
                "unrelated working-tree change\n");
            var status = await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "status",
                "--porcelain"));
            Assert.That(status.Output, Does.Contain("README.md"));

            await PrepareCoverageFixtureAsync(repository);

            var result = await RunAsync(
                repository,
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-File",
                Path.Combine(
                    repository,
                    "scripts",
                    "Test-SharpProofCoverage.ps1"),
                "-CoverageRoot",
                Path.Combine(repository, "coverage"),
                "-BaselinePath",
                Path.Combine(repository, "baseline.json"),
                "-ComparisonRef",
                "comparison",
                "-IncludeWorkingTree",
                "-ReportOnly");
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            using var document = JsonDocument.Parse(result.Output);
            Assert.That(
                document.RootElement
                    .GetProperty("changedTcb")
                    .GetProperty("changedFiles")
                    .GetInt32(),
                Is.EqualTo(expectedChangedFiles));
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    private static async Task<ProcessResult> RunCoverageIdentityFixtureAsync(
        IReadOnlyList<CoverageEntry> entries)
    {
        var root = RepositoryRoot();
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-coverage-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await InitializeRepositoryAsync(repository);
            await WriteIdentityFixtureAsync(root, repository, entries);
            await CommitAllAsync(repository, "root");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "branch",
                "comparison"));
            foreach (var entry in entries)
            {
                await WriteSourceAsync(repository, entry.SourcePath, value: 1);
            }
            await CommitAllAsync(repository, "change trusted sources");

            await PrepareCoverageFixtureAsync(repository);

            return await RunAsync(
                repository,
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-File",
                Path.Combine(
                    repository,
                    "scripts",
                    "Test-SharpProofCoverage.ps1"),
                "-CoverageRoot",
                Path.Combine(repository, "coverage"),
                "-BaselinePath",
                Path.Combine(repository, "baseline.json"),
                "-ComparisonRef",
                "comparison",
                "-ReportOnly");
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    private static IEnumerable<TestCaseData>
        UnmappedSemanticDeclarationCases()
    {
        yield return new TestCaseData(
            "    public const int Limit = 128;",
            "    public const int Limit = 129;",
            false).SetName("UnmappedConstantChangeFailsClosed");
        yield return new TestCaseData(
            "    public static int Initialized = 1;",
            "    public static int Initialized = 2;",
            false).SetName("UnmappedInitializerChangeFailsClosed");
        yield return new TestCaseData(
            "    [System.Obsolete(\"before\")] public static void Legacy() { }",
            "    [System.Obsolete(\"after\")] public static void Legacy() { }",
            false).SetName("UnmappedAttributeChangeFailsClosed");
        yield return new TestCaseData(
            "    public static int Visible = 1;",
            "    internal static int Visible = 1;",
            false).SetName("UnmappedModifierChangeFailsClosed");
        yield return new TestCaseData(
            "    public static int Transform(int value) => value;",
            "    public static long Transform(int value) => value;",
            false).SetName("UnmappedSignatureChangeFailsClosed");
        yield return new TestCaseData(
            "    public static int Computed => 1;",
            "    public static int Computed => 2;",
            false).SetName("UnmappedExpressionBodyChangeFailsClosed");
        yield return new TestCaseData(
            "    public const int GeneratedLimit = 128;",
            "    public const int GeneratedLimit = 129;",
            true).SetName("UnmappedGeneratedDeclarationChangeFailsClosed");
    }

    private static async Task<ChangedLineResult>
        RunUnmappedChangedLineFixtureAsync(
            string originalLine,
            string changedLine,
            bool generated,
            bool targetClosesMethod)
    {
        var root = RepositoryRoot();
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-coverage-unmapped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await InitializeRepositoryAsync(repository);
            var original = CreateChangedLineSource(
                originalLine,
                generated,
                targetClosesMethod);
            await WriteChangedLineFixtureAsync(root, repository, original);
            await CommitAllAsync(repository, "root");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "branch",
                "comparison"));

            var changed = CreateChangedLineSource(
                changedLine,
                generated,
                targetClosesMethod);
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Project", "Trusted.cs"),
                changed.Text);
            await CommitAllAsync(repository, "change trusted source");

            var process = await RunCoverageAsync(
                repository,
                comparisonRef: "comparison",
                reportOnly: true);
            return new ChangedLineResult(process, changed.TargetLine);
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    private static SourceFixture CreateChangedLineSource(
        string targetLine,
        bool generated,
        bool targetClosesMethod)
    {
        var lines = new List<string>();
        if (generated)
        {
            lines.Add("// <auto-generated />");
        }
        lines.Add("public static class Trusted");
        lines.Add("{");
        lines.Add("    public static int Covered()");
        lines.Add("    {");
        lines.Add("        return 1;");
        if (!targetClosesMethod)
        {
            lines.Add("    }");
        }
        lines.Add(targetLine);
        var targetLineNumber = lines.Count;
        lines.Add("}");
        return new SourceFixture(
            string.Join("\n", lines) + "\n",
            targetLineNumber,
            lines.IndexOf("        return 1;") + 1);
    }

    private static async Task WriteChangedLineFixtureAsync(
        string root,
        string repository,
        SourceFixture source)
    {
        var scripts = Path.Combine(repository, "scripts");
        var acceptance = Path.Combine(repository, "eng", "acceptance");
        var coverage = Path.Combine(repository, "coverage");
        var project = Path.Combine(repository, "Project");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(acceptance);
        Directory.CreateDirectory(coverage);
        Directory.CreateDirectory(project);

        await WriteCoverageProjectAsync(repository);

        CopyCoverageScripts(root, repository);
        await WriteInventoryRepositoryMetadataAsync(
            repository,
            approveTrustedSource: source.Text.Contains(
                "// <auto-generated />",
                StringComparison.Ordinal));
        await File.WriteAllTextAsync(
            Path.Combine(acceptance, "contract.json"),
            JsonSerializer.Serialize(new
            {
                trustedKernel = new { paths = s_trustedPaths },
                trustedComputingBase = new
                {
                    components = Array.Empty<object>()
                }
            }) + "\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "baseline.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                projects = new Dictionary<string, double>
                {
                    ["Project"] = 0
                },
                declarationOnlyTcbFiles = Array.Empty<string>(),
                minimumAggregateLinePercent = 0,
                minimumChangedTcbLinePercent = 100
            }) + "\n");
        await File.WriteAllTextAsync(
            Path.Combine(coverage, "fixture.cobertura.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<coverage><packages><package name=\"Project\"><classes>" +
            "<class name=\"Trusted\" filename=\"Project/Trusted.cs\">" +
            "<lines><line number=\"" + source.CoveredLine +
            "\" hits=\"1\" /></lines></class></classes></package>" +
            "</packages></coverage>\n");
        await File.WriteAllTextAsync(
            Path.Combine(project, "Trusted.cs"),
            source.Text);
    }

    private static async Task InitializeRepositoryAsync(string repository)
    {
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "init",
            "--object-format=sha1"));
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "config",
            "user.email",
            "coverage-script@example.invalid"));
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "config",
            "user.name",
            "Coverage Script Test"));
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "config",
            "core.autocrlf",
            "false"));
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "config",
            "core.quotePath",
            "true"));
    }

    private static async Task<string> CreateSingleCommitFixtureAsync()
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-coverage-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        await InitializeRepositoryAsync(repository);
        await WriteFixtureAsync(RepositoryRoot(), repository);
        await CommitAllAsync(repository, "root");
        return repository;
    }

    private static async Task<string> CreateMultiCommitFixtureAsync()
    {
        var repository = await CreateSingleCommitFixtureAsync();
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "branch",
            "comparison"));
        await WriteTrustedSourceAsync(repository, value: 1);
        await CommitAllAsync(repository, "trusted change");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "unrelated.txt"),
            "unrelated\n");
        await CommitAllAsync(repository, "unrelated tip");
        return repository;
    }

    private static async Task<ProcessResult> RunCoverageAsync(
        string repository,
        string? comparisonRef,
        bool reportOnly,
        bool includeWorkingTree = false)
    {
        await PrepareCoverageFixtureAsync(repository);
        return await RunCoverageScriptOnlyAsync(
            repository,
            comparisonRef,
            reportOnly,
            includeWorkingTree);
    }

    private static Task<ProcessResult> RunCoverageScriptOnlyAsync(
        string repository,
        string? comparisonRef,
        bool reportOnly,
        bool includeWorkingTree = false)
    {
        var arguments = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(
                repository,
                "scripts",
                "Test-SharpProofCoverage.ps1"),
            "-CoverageRoot",
            Path.Combine(repository, "coverage"),
            "-BaselinePath",
            Path.Combine(repository, "baseline.json")
        };
        if (comparisonRef != null)
        {
            arguments.Add("-ComparisonRef");
            arguments.Add(comparisonRef);
        }
        if (includeWorkingTree)
        {
            arguments.Add("-IncludeWorkingTree");
        }
        if (reportOnly)
        {
            arguments.Add("-ReportOnly");
        }

        return RunAsync(repository, "pwsh", [.. arguments]);
    }

    private static async Task WriteIdentityFixtureAsync(
        string root,
        string repository,
        IReadOnlyList<CoverageEntry> entries)
    {
        var scripts = Path.Combine(repository, "scripts");
        var acceptance = Path.Combine(repository, "eng", "acceptance");
        var coverage = Path.Combine(repository, "coverage");
        var project = Path.Combine(repository, "Project");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(acceptance);
        Directory.CreateDirectory(coverage);
        Directory.CreateDirectory(project);

        await WriteCoverageProjectAsync(repository);

        CopyCoverageScripts(root, repository);
        await WriteInventoryRepositoryMetadataAsync(repository);

        await File.WriteAllTextAsync(
            Path.Combine(acceptance, "contract.json"),
            JsonSerializer.Serialize(new
            {
                trustedKernel = new
                {
                    paths = entries.Select(static entry =>
                        entry.SourcePath).ToArray()
                },
                trustedComputingBase = new
                {
                    components = Array.Empty<object>()
                }
            }) + "\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "baseline.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                projects = new Dictionary<string, double>
                {
                    ["Project"] = 0
                },
                declarationOnlyTcbFiles = Array.Empty<string>(),
                minimumAggregateLinePercent = 0,
                minimumChangedTcbLinePercent = 100
            }) + "\n");

        var classes = entries
            .Where(static entry => entry.IncludeInReport)
            .Select((entry, index) =>
            new XElement(
                "class",
                new XAttribute("name", "Trusted" + index),
                new XAttribute("filename", entry.ReportPath),
                new XElement(
                    "lines",
                    new XElement(
                        "line",
                        new XAttribute("number", 3),
                        new XAttribute("hits", entry.Hits)))));
        var report = new XDocument(
            new XElement(
                "coverage",
                new XElement(
                    "packages",
                    new XElement(
                        "package",
                        new XAttribute("name", "Project"),
                        new XElement("classes", classes)))));
        await File.WriteAllTextAsync(
            Path.Combine(coverage, "fixture.cobertura.xml"),
            report.Declaration + "\n" +
            report.ToString(SaveOptions.DisableFormatting) + "\n");

        foreach (var entry in entries)
        {
            await WriteSourceAsync(repository, entry.SourcePath, value: 0);
        }
    }

    private static Task WriteSourceAsync(
        string repository,
        string relativePath,
        int value)
    {
        var path = Path.Combine(
            repository,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var className = "Trusted" + relativePath
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace('.', '_')
            .Replace('-', '_');
        return File.WriteAllTextAsync(
            path,
            "public static class " + className + "\n" +
            "{\n" +
            "    public static int Covered() => " + value + ";\n" +
            "}\n");
    }

    private static Task WriteCoverageProjectAsync(string repository)
    {
        return File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Project.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <SharpProofProductionProject>true</SharpProofProductionProject>\n" +
            "    <TargetFramework>net8.0</TargetFramework>\n" +
            "    <AssemblyName>Project</AssemblyName>\n" +
            "    <LangVersion>12.0</LangVersion>\n" +
            "    <DebugType>portable</DebugType>\n" +
            "    <DebugSymbols>true</DebugSymbols>\n" +
            "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"**/*.cs\" Exclude=\"bin/**/*.cs;obj/**/*.cs\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
    }

    private static void ApplyCoverageMutation(
        string reportPath,
        string mutation)
    {
        if (mutation == "truncated")
        {
            File.WriteAllText(reportPath, "<coverage><packages>");
            return;
        }
        if (mutation == "duplicate-report")
        {
            File.Copy(
                reportPath,
                Path.Combine(
                    Path.GetDirectoryName(reportPath)!,
                    "duplicate.cobertura.xml"));
            return;
        }

        var document = XDocument.Load(reportPath);
        var root = document.Root!;
        switch (mutation)
        {
            case "one-line":
            case "missing-project":
                root.Descendants("line").Remove();
                break;
            case "wrong-assembly":
                root.Element("sharpProofAuthority")!
                    .SetAttributeValue("modules", new string('0', 64));
                break;
            case "foreign-source":
                root.Descendants("class")
                    .First()
                    .SetAttributeValue("filename", "Foreign.cs");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        document.Save(reportPath, SaveOptions.DisableFormatting);
    }

    private static async Task PrepareCoverageFixtureAsync(string repository)
    {
        await AssertSuccessAsync(RunAsync(
            repository,
            "dotnet",
            "restore",
            "Project/Project.csproj",
            "--ignore-failed-sources"));
        await AssertSuccessAsync(RunAsync(
            repository,
            "dotnet",
            "build",
            "Project/Project.csproj",
            "--configuration",
            "Release",
            "--no-restore"));
        var coverage = Path.Combine(repository, "coverage");
        var authorityPath = Path.Combine(coverage, "coverage-authority.json");
        await AssertSuccessAsync(RunAsync(
            repository,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(repository, "scripts", "Get-SharpProofProductionInventory.ps1"),
            "-RepositoryRoot",
            repository,
            "-Configuration",
            "Release",
            "-RequirePdb",
            "-OutputPath",
            authorityPath));

        using var authority = JsonDocument.Parse(
            await File.ReadAllTextAsync(authorityPath));
        var moduleHashes = authority.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Select(static module => module
                .GetProperty("assemblySha256")
                .GetString()!)
            .OrderBy(static hash => hash, StringComparer.Ordinal)
            .ToArray();
        var sourceHits = new Dictionary<string, Dictionary<int, int>>(
            StringComparer.Ordinal);
        foreach (var reportPath in Directory.EnumerateFiles(
                     coverage,
                     "*.cobertura.xml",
                     SearchOption.AllDirectories))
        {
            var document = XDocument.Load(reportPath, LoadOptions.PreserveWhitespace);
            var root = document.Root!;
            foreach (var existingClass in root.Descendants("class"))
            {
                var existingPath = existingClass
                    .Attribute("filename")?
                    .Value
                    .Replace('\\', '/');
                if (existingPath != null)
                {
                    if (!sourceHits.TryGetValue(existingPath, out var lines))
                    {
                        lines = new Dictionary<int, int>();
                        sourceHits[existingPath] = lines;
                    }
                    foreach (var existingLine in existingClass
                                 .Descendants("line"))
                    {
                        if (int.TryParse(
                                existingLine.Attribute("number")?.Value,
                                out var lineNumber) &&
                            int.TryParse(
                                existingLine.Attribute("hits")?.Value,
                                out var existingHits))
                        {
                            lines[lineNumber] = existingHits;
                        }
                    }
                }
            }
            var classesContainer = root.Descendants("classes").First();
            classesContainer.RemoveNodes();
            root.Descendants("sharpProofAuthority").Remove();
            var sequenceClassOrdinal = 0;
            foreach (var module in authority.RootElement
                         .GetProperty("modules")
                         .EnumerateArray())
            {
                foreach (var sourceDocument in module
                             .GetProperty("documents")
                             .EnumerateArray())
                {
                    var sourcePath = sourceDocument
                        .GetProperty("path")
                        .GetString()!;
                    var lines = sourceDocument
                        .GetProperty("sequencePoints")
                        .EnumerateArray()
                        .Select(line =>
                        {
                            var lineNumber = line.GetInt32();
                            var hits = sourceHits.TryGetValue(
                                    sourcePath,
                                    out var configuredLines) &&
                                configuredLines.TryGetValue(
                                    lineNumber,
                                    out var configuredHits)
                                ? configuredHits
                                : 0;
                            return new XElement(
                                "line",
                                new XAttribute("number", lineNumber),
                                new XAttribute("hits", hits));
                        });
                    classesContainer.Add(new XElement(
                        "class",
                        new XAttribute(
                            "name",
                            "Trusted" + sequenceClassOrdinal++),
                        new XAttribute("filename", sourcePath),
                        new XElement("lines", lines)));
                }
            }
            root.Add(new XElement(
                "sharpProofAuthority",
                new XAttribute("schemaVersion", "1"),
                new XAttribute(
                    "commit",
                    authority.RootElement.GetProperty("commit").GetString()!),
                new XAttribute(
                    "sourceUniverseSha256",
                    authority.RootElement
                        .GetProperty("sourceUniverseSha256")
                        .GetString()!),
                new XAttribute(
                    "universeSha256",
                    authority.RootElement
                        .GetProperty("pdbUniverseSha256")
                        .GetString()!),
                new XAttribute(
                    "generatedManifestSha256",
                    authority.RootElement
                        .GetProperty("generatedManifestSha256")
                        .GetString()!),
                new XAttribute("modules", string.Join(',', moduleHashes))));
            document.Save(reportPath, SaveOptions.DisableFormatting);
        }
    }

    private static void RequireLinuxFileNames()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore(
                "Coverage path identity is qualified in the canonical Linux container.");
        }
    }

    private static async Task WriteFixtureAsync(
        string root,
        string repository)
    {
        var scripts = Path.Combine(repository, "scripts");
        var acceptance = Path.Combine(repository, "eng", "acceptance");
        var coverage = Path.Combine(repository, "coverage");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(acceptance);
        Directory.CreateDirectory(coverage);
        Directory.CreateDirectory(Path.Combine(repository, "Project"));

        await WriteCoverageProjectAsync(repository);

        CopyCoverageScripts(root, repository);
        await WriteInventoryRepositoryMetadataAsync(repository);

        await File.WriteAllTextAsync(
            Path.Combine(acceptance, "contract.json"),
            JsonSerializer.Serialize(new
            {
                trustedKernel = new
                {
                    paths = s_trustedPaths
                },
                trustedComputingBase = new
                {
                    components = Array.Empty<object>()
                }
            }) + "\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "baseline.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                projects = new Dictionary<string, double>
                {
                    ["Project"] = 0
                },
                declarationOnlyTcbFiles = Array.Empty<string>(),
                minimumAggregateLinePercent = 0,
                minimumChangedTcbLinePercent = 0
            }) + "\n");
        await File.WriteAllTextAsync(
            Path.Combine(coverage, "fixture.cobertura.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="Project">
                  <classes>
                    <class name="Trusted" filename="Project/Trusted.cs">
                      <lines>
                        <line number="3" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """ + "\n");
        await WriteTrustedSourceAsync(repository, value: 0);
    }

    private static Task WriteTrustedSourceAsync(
        string repository,
        int value)
    {
        return File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Trusted.cs"),
            "public static class Trusted\n" +
            "{\n" +
            "    public static int Covered() => " + value + ";\n" +
            "}\n");
    }

    private static void CopyCoverageScripts(string root, string repository)
    {
        var scripts = Path.Combine(repository, "scripts");
        File.Copy(
            Path.Combine(root, "scripts", "Test-SharpProofCoverage.ps1"),
            Path.Combine(scripts, "Test-SharpProofCoverage.ps1"));
        File.Copy(
            Path.Combine(root, "scripts", "Get-SharpProofTcbPaths.ps1"),
            Path.Combine(scripts, "Get-SharpProofTcbPaths.ps1"));
        File.Copy(
            Path.Combine(root, "scripts", "Get-SharpProofProductionInventory.ps1"),
            Path.Combine(scripts, "Get-SharpProofProductionInventory.ps1"));
    }

    private static async Task WriteInventoryRepositoryMetadataAsync(
        string repository,
        bool approveTrustedSource = false)
    {
        Directory.CreateDirectory(Path.Combine(repository, "eng", "coverage"));
        Directory.CreateDirectory(Path.Combine(repository, "eng", "generated"));
        await File.WriteAllTextAsync(
            Path.Combine(repository, "SharpProof.sln"),
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = " +
            "\"Project\", \"Project/Project.csproj\", " +
            "\"{11111111-1111-1111-1111-111111111111}\"\n" +
            "EndProject\n" +
            "Global\n" +
            "EndGlobal\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "eng", "coverage", "SharpProof.Gates.runsettings"),
            "<RunSettings />\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "eng", "generated", "approved-outputs.v1.json"),
            approveTrustedSource
                ? "{\"schemaVersion\":1,\"outputs\":[\"Project/Trusted.cs\"]}\n"
                : "{\"schemaVersion\":1,\"outputs\":[]}\n");
    }

    private static async Task CommitAllAsync(
        string repository,
        string message)
    {
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "add",
            "--",
            "."));
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "commit",
            "-m",
            message));
    }

    private static async Task<ProcessResult> AssertSuccessAsync(
        Task<ProcessResult> operation)
    {
        var result = await operation;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        return result;
    }

    private static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await output,
            await error);
    }

    private static void DeleteTemporaryRepository(string repository)
    {
        if (!Directory.Exists(repository))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
            repository,
            "*",
            SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        Directory.Delete(repository, recursive: true);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not find the repository root.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed record CoverageEntry(
        string SourcePath,
        string ReportPath,
        int Hits,
        bool IncludeInReport = true);

    private sealed record SourceFixture(
        string Text,
        int TargetLine,
        int CoveredLine);

    private sealed record ChangedLineResult(
        ProcessResult Process,
        int ChangedLine);
}

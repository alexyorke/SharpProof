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

    [Test]
    public async Task CaseDistinctTcbPathsKeepIndependentCoverage()
    {
        RequireLinuxFileNames();
        var upper = "Project/Trusted.cs";
        var lower = "Project/trusted.cs";
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
        "Project/Trüsted\tCase.cs",
        "Project/Trüsted\tCase.cs",
        TestName = "GitQuotedUnicodeAndControlPathIsDecoded")]
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

    private static async Task WriteIdentityFixtureAsync(
        string root,
        string repository,
        IReadOnlyList<CoverageEntry> entries)
    {
        var scripts = Path.Combine(repository, "scripts");
        var acceptance = Path.Combine(repository, "eng", "acceptance");
        var coverage = Path.Combine(repository, "coverage");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(acceptance);
        Directory.CreateDirectory(coverage);

        File.Copy(
            Path.Combine(root, "scripts", "Test-SharpProofCoverage.ps1"),
            Path.Combine(scripts, "Test-SharpProofCoverage.ps1"));
        File.Copy(
            Path.Combine(root, "scripts", "Get-SharpProofTcbPaths.ps1"),
            Path.Combine(scripts, "Get-SharpProofTcbPaths.ps1"));

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

        var classes = entries.Select((entry, index) =>
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
        return File.WriteAllTextAsync(
            path,
            "public static class Trusted\n" +
            "{\n" +
            "    public const int Value = " + value + ";\n" +
            "}\n");
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

        File.Copy(
            Path.Combine(root, "scripts", "Test-SharpProofCoverage.ps1"),
            Path.Combine(scripts, "Test-SharpProofCoverage.ps1"));
        File.Copy(
            Path.Combine(root, "scripts", "Get-SharpProofTcbPaths.ps1"),
            Path.Combine(scripts, "Get-SharpProofTcbPaths.ps1"));

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
            "    public const int Value = " + value + ";\n" +
            "}\n");
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
        int Hits);
}

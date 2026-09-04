using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class DependencyAuditScriptTests
{
    private const string AuditSource =
        "https://api.nuget.org/v3/index.json";
    private static readonly string[] ExpectedAuditSources = [
        AuditSource
    ];
    private static readonly string[] ExpectedProjects = [
        "Alpha/Alpha.csproj",
        "Beta/Beta.csproj"
    ];

    [Test]
    public async Task CleanReportProducesDeterministicEvidence()
    {
        using var workspace = DependencyAuditWorkspace.Create();
        var first = await workspace.RunAsync(workspace.CreateCleanReport());
        Assert.That(first.ExitCode, Is.Zero, first.Output);
        var firstEvidence = await File.ReadAllBytesAsync(
            workspace.OutputPath);

        var second = await workspace.RunAsync(workspace.CreateCleanReport());
        Assert.That(second.ExitCode, Is.Zero, second.Output);
        var secondEvidence = await File.ReadAllBytesAsync(
            workspace.OutputPath);

        Assert.That(secondEvidence, Is.EqualTo(firstEvidence));
        using var document = JsonDocument.Parse(secondEvidence);
        var root = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                root.GetProperty("schemaVersion").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                root.GetProperty("gate").GetString(),
                Is.EqualTo("dependencyAudit"));
            Assert.That(
                root.GetProperty("passed").GetBoolean(),
                Is.True);
            Assert.That(
                root.GetProperty("auditSources")
                    .EnumerateArray()
                    .Select(static source => source.GetString()),
                Is.EqualTo(ExpectedAuditSources));
            Assert.That(
                root.GetProperty("projects")
                    .EnumerateArray()
                    .Select(static project => project.GetString()),
                Is.EqualTo(ExpectedProjects));
            Assert.That(
                root.GetProperty("counts")
                    .GetProperty("projects")
                    .GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                Encoding.UTF8.GetString(secondEvidence),
                Does.EndWith("\n"));
        }
    }

    [TestCase("warning")]
    [TestCase("error")]
    public async Task EveryNuGetProblemIsFatal(string level)
    {
        using var workspace = DependencyAuditWorkspace.Create();
        var report = workspace.CreateCleanReport();
        report["problems"] = new JsonArray(
            new JsonObject
            {
                ["level"] = level,
                ["text"] = "Audit data was unavailable."
            });

        await workspace.AssertRejectedAsync(
            report,
            "Dependency audit report contains problems");
    }

    [TestCase("topLevelPackages")]
    [TestCase("transitivePackages")]
    public async Task EveryReportedVulnerablePackageIsFatal(
        string packageKind)
    {
        using var workspace = DependencyAuditWorkspace.Create();
        var report = workspace.CreateCleanReport();
        var framework = report["projects"]![0]!["frameworks"]![0]!
            .AsObject();
        framework[packageKind] = new JsonArray(
            new JsonObject
            {
                ["id"] = "Vulnerable.Package",
                ["resolvedVersion"] = "1.2.3",
                ["vulnerabilities"] = new JsonArray(
                    new JsonObject
                    {
                        ["severity"] = "high",
                        ["advisoryurl"] =
                            "https://example.invalid/advisory"
                    })
            });

        await workspace.AssertRejectedAsync(
            report,
            "Vulnerable.Package@1.2.3");
    }

    [Test]
    public async Task AuditSourceSetMustBeExactAndUnique()
    {
        var sourceSets = new[]
        {
            Array.Empty<string>(),
            new[] { AuditSource, AuditSource },
            new[] { AuditSource, "https://example.invalid/v3/index.json" }
        };
        foreach (var sourceSet in sourceSets)
        {
            using var workspace = DependencyAuditWorkspace.Create();
            var report = workspace.CreateCleanReport();
            report["sources"] = new JsonArray(
                sourceSet
                    .Select(static source => JsonValue.Create(source))
                    .ToArray());

            var result = await workspace.RunAsync(report);
            Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
            Assert.That(
                File.Exists(workspace.OutputPath),
                Is.False,
                result.Output);
        }
    }

    [Test]
    public async Task ProjectCoverageMustBeExactAndUnique()
    {
        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["projects"]!.AsArray().RemoveAt(0);
            await workspace.AssertRejectedAsync(
                report,
                "project coverage is incomplete or invented");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            var projects = report["projects"]!.AsArray();
            projects.Add(projects[0]!.DeepClone());
            await workspace.AssertRejectedAsync(
                report,
                "duplicates project");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["projects"]!.AsArray().Add(
                DependencyAuditWorkspace.Project(
                    Path.Combine(workspace.Root, "Invented.csproj")));
            await workspace.AssertRejectedAsync(
                report,
                "project coverage is incomplete or invented");
        }
    }

    [Test]
    public async Task SchemaAndInvocationParametersMustMatch()
    {
        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["version"] = 2;
            await workspace.AssertRejectedAsync(
                report,
                "does not use JSON schema version 1");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["parameters"] = "--vulnerable";
            await workspace.AssertRejectedAsync(
                report,
                "unexpected parameters");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            await workspace.AssertRawRejectedAsync(
                "{ definitely-not-json",
                "not valid JSON");
        }
    }

    [Test]
    public async Task EveryProjectMustContainAUsableFrameworkInventory()
    {
        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["projects"]![0]!.AsObject().Remove("frameworks");
            await workspace.AssertRejectedAsync(
                report,
                "is missing 'frameworks'");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["projects"]![0]!["frameworks"] = new JsonArray();
            await workspace.AssertRejectedAsync(
                report,
                "has an empty 'frameworks' array");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            report["projects"]![0]!["frameworks"]![0]!["framework"] = " ";
            await workspace.AssertRejectedAsync(
                report,
                "contains an empty framework name");
        }

        using (var workspace = DependencyAuditWorkspace.Create())
        {
            var report = workspace.CreateCleanReport();
            var frameworks = report["projects"]![0]!["frameworks"]!
                .AsArray();
            frameworks.Add(frameworks[0]!.DeepClone());
            await workspace.AssertRejectedAsync(
                report,
                "duplicates framework");
        }
    }

    [Test]
    public async Task EveryFrameworkMustContainTopLevelPackageInventory()
    {
        using var workspace = DependencyAuditWorkspace.Create();
        var report = workspace.CreateCleanReport();
        report["projects"]![0]!["frameworks"]![0]!
            .AsObject()
            .Remove("topLevelPackages");

        await workspace.AssertRejectedAsync(
            report,
            "is missing 'topLevelPackages'");
    }

    [Test]
    public async Task EmptyTransitivePackageInventoryMayBeOmitted()
    {
        using var workspace = DependencyAuditWorkspace.Create();
        var report = workspace.CreateCleanReport();
        report["projects"]![0]!["frameworks"]![0]!
            .AsObject()
            .Remove("transitivePackages");

        var result = await workspace.RunAsync(report);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
    }

    [Test]
    public async Task AuditConfigurationMustBeExplicitAndHermetic()
    {
        using var workspace = DependencyAuditWorkspace.Create();
        await File.WriteAllTextAsync(
            workspace.ConfigurationPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org"
                     value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        await workspace.AssertRejectedAsync(
            workspace.CreateCleanReport(),
            "requires an explicit <auditSources>");
    }

    [Test]
    public void RepositoryAuditSourceIsExplicitAndHermetic()
    {
        var root = TestRepository.FindRoot();
        var configuration = XDocument.Load(
            Path.Combine(root, "NuGet.Config"));
        var auditSources = configuration
            .Descendants("auditSources")
            .Single();
        var source = auditSources.Elements("add").Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                auditSources.Elements("clear"),
                Has.Exactly(1).Items);
            Assert.That(
                (string?)source.Attribute("value"),
                Is.EqualTo(AuditSource));
        }
    }

    [Test]
    public async Task OutputCannotEscapeOrOverwriteAuditInputs()
    {
        using var workspace = DependencyAuditWorkspace.Create();
        var outsidePath = Path.Combine(
            Directory.GetParent(workspace.Root)!.FullName,
            "outside-" + Guid.NewGuid().ToString("N") + ".json");
        foreach (var outputPath in new[]
                 {
                     workspace.SolutionPath,
                     workspace.ConfigurationPath,
                     outsidePath
                 })
        {
            var result = await workspace.RunWithOutputAsync(
                workspace.CreateCleanReport(),
                outputPath);
            Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(workspace.SolutionPath), Is.True);
            Assert.That(File.Exists(workspace.ConfigurationPath), Is.True);
            Assert.That(File.Exists(outsidePath), Is.False);
        }
    }

    private sealed class DependencyAuditWorkspace : IDisposable
    {
        private readonly string _expectedParent;

        private DependencyAuditWorkspace(
            string root,
            string expectedParent)
        {
            Root = root;
            _expectedParent = expectedParent;
            SolutionPath = Path.Combine(root, "Fixture.sln");
            ConfigurationPath = Path.Combine(root, "NuGet.Config");
            ReportPath = Path.Combine(root, "report.json");
            OutputPath = Path.Combine(root, "evidence.json");
            ProjectPaths = new[]
            {
                Path.Combine(root, "Alpha", "Alpha.csproj"),
                Path.Combine(root, "Beta", "Beta.csproj")
            };
        }

        internal string Root
        {
            get;
        }

        internal string SolutionPath
        {
            get;
        }

        internal string ConfigurationPath
        {
            get;
        }

        internal string ReportPath
        {
            get;
        }

        internal string OutputPath
        {
            get;
        }

        private string[] ProjectPaths
        {
            get;
        }

        internal static DependencyAuditWorkspace Create()
        {
            var parent = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.DependencyAuditTests"));
            Directory.CreateDirectory(parent);
            var root = Path.Combine(
                parent,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var workspace = new DependencyAuditWorkspace(root, parent);
            workspace.Initialize();
            return workspace;
        }

        internal JsonObject CreateCleanReport()
        {
            return new JsonObject
            {
                ["version"] = 1,
                ["parameters"] =
                    "--vulnerable --include-transitive",
                ["sources"] = new JsonArray(
                    JsonValue.Create(AuditSource)),
                ["projects"] = new JsonArray(
                    ProjectPaths.Select(Project).ToArray())
            };
        }

        internal static JsonObject Project(string path)
        {
            return new JsonObject
            {
                ["path"] = Path.GetFullPath(path),
                ["frameworks"] = new JsonArray(
                    new JsonObject
                    {
                        ["framework"] = "net9.0",
                        ["topLevelPackages"] = new JsonArray(),
                        ["transitivePackages"] = new JsonArray()
                    })
            };
        }

        internal async Task<ProcessResult> RunAsync(JsonObject report)
        {
            return await RunReportAsync(
                report,
                OutputPath,
                writeIndented: true,
                createStaleOutput: true);
        }

        internal async Task<ProcessResult> RunWithOutputAsync(
            JsonObject report,
            string outputPath)
        {
            return await RunReportAsync(
                report,
                outputPath,
                writeIndented: false,
                createStaleOutput: false);
        }

        internal async Task AssertRejectedAsync(
            JsonObject report,
            string expectedMessage)
        {
            await AssertRejectedAsync(
                () => RunAsync(report),
                expectedMessage);
        }

        internal async Task AssertRawRejectedAsync(
            string report,
            string expectedMessage)
        {
            await AssertRejectedAsync(
                async () =>
                {
                    await File.WriteAllTextAsync(ReportPath, report);
                    return await RunScriptAsync(
                        OutputPath,
                        createStaleOutput: true);
                },
                expectedMessage);
        }

        private async Task AssertRejectedAsync(
            Func<Task<ProcessResult>> run,
            string expectedMessage)
        {
            var result = await run();
            Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
            Assert.That(
                result.Output,
                Does.Contain(expectedMessage),
                result.Output);
            Assert.That(
                File.Exists(OutputPath),
                Is.False,
                result.Output);
        }

        private async Task<ProcessResult> RunReportAsync(
            JsonObject report,
            string outputPath,
            bool writeIndented,
            bool createStaleOutput)
        {
            var options = writeIndented
                ? new JsonSerializerOptions { WriteIndented = true }
                : null;
            await File.WriteAllTextAsync(
                ReportPath,
                report.ToJsonString(options));
            return await RunScriptAsync(outputPath, createStaleOutput);
        }

        public void Dispose()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                Root,
                Path.GetFileName(_expectedParent),
                "Refusing to remove an unexpected audit directory.");
        }

        private void Initialize()
        {
            const string projectType =
                "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
            var alphaId = Guid.NewGuid().ToString("B").ToUpperInvariant();
            var betaId = Guid.NewGuid().ToString("B").ToUpperInvariant();
            File.WriteAllText(
                SolutionPath,
                $"""
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{projectType}") = "Alpha", "Alpha\Alpha.csproj", "{alphaId}"
                EndProject
                Project("{projectType}") = "Beta", "Beta\Beta.csproj", "{betaId}"
                EndProject
                Global
                EndGlobal
                """);
            foreach (var projectPath in ProjectPaths)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(projectPath)!);
                File.WriteAllText(
                    projectPath,
                    "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
            }
            File.WriteAllText(
                ConfigurationPath,
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="nuget.org"
                         value="{AuditSource}" />
                  </packageSources>
                  <auditSources>
                    <clear />
                    <add key="nuget.org"
                         value="{AuditSource}" />
                  </auditSources>
                </configuration>
                """);
        }

        private async Task<ProcessResult> RunScriptAsync(
            string outputPath,
            bool createStaleOutput)
        {
            if (createStaleOutput)
            {
                await File.WriteAllTextAsync(
                    outputPath,
                    "stale evidence");
            }
            return await RunProcessAsync(
                TestRepository.FindRoot(),
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(
                    TestRepository.FindRoot(),
                    "scripts",
                    "Test-SharpProofDependencyAudit.ps1"),
                "-SolutionPath",
                SolutionPath,
                "-NuGetConfigurationPath",
                ConfigurationPath,
                "-ReportPath",
                ReportPath,
                "-OutputPath",
                outputPath);
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var result = await ProcessRunner.RunCapturedAsync(
            workingDirectory,
            fileName,
            arguments);
        return new ProcessResult(
            result.ExitCode,
            result.Output + Environment.NewLine + result.Error);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}

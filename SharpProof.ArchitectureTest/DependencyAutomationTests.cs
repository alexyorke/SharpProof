using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class DependencyAutomationTests
{
    [Test]
    public void DependabotKeepsCompilerDependenciesOnPatchUpdates()
    {
        var configuration = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "dependabot.yml"));
        var common = IgnoreBlock(
            configuration,
            "Microsoft.CodeAnalysis.Common");
        var csharp = IgnoreBlock(
            configuration,
            "Microsoft.CodeAnalysis.CSharp");

        using (Assert.EnterMultipleScope())
        {
            AssertCompilerCeiling(common);
            AssertCompilerCeiling(csharp);
        }
    }

    [Test]
    public void ReusableSecurityFailsClosedOnDependencyAuditEvidence()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "security-reusable.yml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                workflow,
                Does.Contain(
                    "docker compose run --rm tooling dependency-audit"));
            Assert.That(
                workflow,
                Does.Contain(
                    "artifacts/dependency-audit/dependency-audit.json"));
            Assert.That(
                workflow,
                Does.Contain(
                    "security-dependency-audit-${{ github.sha }}-" +
                    "${{ github.run_attempt }}"));
            Assert.That(
                workflow,
                Does.Not.Contain(
                    "list SharpProof.sln package\n" +
                    "          --vulnerable"));
        }
    }

    [Test]
    public void PackageWorkflowRunsThePackageBackedSampleMatrix()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "package-consumers.yml"));

        Assert.That(
            workflow,
            Does.Contain(
                "docker compose run --rm tooling samples"));
        Assert.That(workflow, Does.Contain("-PackageSource nupkgs"));
    }

    [Test]
    public void SamplePackageBuildsUseIsolatedPaths()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Test-SharpProofSamples.ps1"));
        var start = script.IndexOf(
            "function New-LocalPackageFeed",
            StringComparison.Ordinal);
        var end = script.IndexOf(
            "function Invoke-SampleBuild",
            start,
            StringComparison.Ordinal);
        var section = start >= 0 && end > start
            ? script[start..end]
            : string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            Assert.That(section, Does.Contain("$packRepository"));
            Assert.That(section, Does.Contain("$packArchive"));
            Assert.That(section, Does.Contain("/bin/tar"));
            Assert.That(section, Does.Contain("$sourceOrigin"));
            Assert.That(section, Does.Contain("remote set-url origin"));
            Assert.That(section, Does.Contain("$temporaryRoot"));
            Assert.That(section, Does.Contain("'--packages'"));
            Assert.That(section, Does.Contain("$packageCache"));
        }
    }

    [Test]
    public void FuzzCampaignPublishesFailedSummaryBeforeThrowing()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Invoke-SharpProofFuzzCampaign.ps1"));
        var failure = script.IndexOf(
            "if (-not $summary.passed)",
            StringComparison.Ordinal);
        var publish = script.LastIndexOf(
            "Publish-SharpProofFuzzEvidence",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.GreaterThanOrEqualTo(0));
            Assert.That(publish, Is.LessThan(failure));
        }
    }

    [Test]
    public void NightlyFailsClosedOnRetainedDependencyAuditEvidence()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "nightly.yml"));
        var uploadIndex = workflow.IndexOf(
            "- name: Upload nightly evidence",
            StringComparison.Ordinal);
        var upload = uploadIndex >= 0
            ? workflow[uploadIndex..]
            : string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                workflow,
                Does.Contain(
                    "docker compose run --rm tooling dependency-audit"));
            Assert.That(
                workflow,
                Does.Not.Contain(
                    "list SharpProof.sln package"));
            Assert.That(
                workflow,
                Does.Not.Contain("--vulnerable"));
            Assert.That(uploadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(upload, Does.Contain("path: artifacts"));
        }
    }

    [Test]
    public void ArchitectureDocumentsCollectorSplitAndCorpusRatchets()
    {
        var architecture = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "docs",
            "architecture.md"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                architecture,
                Does.Contain(
                    "CompilerArtifact      -> Ir, Worker.Protocol"));
            Assert.That(
                architecture,
                Does.Contain(
                    "Analyzer.Core         -> Contracts, Effects, " +
                    "Frontend, Ir, Specs"));
            Assert.That(
                architecture,
                Does.Contain("Analyzer              -> Analyzer.Core"));
            Assert.That(
                architecture,
                Does.Contain(
                    "CompilerCollector     -> Analyzer.Core, CompilerArtifact, " +
                    "Contracts, Effects,"));
            Assert.That(
                architecture,
                Does.Contain(
                    "ContractForGenerator  -> Analyzer.Core, Contracts"));
            Assert.That(
                architecture,
                Does.Contain("BuildTasks            -> Host, Worker.Protocol"));
            Assert.That(
                architecture,
                Does.Contain(
                    "ordinary live analyzer has no static dependency on " +
                    "the\ncompiler-artifact model or worker protocol"));
            Assert.That(
                architecture,
                Does.Contain(
                    "build-only compiler collector observes the\nfinal " +
                    "post-generator Roslyn `Compilation`"));
            Assert.That(
                architecture,
                Does.Contain(
                    "A `Supported` case producing `Unknown` or\n" +
                    "`SilentUnknown` fails with zero tolerance."));
            Assert.That(
                architecture,
                Does.Contain(
                    "per-reason Unknown counts\nfor " +
                    "`IntentionallyUnsupported` cases cannot exceed the " +
                    "checked-in ratchet."));
            Assert.That(
                architecture,
                Does.Not.Contain(
                    "semantic Unknown rates as metrics; none is a " +
                    "release gate."));
            Assert.That(
                architecture,
                Does.Contain("compiler artifact schema 15"));
            Assert.That(
                architecture,
                Does.Not.Contain("Schema 11 retains"));
        }
    }

    [Test]
    public void RepositorySecurityKeepsCodeQlDisabled()
    {
        var workflowDirectory = Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows");
        var workflows = Directory.EnumerateFiles(workflowDirectory)
            .Where(static path =>
                string.Equals(
                    Path.GetExtension(path),
                    ".yml",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetExtension(path),
                    ".yaml",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(workflows, Is.Not.Empty);

        foreach (var workflowPath in workflows)
        {
            var workflowName = Path.GetFileName(workflowPath);
            var workflow = File.ReadAllText(workflowPath).ToUpperInvariant();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    workflow,
                    Does.Not.Contain("GITHUB/CODEQL-ACTION"),
                    workflowName);
                Assert.That(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        workflow,
                        @"(?m)^\s*SECURITY-EVENTS\s*:\s*WRITE\s*(?:#.*)?$"),
                    Is.False,
                    workflowName);
            }
        }
    }

    [Test]
    public void RepositorySecurityPinsExternalWorkflowActionsToImmutableShas()
    {
        var workflowDirectory = Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows");
        var references = Directory.EnumerateFiles(workflowDirectory)
            .Where(static path =>
                string.Equals(
                    Path.GetExtension(path),
                    ".yml",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetExtension(path),
                    ".yaml",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new
                {
                    Path = path,
                    Line = line,
                    Number = index + 1
                }))
            .Where(static entry => entry.Line.TrimStart().StartsWith(
                "uses:",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(references, Is.Not.Empty);
        using (Assert.EnterMultipleScope())
        {
            foreach (var reference in references)
            {
                var value = reference.Line.TrimStart()["uses:".Length..]
                    .Trim();
                if (value.StartsWith("./", StringComparison.Ordinal))
                {
                    continue;
                }

                var at = value.LastIndexOf('@');
                Assert.That(
                    at,
                    Is.GreaterThan(0),
                    $"{reference.Path}:{reference.Number}");
                if (at <= 0)
                {
                    continue;
                }

                var revision = value[(at + 1)..].Split('#')[0].Trim();
                Assert.That(
                    revision,
                    Does.Match("^[0-9a-fA-F]{40}$"),
                    $"{reference.Path}:{reference.Number}");
            }
        }
    }

    [Test]
    public void RepositoryWorkflowsUsePinnedContainerAndPortableSdks()
    {
        var root = RepositoryRoot();
        var workflowDirectory = Path.Combine(root, ".github", "workflows");
        var workflows = string.Join("\n", Directory
            .EnumerateFiles(workflowDirectory)
            .Where(static path =>
                Path.GetExtension(path) is ".yml" or ".yaml")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText));
        using var toolchain = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "container",
            "toolchain.json")));
        var dotnet = toolchain.RootElement.GetProperty("dotnet");
        var dockerfile = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "container",
            "Dockerfile"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                dotnet.GetProperty("sdkVersion").GetString(),
                Is.EqualTo("9.0.316"));
            Assert.That(
                dotnet.GetProperty("minimumSdkVersion").GetString(),
                Is.EqualTo("9.0.300"));
            Assert.That(workflows, Does.Contain("actions/setup-dotnet@"));
            Assert.That(workflows, Does.Contain("dotnet-version: 9.0.316"));
            Assert.That(
                workflows,
                Does.Contain("uses: ./.github/actions/build-tooling"));
            Assert.That(dockerfile, Does.Contain("DOTNET_SDK_IMAGE="));
            Assert.That(dockerfile, Does.Contain("DOTNET_MINIMUM_SDK_IMAGE="));
            Assert.That(
                dockerfile,
                Does.Contain("DOTNET_MINIMUM_FRAMEWORK_IMAGE="));
        }
    }

    private static void AssertCompilerCeiling(string block)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(block, Does.Contain("versions:"));
            Assert.That(block, Does.Contain("- \">= 4.15.0\""));
            Assert.That(
                block,
                Does.Contain("- \"version-update:semver-minor\""));
            Assert.That(
                block,
                Does.Contain("- \"version-update:semver-major\""));
        }
    }

    private static string IgnoreBlock(
        string configuration,
        string dependencyName)
    {
        var lines = configuration.Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Split('\n');
        var marker = $"- dependency-name: \"{dependencyName}\"";
        var starts = lines
            .Select((line, index) => new
            {
                Line = line,
                Index = index
            })
            .Where(candidate =>
                candidate.Line.TrimStart()
                    .Equals(marker, StringComparison.Ordinal))
            .ToArray();
        Assert.That(
            starts,
            Has.Length.EqualTo(1),
            $"Expected one ignore block for {dependencyName}.");

        var start = starts[0].Index;
        var indentation = lines[start].Length -
            lines[start].TrimStart().Length;
        var end = lines.Length;
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var currentIndentation = line.Length -
                line.TrimStart().Length;
            if (currentIndentation <= indentation)
            {
                end = index;
                break;
            }
        }
        return string.Join('\n', lines[start..end]);
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
}

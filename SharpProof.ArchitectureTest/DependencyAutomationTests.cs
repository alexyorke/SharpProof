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
                    @".\scripts\Test-SharpProofDependencyAudit.ps1"));
            Assert.That(
                workflow,
                Does.Contain(
                    "-OutputPath artifacts/security/dependency-audit.json"));
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
                    @".\scripts\Test-SharpProofDependencyAudit.ps1"));
            Assert.That(
                workflow,
                Does.Contain("-SolutionPath SharpProof.sln"));
            Assert.That(
                workflow,
                Does.Contain(
                    "-NuGetConfigurationPath NuGet.Config"));
            Assert.That(
                workflow,
                Does.Contain(
                    "-OutputPath " +
                    "artifacts/nightly/dependency-audit.json"));
            Assert.That(
                workflow,
                Does.Not.Contain(
                    "list SharpProof.sln package"));
            Assert.That(
                workflow,
                Does.Not.Contain("--vulnerable"));
            Assert.That(uploadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                upload,
                Does.Contain("artifacts/nightly"));
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
                    "Analyzer              -> Contracts, Effects, " +
                    "Frontend, Ir, Specs"));
            Assert.That(
                architecture,
                Does.Contain(
                    "CompilerCollector     -> Analyzer, CompilerArtifact, " +
                    "Contracts, Effects,"));
            Assert.That(
                architecture,
                Does.Contain(
                    "PortableAnalyzer      -> Attributes " +
                    "(build-only payload identity)"));
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

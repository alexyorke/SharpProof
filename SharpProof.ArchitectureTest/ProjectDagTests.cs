using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ProjectDagTests
{
    // Every production project must appear here; adding a project or a
    // reference is an explicit architecture decision.
    private static readonly Dictionary<string, string[]> AllowedReferences =
        new(StringComparer.Ordinal)
        {
            ["SharpProof.Analyzer"] = ["SharpProof.Analyzer.Core"],
            ["SharpProof.Analyzer.Core"] = ["SharpProof.Contracts", "SharpProof.Effects"],
            ["SharpProof.Attributes"] = [],
            ["SharpProof.BuildTasks"] = ["SharpProof.Host", "SharpProof.Worker.Protocol"],
            ["SharpProof.CompilerArtifact"] = ["SharpProof.Ir", "SharpProof.Worker.Protocol"],
            ["SharpProof.CompilerCollector"] = [
                "SharpProof.Analyzer.Core",
                "SharpProof.CompilerArtifact",
                "SharpProof.Summaries"
            ],
            ["SharpProof.ContractForGenerator"] = [],
            ["SharpProof.Contracts"] = ["SharpProof.Frontend"],
            ["SharpProof.Dataflow"] = [],
            ["SharpProof.Effects"] = ["SharpProof.Dataflow", "SharpProof.Frontend", "SharpProof.Specs"],
            ["SharpProof.Frontend"] = ["SharpProof.Attributes", "SharpProof.Ir"],
            ["SharpProof.Fuzz"] = [
                "SharpProof.Attributes",
                "SharpProof.CompilerCollector",
                "SharpProof.Frontend",
                "SharpProof.Host",
                "SharpProof.Smt",
                "SharpProof.Testing",
                "SharpProof.Worker"
            ],
            ["SharpProof.Gates"] = [
                "SharpProof.Analyzer",
                "SharpProof.Attributes",
                "SharpProof.CompilerCollector",
                "SharpProof.ContractForGenerator",
                "SharpProof.Worker"
            ],
            ["SharpProof.Host"] = [],
            ["SharpProof.Ir"] = [],
            ["SharpProof.Smt"] = ["SharpProof.Verify"],
            ["SharpProof.Specs"] = ["SharpProof.Ir"],
            ["SharpProof.Summaries"] = ["SharpProof.Ir"],
            ["SharpProof.Verify"] = ["SharpProof.Specs"],
            ["SharpProof.Worker"] = [
                "SharpProof.CompilerArtifact",
                "SharpProof.Dataflow",
                "SharpProof.Host",
                "SharpProof.Smt"
            ],
            ["SharpProof.Worker.Protocol"] = [],
        };

    // Projects that exist only to package, host tests, or smoke-test.
    private static readonly HashSet<string> NonProductionProjects =
        new(StringComparer.Ordinal)
        {
            "SharpProof.CompilerProbe.TestAsset",
            "SharpProof.Package",
            "SharpProof.Smoke.Net472",
            "SharpProof.Testing",
            "SharpProof.Verifier",
        };

    // The worker must not depend on anything that binds C# source.
    private static readonly string[] CompilerFacingProjects =
    [
        "SharpProof.Analyzer",
        "SharpProof.Analyzer.Core",
        "SharpProof.Attributes",
        "SharpProof.Contracts",
        "SharpProof.Effects",
        "SharpProof.Frontend",
    ];

    private static readonly string[] Z3Consumers = ["SharpProof.Smt", "SharpProof.Fuzz"];

    [Test]
    public void EveryProductionProjectHasAnExplicitDagEntry()
    {
        var production = SolutionProjects()
            .Where(static name => !name.EndsWith("Test", StringComparison.Ordinal))
            .Where(static name => !NonProductionProjects.Contains(name))
            .OrderBy(static name => name, StringComparer.Ordinal);

        Assert.That(
            production,
            Is.EquivalentTo(AllowedReferences.Keys));
    }

    [Test]
    public void ProductionProjectReferencesFollowTheDag()
    {
        foreach (var (project, allowed) in AllowedReferences)
        {
            Assert.That(
                ProjectReferences(project).Order(StringComparer.Ordinal),
                Is.EqualTo(allowed.Order(StringComparer.Ordinal)),
                project);
        }
    }

    [TestCase("SharpProof.Worker")]
    public void WorkerRuntimeClosureIsCompilerNeutral(string root)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var project))
        {
            if (closure.Add(project))
            {
                foreach (var reference in ProjectReferences(project))
                {
                    pending.Push(reference);
                }
            }
        }

        Assert.That(closure.Intersect(CompilerFacingProjects), Is.Empty);
    }

    [Test]
    public void OnlyTheSmtLayerAndFuzzHarnessReferenceZ3()
    {
        var z3Consumers = SolutionProjects()
            .Where(static name => !name.EndsWith("Test", StringComparison.Ordinal))
            .Where(static name => ProjectFile(name)
                .Descendants("PackageReference")
                .Any(static reference =>
                    (string?)reference.Attribute("Include") == "Microsoft.Z3"));

        Assert.That(z3Consumers, Is.SubsetOf(Z3Consumers));
    }

    [Test]
    public void EverySolutionProjectHasALockFile()
    {
        var missing = SolutionProjectPaths()
            .Where(static path => !File.Exists(Path.Combine(
                Path.GetDirectoryName(path)!,
                "packages.lock.json")));

        Assert.That(missing, Is.Empty);
    }

    private static IEnumerable<string> SolutionProjectPaths()
    {
        var root = TestRepository.FindRoot();
        return XDocument.Load(Path.Combine(root, "SharpProof.slnx"))
            .Descendants("Project")
            .Select(static project => (string?)project.Attribute("Path"))
            .Where(static path => path?.EndsWith(".csproj", StringComparison.Ordinal) == true)
            .Select(path => Path.Combine(root, path!.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static IEnumerable<string> SolutionProjects()
    {
        return SolutionProjectPaths().Select(Path.GetFileNameWithoutExtension)!;
    }

    private static XDocument ProjectFile(string project)
    {
        var path = SolutionProjectPaths().Single(candidate =>
            Path.GetFileNameWithoutExtension(candidate) == project);
        return XDocument.Load(path);
    }

    private static string[] ProjectReferences(string project)
    {
        return ProjectFile(project)
            .Descendants("ProjectReference")
            .Select(static reference => (string)reference.Attribute("Include")!)
            .Select(static include => Path.GetFileNameWithoutExtension(
                include.Replace('\\', Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

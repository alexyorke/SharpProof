using System.Collections.Immutable;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ProjectDagTests
{
    // Every production project must appear here; adding a project or a
    // reference is an explicit architecture decision.
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> AllowedReferences =
        ImmutableDictionary.CreateRange<string, ImmutableArray<string>>(StringComparer.Ordinal,
        [
            new("ConsoleLoggingAnalyzer", []),
            new("DefensiveCodingAnalyzer", []),
            new("SharpProof.Analyzer", ["SharpProof.Analyzer.Core"]),
            new("SharpProof.Analyzer.Core", ["SharpProof.Contracts", "SharpProof.Effects"]),
            new("SharpProof.Attributes", []),
            new("SharpProof.BuildTasks", ["SharpProof.Host", "SharpProof.Worker.Protocol"]),
            new("SharpProof.CodeFixes", []),
            new("SharpProof.CompilerArtifact", ["SharpProof.Ir", "SharpProof.Worker.Protocol"]),
            new("SharpProof.CompilerCollector", [
                "SharpProof.Analyzer.Core",
                "SharpProof.CompilerArtifact"
            ]),
            new("SharpProof.ContractForGenerator", []),
            new("SharpProof.Contracts", ["SharpProof.Frontend"]),
            new("SharpProof.Dataflow", []),
            new("SharpProof.Effects", ["SharpProof.Dataflow", "SharpProof.Frontend", "SharpProof.Specs"]),
            new("SharpProof.Frontend", ["SharpProof.Attributes", "SharpProof.Ir"]),
            new("SharpProof.Fuzz", [
                "SharpProof.Attributes",
                "SharpProof.CompilerCollector",
                "SharpProof.Frontend",
                "SharpProof.Host",
                "SharpProof.Logging",
                "SharpProof.Smt",
                "SharpProof.Testing",
                "SharpProof.Worker"
            ]),
            new("SharpProof.Gates", [
                "SharpProof.Analyzer",
                "SharpProof.Attributes",
                "SharpProof.CompilerCollector",
                "SharpProof.ContractForGenerator",
                "SharpProof.Logging",
                "SharpProof.Worker"
            ]),
            new("SharpProof.Host", []),
            new("SharpProof.Ir", []),
            new("SharpProof.Logging", []),
            new("SharpProof.Smt", ["SharpProof.Verify"]),
            new("SharpProof.Specs", ["SharpProof.Ir"]),
            new("SharpProof.Verify", ["SharpProof.Specs"]),
            new("SharpProof.Worker", [
                "SharpProof.CompilerArtifact",
                "SharpProof.Dataflow",
                "SharpProof.Host",
                "SharpProof.Logging",
                "SharpProof.Smt"
            ]),
            new("SharpProof.Worker.Protocol", []),
                ]);

    // Projects that exist only to package, host tests, or smoke-test.
    private static readonly ImmutableHashSet<string> NonProductionProjects =
        ImmutableHashSet.CreateRange<string>(StringComparer.Ordinal,
        [
            "SharpProof.CompilerProbe.TestAsset",
            "SharpProof.Package",
            "SharpProof.Smoke.Net472",
            "SharpProof.Testing",
            "SharpProof.Verifier",
                ]);

    // The worker must not depend on anything that binds C# source.
    private static readonly ImmutableArray<string> CompilerFacingProjects =
    [
        "SharpProof.Analyzer",
        "SharpProof.Analyzer.Core",
        "SharpProof.CodeFixes",
        "SharpProof.Attributes",
        "SharpProof.Contracts",
        "SharpProof.Effects",
        "SharpProof.Frontend",
    ];

    private static readonly ImmutableArray<string> Z3Consumers = ["SharpProof.Smt", "SharpProof.Fuzz"];

    [Test]
    public void EveryProductionProjectHasAnExplicitDagEntry()
    {
        var production = SolutionProjects()
            .Where(static name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (!name.EndsWith("Test", StringComparison.Ordinal)))
            .Where(static name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (!NonProductionProjects.Contains(name)))
            .OrderBy(static name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name), StringComparer.Ordinal);

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
        System.ArgumentNullException.ThrowIfNull(root);
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
            .Where(static name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (!name.EndsWith("Test", StringComparison.Ordinal)))
            .Where(static name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (ProjectFile(name)
                .Descendants("PackageReference")
                .Any(static reference =>
                    reference is null ? throw new System.ArgumentNullException(nameof(reference)) : ((string?)reference.Attribute("Include") == "Microsoft.Z3"))))
            .ToArray();

        Assert.That(z3Consumers, Is.SubsetOf(Z3Consumers));
        // Guard against the scan silently matching nothing.
        Assert.That(z3Consumers, Does.Contain("SharpProof.Smt"));
    }

    [Test]
    public void EverySolutionProjectHasALockFile()
    {
        var missing = SolutionProjectPaths()
            .Where(static path => path is null ? throw new System.ArgumentNullException(nameof(path)) : (!File.Exists(Path.Combine(
                Path.GetDirectoryName(path)!,
                "packages.lock.json"))));

        Assert.That(missing, Is.Empty);
    }

    private static IEnumerable<string> SolutionProjectPaths()
    {
        var root = TestRepository.FindRoot();
        return XDocument.Load(Path.Combine(root, "SharpProof.slnx"))
            .Descendants("Project")
            .Select(static project => project is null ? throw new System.ArgumentNullException(nameof(project)) : ((string?)project.Attribute("Path")))
            .Where(static path => path?.EndsWith(".csproj", StringComparison.Ordinal) == true)
            .Select(path => path is null ? throw new System.ArgumentNullException(nameof(path)) : (Path.Combine(root, path!.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static IEnumerable<string> SolutionProjects()
    {
        return SolutionProjectPaths().Select(Path.GetFileNameWithoutExtension)!;
    }

    private static XDocument ProjectFile(string project)
    {
        System.ArgumentNullException.ThrowIfNull(project);
        var path = SolutionProjectPaths().Single(candidate =>
            candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (Path.GetFileNameWithoutExtension(candidate) == project));
        return XDocument.Load(path);
    }

    private static string[] ProjectReferences(string project)
    {
        System.ArgumentNullException.ThrowIfNull(project);
        return ProjectFile(project)
            .Descendants("ProjectReference")
            .Select(static reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : ((string)reference.Attribute("Include")!))
            .Select(static include => include is null ? throw new System.ArgumentNullException(nameof(include)) : (Path.GetFileNameWithoutExtension(
                include.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

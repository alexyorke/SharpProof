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
            new("SharpProof.Analyzer.Core", ["SharpProof.Compiler.Semantics", "SharpProof.Dataflow"]),
            new("SharpProof.Compiler.Semantics", ["SharpProof.Contracts", "SharpProof.Effects"]),
            new("SharpProof.Attributes", []),
            new("SharpProof.BuildTasks", ["SharpProof.Host", "SharpProof.Worker.Protocol"]),
            new("SharpProof.CodeFixes", ["SharpProof.EditorSuggestions"]),
            new("SharpProof.CompilerArtifact", ["SharpProof.Ir", "SharpProof.Worker.Protocol"]),
            new("SharpProof.CompilerCollector", [
                "SharpProof.Compiler.Semantics",
                "SharpProof.CompilerArtifact"
            ]),
            new("SharpProof.ContractForGenerator", []),
            new("SharpProof.Contracts", ["SharpProof.Frontend"]),
            new("SharpProof.Dataflow", []),
            new("SharpProof.EditorSuggestions", []),
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
            new("SharpProof.Reporting", ["SharpProof.Worker.Protocol"]), new("SharpProof.Tool", ["SharpProof.Worker.Protocol", "SharpProof.Reporting", "SharpProof.CompilerArtifact", "SharpProof.Specs", "SharpProof.Frontend"]),
            new("SharpProof.Verify", ["SharpProof.Ir"]),
            new("SharpProof.Worker", [
                "SharpProof.CompilerArtifact",
                "SharpProof.Host",
                "SharpProof.Logging",
                "SharpProof.Smt",
                "SharpProof.Specs"
            ]),
            new("SharpProof.Worker.Protocol", []),
        ]);

    // Projects that exist only to package, host tests, or smoke-test.
    private static readonly ImmutableHashSet<string> NonProductionProjects =
        ImmutableHashSet.CreateRange<string>(StringComparer.Ordinal,
        [
            "SharpProof.CompilerProbe.TestAsset",
            "SharpProof.Package", "SharpProof.Templates",
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
        "SharpProof.EditorSuggestions",
        "SharpProof.Attributes",
        "SharpProof.Contracts",
        "SharpProof.CompilerCollector",
        "SharpProof.Compiler.Semantics",
        "SharpProof.Effects",
        "SharpProof.Frontend",
    ];

    private static readonly ImmutableArray<string> Z3Consumers = ["SharpProof.Smt", "SharpProof.Fuzz"];

    [Test]
    public void EveryProductionProjectHasAnExplicitDagEntry()
    {
        var production = ImmutableArray.CreateBuilder<string>();
        foreach (var name in SolutionProjects())
        {
            if (!name.EndsWith("Tests", StringComparison.Ordinal) && !NonProductionProjects.Contains(name))
            {
                production.Add(name);
            }
        }
        Assert.That(production, Is.EquivalentTo(AllowedReferences.Keys));
    }

    [Test]
    public void ProductionProjectReferencesFollowTheDag()
    {
        foreach (var (project, allowed) in AllowedReferences)
        {
            Assert.That(ProjectReferences(project), Is.EquivalentTo(allowed), project);
        }
    }

    [Test]
    public void EvaluatedRuntimeReferencesFollowTheDag()
    {
        var inventory = EvaluatedItems();
        var seen = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var item in inventory)
        {
            if (!AllowedReferences.TryGetValue(item.Project, out var allowed))
            {
                continue;
            }
            seen.Add(item.Project);
            if (item.Kind == "ProjectReference")
            {
                Assert.That(allowed, Does.Contain(Path.GetFileNameWithoutExtension(item.Value)), item.Project);
            }
        }
        Assert.That(seen, Is.EquivalentTo(AllowedReferences.Keys));
    }

    [Test]
    public void EditorSuggestionsRemainABuildOnlyCodeFixDependency()
    {
        var project = XDocument.Load(ProjectPath("SharpProof.CodeFixes"));
        var reference = project.Descendants("ProjectReference").Single(item =>
            Path.GetFileNameWithoutExtension((string?)(item ?? throw new ArgumentNullException(nameof(item))).Attribute("Include")) == "SharpProof.EditorSuggestions");
        using (Assert.EnterMultipleScope())
        {
            Assert.That((string?)reference.Attribute("ReferenceOutputAssembly"), Is.EqualTo("false"));
            Assert.That((string?)reference.Attribute("PrivateAssets"), Is.EqualTo("all"));
            Assert.That((string?)reference.Attribute("TreatAsPackageReference"), Is.EqualTo("false"));
            Assert.That(RuntimeClosure("SharpProof.CodeFixes"), Does.Not.Contain("SharpProof.EditorSuggestions"));
        }
    }

    [TestCase("SharpProof.Worker")]
    public void WorkerRuntimeClosureIsCompilerNeutral(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var closure = RuntimeClosure(root);
        Assert.That(closure.Intersect(CompilerFacingProjects), Is.Empty);
        foreach (var item in EvaluatedItems())
        {
            if (closure.Contains(item.Project) && item.Kind == "PackageReference")
            {
                Assert.That(item.Value.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) &&
                    !item.Value.EndsWith("Analyzers", StringComparison.Ordinal), Is.False,
                    item.Project + " -> " + item.Value);
            }
        }
    }

    [Test]
    public void CollectorAndSharedSemanticsDoNotDependOnAnalyzerPolicy()
    {
        foreach (var project in ImmutableArray.Create("SharpProof.CompilerCollector", "SharpProof.Compiler.Semantics"))
        {
            var closure = RuntimeClosure(project);
            Assert.That(closure, Does.Not.Contain("SharpProof.Analyzer"));
            Assert.That(closure, Does.Not.Contain("SharpProof.Analyzer.Core"));
            Assert.That(closure, Does.Not.Contain("SharpProof.Worker"));
            Assert.That(closure, Does.Not.Contain("SharpProof.Smt"));
        }
    }

    [TestCase("SharpProof.Analyzer")]
    [TestCase("SharpProof.CompilerCollector")]
    public void PortableCompilerRuntimeDoesNotContainNativeVerification(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var closure = RuntimeClosure(root);
        Assert.That(closure, Does.Not.Contain("SharpProof.Smt"));
        Assert.That(closure, Does.Not.Contain("SharpProof.Worker"));
        Assert.That(closure, Does.Not.Contain("SharpProof.Verify"));
        foreach (var item in EvaluatedItems())
        {
            if (closure.Contains(item.Project) && item.Kind == "PackageReference")
            {
                Assert.That(item.Value, Is.Not.EqualTo("Microsoft.Z3"));
            }
        }
    }

    [Test]
    public void ProductionCompileItemsRespectSourceOwnership()
    {
        var repository = TestRepository.FindRoot();
        foreach (var item in EvaluatedItems())
        {
            if (item.Kind != "Compile" || !AllowedReferences.ContainsKey(item.Project))
            {
                continue;
            }
            var owner = Path.GetDirectoryName(ProjectPath(item.Project))!;
            var intermediates = Path.Combine(repository, "artifacts", "obj", item.Project);
            if (IsWithin(item.Value, owner) || IsWithin(item.Value, intermediates))
            {
                continue;
            }
            var relative = Path.GetRelativePath(repository, item.Value).Replace('\\', '/');
            var allowed = relative.StartsWith("eng/shared/", StringComparison.Ordinal) ||
                relative == "src/DefensiveCodingAnalyzer/AllValuesValidAttribute.cs" ||
                relative == "eng/ParameterGuard.cs" || relative == "eng/LoggingDefaults.cs" ||
                relative == "eng/RoslynCfgFactory.cs" || relative == "eng/RoslynSymbolFacts.cs" ||
                relative == "eng/CompilerAdditionalTextStability.cs" ||
                relative == "src/SharpProof.Ir/IrIdentifierAliases.cs";
            allowed |= item.Project == "SharpProof.Logging" &&
                relative == "eng/logging/RepositoryLogger.cs";
            allowed |= item.Project == "SharpProof.EditorSuggestions" &&
                relative is "src/SharpProof.CodeFixes/CompanionEligibility.cs" or "src/SharpProof.CodeFixes/CompanionSuggestionContract.cs";
            if (item.Project is "SharpProof.Gates" or "SharpProof.Fuzz")
            {
                allowed |= relative.StartsWith("eng/testing/", StringComparison.Ordinal) ||
                    relative == "eng/scripts/SharpProof.JsonDefaults.cs";
            }
            Assert.That(allowed, Is.True, item.Project + " compiles source owned elsewhere: " + relative);
        }
    }

    [Test]
    public void OnlyTheSmtLayerAndFuzzHarnessReferenceZ3()
    {
        var consumers = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var item in EvaluatedItems())
        {
            if (item.Kind == "PackageReference" && item.Value == "Microsoft.Z3")
            {
                consumers.Add(item.Project);
            }
        }
        Assert.That(consumers, Is.SubsetOf(Z3Consumers));
    }

    [Test]
    public void EverySolutionProjectHasALockFile()
    {
        foreach (var path in SolutionProjectPaths())
        {
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "packages.lock.json")), Is.True, path);
        }
    }

    private static bool IsWithin(string path, string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static ImmutableHashSet<string> RuntimeClosure(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var inventory = EvaluatedItems();
        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var pending = ImmutableStack<string>.Empty.Push(root);
        while (!pending.IsEmpty)
        {
            pending = pending.Pop(out var project);
            if (!result.Add(project))
            {
                continue;
            }
            foreach (var item in inventory)
            {
                if (item.Project == project && item.Kind == "ProjectReference")
                {
                    pending = pending.Push(Path.GetFileNameWithoutExtension(item.Value));
                }
            }
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<ArchitectureItem> EvaluatedItems()
    {
        var result = ImmutableArray.CreateBuilder<ArchitectureItem>();
        var path = Path.Combine(AppContext.BaseDirectory, "architecture-items.tsv");
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('|').ToImmutableArray();
            if (parts.Length != 3)
            {
                throw new InvalidDataException("Invalid architecture inventory entry.");
            }
            result.Add(new(parts[0], parts[1], parts[2]));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<string> SolutionProjectPaths()
    {
        var root = TestRepository.FindRoot();
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var project in XDocument.Load(Path.Combine(root, "SharpProof.slnx")).Descendants("Project"))
        {
            var path = (string?)project.Attribute("Path") ?? throw new InvalidDataException("Missing project path.");
            if (path.EndsWith(".csproj", StringComparison.Ordinal))
            {
                result.Add(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<string> SolutionProjects()
    {
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var path in SolutionProjectPaths())
        {
            result.Add(Path.GetFileNameWithoutExtension(path));
        }
        return result.ToImmutable();
    }

    private static string ProjectPath(string project)
    {
        ArgumentException.ThrowIfNullOrEmpty(project);
        foreach (var path in SolutionProjectPaths())
        {
            if (Path.GetFileNameWithoutExtension(path) == project)
            {
                return path;
            }
        }
        throw new InvalidDataException("Project is missing from the solution: " + project);
    }

    private static ImmutableArray<string> ProjectReferences(string project)
    {
        ArgumentException.ThrowIfNullOrEmpty(project);
        var references = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var reference in XDocument.Load(ProjectPath(project)).Descendants("ProjectReference"))
        {
            var include = (string?)reference.Attribute("Include") ?? throw new InvalidDataException("Missing reference path.");
            references.Add(Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)));
        }
        return [.. references];
    }

    private sealed record ArchitectureItem
    {
        internal ArchitectureItem(string project, string kind, string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(project);
            ArgumentException.ThrowIfNullOrEmpty(kind);
            ArgumentException.ThrowIfNullOrEmpty(value);
            Project = project;
            Kind = kind;
            Value = value;
        }
        internal string Project { get; }
        internal string Kind { get; }
        internal string Value { get; }
    }
}

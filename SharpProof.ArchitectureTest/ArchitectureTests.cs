using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Effects;
using SharpProof.Ir;
using SharpProof.Verify;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ArchitectureTests {
    private static readonly string[] V2ProductionProjects = [
        "SharpProof.Ir",
        "SharpProof.ContractForGenerator",
        "SharpProof.Specs",
        "SharpProof.Dataflow",
        "SharpProof.Frontend",
        "SharpProof.Contracts",
        "SharpProof.Effects",
        "SharpProof.Verify",
        "SharpProof.Smt",
        "SharpProof.Worker.Protocol",
        "SharpProof.Worker",
        "SharpProof.Worker.Launcher"
    ];

    [Test]
    public void NewLayerProjectReferencesFollowTheDependencyDag() {
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal) {
            ["SharpProof.Ir"] = [],
            ["SharpProof.ContractForGenerator"] = ["SharpProof.Frontend"],
            ["SharpProof.Specs"] = ["SharpProof.Ir"],
            ["SharpProof.Dataflow"] = [],
            ["SharpProof.Frontend"] = ["SharpProof.Ir"],
            ["SharpProof.Contracts"] = [
                "SharpProof.Attributes",
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Specs"
            ],
            ["SharpProof.Effects"] = [
                "SharpProof.Attributes",
                "SharpProof.Dataflow",
                "SharpProof.Frontend",
                "SharpProof.Specs"
            ],
            ["SharpProof.Verify"] = ["SharpProof.Ir", "SharpProof.Specs"],
            ["SharpProof.Smt"] = ["SharpProof.Ir", "SharpProof.Verify"],
            ["SharpProof.Worker.Protocol"] = [],
            ["SharpProof.Worker"] = [
                "SharpProof.Attributes",
                "SharpProof.Contracts",
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Smt",
                "SharpProof.Specs",
                "SharpProof.Verify",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.Worker.Launcher"] = [
                "SharpProof.Worker.Protocol"
            ]
        };

        foreach (var project in V2ProductionProjects) {
            var actual = GetProjectReferences(project)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            Assert.That(
                actual,
                Is.EqualTo(allowed[project].OrderBy(
                    static value => value,
                    StringComparer.Ordinal)),
                project);
        }
    }

    [Test]
    public void LanguageNeutralLayersHaveNoCSharpSyntaxDependency() {
        foreach (var project in new[] {
                     "SharpProof.Ir",
                     "SharpProof.Specs",
                     "SharpProof.Dataflow"
                 }) {
            var source = ReadProductionSources(project);
            Assert.That(source, Does.Not.Contain("Microsoft.CodeAnalysis.CSharp"));
            Assert.That(source, Does.Not.Contain("SyntaxNode"));
            Assert.That(source, Does.Not.Contain("SyntaxKind"));
            Assert.That(source, Does.Not.Contain("SyntaxFactory"));
        }
    }

    [Test]
    public void OnlyTheSmtLayerReferencesZ3InTheV2Graph() {
        foreach (var project in V2ProductionProjects) {
            var xml = XDocument.Load(ProjectFile(project));
            var packages = xml
                .Descendants("PackageReference")
                .Select(static element => (string?)element.Attribute("Include"))
                .Where(static value => value != null)
                .ToArray();
            Assert.That(
                packages.Contains("Microsoft.Z3", StringComparer.Ordinal),
                Is.EqualTo(project == "SharpProof.Smt"),
                project);
        }
    }

    [Test]
    public void IrTermPayloadsDoNotExposeSemanticStrings() {
        var stringPayloads = typeof(IrTerm).Assembly
            .GetTypes()
            .Where(static type =>
                type.Namespace == typeof(IrTerm).Namespace &&
                typeof(IrTerm).IsAssignableFrom(type))
            .SelectMany(static type => type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            .Where(static property => property.PropertyType == typeof(string))
            .Select(static property =>
                property.DeclaringType!.Name + "." + property.Name)
            .ToArray();

        Assert.That(stringPayloads, Is.Empty);
    }

    [Test]
    public void FrontendUsesOnlyTotalCompilerBoundLowering() {
        var source = ReadProductionSources("SharpProof.Frontend");
        Assert.That(source, Does.Not.Contain("TryLower"));
        Assert.That(source, Does.Not.Contain("SyntaxFactory"));
        Assert.That(source, Does.Not.Contain("ParseExpression"));
        Assert.That(source, Does.Not.Contain("ParseStatement"));
        Assert.That(source, Does.Not.Contain("SpeculativeSemanticModel"));
        Assert.That(source, Does.Not.Contain("ToDisplayString("));
        Assert.That(source, Does.Contain("IrOpaqueTerm").Or.Contain("PureOpaque("));
    }

    [Test]
    public void ProofProducingOutcomeConstructorsStayInTheKernel() {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "SharpProof.Verify"),
            "*.cs",
            SearchOption.AllDirectories);
        var provenCallers = FindCallers(files, "new ProvenOutcome(");
        var refutedCallers = FindCallers(files, "new RefutedOutcome(");

        Assert.That(provenCallers, Is.EqualTo(["ProofKernel.cs"]));
        Assert.That(refutedCallers, Is.EqualTo(["ProofKernel.cs"]));
        Assert.That(typeof(Assumption).GetConstructors(), Is.Empty);
        Assert.That(typeof(EffectSummary).GetConstructors(), Is.Empty);
        Assert.That(
            typeof(ProofJustification).IsAssignableFrom(
                typeof(ApproximatedJustification)),
            Is.False);

        var productionFiles = V2ProductionProjects
            .SelectMany(ProductionSourceFiles)
            .ToArray();
        Assert.That(
            FindRelativeCallers(productionFiles, "new Assumption("),
            Is.EqualTo(["SharpProof.Worker/CallableVerifier.cs"]));
        Assert.That(
            FindRelativeCallers(productionFiles, "new EffectSummary("),
            Is.EqualTo([
                "SharpProof.Effects/EffectSummary.cs",
                "SharpProof.Effects/EffectSummaryOperations.cs",
                "SharpProof.Effects/ExternalEffectResolver.cs"
            ]));
    }

    [Test]
    public void HistoricalV1AcceptanceTreeRemainsPinned() {
        const string expected = "1388666e46265f306f9687b90eb11e3bdce5c1b9";
        var root = RepositoryRoot();
        var start = new System.Diagnostics.ProcessStartInfo {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("rev-parse");
        start.ArgumentList.Add("HEAD:eng/acceptance/v1");
        using var process = System.Diagnostics.Process.Start(start)!;
        var actual = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.Zero, error);
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static IEnumerable<string> GetProjectReferences(string project) {
        var xml = XDocument.Load(ProjectFile(project));
        return xml
            .Descendants("ProjectReference")
            .Where(static element =>
                !string.Equals(
                    (string?)element.Attribute("OutputItemType"),
                    "Analyzer",
                    StringComparison.OrdinalIgnoreCase))
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value =>
                Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')));
    }

    private static string ProjectFile(string project) =>
        Path.Combine(RepositoryRoot(), project, project + ".csproj");

    private static string ReadProductionSources(string project) =>
        string.Join(
            "\n",
            ProductionSourceFiles(project)
                .Select(File.ReadAllText));

    private static IEnumerable<string> ProductionSourceFiles(string project) =>
        Directory.GetFiles(
                Path.Combine(RepositoryRoot(), project),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains(
                    Path.DirectorySeparatorChar + "obj" +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !path.Contains(
                    Path.DirectorySeparatorChar + "bin" +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal);

    private static string[] FindCallers(
        IEnumerable<string> files,
        string pattern) =>
        [.. files
            .Where(file => File.ReadAllText(file).Contains(
                pattern,
                StringComparison.Ordinal))
            .Select(static file => Path.GetFileName(file)!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)];

    private static string[] FindRelativeCallers(
        IEnumerable<string> files,
        string pattern) =>
        [.. files
            .Where(file => File.ReadAllText(file).Contains(
                pattern,
                StringComparison.Ordinal))
            .Select(Relative)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)];

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');

    private static string RepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root.");
    }
}

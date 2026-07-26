using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    public void SemanticConsumersDoNotEncodeFrameworkIdentitiesAsStringLiterals() {
        var semanticConsumers = new[] {
            "SharpProof.Analyzer",
            "SharpProof.Contracts",
            "SharpProof.Dataflow",
            "SharpProof.Effects",
            "SharpProof.Frontend",
            "SharpProof.Ir",
            "SharpProof.Smt",
            "SharpProof.Verify",
            "SharpProof.Worker",
            "SharpProof.Worker.Protocol",
            "SharpProof.Worker.Launcher"
        };
        var violations = semanticConsumers
            .SelectMany(ProductionSourceFiles)
            .SelectMany(file => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(file),
                    CSharpParseOptions.Default.WithLanguageVersion(
                        LanguageVersion.CSharp12),
                    file)
                .GetRoot()
                .DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(static literal =>
                    literal.Token.ValueText.StartsWith(
                        "System.",
                        StringComparison.Ordinal))
                .Select(literal => {
                    var line = literal.GetLocation()
                        .GetLineSpan()
                        .StartLinePosition.Line + 1;
                    return $"{Relative(file)}:{line}: " +
                        literal.Token.ValueText;
                }))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            violations,
            Is.Empty,
            Environment.NewLine + string.Join(Environment.NewLine, violations));
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
        var productionFiles = V2ProductionProjects
            .SelectMany(ProductionSourceFiles)
            .ToArray();
        Assert.That(
            FindRelativeCallers(productionFiles, "new ProvenOutcome("),
            Is.EqualTo(["SharpProof.Verify/ProofKernel.cs"]));
        Assert.That(
            FindRelativeCallers(productionFiles, "new RefutedOutcome("),
            Is.EqualTo(["SharpProof.Verify/ProofKernel.cs"]));
        Assert.That(
            FindRelativeCallers(productionFiles, "new ValidatedModel("),
            Is.EqualTo(["SharpProof.Verify/ProofKernel.cs"]));
        Assert.That(typeof(Assumption).GetConstructors(), Is.Empty);
        Assert.That(typeof(EffectSummary).GetConstructors(), Is.Empty);
        Assert.That(
            typeof(ProofJustification).IsAssignableFrom(
                typeof(ApproximatedJustification)),
            Is.False);

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

    [Test]
    public void AlgorithmLayerSizeRatchetManifestIsWellFormed() {
        var manifest = ReadSizeRatchetManifest();
        var expectedPaths = new[] {
            "SharpProof.Dataflow/ForwardDataflowAnalysis.cs",
            "SharpProof.Dataflow/IntervalDomain.cs",
            "SharpProof.Dataflow/NullnessDomain.cs",
            "SharpProof.Dataflow/SequenceCardinalityDomain.cs",
            "SharpProof.Effects/EffectAnalysisSession.cs",
            "SharpProof.Effects/ExternalEffectResolver.cs",
            "SharpProof.Effects/OperationEffectScanner.cs",
            "SharpProof.Frontend/RoslynOperationLowerer.cs",
            "SharpProof.Frontend/RoslynProgramLowerer.cs",
            "SharpProof.Verify/Evidence.cs",
            "SharpProof.Verify/Outcomes.cs",
            "SharpProof.Verify/ProofKernel.cs"
        };
        Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
        Assert.That(manifest.Rationale, Is.Not.Empty);
        Assert.That(manifest.Measurement.PhysicalFileLines, Is.Not.Empty);
        Assert.That(manifest.Measurement.MemberLines, Is.Not.Empty);
        Assert.That(
            manifest.Files
                .Select(static entry => entry.Path)
                .OrderBy(static path => path, StringComparer.Ordinal),
            Is.EqualTo(expectedPaths.OrderBy(
                static path => path,
                StringComparer.Ordinal)));

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files) {
            Assert.That(entry.Path, Is.EqualTo(entry.Path.Replace('\\', '/')));
            Assert.That(entry.Path, Does.EndWith(".cs"));
            Assert.That(
                paths.Add(entry.Path),
                Is.True,
                $"Duplicate size-ratchet entry: {entry.Path}");
            Assert.That(entry.MaximumPhysicalLines, Is.Positive, entry.Path);
            Assert.That(entry.MaximumMemberLines, Is.Positive, entry.Path);

            var fullPath = Path.GetFullPath(
                Path.Combine(RepositoryRoot(), entry.Path));
            Assert.That(
                fullPath.StartsWith(
                    RepositoryRoot() + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                Is.True,
                entry.Path);
            Assert.That(File.Exists(fullPath), Is.True, entry.Path);
        }
    }

    [Test]
    public void ReplacedAlgorithmLayersStayWithinSizeCaps() {
        var violations = new List<string>();
        foreach (var entry in ReadSizeRatchetManifest().Files) {
            var fullPath = Path.Combine(RepositoryRoot(), entry.Path);
            var source = File.ReadAllText(fullPath);
            var tree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12),
                fullPath);
            var parseErrors = tree.GetDiagnostics()
                .Where(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            if (parseErrors.Length != 0) {
                violations.Add(
                    $"{entry.Path}: Roslyn parse errors: " +
                    string.Join("; ", parseErrors.Select(
                        static diagnostic => diagnostic.ToString())));
                continue;
            }

            var physicalLines = File.ReadLines(fullPath).Count();
            if (physicalLines > entry.MaximumPhysicalLines) {
                violations.Add(
                    $"{entry.Path}: {physicalLines} physical lines exceeds " +
                    $"cap {entry.MaximumPhysicalLines}");
            }

            var largestMember = tree.GetRoot()
                .DescendantNodes()
                .Where(IsMeasuredMember)
                .Select(node => new {
                    Node = node,
                    Lines = SourceLineCount(node)
                })
                .OrderByDescending(static member => member.Lines)
                .FirstOrDefault();
            if (largestMember != null &&
                largestMember.Lines > entry.MaximumMemberLines) {
                var line = largestMember.Node.GetLocation()
                    .GetLineSpan()
                    .StartLinePosition.Line + 1;
                violations.Add(
                    $"{entry.Path}:{line}: {MemberName(largestMember.Node)} " +
                    $"spans {largestMember.Lines} lines; cap " +
                    $"{entry.MaximumMemberLines}");
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsMeasuredMember(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or
        LocalFunctionStatementSyntax or
        AccessorDeclarationSyntax or
        ParameterListSyntax { Parent: TypeDeclarationSyntax } or
        PropertyDeclarationSyntax { ExpressionBody: not null } or
        IndexerDeclarationSyntax { ExpressionBody: not null };

    private static int SourceLineCount(SyntaxNode node) {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static string MemberName(SyntaxNode node) =>
        node switch {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor =>
                constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor =>
                "~" + destructor.Identifier.ValueText,
            OperatorDeclarationSyntax @operator =>
                "operator " + @operator.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion =>
                "operator " + conversion.Type,
            LocalFunctionStatementSyntax local => local.Identifier.ValueText,
            AccessorDeclarationSyntax accessor =>
                accessor.Keyword.ValueText + " accessor",
            ParameterListSyntax {
                Parent: TypeDeclarationSyntax type
            } => type.Identifier.ValueText + " primary constructor",
            PropertyDeclarationSyntax property =>
                property.Identifier.ValueText + " getter",
            IndexerDeclarationSyntax => "this getter",
            _ => node.Kind().ToString()
        };

    private static SizeRatchetManifest ReadSizeRatchetManifest() {
        var path = Path.Combine(
            RepositoryRoot(),
            "eng",
            "acceptance",
            "v2",
            "algorithm-size-ratchets.json");
        var manifest = JsonSerializer.Deserialize<SizeRatchetManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });
        return manifest ??
            throw new InvalidOperationException(
                "Could not deserialize the algorithm size-ratchet manifest.");
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

    private sealed class SizeRatchetManifest {
        public int SchemaVersion { get; init; }

        public string Rationale { get; init; } = "";

        public SizeRatchetMeasurement Measurement { get; init; } = new();

        public SizeRatchetEntry[] Files { get; init; } = [];
    }

    private sealed class SizeRatchetMeasurement {
        public string PhysicalFileLines { get; init; } = "";

        public string MemberLines { get; init; } = "";
    }

    private sealed class SizeRatchetEntry {
        public string Path { get; init; } = "";

        public int MaximumPhysicalLines { get; init; }

        public int MaximumMemberLines { get; init; }
    }
}

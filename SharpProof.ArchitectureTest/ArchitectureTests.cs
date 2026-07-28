using System.Diagnostics.CodeAnalysis;
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
    private static readonly JsonSerializerOptions SizeRatchetJsonOptions =
        new() {
            PropertyNameCaseInsensitive = true
        };

    private static readonly string[] ProductionProjects = [
        "SharpProof.Ir",
        "SharpProof.CompilerArtifact",
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
            ["SharpProof.CompilerArtifact"] = [
                "SharpProof.Ir",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.ContractForGenerator"] = ["SharpProof.Contracts"],
            ["SharpProof.Specs"] = ["SharpProof.Ir"],
            ["SharpProof.Dataflow"] = [],
            ["SharpProof.Frontend"] = ["SharpProof.Ir"],
            ["SharpProof.Contracts"] = [
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Specs"
            ],
            ["SharpProof.Effects"] = [
                "SharpProof.Dataflow",
                "SharpProof.Frontend",
                "SharpProof.Specs"
            ],
            ["SharpProof.Verify"] = ["SharpProof.Ir", "SharpProof.Specs"],
            ["SharpProof.Smt"] = ["SharpProof.Ir", "SharpProof.Verify"],
            ["SharpProof.Worker.Protocol"] = [],
            ["SharpProof.Worker"] = [
                "SharpProof.CompilerArtifact",
                "SharpProof.Dataflow",
                "SharpProof.Ir",
                "SharpProof.Smt",
                "SharpProof.Specs",
                "SharpProof.Verify",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.Worker.Launcher"] = [
                "SharpProof.CompilerArtifact",
                "SharpProof.Ir",
                "SharpProof.Specs",
                "SharpProof.Worker.Protocol"
            ]
        };

        foreach (var project in ProductionProjects) {
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
    public void WorkerAndLauncherRuntimeClosuresAreCompilerNeutral() {
        var forbiddenProjects = new[] {
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.Contracts",
            "SharpProof.Effects", "SharpProof.Frontend"
        };
        foreach (var root in new[] {
                     "SharpProof.Worker", "SharpProof.Worker.Launcher"
                 }) {
            var closure = TransitiveProjectClosure(root).ToArray();
            Assert.That(
                closure.Intersect(forbiddenProjects, StringComparer.Ordinal),
                Is.Empty,
                root);
            foreach (var project in closure) {
                Assert.That(
                    ProjectPackages(project),
                    Has.None.StartsWith("Microsoft.CodeAnalysis"),
                    project);
                Assert.That(
                    ReadProductionSources(project),
                    Does.Not.Contain("Microsoft.CodeAnalysis"),
                    project);
                Assert.That(
                    File.ReadAllText(ProjectFile(project)),
                    Does.Not.Contain("RoslynTargetsPath"),
                    project);
            }
        }
    }

    [Test]
    public void AnalyzerUtilitiesHasNoStaleBuildOrTestResidue() {
        foreach (var path in new[] {
                     "Directory.Build.targets",
                     "SharpProof.AnalyzerConsumer.props",
                     "SharpProof.Worker.Test/SharpProof.Worker.Test.csproj"
                 })
            Assert.That(
                File.ReadAllText(Path.Combine(
                    RepositoryRoot(), path.Replace('/', Path.DirectorySeparatorChar))),
                Does.Not.Contain("Microsoft.CodeAnalysis.AnalyzerUtilities"),
                path);
    }

    [Test]
    public void LanguageNeutralLayersHaveNoCSharpSyntaxDependency() {
        foreach (var project in new[] {
                     "SharpProof.Ir",
                     "SharpProof.CompilerArtifact",
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
    public void OnlyTheSmtLayerReferencesZ3InTheProductionGraph() {
        foreach (var project in ProductionProjects) {
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
            "SharpProof.CompilerArtifact",
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
        var productionFiles = ProductionProjects
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
            "SharpProof.Ir/IrInterpreter.cs",
            "SharpProof.Ir/IrProgramInterpreter.cs",
            "SharpProof.Verify/Evidence.cs",
            "SharpProof.Verify/Outcomes.cs",
            "SharpProof.Verify/ProofKernel.cs",
            "SharpProof.Worker/CallableCounterexampleReplayer.cs"
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
    public void TrustedComputingBaseDeclarationNamesEveryRequiredPath() {
        var expected = new Dictionary<string, string[]>(
            StringComparer.Ordinal) {
            ["discovery"] = [
                "SharpProof.Analyzer/FinalCompilationCollector.cs",
                "SharpProof.Analyzer/CompilerArtifact/ClaimManifestBuilder.cs",
                "SharpProof.Analyzer/CompilerArtifact/SemanticClaimIdentity.cs",
                "SharpProof.Contracts/ContractClauseInventoryBuilder.cs"
            ],
            ["lowering"] = [
                "SharpProof.Analyzer/CompilerArtifact/CompilerCallableLowerer.cs",
                "SharpProof.Analyzer/CompilerArtifact/CompilerManifestArtifactProducer.cs",
                "SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs",
                "SharpProof.CompilerArtifact/PortableIrGraphCodec.cs",
                "SharpProof.Contracts/ContractBinder.cs",
                "SharpProof.Contracts/ContractExpressionBinder.cs",
                "SharpProof.Frontend/RoslynOperationLowerer.cs",
                "SharpProof.Frontend/RoslynProgramLowerer.cs"
            ],
            ["execution"] = [
                "SharpProof.Worker/CallableVerifier.cs",
                "SharpProof.Worker/SpecResultDomainProjection.cs",
                "SharpProof.Worker/SharpProofWorker.cs"
            ],
            ["encoding"] = ["SharpProof.Smt/IrSmtBackend.cs"],
            ["replay"] = [
                "SharpProof.Verify/ProofKernel.cs",
                "SharpProof.Ir/IrInterpreter.cs",
                "SharpProof.Ir/IrProgramInterpreter.cs",
                "SharpProof.Worker/CallableCounterexampleReplayer.cs"
            ],
            ["policy"] = [
                "SharpProof.Worker.Launcher/Program.cs",
                "SharpProof.Worker/CallableClaimResultAssembler.cs"
            ],
            ["cacheValidation"] = [
                "SharpProof.CompilerArtifact/CompilerManifestArtifact.cs",
                "SharpProof.Worker.Protocol/ProtocolJson.cs",
                "SharpProof.Worker.Protocol/WorkerResultAssembler.cs",
                "SharpProof.Worker/WorkerInputSnapshot.cs",
                "SharpProof.Worker/CacheableWorkerResponse.cs",
                "SharpProof.Worker/VerificationCache.cs"
            ]
        };
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "eng", "acceptance", "contract.json")));
        var root = document.RootElement;
        Assert.That(
            root.GetProperty("trustedKernel")
                .GetProperty("maximumNonblankLines")
                .GetInt32(),
            Is.EqualTo(275));
        var declaration = root.GetProperty("trustedComputingBase");
        Assert.That(
            declaration.GetProperty("measurement").GetString(),
            Is.Not.Empty);
        var actual = declaration.GetProperty("components")
            .EnumerateArray()
            .ToDictionary(
                static component =>
                    component.GetProperty("name").GetString() ?? "",
                static component => component.GetProperty("paths")
                    .EnumerateArray()
                    .Select(static path => path.GetString() ?? "")
                    .ToArray(),
                StringComparer.Ordinal);
        Assert.That(
            actual.Keys.OrderBy(static name => name, StringComparer.Ordinal),
            Is.EqualTo(expected.Keys.OrderBy(
                static name => name,
                StringComparer.Ordinal)));
        foreach (var component in expected) {
            Assert.That(
                actual[component.Key].OrderBy(
                    static path => path,
                    StringComparer.Ordinal),
                Is.EqualTo(component.Value.OrderBy(
                    static path => path,
                    StringComparer.Ordinal)),
                component.Key);
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
            "algorithm-size-ratchets.json");
        var manifest = JsonSerializer.Deserialize<SizeRatchetManifest>(
            File.ReadAllText(path),
            SizeRatchetJsonOptions);
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

    private static IEnumerable<string> TransitiveProjectClosure(string root) {
        var pending = new Stack<string>(); var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.Count != 0) {
            var project = pending.Pop();
            if (!visited.Add(project)) continue;
            yield return project;
            foreach (var dependency in GetProjectReferences(project))
                pending.Push(dependency);
        }
    }

    private static string[] ProjectPackages(string project) => [..
        XDocument.Load(ProjectFile(project))
            .Descendants("PackageReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)];

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

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "System.Text.Json instantiates this model through reflection.")]
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

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "System.Text.Json instantiates this model through reflection.")]
    private sealed class SizeRatchetEntry {
        public string Path { get; init; } = "";

        public int MaximumPhysicalLines { get; init; }

        public int MaximumMemberLines { get; init; }
    }
}

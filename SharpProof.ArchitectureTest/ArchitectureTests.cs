using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Effects;
using SharpProof.Ir;
using SharpProof.Verify;
using static SharpProof.ArchitectureTest.ArchitectureRepository;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ArchitectureTests
{
    private static readonly JsonSerializerOptions SizeRatchetJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private static readonly string[] DeclarationOnlyTcbCoverageFiles =
    [
        "SharpProof.Analyzer.Core/EffectEvaluationTypes.cs"
    ];

    private static readonly string[] AcceptanceTimingPhases = [
        "restore",
        "static-validation",
        "build",
        "semantic-tests",
        "package-tests",
        "fuzz",
        "corpus-and-performance"
    ];

    [Test]
    public void RepositoryRestoreIsHermeticLockedAndSdkPinned()
    {
        var root = TestRepository.FindRoot();
        using var globalJson = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "global.json")));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                sdk.GetProperty("version").GetString(),
                Is.EqualTo("9.0.316"));
            Assert.That(
                sdk.GetProperty("rollForward").GetString(),
                Is.EqualTo("disable"));
        }

        var nuget = XDocument.Load(Path.Combine(root, "NuGet.Config"));
        var packageSources = nuget
            .Descendants("packageSources")
            .Single();
        var source = packageSources.Elements("add").Single();
        var mapping = nuget
            .Descendants("packageSourceMapping")
            .Single()
            .Elements("packageSource")
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                packageSources.Elements("clear"),
                Has.Exactly(1).Items);
            Assert.That(
                (string?)source.Attribute("key"),
                Is.EqualTo("nuget.org"));
            Assert.That(
                (string?)source.Attribute("value"),
                Is.EqualTo(
                    "https://api.nuget.org/v3/index.json"));
            Assert.That(
                (string?)mapping.Attribute("key"),
                Is.EqualTo("nuget.org"));
            Assert.That(
                (string?)mapping.Elements("package")
                    .Single()
                    .Attribute("pattern"),
                Is.EqualTo("*"));
        }

        var props = XDocument.Load(
            Path.Combine(root, "Directory.Build.props"));
        Assert.That(
            props.Descendants("RestorePackagesWithLockFile")
                .Single()
                .Value,
            Is.EqualTo("true"));
        Assert.That(
            props.Descendants("RestoreLockedMode")
                .Single()
                .Value,
            Is.EqualTo("true"));

        var solution = File.ReadAllText(
            Path.Combine(root, "SharpProof.sln"));
        var projects = Regex.Matches(
                solution,
                "\"([^\"]+\\.csproj)\"",
                RegexOptions.CultureInvariant)
            .Select(static match =>
                match.Groups[1].Value.Replace(
                    '\\',
                    Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.That(projects, Is.Not.Empty);
        Assert.That(
            projects.Where(project =>
                !File.Exists(Path.Combine(
                    Path.GetDirectoryName(
                        Path.Combine(root, project))!,
                    "packages.lock.json"))),
            Is.Empty);
    }

    [Test]
    public void NewLayerProjectReferencesFollowTheDependencyDag()
    {
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SharpProof.Analyzer"] = ["SharpProof.Analyzer.Core"],
            ["SharpProof.Analyzer.Core"] = [
                "SharpProof.Contracts",
                "SharpProof.Effects",
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Specs"
            ],
            ["SharpProof.Attributes"] = [],
            ["SharpProof.BuildTasks"] = [
                "SharpProof.Host",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.Host"] = [],
            ["SharpProof.Ir"] = [],
            ["SharpProof.Meta.Analyzers"] = [],
            ["SharpProof.CompilerArtifact"] = [
                "SharpProof.Ir",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.CompilerCollector"] = [
                "SharpProof.Analyzer.Core",
                "SharpProof.CompilerArtifact",
                "SharpProof.Contracts",
                "SharpProof.Effects",
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Specs",
                "SharpProof.Summaries",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.ContractForGenerator"] = [
                "SharpProof.Analyzer.Core",
                "SharpProof.Contracts"
            ],
            ["SharpProof.Specs"] = ["SharpProof.Ir"],
            ["SharpProof.Dataflow"] = [],
            ["SharpProof.Frontend"] = [
                "SharpProof.Attributes",
                "SharpProof.Ir"
            ],
            ["SharpProof.Fuzz"] = [
                "SharpProof.Frontend",
                "SharpProof.Host",
                "SharpProof.Ir",
                "SharpProof.Smt",
                "SharpProof.Testing",
                "SharpProof.Verify"
            ],
            ["SharpProof.Contracts"] = [
                "SharpProof.Frontend",
                "SharpProof.Ir"
            ],
            ["SharpProof.Effects"] = [
                "SharpProof.Dataflow",
                "SharpProof.Frontend",
                "SharpProof.Specs"
            ],
            ["SharpProof.Verify"] = ["SharpProof.Ir", "SharpProof.Specs"],
            ["SharpProof.Smt"] = ["SharpProof.Ir", "SharpProof.Verify"],
            ["SharpProof.Summaries"] = ["SharpProof.Ir"],
            ["SharpProof.Worker.Protocol"] = [],
            ["SharpProof.Worker"] = [
                "SharpProof.CompilerArtifact",
                "SharpProof.Dataflow",
                "SharpProof.Host",
                "SharpProof.Ir",
                "SharpProof.Smt",
                "SharpProof.Specs",
                "SharpProof.Verify",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.Worker.Launcher"] = [
                "SharpProof.CompilerArtifact",
                "SharpProof.Host",
                "SharpProof.Ir",
                "SharpProof.Specs",
                "SharpProof.Worker.Protocol"
            ]
        };

        foreach (var project in ProductionProjects)
        {
            var actual = ProjectReferences(project)
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
    public void WorkerAndLauncherRuntimeClosuresAreCompilerNeutral()
    {
        var forbiddenProjects = new[] {
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.Contracts",
            "SharpProof.Effects", "SharpProof.Frontend"
        };
        var roots = new[] { "SharpProof.Worker", "SharpProof.Worker.Launcher" };
        var snapshots = new Dictionary<string, ProjectFileSnapshot>(
            StringComparer.Ordinal);
        var pending = new Stack<string>(roots);
        while (pending.Count != 0)
        {
            var project = pending.Pop();
            if (snapshots.ContainsKey(project))
            {
                continue;
            }

            var snapshot = ArchitectureRepository.ReadProjectFileSnapshot(project);
            snapshots.Add(project, snapshot);
            foreach (var dependency in snapshot.References)
            {
                pending.Push(dependency);
            }
        }

        var sources = snapshots.Keys.ToDictionary(
            static project => project,
            ArchitectureRepository.ReadProductionSources,
            StringComparer.Ordinal);
        var closures = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var closure = new HashSet<string>(StringComparer.Ordinal);
            pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                var project = pending.Pop();
                if (!closure.Add(project))
                {
                    continue;
                }

                foreach (var dependency in snapshots[project].References)
                {
                    pending.Push(dependency);
                }
            }

            closures.Add(root, [.. closure]);
        }

        foreach (var root in roots)
        {
            var closure = closures[root];
            Assert.That(
                closure.Intersect(forbiddenProjects, StringComparer.Ordinal),
                Is.Empty,
                root);
            foreach (var project in closure)
            {
                Assert.That(
                    snapshots[project].Packages,
                    Has.None.StartsWith("Microsoft.CodeAnalysis"),
                    project);
                Assert.That(
                    sources[project],
                    Does.Not.Contain("Microsoft.CodeAnalysis"),
                    project);
                Assert.That(
                    snapshots[project].Text,
                    Does.Not.Contain("RoslynTargetsPath"),
                    project);
            }
        }
    }

    [Test]
    public void AnalyzerUtilitiesHasNoStaleBuildOrTestResidue()
    {
        foreach (var path in new[] {
                     "Directory.Build.targets",
                     "SharpProof.AnalyzerConsumer.props",
                     "SharpProof.Worker.Test/SharpProof.Worker.Test.csproj"
                 })
        {
            Assert.That(
                File.ReadAllText(Path.Combine(
                    TestRepository.FindRoot(), path.Replace('/', Path.DirectorySeparatorChar))),
                Does.Not.Contain("Microsoft.CodeAnalysis.AnalyzerUtilities"),
                path);
        }
    }

    [Test]
    public void LanguageNeutralLayersHaveNoCSharpSyntaxDependency()
    {
        foreach (var project in new[] {
                     "SharpProof.Ir",
                     "SharpProof.CompilerArtifact",
                     "SharpProof.Specs",
                     "SharpProof.Dataflow"
                 })
        {
            var source = ReadProductionSources(project);
            Assert.That(source, Does.Not.Contain("Microsoft.CodeAnalysis.CSharp"));
            Assert.That(source, Does.Not.Contain("SyntaxNode"));
            Assert.That(source, Does.Not.Contain("SyntaxKind"));
            Assert.That(source, Does.Not.Contain("SyntaxFactory"));
        }
    }

    [Test]
    public void OnlyTheSmtLayerAndFuzzHarnessReferenceZ3InTheProductionGraph()
    {
        foreach (var project in ProductionProjects)
        {
            var xml = XDocument.Load(ProjectFile(project));
            var packages = xml
                .Descendants("PackageReference")
                .Select(static element => (string?)element.Attribute("Include"))
                .Where(static value => value != null)
                .ToArray();
            Assert.That(
                packages.Contains("Microsoft.Z3", StringComparer.Ordinal),
                Is.EqualTo(project is "SharpProof.Smt" or "SharpProof.Fuzz"),
                project);
        }
    }

    [Test]
    public void IrTermPayloadsDoNotExposeSemanticStrings()
    {
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
    public void SemanticConsumersDoNotEncodeFrameworkIdentitiesAsStringLiterals()
    {
        var semanticConsumers = new[] {
            "SharpProof.Analyzer",
            "SharpProof.CompilerArtifact",
            "SharpProof.CompilerCollector",
            "SharpProof.Contracts",
            "SharpProof.Dataflow",
            "SharpProof.Effects",
            "SharpProof.Frontend",
            "SharpProof.Host",
            "SharpProof.Ir",
            "SharpProof.Smt",
            "SharpProof.Summaries",
            "SharpProof.Verify",
            "SharpProof.Worker",
            "SharpProof.Worker.Protocol",
            "SharpProof.Worker.Launcher"
        };
        var violations = semanticConsumers
            .SelectMany(ProductionSourceFiles)
            .Where(file =>
                !string.Equals(
                    TestRepository.Relative(file),
                    "SharpProof.Frontend/ContractApiMetadataRuntime.cs",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    TestRepository.Relative(file),
                    "SharpProof.Frontend/ContractApiMetadata.generated.cs",
                    StringComparison.Ordinal))
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
                .Select(literal =>
                {
                    var line = literal.GetLocation()
                        .GetLineSpan()
                        .StartLinePosition.Line + 1;
                    return $"{TestRepository.Relative(file)}:{line}: " +
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
    public void EffectArrayCardinalityRequiresCompilerBoundSymbolIdentity()
    {
        var source = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Effects",
            "OperationEffectScanner.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                source,
                Does.Contain(
                    "property.Instance?.Type is IArrayTypeSymbol &&"));
            Assert.That(
                source,
                Does.Contain(
                    "CompilerIdentityBridge." +
                    "IsIntrinsicSequenceLength(property);"));
            Assert.That(
                source,
                Does.Not.Contain(
                    "property.Property.Name is \"Length\" or \"LongLength\""));
        }
    }

    [Test]
    public void FrontendUsesOnlyTotalCompilerBoundLowering()
    {
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
    public void ProofProducingOutcomeConstructorsStayInTheKernel()
    {
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
            FindRelativeCallers(productionFiles, "new Assumption("),
            Is.EqualTo([
                "SharpProof.Worker/CallableEvidenceBuilder.cs",
                "SharpProof.Worker/PostconditionObligationBuilder.cs",
                "Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs"
            ]));
        Assert.That(
            FindRelativeCallers(productionFiles, "new EffectSummary("),
            Is.EqualTo([
                "SharpProof.Effects/EffectSummary.cs",
                "SharpProof.Effects/EffectSummaryOperations.cs",
                "SharpProof.Effects/ExternalEffectResolver.cs"
            ]));
    }

    [Test]
    public void AlgorithmLayerSizeRatchetManifestIsWellFormed()
    {
        var manifest = ReadSizeRatchetManifest();
        var expectedPaths = new[] {
            "SharpProof.Dataflow/ForwardDataflowAnalysis.cs",
            "SharpProof.Dataflow/IntervalDomain.cs",
            "SharpProof.Dataflow/NullnessDomain.cs",
            "SharpProof.Dataflow/SequenceCardinalityDomain.cs",
            "SharpProof.Effects/EffectAnalysisSession.cs",
            "SharpProof.Effects/ExternalEffectResolver.cs",
            "SharpProof.Effects/OperationEffectScanner.Assignments.cs",
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
        Assert.That(manifest.SchemaVersion, Is.EqualTo(2));
        Assert.That(manifest.Rationale, Is.Not.Empty);
        Assert.That(manifest.Measurement.FileExpressionNodes, Is.Not.Empty);
        Assert.That(manifest.Measurement.MemberExpressionNodes, Is.Not.Empty);
        Assert.That(manifest.Measurement.FileDecisionPoints, Is.Not.Empty);
        Assert.That(manifest.Measurement.MemberDecisionPoints, Is.Not.Empty);
        Assert.That(
            manifest.Files
                .Select(static entry => entry.Path)
                .OrderBy(static path => path, StringComparer.Ordinal),
            Is.EqualTo(expectedPaths.OrderBy(
                static path => path,
                StringComparer.Ordinal)));

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            Assert.That(entry.Path, Is.EqualTo(entry.Path.Replace('\\', '/')));
            Assert.That(entry.Path, Does.EndWith(".cs"));
            Assert.That(
                paths.Add(entry.Path),
                Is.True,
                $"Duplicate size-ratchet entry: {entry.Path}");
            Assert.That(entry.MaximumFileExpressionNodes, Is.Positive, entry.Path);
            Assert.That(entry.MaximumMemberExpressionNodes, Is.Positive, entry.Path);
            Assert.That(entry.MaximumFileDecisionPoints, Is.Positive, entry.Path);
            Assert.That(entry.MaximumMemberDecisionPoints, Is.Positive, entry.Path);

            var fullPath = Path.GetFullPath(
                Path.Combine(TestRepository.FindRoot(), entry.Path));
            Assert.That(
                fullPath.StartsWith(
                    TestRepository.FindRoot() + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                Is.True,
                entry.Path);
            Assert.That(File.Exists(fullPath), Is.True, entry.Path);
        }
    }

    [Test]
    public void TrustedComputingBaseDeclarationNamesEveryRequiredPath()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(), "eng", "acceptance", "contract.json")));
        var root = document.RootElement;
        Assert.That(
            root.GetProperty("trustedKernel")
                .GetProperty("paths")
                .EnumerateArray()
                .Select(static path => path.GetString() ?? "")
                .Where(static path => path.Length != 0),
            Is.Not.Empty,
            "The trusted kernel must declare at least one path.");
        Assert.That(
            root.GetProperty("trustedKernel")
                .TryGetProperty("maximumNonblankLines", out _),
            Is.False);
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
        // contract.json is the reviewed source of path ownership.
        Assert.That(actual, Is.Not.Empty);
        foreach (var component in actual)
        {
            Assert.That(
                component.Key,
                Is.Not.Empty,
                "Every trusted-computing-base component must be named.");
            Assert.That(
                component.Value,
                Is.Not.Empty,
                component.Key);
            Assert.That(
                component.Value.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(component.Value.Length),
                component.Key + " declares a path twice.");
        }
        var canonicalTcb = root.GetProperty("trustedKernel")
            .GetProperty("paths")
            .EnumerateArray()
            .Select(static path => path.GetString() ?? "")
            .Concat(actual.Values.SelectMany(static paths => paths))
            .ToArray();
        Assert.That(
            canonicalTcb.Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(canonicalTcb.Length),
            "The canonical TCB union must not contain duplicate ownership.");
        var mutationCatalog = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofTrustedMutations.ps1"));
        var mutationTargets = Regex.Matches(
                mutationCatalog,
                @"(?m)^\s*File\s*=\s*'([^']+)'\s*$")
            .Select(static match => match.Groups[1].Value.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.That(mutationTargets, Is.Not.Empty);
        Assert.That(
            mutationTargets.Except(canonicalTcb, StringComparer.Ordinal),
            Is.Empty,
            "Every trusted-mutation target must be owned by the canonical TCB.");

        // Every declared path must also resolve to a file inside the tree.
        foreach (var path in canonicalTcb)
        {
            var full = Path.GetFullPath(Path.Combine(TestRepository.FindRoot(), path));
            Assert.That(
                full.StartsWith(
                    TestRepository.FindRoot() + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                Is.True,
                path + " escapes the repository root.");
            Assert.That(
                File.Exists(full),
                Is.True,
                path + " is declared in the trusted computing base but missing.");
        }
        // Keep a readable tripwire for the highest-risk owners.
        Assert.That(
            canonicalTcb,
            Does.Contain("SharpProof.Verify/Evidence.cs")
                .And.Contain("SharpProof.Verify/Outcomes.cs")
                .And.Contain("SharpProof.Effects/TrustedBoundaryPolicy.cs")
                .And.Contain("SharpProof.Analyzer.Core/LanguageSubsetGate.cs")
                .And.Contain("SharpProof.Frontend/OperationSubsetClassifier.cs")
                .And.Contain("SharpProof.Frontend/FrontendSubset.cs")
                .And.Contain("SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs")
                .And.Contain("SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs")
                .And.Contain("SharpProof.Verify/Backend.cs")
                .And.Contain("SharpProof.Contracts/ContractApiSymbols.cs")
                .And.Contain("SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs")
                .And.Contain("SharpProof.Attributes/EffectContractAttribute.cs")
                .And.Contain("SharpProof.Frontend/CompilationModelProvider.cs")
                .And.Contain("SharpProof.Contracts/ContractForSymbolMatcher.cs")
                .And.Contain("SharpProof.Contracts/ContractClauseInventory.cs")
                .And.Contain("SharpProof.Attributes/SharpProofEffect.cs")
                .And.Contain("SharpProof.Attributes/SharpProofCapability.cs")
                .And.Contain("SharpProof.Worker/Program.cs"));
        Assert.That(
            File.ReadAllText(Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "Test-SharpProofCoverage.ps1")),
            Does.Contain("Get-SharpProofTcbPaths")
                .And.Contain("$canonicalTcbPaths")
                .And.Contain("$coverageTcbPaths")
                .And.Contain("$changedTcbFiles")
                .And.Contain("ComparisonRef is required")
                .And.Contain("contract.json")
                .And.Contain("-Contract $contract")
                .And.Contain("-IncludeAcceptanceContract")
                .And.Contain("EndsWith('.cs'")
                .And.Contain("changedMetadataFiles"));
    }

    [Test]
    public void DeclarationOnlyTcbCoverageExceptionsAreExplicitAndNonExecutable()
    {
        var repository = TestRepository.FindRoot();
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "eng",
            "coverage",
            "baseline.json")));
        var paths = baseline.RootElement
            .GetProperty("declarationOnlyTcbFiles")
            .EnumerateArray()
            .Select(static value => value.GetString() ?? string.Empty)
            .ToArray();

        Assert.That(
            paths,
            Is.EqualTo(DeclarationOnlyTcbCoverageFiles));
        foreach (var path in paths)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(
                    repository,
                    path.Replace('/', Path.DirectorySeparatorChar))))
                .GetCompilationUnitRoot();
            var declarations = root.DescendantNodes()
                .Where(static node => node is BaseTypeDeclarationSyntax)
                .ToArray();

            Assert.That(declarations, Is.Not.Empty, path);
            Assert.That(
                declarations,
                Is.All.InstanceOf<EnumDeclarationSyntax>(),
                path);
            Assert.That(
                root.DescendantNodes().Any(static node =>
                    node is StatementSyntax or
                        ArrowExpressionClauseSyntax or
                        EqualsValueClauseSyntax),
                Is.False,
                path);
        }
    }

    [Test]
    public void PreviewConfigurationInterfaceMatchesFrozenSnapshot()
    {
        var repository = TestRepository.FindRoot();
        using var snapshotDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(
                repository,
                "eng",
                "acceptance",
                "preview-interface.v1.json")));
        using var contractDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repository, "eng", "acceptance", "contract.json")));
        using var compilerSchemaDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(
                repository,
                "SharpProof.CompilerArtifact",
                "CompilerArtifactModel.schema.json")));
        var snapshot = snapshotDocument.RootElement;
        var active = snapshot.GetProperty("msbuildProperties")
            .EnumerateArray()
            .Select(static value => value.GetString() ?? string.Empty)
            .ToArray();
        var retired = snapshot.GetProperty("retiredMsbuildProperties")
            .EnumerateArray()
            .Select(static value => value.GetString() ?? string.Empty)
            .ToArray();
        string[] buildFilePaths =
        [
            "SharpProof.Package/buildTransitive/SharpProof.props",
            "SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props",
            "SharpProof.Package/buildTransitive/SharpProof.targets",
            "SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props",
            "SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets"
        ];
        var buildFiles = buildFilePaths
            .Select(path => File.ReadAllText(Path.Combine(repository, path)))
            .ToArray();
        var parsedBuildFiles = buildFiles
            .Select(static text => (Text: text, Document: XDocument.Parse(text)))
            .ToArray();
        var combinedBuildSurface = string.Join("\n", buildFiles);
        var actualPublicProperties = parsedBuildFiles
            .SelectMany(static buildFile =>
                buildFile.Document
                    .Descendants("PropertyGroup")
                    .Elements()
                    .Select(static element => element.Name.LocalName)
                    .Concat(buildFile.Document
                        .Descendants("CompilerVisibleProperty")
                        .Select(static element =>
                            element.Attribute("Include")?.Value)
                        .Where(static value => value != null)
                        .SelectMany(static value => value!.Split(
                            ';',
                            StringSplitOptions.RemoveEmptyEntries)))
                    .Concat(Regex.Matches(
                            buildFile.Text,
                            @"\$\((SharpProof[A-Za-z0-9_]+)\)",
                            RegexOptions.CultureInvariant)
                        .Select(static match => match.Groups[1].Value)))
            .Where(static name => name.StartsWith(
                "SharpProof",
                StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var frozenPublicProperties = active
            .Concat(retired)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var compilerVisible = parsedBuildFiles
            .SelectMany(static buildFile => buildFile.Document
                .Descendants()
                .Where(static element =>
                    element.Name.LocalName == "CompilerVisibleProperty"))
            .Select(static value => value.Attribute("Include")?.Value)
            .Where(static value => value != null)
            .SelectMany(static value => value!.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        var dogfoodCompilerVisible = new[]
        {
            Path.Combine(repository, "SharpProof.AnalyzerConsumer.props"),
            Path.Combine(
                repository,
                "eng",
                "self-application",
                "SharpProof.SelfApplication.props")
        }
        .Select(path => XDocument.Load(path)
            .Descendants()
            .Where(static element =>
                element.Name.LocalName == "CompilerVisibleProperty")
            .Select(static element => element.Attribute("Include")?.Value)
            .Where(static value => value != null)
            .SelectMany(static value => value!.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray())
        .ToArray();
        var expectedCompilerVisible = compilerVisible
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var contract = contractDocument.RootElement;
        var worker = contract.GetProperty("worker");
        var cache = contract.GetProperty("cache");
        var versions = snapshot.GetProperty("versions");
        var referenceLimits = compilerSchemaDocument.RootElement
            .GetProperty("declarations")
            .EnumerateArray()
            .Single(static declaration =>
                declaration.GetProperty("name").GetString() ==
                    "CompilerReferenceLimits")
            .GetProperty("constants")
            .EnumerateArray()
            .ToDictionary(
                static constant => constant.GetProperty("name").GetString()!,
                static constant => constant.GetProperty("value").GetInt32(),
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(
                active,
                Is.EqualTo(active.OrderBy(
                    static value => value,
                    StringComparer.Ordinal)));
            Assert.That(active, Is.Unique);
            Assert.That(retired, Is.Unique);
            Assert.That(active.Intersect(retired, StringComparer.Ordinal), Is.Empty);
            Assert.That(
                actualPublicProperties,
                Is.EqualTo(frozenPublicProperties),
                "Every SharpProof* MSBuild property consumed or declared by " +
                "the shipping build files must be frozen in the preview snapshot.");
            Assert.That(dogfoodCompilerVisible[0], Is.EqualTo(expectedCompilerVisible));
            Assert.That(dogfoodCompilerVisible[1], Is.EqualTo(expectedCompilerVisible));
            foreach (var property in active)
            {
                Assert.That(
                    actualPublicProperties,
                    Does.Contain(property),
                    property);
            }
            foreach (var property in retired)
            {
                Assert.That(compilerVisible, Does.Not.Contain(property));
                Assert.That(
                    combinedBuildSurface,
                    Does.Contain(property + " was removed"),
                    property);
            }
            Assert.That(
                versions.GetProperty("workerProtocol").GetInt32(),
                Is.EqualTo(worker.GetProperty("protocolVersion").GetInt32()));
            Assert.That(
                versions.GetProperty("workerManifest").GetInt32(),
                Is.EqualTo(worker.GetProperty("manifestSchemaVersion").GetInt32()));
            Assert.That(
                versions.GetProperty("compilerArtifact").GetInt32(),
                Is.EqualTo(worker.GetProperty("compilerArtifactSchemaVersion").GetInt32()));
            Assert.That(
                versions.GetProperty("relationalSummary").GetInt32(),
                Is.EqualTo(worker.GetProperty("relationalSummarySchemaVersion").GetInt32()));
            Assert.That(
                versions.GetProperty("specificationPack").GetInt32(),
                Is.EqualTo(worker.GetProperty("specificationPackSchemaVersion").GetInt32()));
            Assert.That(
                versions.GetProperty("workerCache").GetInt32(),
                Is.EqualTo(cache.GetProperty("schemaVersion").GetInt32()));
            Assert.That(
                referenceLimits["MaximumModuleBytes"],
                Is.EqualTo(worker.GetProperty(
                    "maximumCompilerReferenceModuleBytes").GetInt32()));
            Assert.That(
                referenceLimits["MaximumClosureBytes"],
                Is.EqualTo(worker.GetProperty(
                    "maximumCompilerReferenceClosureBytes").GetInt32()));
            Assert.That(
                referenceLimits["MaximumModuleCount"],
                Is.EqualTo(worker.GetProperty(
                    "maximumCompilerReferenceModules").GetInt32()));
        }
    }

    [Test]
    public void PilotPackageVersionDerivesFromReleaseOwner()
    {
        var pilotProps = XDocument.Load(Path.Combine(
            TestRepository.FindRoot(),
            "eng",
            "pilots",
            "Directory.Build.props"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                pilotProps.Descendants("Import")
                    .Select(static element =>
                        element.Attribute("Project")?.Value),
                Does.Contain(
                    "$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))"));
            Assert.That(
                pilotProps.Descendants("SharpProofPilotVersion")
                    .Single()
                    .Value,
                Is.EqualTo("$(SharpProofPackageVersion)"));
        }
    }

    [Test]
    public void PilotRunnerPreservesRootedInputAndOutputPaths()
    {
        var script = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofPilots.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("[IO.Path]::IsPathRooted($Path)"));
            Assert.That(
                script,
                Does.Contain("$resolvedPackageSource = Resolve-RepositoryPath $PackageSource"));
            Assert.That(
                script,
                Does.Contain("Resolve-SharpProofContainedPath `"));
            Assert.That(script, Does.Not.Contain("Get-CimInstance"));
            Assert.That(
                script,
                Does.Contain("[IO.Directory]::EnumerateDirectories('/proc')"));
        }
    }

    [Test]
    public void ContainerConsumerMatrixUsesCatalogOwnedNet8ReferencePacks()
    {
        var script = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofPackageConsumers.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                script,
                Does.Contain("$toolchain.dotnet.testRuntimeVersion"));
            Assert.That(script, Does.Contain("microsoft.netcore.app.ref"));
            Assert.That(script, Does.Contain("microsoft.aspnetcore.app.ref"));
            Assert.That(
                script,
                Does.Contain("Pattern = 'Microsoft.NETCore.App.Ref'"));
            Assert.That(
                script,
                Does.Contain("Pattern = 'Microsoft.AspNetCore.App.Ref'"));
            Assert.That(
                script,
                Does.Contain("Select-Object -ExpandProperty Pattern -Unique"));
        }
    }

    [Test]
    public void WorkflowCommandsUsePowerShellSafeMsBuildSwitches()
    {
        var violations = WorkflowFiles()
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new
                {
                    Path = path,
                    Line = line,
                    Number = index + 1
                }))
            .Where(static entry => Regex.IsMatch(
                entry.Line,
                @"(?:^|\s)-[mp]:",
                RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase))
            .Select(static entry =>
                $"{Path.GetFileName(entry.Path)}:{entry.Number}: " +
                entry.Line.Trim())
            .ToArray();

        Assert.That(
            violations,
            Is.Empty,
            "Use /p: and /m: in workflow PowerShell commands so script " +
            "parameter binding cannot consume MSBuild switches." +
            Environment.NewLine +
                string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void ReleasePackageWorkflowBindsTheExactRepositoryCommit()
    {
        var root = TestRepository.FindRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var build = File.ReadAllText(Path.Combine(root, "build.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                container,
                Does.Contain(
                    "$repositoryCommitProperty = \"/p:RepositoryCommit=$repositoryCommit\""));
            Assert.That(container, Does.Contain("$repositoryCommitProperty)"));
            Assert.That(
                container,
                Does.Contain("'/p:GeneratePackageOnBuild=false'"));
            Assert.That(
                container,
                Does.Contain("'--no-build', '--no-restore'"));
            Assert.That(
                workflow,
                Does.Contain("docker compose run --rm tooling pack"));
            Assert.That(build, Does.Contain("Invoke-Container $Profile"));
            Assert.That(workflow, Does.Contain("fetch-depth: 0"));
        }
    }

    [Test]
    public void ContainerMutationEvidenceUsesAPersistentRelativePath()
    {
        var container = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Invoke-SharpProofContainer.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                container,
                Does.Contain(
                    "$mutationOutput = " +
                    "'artifacts/mutation/trusted-mutations.json'"));
            Assert.That(
                container,
                Does.Contain("OutputPath = $mutationOutput"));
            Assert.That(
                container,
                Does.Contain(
                    "Invoke-SharpProofTrustedMutationsParallel.ps1"));
            Assert.That(
                container,
                Does.Not.Contain(
                    "-OutputPath (Join-Path $mutationRoot " +
                    "'trusted-mutations.json')"));

            var mutationDriver = File.ReadAllText(Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "Test-SharpProofTrustedMutations.ps1"));
            Assert.That(
                mutationDriver,
                Does.Contain("-EvidenceSelection inProgress"));
            Assert.That(
                mutationDriver,
                Does.Contain("$completedMutationNames.Contains"));

            var parallelDriver = File.ReadAllText(Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "Invoke-SharpProofTrustedMutationsParallel.ps1"));
            Assert.That(parallelDriver, Does.Contain("MutationShardCount"));
            Assert.That(parallelDriver, Does.Contain("Get-CompleteShard"));
            Assert.That(
                parallelDriver,
                Does.Contain("weighted-longest-processing-time-first"));
            Assert.That(
                parallelDriver,
                Does.Contain("selection = 'full'"));

            var scheduler = File.ReadAllText(Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "SharpProof.MutationScheduling.psm1"));
            Assert.That(scheduler, Does.Contain("CatalogOrdinal"));
            Assert.That(scheduler, Does.Contain("Sort-Object"));
            Assert.That(scheduler, Does.Not.Contain("chunkSize"));
        }
    }

    [Test]
    public void ContainerDependencyAuditRestoresTheDisposableClone()
    {
        var container = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var auditHelper = container.IndexOf(
            "function Invoke-DependencyAudit",
            StringComparison.Ordinal);
        var restore = container.IndexOf(
            "Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')",
            auditHelper,
            StringComparison.Ordinal);
        var audit = container.IndexOf(
            "Test-SharpProofDependencyAudit.ps1",
            auditHelper,
            StringComparison.Ordinal);
        var branch = container.IndexOf(
            "'dependency-audit' {",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditHelper, Is.GreaterThanOrEqualTo(0));
            Assert.That(restore, Is.GreaterThanOrEqualTo(0));
            Assert.That(audit, Is.GreaterThan(restore));
            Assert.That(branch, Is.GreaterThan(auditHelper));
            Assert.That(
                container.IndexOf("Invoke-DependencyAudit", branch, StringComparison.Ordinal),
                Is.GreaterThan(branch));
        }
    }

    [Test]
    public void ContainerPackageConsumersRestoreBeforeBuildingOfflineFeed()
    {
        var container = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var branchStart = container.IndexOf(
            "'package-consumers' {",
            StringComparison.Ordinal);
        var branchEnd = container.IndexOf(
            "'samples' {",
            branchStart,
            StringComparison.Ordinal);
        var branch = container[branchStart..branchEnd];
        var restore = branch.IndexOf(
            "Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')",
            StringComparison.Ordinal);
        var consumer = branch.IndexOf(
            "Test-SharpProofPackageConsumers.ps1",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restore, Is.GreaterThanOrEqualTo(0));
            Assert.That(consumer, Is.GreaterThan(restore));
        }
    }

    [Test]
    public void ContainerTestConcurrencyIsCatalogOwnedAndProjectScoped()
    {
        var root = TestRepository.FindRoot();
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "contract.json")));
        var automation = contract.RootElement.GetProperty("automation");
        var container = contract.RootElement.GetProperty("container");
        var compose = File.ReadAllText(Path.Combine(root, "compose.yaml"));
        var execution = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "SharpProof.ContainerExecution.psm1"));
        var acceptance = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "Verify.ps1"));
        var semanticTests = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofSemanticTests.ps1"));
        var portable = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Portable.Tests.slnf"));
        var mutationDriver = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofTrustedMutations.ps1"));
        var parallelMutationDriver = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofTrustedMutationsParallel.ps1"));
        var packageTests = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofPackageTests.ps1"));
        var developerCheck = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofDevCheck.ps1"));
        var mutationProjects = Regex.Matches(
                mutationDriver,
                @"(?m)^\s*Project\s*=\s*'([^']+)'\s*$")
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var weightedProjects = automation
            .GetProperty("mutationProjectWeights")
            .EnumerateObject()
            .Select(static property => property.Name)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(container.GetProperty("defaultCpuLimit").GetInt32(),
                Is.Zero);
            Assert.That(container.GetProperty("defaultMemoryMiB").GetInt32(),
                Is.EqualTo(40 * 1024));
            Assert.That(
                automation.GetProperty("testProjectCpuDivisor").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                automation.GetProperty("packageTestCpuPercent").GetInt32(),
                Is.EqualTo(75));
            Assert.That(
                automation.GetProperty("buildCpuPercent").GetInt32(),
                Is.EqualTo(75));
            Assert.That(
                automation.GetProperty("mutationParallelism").GetInt32(),
                Is.EqualTo(4));
            Assert.That(
                automation.GetProperty("mutationDefaultWeight").GetInt32(),
                Is.Positive);
            Assert.That(weightedProjects, Is.EqualTo(mutationProjects));
            Assert.That(
                automation.GetProperty("mutationProjectWeights")
                    .EnumerateObject()
                    .Select(static property => property.Value.GetInt32()),
                Is.All.Positive);
            Assert.That(compose, Does.Contain("CPU_LIMIT:-0"));
            Assert.That(compose, Does.Contain("MEMORY_LIMIT:-40g"));
            Assert.That(
                compose,
                Does.Contain("SHARPPROOF_TEST_PROJECT_PARALLELISM"));
            Assert.That(compose, Does.Contain("SHARPPROOF_TMPFS_SIZE"));
            Assert.That(execution, Does.Contain("Environment]::ProcessorCount"));
            Assert.That(
                execution,
                Does.Contain("SHARPPROOF_TEST_PROJECT_PARALLELISM"));
            Assert.That(
                acceptance,
                Does.Contain("Invoke-SharpProofSemanticTests.ps1"));
            Assert.That(
                acceptance,
                Does.Contain("Invoke-SharpProofPackageTests.ps1"));
            Assert.That(
                semanticTests,
                Does.Contain("SharpProof.Semantic.Tests.slnf"));
            Assert.That(semanticTests, Does.Contain("ProjectParallelism"));
            Assert.That(
                semanticTests,
                Does.Contain("worker-claim-manifest"));
            Assert.That(
                semanticTests,
                Does.Contain("artifacts/timings"));
            Assert.That(
                packageTests,
                Does.Contain("priorMethodMilliseconds"));
            Assert.That(packageTests, Does.Contain("Sort-Object"));
            Assert.That(
                packageTests,
                Does.Contain("workerMethods = $workerMethodTimings"));
            Assert.That(
                packageTests,
                Does.Contain(
                    "$directVstest = -not $coverageEnabled -and"));
            Assert.That(
                packageTests,
                Does.Contain("'/TestCaseFilter:' + $shard.Filter"));
            Assert.That(
                packageTests,
                Does.Contain("'/ResultsDirectory:' + ("));
            Assert.That(
                packageTests,
                Does.Contain("Join-Path $results $shard.Name)"));
            Assert.That(
                mutationDriver,
                Does.Contain("Get-SharpProofMutationBaselinePlan"));
            Assert.That(
                mutationDriver,
                Does.Not.Contain("($filters -join '|')"));
            Assert.That(
                mutationDriver,
                Does.Contain("baselineInvocationCount"));
            Assert.That(
                parallelMutationDriver,
                Does.Contain("-BaselineOnly"));
            Assert.That(
                parallelMutationDriver,
                Does.Contain("-BaselineEvidencePath"));
            Assert.That(
                parallelMutationDriver,
                Does.Contain("focused-baseline-v3"));
            Assert.That(
                developerCheck,
                Does.Contain("Invoke-SharpProofSemanticTests.ps1"));
            Assert.That(
                developerCheck,
                Does.Contain("performance-smoke"));
            Assert.That(
                portable,
                Does.Not.Contain("SharpProof.Package.Test"));
            Assert.That(
                portable,
                Does.Not.Contain("SharpProof.Worker.Test"));
        }
    }

    [Test]
    public void AcceptanceTimingEvidenceHasACatalogOwnedCanonicalShape()
    {
        var root = TestRepository.FindRoot();
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "contract.json")));
        var phases = contract.RootElement
            .GetProperty("automation")
            .GetProperty("acceptanceTimingPhases")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .ToArray();
        var acceptance = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "Verify.ps1"));
        var container = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(phases, Is.EqualTo(AcceptanceTimingPhases));
            Assert.That(phases, Is.Unique);
            Assert.That(
                acceptance,
                Does.Contain("acceptanceTimingPhases"));
            Assert.That(
                acceptance,
                Does.Contain("totalElapsedMilliseconds"));
            Assert.That(
                acceptance,
                Does.Contain("Move-Item -LiteralPath $temporary"));
            Assert.That(
                container,
                Does.Not.Contain("SHARPPROOF_ACCEPTANCE_RESTORE_MILLISECONDS"));
        }
    }

    [Test]
    public void DevContainerIsNonRootPinnedAndDoesNotNestDocker()
    {
        var root = TestRepository.FindRoot();
        var rawConfiguration = File.ReadAllText(Path.Combine(
            root,
            ".devcontainer",
            "devcontainer.json"));
        using var document = JsonDocument.Parse(rawConfiguration);
        var configuration = document.RootElement;
        var dockerfile = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "container",
            "Dockerfile"));
        var initialization = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "container",
            "dev-init.sh"));
        var developerCommand = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "container",
            "dev-command.sh"));
        var compose = File.ReadAllText(Path.Combine(root, "compose.yaml"));
        var gitIgnore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        var dockerIgnore = File.ReadAllText(Path.Combine(root, ".dockerignore"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                configuration.GetProperty("remoteUser").GetString(),
                Is.EqualTo("sharpproof"));
            Assert.That(
                configuration.GetProperty("containerUser").GetString(),
                Is.EqualTo("sharpproof"));
            Assert.That(
                configuration.TryGetProperty("containerEnv", out _),
                Is.False,
                "Compose owns development environment variables.");
            Assert.That(
                configuration.TryGetProperty("forwardPorts", out _),
                Is.False,
                "No development ports are forwarded.");
            Assert.That(
                configuration.GetProperty("postCreateCommand").GetString(),
                Is.EqualTo("sharpproof-dev-init"));
            Assert.That(
                configuration.GetProperty("postStartCommand").GetString(),
                Is.EqualTo("sp contract"));
            Assert.That(
                configuration.TryGetProperty("initializeCommand", out _),
                Is.False,
                "Dev Containers must not invoke host Git or other host tooling.");
            Assert.That(rawConfiguration, Does.Not.Contain("pwsh"));
            Assert.That(dockerfile, Does.Contain("/usr/local/bin/sp"));
            Assert.That(
                dockerfile,
                Does.Contain("/usr/local/bin/sharpproof-dev-init"));
            Assert.That(initialization, Does.Contain("SHARPPROOF_ORIGIN_URL"));
            Assert.That(initialization, Does.Contain("SHARPPROOF_DEV_REF"));
            Assert.That(
                initialization,
                Does.Contain("git \"${clone_arguments[@]}\""));
            Assert.That(initialization, Does.Not.Contain("git bundle"));
            Assert.That(initialization, Does.Not.Contain("repository.bundle"));
            Assert.That(initialization, Does.Not.Contain("SHARPPROOF_SEED_ROOT"));
            Assert.That(initialization, Does.Not.Contain("tar "));
            Assert.That(initialization, Does.Not.Contain("reset --mixed"));
            Assert.That(initialization, Does.Contain("sp restore"));
            Assert.That(initialization, Does.Not.Contain("docker"));
            Assert.That(developerCommand, Does.Contain("Invoke-SharpProofContainer.ps1"));
            Assert.That(developerCommand, Does.Not.Contain("docker"));
            Assert.That(
                compose,
                Does.Contain("sharpproof-workspace:/workspace/SharpProof"));
            Assert.That(compose, Does.Not.Contain("/workspace/seed"));
            Assert.That(compose, Does.Not.Contain("SHARPPROOF_SEED_ROOT"));
            Assert.That(gitIgnore, Does.Not.Contain("repository.bundle"));
            Assert.That(dockerIgnore, Does.Not.Contain("repository.bundle"));
        }
    }

    [Test]
    public void CanonicalTaskSetupCopiesOnlyWorkingTreeDeltas()
    {
        var entrypoint = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "eng",
            "container",
            "entrypoint.sh"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                entrypoint,
                Does.Contain("--binary --full-index --no-ext-diff HEAD"));
            Assert.That(
                entrypoint,
                Does.Contain("--binary --whitespace=nowarn -"));
            Assert.That(
                entrypoint,
                Does.Contain("--others --exclude-standard -z --"));
        }
    }

    [Test]
    public void RepositoryMsBuildEntryPointsRejectHostExecution()
    {
        var targets = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "Directory.Build.targets"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                targets,
                Does.Contain("_RequireSharpProofCanonicalContainer"));
            Assert.That(targets, Does.Contain("BeforeTargets=\"Restore;PrepareForBuild\""));
            Assert.That(targets, Does.Contain("'$(SHARPPROOF_CONTAINER)' != '1'"));
            Assert.That(
                targets,
                Does.Contain("$(SHARPPROOF_CONTAINER_CONTRACT)"));
            Assert.That(targets, Does.Contain("Docker Compose tooling container"));
        }
    }

    [Test]
    public void MutationCatalogTargetsArePreflightedBeforeTests()
    {
        var mutationDriver = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "scripts",
            "Test-SharpProofTrustedMutations.ps1"));
        var archiveExpansion = mutationDriver.IndexOf(
            "Expand-Archive -LiteralPath $archive -DestinationPath $sourceRoot",
            StringComparison.Ordinal);
        var preflight = mutationDriver.IndexOf(
            "Assert-UniqueMutationTarget `",
            archiveExpansion,
            StringComparison.Ordinal);
        var restore = mutationDriver.IndexOf(
            "$restoreRun = Invoke-IsolatedDotnet",
            archiveExpansion,
            StringComparison.Ordinal);
        var baseline = mutationDriver.IndexOf(
            "$baselineTrxName =",
            archiveExpansion,
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(archiveExpansion, Is.GreaterThanOrEqualTo(0));
            Assert.That(preflight, Is.GreaterThan(archiveExpansion));
            Assert.That(restore, Is.GreaterThan(preflight));
            Assert.That(baseline, Is.GreaterThan(restore));
        }
    }

    [Test]
    public void RepositoryAutomationRunsProductToolingOnlyInDocker()
    {
        var workflows = WorkflowFiles()
            .Select(File.ReadAllText)
            .ToArray();
        var productWorkflows = workflows
            .Where(static workflow => workflow.Contains(
                "SharpProof",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workflows, Has.None.Contain("actions/setup-dotnet"));
            Assert.That(workflows, Has.None.Contain("runs-on: windows"));
            Assert.That(workflows, Has.None.Contain("runs-on: macos"));
            Assert.That(workflows, Has.None.Contain("shell: pwsh"));
            Assert.That(productWorkflows, Has.Some.Contain("docker compose"));
        }
    }

    [Test]
    public void DockerWorkflowsCapCpuUseToHostedRunnerCapacity()
    {
        var dockerWorkflows = WorkflowFiles()
            .Where(static path => File.ReadAllText(path).Contains(
                "docker compose",
                StringComparison.Ordinal))
            .ToArray();

        Assert.That(dockerWorkflows, Is.Not.Empty);
        foreach (var workflow in dockerWorkflows)
        {
            Assert.That(
                File.ReadAllText(workflow),
                Does.Contain("SHARPPROOF_CONTAINER_CPU_LIMIT: 4"),
                Path.GetFileName(workflow));
        }
    }

    [Test]
    public void WorkerProcessBoundaryUsesADirectLinuxChildAndStdinRelease()
    {
        var host = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Host",
            "LinuxWorkerProcess.cs"));
        var launcher = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Worker.Launcher",
            "Program.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                host.Contains(
                    "RedirectStandardInput = true",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(host, Does.Contain("--parent-pid"));
            Assert.That(host, Does.Contain("EntryPoint = \"prctl\""));
            Assert.That(launcher, Does.Contain("LinuxWorkerProcess.Start"));
            Assert.That(launcher, Does.Not.Contain("kernel32"));
        }
    }

    [Test]
    public void NativeZ3ResolverLoadsOnlyTheContainerVerifiedPath()
    {
        var host = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Host",
            "ContainerNativeLibrary.cs"));

        var exactLoad =
            "NativeLibrary.Load(" + Environment.NewLine +
            "                " +
            "ContainerContract.ResolveZ3LibraryRequired());";
        Assert.That(
            host.Contains(exactLoad, StringComparison.Ordinal) &&
            !host.Contains(
                "NativeLibrary.Load(Z3ImportName);",
                StringComparison.Ordinal),
            Is.True);

        var handlePublication = host.IndexOf(
            "Volatile.Write(ref s_z3Handle, handle);",
            StringComparison.Ordinal);
        var resolverRegistration = host.IndexOf(
            "NativeLibrary.SetDllImportResolver(",
            StringComparison.Ordinal);
        Assert.That(
            handlePublication >= 0 &&
            resolverRegistration > handlePublication,
            Is.True,
            "The resolver must not become visible before its verified handle is published.");
    }

    [Test]
    public void WorkerClosureRetainsStagedComponentsUntilSnapshotDisposal()
    {
        var source = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.CompilerArtifact",
            "CompilerManifestArtifact.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                source.Contains(
                    "FileStream[] stagedHandles = [];",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(
                source.Contains(
                    "FileMode.CreateNew))",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(
                source.Contains(
                    "staged.Write(sourceBytes, 0, sourceBytes.Length);",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(
                source.Contains(
                    "hash.Add(component.Key).Add(stagedRead);",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(
                source.Contains(
                    "stagedHandles[stagedCount++] = OpenRead(stagedPath);",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(
                source.Contains(
                    "File.Copy(component.Value, stagedPath);",
                    StringComparison.Ordinal),
                Is.False);
            Assert.That(
                source.Contains(
                    "foreach (var handle in StagedHandles)",
                    StringComparison.Ordinal),
                Is.True);
            Assert.That(
                source.Contains("handle.Dispose();", StringComparison.Ordinal),
                Is.True);
        }
    }

    [Test]
    public void PerformanceContractIsIsolatedFromBroadTestAndCoverageRuns()
    {
        var root = TestRepository.FindRoot();
        var performanceTests = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Gates.Test",
            "PerformanceGateTests.cs"));
        var corpusTests = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Gates.Test",
            "CorpusGateTests.cs"));
        Assert.That(
            performanceTests.Split("[Category(\"Performance\")]", StringSplitOptions.None),
            Has.Length.EqualTo(3));
        Assert.That(
            performanceTests.Split("[Category(\"Coverage\")]", StringSplitOptions.None),
            Has.Length.EqualTo(2));
        var coverageCategoryIndex = performanceTests.IndexOf(
            "[Category(\"Coverage\")]",
            StringComparison.Ordinal);
        Assert.That(coverageCategoryIndex, Is.GreaterThanOrEqualTo(0));
        var coverageMethodIndex = performanceTests.IndexOf(
            "ReleasePerformanceProtocolProducesStructuralEvidence",
            coverageCategoryIndex,
            StringComparison.Ordinal);
        Assert.That(coverageMethodIndex, Is.GreaterThan(coverageCategoryIndex));
        Assert.That(
            corpusTests,
            Does.Contain(
                "[Category(\"Corpus\")]" + Environment.NewLine +
                "    public async Task " +
                "AnalyzerMatchesCanonicalCorpusAndReplayModes"));

        var fastWorkflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "ci.yml"));
        var containerCommands = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        var performanceIndex = containerCommands.IndexOf(
            "scripts/Invoke-SharpProofGateEvidence.ps1",
            StringComparison.Ordinal);
        var semanticTestsIndex = containerCommands.IndexOf(
            "scripts/Invoke-SharpProofSemanticTests.ps1",
            StringComparison.Ordinal);
        var packageTestsIndex = containerCommands.LastIndexOf(
            "scripts/Invoke-SharpProofPackageTests.ps1",
            StringComparison.Ordinal);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                containerCommands,
                Does.Contain(
                    "TestCategory!=Performance&TestCategory!=Coverage"));
            Assert.That(
                containerCommands,
                Does.Contain(
                    "pr-gates requires -Configuration Release."));
            Assert.That(
                containerCommands,
                Does.Not.Contain("TestCategory=Performance"));
            Assert.That(
                containerCommands,
                Does.Contain(
                    "FullyQualifiedName~" +
                    "ForcedTerminationDeadlineIsStableAcrossLaunches"));
            Assert.That(
                fastWorkflow,
                Does.Contain(
                    "fast-pr-performance-${{ github.sha }}-" +
                    "${{ github.run_attempt }}"));
            Assert.That(performanceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                semanticTestsIndex,
                Is.GreaterThan(performanceIndex));
            Assert.That(packageTestsIndex, Is.GreaterThan(semanticTestsIndex));
        }

        Assert.That(
            containerCommands,
            Does.Contain("artifacts/ci/performance.json"));

        var acceptance = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "Verify.ps1"));
        var semanticTests = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofSemanticTests.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                semanticTests,
                Does.Contain(
                    "TestCategory!=Performance&TestCategory!=Coverage&" +
                    "TestCategory!=Corpus"));
            Assert.That(
                acceptance,
                Does.Contain(
                    "contract.automation.solutionBuildWallSeconds"));
            Assert.That(
                acceptance,
                Does.Not.Contain(
                    "-TimeoutSeconds " +
                    "([int]$contract.worker.maximumProjectWallSeconds)"));
        }

        using var acceptanceContract = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                root,
                "eng",
                "acceptance",
                "contract.json")));
        Assert.That(
            acceptanceContract.RootElement
                .GetProperty("automation")
                .GetProperty("solutionBuildWallSeconds")
                .GetInt32(),
            Is.EqualTo(600));

        var coverageCollector = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofCoverage.ps1"));
        Assert.That(
            coverageCollector,
            Does.Contain("TestCategory!=Performance"));
        Assert.That(
            coverageCollector,
            Does.Contain("TestCategory=Coverage"));
        Assert.That(
            containerCommands,
            Does.Contain("Invoke-SharpProofCoverage.ps1"));
        Assert.That(
            containerCommands,
            Does.Contain("'performance' {"));
        Assert.That(
            containerCommands,
            Does.Contain(
                "function Invoke-SharpProofSolutionBuild"));
        Assert.That(
            containerCommands,
            Does.Contain(
                "Invoke-SharpProofSolutionBuild -BuildConfiguration 'Release'"));
        var gateEvidence = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofGateEvidence.ps1"));
        Assert.That(
            gateEvidence,
            Does.Contain("Resolve-SharpProofContainedPath `"));
        foreach (var workflow in new[] {
                     ".github/workflows/coverage.yml",
                     ".github/workflows/package-consumers.yml"
                 })
        {
            var contents = File.ReadAllText(Path.Combine(
                root,
                workflow.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(
                contents,
                Does.Contain("tooling coverage"),
                workflow);
        }
    }

    [Test]
    public void CoverageCollectionPreservesTrustedContractPayloadIdentity()
    {
        var root = TestRepository.FindRoot();
        var collector = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofCoverage.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                collector,
                Does.Contain(
                    "Code Coverage;Format=Cobertura"));
            Assert.That(
                collector,
                Does.Not.Contain("XPlat Code Coverage"));
            Assert.That(
                collector,
                Does.Contain("SharpProof.Dev.Tests.slnf"));
            Assert.That(
                collector,
                Does.Contain("Invoke-SharpProofSemanticTests.ps1"));
            Assert.That(
                collector,
                Does.Contain("Invoke-SharpProofPackageTests.ps1"));
            Assert.That(
                collector,
                Does.Contain("SharpProof.Managed.runsettings"));
            Assert.That(
                collector,
                Does.Contain("New-CoverageSettings"));
            Assert.That(
                collector,
                Does.Contain(".*SharpProof\\.Attributes\\.dll$"));
            Assert.That(
                collector,
                Does.Contain(".*SharpProof\\.Gates\\.dll$"));
            Assert.That(
                collector,
                Does.Not.Contain("SharpProof.Gates.runsettings"));
        }

        var managedSettings = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "coverage",
            "SharpProof.Managed.runsettings"));
        using var coverageBaseline = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "eng", "coverage", "baseline.json")));
        var actualCoverageProjects = coverageBaseline.RootElement
            .GetProperty("projects")
            .EnumerateObject()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedCoverageProjects = BannedApiProjects;
        Assert.That(
            actualCoverageProjects,
            Is.EqualTo(expectedCoverageProjects),
            "Every production source owner must participate in project and " +
            "aggregate coverage; linked-source packaging exceptions are forbidden.");
        Assert.That(
            managedSettings,
            Does.Contain(
                "<CollectFromChildProcesses>False" +
                "</CollectFromChildProcesses>"));
        Assert.That(
            managedSettings,
            Does.Not.Contain(
                "<EnableStaticManagedInstrumentation>False" +
                "</EnableStaticManagedInstrumentation>"));
        Assert.That(
            managedSettings,
            Does.Contain(
                "<EnableStaticManagedInstrumentation>True" +
                "</EnableStaticManagedInstrumentation>"));
        Assert.That(
            managedSettings,
            Does.Contain(
                "<EnableDynamicManagedInstrumentation>False" +
                "</EnableDynamicManagedInstrumentation>"));

        Assert.That(
            collector,
            Does.Contain("-StaticManagedInstrumentation $false"));
    }

    [Test]
    public void PackageFeedConstructionIsDemandDriven()
    {
        var root = TestRepository.FindRoot();
        var feed = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Package.Test",
            "PackagedProductFeed.cs"));
        using var packageCatalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "scripts", "package-projects.json")));
        var packageProjects = packageCatalog.RootElement
            .GetProperty("projects")
            .EnumerateArray()
            .Select(static project => project.GetString()!)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(feed, Does.Not.Contain("[OneTimeSetUp]"));
            Assert.That(
                feed,
                Does.Contain(
                    "new(CreateAsync, " +
                    "LazyThreadSafetyMode.ExecutionAndPublication)"));
            Assert.That(
                feed,
                Does.Contain(
                    "internal static Task<PackagedProductFeed> GetAsync()"));
            foreach (var project in packageProjects)
            {
                var document = XDocument.Load(Path.Combine(root, project));
                var generateOnBuild = document.Descendants(
                        "GeneratePackageOnBuild")
                    .SingleOrDefault();
                Assert.That(
                    generateOnBuild == null ||
                        string.Equals(
                            generateOnBuild.Value,
                            "false",
                            StringComparison.OrdinalIgnoreCase),
                    Is.True,
                    project + " must reserve package creation for the " +
                    "explicit container pack command (the SDK default is " +
                    "false).");
            }
        }
    }

    [Test]
    public void ContractApiMetadataNamesHaveOneSourceOfTruth()
    {
        var root = TestRepository.FindRoot();
        var catalog = Path.GetFullPath(Path.Combine(
            root,
            "SharpProof.Frontend",
            "ContractApiMetadataRuntime.cs"));
        var violations = ProductionProjects
            .SelectMany(ProductionSourceFiles)
            .Where(path => !string.Equals(
                Path.GetFullPath(path),
                catalog,
                StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(
                    ".generated.cs",
                    StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(static entry =>
                entry.Line.Contains(
                    "SharpProof.Attributes.",
                    StringComparison.Ordinal) &&
                !entry.Line.Contains(
                    "SharpProof.Attributes.dll",
                    StringComparison.Ordinal))
            .Select(entry =>
                $"{Path.GetRelativePath(root, entry.Path)}:{entry.Number}")
            .ToArray();

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void CollectorDepthBudgetsDoNotExceedLoweringBudget()
    {
        var root = TestRepository.FindRoot();
        var loweringDepth = ReadIntegerConstant(
            root,
            "SharpProof.Frontend/RoslynOperationLowerer.cs",
            "MaximumLoweringDepth");
        var termDepth = ReadIntegerConstant(
            root,
            "SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs",
            "MaximumTermDepth");
        var dependencyDepth = ReadIntegerConstant(
            root,
            "SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs",
            "MaximumDependencyDepth");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(termDepth, Is.LessThanOrEqualTo(loweringDepth));
            Assert.That(dependencyDepth, Is.LessThanOrEqualTo(loweringDepth));
        }
    }

    [Test]
    public void DeclarativeModelOutputsContainOnlyStorageDeclarations()
    {
        var root = TestRepository.FindRoot();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "SharpProof.DeclarativeModels.catalog.json")));
        var violations = catalog.RootElement
            .GetProperty("outputs")
            .EnumerateArray()
            .Select(static output => output.GetProperty("path").GetString()!)
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(Path.Combine(
                    root,
                    path.Replace('/', Path.DirectorySeparatorChar)))
            })
            .Where(static output =>
                Regex.IsMatch(
                    output.Source,
                    @"(?m)^\s*(if|else|switch|case|for|foreach|while|do|catch)\b",
                    RegexOptions.CultureInvariant) ||
                output.Source.Contains("throw ", StringComparison.Ordinal))
            .Select(static output => output.Path)
            .ToArray();

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void PackageTestsDeclareReleaseEvidenceAssetDependencies()
    {
        var references = ProjectReferences("SharpProof.Package.Test");

        Assert.That(references, Does.Contain("SharpProof.Package"));
        Assert.That(references, Does.Contain("SharpProof.Worker"));
    }

    [Test]
    public void ProductionComplexityRationaleBindsTheExactCeilings()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "eng",
            "acceptance",
            "contract.json")));
        var complexity = contract.RootElement.GetProperty(
            "productionComplexity");
        var maximumExpressionNodes = complexity.GetProperty(
            "maximumExpressionNodes").GetInt32();
        var maximumDecisionPoints = complexity.GetProperty(
            "maximumDecisionPoints").GetInt32();
        var maximumMembers = complexity.GetProperty(
            "maximumMembers").GetInt32();
        var binding = FormattableString.Invariant(
            $"ceilings:{maximumExpressionNodes}/{maximumDecisionPoints}/{maximumMembers}");

        Assert.That(
            complexity.GetProperty("ceilingRationale").GetString(),
            Does.Contain(binding));
    }

    private static int ReadIntegerConstant(
        string root,
        string relativePath,
        string name)
    {
        var source = File.ReadAllText(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var match = Regex.Match(
            source,
            $@"(?m)^\s*(?:private|internal|public)\s+const\s+int\s+{Regex.Escape(name)}\s*=\s*(?<value>\d+)\s*;",
            RegexOptions.CultureInvariant);
        Assert.That(match.Success, Is.True, relativePath + ":" + name);
        return int.Parse(
            match.Groups["value"].Value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    [Test]
    public void AlgorithmLayersStayWithinStructuralComplexityCaps()
    {
        var violations = new List<string>();
        foreach (var entry in ReadSizeRatchetManifest().Files)
        {
            var fullPath = Path.Combine(TestRepository.FindRoot(), entry.Path);
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
            if (parseErrors.Length != 0)
            {
                violations.Add(
                    $"{entry.Path}: Roslyn parse errors: " +
                    string.Join("; ", parseErrors.Select(
                        static diagnostic => diagnostic.ToString())));
                continue;
            }

            var root = tree.GetRoot();
            var fileExpressionNodes = root.DescendantNodes()
                .Count(static node => node is ExpressionSyntax);
            var fileDecisionPoints = root.DescendantNodes()
                .Count(IsDecisionPoint);
            if (fileExpressionNodes > entry.MaximumFileExpressionNodes)
            {
                violations.Add(
                    $"{entry.Path}: {fileExpressionNodes} expression nodes exceeds " +
                    $"cap {entry.MaximumFileExpressionNodes}");
            }
            if (fileDecisionPoints > entry.MaximumFileDecisionPoints)
            {
                violations.Add(
                    $"{entry.Path}: {fileDecisionPoints} decision points " +
                    $"exceeds cap {entry.MaximumFileDecisionPoints}");
            }

            var members = root
                .DescendantNodes()
                .Where(IsMeasuredMember)
                .Select(static node => new
                {
                    Node = node,
                    ExpressionNodes = node.DescendantNodesAndSelf()
                        .Count(static candidate => candidate is ExpressionSyntax),
                    DecisionPoints = node.DescendantNodesAndSelf()
                        .Count(IsDecisionPoint)
                })
                .ToArray();
            foreach (var member in members)
            {
                if (member.ExpressionNodes <= entry.MaximumMemberExpressionNodes &&
                    member.DecisionPoints <= entry.MaximumMemberDecisionPoints)
                {
                    continue;
                }

                var line = member.Node.GetLocation()
                    .GetLineSpan()
                    .StartLinePosition.Line + 1;
                violations.Add(
                    $"{entry.Path}:{line}: {MemberName(member.Node)} has " +
                    $"{member.ExpressionNodes} expression nodes and " +
                    $"{member.DecisionPoints} decision points; caps " +
                    $"{entry.MaximumMemberExpressionNodes} and " +
                    $"{entry.MaximumMemberDecisionPoints}");
            }
            TestContext.WriteLine(
                $"{entry.Path}|{fileExpressionNodes}|{fileDecisionPoints}|" +
                $"{members.Max(static member => member.ExpressionNodes)}|" +
                $"{members.Max(static member => member.DecisionPoints)}");
        }

        Assert.That(
            violations,
            Is.Empty,
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsMeasuredMember(SyntaxNode node)
    {
        return node is BaseMethodDeclarationSyntax or
        LocalFunctionStatementSyntax or
        AccessorDeclarationSyntax or
        ParameterListSyntax { Parent: TypeDeclarationSyntax } or
        PropertyDeclarationSyntax { ExpressionBody: not null } or
        IndexerDeclarationSyntax { ExpressionBody: not null };
    }

    private static bool IsDecisionPoint(SyntaxNode node)
    {
        return node is IfStatementSyntax or
            ForStatementSyntax or
            ForEachStatementSyntax or
            ForEachVariableStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax or
            CaseSwitchLabelSyntax or
            CasePatternSwitchLabelSyntax or
            CatchClauseSyntax or
            ConditionalExpressionSyntax or
            SwitchExpressionArmSyntax ||
        node.IsKind(SyntaxKind.LogicalAndExpression) ||
        node.IsKind(SyntaxKind.LogicalOrExpression) ||
        node.IsKind(SyntaxKind.CoalesceExpression);
    }

    private static string MemberName(SyntaxNode node)
    {
        return node switch
        {
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
            ParameterListSyntax
            {
                Parent: TypeDeclarationSyntax type
            } => type.Identifier.ValueText + " primary constructor",
            PropertyDeclarationSyntax property =>
                property.Identifier.ValueText + " getter",
            IndexerDeclarationSyntax => "this getter",
            _ => node.Kind().ToString()
        };
    }

    private static SizeRatchetManifest ReadSizeRatchetManifest()
    {
        var path = Path.Combine(
            TestRepository.FindRoot(),
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

    private static string[] FindRelativeCallers(
        IEnumerable<string> files,
        string pattern)
    {
        return [.. files
            .Where(file => File.ReadAllText(file).Contains(
                pattern,
                StringComparison.Ordinal))
            .Select(TestRepository.Relative)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)];
    }

    [Test]
    public void NightlyFuzzCampaignIsContainerConnectedAndEvidenceBound()
    {
        var root = TestRepository.FindRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, ".github", "workflows", "nightly.yml"));
        var dispatcher = File.ReadAllText(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        var entrypoint = File.ReadAllText(Path.Combine(
            root, "eng", "container", "entrypoint.sh"));
        var campaign = File.ReadAllText(Path.Combine(
            root, "scripts", "Invoke-SharpProofFuzzCampaign.ps1"));
        var acceptance = File.ReadAllText(Path.Combine(
            root, "eng", "acceptance", "Verify.ps1"));
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "eng", "acceptance", "contract.json")));
        var nightlyCases = contract.RootElement
            .GetProperty("fuzz")
            .GetProperty("nightlyCases")
            .GetInt32();
        var maximumCampaignCases = contract.RootElement
            .GetProperty("fuzz")
            .GetProperty("maximumCampaignCases")
            .GetInt32();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nightlyCases, Is.Positive);
            Assert.That(maximumCampaignCases, Is.GreaterThan(nightlyCases));
            Assert.That(workflow, Does.Contain("tooling nightly"));
            Assert.That(
                WorkflowFiles()
                    .Where(path => !path.EndsWith(
                        "nightly.yml",
                        StringComparison.Ordinal))
                    .Select(File.ReadAllText),
                Has.None.Contain("tooling nightly"));
            Assert.That(dispatcher,
                Does.Contain("'fuzz-nightly'")
                    .And.Contain("Invoke-SharpProofFuzzCampaign.ps1")
                    .And.Contain("fuzz-nightly requires -Configuration Release."));
            Assert.That(entrypoint,
                Does.Contain("fuzz-nightly")
                    .And.Contain("requires clean exact-commit source"));
            Assert.That(campaign,
                Does.Contain("contract.fuzz.nightlyCases")
                    .And.Contain("contract.fuzz.maximumCampaignCases")
                    .And.Contain("Assert-SharpProofFuzzCampaignBudget")
                    .And.Contain("ContainsKey('RotatingSeed')")
                    .And.Contain("Read-SharpProofRetainedFuzzSeedManifest")
                    .And.Contain("$retained.Seeds")
                    .And.Contain("Invoke-FuzzRun")
                    .And.Contain("yyyyMMdd")
                    .And.Contain("schemaVersion = 4")
                    .And.Contain("commit = $sourceCommit")
                     .And.Contain("rotatingCases = $effectiveRotatingCases")
                     .And.Contain("retainedCasesPerSeed = $effectiveRetainedCases")
                     .And.Contain("retainedSeeds = $retainedSeeds")
                     .And.Not.Contain("retainedSeedManifestSha256")
                     .And.Not.Contain("runnerSha256")
                     .And.Not.Contain("resultSha256")
                     .And.Not.Contain("Get-FileHash")
                     .And.Contain("status = if"));
            Assert.That(acceptance,
                Does.Contain("contract.fuzz.pullRequestCases")
                    .And.Not.Contain("contract.fuzz.nightlyCases"));
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "System.Text.Json instantiates this model through reflection.")]
    private sealed class SizeRatchetManifest
    {
        public int SchemaVersion
        {
            get; init;
        }

        public string Rationale { get; init; } = "";

        public SizeRatchetMeasurement Measurement { get; init; } = new();

        public SizeRatchetEntry[] Files { get; init; } = [];
    }

    private sealed class SizeRatchetMeasurement
    {
        public string FileExpressionNodes { get; init; } = "";

        public string MemberExpressionNodes { get; init; } = "";

        public string FileDecisionPoints { get; init; } = "";

        public string MemberDecisionPoints { get; init; } = "";
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "System.Text.Json instantiates this model through reflection.")]
    private sealed class SizeRatchetEntry
    {
        public string Path { get; init; } = "";

        public int MaximumFileExpressionNodes
        {
            get; init;
        }

        public int MaximumMemberExpressionNodes
        {
            get; init;
        }

        public int MaximumFileDecisionPoints
        {
            get; init;
        }

        public int MaximumMemberDecisionPoints
        {
            get; init;
        }
    }
}

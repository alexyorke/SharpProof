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

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ArchitectureTests
{
    private static readonly JsonSerializerOptions SizeRatchetJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private static readonly string[] ProductionProjects = [
        "SharpProof.Analyzer",
        "SharpProof.Attributes",
        "SharpProof.Ir",
        "SharpProof.Meta.Analyzers",
        "SharpProof.PortableAnalyzer",
        "SharpProof.CompilerArtifact",
        "SharpProof.CompilerCollector",
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

    private static readonly string[] TrustedKernelPaths = [
        "SharpProof.Verify/Evidence.cs",
        "SharpProof.Verify/Outcomes.cs",
        "SharpProof.Verify/ProofKernel.cs"
    ];

    [Test]
    public void RepositoryRestoreIsHermeticLockedAndSdkPinned()
    {
        var root = RepositoryRoot();
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
            ["SharpProof.Analyzer"] = [
                "SharpProof.Contracts",
                "SharpProof.Effects",
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Specs"
            ],
            ["SharpProof.Attributes"] = [],
            ["SharpProof.Ir"] = [],
            ["SharpProof.Meta.Analyzers"] = [],
            ["SharpProof.PortableAnalyzer"] = ["SharpProof.Attributes"],
            ["SharpProof.CompilerArtifact"] = [
                "SharpProof.Ir",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.CompilerCollector"] = [
                "SharpProof.Analyzer",
                "SharpProof.CompilerArtifact",
                "SharpProof.Contracts",
                "SharpProof.Effects",
                "SharpProof.Frontend",
                "SharpProof.Ir",
                "SharpProof.Specs",
                "SharpProof.Worker.Protocol"
            ],
            ["SharpProof.ContractForGenerator"] = ["SharpProof.Contracts"],
            ["SharpProof.Specs"] = ["SharpProof.Ir"],
            ["SharpProof.Dataflow"] = [],
            ["SharpProof.Frontend"] = [
                "SharpProof.Attributes",
                "SharpProof.Ir"
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

        foreach (var project in ProductionProjects)
        {
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
    public void WorkerAndLauncherRuntimeClosuresAreCompilerNeutral()
    {
        var forbiddenProjects = new[] {
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.Contracts",
            "SharpProof.Effects", "SharpProof.Frontend"
        };
        foreach (var root in new[] {
                     "SharpProof.Worker", "SharpProof.Worker.Launcher"
                 })
        {
            var closure = TransitiveProjectClosure(root).ToArray();
            Assert.That(
                closure.Intersect(forbiddenProjects, StringComparer.Ordinal),
                Is.Empty,
                root);
            foreach (var project in closure)
            {
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
                    RepositoryRoot(), path.Replace('/', Path.DirectorySeparatorChar))),
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
    public void OnlyTheSmtLayerReferencesZ3InTheProductionGraph()
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
                Is.EqualTo(project == "SharpProof.Smt"),
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
            "SharpProof.Ir",
            "SharpProof.Smt",
            "SharpProof.Verify",
            "SharpProof.Worker",
            "SharpProof.Worker.Protocol",
            "SharpProof.Worker.Launcher"
        };
        var violations = semanticConsumers
            .SelectMany(ProductionSourceFiles)
            .Where(file =>
                !string.Equals(
                    Relative(file),
                    "SharpProof.Frontend/ContractApiMetadataRuntime.cs",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    Relative(file),
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
    public void EffectArrayCardinalityRequiresCompilerBoundSymbolIdentity()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
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
            typeof(ProofJustification).IsAssignableFrom(
                typeof(ApproximatedJustification)),
            Is.False);

        Assert.That(
            FindRelativeCallers(productionFiles, "new Assumption("),
            Is.EqualTo([
            "SharpProof.Worker/CallableEvidenceBuilder.cs",
                "SharpProof.Worker/PostconditionObligationBuilder.cs"
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
    public void TrustedComputingBaseDeclarationNamesEveryRequiredPath()
    {
        var expected = new Dictionary<string, string[]>(
            StringComparer.Ordinal)
        {
            ["discovery"] = [
                "SharpProof.Analyzer/SharpProofAnalyzer.cs",
                "SharpProof.Analyzer/ContractRuntimePolicy.cs",
                "SharpProof.CompilerCollector/FinalCompilationCollectorAnalyzer.cs",
                "SharpProof.CompilerCollector/FinalCompilationCollector.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs",
                "SharpProof.Contracts/ContractClauseInventoryBuilder.cs",
                "SharpProof.Contracts/ContractClauseInventory.cs",
                "SharpProof.Contracts/ContractForSymbolMatcher.cs",
                "SharpProof.Frontend/ReferencedTypeSymbols.cs",
                "SharpProof.Contracts/EffectiveContractSourceResolver.cs",
                "SharpProof.Frontend/CSharpPreprocessorSymbols.cs",
                "SharpProof.Frontend/CompilationModelProvider.cs",
                "SharpProof.Frontend/ContractApiIdentityResolver.cs",
                "SharpProof.Frontend/ContractApiMetadataRuntime.cs",
                "SharpProof.Frontend/SharpProof.Frontend.csproj"
            ],
            ["lowering"] = [
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs",
                "SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs",
                "SharpProof.CompilerArtifact/PortableIrGraphCodec.cs",
                "SharpProof.Contracts/ClosedContractAttributeValidator.cs",
                "SharpProof.Contracts/ContractBinder.cs",
                "SharpProof.Contracts/ContractCanonicalization.cs",
                "SharpProof.Contracts/ContractExpressionBinder.cs",
                "SharpProof.Contracts/ContractIntrinsicValidator.cs",
                "SharpProof.Contracts/ContractApiSymbols.cs",
                "SharpProof.Frontend/OperationSupportCatalog.cs",
                "SharpProof.Frontend/RoslynOperationLowerer.cs",
                "SharpProof.Frontend/RoslynProgramLowerer.cs"
            ],
            ["execution"] = [
                "SharpProof.Worker/CallableEvidenceBuilder.cs",
                "SharpProof.Worker/CallableEntryFeasibility.cs",
                "SharpProof.Worker/CallableVerifier.cs",
                "SharpProof.Worker/AcyclicBlockPredicateExecutor.cs",
                "SharpProof.Worker/SpecResultDomainProjection.cs",
                "SharpProof.Worker/SharpProofWorker.cs"
            ],
            ["obligationGeneration"] = [
                "SharpProof.Worker/PostconditionObligationBuilder.cs"
            ],
            ["encoding"] = [
                "SharpProof.Smt/IrSmtBackend.cs",
                "SharpProof.Verify/Backend.cs"
            ],
            ["apiSpecifications"] = [
                "SharpProof.Specs/ApiSpecTable.cs",
                "SharpProof.Specs/ApiSpecTermValidator.cs",
                "SharpProof.Effects/ApiSpecResolution.cs",
                "SharpProof.Specs/ApiSpecInstantiation.cs"
            ],
            ["apiSpecificationIdentity"] = [
                "SharpProof.Specs/ApiSpecContentDigest.cs"
            ],
            ["apiSpecificationCatalog"] = [
                "SharpProof.Specs/DefaultApiSpecCatalog.json",
                "scripts/Generate-ApiSpecCatalog.ps1",
                "SharpProof.Specs/DefaultApiSpecCatalog.generated.cs"
            ],
            ["scalarSemanticsCatalog"] = [
                "SharpProof.Frontend/CSharpScalarSemantics.json",
                "scripts/Generate-CSharpScalarSemantics.ps1",
                "SharpProof.Frontend/CSharpScalarSemantics.generated.cs",
                "SharpProof.Ir/IrOperatorCatalog.generated.cs"
            ],
            ["contractApiCatalog"] = [
                "SharpProof.Frontend/ContractApi.catalog.json",
                "scripts/Generate-ContractApiCatalog.ps1",
                "SharpProof.Frontend/ContractApiMetadata.generated.cs",
                "SharpProof.Attributes/Contract.cs",
                "SharpProof.Attributes/ContractForAttribute.cs",
                "SharpProof.Attributes/ClosedContractAttributes.cs",
                "SharpProof.Attributes/AllowedCapabilitiesAttribute.cs",
                "SharpProof.Attributes/AllowedExceptionsAttribute.cs",
                "SharpProof.Attributes/DoesNotThrowAttribute.cs",
                "SharpProof.Attributes/EnforcePureAttribute.cs",
                "SharpProof.Attributes/EffectContractAttribute.cs",
                "SharpProof.Attributes/SharpProofSuppressAttribute.cs",
                "SharpProof.Attributes/SharpProofTrustedAttribute.cs",
                "SharpProof.Attributes/ZeroAllocationsAttribute.cs",
                "SharpProof.Attributes/SharpProofEffect.cs",
                "SharpProof.Attributes/SharpProofCapability.cs"
            ],
            ["analyzerDiagnosticCatalog"] = [
                "SharpProof.Analyzer/AnalyzerDiagnostic.catalog.json",
                "scripts/Generate-AnalyzerDiagnosticCatalog.ps1",
                "SharpProof.Analyzer/AnalyzerDiagnosticCatalog.generated.cs"
            ],
            ["diagnosticDescriptorCatalog"] = [
                "eng/diagnostics/diagnostic-descriptors.v1.json",
                "scripts/Generate-DiagnosticDescriptors.ps1",
                "SharpProof.Analyzer/GeneratedDiagnosticDescriptors.generated.cs",
                "SharpProof.ContractForGenerator/GeneratedDiagnosticDescriptors.generated.cs",
                "SharpProof.Meta.Analyzers/MetaDiagnosticDescriptors.generated.cs"
            ],
            ["projectionCatalog"] = [
                "SharpProof.Projection.catalog.json",
                "scripts/Generate-ProjectionCatalog.ps1",
                "SharpProof.Verify/VerificationProjections.generated.cs",
                "SharpProof.Analyzer/EffectEvaluationProjections.generated.cs",
                "SharpProof.Worker/WorkerProjections.generated.cs",
                "SharpProof.Contracts/ContractProjections.generated.cs",
                "SharpProof.Ir/IrPrinterProjections.generated.cs",
                "SharpProof.Effects/EffectProjections.generated.cs",
                "SharpProof.Frontend/OperationSupportProjections.generated.cs",
                "SharpProof.Worker.Launcher/LauncherProjections.generated.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableProjections.generated.cs",
                "SharpProof.Frontend/CompilerIdentityProjections.generated.cs"
            ],
            ["launcherArgumentCatalog"] = [
                "SharpProof.Worker.Launcher/LauncherArguments.catalog.json",
                "scripts/Generate-LauncherArguments.ps1",
                "SharpProof.Worker.Launcher/LauncherArguments.generated.cs"
            ],
            ["generatedOutputPolicy"] = [
                "eng/generated/approved-outputs.v1.json",
                "scripts/Test-ProductionCSharpComplexity.ps1"
            ],
            ["mutationEvidencePolicy"] = [
                "scripts/SharpProof.MutationEvidence.psm1",
                "scripts/Test-SharpProofMutationEvidence.ps1",
                "scripts/Test-SharpProofTrustedMutations.ps1",
                "scripts/Test-SharpProofMutationCatalog.ps1"
            ],
            ["declarativeModelsCatalog"] = [
                "SharpProof.DeclarativeModels.catalog.json",
                "scripts/Generate-DeclarativeModels.ps1",
                "SharpProof.Analyzer/DeclarativeModels.generated.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/DeclarativeModels.generated.cs",
                "SharpProof.Specs/DeclarativeModels.generated.cs",
                "SharpProof.Worker/DeclarativeModels.generated.cs",
                "SharpProof.Contracts/DeclarativeModels.generated.cs",
                "SharpProof.Contracts/EffectiveContractModels.generated.cs",
                "SharpProof.Effects/EffectResultModels.generated.cs",
                "SharpProof.Effects/ApiSpecResolutionModels.generated.cs",
                "SharpProof.Frontend/DeclarativeModels.generated.cs",
                "SharpProof.Verify/DeclarativeModels.generated.cs"
            ],
            ["boundContractModelCatalog"] = [
                "SharpProof.Contracts/BoundContractModel.schema.json",
                "scripts/Generate-BoundContractModel.ps1",
                "SharpProof.Contracts/BoundContractModel.generated.cs"
            ],
            ["effectContractCatalog"] = [
                "SharpProof.Effects/EffectContractMappings.catalog.json",
                "scripts/Generate-EffectContractMappings.ps1",
                "SharpProof.Effects/EffectContractMappings.generated.cs"
            ],
            ["operationSupportCatalog"] = [
                "SharpProof.Frontend/OperationSupport.catalog.json",
                "scripts/Generate-OperationSupportCatalog.ps1",
                "SharpProof.Frontend/OperationSupportCatalog.generated.cs"
            ],
            ["irModelCatalog"] = [
                "SharpProof.Ir/IrModel.schema.json",
                "scripts/Generate-IrModel.ps1",
                "SharpProof.Ir/IrIdentifierAliases.cs",
                "SharpProof.Ir/ScopedIrId.cs",
                "SharpProof.Ir/ArgumentNullGuard.cs",
                "SharpProof.Ir/IrModel.generated.cs",
                "SharpProof.Ir/IrProgram.cs"
            ],
            ["compilerWireCatalog"] = [
                "SharpProof.CompilerArtifact/CompilerArtifactModel.schema.json",
                "scripts/Generate-CompilerArtifactModel.ps1",
                "scripts/Test-CompilerArtifactModelGenerator.ps1",
                "SharpProof.CompilerArtifact/PortableIrModel.generated.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerWireMappings.generated.cs"
            ],
            ["effectAnalysis"] = [
                "SharpProof.Analyzer/AnalyzerFeaturePipeline.cs",
                "SharpProof.Analyzer/AnalyzerSession.cs",
                "SharpProof.Analyzer/CallArgumentAliasPolicy.cs",
                "SharpProof.Analyzer/EffectCallPreconditionPolicy.cs",
                "SharpProof.Analyzer/EffectContractDiagnostics.cs",
                "SharpProof.Analyzer/EffectEvaluationTypes.cs",
                "SharpProof.Analyzer/ManagedContractFacts.cs",
                "SharpProof.Analyzer/RequiresCallSiteAnalyzer.cs",
                "SharpProof.Analyzer/RequiresCallSiteDiscovery.cs",
                "SharpProof.Analyzer/RequiresCallSiteTreeAnalyzer.cs",
                "SharpProof.Contracts/ContractSelectionInventory.cs",
                "SharpProof.Effects/EffectAnalysisSession.cs",
                "SharpProof.Effects/EffectCallPreconditionPolicy.cs",
                "SharpProof.Effects/EffectCallSiteResolver.cs",
                "SharpProof.Effects/EffectCallGraph.cs",
                "SharpProof.Effects/EffectMethodNodeBuilder.cs",
                "SharpProof.Effects/EffectContractMappings.cs",
                "SharpProof.Effects/OperationEffectScanner.cs",
                "SharpProof.Effects/EffectExceptionFlow.cs",
                "SharpProof.Effects/ManagedAbstractFlow.cs",
                "SharpProof.Effects/ExternalEffectResolver.cs",
                "SharpProof.Effects/EffectSummaryOperations.cs",
                "SharpProof.Effects/EffectProjection.cs",
                "SharpProof.Effects/EffectSummary.cs",
                "SharpProof.Effects/EffectValues.cs",
                "SharpProof.Effects/EffectRegions.cs",
                "SharpProof.Effects/EffectContractValues.cs",
                "SharpProof.Dataflow/ForwardDataflowAnalysis.cs",
                "SharpProof.Dataflow/DataflowGraph.cs",
                "SharpProof.Dataflow/IAbstractDomain.cs",
                "SharpProof.Dataflow/ClosedAbstractDomain.cs",
                "SharpProof.Dataflow/IntervalDomain.cs",
                "SharpProof.Dataflow/IntervalValue.cs",
                "SharpProof.Dataflow/NullnessDomain.cs",
                "SharpProof.Dataflow/NullnessValue.cs",
                "SharpProof.Specs/FrameworkTypeMetadataNames.cs"
            ],
            ["replay"] = [
                "SharpProof.Ir/IrInterpreter.cs",
                "SharpProof.Ir/IrProgramInterpreter.cs",
                "SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs",
                "SharpProof.Worker/CallableCounterexampleReplayer.cs",
                "SharpProof.Worker/EffectCounterexampleReplayer.cs"
            ],
            ["effectResultAssembly"] = [
                "SharpProof.Worker/EffectClaimResultAssembler.cs"
            ],
            ["policy"] = [
                "SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs",
                "SharpProof.Analyzer/AnalyzerGeneratedCodePolicy.cs",
                "SharpProof.Analyzer/LanguageSubsetGate.cs",
                "SharpProof.Analyzer/SharpProofControlAttributePolicy.cs",
                "SharpProof.Effects/TrustedBoundaryPolicy.cs",
                "SharpProof.Frontend/FrontendSubset.cs",
                "SharpProof.Frontend/OperationSubsetClassifier.cs",
                "SharpProof.Worker.Launcher/Program.cs",
                "SharpProof.Worker/CallableVerificationPolicy.cs",
                "scripts/Get-SharpProofTcbPaths.ps1"
            ],
            ["resultAssembly"] = [
                "SharpProof.Worker/CallableClaimResultAssembler.cs",
                "SharpProof.Worker.Protocol/WorkerResultAssembler.cs"
            ],
            ["compilerInputIdentity"] = [
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs",
                "SharpProof.CompilerCollector/CompilerArtifact/CompilerOptionWireMappings.cs",
                "SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs",
                "SharpProof.CompilerArtifact/CompilationFingerprint.cs"
            ],
            ["canonicalIdentityEncoding"] = [
                "SharpProof.Analyzer/CompilerArtifact/CompilerExceptionTypeIdentity.cs",
                "SharpProof.Frontend/CompilerIdentityBridge.cs",
                "SharpProof.Ir/CanonicalHashWriter.cs"
            ],
            ["protocolValidation"] = [
                "SharpProof.Worker/Program.cs",
                "SharpProof.Worker.Protocol/ProtocolModel.schema.json",
                "scripts/Generate-ProtocolModel.ps1",
                "SharpProof.Worker.Protocol/ProtocolModel.generated.cs",
                "SharpProof.Worker.Protocol/ProtocolManifest.cs",
                "SharpProof.Worker.Protocol/ProtocolManifestPayload.cs",
                "SharpProof.Worker.Protocol/ProtocolJson.cs",
                "SharpProof.Worker.Protocol/ProtocolJsonSupport.cs"
            ],
            ["cacheValidation"] = [
                "SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs",
                "SharpProof.CompilerArtifact/CompilerManifestArtifact.cs",
                "SharpProof.Worker.Protocol/WorkerCachePath.cs",
                "SharpProof.Worker/WorkerInputSnapshot.cs",
                "SharpProof.Worker/VerificationCache.cs"
            ],
            ["portableShippingBoundary"] = [
                "SharpProof.PortableAnalyzer/SharpProof.PortableAnalyzer.csproj",
                "SharpProof.Package/SharpProof.nuspec",
                "SharpProof.Package/buildTransitive/SharpProof.props",
                "SharpProof.Package/buildTransitive/SharpProof.targets",
                "SharpProof.Verifier.Win-x64/buildTransitive/SharpProof.Verifier.Win-x64.targets"
            ],
            ["releaseContainment"] = [
                "scripts/Invoke-SharpProofDotnet.ps1",
                "scripts/JobObjectHelpers.ps1",
                "scripts/Test-SharpProofPackageConsumers.ps1"
            ]
        };
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "eng", "acceptance", "contract.json")));
        var root = document.RootElement;
        Assert.That(
            root.GetProperty("trustedKernel")
                .GetProperty("paths")
                .EnumerateArray()
                .Select(static path => path.GetString() ?? "")
                .OrderBy(static path => path, StringComparer.Ordinal),
            Is.EqualTo(TrustedKernelPaths));
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
        Assert.That(
            actual.Keys.OrderBy(static name => name, StringComparer.Ordinal),
            Is.EqualTo(expected.Keys.OrderBy(
                static name => name,
                StringComparer.Ordinal)));
        foreach (var component in expected)
        {
            Assert.That(
                actual[component.Key].OrderBy(
                    static path => path,
                    StringComparer.Ordinal),
                Is.EqualTo(component.Value.OrderBy(
                    static path => path,
                    StringComparer.Ordinal)),
                component.Key);
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
        Assert.That(
            canonicalTcb,
            Does.Contain("SharpProof.Verify/Evidence.cs")
                .And.Contain("SharpProof.Verify/Outcomes.cs")
                .And.Contain("SharpProof.Effects/TrustedBoundaryPolicy.cs")
                .And.Contain("SharpProof.Analyzer/LanguageSubsetGate.cs")
                .And.Contain("SharpProof.Frontend/OperationSubsetClassifier.cs")
                .And.Contain("SharpProof.Frontend/FrontendSubset.cs")
                .And.Contain("SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs")
                .And.Contain("SharpProof.Analyzer/SharpProofControlAttributePolicy.cs")
                .And.Contain("SharpProof.Verify/Backend.cs")
                .And.Contain("SharpProof.Contracts/ContractApiSymbols.cs")
                .And.Contain("SharpProof.Analyzer/AnalyzerGeneratedCodePolicy.cs")
                .And.Contain("SharpProof.Attributes/EffectContractAttribute.cs")
                .And.Contain("SharpProof.Frontend/CompilationModelProvider.cs")
                .And.Contain("SharpProof.Contracts/ContractForSymbolMatcher.cs")
                .And.Contain("SharpProof.Contracts/ContractClauseInventory.cs")
                .And.Contain("SharpProof.Attributes/SharpProofEffect.cs")
                .And.Contain("SharpProof.Attributes/SharpProofCapability.cs")
                .And.Contain("SharpProof.Worker/Program.cs"));
        Assert.That(
            File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "scripts",
                "Get-SharpProofReleaseDigests.ps1")),
            Does.Contain("Get-SharpProofTcbPaths"));
        Assert.That(
            File.ReadAllText(Path.Combine(
                RepositoryRoot(),
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
    public void WorkflowCommandsUsePowerShellSafeMsBuildSwitches()
    {
        var workflowRoot = Path.Combine(RepositoryRoot(), ".github", "workflows");
        var violations = Directory
            .EnumerateFiles(workflowRoot, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(
                workflowRoot,
                "*.yaml",
                SearchOption.TopDirectoryOnly))
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
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "package-consumers.yml"));
        var bindings = Regex.Matches(
                workflow,
                @"/p:RepositoryCommit=\$env:GITHUB_SHA",
                RegexOptions.CultureInvariant)
            .Count;

        Assert.That(bindings, Is.EqualTo(3));
    }

    [Test]
    public void CrossPlatformPackageCachePrimingUsesTheNativeDotnetPath()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "package-consumers.yml"));
        var primeStart = workflow.IndexOf(
            "      - name: Prime the framework-only package cache",
            StringComparison.Ordinal);
        var primeEnd = workflow.IndexOf(
            "      - name: Download exact NuGet artifacts",
            primeStart,
            StringComparison.Ordinal);

        Assert.That(primeStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(primeEnd, Is.GreaterThan(primeStart));
        var primeStep = workflow[primeStart..primeEnd];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(primeStep, Does.Contain("if ($IsWindows)"));
            Assert.That(
                primeStep,
                Does.Contain(
                    "Invoke-SharpProofDotnet.ps1\" @restoreArguments"));
            Assert.That(primeStep, Does.Contain("& dotnet @restoreArguments"));
            Assert.That(
                primeStep,
                Does.Contain(
                    "Framework-only package cache restore failed"));
        }
    }

    [Test]
    public void WorkerProcessCreationDisablesHandleInheritance()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "SharpProof.Worker.Launcher",
            "Program.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Contain("inheritHandles: false"));
            Assert.That(source, Does.Not.Contain("inheritHandles: true"));
        }
    }

    [Test]
    public void WorkerClosureRetainsStagedComponentsUntilSnapshotDisposal()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
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
                    "hash.Add(component.Key.ToUpperInvariant()).Add(stagedRead);",
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
        var root = RepositoryRoot();
        var performanceTests = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Gates.Test",
            "PerformanceGateTests.cs"));
        Assert.That(
            performanceTests.Split("[Category(\"Performance\")]", StringSplitOptions.None),
            Has.Length.EqualTo(3));

        var fastWorkflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "ci.yml"));
        var performanceIndex = fastWorkflow.IndexOf(
            "Invoke-SharpProofGateEvidence.ps1",
            StringComparison.Ordinal);
        var broadTestsIndex = fastWorkflow.IndexOf(
            "test SharpProof.Dev.Tests.slnf",
            StringComparison.Ordinal);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                fastWorkflow,
                Does.Contain(
                    "TestCategory!=Performance&TestCategory!=Coverage"));
            Assert.That(
                fastWorkflow,
                Does.Not.Contain("TestCategory=Performance"));
            Assert.That(
                fastWorkflow,
                Does.Contain(
                    "FullyQualifiedName~" +
                    "ForcedTerminationDeadlineIsStableAcrossLaunches"));
            Assert.That(
                fastWorkflow,
                Does.Contain(
                    "-OutputPath artifacts/ci/performance.json"));
            Assert.That(
                fastWorkflow,
                Does.Contain(
                    "fast-pr-performance-${{ github.sha }}-" +
                    "${{ github.run_attempt }}"));
            Assert.That(performanceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                broadTestsIndex,
                Is.GreaterThan(performanceIndex));
        }

        var acceptance = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "Verify.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                acceptance,
                Does.Contain(
                    "TestCategory!=Performance&TestCategory!=Coverage"));
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
                Does.Contain("Invoke-SharpProofCoverage.ps1"),
                workflow);
        }
    }

    [Test]
    public void CoverageCollectionPreservesTrustedContractPayloadIdentity()
    {
        var root = RepositoryRoot();
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
                Does.Contain("SharpProof.Managed.runsettings"));
            Assert.That(
                collector,
                Does.Contain("SharpProof.Gates.runsettings"));
        }

        var managedSettings = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "coverage",
            "SharpProof.Managed.runsettings"));
        using var coverageBaseline = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "eng", "coverage", "baseline.json")));
        Assert.That(
            coverageBaseline.RootElement
                .GetProperty("projects")
                .TryGetProperty("SharpProof.CompilerCollector", out _),
            Is.True,
            "CompilerCollector must participate in project and aggregate coverage.");
        Assert.That(
            managedSettings,
            Does.Contain(
                "<CollectFromChildProcesses>False" +
                "</CollectFromChildProcesses>"));

        var gateSettings = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "coverage",
            "SharpProof.Gates.runsettings"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                gateSettings,
                Does.Contain(
                    ".*SharpProof\\.Gates\\.dll$"));
            Assert.That(
                gateSettings,
                Does.Contain(
                    "<CollectFromChildProcesses>False" +
                    "</CollectFromChildProcesses>"));
            Assert.That(
                gateSettings,
                Does.Not.Contain("SharpProof.Attributes"));
        }
    }

    [Test]
    public void ContractApiMetadataNamesHaveOneSourceOfTruth()
    {
        var root = RepositoryRoot();
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
    public void DeclarativeModelOutputsContainOnlyStorageDeclarations()
    {
        var root = RepositoryRoot();
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
        var references = GetProjectReferences("SharpProof.Package.Test").ToArray();

        Assert.That(references, Does.Contain("SharpProof.Package"));
        Assert.That(references, Does.Contain("SharpProof.Worker"));
    }

    [Test]
    public void AlgorithmLayersStayWithinStructuralComplexityCaps()
    {
        var violations = new List<string>();
        foreach (var entry in ReadSizeRatchetManifest().Files)
        {
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

    private static IEnumerable<string> GetProjectReferences(string project)
    {
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

    private static IEnumerable<string> TransitiveProjectClosure(string root)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.Count != 0)
        {
            var project = pending.Pop();
            if (!visited.Add(project))
            {
                continue;
            }

            yield return project;
            foreach (var dependency in GetProjectReferences(project))
            {
                pending.Push(dependency);
            }
        }
    }

    private static string[] ProjectPackages(string project)
    {
        return [..
        XDocument.Load(ProjectFile(project))
            .Descendants("PackageReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)];
    }

    private static string ProjectFile(string project)
    {
        return Path.Combine(RepositoryRoot(), project, project + ".csproj");
    }

    private static string ReadProductionSources(string project)
    {
        return string.Join(
            "\n",
            ProductionSourceFiles(project)
                .Select(File.ReadAllText));
    }

    private static IEnumerable<string> ProductionSourceFiles(string project)
    {
        return Directory.GetFiles(
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
    }

    private static string[] FindRelativeCallers(
        IEnumerable<string> files,
        string pattern)
    {
        return [.. files
            .Where(file => File.ReadAllText(file).Contains(
                pattern,
                StringComparison.Ordinal))
            .Select(Relative)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)];
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
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
        throw new InvalidOperationException("Could not find the repository root.");
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

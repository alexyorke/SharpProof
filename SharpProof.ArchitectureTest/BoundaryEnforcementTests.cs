using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using static SharpProof.ArchitectureTest.ArchitectureRepository;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class BoundaryEnforcementTests
{
    private static readonly string[] SoundnessCriticalProjects = [..
        ProductionProjects.Where(static project =>
            XDocument.Load(ProjectFile(project))
                .Descendants("SharpProofUsesMetaAnalyzer")
                .Any(static element =>
                    string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase)))];
    private static readonly MetadataReference[] DescriptorAuditReferences =
        CreateDescriptorAuditReferences();

    private static readonly (string Project, string[] Grantees)[] ExpectedInternalsVisibleTo = [
        ("SharpProof.Analyzer.Core", [
            "SharpProof.Analyzer",
            "SharpProof.Analyzer.Test",
            "SharpProof.CompilerCollector",
            "SharpProof.ContractForGenerator",
            "SharpProof.ContractForGenerator.Test",
            "SharpProof.Gates",
            "SharpProof.Worker.Test"
        ]),
        ("SharpProof.Analyzer", [
            "SharpProof.Analyzer.Test",
            "SharpProof.Gates"
        ]),
        ("SharpProof.BuildTasks", ["SharpProof.Package.Test"]),
        ("SharpProof.CompilerArtifact", [
            "SharpProof.Analyzer.Test",
            "SharpProof.CompilerCollector",
            "SharpProof.Gates",
            "SharpProof.Package.Test",
            "SharpProof.Worker",
            "SharpProof.Worker.Launcher",
            "SharpProof.Worker.Test"
        ]),
        ("SharpProof.CompilerCollector", [
            "SharpProof.Analyzer.Test",
            "SharpProof.Gates",
            "SharpProof.Worker.Test"
        ]),
        ("SharpProof.Contracts", [
            "SharpProof.Analyzer",
            "SharpProof.Analyzer.Core",
            "SharpProof.CompilerCollector",
            "SharpProof.ContractForGenerator"
        ]),
        ("SharpProof.Dataflow", [
            "SharpProof.Analyzer.Core",
            "SharpProof.Dataflow.Test",
            "SharpProof.Effects"
        ]),
        ("SharpProof.Effects", [
            "SharpProof.Analyzer",
            "SharpProof.Analyzer.Core",
            "SharpProof.Analyzer.Test",
            "SharpProof.CompilerCollector",
            "SharpProof.Effects.Test"
        ]),
        ("SharpProof.Frontend", [
            "SharpProof.Analyzer",
            "SharpProof.Analyzer.Core",
            "SharpProof.CompilerCollector",
            "SharpProof.Contracts",
            "SharpProof.Effects",
            "SharpProof.Frontend.Test"
        ]),
        ("SharpProof.Fuzz", ["SharpProof.Fuzz.Test"]),
        ("SharpProof.Gates", ["SharpProof.Gates.Test"]),
        ("SharpProof.Host", [
            "SharpProof.BuildTasks",
            "SharpProof.Package.Test",
            "SharpProof.Worker",
            "SharpProof.Worker.Launcher",
            "SharpProof.Worker.Test"
        ]),
        ("SharpProof.Ir", [
            "SharpProof.Analyzer",
            "SharpProof.Analyzer.Core",
            "SharpProof.Analyzer.Test",
            "SharpProof.CompilerArtifact",
            "SharpProof.CompilerCollector",
            "SharpProof.Contracts",
            "SharpProof.Effects",
            "SharpProof.Frontend",
            "SharpProof.Fuzz",
            "SharpProof.Gates",
            "SharpProof.Ir.Test",
            "SharpProof.Smt",
            "SharpProof.Specs",
            "SharpProof.Specs.Test",
            "SharpProof.Summaries",
            "SharpProof.Testing",
            "SharpProof.Verify",
            "SharpProof.Worker",
            "SharpProof.Worker.Launcher"
        ]),
        ("SharpProof.Smt", ["SharpProof.Smt.Test"]),
        ("SharpProof.Specs", ["SharpProof.CompilerCollector"]),
        ("SharpProof.Verify", [
            "SharpProof.Fuzz",
            "SharpProof.Smt.Test",
            "SharpProof.Verify.Test",
            "SharpProof.Worker"
        ]),
        ("SharpProof.Worker.Launcher", ["SharpProof.Package.Test"]),
        ("SharpProof.Worker.Protocol", [
            "SharpProof.BuildTasks",
            "SharpProof.CompilerArtifact",
            "SharpProof.Gates",
            "SharpProof.Package.Test",
            "SharpProof.Worker",
            "SharpProof.Worker.Launcher",
            "SharpProof.Worker.Test"
        ]),
        ("SharpProof.Worker", ["SharpProof.Worker.Test"])
    ];

    [Test]
    public void BannedApiAnalyzerIsScopedToProductionProjects()
    {
        var root = TestRepository.FindRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var marker = props
            .Descendants("SharpProofProductionProject")
            .Single();
        var condition = (string?)marker.Attribute("Condition") ?? string.Empty;
        Assert.That(condition,
            Does.Contain("'$(SharpProofTestProject)' != 'true'")
                .And.Contain("samples|eng")
                .And.Contain("Testing|Package|Verifier")
                .And.Contain("Smoke\\.Net472")
                .And.Contain("CompilerProbe\\.TestAsset")
                .And.Not.Contain("PortableAnalyzer"));
        Assert.That(condition, Does.Not.Contain("== 'SharpProof."));

        var scopedGroup = props
            .Descendants("ItemGroup")
            .Single(group =>
                string.Equals(
                    (string?)group.Attribute("Condition"),
                    "'$(SharpProofProductionProject)' == 'true'",
                    StringComparison.Ordinal));
        var package = scopedGroup.Elements("PackageReference").Single();
        Assert.That(
            (string?)package.Attribute("Include"),
            Is.EqualTo("Microsoft.CodeAnalysis.BannedApiAnalyzers"));
        Assert.That(
            (string?)package.Attribute("PrivateAssets"),
            Is.EqualTo("all"));
        Assert.That(
            scopedGroup.Elements("AdditionalFiles")
                .Single()
                .Attribute("Include")?.Value,
            Does.EndWith("BannedSymbols.txt"));
        var productionProperties = props
            .Descendants("PropertyGroup")
            .Single(group =>
                string.Equals(
                    (string?)group.Attribute("Condition"),
                    "'$(SharpProofProductionProject)' == 'true'",
                    StringComparison.Ordinal));
        Assert.That(
            productionProperties.Element("TreatWarningsAsErrors")?.Value,
            Is.EqualTo("true"));
        var scopedWarnings = productionProperties.Element("WarningsAsErrors")?.Value;
        Assert.That(scopedWarnings, Does.Contain("RS0030"));
    }

    [Test]
    public void GeneratedProductionFilesAreExplicitlyApproved()
    {
        var root = TestRepository.FindRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "eng",
            "generated",
            "approved-outputs.v1.json")));
        Assert.That(
            manifest.RootElement.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(1));
        var approved = manifest.RootElement
            .GetProperty("outputs")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        Assert.That(
            approved.Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(approved.Length),
            "Generated-output paths must be unique.");

        var actual = BannedApiProjects
            .SelectMany(ProductionSourceFiles)
            .Where(path =>
                Regex.IsMatch(
                    path,
                    @"\.(g|generated)\.cs$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    File.ReadAllText(path),
                    @"(?im)^\s*//\s*<auto-generated(?:\s*/>|>)",
                    RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            actual,
            Is.EqualTo(approved.OrderBy(
                static path => path,
                StringComparer.Ordinal)));
    }

    [Test]
    public void BannedSymbolInventoryCoversEverySoundnessBoundary()
    {
        var text = File.ReadAllText(
            Path.Combine(TestRepository.FindRoot(), "BannedSymbols.txt"));
        var required = new[] {
            "Compilation.ReplaceSyntaxTree(Microsoft.CodeAnalysis.SyntaxTree,Microsoft.CodeAnalysis.SyntaxTree)",
            "Compilation.AddSyntaxTrees(Microsoft.CodeAnalysis.SyntaxTree[])",
            "Compilation.AddSyntaxTrees(System.Collections.Generic.IEnumerable{Microsoft.CodeAnalysis.SyntaxTree})",
            "Compilation.RemoveSyntaxTrees(Microsoft.CodeAnalysis.SyntaxTree[])",
            "Compilation.RemoveSyntaxTrees(System.Collections.Generic.IEnumerable{Microsoft.CodeAnalysis.SyntaxTree})",
            "Compilation.RemoveAllSyntaxTrees",
            "Compilation.GetSymbolsWithName",
            "Compilation.GetSemanticModel",
            "SemanticModel.GetDiagnostics",
            "GetSpeculativeSemanticModel",
            "SemanticModel.GetSpeculativeTypeInfo",
            "SyntaxFactory.ParseStatement",
            "SyntaxFactory.ParseExpression",
            "SyntaxFactory.ParseTypeName",
            "ISymbol.ToDisplayString"
        };

        foreach (var member in required)
        {
            Assert.That(text, Does.Contain(member), member);
        }
    }

    [Test]
    public void SemanticModelsFlowThroughTheSingleAuditedHostAdapter()
    {
        const string adapterProject = "SharpProof.Frontend";
        const string adapterFile = "CompilationModelProvider.cs";
        var directCallFiles = new List<string>();
        var suppressionFiles = new List<string>();

        foreach (var project in BannedApiProjects)
        {
            foreach (var file in ProductionSourceFiles(project))
            {
                var source = File.ReadAllText(file);
                var compact = Regex.Replace(source, @"\s+", string.Empty)
                    .Replace(
                        "SharpProof.Frontend.Host." +
                        "CompilationModelProvider.GetSemanticModel(",
                        "AllowedSemanticModel(",
                        StringComparison.Ordinal)
                    .Replace(
                        "CompilationModelProvider.GetSemanticModel(",
                        "AllowedSemanticModel(",
                        StringComparison.Ordinal);
                if (compact.Contains(
                        ".GetSemanticModel(",
                        StringComparison.Ordinal))
                {
                    directCallFiles.Add(TestRepository.Relative(file));
                }

                if (source.Contains(
                        "#pragma warning disable RS0030",
                        StringComparison.Ordinal))
                {
                    suppressionFiles.Add(TestRepository.Relative(file));
                }
            }
        }

        var expected =
            Path.Combine(adapterProject, adapterFile).Replace('\\', '/');
        Assert.That(directCallFiles, Is.EqualTo([expected]));
        Assert.That(suppressionFiles, Is.EqualTo([expected]));

        var adapter = File.ReadAllText(
            Path.Combine(TestRepository.FindRoot(), adapterProject, adapterFile));
        Assert.That(
            adapter,
            Does.Contain("The single audited boundary"));
        Assert.That(
            TestTextHelpers.CountOrdinal(adapter, "#pragma warning disable RS0030"),
            Is.EqualTo(1));
        Assert.That(
            TestTextHelpers.CountOrdinal(adapter, "#pragma warning restore RS0030"),
            Is.EqualTo(1));
    }

    [Test]
    public void ThinAnalyzerHasOnlyCurrentFrontendDependencies()
    {
        var direct = ProjectReferences("SharpProof.Analyzer");
        string[] expectedDirect = ["SharpProof.Analyzer.Core"];
        Assert.That(
            direct.OrderBy(static value => value, StringComparer.Ordinal),
            Is.EqualTo(expectedDirect));

        var closure = TransitiveProjectClosure(
            "SharpProof.Analyzer",
            includeRoot: false);
        Assert.That(
            closure,
            Does.Not.Contain("SharpProof.CompilerArtifact"));
        Assert.That(
            closure,
            Does.Not.Contain("SharpProof.Worker.Protocol"));
        Assert.That(closure, Does.Not.Contain("SharpProof.Smt"));
        Assert.That(closure, Does.Not.Contain("SharpProof.Verify"));
        Assert.That(
            ProjectPackages("SharpProof.Analyzer"),
            Does.Not.Contain("Microsoft.Z3"));
        Assert.That(
            ProjectPackages("SharpProof.Analyzer"),
            Does.Not.Contain("System.Text.Json"));

        var source = ReadProductionSources("SharpProof.Analyzer");
        Assert.That(
            source,
            Does.Not.Contain("SharpProof.Worker.Protocol"));
        Assert.That(source, Does.Not.Contain("Microsoft.Z3"));
        Assert.That(source, Does.Not.Contain("SharpProof.Smt"));
        Assert.That(source, Does.Not.Contain("SharpProof.Verify"));
    }

    [Test]
    public void EverySoundnessCriticalProjectRunsTheMetaAnalyzer()
    {
        var buildTargets = XDocument.Load(Path.Combine(
            TestRepository.FindRoot(),
            "Directory.Build.targets"));
        var centralReference = buildTargets
            .Descendants("ProjectReference")
            .SingleOrDefault(element =>
                Path.GetFileNameWithoutExtension(
                    ((string?)element.Attribute("Include") ?? string.Empty)
                        .Replace('\\', '/')) ==
                "SharpProof.Meta.Analyzers");
        Assert.That(centralReference, Is.Not.Null);
        Assert.That(
            (string?)centralReference!.Attribute("OutputItemType"),
            Is.EqualTo("Analyzer"));
        Assert.That(
            (string?)centralReference.Attribute("ReferenceOutputAssembly"),
            Is.EqualTo("false"));
        Assert.That(
            centralReference.Parent?.Attribute("Condition")?.Value,
            Does.Contain("$(SharpProofUsesMetaAnalyzer)"));

        var inlineReferences = ProductionProjects
            .Where(static project => XDocument.Load(ProjectFile(project))
                .Descendants("ProjectReference")
                .Any(static element =>
                    Path.GetFileNameWithoutExtension(
                        ((string?)element.Attribute("Include") ?? string.Empty)
                            .Replace('\\', '/')) ==
                    "SharpProof.Meta.Analyzers"))
            .ToArray();
        Assert.That(inlineReferences, Is.Empty);

    }

    [Test]
    public void InternalsVisibleToMatchesApprovedAssemblyBoundary()
    {
        var actual = BannedApiProjects
            .SelectMany(project => XDocument.Load(ProjectFile(project))
                .Descendants("InternalsVisibleTo")
                .Select(element =>
                    $"{project}|{(string?)element.Attribute("Include")}"))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var expected = ExpectedInternalsVisibleTo
            .SelectMany(static entry => entry.Grantees.Select(grantee =>
                $"{entry.Project}|{grantee}"))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void MetaAnalyzerSelfDogfoodsBannedApisWithoutReferencingItself()
    {
        Assert.That(
            BannedApiProjects,
            Does.Contain("SharpProof.Meta.Analyzers"));
        Assert.That(
            SoundnessCriticalProjects,
            Does.Not.Contain("SharpProof.Meta.Analyzers"));
        Assert.That(
            ProjectReferences("SharpProof.Meta.Analyzers"),
            Does.Not.Contain("SharpProof.Meta.Analyzers"));
        Assert.That(
            ReadProductionSources("SharpProof.Meta.Analyzers"),
            Does.Not.Contain("ToDisplayString("));
    }

    [Test]
    public void DiagnosticDescriptorsComeOnlyFromTheGeneratedCatalog()
    {
        var root = TestRepository.FindRoot();
        Assert.That(
            File.Exists(Path.Combine(
                root,
                "SharpProof.Analyzer",
                "AnalyzerDiagnosticCatalog.cs")),
            Is.False);

        using var catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "eng", "diagnostics", "diagnostic-descriptors.v1.json")));
        var allowedPaths = catalog.RootElement.GetProperty("outputs")
            .EnumerateArray()
            .Select(output => output.GetProperty("outputPath").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var descriptorProjects = ProductionRoslynProjects();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptorProjects, Does.Contain("SharpProof.Analyzer.Core"));
            Assert.That(descriptorProjects, Does.Contain("SharpProof.Meta.Analyzers"));
            Assert.That(descriptorProjects, Does.Contain("SharpProof.Gates"));
        }

        var observedCatalogPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in descriptorProjects)
        {
            var sourceFiles = ProductionSourceFiles(project).ToArray();
            foreach (var file in sourceFiles)
            {
                Assert.That(
                    File.ReadAllText(file),
                    Does.Not.Contain("AnalyzerDiagnosticCatalog.Get("),
                    TestRepository.Relative(file));
            }

            var trees = sourceFiles
                .Select(file => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(file),
                    new CSharpParseOptions(LanguageVersion.Preview),
                    path: file))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "SharpProofDescriptorAudit_" + project.Replace('.', '_'),
                trees,
                DescriptorAuditReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var descriptorType = compilation.GetTypeByMetadataName(
                "Microsoft.CodeAnalysis.DiagnosticDescriptor");
            Assert.That(descriptorType, Is.Not.Null, project);

            foreach (var (tree, creation) in FindDiagnosticDescriptorCreations(
                         compilation,
                         trees))
            {
                var relativePath = Path.GetRelativePath(root, tree.FilePath)
                    .Replace('\\', '/');
                Assert.That(
                    allowedPaths,
                    Does.Contain(relativePath),
                    relativePath + " constructs DiagnosticDescriptor at " +
                    creation.GetLocation().GetLineSpan().StartLinePosition);
                observedCatalogPaths.Add(relativePath);
            }
        }

        var generated = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Analyzer.Core",
            "GeneratedDiagnosticDescriptors.generated.cs"));
        Assert.That(generated, Does.Contain("SupportedDiagnostics"));
        Assert.That(
            observedCatalogPaths,
            Is.EquivalentTo(allowedPaths),
            "Each declared generated catalog must contain descriptor creations.");
    }

    [Test]
    public void DiagnosticDescriptorBackstopRecognizesTargetTypedCreation()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            using Microsoft.CodeAnalysis;
            static class OutsideCatalog {
                static readonly DiagnosticDescriptor Rule = new(
                    "ID", "title", "message", "category",
                    DiagnosticSeverity.Info, true);
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "TargetTypedDescriptorAudit",
            [tree],
            DescriptorAuditReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var creations = FindDiagnosticDescriptorCreations(compilation, [tree]);
        Assert.That(creations.Count(), Is.EqualTo(1));
    }

    [Test]
    public void CurrentProductionDoesNotParseContractStrings()
    {
        var parserCallers = BannedApiProjects
            .SelectMany(ProductionSourceFiles)
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"SyntaxFactory\s*\.\s*Parse" +
                @"(?:Expression|Statement|TypeName)\s*\("))
            .Select(TestRepository.Relative)
            .ToArray();

        Assert.That(parserCallers, Is.Empty);
    }

    [Test]
    public void ProductionProjectsDoNotUseSemanticDisplayText()
    {
        foreach (var project in BannedApiProjects)
        {
            var source = ReadProductionSources(project);
            Assert.That(
                source,
                Does.Not.Contain("ToDisplayString("),
                project);
        }
    }

    [Test]
    public void ActiveSolutionContainsExactlyCurrentProjects()
    {
        string[] expected = [
            @"SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj",
            @"SharpProof.Analyzer.Core\SharpProof.Analyzer.Core.csproj",
            @"SharpProof.Analyzer\SharpProof.Analyzer.csproj",
            @"SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj",
            @"SharpProof.Attributes.Test\SharpProof.Attributes.Test.csproj",
            @"SharpProof.Attributes\SharpProof.Attributes.csproj",
            @"SharpProof.BuildTasks\SharpProof.BuildTasks.csproj",
            @"SharpProof.CompilerProbe.TestAsset\SharpProof.CompilerProbe.TestAsset.csproj",
            @"SharpProof.CompilerArtifact\SharpProof.CompilerArtifact.csproj",
            @"SharpProof.CompilerCollector\SharpProof.CompilerCollector.csproj",
            @"SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj",
            @"SharpProof.ContractForGenerator\SharpProof.ContractForGenerator.csproj",
            @"SharpProof.Contracts.Test\SharpProof.Contracts.Test.csproj",
            @"SharpProof.Contracts\SharpProof.Contracts.csproj",
            @"SharpProof.Dataflow.Test\SharpProof.Dataflow.Test.csproj",
            @"SharpProof.Dataflow\SharpProof.Dataflow.csproj",
            @"SharpProof.Effects.Test\SharpProof.Effects.Test.csproj",
            @"SharpProof.Effects\SharpProof.Effects.csproj",
            @"SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj",
            @"SharpProof.Frontend\SharpProof.Frontend.csproj",
            @"SharpProof.Ir.Test\SharpProof.Ir.Test.csproj",
            @"SharpProof.Ir\SharpProof.Ir.csproj",
            @"SharpProof.Meta.Analyzers.Test\SharpProof.Meta.Analyzers.Test.csproj",
            @"SharpProof.Meta.Analyzers\SharpProof.Meta.Analyzers.csproj",
            @"SharpProof.Package.Test\SharpProof.Package.Test.csproj",
            @"SharpProof.Package\SharpProof.Package.csproj",
            @"SharpProof.Smoke.Net472\SharpProof.Smoke.Net472.csproj",
            @"SharpProof.Smt.Test\SharpProof.Smt.Test.csproj",
            @"SharpProof.Smt\SharpProof.Smt.csproj",
            @"SharpProof.Specs.Test\SharpProof.Specs.Test.csproj",
            @"SharpProof.Specs\SharpProof.Specs.csproj",
            @"SharpProof.Summaries.Test\SharpProof.Summaries.Test.csproj",
            @"SharpProof.Summaries\SharpProof.Summaries.csproj",
            @"SharpProof.Testing.Test\SharpProof.Testing.Test.csproj",
            @"SharpProof.Testing\SharpProof.Testing.csproj",
            @"SharpProof.Verifier\SharpProof.Verifier.csproj",
            @"SharpProof.Fuzz.Test\SharpProof.Fuzz.Test.csproj",
            @"SharpProof.Gates.Test\SharpProof.Gates.Test.csproj",
            @"SharpProof.Gates\SharpProof.Gates.csproj",
            @"SharpProof.Host\SharpProof.Host.csproj",
            @"SharpProof.Verify.Test\SharpProof.Verify.Test.csproj",
            @"SharpProof.Verify\SharpProof.Verify.csproj",
            @"SharpProof.Worker.Launcher\SharpProof.Worker.Launcher.csproj",
            @"SharpProof.Worker.Protocol\SharpProof.Worker.Protocol.csproj",
            @"SharpProof.Worker.Test\SharpProof.Worker.Test.csproj",
            @"SharpProof.Worker\SharpProof.Worker.csproj",
            @"Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj"
        ];
        var actual = XDocument.Load(
                Path.Combine(TestRepository.FindRoot(), "SharpProof.slnx"))
            .Descendants("Project")
            .Select(static project => (string?)project.Attribute("Path"))
            .Where(static path =>
                path?.EndsWith(".csproj", StringComparison.Ordinal) == true)
            .Select(static path => path!.Replace('/', '\\'))
            .ToArray();

        Assert.That(actual, Is.EquivalentTo(expected));
        foreach (var project in actual)
        {
            Assert.That(
                File.Exists(Path.Combine(
                    TestRepository.FindRoot(),
                    project.Replace(
                        '\\',
                        Path.DirectorySeparatorChar))),
                Is.True,
                project);
        }
    }

    [Test]
    public void AnalyzerPackagePayloadExcludesWorkerAndSolverAssets()
    {
        var packageFile =
            Path.Combine(
                TestRepository.FindRoot(),
                "SharpProof.Package",
                "SharpProof.Package.csproj");
        var package = XDocument.Load(packageFile);
        var analyzerPayload = string.Join(
            ";",
            package.Descendants("TfmSpecificPackageFile")
                .Where(element =>
                {
                    var path = (string?)element.Attribute("PackagePath");
                    return string.IsNullOrWhiteSpace(path) ||
                           path.Contains(
                               "analyzers",
                               StringComparison.OrdinalIgnoreCase);
                })
                .Select(element =>
                    (string?)element.Attribute("Include") ?? string.Empty));
        string[] forbiddenAssets = [
            "Microsoft.Z3",
            "libz3",
            "SharpProof.Smt",
            "SharpProof.Verify",
            "SharpProof.Worker.dll",
            "SharpProof.Worker.deps.json",
            "SharpProof.Worker.runtimeconfig.json",
            "SharpProof.Worker.Launcher"
        ];
        foreach (var forbidden in forbiddenAssets)
        {
            Assert.That(
                analyzerPayload,
                Does.Not.Contain(forbidden),
                forbidden);
        }
    }

    private static string[] ProductionRoslynProjects()
    {
        return BannedApiProjects
            .Where(project => XDocument.Load(ProjectFile(project))
                .Descendants()
                .Where(static element =>
                    element.Name.LocalName is "PackageReference" or "Reference")
                .Select(static element =>
                    (string?)element.Attribute("Include") ??
                    (string?)element.Attribute("Update"))
                .Any(static include =>
                    include is "Microsoft.CodeAnalysis" or
                        "Microsoft.CodeAnalysis.CSharp" ||
                    include?.StartsWith(
                        "Microsoft.CodeAnalysis.",
                        StringComparison.Ordinal) == true))
            .OrderBy(static project => project, StringComparer.Ordinal)
            .ToArray();
    }

    private static MetadataReference[] CreateDescriptorAuditReferences()
    {
        var trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            throw new InvalidOperationException(
                "The runtime did not provide its trusted platform assemblies.");
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(Compilation).Assembly.Location)
            .Append(typeof(CSharpCompilation).Assembly.Location)
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static IEnumerable<(SyntaxTree Tree, SyntaxNode Creation)>
        FindDiagnosticDescriptorCreations(
            CSharpCompilation compilation,
            IEnumerable<SyntaxTree> trees)
    {
        var descriptorType = compilation.GetTypeByMetadataName(
            "Microsoft.CodeAnalysis.DiagnosticDescriptor");
        if (descriptorType is null)
        {
            yield break;
        }

        foreach (var tree in trees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            foreach (var creation in tree.GetRoot().DescendantNodes()
                         .Where(static node =>
                             node is ObjectCreationExpressionSyntax or
                                 ImplicitObjectCreationExpressionSyntax))
            {
                var typeInfo = semanticModel.GetTypeInfo((ExpressionSyntax)creation);
                var type = typeInfo.Type ?? typeInfo.ConvertedType;
                if (SymbolEqualityComparer.Default.Equals(type, descriptorType))
                {
                    yield return (tree, creation);
                }
            }
        }
    }

}

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
            "SharpProof.Dataflow.Test"
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
            .SelectMany(project => Directory.GetFiles(
                ProjectDirectory(project),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(static path =>
                !path.Contains(
                    Path.DirectorySeparatorChar + "bin" +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !path.Contains(
                    Path.DirectorySeparatorChar + "obj" +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
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

        foreach (var project in SoundnessCriticalProjects)
        {
            Assert.That(
                XDocument.Load(ProjectFile(project))
                    .Descendants("SharpProofUsesMetaAnalyzer")
                    .Any(static element =>
                        string.Equals(
                            element.Value,
                            "true",
                            StringComparison.OrdinalIgnoreCase)),
                Is.True,
                project);
        }
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
        Assert.That(
            File.Exists(Path.Combine(
                TestRepository.FindRoot(),
                "SharpProof.Analyzer",
                "AnalyzerDiagnosticCatalog.cs")),
            Is.False);

        string[] descriptorProjects = [
            "SharpProof.Analyzer.Core",
            "SharpProof.Meta.Analyzers"
        ];
        foreach (var project in descriptorProjects)
        {
            foreach (var file in ProductionSourceFiles(project))
            {
                var source = File.ReadAllText(file);
                Assert.That(
                    source,
                    Does.Not.Contain("AnalyzerDiagnosticCatalog.Get("),
                    TestRepository.Relative(file));
                if (Path.GetFileName(file).EndsWith(
                        "DiagnosticDescriptors.generated.cs",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(
                    Regex.IsMatch(
                        source,
                        @"new\s+DiagnosticDescriptor\s*\("),
                    Is.False,
                    TestRepository.Relative(file));
            }
        }

        var generated = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Analyzer.Core",
            "GeneratedDiagnosticDescriptors.generated.cs"));
        Assert.That(
            generated,
            Does.Contain("SupportedDiagnostics"));
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
        var actual = File.ReadLines(
                Path.Combine(TestRepository.FindRoot(), "SharpProof.sln"))
            .Select(line => Regex.Match(
                line,
                "^Project\\(.*\\) = \".*\", \"(?<path>[^\"]+\\.csproj)\""))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["path"].Value)
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

}

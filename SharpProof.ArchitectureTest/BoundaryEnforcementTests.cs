using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class BoundaryEnforcementTests
{
    private static readonly string[] BannedApiProjects = [
        "SharpProof.Analyzer",
        "SharpProof.Analyzer.Core",
        "SharpProof.Attributes",
        "SharpProof.BuildTasks",
        "SharpProof.CompilerArtifact",
        "SharpProof.CompilerCollector",
        "SharpProof.ContractForGenerator",
        "SharpProof.Contracts",
        "SharpProof.Dataflow",
        "SharpProof.Effects",
        "SharpProof.Frontend",
        "SharpProof.Fuzz",
        "SharpProof.Gates",
        "SharpProof.Host",
        "SharpProof.Ir",
        "SharpProof.Meta.Analyzers",
        "SharpProof.Smt",
        "SharpProof.Specs",
        "SharpProof.Summaries",
        "SharpProof.Verify",
        "SharpProof.Worker",
        "SharpProof.Worker.Launcher",
        "SharpProof.Worker.Protocol"
    ];

    private static readonly string[] SoundnessCriticalProjects = [
        "SharpProof.Analyzer",
        "SharpProof.Analyzer.Core",
        "SharpProof.CompilerArtifact",
        "SharpProof.CompilerCollector",
        "SharpProof.ContractForGenerator",
        "SharpProof.Contracts",
        "SharpProof.Dataflow",
        "SharpProof.Effects",
        "SharpProof.Frontend",
        "SharpProof.Host",
        "SharpProof.Ir",
        "SharpProof.Smt",
        "SharpProof.Specs",
        "SharpProof.Summaries",
        "SharpProof.Verify",
        "SharpProof.Worker"
    ];

    [Test]
    public void BannedApiAnalyzerIsScopedToProductionProjects()
    {
        var root = RepositoryRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var marker = props
            .Descendants("SharpProofProductionProject")
            .Single();
        var matches = Regex.Matches(
            (string?)marker.Attribute("Condition") ?? string.Empty,
            @"==\s*'([^']+)'");
        var actual = matches
            .Select(static match => match.Groups[1].Value)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            actual,
            Is.EqualTo(BannedApiProjects.OrderBy(
                static value => value,
                StringComparer.Ordinal)));

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
        var root = RepositoryRoot();
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
                Path.Combine(root, project),
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
            Path.Combine(RepositoryRoot(), "BannedSymbols.txt"));
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
            foreach (var file in SourceFiles(project))
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
                    directCallFiles.Add(Relative(file));
                }

                if (source.Contains(
                        "#pragma warning disable RS0030",
                        StringComparison.Ordinal))
                {
                    suppressionFiles.Add(Relative(file));
                }
            }
        }

        var expected =
            Path.Combine(adapterProject, adapterFile).Replace('\\', '/');
        Assert.That(directCallFiles, Is.EqualTo([expected]));
        Assert.That(suppressionFiles, Is.EqualTo([expected]));

        var adapter = File.ReadAllText(
            Path.Combine(RepositoryRoot(), adapterProject, adapterFile));
        Assert.That(
            adapter,
            Does.Contain("The single audited boundary"));
        Assert.That(
            Count(adapter, "#pragma warning disable RS0030"),
            Is.EqualTo(1));
        Assert.That(
            Count(adapter, "#pragma warning restore RS0030"),
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

        var closure = TransitiveProjectClosure("SharpProof.Analyzer");
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
        foreach (var project in SoundnessCriticalProjects)
        {
            var reference = XDocument.Load(ProjectFile(project))
                .Descendants("ProjectReference")
                .SingleOrDefault(element =>
                    Path.GetFileNameWithoutExtension(
                        ((string?)element.Attribute("Include") ?? string.Empty)
                            .Replace('\\', '/')) ==
                    "SharpProof.Meta.Analyzers");
            Assert.That(reference, Is.Not.Null, project);
            Assert.That(
                (string?)reference!.Attribute("OutputItemType"),
                Is.EqualTo("Analyzer"),
                project);
            Assert.That(
                (string?)reference.Attribute("ReferenceOutputAssembly"),
                Is.EqualTo("false"),
                project);
        }
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
                RepositoryRoot(),
                "SharpProof.Analyzer",
                "AnalyzerDiagnosticCatalog.cs")),
            Is.False);

        string[] descriptorProjects = [
            "SharpProof.Analyzer.Core",
            "SharpProof.Meta.Analyzers"
        ];
        foreach (var project in descriptorProjects)
        {
            foreach (var file in SourceFiles(project))
            {
                var source = File.ReadAllText(file);
                Assert.That(
                    source,
                    Does.Not.Contain("AnalyzerDiagnosticCatalog.Get("),
                    Relative(file));
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
                    Relative(file));
            }
        }

        var generated = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
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
            .SelectMany(SourceFiles)
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"SyntaxFactory\s*\.\s*Parse" +
                @"(?:Expression|Statement|TypeName)\s*\("))
            .Select(Relative)
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
                Path.Combine(RepositoryRoot(), "SharpProof.sln"))
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
                    RepositoryRoot(),
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
                RepositoryRoot(),
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

    private static HashSet<string> TransitiveProjectClosure(
        string rootProject)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(rootProject);
        while (pending.Count != 0)
        {
            var project = pending.Pop();
            foreach (var dependency in ProjectReferences(project))
            {
                if (result.Add(dependency))
                {
                    pending.Push(dependency);
                }
            }
        }
        return result;
    }

    private static string[] ProjectReferences(string project)
    {
        return [.. XDocument.Load(ProjectFile(project))
            .Descendants("ProjectReference")
            .Where(static element =>
                !string.Equals(
                    (string?)element.Attribute("OutputItemType"),
                    "Analyzer",
                    StringComparison.OrdinalIgnoreCase))
            .Select(static element =>
                Path.GetFileNameWithoutExtension(
                    ((string?)element.Attribute("Include") ?? string.Empty)
                        .Replace('\\', '/')))
            .Where(static value => !string.IsNullOrWhiteSpace(value))];
    }

    private static string[] ProjectPackages(string project)
    {
        return [.. XDocument.Load(ProjectFile(project))
            .Descendants("PackageReference")
            .Select(static element =>
                (string?)element.Attribute("Include") ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))];
    }

    private static IEnumerable<string> SourceFiles(string project)
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

    private static string ReadProductionSources(string project)
    {
        return string.Join("\n", SourceFiles(project).Select(File.ReadAllText));
    }

    private static string ProjectFile(string project)
    {
        return Path.Combine(RepositoryRoot(), project, project + ".csproj");
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SharpProof.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not find the repository root.");
    }
}

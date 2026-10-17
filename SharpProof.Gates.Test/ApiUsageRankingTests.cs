using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class ApiUsageRankingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Site(string Assembly, string Api, string Access, string Origin, string Caller, string Location, string Status);

    [Test]
    public void RankFrameworkApiUsageInSamplesAndOriginalCorpusTargets()
    {
        var root = RepositoryLayout.FindRoot();
        var inventory = File.ReadAllLines(Path.Combine(root, "eng/api-coverage/net9.0.tsv"))
            .Select(line => line.Split('\t')).ToDictionary(parts => parts[0] + "\t" + parts[1], parts => parts[3], StringComparer.Ordinal);
        var dotnetRoot = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!;
        var manifestPath = Path.Combine(root, "eng/api-coverage/net9.0.manifest.json");
        using var policy = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var pack = Path.Combine(dotnetRoot.FullName, "packs", policy.RootElement.GetProperty("pack").GetString()!,
            policy.RootElement.GetProperty("version").GetString()!, "ref", policy.RootElement.GetProperty("framework").GetString()!);
        var references = AnalyzerGateHost.CreateReferences([
            .. Directory.GetFiles(pack, "*.dll").Select(path => MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(Contract).Assembly.Location)
        ]);
        var sites = new List<Site>();
        var gaps = new List<string>();
        var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var corpusPath = Path.Combine(root, "SharpProof.Gates/Corpus/oss-methods.json");
        inputs.Add("SharpProof.Gates/Corpus/oss-methods.json", Hash(File.ReadAllText(corpusPath)));
        inputs.Add("SharpProof.Gates/Corpus/CorpusCatalog.cs", Hash(File.ReadAllText(Path.Combine(root, "SharpProof.Gates/Corpus/CorpusCatalog.cs"))));
        inputs.Add("eng/api-coverage/net9.0.manifest.json", Hash(File.ReadAllText(manifestPath)));
        inputs.Add("eng/api-coverage/net9.0.tsv", Hash(File.ReadAllText(Path.Combine(root, "eng/api-coverage/net9.0.tsv"))));
        var corpus = OpenSourceCorpusCatalog.Load(root);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(corpus, TestContext.CurrentContext.CancellationToken)
            .WithReferences(references);
        RequireCompilation(compilation);
        var targets = compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(method => OpenSourceCorpusRunner.CorpusMethodId(method) != null).ToArray();
        Assert.That(targets, Has.Length.EqualTo(corpus.Methods.Length));
        foreach (var target in targets)
        { Scan(compilation, target, "oss", OpenSourceCorpusRunner.CorpusMethodId(target)!, inventory, sites, gaps); }

        var seeds = CorpusCatalog.CreateSyntheticCases().Where(item => item.Variant == CorpusVariant.Baseline).ToArray();
        Assert.That(seeds, Has.Length.EqualTo(CorpusCatalog.Seeds.Length));
        foreach (var seed in seeds)
        {
            var synthetic = AnalyzerGateHost.CreateCompilation(seed.Source).WithReferences(references);
            RequireCompilation(synthetic);
            var target = synthetic.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Focus_" + seed.SeedId);
            Scan(synthetic, target, "synthetic", seed.SeedId, inventory, sites, gaps);
        }

        var sampleFiles = Directory.GetFiles(Path.Combine(root, "samples"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal) &&
                !path.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        var sampleTrees = sampleFiles.Select(path =>
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var source = File.ReadAllText(path);
            inputs.Add(relative, Hash(source));
            return CSharpSyntaxTree.ParseText(source, AnalyzerGateHost.ParseOptions, relative);
        }).ToArray();
        var implicitUsings = CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Linq; global using System.Threading.Tasks;",
            AnalyzerGateHost.ParseOptions);
        var samples = CSharpCompilation.Create("ApiUsageSamples", sampleTrees.Append(implicitUsings),
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompilation(samples);
        foreach (var tree in sampleTrees)
        { Scan(samples, tree.GetRoot(), "samples", tree.FilePath, inventory, sites, gaps); }

        var ranked = sites.GroupBy(site => site.Assembly + "\t" + site.Api + "\t" + site.Access)
            .Select(group => new
            {
                assembly = group.First().Assembly,
                api = group.First().Api,
                access = group.First().Access,
                status = group.First().Status,
                sites = group.Count(),
                callers = group.Select(site => site.Origin + ":" + site.Caller).Distinct(StringComparer.Ordinal).Count(),
                samples = group.Count(site => site.Origin == "samples"),
                oss = group.Count(site => site.Origin == "oss"),
                synthetic = group.Count(site => site.Origin == "synthetic"),
                locations = group.OrderBy(site => site.Location, StringComparer.Ordinal).ToArray()
            }).OrderByDescending(item => item.sites).ThenBy(item => item.api, StringComparer.Ordinal).ToArray();
        var output = Path.Combine(root, "artifacts/api-coverage");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "usage-ranking.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            scope = "explicit calls, constructions, and property/indexer uses; selected OSS methods, original synthetic targets, and sample sources; no metamorphic duplicates or dependency-only methods",
            ossTargets = targets.Length,
            syntheticTargets = seeds.Length,
            sampleFiles = sampleFiles.Length,
            inputs,
            unresolved = gaps,
            ranked
        }, JsonOptions) + "\n");
        var report = new StringBuilder("# Framework API usage ranking\n\n")
            .Append(CultureInfo.InvariantCulture, $"{targets.Length} original OSS targets, {seeds.Length} synthetic baseline targets, {sampleFiles.Length} sample files. {sites.Count} resolved framework use sites. {gaps.Count} unresolved sites.\n\n")
            .Append("Counts are static source sites, not execution frequency. Dependency-only methods and metamorphic copies are excluded. Contextual lowering may support APIs without catalog evidence. Full source hashes and locations are in usage-ranking.json.\n\n")
            .Append("| API | Access | Sites | Caller units | Samples | OSS | Synthetic | Status |\n| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |\n");
        foreach (var item in ranked)
        { report.Append(CultureInfo.InvariantCulture, $"| {item.api} | {item.access} | {item.sites} | {item.callers} | {item.samples} | {item.oss} | {item.synthetic} | {item.status} |\n"); }
        File.WriteAllText(Path.Combine(output, "usage-ranking.md"), report.ToString());
        TestContext.Out.WriteLine($"Ranked {ranked.Length} framework APIs at {sites.Count} sites; unresolved: {gaps.Count}.");
        Assert.That(gaps, Is.Empty, "Unresolved sites must be investigated rather than silently omitted.");
    }

    private static void Scan(CSharpCompilation compilation, SyntaxNode region, string origin, string caller,
        Dictionary<string, string> inventory, List<Site> sites, List<string> gaps)
    {
        var model = compilation.GetSemanticModel(region.SyntaxTree);
        foreach (var syntax in region.DescendantNodes().Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or
            MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or IdentifierNameSyntax))
        {
            var symbol = model.GetSymbolInfo(syntax).Symbol;
            var call = syntax is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax;
            // Delegate construction is a compiler operation, without a callable
            // framework constructor symbol. It is not an unresolved API call.
            if (call && model.GetOperation(syntax) is INameOfOperation or IDelegateCreationOperation)
            { continue; }
            if (!call && (symbol is not IPropertySymbol || syntax is IdentifierNameSyntax { Parent: MemberAccessExpressionSyntax }))
            { continue; }
            if (symbol == null)
            {
                if (call)
                { gaps.Add(origin + ":" + caller + ":" + syntax); }
                continue;
            }
            if (symbol.Locations.Any(location => location.IsInSource) || symbol.ContainingAssembly?.Name is "SharpProof.Attributes" or "SharpProof.Gates.ExternalEffects")
            { continue; }
            symbol = symbol is IMethodSymbol method ? (method.ReducedFrom ?? method).OriginalDefinition : symbol.OriginalDefinition;
            var id = symbol.GetDocumentationCommentId();
            var assembly = symbol.ContainingAssembly?.Name ?? "";
            var key = assembly + "\t" + id;
            if (id == null || !inventory.TryGetValue(key, out var status))
            { gaps.Add(origin + ":" + caller + ":outside-inventory:" + key); continue; }
            var access = "call";
            if (symbol is IPropertySymbol)
            {
                var operation = model.GetOperation(syntax);
                access = operation?.Parent switch
                {
                    ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, operation) => "write",
                    ICompoundAssignmentOperation => "read-write",
                    IIncrementOrDecrementOperation => "read-write",
                    _ => "read"
                };
                // Property inventory evidence resolves the getter. Never credit
                // a setter with support gained by adding a getter declaration.
                if (access != "read")
                { status = "unmodeled-setter"; }
            }
            var line = syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            sites.Add(new(assembly, id, access, origin, caller, region.SyntaxTree.FilePath + ":" + line, status));
        }
    }

    [Test]
    public void UsageScanSeparatesGetterSetterAndOverloadsWithoutCreditingSourceMethods()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class UsageProbe {
                public static void Target(System.Collections.Generic.List<int> values) {
                    var first = values[0];
                    values[0] = System.Math.Max(first, 2);
                    values[0] += 1;
                    _ = System.Math.Max(1d, 2d);
                    _ = Local();
                }
                private static int Local() => 1;
            }
            """, TestContext.CurrentContext.CancellationToken);
        var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!;
        var property = list.GetMembers().OfType<IPropertySymbol>().Single(item => item.IsIndexer);
        var integerMax = compilation.GetTypeByMetadataName("System.Math")!.GetMembers("Max").OfType<IMethodSymbol>()
            .Single(method => method.Parameters.Length == 2 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32);
        var inventory = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [property.ContainingAssembly.Name + "\t" + property.GetDocumentationCommentId()] = "partially-modeled",
            [integerMax.ContainingAssembly.Name + "\t" + integerMax.GetDocumentationCommentId()] = "partially-modeled"
        };
        var sites = new List<Site>();
        var gaps = new List<string>();
        Scan(compilation, compilation.SyntaxTrees.Single().GetRoot(), "test", "probe", inventory, sites, gaps);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sites.Count(site => site.Access == "read"), Is.EqualTo(1));
            Assert.That(sites.Count(site => site.Access == "write"), Is.EqualTo(1));
            Assert.That(sites.Count(site => site.Access == "read-write"), Is.EqualTo(1));
            Assert.That(sites.Count(site => site.Status == "unmodeled-setter"), Is.EqualTo(2));
            Assert.That(sites.Count(site => site.Access == "call"), Is.EqualTo(1));
            Assert.That(gaps.Single(), Does.Contain("System.Double"));
        }
    }
    private static void RequireCompilation(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString()), Is.Empty, "Usage ranking requires fully bound inputs.");
    }

    private static string Hash(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}

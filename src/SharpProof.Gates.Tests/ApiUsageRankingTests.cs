using SharpProof.Ir;
using System.Collections.Immutable;
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
using SharpProof.Frontend;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class ApiUsageRankingTests
{
    private static readonly ImmutableArray<bool> FreshDictionaryCalls = [true, false, false];
    private static readonly ImmutableArray<bool> FreshHashSetCalls = [true, true, true, false, false, false];
    private static readonly ImmutableArray<bool> FreshListCalls = [true, false];
    private static readonly ImmutableArray<bool> FreshListIndexers = [true, true, false, false, false];
    private static readonly ImmutableArray<bool> FreshDictionaryIndexers = [true, true, false, false];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private sealed record Site(string Assembly, string Api, string Access, string Origin, string Caller, string Location, string Status)
    {
        public string Assembly { get; init; } = Assembly ?? throw new global::System.ArgumentNullException(nameof(Assembly));
        public string Api { get; init; } = Api ?? throw new global::System.ArgumentNullException(nameof(Api));
        public string Access { get; init; } = Access ?? throw new global::System.ArgumentNullException(nameof(Access));
        public string Origin { get; init; } = Origin ?? throw new global::System.ArgumentNullException(nameof(Origin));
        public string Caller { get; init; } = Caller ?? throw new global::System.ArgumentNullException(nameof(Caller));
        public string Location { get; init; } = Location ?? throw new global::System.ArgumentNullException(nameof(Location));
        public string Status { get; init; } = Status ?? throw new global::System.ArgumentNullException(nameof(Status));
    }

    [Test]
    public void RankFrameworkApiUsageInSamplesAndOriginalCorpusTargets()
    {
        var root = RepositoryLayout.FindRoot();
        var inventory = ImmutableDictionaryCapture.Create(File.ReadAllLines(Path.Combine(root, "eng/api-coverage/net10.0.tsv")).Select(line => line is null ? throw new global::System.ArgumentNullException(nameof(line)) : (line.Split('\t'))), parts => parts is null ? throw new global::System.ArgumentNullException(nameof(parts)) : (parts[0] + "\t" + parts[1]), parts => parts is null ? throw new global::System.ArgumentNullException(nameof(parts)) : (parts[3]), StringComparer.Ordinal);
        var dotnetRoot = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!;
        var manifestPath = Path.Combine(root, "eng/api-coverage/net10.0.manifest.json");
        using var policy = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var pack = Path.Combine(dotnetRoot.FullName, "packs", policy.RootElement.GetProperty("pack").GetString()!, policy.RootElement.GetProperty("version").GetString()!, "ref", policy.RootElement.GetProperty("framework").GetString()!);
        var references = AnalyzerGateHost.CreateReferences([.. Directory.GetFiles(pack, "*.dll").Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), MetadataReference.CreateFromFile(typeof(Contract).Assembly.Location)]);
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        var inputBuilder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        void AddInput(string key, string value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            if (inputBuilder.ContainsKey(key))
            {
                throw new ArgumentException("An item with the same key has already been added. Key: " + key);
            }
            inputBuilder.Add(key, value);
        }
        var corpusPath = Path.Combine(root, "src/SharpProof.Gates/Corpus/oss-methods.json");
        AddInput("src/SharpProof.Gates/Corpus/oss-methods.json", Hash(File.ReadAllText(corpusPath)));
        AddInput("src/SharpProof.Gates/Corpus/CorpusCatalog.cs", Hash(File.ReadAllText(Path.Combine(root, "src/SharpProof.Gates/Corpus/CorpusCatalog.cs"))));
        AddInput("eng/api-coverage/net10.0.manifest.json", Hash(File.ReadAllText(manifestPath)));
        AddInput("eng/api-coverage/net10.0.tsv", Hash(File.ReadAllText(Path.Combine(root, "eng/api-coverage/net10.0.tsv"))));
        var corpus = OpenSourceCorpusCatalog.Load(root);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(corpus, TestContext.CurrentContext.CancellationToken).WithReferences(references);
        RequireCompilation(compilation);
        var targets = compilation.SyntaxTrees.SelectMany(tree => tree is null ? throw new global::System.ArgumentNullException(nameof(tree)) : (tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())).Where(method => OpenSourceCorpusRunner.CorpusMethodId(method) != null).ToImmutableArray();
        Assert.That(targets, Has.Length.EqualTo(corpus.Methods.Length));
        foreach (var target in targets)
        {
            Scan(compilation, target, "oss", OpenSourceCorpusRunner.CorpusMethodId(target)!, inventory, siteBuilder, gapBuilder);
        }

        var seeds = CorpusCatalog.CreateSyntheticCases().Where(item => item is null ? throw new global::System.ArgumentNullException(nameof(item)) : (item.Variant == CorpusVariant.Baseline)).ToImmutableArray();
        Assert.That(seeds, Has.Length.EqualTo(CorpusCatalog.Seeds.Length));
        foreach (var seed in seeds)
        {
            var synthetic = AnalyzerGateHost.CreateCompilation(seed.Source).WithReferences(references);
            RequireCompilation(synthetic);
            var target = synthetic.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Focus_" + seed.SeedId));
            Scan(synthetic, target, "synthetic", seed.SeedId, inventory, siteBuilder, gapBuilder);
        }

        var sampleFiles = Directory.GetFiles(Path.Combine(root, "src/samples"), "*.cs", SearchOption.AllDirectories).Where(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (!path.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal) && !path.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToImmutableArray();
        var sampleTrees = sampleFiles.Select(path =>
        {
            global::System.ArgumentNullException.ThrowIfNull(path);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var source = File.ReadAllText(path);
            AddInput(relative, Hash(source));
            return CSharpSyntaxTree.ParseText(source, AnalyzerGateHost.ParseOptions, relative);
        }).ToImmutableArray();
        var implicitUsings = CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Linq; global using System.Threading.Tasks;", AnalyzerGateHost.ParseOptions);
        var samples = CSharpCompilation.Create("ApiUsageSamples", sampleTrees.Append(implicitUsings), references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompilation(samples);
        foreach (var tree in sampleTrees)
        {
            Scan(samples, tree.GetRoot(), "src/samples", tree.FilePath, inventory, siteBuilder, gapBuilder);
        }

        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        var inputs = inputBuilder.ToImmutable();
        var ranked = sites.GroupBy(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Assembly + "\t" + site.Api + "\t" + site.Access + "\t" + site.Status)).Select(group => group is null ? throw new global::System.ArgumentNullException(nameof(group)) : (new { assembly = group.First().Assembly, api = group.First().Api, access = group.First().Access, status = group.First().Status, sites = group.Count(), callers = group.Select(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Origin + ":" + site.Caller)).Distinct(StringComparer.Ordinal).Count(), samples = group.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Origin == "src/samples")), oss = group.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Origin == "oss")), synthetic = group.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Origin == "synthetic")), locations = group.OrderBy(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Location), StringComparer.Ordinal).ToImmutableArray() })).OrderByDescending(item => item is null ? throw new global::System.ArgumentNullException(nameof(item)) : (item.sites)).ThenBy(item => item is null ? throw new global::System.ArgumentNullException(nameof(item)) : (item.api), StringComparer.Ordinal).ToImmutableArray();
        var output = Path.Combine(root, "artifacts/api-coverage");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "usage-ranking.json"), JsonSerializer.Serialize(new { schemaVersion = 1, scope = "explicit calls, constructions, and property/indexer uses; selected OSS methods, original synthetic targets, and sample sources; no metamorphic duplicates or dependency-only methods", ossTargets = targets.Length, syntheticTargets = seeds.Length, sampleFiles = sampleFiles.Length, inputs, unresolved = gaps, ranked }, JsonOptions) + "\n");
        var report = new StringBuilder("# Framework API usage ranking\n\n").Append(CultureInfo.InvariantCulture, $"{targets.Length} original OSS targets, {seeds.Length} synthetic baseline targets, {sampleFiles.Length} sample files. {sites.Length} resolved framework use sites. {gaps.Length} unresolved sites.\n\n").Append("Counts are static source sites, not execution frequency. Dependency-only methods and metamorphic copies are excluded. Contextual lowering may support APIs without catalog evidence. Full source hashes and locations are in usage-ranking.json.\n\n").Append("| API | Access | Sites | Caller units | Samples | OSS | Synthetic | Status |\n| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |\n");
        foreach (var item in ranked)
        {
            report.Append(CultureInfo.InvariantCulture, $"| {item.api} | {item.access} | {item.sites} | {item.callers} | {item.samples} | {item.oss} | {item.synthetic} | {item.status} |\n");
        }

        File.WriteAllText(Path.Combine(output, "usage-ranking.md"), report.ToString());
        var backlog = File.ReadAllText(Path.Combine(root, "eng/api-coverage/gaps.md"));
        foreach (var item in ranked)
        {
            Assert.That(backlog, Does.Contain($"| {item.api} | {item.access} | {item.sites} | {item.status} |"),
                "Update the committed gap backlog for every new or changed ranked API/access row.");
        }
        foreach (var line in File.ReadAllLines(Path.Combine(root, "eng/api-coverage/net10.0.tsv")))
        {
            var parts = line.Split('\t');
            if (parts[3] == "partially-modeled")
            { Assert.That(backlog, Does.Contain("| " + parts[1] + " | " + parts[4] + " |"), "Every partially modeled declaration must retain its current facet evidence in the backlog."); }
        }
        TestContext.Out.WriteLine($"Ranked {ranked.Length} framework APIs at {sites.Length} sites; unresolved: {gaps.Length}.");
        Assert.That(gaps, Is.Empty, "Unresolved sites must be investigated rather than silently omitted.");
    }

    private static void Scan(CSharpCompilation compilation, SyntaxNode region, string origin, string caller, ImmutableDictionary<string, string> inventory, ImmutableArray<Site>.Builder sites, ImmutableArray<string>.Builder gaps)
    {
        global::System.ArgumentNullException.ThrowIfNull(compilation);
        global::System.ArgumentNullException.ThrowIfNull(region);
        global::System.ArgumentNullException.ThrowIfNull(origin);
        global::System.ArgumentNullException.ThrowIfNull(caller);
        global::System.ArgumentNullException.ThrowIfNull(inventory);
        global::System.ArgumentNullException.ThrowIfNull(sites);
        global::System.ArgumentNullException.ThrowIfNull(gaps);
        var model = compilation.GetSemanticModel(region.SyntaxTree);
        var knownIntrinsics = KnownIntrinsicSites(model, region);
        foreach (var syntax in region.DescendantNodes().Where(node => node is null ? throw new global::System.ArgumentNullException(nameof(node)) : (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or IdentifierNameSyntax)))
        {
            var symbol = model.GetSymbolInfo(syntax).Symbol;
            var call = syntax is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax;
            // Delegate construction is a compiler operation, without a callable
            // framework constructor symbol. It is not an unresolved API call.
            if (call && model.GetOperation(syntax) is INameOfOperation or IDelegateCreationOperation)
            {
                continue;
            }

            if (!call && (symbol is not IPropertySymbol || syntax is IdentifierNameSyntax { Parent: MemberAccessExpressionSyntax }))
            {
                continue;
            }

            if (symbol == null)
            {
                if (call)
                {
                    gaps.Add(origin + ":" + caller + ":" + syntax);
                }

                continue;
            }

            if (symbol.Locations.Any(location => location is null ? throw new global::System.ArgumentNullException(nameof(location)) : (location.IsInSource)) || symbol.ContainingAssembly?.Name is "SharpProof.Attributes" or "SharpProof.Gates.ExternalEffects")
            {
                continue;
            }

            symbol = symbol is IMethodSymbol method ? (method.ReducedFrom ?? method).OriginalDefinition : symbol.OriginalDefinition;
            var id = symbol.GetDocumentationCommentId();
            var assembly = symbol.ContainingAssembly?.Name ?? "";
            var key = assembly + "\t" + id;
            if (id == null || !inventory.TryGetValue(key, out var status))
            {
                gaps.Add(origin + ":" + caller + ":outside-inventory:" + key);
                continue;
            }

            // A declaration-level partial model does not cover every call site.
            if (symbol is IMethodSymbol declaration && CSharpOperationSemantics.IsIntegerComparerDeclaration(declaration) && model.GetOperation(syntax) is IInvocationOperation comparison && !CSharpOperationSemantics.IsDefaultIntegerComparison(comparison) && !knownIntrinsics.Contains(syntax.SpanStart))
            {
                status = "unmodeled-dispatch";
            }

            if (CSharpOperationSemantics.IsDictionaryStateDeclaration(symbol) && symbol is IMethodSymbol)
            {
                status = model.GetOperation(syntax) is IObjectCreationOperation creation
                    ? CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(creation) ? "partially-modeled" : "unmodeled-domain"
                    : knownIntrinsics.Contains(syntax.SpanStart) ? "partially-modeled" : "unmodeled-dispatch";
            }

            if (symbol is IMethodSymbol clearing && CSharpOperationSemantics.IsArrayClear(clearing))
            {
                status = model.GetOperation(syntax) is IInvocationOperation invocation && CSharpOperationSemantics.TypedArrayClearReceiver(invocation) != null
                    ? "partially-modeled" : "unmodeled-domain";
            }

            if ((symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod) is { } queueMethod && CSharpOperationSemantics.IsQueueStateMember(queueMethod))
            {
                var use = model.GetOperation(syntax);
                status = use switch
                {
                    IObjectCreationOperation { Initializer: null, Constructor: { } constructor } => CSharpOperationSemantics.IsQueueStateMember(constructor) ? "partially-modeled" : "unmodeled-domain",
                    IInvocationOperation invocation => CSharpOperationSemantics.IsQueueStateMember(invocation.TargetMethod) ? "partially-modeled" : "unmodeled-domain",
                    IPropertyReferenceOperation => "partially-modeled",
                    _ => "unmodeled-domain"
                };
            }
            if (CSharpOperationSemantics.IsHashSetStateDeclaration(symbol))
            {
                status = model.GetOperation(syntax) switch
                {
                    IObjectCreationOperation creation => CSharpOperationSemantics.IsEmptyDefaultHashSetCreation(creation) ? "partially-modeled" : "unmodeled-domain",
                    IPropertyReferenceOperation { Property.Name: "Count" } => "partially-modeled",
                    IInvocationOperation invocation => knownIntrinsics.Contains(syntax.SpanStart) ? "partially-modeled" :
                        CSharpOperationSemantics.IsHashSetStateMember(invocation.TargetMethod) ? "unmodeled-dispatch" : "unmodeled-domain",
                    _ => "unmodeled-domain"
                };
            }
            if (CSharpOperationSemantics.IsListStateDeclaration(symbol))
            {
                var use = model.GetOperation(syntax);
                status = use is IObjectCreationOperation creation && CSharpOperationSemantics.IsEmptyListCreation(creation) || knownIntrinsics.Contains(syntax.SpanStart) ||
                    symbol is IPropertySymbol { GetMethod: { } getter } && CSharpOperationSemantics.IsNonnegativeCollectionCount(getter)
                    ? "partially-modeled" : "unmodeled-dispatch";
            }

            if (model.GetOperation(syntax) is IPropertyReferenceOperation length && CSharpOperationSemantics.IsLength(length))
            { status = "partially-modeled"; }
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
                if (operation is IPropertyReferenceOperation property && CSharpOperationSemantics.IsArrayCollectionDeclaration(property.Property))
                {
                    status = CSharpOperationSemantics.ArrayCollectionReceiver(property) == null && !knownIntrinsics.Contains(syntax.SpanStart) ? "unmodeled-dispatch" : access == "read-write" ? "unmodeled-access" : "partially-modeled";
                }
                else if (symbol is IPropertySymbol { IsIndexer: true } collection &&
                    (CSharpOperationSemantics.IsDictionaryStateDeclaration(collection) || CSharpOperationSemantics.IsListStateDeclaration(collection)))
                {
                    status = !knownIntrinsics.Contains(syntax.SpanStart) ? "unmodeled-dispatch"
                        : access == "read-write" ? "unmodeled-access" : "partially-modeled";
                }
                // Catalog property evidence resolves only the getter. The
                // guarded array intrinsic above explicitly models simple stores.
                else if (access != "read")
                {
                    status = "unmodeled-setter";
                }
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
        var property = list.GetMembers().OfType<IPropertySymbol>().Single(item => item is null ? throw new global::System.ArgumentNullException(nameof(item)) : (item.IsIndexer));
        var integerMax = compilation.GetTypeByMetadataName("System.Math")!.GetMembers("Max").OfType<IMethodSymbol>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Parameters.Length == 2 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32));
        var inventoryBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        inventoryBuilder[property.ContainingAssembly.Name + "\t" + property.GetDocumentationCommentId()] = "partially-modeled";
        inventoryBuilder[integerMax.ContainingAssembly.Name + "\t" + integerMax.GetDocumentationCommentId()] = "partially-modeled";
        var inventory = inventoryBuilder.ToImmutable();
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        Scan(compilation, compilation.SyntaxTrees.Single().GetRoot(), "test", "probe", inventory, siteBuilder, gapBuilder);
        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Access == "read")), Is.EqualTo(1));
            Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Access == "write")), Is.EqualTo(1));
            Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Access == "read-write")), Is.EqualTo(1));
            Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Status == "unmodeled-dispatch")), Is.EqualTo(1));
            Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Access == "call")), Is.EqualTo(1));
            Assert.That(gaps.Single(), Does.Contain("System.Double"));
        }
    }

    [Test]
    public void UsageScanCreditsOnlyTheGuardedDefaultIntegerComparerCalls()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class UsageProbe {
                public static void Target<T>(System.Collections.Generic.Comparer<int> custom, T left, T right) {
                    _ = System.Collections.Generic.Comparer<int>.Default.Compare(1, 2);
                    _ = custom.Compare(1, 2);
                    var alias = System.Collections.Generic.Comparer<int>.Default;
                    _ = alias.Compare(1, 2);
                    alias = custom;
                    _ = alias.Compare(1, 2);
                    var known = System.Collections.Generic.Comparer<int>.Default;
                    _ = known.Compare(1, 2);
                    _ = System.Collections.Generic.Comparer<T>.Default.Compare(left, right);
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var comparer = compilation.GetTypeByMetadataName("System.Collections.Generic.Comparer`1")!;
        var compare = comparer.GetMembers("Compare").OfType<IMethodSymbol>().Single();
        var getter = comparer.GetMembers("Default").OfType<IPropertySymbol>().Single();
        var inventoryBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        inventoryBuilder[compare.ContainingAssembly.Name + "\t" + compare.GetDocumentationCommentId()] = "partially-modeled";
        inventoryBuilder[getter.ContainingAssembly.Name + "\t" + getter.GetDocumentationCommentId()] = "unmodeled";
        var inventory = inventoryBuilder.ToImmutable();
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        Scan(compilation, compilation.SyntaxTrees.Single().GetRoot(), "test", "probe", inventory, siteBuilder, gapBuilder);
        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        Assert.That(gaps, Is.Empty);
        Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Access == "call" && site.Status == "partially-modeled")), Is.EqualTo(2));
        Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Status == "unmodeled-dispatch")), Is.EqualTo(4));
    }

    [Test]
    public void UsageScanSeparatesGuardedInterfaceReadsStoresAndUnknownDispatch()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class UsageProbe {
                public static void Target(int[] values, System.Collections.Generic.IList<int> list,
                    System.Collections.Generic.ICollection<int> collection) {
                    _ = ((System.Collections.Generic.ICollection<int>)values).Count;
                    _ = ((System.Collections.Generic.IList<int>)values)[0];
                    ((System.Collections.Generic.IList<int>)values)[0] = 7;
                    ((System.Collections.Generic.IList<int>)values)[0] += 1;
                    System.Collections.Generic.IList<int> alias = values;
                    _ = alias.Count;
                    alias[0] = 7;
                    System.Collections.Generic.IList<int> mixed = values;
                    mixed = list;
                    _ = mixed.Count;
                    _ = collection.Count;
                    _ = list[0];
                    list[0] = 7;
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var count = compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")!.GetMembers("Count").OfType<IPropertySymbol>().Single();
        var item = compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1")!.GetMembers().OfType<IPropertySymbol>().Single(property => property is null ? throw new global::System.ArgumentNullException(nameof(property)) : (property.IsIndexer));
        var inventory = ImmutableDictionaryCapture.Create(ImmutableArray.Create(count, item), property => property is null ? throw new global::System.ArgumentNullException(nameof(property)) : (property.ContainingAssembly.Name + "\t" + property.GetDocumentationCommentId()), _ => "partially-modeled", StringComparer.Ordinal);
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        Scan(compilation, compilation.SyntaxTrees.Single().GetRoot(), "test", "probe", inventory, siteBuilder, gapBuilder);
        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        Assert.That(gaps, Is.Empty);
        Assert.That(sites, Has.Length.EqualTo(10));
        Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Status == "partially-modeled")), Is.EqualTo(5));
        Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Status == "partially-modeled" && site.Access == "write")), Is.EqualTo(2));
        Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Status == "unmodeled-dispatch")), Is.EqualTo(4));
        Assert.That(sites.Count(site => site is null ? throw new global::System.ArgumentNullException(nameof(site)) : (site.Status == "unmodeled-access")), Is.EqualTo(1));
    }

    [Test]
    public void UsageScanCreditsContextualArrayLengthWithoutCreditingSystemArrayOrMultipleDimensions()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class UsageProbe {
                public static void Target(int[] values, int[,] matrix, System.Array unknown) {
                    _ = values.Length;
                    _ = matrix.Length;
                    _ = unknown.Length;
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var property = compilation.GetSpecialType(SpecialType.System_Array).GetMembers("Length").OfType<IPropertySymbol>().Single();
        var inventoryBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        inventoryBuilder[property.ContainingAssembly.Name + "\t" + property.GetDocumentationCommentId()] = "unmodeled";
        var inventory = inventoryBuilder.ToImmutable();
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        Scan(compilation, compilation.SyntaxTrees.Single().GetRoot(), "test", "probe", inventory, siteBuilder, gapBuilder);
        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        Assert.That(gaps, Is.Empty);
        Assert.That(sites.Count(site => site is null ? throw new System.ArgumentNullException(nameof(site)) : site.Status == "partially-modeled"), Is.EqualTo(1));
        Assert.That(sites.Count(site => site is null ? throw new System.ArgumentNullException(nameof(site)) : site.Status == "unmodeled"), Is.EqualTo(2));
    }
    [Test]
    public void UsageScanCreditsFreshDictionaryStateWithoutCreditingIncomingOrCustomComparers()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class DictionaryUsageProbe {
                public static void Target(System.Collections.Generic.Dictionary<int, int> incoming,
                    System.Collections.Generic.IEqualityComparer<int> comparer) {
                    var fresh = new System.Collections.Generic.Dictionary<int, int>();
                    fresh.Add(1, 23); _ = fresh[1]; fresh[1] = 29;
                    incoming.Add(1, 23); _ = incoming[1];
                    var custom = new System.Collections.Generic.Dictionary<int, int>(comparer);
                    custom.Add(1, 23); _ = custom[1];
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var tree = compilation.SyntaxTrees.Single();
        var known = KnownIntrinsicSites(compilation.GetSemanticModel(tree), tree.GetRoot());
        var calls = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToImmutableArray();
        Assert.That(calls, Has.Length.EqualTo(3));
        Assert.That(calls.Select(call => call is null ? throw new ArgumentNullException(nameof(call)) : known.Contains(call.SpanStart)),
            Is.EqualTo(FreshDictionaryCalls));
        var indexers = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToImmutableArray();
        Assert.That(indexers, Has.Length.EqualTo(4));
        Assert.That(indexers.Select(indexer => indexer is null ? throw new ArgumentNullException(nameof(indexer)) : known.Contains(indexer.SpanStart)),
            Is.EqualTo(FreshDictionaryIndexers));
    }

    [Test]
    public void UsageScanCreditsCapacityOriginWithoutCreditingCustomComparerConstruction()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class CapacityUsageProbe {
                public static void Target(System.Collections.Generic.IEqualityComparer<int> comparer) {
                    var fresh = new System.Collections.Generic.Dictionary<int, int>(3);
                    fresh.Add(1, 23); _ = fresh[1]; _ = fresh.Count;
                    var custom = new System.Collections.Generic.Dictionary<int, int>(3, comparer);
                    custom.Add(1, 23); _ = custom[1];
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var dictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")!;
        var inventory = ImmutableDictionaryCapture.Create(dictionary.GetMembers().Where(member => member is null ? throw new ArgumentNullException(nameof(member)) :
            CSharpOperationSemantics.IsDictionaryStateDeclaration(member) || member is IMethodSymbol { MethodKind: MethodKind.Constructor }),
                member => member is null ? throw new ArgumentNullException(nameof(member)) : member.ContainingAssembly.Name + "\t" + member.GetDocumentationCommentId(),
                member => member is null ? throw new ArgumentNullException(nameof(member)) : CSharpOperationSemantics.IsDictionaryStateDeclaration(member) ? "partially-modeled" : "unmodeled", StringComparer.Ordinal);
        var tree = compilation.SyntaxTrees.Single();
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        Scan(compilation, tree.GetRoot(), "test", "capacity", inventory, siteBuilder, gapBuilder);
        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        Assert.That(gaps, Is.Empty);
        Assert.That(sites.Count(site => site is null ? throw new ArgumentNullException(nameof(site)) : site.Status == "partially-modeled"), Is.EqualTo(4));
        Assert.That(sites.Count(site => site is null ? throw new ArgumentNullException(nameof(site)) : site.Status == "unmodeled-dispatch"), Is.EqualTo(2));
        Assert.That(sites.Count(site => site is null ? throw new ArgumentNullException(nameof(site)) : site.Status == "unmodeled"), Is.EqualTo(1));
    }
    [Test]
    public void UsageScanCreditsOnlyProvenPrimitiveDefaultHashSetMembership()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class HashSetUsageProbe {
                public static void Target(System.Collections.Generic.HashSet<int> incoming,
                    System.Collections.Generic.IEqualityComparer<int> comparer) {
                    var fresh = new System.Collections.Generic.HashSet<int>();
                    fresh.Add(1); _ = fresh.Contains(1); fresh.Remove(1);
                    _ = incoming.Contains(1);
                    var custom = new System.Collections.Generic.HashSet<int>(comparer);
                    _ = custom.Contains(1);
                    var floating = new System.Collections.Generic.HashSet<double>();
                    _ = floating.Contains(double.NaN);
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var tree = compilation.SyntaxTrees.Single();
        var known = KnownIntrinsicSites(compilation.GetSemanticModel(tree), tree.GetRoot());
        var calls = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.That(calls, Has.Length.EqualTo(6));
        Assert.That(calls.Select(call => call is null ? throw new ArgumentNullException(nameof(call)) : known.Contains(call.SpanStart)),
            Is.EqualTo(FreshHashSetCalls));
    }

    [Test]
    public void UsageScanCreditsOwnedListAliasesAndInitializersWithoutCreditingUnprovenDispatch()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class ListUsageProbe {
                public static void Target(bool choose, System.Collections.Generic.IList<int> incoming) {
                    var fresh = new System.Collections.Generic.List<int>() { 17 };
                    System.Collections.Generic.IList<int> alias = fresh;
                    var mixed = choose ? alias : incoming;
                    fresh.Add(23); incoming.Add(31);
                    _ = fresh[0]; alias[0] = 29; _ = incoming[0]; _ = mixed[0]; alias[0]++;
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var tree = compilation.SyntaxTrees.Single();
        var known = KnownIntrinsicSites(compilation.GetSemanticModel(tree), tree.GetRoot());
        var creation = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        Assert.That(known.Contains(creation.SpanStart), Is.True);
        var calls = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToImmutableArray();
        Assert.That(calls.Select(call => call is null ? throw new ArgumentNullException(nameof(call)) : known.Contains(call.SpanStart)),
            Is.EqualTo(FreshListCalls));
        var indexers = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToImmutableArray();
        Assert.That(indexers.Select(indexer => indexer is null ? throw new ArgumentNullException(nameof(indexer)) : known.Contains(indexer.SpanStart)),
            Is.EqualTo(FreshListIndexers));
    }

    [Test]
    public void UsageScanCreditsPublishedDictionaryOriginsWithoutCreditingUnprovenReceivers()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class DictionaryOriginUsageProbe {
                private static readonly System.Collections.Generic.Dictionary<int, decimal> Field = new();
                public static void Target<T>(System.Collections.Generic.Dictionary<int, decimal> incoming,
                    System.Collections.Generic.IEqualityComparer<int> comparer, T key) where T : notnull {
                    var fresh = new System.Collections.Generic.Dictionary<int, decimal>();
                    Publish(fresh);
                    fresh.Add(1, 23m); _ = fresh.ContainsKey(1); _ = fresh[1]; fresh[1] = 29m; _ = fresh.Count;
                    incoming.Add(1, 23m); _ = incoming[1];
                    var custom = new System.Collections.Generic.Dictionary<int, decimal>(comparer);
                    custom.Add(1, 23m); _ = custom[1];
                    var capacity = new System.Collections.Generic.Dictionary<int, decimal>(4);
                    capacity.Add(1, 23m); _ = capacity[1];
                    var genericKeys = new System.Collections.Generic.Dictionary<T, decimal>();
                    genericKeys.Add(key, 23m); _ = genericKeys[key];
                    Field.Add(1, 23m); _ = Field[1];
                    fresh[1]++;
                }
                private static void Publish(System.Collections.Generic.Dictionary<int, decimal> data) { }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var target = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var known = KnownIntrinsicSites(model, target);
        var freshMembers = target.DescendantNodes().Where(node => node is null ? throw new ArgumentNullException(nameof(node)) :
            node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "fresh" } } } ||
            node is ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "fresh" }, Parent: not PostfixUnaryExpressionSyntax } ||
            node is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "fresh" }, Name.Identifier.ValueText: "Count" }).ToImmutableArray();
        Assert.That(freshMembers, Has.Length.EqualTo(5));
        Assert.That(freshMembers.All(member => member is null ? throw new ArgumentNullException(nameof(member)) : known.Contains(member.SpanStart)), Is.True);
        var capacityMembers = target.DescendantNodes().Where(node => node is null ? throw new ArgumentNullException(nameof(node)) :
            node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "capacity" } } } ||
            node is ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "capacity" } }).ToImmutableArray();
        Assert.That(capacityMembers, Has.Length.EqualTo(2));
        Assert.That(capacityMembers.All(member => member is null ? throw new ArgumentNullException(nameof(member)) : known.Contains(member.SpanStart)), Is.True);
        var unprovenIndexers = target.DescendantNodes().OfType<ElementAccessExpressionSyntax>()
            .Except(freshMembers.OfType<ElementAccessExpressionSyntax>()).Except(capacityMembers.OfType<ElementAccessExpressionSyntax>()).ToImmutableArray();
        Assert.That(unprovenIndexers, Has.Length.EqualTo(5));
        Assert.That(unprovenIndexers.Any(member => member is null ? throw new ArgumentNullException(nameof(member)) : known.Contains(member.SpanStart)), Is.False);
        var dictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")!;
        var inventory = ImmutableDictionaryCapture.Create(dictionary.GetMembers().Where(member => member is null ? throw new ArgumentNullException(nameof(member)) :
            CSharpOperationSemantics.IsDictionaryStateDeclaration(member) || member is IMethodSymbol { MethodKind: MethodKind.Constructor }), member => member is null ? throw new ArgumentNullException(nameof(member)) : member.ContainingAssembly.Name + "\t" + member.GetDocumentationCommentId(), _ => "partially-modeled", StringComparer.Ordinal);
        var siteBuilder = ImmutableArray.CreateBuilder<Site>();
        var gapBuilder = ImmutableArray.CreateBuilder<string>();
        Scan(compilation, target, "test", "probe", inventory, siteBuilder, gapBuilder);
        var sites = siteBuilder.ToImmutable();
        var gaps = gapBuilder.ToImmutable();
        Assert.That(gaps, Is.Empty);
        Assert.That(sites.Count(site => site is null ? throw new ArgumentNullException(nameof(site)) :
            site.Status == "partially-modeled" && !site.Api.Contains("#ctor", StringComparison.Ordinal)), Is.EqualTo(7));
        Assert.That(sites.Count(site => site is null ? throw new ArgumentNullException(nameof(site)) : site.Status == "unmodeled-dispatch"), Is.EqualTo(9));
    }

    [Test]
    public void UsageScanCreditsConcreteListParametersWithinTheirValueDomain()
    {
        var compilation = AnalyzerGateHost.CreateValidatedCompilation("""
            public static class ConcreteListProbe {
                public static void Target(System.Collections.Generic.List<int> data,
                    System.Collections.Generic.IList<int> unknown, System.Collections.Generic.List<decimal> opaque, decimal item,
                    System.Collections.Generic.List<int[][]> unsupported) {
                    data.Add(23); unknown.Add(23); opaque.Add(item); unsupported.Add(null);
                    _ = data[0]; _ = unknown[0]; _ = opaque[0]; data[0]++; _ = unsupported[0];
                }
            }
            """, TestContext.CurrentContext.CancellationToken);
        var tree = compilation.SyntaxTrees.Single();
        var known = KnownIntrinsicSites(compilation.GetSemanticModel(tree), tree.GetRoot());
        var calls = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToImmutableArray();
        Assert.That(known.Contains(calls[0].SpanStart), Is.True);
        Assert.That(known.Contains(calls[1].SpanStart), Is.False);
        Assert.That(known.Contains(calls[2].SpanStart), Is.True);
        Assert.That(known.Contains(calls[3].SpanStart), Is.False);
        var indexers = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToImmutableArray();
        Assert.That(known.Contains(indexers[0].SpanStart), Is.True);
        Assert.That(known.Contains(indexers[1].SpanStart), Is.False);
        Assert.That(known.Contains(indexers[2].SpanStart), Is.True);
        Assert.That(known.Contains(indexers[3].SpanStart), Is.False);
        Assert.That(known.Contains(indexers[4].SpanStart), Is.False);
    }

    private static ImmutableHashSet<int> KnownIntrinsicSites(SemanticModel model, SyntaxNode region)
    {
        global::System.ArgumentNullException.ThrowIfNull(model);
        global::System.ArgumentNullException.ThrowIfNull(region);
        var known = ImmutableHashSet.CreateBuilder<int>();
        foreach (var syntax in region.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>())
        {
            var method = model.GetDeclaredSymbol(syntax)!;
            var graph = TotalSourceCallSession.CreateGraph(syntax, method, model, TestContext.CurrentContext.CancellationToken);
            if (graph == null)
            {
                continue;
            }

            var context = new TotalLoweringContext(new SharpProof.Ir.IrFactory(SharpProof.Ir.IrExecutionSemantics.Total), method);
            context.TrackArrayReceivers(graph);
            context.TrackDefaultComparers(graph);
            context.TrackDefaultDictionaries(graph);
            context.TrackDefaultHashSets(graph);
            context.TrackFreshLists(graph);
            foreach (var creation in graph.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Operations.Concat(block.BranchValue == null ? [] : ImmutableArray.Create(block.BranchValue))).SelectMany(operation => operation is null ? throw new ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf()).OfType<IObjectCreationOperation>())
            {
                if (CSharpOperationSemantics.IsEmptyListCreation(creation))
                { known.Add(creation.Syntax.SpanStart); }
            }
            foreach (var invocation in graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : block.Operations.Concat(block.BranchValue == null ? [] : ImmutableArray.Create(block.BranchValue))).SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf()).OfType<IInvocationOperation>())
            {
                if (context.IsDefaultIntegerComparison(invocation) ||
                    invocation.Instance != null && CSharpOperationSemantics.IsHashSetStateMember(invocation.TargetMethod) &&
                    context.IsDefaultHashSetOrigin(invocation.Instance) ||
                    invocation.Instance != null && DictionaryMember(invocation.TargetMethod) &&
                    context.IsDefaultDictionaryOrigin(invocation.Instance) ||
                    invocation.Instance != null && CSharpOperationSemantics.IsListStateMember(invocation.TargetMethod) &&
                    (CSharpOperationSemantics.IsConcreteListStateMember(invocation.TargetMethod) || context.IsExactList(invocation.Instance)))
                { known.Add(invocation.Syntax.SpanStart); }
            }
            foreach (var property in graph.Blocks.SelectMany(block => block is null ? throw new global::System.ArgumentNullException(nameof(block)) : (block.Operations.Concat(block.BranchValue == null ? [] : ImmutableArray.Create(block.BranchValue)))).SelectMany(operation => operation is null ? throw new global::System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).OfType<IPropertyReferenceOperation>())
            {
                if (context.ArrayCollectionReceiver(property) != null ||
                    property.Instance != null && property.Property.GetMethod is { } getter && DictionaryMember(getter) &&
                    context.IsDefaultDictionaryOrigin(property.Instance) &&
                    property.Parent is not (ICompoundAssignmentOperation or IIncrementOrDecrementOperation) ||
                    property.Instance != null && property.Property.GetMethod is { } listGetter && CSharpOperationSemantics.IsListStateMember(listGetter) &&
                    (CSharpOperationSemantics.IsConcreteListStateMember(listGetter) || context.IsExactList(property.Instance)) &&
                    property.Parent is not (ICompoundAssignmentOperation or IIncrementOrDecrementOperation))
                {
                    known.Add(property.Syntax.SpanStart);
                }
            }

            bool DictionaryMember(IMethodSymbol member)
            {
                global::System.ArgumentNullException.ThrowIfNull(member);
                return CSharpOperationSemantics.IsDefaultDictionaryStateMember(member,
                    context.KnownValueType(member.ContainingType.TypeArguments.ElementAtOrDefault(0)),
                    context.KnownValueType(member.ContainingType.TypeArguments.ElementAtOrDefault(1)));
            }
        }

        return known.ToImmutable();
    }

    private static void RequireCompilation(CSharpCompilation compilation)
    {
        global::System.ArgumentNullException.ThrowIfNull(compilation);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).Select(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())), Is.Empty, "Usage ranking requires fully bound inputs.");
    }

    private static string Hash(string value)
    {
        global::System.ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}

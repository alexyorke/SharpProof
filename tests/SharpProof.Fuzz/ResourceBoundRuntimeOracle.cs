using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Compiler.Semantics;
using SharpProof.Ir;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Fuzz;

internal sealed record ResourceBoundFuzzCoverage(int Cases, int ProvenCases, int UnknownCases, int ViolatingCases, int TightCases,
    int NativeBounds, int Executions, int ExceptionalExecutions, int SkippedInputs, int ShapeMask, int ProvenShapeMask)
{
    internal const int AllShapes = (1 << ResourceBoundCase.ShapeCount) - 1;
    internal const int ProvableShapes = 0b1111;
    [JsonIgnore]
    public bool HasValidCounts => Cases > 0 && ProvenCases >= 0 && UnknownCases >= 0 && (long)ProvenCases + UnknownCases == Cases &&
        ViolatingCases >= 0 && ViolatingCases <= ProvenCases &&
        TightCases >= 0 && TightCases <= ProvenCases && NativeBounds >= 0 && NativeBounds <= ProvenCases && Executions >= 0 &&
        ExceptionalExecutions >= 0 && ExceptionalExecutions <= Executions && SkippedInputs >= 0 &&
        ShapeMask is > 0 and <= AllShapes && ProvenShapeMask >= 0 && (ProvenShapeMask & ~ShapeMask) == 0;
    // The provable shapes must keep proving, or a regression to Unknown would
    // leave the oracle comparing nothing and still passing.
    [JsonIgnore]
    public bool HasExpandedCategories => ShapeMask == AllShapes && (ProvenShapeMask & ProvableShapes) == ProvableShapes &&
        TightCases > 0 && NativeBounds > 0 && Executions > 0 && ExceptionalExecutions > 0;
}

internal sealed record ResourceBoundFuzzSummary(int SchemaVersion, int Cases, int Seed, int MaximumParallelism,
    ResourceBoundFuzzCoverage Coverage, bool CoverageSatisfied, ImmutableArray<FuzzFailure> Failures)
{
    internal const int CurrentSchema = 1;
    public ResourceBoundFuzzCoverage Coverage { get; init; } = Coverage ?? throw new System.ArgumentNullException(nameof(Coverage));
    public ImmutableArray<FuzzFailure> Failures { get; init; } = Failures.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Failures)) : Failures;
    public bool Passed => SchemaVersion == CurrentSchema && Cases > 0 && Coverage.Cases == Cases && Coverage.HasValidCounts &&
        MaximumParallelism is >= 1 and <= FuzzOptions.MaximumAllowedParallelism && CoverageSatisfied &&
        Coverage.ViolatingCases == 0 && Failures.IsEmpty;
}

// One proved cost, from one proof route, for one generated method.
internal sealed record ResourceBoundClaim(string Route, ResourceExpression Entries, ResourceExpression Arrays, ResourceExpression Elements)
{
    public string Route { get; init; } = Route ?? throw new System.ArgumentNullException(nameof(Route));
    public ResourceExpression Entries { get; init; } = Entries ?? throw new System.ArgumentNullException(nameof(Entries));
    public ResourceExpression Arrays { get; init; } = Arrays ?? throw new System.ArgumentNullException(nameof(Arrays));
    public ResourceExpression Elements { get; init; } = Elements ?? throw new System.ArgumentNullException(nameof(Elements));
}

internal sealed record ResourceBoundInput(int N, int M, long W, int[]? Values, string? Text)
{
    internal BigInteger Size(ResourceInput input)
    {
        input = RepositoryParameterGuard.NotNull(input, nameof(input));
        return input.Ordinal switch
        {
            0 => N,
            1 => M,
            2 => W,
            3 => Values?.Length ?? 0,
            4 => Text?.Length ?? 0,
            _ => throw new InvalidOperationException("Generated targets have five parameters.")
        };
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"n={N}, m={M}, w={W}, values.Length={(Values is null ? "null" : Values.Length)}, text.Length={(Text is null ? "null" : Text.Length)}");
    }
}

internal delegate void ResourceBoundTarget(int n, int m, long w, int[]? values, string? text);

// Compares proved resource bounds with compiled-CLR execution. The proofs read the
// generated source. The counts come from that same syntax tree, rewritten to count
// each loop body entry and array, so they never depend on the analysis under test.
// The unrewritten build is measured too, in bytes, because that sees allocations
// the rewriter does not mark, such as strings. Each batch owns its assemblies and
// their static counters, and allocated bytes are per thread, so batches can overlap.
internal static class ResourceBoundRuntimeOracle
{
    private const int BatchSize = 128;
    private const string GeneratedType = "ResourceBoundGenerated";
    // Fuel for a method that a false proof calls terminating, and a skip for
    // proved bounds too large to execute cheaply. The grid keeps most far below.
    private const long MaximumEntries = 2_000_000;
    // A CLR array has a 24-byte header and rounds up to 8 bytes.
    private const int ArrayOverheadBytes = 24 + 7;
    // Only a repeated excess counts. A first call can pay for loading and tiering.
    private const int ByteMeasurements = 3;
    private const string ProbeSource = """
        public sealed class ResourceProbeStop : System.Exception { }
        public static class ResourceProbe
        {
            public static long Entries;
            public static long Arrays;
            public static long Elements;
            public static long EntryLimit;
            public static void Enter()
            {
                if (++Entries > EntryLimit)
                { throw new ResourceProbeStop(); }
            }
            public static T[] Array<T>(T[] array)
            {
                Arrays++;
                Elements += array.Length;
                return array;
            }
        }
        """;

    internal static async Task<ResourceBoundFuzzSummary> RunAsync(int cases, int seed, int maximumParallelism, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cases, 1);
        var batches = await FuzzRunner.RunIndexedAsync<ResourceBoundBatch>((cases + BatchSize - 1) / BatchSize, maximumParallelism,
            (index, token) =>
            {
                RepositoryParameterGuard.RequireInitialized(index < 0, nameof(index), "Value must be initialized.");
                var offset = index * BatchSize;
                var generated = Enumerable.Range(offset, Math.Min(BatchSize, cases - offset))
                    .Select(ordinal => ResourceBoundProgramGenerator.Generate(seed, ordinal)).ToImmutableArray();
                return ValueTask.FromResult(Check(generated, seed, static claim => claim, token));
            }, cancellationToken).ConfigureAwait(false);
        var total = new ResourceBoundBatch();
        foreach (var batch in batches)
        { total.Add(batch); }
        var coverage = new ResourceBoundFuzzCoverage(cases, total.Proven, cases - total.Proven, total.Violating, total.Tight, total.NativeBounds,
            total.Executions, total.Exceptional, total.Skipped, total.ShapeMask, total.ProvenShapeMask);
        return new(ResourceBoundFuzzSummary.CurrentSchema, cases, seed, maximumParallelism, coverage,
            cases < FuzzOptions.DefaultCases || coverage.HasExpandedCategories, total.Failures.ToImmutable());
    }

    // A test can replace a claim to confirm that a bound below the real cost fails.
    internal static ResourceBoundBatch Check(ImmutableArray<ResourceBoundCase> cases, int seed,
        Func<ResourceBoundClaim, ResourceBoundClaim> claims, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfInvalid(cases.IsDefault, nameof(cases));
        claims = RepositoryParameterGuard.NotNull(claims, nameof(claims));
        var result = new ResourceBoundBatch();
        var source = GeneratedSource(cases.Select(static item => item.Member));
        var tree = Parse(source, "resource-bounds.cs", cancellationToken);
        var compilation = Compile("ResourceBoundPlain", [tree], TestMetadataReferences.SortedDistinctPlatform);
        var proofs = Prove(cases, compilation, tree, claims, cancellationToken);
        var proven = cases.Where(item => proofs.ContainsKey(item.Name)).ToImmutableArray();
        foreach (var item in cases)
        { result.ShapeMask |= 1 << (int)item.Shape; }
        if (proven.IsEmpty)
        { return result; }
        var native = NativeClaims(proven, claims, cancellationToken);
        var probed = tree.WithRootAndOptions(new ProbeRewriter().Visit(tree.GetRoot(cancellationToken)), tree.Options);
        var context = new AssemblyLoadContext("ResourceBoundFuzz", isCollectible: true);
        try
        {
            var plainType = Load(context, compilation, cancellationToken).GetType(GeneratedType)
                ?? throw new InvalidOperationException("Generated resource type is missing from the plain assembly.");
            var probedAssembly = Load(context, Compile("ResourceBoundProbed", [probed, Parse(ProbeSource, "resource-probe.cs", cancellationToken)],
                TestMetadataReferences.SortedDistinctPlatform), cancellationToken);
            var probedType = probedAssembly.GetType(GeneratedType)
                ?? throw new InvalidOperationException("Generated resource type is missing from the probed assembly.");
            var probe = new ResourceProbeHandle(probedAssembly);
            foreach (var item in proven)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Proven++;
                result.ProvenShapeMask |= 1 << (int)item.Shape;
                var routes = native.TryGetValue(item.Name, out var nativeClaim)
                    ? ImmutableArray.Create(proofs[item.Name], nativeClaim) : [proofs[item.Name]];
                result.NativeBounds += routes.Length - 1;
                Execute(item, routes, Target(plainType, item), Target(probedType, item), probe, seed, result, cancellationToken);
            }
        }
        finally
        { context.Unload(); }
        return result;
    }

    private static ImmutableDictionary<string, ResourceBoundClaim> Prove(ImmutableArray<ResourceBoundCase> cases, CSharpCompilation compilation,
        SyntaxTree tree, Func<ResourceBoundClaim, ResourceBoundClaim> claims, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfInvalid(cases.IsDefault, nameof(cases));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        claims = RepositoryParameterGuard.NotNull(claims, nameof(claims));
        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
        var targets = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Identifier.ValueText.StartsWith("Target", StringComparison.Ordinal))
            .ToImmutableArray();
        if (targets.Length != cases.Length)
        { throw new InvalidOperationException("Generated method count does not match the resource case universe."); }
        var proofs = ImmutableDictionary.CreateBuilder<string, ResourceBoundClaim>(StringComparer.Ordinal);
        foreach (var method in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = model.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol
                ?? throw new InvalidOperationException("Generated method has no declared symbol.");
            if (ResourceBoundsAnalysis.Analyze(symbol, model.GetOperation(method.Body!, cancellationToken), cancellationToken).Bounds is { } bounds)
            {
                proofs.Add(method.Identifier.ValueText, claims(new("sp0060", bounds.LoopIterations.Expression, bounds.Arrays.Expression, bounds.Elements.Expression)));
            }
        }
        return proofs.ToImmutable();
    }

    // The verified cost is the upper bound that the worker's Proven path compares
    // with a declared contract. The contract itself cannot state a bound like n * m,
    // so any declaration serves here. It only makes the collector carry the program.
    private static ImmutableDictionary<string, ResourceBoundClaim> NativeClaims(ImmutableArray<ResourceBoundCase> proven,
        Func<ResourceBoundClaim, ResourceBoundClaim> claims, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfInvalid(proven.IsDefault, nameof(proven));
        claims = RepositoryParameterGuard.NotNull(claims, nameof(claims));
        var source = "using SharpProof.Attributes; " + GeneratedSource(proven.Select(static item => "[ResourceUpperBound(ResourceMetric.LoopBodyEntries, 0)] " + item.Member));
        var compilation = Compile("ResourceBoundNative", [Parse(source, "/resource-bounds/Subject.cs", cancellationToken)],
            TestMetadataReferences.WithAdditionalPaths([typeof(SharpProof.Attributes.Contract).Assembly.Location], sort: true));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/resource-bounds", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellationToken).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, cancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken), out var preparations, cancellationToken);
        var results = ImmutableDictionary.CreateBuilder<string, ResourceBoundClaim>(StringComparer.Ordinal);
        foreach (var item in proven)
        {
            var preparation = preparations.FirstOrDefault(candidate => candidate.Entry.CallableId.Contains(GeneratedType + "." + item.Name + "(", StringComparison.Ordinal));
            if (preparation is not { ResourceProgram: { } program, Total: { IsBodyAbstraction: false } owner })
            { continue; }
            var check = NativeResourceProgramVerifier.Verify(program, preparation.Entry.CallableId, owner, new NativeResourceMetadataBinding(preparation).Bind, cancellationToken);
            if (check.Cost is { } cost)
            { results.Add(item.Name, claims(new("native", cost.LoopEntries, cost.Arrays, cost.Elements))); }
        }
        return results.ToImmutable();
    }

    private static void Execute(ResourceBoundCase item, ImmutableArray<ResourceBoundClaim> routes, ResourceBoundTarget plain, ResourceBoundTarget probed,
        ResourceProbeHandle probe, int seed, ResourceBoundBatch result, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfInvalid(routes.IsDefault, nameof(routes));
        item = RepositoryParameterGuard.NotNull(item, nameof(item));
        plain = RepositoryParameterGuard.NotNull(plain, nameof(plain));
        probed = RepositoryParameterGuard.NotNull(probed, nameof(probed));
        probe = RepositoryParameterGuard.NotNull(probe, nameof(probe));
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        var failed = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var tight = false;
        foreach (var input in Inputs(seed, item.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryLimit = routes.Aggregate(BigInteger.Zero, (limit, route) => route is null ? throw new System.ArgumentNullException(nameof(route)) :
                BigInteger.Max(limit, route.Entries.Evaluate(input.Size, cancellationToken)));
            if (entryLimit > MaximumEntries)
            {
                result.Skipped++;
                continue;
            }
            var completed = Count(probed, input, probe, (long)entryLimit);
            var counts = probe.Counts;
            result.Executions++;
            if (!completed)
            { result.Exceptional++; }
            foreach (var route in routes)
            {
                var entries = route.Entries.Evaluate(input.Size, cancellationToken);
                var arrays = route.Arrays.Evaluate(input.Size, cancellationToken);
                var elements = route.Elements.Evaluate(input.Size, cancellationToken);
                tight |= counts.Entries > 0 && counts.Entries == entries || counts.Arrays > 0 && counts.Arrays == arrays;
                var bytes = completed ? AllocatedBytes(plain, input, ArrayOverheadBytes * arrays + item.ElementWidth * elements) : null;
                var excess =
                    counts.Entries > entries ? Excess("LoopBodyEntries", counts.Entries, route.Entries, entries, input) :
                    counts.Arrays > arrays ? Excess("ArrayAllocations", counts.Arrays, route.Arrays, arrays, input) :
                    counts.Elements > elements ? Excess("AllocatedElements", counts.Elements, route.Elements, elements, input) :
                    bytes is { } measured ? $"AllocatedBytes {measured} exceeds the {route.Arrays.ToDisplayString()} arrays and {route.Elements.ToDisplayString()} elements proved at {input}" : null;
                if (excess is not null && failed.Add(route.Route))
                { result.Fail(item, seed, route.Route, input, excess); }
            }
            if (failed.Count == routes.Length)
            { break; }
        }
        result.Tight += tight ? 1 : 0;
        result.Violating += failed.Count > 0 ? 1 : 0;
    }

    private static string Excess(string metric, long observed, ResourceExpression bound, BigInteger evaluated, ResourceBoundInput input)
    {
        metric = RepositoryParameterGuard.NotNull(metric, nameof(metric));
        bound = RepositoryParameterGuard.NotNull(bound, nameof(bound));
        input = RepositoryParameterGuard.NotNull(input, nameof(input));
        return string.Create(CultureInfo.InvariantCulture, $"{metric} {observed} exceeds the proved {bound.ToDisplayString()} = {evaluated} at {input}");
    }

    // True when the body completed. A method the proof calls cheaper than it is
    // stops at the limit, so a false proof of termination cannot hang the run.
    private static bool Count(ResourceBoundTarget target, ResourceBoundInput input, ResourceProbeHandle probe, long entryLimit)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        input = RepositoryParameterGuard.NotNull(input, nameof(input));
        probe = RepositoryParameterGuard.NotNull(probe, nameof(probe));
        probe.Reset(entryLimit);
        try
        {
            target(input.N, input.M, input.W, input.Values, input.Text);
            return true;
        }
        catch (Exception exception) when (probe.IsStop(exception) || exception is OverflowException or NullReferenceException)
        { return false; }
    }

    // Null when the build stays within the ceiling. The rewriter's counts cannot
    // see these bytes in the generated grammar: only arrays are meant to allocate.
    private static long? AllocatedBytes(ResourceBoundTarget target, ResourceBoundInput input, BigInteger ceiling)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        input = RepositoryParameterGuard.NotNull(input, nameof(input));
        var least = long.MaxValue;
        for (var attempt = 0; attempt < ByteMeasurements; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            target(input.N, input.M, input.W, input.Values, input.Text);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            if (least <= ceiling)
            { return null; }
        }
        return least;
    }

    private static ImmutableArray<ResourceBoundInput> Inputs(int seed, int ordinal)
    {
        var random = new Random(unchecked(seed + ordinal * 1009));
        return
        [
            new(-1, 2, -3, [], ""),
            new(0, 0, 0, null, null),
            new(1, 1, 1, new int[1], "a"),
            new(2, 3, 2, new int[2], "ab"),
            new(3, 4, 5, new int[1], "abc"),
            new(5, 4, 3, new int[4], ""),
            new(7, 7, 7, new int[3], "abcd"),
            new(10, 10, 10, new int[6], "abcdef"),
            new(random.Next(13), random.Next(13), random.Next(13), new int[random.Next(9)], new string('a', random.Next(9))),
            new(random.Next(13), random.Next(13), random.Next(13), new int[random.Next(9)], new string('a', random.Next(9)))
        ];
    }

    private static ResourceBoundTarget Target(Type type, ResourceBoundCase item)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        item = RepositoryParameterGuard.NotNull(item, nameof(item));
        return (type.GetMethod(item.Name) ?? throw new InvalidOperationException("Generated method is missing from the emitted assembly."))
            .CreateDelegate<ResourceBoundTarget>();
    }

    private static string GeneratedSource(IEnumerable<string> members)
    {
        members = RepositoryParameterGuard.NotNull(members, nameof(members));
        return "public static class " + GeneratedType + " { " + string.Join("\n", members) + " }";
    }

    private static SyntaxTree Parse(string source, string path, CancellationToken cancellationToken)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        return CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path, cancellationToken: cancellationToken);
    }

    private static CSharpCompilation Compile(string name, ImmutableArray<SyntaxTree> trees, ImmutableArray<MetadataReference> references)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        RepositoryParameterGuard.ThrowIfInvalid(trees.IsDefault, nameof(trees));
        RepositoryParameterGuard.ThrowIfInvalid(references.IsDefault, nameof(references));
        return CSharpCompilation.Create(name, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true));
    }

    private static Assembly Load(AssemblyLoadContext context, CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var (image, emit) = GeneratedCompilationEmitter.Emit(compilation, cancellationToken);
        using var imageScope = image;
        if (!emit.Success)
        { throw new InvalidOperationException(string.Join("\n", emit.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))); }
        image.Position = 0;
        return context.LoadFromStream(image);
    }

    // The tree the proofs read, with a count at each loop body entry and array.
    private sealed class ProbeRewriter : CSharpSyntaxRewriter
    {
        private static readonly StatementSyntax Enter = SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(Member("Enter")));

        private static MemberAccessExpressionSyntax Member(string name)
        {
            name = RepositoryParameterGuard.NotNull(name, nameof(name));
            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("ResourceProbe"), SyntaxFactory.IdentifierName(name));
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            node = RepositoryParameterGuard.NotNull(node, nameof(node));
            var visited = (ForStatementSyntax)base.VisitForStatement(node)!;
            return visited.WithStatement(SyntaxFactory.Block(Enter, visited.Statement));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            node = RepositoryParameterGuard.NotNull(node, nameof(node));
            var visited = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return visited.WithStatement(SyntaxFactory.Block(Enter, visited.Statement));
        }

        public override SyntaxNode? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            node = RepositoryParameterGuard.NotNull(node, nameof(node));
            var visited = (ExpressionSyntax)base.VisitArrayCreationExpression(node)!;
            return SyntaxFactory.InvocationExpression(Member("Array"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(visited))));
        }
    }

    private readonly record struct ResourceBoundCounts(long Entries, long Arrays, long Elements);

    private sealed class ResourceProbeHandle
    {
        private readonly FieldInfo entries;
        private readonly FieldInfo arrays;
        private readonly FieldInfo elements;
        private readonly FieldInfo entryLimit;
        private readonly Type stop;

        internal ResourceProbeHandle(Assembly assembly)
        {
            assembly = RepositoryParameterGuard.NotNull(assembly, nameof(assembly));
            var probe = assembly.GetType("ResourceProbe", throwOnError: true)!;
            entries = probe.GetField("Entries")!;
            arrays = probe.GetField("Arrays")!;
            elements = probe.GetField("Elements")!;
            entryLimit = probe.GetField("EntryLimit")!;
            stop = assembly.GetType("ResourceProbeStop", throwOnError: true)!;
        }

        internal ResourceBoundCounts Counts => new((long)entries.GetValue(null)!, (long)arrays.GetValue(null)!, (long)elements.GetValue(null)!);

        internal void Reset(long limit)
        {
            entries.SetValue(null, 0L);
            arrays.SetValue(null, 0L);
            elements.SetValue(null, 0L);
            entryLimit.SetValue(null, limit);
        }

        internal bool IsStop(Exception exception)
        {
            exception = RepositoryParameterGuard.NotNull(exception, nameof(exception));
            return exception.GetType() == stop;
        }
    }
}

internal sealed class ResourceBoundBatch
{
    internal int Proven { get; set; }
    internal int Violating { get; set; }
    internal int Tight { get; set; }
    internal int NativeBounds { get; set; }
    internal int Executions { get; set; }
    internal int Exceptional { get; set; }
    internal int Skipped { get; set; }
    internal int ShapeMask { get; set; }
    internal int ProvenShapeMask { get; set; }
    internal ImmutableArray<FuzzFailure>.Builder Failures { get; } = ImmutableArray.CreateBuilder<FuzzFailure>();

    internal void Add(ResourceBoundBatch other)
    {
        other = RepositoryParameterGuard.NotNull(other, nameof(other));
        Proven += other.Proven;
        Violating += other.Violating;
        Tight += other.Tight;
        NativeBounds += other.NativeBounds;
        Executions += other.Executions;
        Exceptional += other.Exceptional;
        Skipped += other.Skipped;
        ShapeMask |= other.ShapeMask;
        ProvenShapeMask |= other.ProvenShapeMask;
        Failures.AddRange(other.Failures.Take(Math.Max(0, FuzzRunner.MaximumRetainedFailures - Failures.Count)));
    }

    internal void Fail(ResourceBoundCase item, int seed, string route, ResourceBoundInput input, string detail)
    {
        item = RepositoryParameterGuard.NotNull(item, nameof(item));
        route = RepositoryParameterGuard.NotNull(route, nameof(route));
        input = RepositoryParameterGuard.NotNull(input, nameof(input));
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        if (Failures.Count < FuzzRunner.MaximumRetainedFailures)
        {
            var reproducer = item.Member + "\n// input: " + input;
            Failures.Add(new(item.Ordinal, seed, "resource-bound-" + route, reproducer, reproducer, item.Name + " (" + item.Shape + "): " + detail));
        }
    }
}

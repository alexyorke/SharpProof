using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

public sealed partial class EffectMethodResult
{
    internal EffectMethodResult(
        IMethodSymbol method, EffectSummary summary,
        ImmutableArray<EffectDirectWitness> directWitnesses = default)
        : this(
            method,
            summary,
            EffectSummaryProjector.Project(summary),
            directWitnesses.IsDefault ? [] : directWitnesses)
    {
    }
}

/// <summary>
/// Compilation-scoped deterministic may-effect analysis.
/// </summary>
public sealed class EffectAnalysisSession
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private readonly Dictionary<SyntaxTree, ImmutableHashSet<string>> _definedPreprocessorSymbols = [];
    private readonly ExternalEffectResolver _external;
    private readonly IEffectCallPreconditionPolicy
        _callPreconditions;
    private readonly EffectMethodNodeBuilder _nodeBuilder;
    private readonly object _gate = new();
    private readonly Dictionary<IMethodSymbol, EffectMethodNode> _nodes = new(SymbolEqualityComparer.Default);
    private volatile ImmutableDictionary<IMethodSymbol, EffectSummary> _summaries =
        ImmutableDictionary.Create<IMethodSymbol, EffectSummary>(SymbolEqualityComparer.Default);

    public EffectAnalysisSession(Compilation compilation, ApiSpecTable? apiSpecs = null)
        : this(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            new ApiSpecResolver(apiSpecs ?? ApiSpecTable.Default)
                .Resolve(compilation),
            callPreconditions: null)
    {
    }

    internal EffectAnalysisSession(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs,
        IEffectCallPreconditionPolicy? callPreconditions = null)
    {
        _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _conditionalAttribute = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.ConditionalAttribute);
        _external = new ExternalEffectResolver(compilation,
            ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs)));
        _callPreconditions =
            callPreconditions ??
            new ConservativeEffectCallPreconditionPolicy(
                compilation);
        _nodeBuilder = new EffectMethodNodeBuilder(
            this,
            compilation,
            ManagedAbstractFlow.Create(compilation, apiSpecs));
    }

    public Compilation Compilation => _compilation;
    internal ResolvedApiSpecTable ApiSpecs => _external.ApiSpecs;

    internal EffectContractResolution ResolveExternalContract(IMethodSymbol method)
    {
        return _external.ResolveContract(method);
    }

    public EffectMethodResult Analyze(
        IMethodSymbol method, CancellationToken cancellationToken = default)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));

        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizeMethod(method);
        if (!IsSourceMethod(normalized))
        {
            return new EffectMethodResult(
                normalized,
                EffectSummaryOperations.Join(
                    _external.Resolve(normalized),
                    ResolveEntryPreconditions(normalized)));
        }

        EnsureAnalyzed([normalized], cancellationToken);
        var summary = _summaries.TryGetValue(normalized, out var analyzed)
            ? analyzed
            : EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnsupportedOperation);
        ImmutableArray<EffectDirectWitness> directWitnesses;
        lock (_gate)
        {
            directWitnesses = _nodes.TryGetValue(normalized, out var node)
                ? node.DirectWitnesses
                : [];
        }

        return new EffectMethodResult(normalized, summary, directWitnesses);
    }

    public ImmutableArray<EffectMethodResult> AnalyzeAll(
        CancellationToken cancellationToken = default)
    {
        var methods = CollectSourceMethods(cancellationToken);
        EnsureAnalyzed(methods, cancellationToken);
        return [.. methods.Select(method => new EffectMethodResult(method, _summaries[method]))];
    }

    internal int AnalyzedSourceMethodCount => _summaries.Count;

    internal EffectSummary ResolveCall(
        IMethodSymbol caller, IMethodSymbol target,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments, bool dispatchUncertain,
        List<EffectCallSite> sourceCalls, IOperation origin,
        IOperation? instance,
        ImmutableArray<IOperation?> actualArguments,
        ManagedFlowResult? flow)
    {
        if (target.ReducedFrom != null)
        {
            actualArguments = [instance, .. actualArguments];
            instance = null;
            arguments = [receiver, .. arguments];
            receiver = EffectRegionSet.Empty;
        }
        var normalized = NormalizeMethod(target);
        var preconditions = _callPreconditions.Assess(
            new EffectCallPreconditionContext(
                caller,
                normalized,
                instance,
                actualArguments,
                flow,
                origin));
        var preconditionEvidence =
            preconditions == EffectCallPreconditionStatus.NotProven
                ? EffectSummaryOperations.IncompleteAnalysis(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven)
                : EffectSummary.Empty;
        if (dispatchUncertain)
        {
            return EffectSummaryOperations.Join(
                preconditionEvidence,
                EffectSummaryOperations.DirectCall(),
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.DirectCall |
                    EffectUncertainty.Dispatch));
        }

        if (IsSourceMethod(normalized))
        {
            sourceCalls.Add(new EffectCallSite(normalized, receiver, arguments, origin));
            return EffectSummaryOperations.Join(
                preconditionEvidence,
                EffectSummaryOperations.DirectCall());
        }
        return EffectSummaryOperations.Join(
            preconditionEvidence,
            EffectSummaryOperations.DirectCall(),
            EffectSummaryOperations.Remap(_external.Resolve(normalized), receiver, arguments));
    }

    internal EffectSummary ResolveEntryPreconditions(
        IMethodSymbol method)
    {
        return _callPreconditions.AssessEntry(
                NormalizeMethod(method)) ==
            EffectCallPreconditionStatus.NotProven
                ? EffectSummaryOperations.IncompleteAnalysis(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven)
                : EffectSummary.Empty;
    }

    internal EffectThrowSet ResolveExceptionSet(params string[] metadataNames)
    {
        return _external.ResolveExceptionSet(metadataNames);
    }

    internal bool IsConditionallyElided(IInvocationOperation invocation)
    {
        if (_conditionalAttribute == null ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions)
        {
            return false;
        }

        var conditionalSymbols = invocation.TargetMethod.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition, _conditionalAttribute.OriginalDefinition))
            .Select(attribute =>
                attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToImmutableArray();
        if (conditionalSymbols.IsDefaultOrEmpty)
        {
            return false;
        }

        var definedSymbols = GetDefinedPreprocessorSymbols(invocation.Syntax.SyntaxTree);
        return conditionalSymbols.All(symbol => !definedSymbols.Contains(symbol!));
    }

    private ImmutableHashSet<string> GetDefinedPreprocessorSymbols(
        SyntaxTree tree)
    {
        if (_definedPreprocessorSymbols.TryGetValue(tree, out var cached))
        {
            return cached;
        }

        cached = CSharpPreprocessorSymbols.GetDefined(tree);
        _definedPreprocessorSymbols.Add(tree, cached);
        return cached;
    }

    internal EffectThrowSet ResolveThrownException(IOperation? exception)
    {
        while (exception is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion)
        {
            exception = conversion.Operand;
        }

        if (exception?.ConstantValue is { HasValue: true, Value: null })
        {
            return ResolveExceptionSet(FrameworkTypeMetadataNames.NullReferenceException);
        }

        if (exception?.Type is INamedTypeSymbol named)
        {
            return EffectThrowSet.Create([named]);
        }

        return EffectThrowSet.Unknown;
    }

    internal EffectSummary ResolveStaticFieldTypeInitialization(
        IMethodSymbol caller, IFieldSymbol field)
    {
        var normalizedTarget = field.ContainingType.OriginalDefinition;
        if (caller.MethodKind == MethodKind.StaticConstructor &&
            SymbolEqualityComparer.Default.Equals(caller.ContainingType.OriginalDefinition, normalizedTarget))
        {
            return EffectSummary.Empty;
        }

        var isSourceType = SymbolEqualityComparer.Default.Equals(
            normalizedTarget.ContainingAssembly, _compilation.Assembly);
        var mayInitialize = !isSourceType ||
            EffectMethodNodeBuilder.HasPotentialStaticInitialization(
                normalizedTarget,
                ApiSpecs);
        return mayInitialize
            ? EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall)
            : EffectSummary.Empty;
    }

    private void EnsureAnalyzed(
        IEnumerable<IMethodSymbol> roots, CancellationToken cancellationToken)
    {
        var orderedRoots = roots
            .Select(NormalizeMethod)
            .Where(IsSourceMethod)
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(static method => method, EffectSymbolComparer<IMethodSymbol>.Instance)
            .ToImmutableArray();
        var snapshot = _summaries;
        if (orderedRoots.All(snapshot.ContainsKey))
        {
            return;
        }

        lock (_gate)
        {
            snapshot = _summaries;
            var pendingRoots = orderedRoots.Where(method => !snapshot.ContainsKey(method)).ToImmutableArray();
            if (pendingRoots.IsDefaultOrEmpty)
            {
                return;
            }

            var nodes = BuildNodes(pendingRoots, snapshot, cancellationToken);
            _summaries = ComputeSummaries(nodes, snapshot, cancellationToken);
        }
    }

    private ImmutableDictionary<IMethodSymbol, EffectSummary> ComputeSummaries(
        IReadOnlyDictionary<IMethodSymbol, EffectMethodNode> nodes,
        ImmutableDictionary<IMethodSymbol, EffectSummary> existing,
        CancellationToken cancellationToken)
    {
        var summaries = existing.ToBuilder();
        foreach (var method in EffectCallGraph.FindRecursiveMethods(
                     nodes,
                     cancellationToken))
        {
            summaries[method] = EffectSummaryDomain.Instance.Join(nodes[method].LocalSummary,
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.DirectCall | EffectUncertainty.Recursion));
        }

        EffectSummary Compute(IMethodSymbol method)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summaries.TryGetValue(method, out var cached))
            {
                return cached;
            }

            if (!nodes.TryGetValue(method, out var node))
            {
                return EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall);
            }

            var summary = node.LocalSummary;
            foreach (var call in node.Calls
                         .OrderBy(static call => call.Target, EffectSymbolComparer<IMethodSymbol>.Instance)
                         .ThenBy(static call => call.Receiver.GetHashCode()))
            {
                summary = EffectSummaryDomain.Instance.Join(summary,
                    EffectExceptionFlow.KeepEscaping(EffectSummaryOperations.Remap(
                        Compute(call.Target), call.Receiver, call.Arguments),
                        call.Origin, _compilation));
            }

            summaries[method] = summary;
            return summary;
        }

        foreach (var method in nodes.Keys.OrderBy(
                     static method => method, EffectSymbolComparer<IMethodSymbol>.Instance))
        {
            Compute(method);
        }

        return summaries.ToImmutable();
    }

    private Dictionary<IMethodSymbol, EffectMethodNode> BuildNodes(
        ImmutableArray<IMethodSymbol> roots,
        ImmutableDictionary<IMethodSymbol, EffectSummary> knownSummaries,
        CancellationToken cancellationToken)
    {
        var nodes = new Dictionary<IMethodSymbol, EffectMethodNode>(SymbolEqualityComparer.Default);
        var pending = new Queue<IMethodSymbol>(roots);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = pending.Dequeue();
            if (knownSummaries.ContainsKey(method) || nodes.ContainsKey(method))
            {
                continue;
            }

            if (!_nodes.TryGetValue(method, out var node))
            {
                node = _nodeBuilder.Build(method, cancellationToken);
                _nodes.Add(method, node);
            }
            nodes.Add(method, node);
            foreach (var call in node.Calls)
            {
                if (!knownSummaries.ContainsKey(call.Target) && !nodes.ContainsKey(call.Target))
                {
                    pending.Enqueue(call.Target);
                }
            }
        }
        return nodes;
    }

    private ImmutableArray<IMethodSymbol> CollectSourceMethods(CancellationToken cancellationToken)
    {
        var methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, tree);
            foreach (var syntax in tree.GetRoot(cancellationToken).DescendantNodesAndSelf())
            {
                var symbol = model.GetDeclaredSymbol(syntax, cancellationToken);
                switch (symbol)
                {
                    case IMethodSymbol method when IsSourceMethod(method):
                        methods.Add(NormalizeMethod(method));
                        break;
                    case IPropertySymbol property:
                        if (property.GetMethod is { } getter && IsSourceMethod(getter))
                        {
                            methods.Add(NormalizeMethod(getter));
                        }

                        if (property.SetMethod is { } setter && IsSourceMethod(setter))
                        {
                            methods.Add(NormalizeMethod(setter));
                        }

                        break;
                }
            }
        }
        return [.. methods.OrderBy(
            static method => method, EffectSymbolComparer<IMethodSymbol>.Instance)];
    }

    private bool IsSourceMethod(IMethodSymbol method)
    {
        return !method.IsAbstract &&
        !method.IsExtern &&
        method.DeclaringSyntaxReferences.Length != 0 &&
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _compilation.Assembly);
    }

    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        var normalized = method.ReducedFrom ?? method;
        normalized = normalized.PartialImplementationPart ?? normalized;
        return normalized.OriginalDefinition;
    }
}

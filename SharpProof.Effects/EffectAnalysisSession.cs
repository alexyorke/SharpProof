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
    private readonly InvocationEmissionPolicy _invocationEmission;
    private readonly ExternalEffectResolver _external;
    private readonly IEffectCallPreconditionPolicy
        _callPreconditions;
    private readonly EffectModuleInitialization _moduleInitialization;
    private readonly EffectMethodNodeBuilder _nodeBuilder;
    private readonly object _gate = new();
    private ImmutableArray<IMethodSymbol> _moduleInitializers;
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
        _invocationEmission = new InvocationEmissionPolicy(compilation);
        _external = new ExternalEffectResolver(compilation,
            ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs)));
        _callPreconditions =
            callPreconditions ??
            new ConservativeEffectCallPreconditionPolicy(
                compilation);
        _moduleInitialization = new EffectModuleInitialization(compilation);
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

        var moduleInitializers = GetModuleInitializers(cancellationToken);
        EnsureAnalyzed(moduleInitializers.Add(normalized), cancellationToken);
        var summaries = _summaries;
        var summary = summaries.TryGetValue(normalized, out var analyzed)
            ? analyzed
            : EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnsupportedOperation);
        var initialization = EffectModuleInitialization.SummarizeBeforeEntry(
            normalized,
            moduleInitializers,
            summaries);
        summary = EffectSummaryDomain.Instance.Join(initialization, summary);
        ImmutableArray<EffectDirectWitness> directWitnesses;
        lock (_gate)
        {
            directWitnesses =
                !EffectModuleInitialization.CanPreventBodyEntry(initialization) &&
                _nodes.TryGetValue(normalized, out var node)
                ? node.DirectWitnesses
                : [];
        }

        return new EffectMethodResult(normalized, summary, directWitnesses);
    }

    public ImmutableArray<EffectMethodResult> AnalyzeAll(
        CancellationToken cancellationToken = default)
    {
        var methods = CollectSourceMethods(cancellationToken);
        var moduleInitializers = GetModuleInitializers(cancellationToken);
        EnsureAnalyzed(methods, cancellationToken);
        var summaries = _summaries;
        lock (_gate)
        {
            return [.. methods.Select(method =>
            {
                var initialization =
                    EffectModuleInitialization.SummarizeBeforeEntry(
                        method,
                        moduleInitializers,
                        summaries);
                return new EffectMethodResult(
                    method,
                    EffectSummaryDomain.Instance.Join(
                        initialization,
                        summaries[method]),
                    EffectModuleInitialization.CanPreventBodyEntry(
                        initialization)
                        ? []
                        : _nodes[method].DirectWitnesses);
            })];
        }
    }

    internal int AnalyzedSourceMethodCount => _summaries.Count;

    internal EffectSummary ResolveCall(
        IMethodSymbol caller, IMethodSymbol target,
        EffectRegionSet receiver,
        EffectRegionSet writeReceiver,
        ImmutableArray<EffectRegionSet> arguments, bool dispatchUncertain,
        List<EffectCallSite> sourceCalls, IOperation origin,
        IOperation? instance,
        ImmutableArray<IOperation?> actualArguments,
        ManagedFlowResult? flow)
    {
        if (target.ReducedFrom != null)
        {
            actualArguments = actualArguments.Insert(0, instance);
            instance = null;
            arguments = arguments.Insert(0, receiver);
            receiver = EffectRegionSet.Empty;
            writeReceiver = EffectRegionSet.Empty;
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
            sourceCalls.Add(new EffectCallSite(
                normalized,
                receiver,
                writeReceiver,
                arguments,
                origin));
            return EffectSummaryOperations.Join(
                preconditionEvidence,
                EffectSummaryOperations.DirectCall());
        }
        return EffectSummaryOperations.Join(
            preconditionEvidence,
            EffectSummaryOperations.DirectCall(),
            EffectSummaryOperations.Remap(
                _external.Resolve(normalized),
                receiver,
                writeReceiver,
                arguments));
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
        return _invocationEmission.IsElided(invocation);
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

        var computeDepth = 0;

        EffectSummary Compute(IMethodSymbol method)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summaries.TryGetValue(method, out var cached))
            {
                return cached;
            }

            // A depth cut-off is a fact about this chain, not about the method,
            // so it is deliberately not cached: another path may still reach the
            // method shallowly enough to summarize it.
            if (!nodes.TryGetValue(method, out var node) ||
                computeDepth >= EffectCallGraph.MaximumCallGraphDepth)
            {
                return EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall);
            }

            computeDepth++;
            var summary = node.LocalSummary;
            foreach (var call in node.Calls
                         .OrderBy(static call => call.Target, EffectSymbolComparer<IMethodSymbol>.Instance)
                         .ThenBy(static call => call.Receiver.GetHashCode()))
            {
                summary = EffectSummaryDomain.Instance.Join(summary,
                    EffectExceptionFlow.KeepEscaping(EffectSummaryOperations.Remap(
                        Compute(call.Target),
                        call.Receiver,
                        call.WriteReceiver,
                        call.Arguments),
                        call.Origin, _compilation));
            }

            computeDepth--;
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

    private ImmutableArray<IMethodSymbol> GetModuleInitializers(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_moduleInitializers.IsDefault)
            {
                _moduleInitializers = _moduleInitialization.Discover(
                    cancellationToken);
            }

            return _moduleInitializers;
        }
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

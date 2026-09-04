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
    private readonly EffectKnownSymbols _knownSymbols;
    private readonly CSharpCompilation? _metadataImportCompilation;
    private readonly IEffectCallPreconditionPolicy
        _callPreconditions;
    private readonly EffectModuleInitialization _moduleInitialization;
    private readonly EffectMethodNodeBuilder _nodeBuilder;
    private readonly object _gate = new();
    private ImmutableArray<EffectModuleInitializer> _moduleInitializers;
    private readonly Dictionary<IMethodSymbol, EffectMethodNode> _nodes = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, bool>
        _staticInitializationFailureCache = new(SymbolEqualityComparer.Default);
    private ImmutableDictionary<IMethodSymbol, EffectSummary> _bodySummaries =
        ImmutableDictionary.Create<IMethodSymbol, EffectSummary>(
            SymbolEqualityComparer.Default);
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
        _metadataImportCompilation = compilation is CSharpCompilation csharp
            ? csharp.WithOptions(
                csharp.Options.WithMetadataImportOptions(
                    MetadataImportOptions.All))
            : null;
        _invocationEmission = new InvocationEmissionPolicy(compilation);
        _external = new ExternalEffectResolver(compilation,
            ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs)));
        _knownSymbols = new EffectKnownSymbols(compilation);
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
    internal EffectKnownSymbols KnownSymbols => _knownSymbols;

    internal EffectContractResolution ResolveExternalContract(IMethodSymbol method)
    {
        return _external.ResolveContract(method);
    }

    public EffectMethodResult Analyze(
        IMethodSymbol method, CancellationToken cancellationToken = default)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));

        cancellationToken.ThrowIfCancellationRequested();
        var preconditionTarget = NormalizeMethodConstruction(method);
        var normalized = preconditionTarget.OriginalDefinition;
        if (!IsSourceMethod(normalized))
        {
            return new EffectMethodResult(
                normalized,
                EffectSummaryOperations.Join(
                    _external.Resolve(normalized),
                    ResolveEntryPreconditions(preconditionTarget)));
        }

        var moduleInitializers = GetModuleInitializers(cancellationToken);
        EnsureAnalyzed(
            moduleInitializers.Select(static initializer => initializer.Method)
                .Append(normalized),
            cancellationToken);
        var summaries = _summaries;
        var summary = summaries.TryGetValue(normalized, out var analyzed)
            ? analyzed
            : EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnsupportedOperation);
        var initialization = EffectModuleInitialization.SummarizeBeforeEntry(
            normalized,
            moduleInitializers,
            summaries);
        summary = initialization.Then(new EffectStep(summary, true)).Summary;
        ImmutableArray<EffectDirectWitness> directWitnesses;
        lock (_gate)
        {
            directWitnesses =
                !EffectModuleInitialization.CanPreventBodyEntry(
                    initialization.Summary) &&
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
        var initializationByMethod =
            new Dictionary<IMethodSymbol, EffectStep>(
                SymbolEqualityComparer.Default);
        var finalInitialization =
            new EffectStep(EffectSummary.Bottom, true);
        foreach (var initializer in moduleInitializers)
        {
            initializationByMethod[initializer.Method] = finalInitialization;
            finalInitialization = finalInitialization.Then(new EffectStep(
                summaries.TryGetValue(initializer.Method, out var summary)
                    ? summary
                    : EffectSummaryOperations.UnknownBoundary(
                        EffectUncertainty.UnsupportedOperation),
                initializer.CompletesNormally));
        }
        lock (_gate)
        {
            return [.. methods.Select(method =>
            {
                var initialization = initializationByMethod.TryGetValue(
                        method,
                        out var beforeInitializer)
                    ? beforeInitializer
                    : finalInitialization;
                return new EffectMethodResult(
                    method,
                    initialization.Then(new EffectStep(
                        summaries[method],
                        true)).Summary,
                    EffectModuleInitialization.CanPreventBodyEntry(
                        initialization.Summary)
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
        var preconditionTarget = NormalizeMethodConstruction(target);
        var normalized = preconditionTarget.OriginalDefinition;
        var preconditions = _callPreconditions.Assess(
            new EffectCallPreconditionContext(
                caller,
                preconditionTarget,
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
            ResolveExactMetadataTypeInitialization(normalized),
            EffectSummaryOperations.Remap(
                _external.Resolve(normalized),
                receiver,
                writeReceiver,
                arguments));
    }

    private EffectSummary ResolveExactMetadataTypeInitialization(
        IMethodSymbol target)
    {
        if (!ApiSpecs.TryGet(target, out var spec) ||
            !EffectMethodNodeBuilder.CanTriggerOwnTypeInitialization(
                target))
        {
            return EffectSummary.Empty;
        }

        var importedType = ResolveImportedMetadataType(
            target,
            spec.Template.Target.ContainingTypeMetadataName);
        if (importedType == null)
        {
            return EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnmodeledCall);
        }

        var initializers = importedType.StaticConstructors;
        if (initializers.IsDefaultOrEmpty)
        {
            return EffectSummary.Empty;
        }

        if (initializers.Length != 1)
        {
            return EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnmodeledCall);
        }

        return WrapTypeInitializationFailures(
            new ExternalEffectResolver(
                _metadataImportCompilation!,
                ApiSpecs).Resolve(initializers[0]));
    }

    private INamedTypeSymbol? ResolveImportedMetadataType(
        IMethodSymbol target,
        string metadataName)
    {
        if (_metadataImportCompilation == null)
        {
            return null;
        }

        foreach (var reference in _compilation.References)
        {
            if (_compilation.GetAssemblyOrModuleSymbol(reference)
                    is not IAssemblySymbol assembly ||
                !SymbolEqualityComparer.Default.Equals(
                    assembly,
                    target.ContainingAssembly) ||
                _metadataImportCompilation.GetAssemblyOrModuleSymbol(
                    reference) is not IAssemblySymbol importedAssembly)
            {
                continue;
            }

            return importedAssembly.GetTypeByMetadataName(metadataName);
        }

        return null;
    }

    internal EffectSummary ResolveEntryPreconditions(
        IMethodSymbol method)
    {
        return _callPreconditions.AssessEntry(
                NormalizeMethodConstruction(method)) ==
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

    internal EffectSummary WrapTypeInitializationFailures(
        EffectSummary summary)
    {
        return summary.Throws.IsEmpty
            ? summary
            : EffectSummaryOperations.WithThrows(
                summary,
                ResolveExceptionSet(
                    FrameworkTypeMetadataNames.TypeInitializationException));
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
        if (OperationCompletionEvaluator
                .CanAssumeStaticInitializationComplete(caller, field))
        {
            return EffectSummary.Empty;
        }

        var isSourceType = SymbolEqualityComparer.Default.Equals(
            normalizedTarget.ContainingAssembly, _compilation.Assembly);
        if (isSourceType &&
            field.ContainingType.IsGenericType &&
            !SymbolEqualityComparer.Default.Equals(
                caller.ContainingType,
                field.ContainingType) &&
            normalizedTarget.StaticConstructors.Any(
                static constructor => !constructor.IsImplicitlyDeclared))
        {
            return EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnmodeledCall);
        }
        var mayInitialize = !isSourceType ||
            EffectMethodNodeBuilder.HasPotentialStaticInitialization(
                normalizedTarget,
                ApiSpecs);
        if (!mayInitialize)
        {
            return EffectSummary.Empty;
        }
        if (isSourceType && StaticInitializationCannotComplete(normalizedTarget))
        {
            return EffectSummaryOperations.Throw(
                ResolveExceptionSet(
                    FrameworkTypeMetadataNames.TypeInitializationException));
        }
        return EffectSummaryOperations.UnknownBoundary(
            EffectUncertainty.UnmodeledCall);
    }

    private bool StaticInitializationCannotComplete(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (_staticInitializationFailureCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var facts = new DefiniteOperationFacts(
            _compilation,
            CancellationToken.None);
        var result = !EffectMethodNodeBuilder.AllStaticInitializersSatisfy(
                type, _compilation, facts.MayCompleteNormally) ||
            type.StaticConstructors.Any(
            constructor => constructor.DeclaringSyntaxReferences.Length != 0 &&
                !facts.MethodCanCompleteNormally(constructor));
        _staticInitializationFailureCache.Add(type, result);
        return result;
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
        var bodySummaries = _bodySummaries.ToBuilder();
        foreach (var method in EffectCallGraph.FindRecursiveMethods(
                     nodes,
                     cancellationToken))
        {
            var recursive = EffectSummaryDomain.Instance.Join(
                nodes[method].LocalSummary,
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.DirectCall |
                    EffectUncertainty.Recursion));
            summaries[method] = recursive;
            bodySummaries[method] = recursive;
        }

        var computeDepth = 0;
        var activeEntries = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);

        EffectSummary Compute(IMethodSymbol method)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summaries.TryGetValue(method, out var cached))
            {
                return cached;
            }

            if (!nodes.ContainsKey(method) || !activeEntries.Add(method))
            {
                return EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.UnmodeledCall);
            }

            var summary = ComputeBody(method);
            if (_nodeBuilder.TryGetBeforeFieldInitNode(
                    method,
                    out var initialization))
            {
                var initializationSummary = initialization.LocalSummary;
                foreach (var call in OrderCalls(initialization.Calls))
                {
                    var target = HasSameContainingType(method, call.Target)
                        ? ComputeBody(call.Target)
                        : Compute(call.Target);
                    initializationSummary = JoinCall(
                        initializationSummary,
                        call,
                        target,
                        wrapTypeInitializationFailures: true);
                }
                summary = EffectSummaryDomain.Instance.Join(
                    summary,
                    initializationSummary);
            }

            activeEntries.Remove(method);
            summaries[method] = summary;
            return summary;
        }

        EffectSummary ComputeBody(IMethodSymbol method)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bodySummaries.TryGetValue(method, out var cached))
            {
                return cached;
            }

            // A depth cut-off is a fact about this chain, not about the method,
            // so it is deliberately not cached: another path may still reach the
            // method shallowly enough to summarize it.
            if (!nodes.TryGetValue(method, out var node) ||
                computeDepth >= EffectCallGraph.MaximumCallGraphDepth)
            {
                return EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.UnmodeledCall);
            }

            computeDepth++;
            var summary = node.LocalSummary;
            foreach (var call in OrderCalls(node.Calls))
            {
                var target =
                    EffectMethodNodeBuilder
                        .CanTriggerBeforeFieldInitInitialization(method) &&
                    HasSameContainingType(method, call.Target)
                        ? ComputeBody(call.Target)
                        : Compute(call.Target);
                summary = JoinCall(summary, call, target);
            }

            computeDepth--;
            bodySummaries[method] = summary;
            return summary;
        }

        foreach (var method in nodes.Keys.OrderBy(
                     static method => method, EffectSymbolComparer<IMethodSymbol>.Instance))
        {
            Compute(method);
        }

        _bodySummaries = bodySummaries.ToImmutable();
        return summaries.ToImmutable();

        EffectSummary JoinCall(
            EffectSummary summary,
            EffectCallSite call,
            EffectSummary target,
            bool wrapTypeInitializationFailures = false)
        {
            var remapped = EffectSummaryOperations.Remap(
                target,
                call.Receiver,
                call.WriteReceiver,
                call.Arguments);
            if (wrapTypeInitializationFailures)
            {
                remapped = WrapTypeInitializationFailures(
                    remapped);
            }
            return EffectSummaryDomain.Instance.Join(
                summary,
                EffectExceptionFlow.KeepEscaping(
                    remapped,
                    call.Origin,
                    _compilation));
        }

        static IOrderedEnumerable<EffectCallSite> OrderCalls(
            IEnumerable<EffectCallSite> calls)
        {
            return calls
                .OrderBy(
                    static call => call.Target,
                    EffectSymbolComparer<IMethodSymbol>.Instance)
                .ThenBy(static call => call.Receiver.GetHashCode());
        }

        static bool HasSameContainingType(
            IMethodSymbol left,
            IMethodSymbol right)
        {
            return SymbolEqualityComparer.Default.Equals(
                left.ContainingType.OriginalDefinition,
                right.ContainingType.OriginalDefinition);
        }
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
            var calls = _nodeBuilder.TryGetBeforeFieldInitNode(
                    method,
                    out var initialization)
                ? node.Calls.Concat(initialization.Calls)
                : node.Calls;
            foreach (var call in calls)
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
                    case INamedTypeSymbol type
                        when syntax is TypeDeclarationSyntax declaration:
                        AddPrimaryConstructor(
                            methods,
                            type,
                            declaration,
                            cancellationToken);
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

    private void AddPrimaryConstructor(
        HashSet<IMethodSymbol> methods,
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        if (declaration.ParameterList == null)
        {
            return;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (constructor.MethodKind == MethodKind.Constructor &&
                IsSourceMethod(constructor) &&
                constructor.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree == declaration.SyntaxTree &&
                    reference.GetSyntax(cancellationToken) is
                        TypeDeclarationSyntax owner &&
                    owner.Span == declaration.Span))
            {
                methods.Add(NormalizeMethod(constructor));
            }
        }
    }

    private ImmutableArray<EffectModuleInitializer> GetModuleInitializers(
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
        return NormalizeMethodConstruction(method).OriginalDefinition;
    }

    internal static IMethodSymbol NormalizeMethodConstruction(
        IMethodSymbol method)
    {
        var normalized = method.ReducedFrom ?? method;
        normalized = normalized.PartialImplementationPart ?? normalized;
        return normalized;
    }
}

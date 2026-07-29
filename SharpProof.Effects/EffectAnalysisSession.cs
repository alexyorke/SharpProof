using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

public sealed class EffectMethodResult {
    internal EffectMethodResult(
        IMethodSymbol method, EffectSummary summary,
        ImmutableArray<EffectDirectWitness> directWitnesses = default) {
        Method = method;
        Summary = summary;
        Projection = EffectSummaryProjector.Project(summary);
        DirectWitnesses = directWitnesses.IsDefault ? [] : directWitnesses;
    }

    public IMethodSymbol Method { get; }
    public EffectSummary Summary { get; }
    public EffectProjection Projection { get; }
    internal ImmutableArray<EffectDirectWitness> DirectWitnesses { get; }
}

/// <summary>
/// Compilation-scoped deterministic may-effect analysis.
/// </summary>
public sealed class EffectAnalysisSession {
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private readonly Dictionary<SyntaxTree, ImmutableHashSet<string>> _definedPreprocessorSymbols = [];
    private readonly ExternalEffectResolver _external;
    private readonly ManagedAbstractFlow _managedFlow;
    private readonly object _gate = new();
    private readonly Dictionary<IMethodSymbol, EffectMethodNode> _nodes = new(SymbolEqualityComparer.Default);
    private volatile ImmutableDictionary<IMethodSymbol, EffectSummary> _summaries =
        ImmutableDictionary.Create<IMethodSymbol, EffectSummary>(SymbolEqualityComparer.Default);

    public EffectAnalysisSession(Compilation compilation, ApiSpecTable? apiSpecs = null)
        : this(
            compilation,
            new ApiSpecResolver(apiSpecs ?? ApiSpecTable.Default)
                .Resolve(compilation ?? throw new ArgumentNullException(nameof(compilation)))) {
    }

    internal EffectAnalysisSession(Compilation compilation, ResolvedApiSpecTable apiSpecs) {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _conditionalAttribute = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.ConditionalAttribute);
        _external = new ExternalEffectResolver(compilation,
            apiSpecs ?? throw new ArgumentNullException(nameof(apiSpecs)));
        _managedFlow = ManagedAbstractFlow.Create(compilation, apiSpecs);
    }

    public Compilation Compilation => _compilation;
    internal ResolvedApiSpecTable ApiSpecs => _external.ApiSpecs;

    internal EffectContractResolution ResolveExternalContract(IMethodSymbol method) =>
        _external.ResolveContract(method);

    public EffectMethodResult Analyze(
        IMethodSymbol method, CancellationToken cancellationToken = default) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizeMethod(method);
        if (!IsSourceMethod(normalized))
            return new EffectMethodResult(normalized, _external.Resolve(normalized));
        EnsureAnalyzed([normalized], cancellationToken);
        var summary = _summaries.TryGetValue(normalized, out var analyzed)
            ? analyzed
            : EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnsupportedOperation);
        ImmutableArray<EffectDirectWitness> directWitnesses;
        lock (_gate)
            directWitnesses = _nodes.TryGetValue(normalized, out var node)
                ? node.DirectWitnesses
                : [];
        return new EffectMethodResult(normalized, summary, directWitnesses);
    }

    public ImmutableArray<EffectMethodResult> AnalyzeAll(
        CancellationToken cancellationToken = default) {
        var methods = CollectSourceMethods(cancellationToken);
        EnsureAnalyzed(methods, cancellationToken);
        return [.. methods.Select(method => new EffectMethodResult(method, _summaries[method]))];
    }

    internal int AnalyzedSourceMethodCount => _summaries.Count;

    internal EffectSummary ResolveCall(
        IMethodSymbol target, EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments, bool dispatchUncertain,
        List<EffectCallSite> sourceCalls, IOperation origin) {
        if (target.ReducedFrom != null) {
            arguments = [receiver, .. arguments];
            receiver = EffectRegionSet.Empty;
        }
        var normalized = NormalizeMethod(target);
        if (dispatchUncertain)
            return EffectSummaryOperations.Join(
                EffectSummaryOperations.DirectCall(),
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.DirectCall |
                    EffectUncertainty.Dispatch));
        if (IsSourceMethod(normalized)) {
            sourceCalls.Add(new EffectCallSite(normalized, receiver, arguments, origin));
            return EffectSummaryOperations.DirectCall();
        }
        return EffectSummaryOperations.Join(
            EffectSummaryOperations.DirectCall(),
            EffectSummaryOperations.Remap(_external.Resolve(normalized), receiver, arguments));
    }

    internal EffectThrowSet ResolveExceptionSet(params string[] metadataNames) =>
        _external.ResolveExceptionSet(metadataNames);

    internal bool IsConditionallyElided(IInvocationOperation invocation) {
        if (_conditionalAttribute == null ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions options)
            return false;
        var conditionalSymbols = invocation.TargetMethod.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition, _conditionalAttribute.OriginalDefinition))
            .Select(attribute =>
                attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToImmutableArray();
        if (conditionalSymbols.IsDefaultOrEmpty) return false;
        var definedSymbols = GetDefinedPreprocessorSymbols(invocation.Syntax.SyntaxTree, options);
        return conditionalSymbols.All(symbol => !definedSymbols.Contains(symbol!));
    }

    private ImmutableHashSet<string> GetDefinedPreprocessorSymbols(
        SyntaxTree tree, CSharpParseOptions options) {
        if (_definedPreprocessorSymbols.TryGetValue(tree, out var cached))
            return cached;
        var definedSymbols = options.PreprocessorSymbolNames
            .ToImmutableHashSet(StringComparer.Ordinal)
            .ToBuilder();
        foreach (var trivia in tree.GetRoot().DescendantTrivia(descendIntoTrivia: true)) {
            var directive = trivia.GetStructure();
            switch (directive) {
                case DefineDirectiveTriviaSyntax { IsActive: true } define:
                    definedSymbols.Add(define.Name.ValueText);
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef:
                    definedSymbols.Remove(undef.Name.ValueText);
                    break;
            }
        }
        cached = definedSymbols.ToImmutable();
        _definedPreprocessorSymbols.Add(tree, cached);
        return cached;
    }

    internal EffectThrowSet ResolveThrownException(IOperation? exception) {
        while (exception is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion)
            exception = conversion.Operand;
        if (exception?.ConstantValue is { HasValue: true, Value: null })
            return ResolveExceptionSet(FrameworkTypeMetadataNames.NullReferenceException);
        if (exception?.Type is INamedTypeSymbol named)
            return EffectThrowSet.Create([named]);
        return EffectThrowSet.Unknown;
    }

    internal EffectSummary ResolveStaticFieldTypeInitialization(
        IMethodSymbol caller, IFieldSymbol field) {
        var normalizedTarget = field.ContainingType.OriginalDefinition;
        if (caller.MethodKind == MethodKind.StaticConstructor &&
            SymbolEqualityComparer.Default.Equals(caller.ContainingType.OriginalDefinition, normalizedTarget))
            return EffectSummary.Empty;
        var isSourceType = SymbolEqualityComparer.Default.Equals(
            normalizedTarget.ContainingAssembly, _compilation.Assembly);
        var mayInitialize = !isSourceType || HasPotentialStaticInitialization(normalizedTarget);
        return mayInitialize
            ? EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall)
            : EffectSummary.Empty;
    }

    private void EnsureAnalyzed(
        IEnumerable<IMethodSymbol> roots, CancellationToken cancellationToken) {
        var orderedRoots = roots
            .Select(NormalizeMethod)
            .Where(IsSourceMethod)
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(static method => method, EffectSymbolComparer<IMethodSymbol>.Instance)
            .ToImmutableArray();
        var snapshot = _summaries;
        if (orderedRoots.All(snapshot.ContainsKey)) return;
        lock (_gate) {
            snapshot = _summaries;
            var pendingRoots = orderedRoots.Where(method => !snapshot.ContainsKey(method)).ToImmutableArray();
            if (pendingRoots.IsDefaultOrEmpty) return;
            var nodes = BuildNodes(pendingRoots, snapshot, cancellationToken);
            _summaries = ComputeSummaries(nodes, snapshot, cancellationToken);
        }
    }

    private ImmutableDictionary<IMethodSymbol, EffectSummary> ComputeSummaries(
        IReadOnlyDictionary<IMethodSymbol, EffectMethodNode> nodes,
        ImmutableDictionary<IMethodSymbol, EffectSummary> existing,
        CancellationToken cancellationToken) {
        var summaries = existing.ToBuilder();
        foreach (var method in FindRecursiveMethods(nodes, cancellationToken))
            summaries[method] = EffectSummaryDomain.Instance.Join(nodes[method].LocalSummary,
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.DirectCall | EffectUncertainty.Recursion));

        EffectSummary Compute(IMethodSymbol method) {
            cancellationToken.ThrowIfCancellationRequested();
            if (summaries.TryGetValue(method, out var cached)) return cached;
            if (!nodes.TryGetValue(method, out var node))
                return EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall);
            var summary = node.LocalSummary;
            foreach (var call in node.Calls
                         .OrderBy(static call => call.Target, EffectSymbolComparer<IMethodSymbol>.Instance)
                         .ThenBy(static call => call.Receiver.GetHashCode()))
                summary = EffectSummaryDomain.Instance.Join(summary,
                    EffectExceptionFlow.KeepEscaping(EffectSummaryOperations.Remap(
                        Compute(call.Target), call.Receiver, call.Arguments),
                        call.Origin, _compilation));
            summaries[method] = summary;
            return summary;
        }

        foreach (var method in nodes.Keys.OrderBy(
                     static method => method, EffectSymbolComparer<IMethodSymbol>.Instance))
            Compute(method);
        return summaries.ToImmutable();
    }

    private Dictionary<IMethodSymbol, EffectMethodNode> BuildNodes(
        ImmutableArray<IMethodSymbol> roots,
        ImmutableDictionary<IMethodSymbol, EffectSummary> knownSummaries,
        CancellationToken cancellationToken) {
        var nodes = new Dictionary<IMethodSymbol, EffectMethodNode>(SymbolEqualityComparer.Default);
        var pending = new Queue<IMethodSymbol>(roots);
        while (pending.Count != 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var method = pending.Dequeue();
            if (knownSummaries.ContainsKey(method) || nodes.ContainsKey(method))
                continue;
            if (!_nodes.TryGetValue(method, out var node)) {
                node = BuildNode(method, cancellationToken);
                _nodes.Add(method, node);
            }
            nodes.Add(method, node);
            foreach (var call in node.Calls)
                if (!knownSummaries.ContainsKey(call.Target) && !nodes.ContainsKey(call.Target))
                    pending.Enqueue(call.Target);
        }
        return nodes;
    }

    private ImmutableArray<IMethodSymbol> CollectSourceMethods(CancellationToken cancellationToken) {
        var methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in _compilation.SyntaxTrees) {
            cancellationToken.ThrowIfCancellationRequested();
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, tree);
            foreach (var syntax in tree.GetRoot(cancellationToken).DescendantNodesAndSelf()) {
                var symbol = model.GetDeclaredSymbol(syntax, cancellationToken);
                switch (symbol) {
                    case IMethodSymbol method when IsSourceMethod(method):
                        methods.Add(NormalizeMethod(method));
                        break;
                    case IPropertySymbol property:
                        if (property.GetMethod is { } getter && IsSourceMethod(getter))
                            methods.Add(NormalizeMethod(getter));
                        if (property.SetMethod is { } setter && IsSourceMethod(setter))
                            methods.Add(NormalizeMethod(setter));
                        break;
                }
            }
        }
        return [.. methods.OrderBy(
            static method => method, EffectSymbolComparer<IMethodSymbol>.Instance)];
    }

    private EffectMethodNode BuildNode(IMethodSymbol method, CancellationToken cancellationToken) {
        var calls = new List<EffectCallSite>();
        var root = GetOperationRoot(method, cancellationToken);
        if (root == null)
            return new EffectMethodNode(EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnsupportedOperation), [], []);
        var graph = TryCreateControlFlowGraph(root, cancellationToken);
        var abstractAnalysis = graph == null
            ? null
            : _managedFlow.Analyze(
                method,
                graph,
                entryState: null,
                cancellationToken);
        var scanner = new OperationEffectScanner(
            this, method, calls, root, abstractAnalysis?.Result,
            allowDirectWitnesses: graph != null);
        var localSummary = graph == null
            ? EffectSummaryOperations.Join(scanner.Scan(root), EffectSummaryOperations.Unsupported())
            : AnalyzeControlFlowGraph(graph, scanner);
        // Cyclic scalar flow does not invalidate the conservative all-block effect scan.
        if (abstractAnalysis is {
            IsComplete: false,
            IncompleteReason: not EffectAnalysisIncompleteReason.CyclicControlFlow
        })
            localSummary = EffectSummaryOperations.Join(
                localSummary,
                EffectSummaryOperations.IncompleteAnalysis(
                    abstractAnalysis.IncompleteReason));
        localSummary = EffectSummaryOperations.Join(
            localSummary,
            scanner.ScanLexicalControlEffects(root),
            ScanConstructorMemberInitializers(method, scanner, cancellationToken),
            CanTriggerOwnTypeInitialization(method) &&
            HasPotentialStaticInitialization(method.ContainingType)
                ? EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall)
                : EffectSummary.Empty);
        return new EffectMethodNode(localSummary, [.. calls], scanner.DirectWitnesses);
    }

    private EffectSummary ScanConstructorMemberInitializers(
        IMethodSymbol method, OperationEffectScanner scanner,
        CancellationToken cancellationToken) {
        var staticInitializers = method.MethodKind == MethodKind.StaticConstructor;
        if (!staticInitializers && method.MethodKind != MethodKind.Constructor)
            return EffectSummary.Empty;

        var summary = EffectSummary.Empty;
        var write = EffectSummaryOperations.Write(EffectRegionSet.Create(
            staticInitializers ? EffectRegionId.Static() : EffectRegionId.Receiver));
        foreach (var member in method.ContainingType.GetMembers()
                     .Where(member => !member.IsImplicitlyDeclared &&
                         IsInitializableMember(member, staticInitializers))
                     .OrderBy(static member => member.MetadataName, StringComparer.Ordinal)) {
            foreach (var syntaxReference in member.DeclaringSyntaxReferences
                         .OrderBy(static reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                         .ThenBy(static reference => reference.Span.Start)) {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var expression = GetInitializerExpression(declaration);
                if (expression == null) continue;
                var model = SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(_compilation, expression.SyntaxTree);
                var operation = model.GetOperation(expression, cancellationToken);
                summary = EffectSummaryDomain.Instance.Join(summary,
                    operation == null
                        ? EffectSummaryOperations.Unsupported()
                        : EffectSummaryOperations.Join(scanner.Scan(operation), write));
            }
        }
        return summary;
    }

    private static bool HasPotentialStaticInitialization(INamedTypeSymbol type) =>
        type.StaticConstructors.Any(static constructor => !constructor.IsImplicitlyDeclared) ||
        type.GetMembers().Any(member => !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: true) &&
            member.DeclaringSyntaxReferences.Any(reference => GetInitializerExpression(reference.GetSyntax()) != null));

    private static bool CanTriggerOwnTypeInitialization(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Constructor ||
        method.IsStatic && method.MethodKind != MethodKind.StaticConstructor;

    private static bool IsInitializableMember(
        ISymbol member, bool staticInitializers) =>
        member switch {
            IFieldSymbol field => !field.IsConst && field.IsStatic == staticInitializers,
            IPropertySymbol property => property.IsStatic == staticInitializers,
            IEventSymbol @event => @event.IsStatic == staticInitializers,
            _ => false
        };

    private static ExpressionSyntax? GetInitializerExpression(SyntaxNode declaration) =>
        declaration switch {
            VariableDeclaratorSyntax variable => variable.Initializer?.Value,
            PropertyDeclarationSyntax property => property.Initializer?.Value,
            _ => null
        };

    private static EffectSummary AnalyzeControlFlowGraph(
        ControlFlowGraph graph, OperationEffectScanner scanner) {
        var summary = EffectSummary.Empty;
        foreach (var block in graph.Blocks.Where(static block => block.IsReachable)) {
            summary = EffectSummaryOperations.JoinFrom(summary,
                block.Operations.Where(scanner.IsReachable).Select(scanner.Scan));
            if (block.BranchValue != null && scanner.IsReachable(block.BranchValue))
                summary = EffectSummaryOperations.Join(summary, scanner.Scan(block.BranchValue));
        }
        return ManagedAbstractFlow.IsAcyclic(graph)
            ? summary
            : EffectSummaryOperations.Join(summary, EffectSummaryOperations.MayDiverge());
    }

    private IOperation? GetOperationRoot(IMethodSymbol method, CancellationToken cancellationToken) {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences
                     .OrderBy(static reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(static reference => reference.Span.Start)) {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            if (operation is IMethodBodyOperation or IConstructorBodyOperation or IBlockOperation)
                return operation;
            foreach (var node in syntax.DescendantNodes()) {
                operation = model.GetOperation(node, cancellationToken);
                if (operation is IMethodBodyOperation or IConstructorBodyOperation)
                    return operation;
            }
        }
        return null;
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        IOperation root, CancellationToken cancellationToken) {
        try {
            return root switch {
                IMethodBodyOperation method => ControlFlowGraph.Create(method, cancellationToken),
                IConstructorBodyOperation constructor => ControlFlowGraph.Create(constructor, cancellationToken),
                IBlockOperation { Parent: null } block => ControlFlowGraph.Create(block, cancellationToken),
                _ => null
            };
        }
        catch (ArgumentException) {
            return null;
        }
    }

    private static HashSet<IMethodSymbol> FindRecursiveMethods(
        IReadOnlyDictionary<IMethodSymbol, EffectMethodNode> nodes,
        CancellationToken cancellationToken) {
        var states = new Dictionary<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
        var stack = new List<IMethodSymbol>();
        var recursive = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        void Visit(IMethodSymbol method) {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(method, 1);
            stack.Add(method);
            foreach (var target in nodes[method].Calls
                         .Select(static call => call.Target)
                         .Where(nodes.ContainsKey)
                         .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                         .OrderBy(static target => target, EffectSymbolComparer<IMethodSymbol>.Instance)) {
                if (!states.TryGetValue(target, out var state))
                    Visit(target);
                else if (state == 1) {
                    for (var index = stack.Count - 1; index >= 0; index--) {
                        recursive.Add(stack[index]);
                        if (SymbolEqualityComparer.Default.Equals(stack[index], target))
                            break;
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
            states[method] = 2;
        }

        foreach (var method in nodes.Keys.OrderBy(
                     static method => method, EffectSymbolComparer<IMethodSymbol>.Instance))
            if (!states.ContainsKey(method))
                Visit(method);
        return recursive;
    }

    private bool IsSourceMethod(IMethodSymbol method) =>
        !method.IsAbstract &&
        !method.IsExtern &&
        method.DeclaringSyntaxReferences.Length != 0 &&
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _compilation.Assembly);

    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method) {
        var normalized = method.ReducedFrom ?? method;
        normalized = normalized.PartialImplementationPart ?? normalized;
        return normalized.OriginalDefinition;
    }
}

internal readonly record struct EffectMethodNode(
    EffectSummary LocalSummary,
    ImmutableArray<EffectCallSite> Calls,
    ImmutableArray<EffectDirectWitness> DirectWitnesses);

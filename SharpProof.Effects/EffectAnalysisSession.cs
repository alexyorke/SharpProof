namespace SharpProof.Effects;

public sealed class EffectMethodResult {
    internal EffectMethodResult(IMethodSymbol method, EffectSummary summary) {
        Method = method;
        Summary = summary;
        Projection = EffectSummaryProjector.Project(summary);
    }

    public IMethodSymbol Method { get; }
    public EffectSummary Summary { get; }
    public EffectProjection Projection { get; }
}

/// <summary>
/// Compilation-scoped deterministic may-effect analysis.
/// </summary>
public sealed class EffectAnalysisSession {
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private readonly Dictionary<SyntaxTree, ImmutableHashSet<string>>
        _definedPreprocessorSymbols = [];
    private readonly ExternalEffectResolver _external;
    private readonly object _gate = new();
    private volatile ImmutableDictionary<IMethodSymbol, EffectSummary>? _summaries;
    private ImmutableArray<IMethodSymbol> _orderedMethods;

    public EffectAnalysisSession(
        Compilation compilation,
        ApiSpecTable? apiSpecs = null)
        : this(
            compilation,
            new ApiSpecResolver(apiSpecs ?? ApiSpecTable.Default)
                .Resolve(
                    compilation ??
                    throw new ArgumentNullException(nameof(compilation)))) {
    }

    internal EffectAnalysisSession(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs) {
        _compilation = compilation ??
            throw new ArgumentNullException(nameof(compilation));
        _conditionalAttribute = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ConditionalAttribute);
        _external = new ExternalEffectResolver(
            compilation,
            apiSpecs ?? throw new ArgumentNullException(nameof(apiSpecs)));
    }

    public Compilation Compilation => _compilation;
    internal ResolvedApiSpecTable ApiSpecs => _external.ApiSpecs;

    public EffectMethodResult Analyze(
        IMethodSymbol method,
        CancellationToken cancellationToken = default) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizeMethod(method);
        if (!IsSourceMethod(normalized))
            return new EffectMethodResult(
                normalized,
                _external.Resolve(normalized));
        EnsureAnalyzed(cancellationToken);
        return new EffectMethodResult(
            normalized,
            _summaries!.TryGetValue(normalized, out var summary)
                ? summary
                : EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.UnsupportedOperation));
    }

    public ImmutableArray<EffectMethodResult> AnalyzeAll(
        CancellationToken cancellationToken = default) {
        EnsureAnalyzed(cancellationToken);
        return [.. _orderedMethods.Select(method =>
            new EffectMethodResult(method, _summaries![method]))];
    }

    internal EffectSummary ResolveCall(
        IMethodSymbol target,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments,
        bool dispatchUncertain,
        List<EffectCallSite> sourceCalls) {
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
            sourceCalls.Add(new EffectCallSite(normalized, receiver, arguments));
            return EffectSummaryOperations.DirectCall();
        }
        return EffectSummaryOperations.Join(
            EffectSummaryOperations.DirectCall(),
            EffectSummaryOperations.Remap(
                _external.Resolve(normalized),
                receiver,
                arguments));
    }

    internal EffectThrowSet ResolveExceptionSet(params string[] metadataNames) =>
        _external.ResolveExceptionSet(metadataNames);

    internal bool IsConditionallyElided(IInvocationOperation invocation) {
        if (_conditionalAttribute == null ||
            invocation.Syntax.SyntaxTree.Options is not
                Microsoft.CodeAnalysis.CSharp.CSharpParseOptions options)
            return false;
        var conditionalSymbols = invocation.TargetMethod.GetAttributes()
            .Where(attribute =>
                SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass?.OriginalDefinition,
                    _conditionalAttribute.OriginalDefinition))
            .Select(attribute =>
                attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToImmutableArray();
        if (conditionalSymbols.IsDefaultOrEmpty) return false;
        var definedSymbols = GetDefinedPreprocessorSymbols(
            invocation.Syntax.SyntaxTree,
            options);
        return conditionalSymbols.All(symbol => !definedSymbols.Contains(symbol!));
    }

    private ImmutableHashSet<string> GetDefinedPreprocessorSymbols(
        SyntaxTree tree,
        Microsoft.CodeAnalysis.CSharp.CSharpParseOptions options) {
        if (_definedPreprocessorSymbols.TryGetValue(tree, out var cached))
            return cached;
        var definedSymbols = options.PreprocessorSymbolNames
            .ToImmutableHashSet(StringComparer.Ordinal)
            .ToBuilder();
        foreach (var trivia in tree.GetRoot()
                     .DescendantTrivia(descendIntoTrivia: true)) {
            var directive = trivia.GetStructure();
            switch (directive) {
                case Microsoft.CodeAnalysis.CSharp.Syntax.DefineDirectiveTriviaSyntax {
                    IsActive: true
                } define:
                    definedSymbols.Add(define.Name.ValueText);
                    break;
                case Microsoft.CodeAnalysis.CSharp.Syntax.UndefDirectiveTriviaSyntax {
                    IsActive: true
                } undef:
                    definedSymbols.Remove(undef.Name.ValueText);
                    break;
            }
        }
        cached = definedSymbols.ToImmutable();
        _definedPreprocessorSymbols.Add(tree, cached);
        return cached;
    }

    internal EffectThrowSet ResolveThrownException(IOperation? exception) {
        if (exception?.ConstantValue is { HasValue: true, Value: null })
            return ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException);
        if (exception?.Type is INamedTypeSymbol named)
            return EffectThrowSet.Create([named]);
        return EffectThrowSet.Unknown;
    }

    internal EffectSummary ResolveStaticFieldTypeInitialization(
        IMethodSymbol caller,
        IFieldSymbol field) {
        var normalizedTarget = field.ContainingType.OriginalDefinition;
        if (caller.MethodKind == MethodKind.StaticConstructor &&
            SymbolEqualityComparer.Default.Equals(
                caller.ContainingType.OriginalDefinition,
                normalizedTarget))
            return EffectSummary.Empty;
        var isSourceType = SymbolEqualityComparer.Default.Equals(
            normalizedTarget.ContainingAssembly,
            _compilation.Assembly);
        var mayInitialize =
            !isSourceType ||
            HasPotentialStaticInitialization(normalizedTarget);
        return mayInitialize
            ? EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnmodeledCall)
            : EffectSummary.Empty;
    }

    private void EnsureAnalyzed(CancellationToken cancellationToken) {
        if (_summaries != null) return;
        lock (_gate) {
            if (_summaries != null) return;
            var nodes = BuildNodes(cancellationToken);
            var recursive = FindRecursiveMethods(nodes, cancellationToken);
            var summaries = new Dictionary<IMethodSymbol, EffectSummary>(
                SymbolEqualityComparer.Default);
            foreach (var method in recursive) {
                var node = nodes[method];
                summaries.Add(
                    method,
                    EffectSummaryDomain.Instance.Join(
                        node.LocalSummary,
                        EffectSummaryOperations.UnknownBoundary(
                            EffectUncertainty.DirectCall |
                            EffectUncertainty.Recursion)));
            }

            EffectSummary Compute(IMethodSymbol method) {
                cancellationToken.ThrowIfCancellationRequested();
                if (summaries.TryGetValue(method, out var cached))
                    return cached;
                if (!nodes.TryGetValue(method, out var node))
                    return EffectSummaryOperations.UnknownBoundary(
                        EffectUncertainty.UnmodeledCall);
                var summary = node.LocalSummary;
                foreach (var call in node.Calls
                             .OrderBy(static call => call.Target, EffectMethodComparer.Instance)
                             .ThenBy(static call => call.Receiver.GetHashCode())) {
                    var callee = Compute(call.Target);
                    summary = EffectSummaryDomain.Instance.Join(
                        summary,
                        EffectSummaryOperations.Remap(
                            callee,
                            call.Receiver,
                            call.Arguments));
                }
                summaries.Add(method, summary);
                return summary;
            }

            foreach (var method in nodes.Keys.OrderBy(
                         static method => method,
                         EffectMethodComparer.Instance))
                Compute(method);
            _orderedMethods = [.. nodes.Keys.OrderBy(
                static method => method,
                EffectMethodComparer.Instance)];
            _summaries = summaries.ToImmutableDictionary(
                SymbolEqualityComparer.Default);
        }
    }

    private Dictionary<IMethodSymbol, EffectMethodNode> BuildNodes(
        CancellationToken cancellationToken) {
        var methods = CollectSourceMethods(cancellationToken);
        var nodes = new Dictionary<IMethodSymbol, EffectMethodNode>(
            SymbolEqualityComparer.Default);
        var pending = new Queue<IMethodSymbol>(methods);
        while (pending.Count != 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var method = pending.Dequeue();
            if (nodes.ContainsKey(method)) continue;
            var node = BuildNode(method, cancellationToken);
            nodes.Add(method, node);
            foreach (var call in node.Calls)
                if (!nodes.ContainsKey(call.Target))
                    pending.Enqueue(call.Target);
        }
        return nodes;
    }

    private ImmutableArray<IMethodSymbol> CollectSourceMethods(
        CancellationToken cancellationToken) {
        var methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in _compilation.SyntaxTrees) {
            cancellationToken.ThrowIfCancellationRequested();
            var model =
                SharpProof.Frontend.Host.CompilationModelProvider
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
            static method => method,
            EffectMethodComparer.Instance)];
    }

    private EffectMethodNode BuildNode(
        IMethodSymbol method,
        CancellationToken cancellationToken) {
        var calls = new List<EffectCallSite>();
        var root = GetOperationRoot(method, cancellationToken);
        if (root == null)
            return new EffectMethodNode(
                method,
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.UnsupportedOperation),
                []);
        var scanner = new OperationEffectScanner(this, method, calls, root);
        var graph = TryCreateControlFlowGraph(root, cancellationToken);
        EffectSummary localSummary;
        if (graph == null) {
            localSummary = EffectSummaryDomain.Instance.Join(
                scanner.Scan(root),
                EffectSummaryOperations.Unsupported());
        }
        else {
            localSummary = AnalyzeControlFlowGraph(graph, scanner);
        }
        localSummary = EffectSummaryDomain.Instance.Join(
            localSummary,
            scanner.ScanLexicalControlEffects(root));
        localSummary = EffectSummaryDomain.Instance.Join(
            localSummary,
            ScanConstructorMemberInitializers(
                method,
                scanner,
                cancellationToken));
        if (CanTriggerOwnTypeInitialization(method) &&
            HasPotentialStaticInitialization(method.ContainingType))
            localSummary = EffectSummaryDomain.Instance.Join(
                localSummary,
                EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.UnmodeledCall));
        return new EffectMethodNode(method, localSummary, [.. calls]);
    }

    private EffectSummary ScanConstructorMemberInitializers(
        IMethodSymbol method,
        OperationEffectScanner scanner,
        CancellationToken cancellationToken) {
        var staticInitializers = method.MethodKind == MethodKind.StaticConstructor;
        if (!staticInitializers &&
            method.MethodKind != MethodKind.Constructor)
            return EffectSummary.Empty;

        var summary = EffectSummary.Empty;
        var write = EffectSummaryOperations.Write(
            EffectRegionSet.Create(
                staticInitializers
                    ? EffectRegionId.Static()
                    : EffectRegionId.Receiver));
        foreach (var member in method.ContainingType.GetMembers()
                     .Where(member =>
                         !member.IsImplicitlyDeclared &&
                         IsInitializableMember(member, staticInitializers))
                     .OrderBy(
                         static member => member.MetadataName,
                         StringComparer.Ordinal)) {
            foreach (var syntaxReference in member.DeclaringSyntaxReferences
                         .OrderBy(
                             static reference => reference.SyntaxTree.FilePath,
                             StringComparer.Ordinal)
                         .ThenBy(static reference => reference.Span.Start)) {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var expression = GetInitializerExpression(declaration);
                if (expression == null) continue;
                var model =
                    SharpProof.Frontend.Host.CompilationModelProvider
                        .GetSemanticModel(_compilation, expression.SyntaxTree);
                var operation = model.GetOperation(expression, cancellationToken);
                summary = EffectSummaryDomain.Instance.Join(
                    summary,
                    operation == null
                        ? EffectSummaryOperations.Unsupported()
                        : EffectSummaryOperations.Join(
                            scanner.Scan(operation),
                            write));
            }
        }
        return summary;
    }

    private bool HasPotentialStaticInitialization(INamedTypeSymbol type) {
        foreach (var constructor in type.StaticConstructors)
            if (!constructor.IsImplicitlyDeclared)
                return true;
        return type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: true) &&
            member.DeclaringSyntaxReferences.Any(reference =>
                GetInitializerExpression(reference.GetSyntax()) != null));
    }

    private static bool CanTriggerOwnTypeInitialization(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Constructor ||
        method.IsStatic &&
        method.MethodKind != MethodKind.StaticConstructor;

    private static bool IsInitializableMember(
        ISymbol member,
        bool staticInitializers) =>
        member switch {
            IFieldSymbol field =>
                !field.IsConst &&
                field.IsStatic == staticInitializers,
            IPropertySymbol property =>
                property.IsStatic == staticInitializers,
            IEventSymbol @event =>
                @event.IsStatic == staticInitializers,
            _ => false
        };

    private static SyntaxNode? GetInitializerExpression(SyntaxNode declaration) =>
        declaration switch {
            Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax variable =>
                variable.Initializer?.Value,
            Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax property =>
                property.Initializer?.Value,
            _ => null
        };

    private static EffectSummary AnalyzeControlFlowGraph(
        ControlFlowGraph graph,
        OperationEffectScanner scanner) {
        var blocks = new List<DataflowBlock<EffectSummary>>(graph.Blocks.Length);
        var edges = new List<DataflowEdge>();
        foreach (var block in graph.Blocks) {
            var blockSummary = EffectSummary.Empty;
            if (block.IsReachable) {
                foreach (var operation in block.Operations)
                    blockSummary = EffectSummaryDomain.Instance.Join(
                        blockSummary,
                        scanner.Scan(operation));
                if (block.BranchValue != null)
                    blockSummary = EffectSummaryDomain.Instance.Join(
                        blockSummary,
                        scanner.Scan(block.BranchValue));
                blockSummary = EffectSummaryDomain.Instance.Join(
                    blockSummary,
                    scanner.ScanExceptionalBranch(
                        block.FallThroughSuccessor,
                        block.BranchValue));
                blockSummary = EffectSummaryDomain.Instance.Join(
                    blockSummary,
                    scanner.ScanExceptionalBranch(
                        block.ConditionalSuccessor,
                        block.BranchValue));
            }
            var captured = blockSummary;
            blocks.Add(new DataflowBlock<EffectSummary>(
                block.Ordinal,
                input => input.IsBottom
                    ? EffectSummary.Bottom
                    : EffectSummaryDomain.Instance.Join(input, captured)));
            AddEdge(block.Ordinal, block.FallThroughSuccessor?.Destination, edges);
            AddEdge(block.Ordinal, block.ConditionalSuccessor?.Destination, edges);
        }
        var dataflowGraph = new DataflowGraph<EffectSummary>(
            blocks,
            edges,
            graph.Blocks[0].Ordinal);
        var result = ForwardDataflowAnalysis.Analyze(
            dataflowGraph,
            EffectSummaryDomain.Instance,
            EffectSummary.Empty);
        var summary = EffectSummary.Empty;
        foreach (var output in result.OutputStates)
            summary = EffectSummaryDomain.Instance.Join(summary, output);
        if (graph.Blocks.Any(block =>
                block.IsReachable &&
                dataflowGraph.IsCyclicBlock(block.Ordinal)))
            summary = EffectSummaryDomain.Instance.Join(
                summary,
                EffectSummaryOperations.MayDiverge());
        return summary;
    }

    private static void AddEdge(
        int source,
        BasicBlock? destination,
        List<DataflowEdge> edges) {
        if (destination != null)
            edges.Add(new DataflowEdge(source, destination.Ordinal));
    }

    private IOperation? GetOperationRoot(
        IMethodSymbol method,
        CancellationToken cancellationToken) {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences
                     .OrderBy(static reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(static reference => reference.Span.Start)) {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var model =
                SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(_compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            if (operation is IMethodBodyOperation or
                IConstructorBodyOperation or
                IBlockOperation)
                return operation;
            foreach (var descendant in syntax.DescendantNodes()) {
                operation = model.GetOperation(descendant, cancellationToken);
                if (operation is IMethodBodyOperation or
                    IConstructorBodyOperation)
                    return operation;
            }
        }
        return null;
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        IOperation root,
        CancellationToken cancellationToken) {
        try {
            return root switch {
                IMethodBodyOperation method =>
                    ControlFlowGraph.Create(method, cancellationToken),
                IConstructorBodyOperation constructor =>
                    ControlFlowGraph.Create(constructor, cancellationToken),
                IBlockOperation { Parent: null } block =>
                    ControlFlowGraph.Create(block, cancellationToken),
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
        var index = 0;
        var indices = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var lowLinks = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var stack = new Stack<IMethodSymbol>();
        var onStack = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var recursive = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        void Visit(IMethodSymbol method) {
            cancellationToken.ThrowIfCancellationRequested();
            indices.Add(method, index);
            lowLinks.Add(method, index);
            index++;
            stack.Push(method);
            onStack.Add(method);
            foreach (var target in nodes[method].Calls
                         .Select(static call => call.Target)
                         .Where(nodes.ContainsKey)
                         .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                         .OrderBy(static target => target, EffectMethodComparer.Instance)) {
                if (!indices.ContainsKey(target)) {
                    Visit(target);
                    lowLinks[method] = Math.Min(lowLinks[method], lowLinks[target]);
                }
                else if (onStack.Contains(target)) {
                    lowLinks[method] = Math.Min(lowLinks[method], indices[target]);
                }
            }
            if (lowLinks[method] != indices[method]) return;
            var component = new List<IMethodSymbol>();
            IMethodSymbol current;
            do {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            } while (!SymbolEqualityComparer.Default.Equals(current, method));
            if (component.Count > 1 ||
                nodes[method].Calls.Any(call =>
                    SymbolEqualityComparer.Default.Equals(call.Target, method)))
                foreach (var member in component)
                    recursive.Add(member);
        }

        foreach (var method in nodes.Keys.OrderBy(
                     static method => method,
                     EffectMethodComparer.Instance))
            if (!indices.ContainsKey(method))
                Visit(method);
        return recursive;
    }

    private bool IsSourceMethod(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Length != 0 &&
        SymbolEqualityComparer.Default.Equals(
            method.ContainingAssembly,
            _compilation.Assembly);

    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method) {
        var normalized = method.ReducedFrom ?? method;
        normalized = normalized.PartialImplementationPart ?? normalized;
        return normalized.OriginalDefinition;
    }
}

internal sealed class EffectMethodNode {
    internal EffectMethodNode(
        IMethodSymbol method,
        EffectSummary localSummary,
        ImmutableArray<EffectCallSite> calls) {
        Method = method;
        LocalSummary = localSummary;
        Calls = calls;
    }

    internal IMethodSymbol Method { get; }
    internal EffectSummary LocalSummary { get; }
    internal ImmutableArray<EffectCallSite> Calls { get; }
}

internal sealed class EffectMethodComparer : IComparer<IMethodSymbol> {
    internal static EffectMethodComparer Instance { get; } = new();

    private EffectMethodComparer() {
    }

    public int Compare(IMethodSymbol? left, IMethodSymbol? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var result = EffectNamedTypeComparer.Instance.Compare(
            left.ContainingType,
            right.ContainingType);
        if (result != 0) return result;
        result = string.Compare(left.MetadataName, right.MetadataName, StringComparison.Ordinal);
        if (result != 0) return result;
        result = left.Arity.CompareTo(right.Arity);
        if (result != 0) return result;
        result = left.Parameters.Length.CompareTo(right.Parameters.Length);
        if (result != 0) return result;
        for (var index = 0; index < left.Parameters.Length; index++) {
            result = left.Parameters[index].RefKind.CompareTo(right.Parameters[index].RefKind);
            if (result != 0) return result;
            result = CompareType(left.Parameters[index].Type, right.Parameters[index].Type);
            if (result != 0) return result;
        }
        var leftLocation = left.Locations.FirstOrDefault(static location => location.IsInSource);
        var rightLocation = right.Locations.FirstOrDefault(static location => location.IsInSource);
        result = string.Compare(
            leftLocation?.SourceTree?.FilePath,
            rightLocation?.SourceTree?.FilePath,
            StringComparison.Ordinal);
        return result != 0
            ? result
            : (leftLocation?.SourceSpan.Start ?? -1)
                .CompareTo(rightLocation?.SourceSpan.Start ?? -1);
    }

    private static int CompareType(ITypeSymbol left, ITypeSymbol right) {
        if (left is INamedTypeSymbol leftNamed &&
            right is INamedTypeSymbol rightNamed)
            return EffectNamedTypeComparer.Instance.Compare(leftNamed, rightNamed);
        if (left is IArrayTypeSymbol leftArray &&
            right is IArrayTypeSymbol rightArray) {
            var rank = leftArray.Rank.CompareTo(rightArray.Rank);
            return rank != 0
                ? rank
                : CompareType(leftArray.ElementType, rightArray.ElementType);
        }
        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter) {
            var kind = leftParameter.TypeParameterKind.CompareTo(
                rightParameter.TypeParameterKind);
            return kind != 0
                ? kind
                : leftParameter.Ordinal.CompareTo(rightParameter.Ordinal);
        }
        var result = left.TypeKind.CompareTo(right.TypeKind);
        return result != 0
            ? result
            : string.Compare(left.MetadataName, right.MetadataName, StringComparison.Ordinal);
    }
}

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Roslyn;

namespace SharpProof.Effects;

/// <summary>
/// Lowers one source method into the local node consumed by the effect call graph.
/// </summary>
internal sealed class EffectMethodNodeBuilder
{
    private static readonly ConditionalWeakTable<
        Compilation,
        Lazy<IReadOnlyDictionary<SyntaxTree, int>>>
        SyntaxTreeOrders = new();
    private readonly EffectAnalysisSession _session;
    private readonly Compilation _compilation;
    private readonly ManagedAbstractFlow _managedFlow;
    private readonly Dictionary<INamedTypeSymbol, EffectBeforeFieldInitNode>
        _beforeFieldInitNodes = new(SymbolEqualityComparer.Default);

    internal EffectMethodNodeBuilder(
        EffectAnalysisSession session,
        Compilation compilation,
        ManagedAbstractFlow managedFlow)
    {
        _session = session;
        _compilation = compilation;
        _managedFlow = managedFlow;
    }

    internal EffectMethodNode Build(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var calls = new List<EffectCallSite>();
        var root = GetOperationRoot(method, cancellationToken);
        if (root == null)
        {
            return new EffectMethodNode(EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnsupportedOperation), [], []);
        }
        if (TryBuildNonCompletingStaticInitialization(
                method,
                cancellationToken,
                out var initialization))
        {
            return initialization;
        }

        var graph = TryCreateControlFlowGraph(root, cancellationToken);
        var abstractAnalysis = graph == null
            ? null
            : _managedFlow.Analyze(
                method,
                graph,
                entryState: null,
                cancellationToken);
        var scanner = new OperationEffectScanner(
            _session, method, calls, root, abstractAnalysis?.Result,
            allowDirectWitnesses:
                graph != null &&
                HasDefiniteBodyEntry(method, _session.ApiSpecs),
            cancellationToken);
        var preBodyInitializers = method.MethodKind ==
            MethodKind.StaticConstructor
                ? ScanConstructorMemberInitializers(
                    method,
                    scanner,
                    cancellationToken)
                : EffectStep.Empty;
        var constructorPlan = CreateConstructorInitializationPlan(
            method,
            root,
            scanner,
            cancellationToken);
        EnsureBeforeFieldInitNode(method, cancellationToken);
        var localSummary = preBodyInitializers.Summary;
        if (preBodyInitializers.CompletesNormally)
        {
            var bodyAnalysis = graph == null
                ? AnalyzeWithoutControlFlowGraph(
                    root,
                    scanner,
                    constructorPlan)
                : AnalyzeControlFlowGraph(
                    graph,
                    scanner,
                    constructorPlan);
            var bodySummary = bodyAnalysis.Summary;

            // Cyclic scalar flow does not invalidate the conservative
            // all-block effect scan.
            if (bodyAnalysis.BodyEntryReached &&
                abstractAnalysis is
                {
                    IsComplete: false,
                    IncompleteReason: not
                        EffectAnalysisIncompleteReason.CyclicControlFlow
                })
            {
                bodySummary = EffectSummaryOperations.Join(
                    bodySummary,
                    EffectSummaryOperations.IncompleteAnalysis(
                        abstractAnalysis.IncompleteReason));
            }

            var lexicalRoot = bodyAnalysis.BodyEntryReached ||
                constructorPlan == null
                    ? root
                    : constructorPlan.Value.Initializer;
            localSummary = EffectSummaryOperations.Join(
                localSummary,
                bodySummary,
                lexicalRoot == null
                    ? EffectSummary.Empty
                    : scanner.ScanLexicalControlEffects(lexicalRoot),
                bodyAnalysis.BodyEntryReached
                    ? scanner.ScanUsingDisposalEffects(root)
                    : EffectSummary.Empty);
        }

        localSummary = EffectSummaryOperations.Join(
            localSummary,
            _session.ResolveEntryPreconditions(method),
            CanTriggerOwnTypeInitialization(method) &&
            (!SymbolEqualityComparer.Default.Equals(
                method.ContainingAssembly,
                _compilation.Assembly)
                ? true
                : method.MethodKind == MethodKind.Constructor
                    ? method.ContainingType.StaticConstructors.All(
                        static constructor => constructor.IsImplicitlyDeclared)
                    : !method.ContainingType.IsGenericType &&
                      method.ContainingType.StaticConstructors.Any(
                          constructor =>
                              !constructor.IsImplicitlyDeclared &&
                              StaticConstructorCanAffectEntry(constructor))) &&
            HasPotentialStaticInitialization(
                method.ContainingType,
                _session.ApiSpecs,
                cancellationToken)
                ? EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall)
                : EffectSummary.Empty);
        return new EffectMethodNode(localSummary, [.. calls], scanner.DirectWitnesses);
    }

    private ConstructorInitializationPlan? CreateConstructorInitializationPlan(
        IMethodSymbol method,
        IOperation root,
        OperationEffectScanner scanner,
        CancellationToken cancellationToken)
    {
        if (method.MethodKind != MethodKind.Constructor ||
            root is not IConstructorBodyOperation constructorBody)
        {
            return null;
        }

        var invocation = GetConstructorInitializerInvocation(constructorBody);
        var delegatesToThis = invocation != null &&
            SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.ContainingType.OriginalDefinition,
                method.ContainingType.OriginalDefinition);
        return new ConstructorInitializationPlan(
            constructorBody.Initializer,
            delegatesToThis
                ? static () => EffectStep.Empty
                : () => ScanMemberInitializers(
                    method,
                    scanner,
                    staticInitializers: false,
                    cancellationToken));
    }

    internal static IInvocationOperation? GetConstructorInitializerInvocation(
        IConstructorBodyOperation body)
    {
        return body.Initializer?.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .FirstOrDefault(static invocation =>
                invocation.TargetMethod.MethodKind == MethodKind.Constructor);
    }

    private bool TryBuildNonCompletingStaticInitialization(
        IMethodSymbol method,
        CancellationToken cancellationToken,
        out EffectMethodNode initialization)
    {
        initialization = default;
        if (!method.IsStatic ||
            method.MethodKind == MethodKind.StaticConstructor ||
            method.ContainingType.IsGenericType ||
            !SymbolEqualityComparer.Default.Equals(
                method.ContainingAssembly,
                _compilation.Assembly))
        {
            return false;
        }

        var constructor = method.ContainingType.StaticConstructors
            .FirstOrDefault(static candidate =>
                !candidate.IsImplicitlyDeclared &&
                candidate.DeclaringSyntaxReferences.Length != 0);
        if (constructor == null ||
            HasLexicalThrow(constructor) ||
            new DefiniteOperationFacts(
                _compilation,
                cancellationToken).MethodCanCompleteNormally(constructor))
        {
            return false;
        }

        var constructorNode = Build(constructor, cancellationToken);
        initialization = new EffectMethodNode(
            _session.WrapTypeInitializationFailures(
                constructorNode.LocalSummary),
            constructorNode.Calls,
            []);
        return true;
    }

    private EffectStep ScanConstructorMemberInitializers(
        IMethodSymbol method,
        OperationEffectScanner scanner,
        CancellationToken cancellationToken)
    {
        var staticInitializers = method.MethodKind == MethodKind.StaticConstructor;
        if (!staticInitializers && method.MethodKind != MethodKind.Constructor)
        {
            return EffectStep.Empty;
        }

        return ScanMemberInitializers(
            method,
            scanner,
            staticInitializers,
            cancellationToken);
    }

    private void EnsureBeforeFieldInitNode(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        if (!CanTriggerBeforeFieldInitInitialization(method))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_beforeFieldInitNodes.ContainsKey(method.ContainingType) ||
            !HasPotentialStaticInitialization(
                method.ContainingType,
                _session.ApiSpecs,
                cancellationToken))
        {
            return;
        }

        var initializer = method.ContainingType.StaticConstructors
            .SingleOrDefault(static constructor =>
                constructor.IsImplicitlyDeclared);
        if (initializer == null)
        {
            _beforeFieldInitNodes.Add(
                method.ContainingType,
                new EffectBeforeFieldInitNode(
                    EffectSummaryOperations.UnknownBoundary(
                        EffectUncertainty.UnsupportedOperation),
                    []));
            return;
        }

        var calls = new List<EffectCallSite>();
        var result = EffectStep.Empty;
        var write = EffectSummaryOperations.Write(EffectRegionSet.Create(
            EffectRegionId.Static()));
        foreach (var operation in GetMemberInitializerOperations(
                     _compilation,
                     method.ContainingType,
                     staticInitializers: true,
                     cancellationToken))
        {
            if (operation == null)
            {
                result = result.Then(new EffectStep(
                    EffectSummaryOperations.Unsupported(),
                    true));
                continue;
            }

            var scanner = new OperationEffectScanner(
                _session,
                initializer,
                calls,
                operation,
                abstractFlow: null,
                allowDirectWitnesses: false,
                cancellationToken);
            result = result.Then(scanner.ScanSequence([operation]));
            if (!result.CompletesNormally)
            {
                break;
            }

            result = result.Then(new EffectStep(write, true));
        }

        _beforeFieldInitNodes.Add(
            method.ContainingType,
            new EffectBeforeFieldInitNode(
                _session.WrapTypeInitializationFailures(result.Summary),
                [.. calls]));
    }

    private EffectStep ScanMemberInitializers(
        IMethodSymbol method,
        OperationEffectScanner scanner,
        bool staticInitializers,
        CancellationToken cancellationToken)
    {

        var result = EffectStep.Empty;
        var write = EffectSummaryOperations.Write(EffectRegionSet.Create(
            staticInitializers ? EffectRegionId.Static() : EffectRegionId.Receiver));
        foreach (var operation in GetMemberInitializerOperations(
                     _compilation,
                     method.ContainingType,
                     staticInitializers,
                     cancellationToken))
        {
            if (operation == null)
            {
                result = result.Then(new EffectStep(
                    EffectSummaryOperations.Unsupported(),
                    true));
                continue;
            }

            result = result.Then(scanner.ScanSequence([operation]));
            if (!result.CompletesNormally)
            {
                break;
            }

            result = result.Then(new EffectStep(write, true));
        }

        return result;
    }

    internal bool TryGetBeforeFieldInitNode(
        IMethodSymbol method,
        out EffectBeforeFieldInitNode node)
    {
        if (!CanTriggerBeforeFieldInitInitialization(method))
        {
            node = default;
            return false;
        }

        return _beforeFieldInitNodes.TryGetValue(
            method.ContainingType,
            out node);
    }

    internal static IEnumerable<SyntaxReference>
        GetMemberInitializerReferences(
        Compilation compilation,
        INamedTypeSymbol type,
        bool staticInitializers)
    {
        var syntaxTreeOrder = GetSyntaxTreeOrder(compilation);
        return type.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared &&
                IsInitializableMember(member, staticInitializers))
            .SelectMany(static member => member.DeclaringSyntaxReferences)
            .OrderBy(reference => syntaxTreeOrder.TryGetValue(
                    reference.SyntaxTree,
                    out var ordinal)
                ? ordinal
                : int.MaxValue)
            .ThenBy(static reference => reference.Span.Start);
    }

    private static IReadOnlyDictionary<SyntaxTree, int> GetSyntaxTreeOrder(
        Compilation compilation)
    {
        return SyntaxTreeOrders.GetValue(
            compilation,
            static value => new(
                () => value.SyntaxTrees
                    .Select(static (tree, ordinal) => (tree, ordinal))
                    .ToDictionary(
                        static item => item.tree,
                        static item => item.ordinal),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    internal static IEnumerable<IOperation?> GetMemberInitializerOperations(
        Compilation compilation,
        INamedTypeSymbol type,
        bool staticInitializers,
        CancellationToken cancellationToken)
    {
        foreach (var reference in GetMemberInitializerReferences(
                     compilation,
                     type,
                     staticInitializers))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expression = EffectProjections.GetInitializerExpression(
                reference.GetSyntax(cancellationToken));
            if (expression == null)
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, expression.SyntaxTree);
            yield return model.GetOperation(expression, cancellationToken);
        }
    }

    internal static bool HasPotentialStaticInitialization(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs)
    {
        return HasPotentialStaticInitialization(
            type,
            apiSpecs,
            CancellationToken.None);
    }

    internal static bool AllStaticInitializersSatisfy(
        INamedTypeSymbol type,
        Compilation compilation,
        Func<IOperation, bool> predicate)
    {
        foreach (var member in type.GetMembers())
        {
            if (!IsInitializableMember(member, staticInitializers: true))
            {
                continue;
            }

            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                var expression = EffectProjections.GetInitializerExpression(
                    reference.GetSyntax());
                if (expression != null &&
                    SharpProof.Frontend.Host.CompilationModelProvider
                        .GetSemanticModel(compilation, expression.SyntaxTree)
                        .GetOperation(expression) is { } operation &&
                    !predicate(operation))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool HasPotentialStaticInitialization(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        if (type.SpecialType == SpecialType.System_Object &&
            HasApprovedSystemObjectConstructor(
                type,
                apiSpecs,
                cancellationToken))
        {
            return false;
        }

        if (type.DeclaringSyntaxReferences.Length == 0)
        {
            return true;
        }

        foreach (var constructor in type.StaticConstructors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (constructor.DeclaringSyntaxReferences.Length != 0)
            {
                return true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.IsImplicitlyDeclared ||
                !IsInitializableMember(member, staticInitializers: true))
            {
                continue;
            }

            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (EffectProjections.GetInitializerExpression(
                        reference.GetSyntax(cancellationToken)) != null)
                {
                    return true;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    internal static bool HasPotentialConstructionInitialization(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs)
    {
        return HasPotentialConstructionInitialization(
            type,
            apiSpecs,
            CancellationToken.None);
    }

    internal static bool HasPotentialConstructionInitialization(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs,
        CancellationToken cancellationToken)
    {
        const int maximumBaseTypeDepth = 256;
        var seen = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        INamedTypeSymbol? current = type;
        for (var depth = 0; current != null; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth >= maximumBaseTypeDepth ||
                current.TypeKind == TypeKind.Error ||
                !seen.Add(current.OriginalDefinition) ||
                HasPotentialStaticInitialization(
                    current,
                    apiSpecs,
                    cancellationToken))
            {
                return true;
            }

            current = current.BaseType;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    internal static bool IsProvablyEmptyImplicitConstructorLayer(
        IMethodSymbol method,
        ResolvedApiSpecTable apiSpecs)
    {
        var type = method.ContainingType;
        return method.MethodKind == MethodKind.Constructor &&
        method.IsImplicitlyDeclared &&
        CanBeImplicitlyInvoked(method.Parameters) &&
        type.DeclaringSyntaxReferences.Length != 0 &&
        !HasPotentialStaticInitialization(type, apiSpecs) &&
        !HasInstanceMemberInitializer(type);
    }

    internal static bool IsSourceImplicitParameterlessConstructor(
        IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        return method is
        {
            MethodKind: MethodKind.Constructor,
            IsImplicitlyDeclared: true,
            Parameters.IsDefaultOrEmpty: true
        } && method.ContainingType.OriginalDefinition
            .DeclaringSyntaxReferences.Length != 0;
    }

    internal static IMethodSymbol? GetUniqueParameterlessBaseConstructor(
        IMethodSymbol constructor)
    {
        var candidates = constructor.ContainingType.BaseType?
            .InstanceConstructors
            .Where(static candidate =>
                CanBeImplicitlyInvoked(candidate.Parameters))
            .ToImmutableArray() ?? [];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool CanBeImplicitlyInvoked(
        ImmutableArray<IParameterSymbol> parameters)
    {
        // The compiler may omit the argument list for an implicit base call
        // when every parameter has a default, including a params parameter.
        return parameters.All(static parameter =>
            parameter.IsOptional || parameter.IsParams);
    }

    private static bool HasInstanceMemberInitializer(INamedTypeSymbol type)
    {
        return type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: false) &&
            member.DeclaringSyntaxReferences.Any(reference =>
                EffectProjections.GetInitializerExpression(
                    reference.GetSyntax()) != null));
    }

    private static bool HasApprovedSystemObjectConstructor(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs,
        CancellationToken cancellationToken)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (constructor.Parameters.IsDefaultOrEmpty &&
                apiSpecs.TryGet(constructor, out var spec) &&
                spec.Template.Target.WitnessIdentifier ==
                "bcl.object.ctor")
            {
                return true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private static bool HasDefiniteBodyEntry(
        IMethodSymbol method,
        ResolvedApiSpecTable apiSpecs)
    {
        return method.MethodKind is not (
            MethodKind.Constructor or MethodKind.StaticConstructor) &&
        (!CanTriggerOwnTypeInitialization(method) ||
         !HasPotentialStaticInitialization(
             method.ContainingType,
             apiSpecs));
    }

    internal static bool CanTriggerOwnTypeInitialization(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Constructor ||
        method.MethodKind != MethodKind.StaticConstructor &&
        (method.IsStatic || method.ContainingType.IsValueType);
    }

    internal static bool CanTriggerBeforeFieldInitInitialization(
        IMethodSymbol method)
    {
        return method.IsStatic &&
            method.MethodKind != MethodKind.StaticConstructor &&
            method.ContainingType.StaticConstructors.All(
                static constructor => constructor.IsImplicitlyDeclared);
    }

    private bool StaticConstructorCanAffectEntry(
        IMethodSymbol constructor)
    {
        if (HasLexicalThrow(constructor))
        {
            return true;
        }
        return constructor.DeclaringSyntaxReferences.Length == 0 ||
            new DefiniteOperationFacts(
                _compilation,
                CancellationToken.None).MethodCanCompleteNormally(
                    constructor);
    }

    private static bool HasLexicalThrow(IMethodSymbol constructor)
    {
        return constructor.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax().DescendantNodesAndSelf().Any(
                static syntax => syntax is ThrowStatementSyntax or
                    ThrowExpressionSyntax));
    }

    private static bool IsInitializableMember(
        ISymbol member,
        bool staticInitializers)
    {
        return member switch
        {
            IFieldSymbol field => !field.IsConst &&
                field.IsStatic == staticInitializers,
            IPropertySymbol property => property.IsStatic == staticInitializers,
            IEventSymbol @event => @event.IsStatic == staticInitializers,
            _ => false
        };
    }

    private static MethodBodyAnalysis AnalyzeWithoutControlFlowGraph(
        IOperation root,
        OperationEffectScanner scanner,
        ConstructorInitializationPlan? constructorPlan)
    {
        if (constructorPlan is not { } plan ||
            root is not IConstructorBodyOperation constructorBody)
        {
            return new MethodBodyAnalysis(
                EffectSummaryOperations.Join(
                    scanner.Scan(root),
                    EffectSummaryOperations.Unsupported()),
                BodyEntryReached: true);
        }

        var step = plan.Initializer == null
            ? EffectStep.Empty
            : scanner.ScanSequence([plan.Initializer]);
        if (step.CompletesNormally)
        {
            step = step.Then(plan.ScanMemberInitializers());
        }
        var bodyEntryReached = step.CompletesNormally;
        var body = (IOperation?)constructorBody.BlockBody ??
            constructorBody.ExpressionBody;
        if (bodyEntryReached && body != null)
        {
            step = step.Then(scanner.ScanSequence([body]));
        }

        return new MethodBodyAnalysis(
            EffectSummaryOperations.Join(
                step.Summary,
                EffectSummaryOperations.Unsupported()),
            bodyEntryReached);
    }

    private static MethodBodyAnalysis AnalyzeControlFlowGraph(
        ControlFlowGraph graph,
        OperationEffectScanner scanner,
        ConstructorInitializationPlan? constructorPlan)
    {
        var summary = EffectSummary.Empty;
        var pending = new SortedSet<int> { graph.Blocks[0].Ordinal };
        var constructorInitializersScanned = false;
        var bodyEntryReached = constructorPlan == null;
        var exceptionalEntriesAdded = false;
        var regions = graph.Blocks
            .SelectMany(static block => EnclosingRegions(block.EnclosingRegion))
            .Distinct()
            .ToArray();
        var exceptionalRegionOperations =
            CreateExceptionalRegionOperations(graph, regions);
        var finallyEntries = CreateFinallyEntries(graph, regions);
        if (constructorPlan == null)
        {
            AddExceptionalEntries();
        }

        var visited = new HashSet<int>();
        while (pending.Count != 0)
        {
            var ordinal = pending.Min;
            pending.Remove(ordinal);
            if (!visited.Add(ordinal))
            {
                continue;
            }

            var block = graph.Blocks[ordinal];
            var step = EffectStep.Empty;
            if (block.Ordinal == graph.Blocks[0].Ordinal &&
                constructorPlan is { Initializer: null } entryPlan)
            {
                step = ApplyConstructorInitializers(step, entryPlan);
            }
            foreach (var operation in block.Operations.Where(
                         scanner.IsReachable))
            {
                step = ScanOperation(step, operation);
                if (!step.CompletesNormally)
                {
                    break;
                }
            }
            if (step.CompletesNormally &&
                block.BranchValue != null &&
                scanner.IsReachable(block.BranchValue))
            {
                step = ScanOperation(step, block.BranchValue);
            }

            summary = EffectSummaryOperations.Join(summary, step.Summary);
            if (!step.Summary.Throws.IsEmpty)
            {
                AddReachableFinallyEntriesForBlock(block);
            }
            AddControlTransferFinally(block, block.FallThroughSuccessor, step);
            AddControlTransferFinally(block, block.ConditionalSuccessor, step);
            if (!step.CompletesNormally)
            {
                continue;
            }

            AddRegularSuccessor(block.FallThroughSuccessor);
            AddRegularSuccessor(block.ConditionalSuccessor);
        }

        var result = ManagedAbstractFlow.IsAcyclic(graph, visited)
            ? summary
            : EffectSummaryOperations.Join(
                summary,
                EffectSummaryOperations.MayDiverge());
        return new MethodBodyAnalysis(result, bodyEntryReached);

        EffectStep ScanOperation(
            EffectStep current,
            IOperation operation)
        {
            current = current.Then(scanner.ScanSequence([operation]));
            if (!current.CompletesNormally ||
                constructorInitializersScanned ||
                constructorPlan is not { } plan ||
                plan.Initializer == null ||
                !ManagedFlowResult.HasSameIdentity(
                    operation,
                    plan.Initializer))
            {
                return current;
            }

            return ApplyConstructorInitializers(current, plan);
        }

        EffectStep ApplyConstructorInitializers(
            EffectStep current,
            ConstructorInitializationPlan plan)
        {
            constructorInitializersScanned = true;
            current = current.Then(plan.ScanMemberInitializers());
            bodyEntryReached = current.CompletesNormally;
            if (bodyEntryReached)
            {
                AddExceptionalEntries();
            }
            return current;
        }

        void AddExceptionalEntries()
        {
            if (exceptionalEntriesAdded)
            {
                return;
            }

            exceptionalEntriesAdded = true;
            foreach (var candidate in graph.Blocks.Where(static block =>
                         block.Predecessors.All(static predecessor =>
                             predecessor.Semantics !=
                                 ControlFlowBranchSemantics.Regular)))
            {
                if (IsExceptionalEntryReachable(candidate))
                {
                    pending.Add(candidate.Ordinal);
                }
            }
        }

        void AddRegularSuccessor(ControlFlowBranch? branch)
        {
            if (branch is
                {
                    Semantics: ControlFlowBranchSemantics.Regular,
                    Destination: { IsReachable: true } destination
                } && LeavingFinallysMayComplete(branch))
            {
                pending.Add(destination.Ordinal);
            }
        }

        bool LeavingFinallysMayComplete(ControlFlowBranch branch)
        {
            foreach (var region in branch.LeavingRegions)
            {
                if (finallyEntries.TryGetValue(region, out var entry) &&
                    entry.Operation is { } operation &&
                    !scanner.CanCompleteNormally(operation))
                {
                    return false;
                }
            }
            return true;
        }

        void AddControlTransferFinally(
            BasicBlock source,
            ControlFlowBranch? branch,
            EffectStep step)
        {
            if (branch == null ||
                !step.CompletesNormally &&
                branch.Semantics is not (
                    ControlFlowBranchSemantics.Throw or
                    ControlFlowBranchSemantics.Rethrow))
            {
                return;
            }
            if (branch.Semantics is
                ControlFlowBranchSemantics.Throw or
                ControlFlowBranchSemantics.Rethrow)
            {
                AddReachableFinallyEntriesForBlock(source);
                return;
            }
            AddReachableFinallyEntries(branch);
        }

        void AddReachableFinallyEntries(ControlFlowBranch branch)
        {
            foreach (var region in branch.LeavingRegions)
            {
                if (finallyEntries.TryGetValue(region, out var entry))
                {
                    pending.Add(entry.EntryOrdinal);
                    return;
                }
            }
        }

        bool IsExceptionalEntryReachable(BasicBlock block)
        {
            for (var region = block.EnclosingRegion;
                 region != null;
                 region = region.EnclosingRegion)
            {
                if (region.Kind == ControlFlowRegionKind.Finally)
                {
                    return false;
                }
                if (exceptionalRegionOperations.TryGetValue(
                        region,
                        out var operation))
                {
                    return scanner.IsReachable(operation);
                }
            }
            return true;
        }

        void AddReachableFinallyEntriesForBlock(BasicBlock block)
        {
            for (var region = block.EnclosingRegion;
                 region != null;
                 region = region.EnclosingRegion)
            {
                if (finallyEntries.TryGetValue(region, out var entry))
                {
                    pending.Add(entry.EntryOrdinal);
                    return;
                }
            }
        }
    }

    private readonly record struct ConstructorInitializationPlan(
        IOperation? Initializer,
        Func<EffectStep> ScanMemberInitializers);

    private readonly record struct MethodBodyAnalysis(
        EffectSummary Summary,
        bool BodyEntryReached);

    private readonly record struct FinallyEntry(
        int EntryOrdinal,
        IOperation? Operation);

    private static Dictionary<ControlFlowRegion, FinallyEntry> CreateFinallyEntries(
        ControlFlowGraph graph,
        ControlFlowRegion[] regions)
    {
        var finallyRegions = regions
            .Where(static region =>
                region.Kind == ControlFlowRegionKind.Finally)
            .OrderBy(static region => region.FirstBlockOrdinal)
            .ToArray();
        var finallyOperations = graph.OriginalOperation.DescendantsAndSelf()
            .OfType<ITryOperation>()
            .Where(@try =>
                @try.Finally != null &&
                !ConversionOwnershipClassifier.IsInsideNestedCallable(
                    @try,
                    graph.OriginalOperation))
            .OrderBy(static @try => @try.Finally!.Syntax.SpanStart)
            .Select(static @try => (IOperation)@try.Finally!)
            .ToArray();
        var operationByRegion = new Dictionary<ControlFlowRegion, IOperation>();
        if (finallyRegions.Length == finallyOperations.Length)
        {
            for (var index = 0; index < finallyRegions.Length; index++)
            {
                operationByRegion.Add(
                    finallyRegions[index],
                    finallyOperations[index]);
            }
        }
        var finallyByEnclosingRegion =
            new Dictionary<ControlFlowRegion, ControlFlowRegion>();
        ControlFlowRegion? rootFinallyRegion = null;
        foreach (var region in regions)
        {
            if (region.Kind != ControlFlowRegionKind.Finally)
            {
                continue;
            }

            if (region.EnclosingRegion is { } parent)
            {
                if (!finallyByEnclosingRegion.ContainsKey(parent))
                {
                    finallyByEnclosingRegion.Add(parent, region);
                }
            }
            else
            {
                rootFinallyRegion ??= region;
            }
        }
        var result = new Dictionary<ControlFlowRegion, FinallyEntry>();
        foreach (var tryRegion in regions.Where(static region =>
                     region.Kind == ControlFlowRegionKind.Try))
        {
            ControlFlowRegion? finallyRegion;
            if (tryRegion.EnclosingRegion is { } parent)
            {
                finallyRegion = finallyByEnclosingRegion.TryGetValue(
                        parent,
                        out var indexedFinally)
                    ? indexedFinally
                    : null;
            }
            else
            {
                finallyRegion = rootFinallyRegion;
            }
            if (finallyRegion != null)
            {
                result.Add(
                    tryRegion,
                    new FinallyEntry(
                        finallyRegion.FirstBlockOrdinal,
                        operationByRegion.TryGetValue(
                            finallyRegion,
                            out var operation)
                                ? operation
                                : null));
            }
        }
        return result;
    }

    private static Dictionary<ControlFlowRegion, IOperation>
        CreateExceptionalRegionOperations(
            ControlFlowGraph graph,
            ControlFlowRegion[] regions)
    {
        var catches = graph.OriginalOperation.DescendantsAndSelf()
            .OfType<ICatchClauseOperation>()
            .Where(@catch =>
                !ConversionOwnershipClassifier.IsInsideNestedCallable(
                    @catch,
                    graph.OriginalOperation))
            .OrderBy(static @catch => @catch.Syntax.SpanStart)
            .ToArray();
        var result = new Dictionary<ControlFlowRegion, IOperation>();
        AddMappings(
            regions.Where(static region =>
                    region.Kind == ControlFlowRegionKind.Catch)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray(),
            catches.Select(static @catch => @catch.Handler).ToArray());
        AddMappings(
            regions.Where(static region =>
                    region.Kind == ControlFlowRegionKind.Filter)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray(),
            catches.Where(static @catch => @catch.Filter != null)
                .Select(static @catch => @catch.Filter!)
                .ToArray());
        return result;

        void AddMappings(
            ControlFlowRegion[] candidates,
            IOperation[] operations)
        {
            if (candidates.Length != operations.Length)
            {
                return;
            }
            for (var index = 0; index < candidates.Length; index++)
            {
                result.Add(candidates[index], operations[index]);
            }
        }

    }

    private static IEnumerable<ControlFlowRegion> EnclosingRegions(
        ControlFlowRegion? region)
    {
        for (; region != null; region = region.EnclosingRegion)
        {
            yield return region;
        }
    }

    private IOperation? GetOperationRoot(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences
                     .OrderBy(
                         static reference => reference.SyntaxTree.FilePath,
                         StringComparer.Ordinal)
                     .ThenBy(static reference => reference.Span.Start))
        {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            if (operation is
                IMethodBodyOperation or
                IConstructorBodyOperation or
                IBlockOperation)
            {
                return operation;
            }

            foreach (var node in syntax.DescendantNodes())
            {
                operation = model.GetOperation(node, cancellationToken);
                if (operation is IMethodBodyOperation or IConstructorBodyOperation)
                {
                    return operation;
                }
            }
        }

        return null;
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        IOperation root,
        CancellationToken cancellationToken)
    {
        try
        {
            return root switch
            {
                IMethodBodyOperation or IConstructorBodyOperation =>
                    RoslynCfgFactory.TryCreateMethodOrConstructorGraph(
                        root, cancellationToken),
                IBlockOperation { Parent: null } block =>
                    ControlFlowGraph.Create(block, cancellationToken),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

internal readonly record struct EffectBeforeFieldInitNode(
    EffectSummary LocalSummary,
    ImmutableArray<EffectCallSite> Calls);

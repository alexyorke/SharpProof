using SharpProof.Roslyn;

namespace SharpProof.Analyzer;

internal sealed partial class RequiresCallSiteDiscovery(
    IMethodSymbol caller,
    SyntaxNode declaration,
    SemanticModel semanticModel,
    CancellationToken cancellationToken,
    ControlFlowGraph? suppliedGraph = null,
    IOperation? suppliedOperationRoot = null,
    IOperation? suppliedInitializerOperation = null)
{
    private readonly InvocationEmissionPolicy _invocationEmission =
        new(semanticModel.Compilation);
    private readonly Dictionary<bool,
        (INamedTypeSymbol? Interface, IMethodSymbol? Method)>
        _disposeInterfaceMethodCache = [];

    internal ImmutableHashSet<IMethodSymbol>?
        GetPotentialCallOwners(
            Func<IMethodSymbol, bool>
                hasPotentialPreconditions)
    {
        hasPotentialPreconditions = ArgumentNullGuard.NotNull(
            hasPotentialPreconditions, nameof(hasPotentialPreconditions));

        if (!TryGetOperationRoot(out var operationRoot))
        {
            return null;
        }

        var owners = ImmutableHashSet.CreateBuilder<
            IMethodSymbol>(
            SymbolEqualityComparer.Default);
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        var delegateTargets = GetDirectDelegateTargets(operationRoot);
        foreach (var operation in
                 ExecutableDescendantsAndSelf(operationRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calls = GetCalls(
                operation,
                operationFacts,
                semanticModel,
                delegateTargets,
                cancellationToken: cancellationToken,
                disposeInterfaceMethodCache: _disposeInterfaceMethodCache);
            if (calls.IsDefaultOrEmpty)
            {
                continue;
            }
            foreach (var call in calls)
            {
                var target = RequiresCallSiteDispatch.ResolveExactTarget(
                    call.TargetMethod,
                    call.Instance,
                    cancellationToken);
                if (hasPotentialPreconditions(target))
                {
                    var owner = semanticModel.GetEnclosingSymbol(
                        operation.Syntax.SpanStart,
                        cancellationToken) as IMethodSymbol;
                    if (owner == null)
                    {
                        return null;
                    }

                    owners.Add(
                        ContractClauseInventoryBuilder
                            .NormalizeCallable(owner));
                }
            }
        }

        if (TryGetImplicitBaseConstructor(out var baseConstructor) &&
            hasPotentialPreconditions(baseConstructor))
        {
            owners.Add(
                ContractClauseInventoryBuilder
                    .NormalizeCallable(caller));
        }

        return owners.ToImmutable();
    }

    internal ImmutableArray<RequiresCallSiteCandidate>? Get(
        BoundMethodContracts? callerContracts,
        bool requireCallerOwnership = true)
    {
        if (!TryCreateGraph(out var operationRoot, out var graph))
        {
            return null;
        }

        var managedFlow = ManagedAbstractFlow.ForCompilation(semanticModel.Compilation);
        var entryState = ManagedContractFacts.ApplyRequires(
            managedFlow.CreateEntryState(caller),
            callerContracts);
        var flowAnalysis = managedFlow.Analyze(
            caller,
            graph,
            entryState,
            cancellationToken);
        var flowResult = flowAnalysis.Result;
        var callSites = new List<RequiresCallSiteCandidate>();
        var reachableOperationSites = new HashSet<(
            SyntaxTree Tree, int Start, int Length)>();
        var initializer = (operationRoot as IConstructorBodyOperation)?.Initializer;
        if (TryGetImplicitBaseConstructor(out var baseConstructor))
        {
            var constructorBody = operationRoot as IConstructorBodyOperation;
            var origin = (IOperation?)constructorBody?.BlockBody ??
                constructorBody?.ExpressionBody ??
                operationRoot!;
            callSites.Add(new RequiresCallSiteCandidate(
                origin,
                origin.Syntax,
                baseConstructor,
                Instance: null,
                Arguments: [],
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                CanReplay: true,
                Flow: null,
                ManagedFlowStatus.BudgetExceeded));
        }
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        var blockPrefixCompletionIndices = new Dictionary<
            BlockSyntax,
            BlockPrefixCompletionIndex>(
            ReferenceComparer<BlockSyntax>.Instance);
        var reachableInitializerSites = GetReachableInitializerSites(
            operationFacts);
        var delegateTargets = GetDirectDelegateTargets(operationRoot!);
        OperationEffectScanner? semanticReachability = null;
        foreach (var block in RoslynCfgThrowFacts.ReachableBlocks(
                     graph,
                     cancellationToken))
        {
            var roots = block.Operations
                .Concat(block.BranchValue == null ? [] : [block.BranchValue])
                .Concat(
                    block.Ordinal == graph.Blocks[0].Ordinal &&
                    initializer != null
                        ? [initializer]
                        : []);
            foreach (var operation in roots.SelectMany(
                         ExecutableDescendantsAndSelf))
            {
                reachableOperationSites.Add((
                    operation.Syntax.SyntaxTree,
                    operation.Syntax.SpanStart,
                    operation.Syntax.Span.Length));
                var calls = GetCalls(
                    operation,
                    operationFacts,
                    semanticModel,
                    delegateTargets,
                    flowResult,
                    _disposeInterfaceMethodCache,
                    cancellationToken);
                if (calls.IsDefaultOrEmpty ||
                    reachableInitializerSites != null &&
                    !reachableInitializerSites.Contains((
                        operation.Syntax.SyntaxTree,
                        operation.Syntax.SpanStart,
                        operation.Syntax.Span.Length)) ||
                    requireCallerOwnership &&
                    !SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetEnclosingSymbol(
                            operation.Syntax.SpanStart,
                            cancellationToken),
                        caller))
                {
                    continue;
                }

                var syntacticReplayable = HasReplayablePrefix(
                    operation,
                    operationFacts,
                    flowResult,
                    blockPrefixCompletionIndices);

                var hasFlowState =
                    flowResult?.TryGetState(operation, out _) == true;
                var hasReachableFlowState =
                    flowResult?.IsReachable(operation) == true &&
                    (hasFlowState || operation is IListPatternOperation);
                var allowSyntacticReplay =
                    syntacticReplayable &&
                    !hasFlowState &&
                    (flowResult == null ||
                     flowResult.IsReachable(operation));
                var isInsideExceptionHandler =
                    IsInsideExceptionHandler(operation);
                if (flowAnalysis.IsComplete &&
                    !hasReachableFlowState &&
                    !allowSyntacticReplay &&
                    (!isInsideExceptionHandler ||
                     !(semanticReachability ??=
                         OperationEffectScanner.CreateReachabilityProbe(
                             semanticModel.Compilation,
                             caller,
                             operationRoot!,
                             flowResult)).IsReachable(operation)))
                {
                    continue;
                }

                foreach (var call in calls)
                {
                    if (call.TargetMethod.MethodKind == MethodKind.PropertySet &&
                        operation is IPropertyReferenceOperation property &&
                        property.Parent is ICoalesceAssignmentOperation coalesce &&
                        ReferenceEquals(coalesce.Target, property) &&
                        !CanCoalesceGetterComplete(property, operationFacts))
                    {
                        continue;
                    }
                    var candidate = CreateCandidate(
                        operation,
                        call,
                        call.CanReplay && HasReplayableCallEvaluation(
                            operation,
                            call,
                            operationFacts,
                            flowResult,
                            blockPrefixCompletionIndices),
                        hasFlowState ? flowResult : null,
                        flowAnalysis.Status,
                        cancellationToken);
                    AddOrUpgrade(
                        callSites,
                        candidate,
                        skipDeduplication: operation is IListPatternOperation);
                }
            }
        }

        // Roslyn can omit operations in a finally region from the set of
        // reachable CFG blocks even though the region is entered whenever
        // its containing try statement is entered.  Revisit syntactically
        // definite call sites that were not covered by those blocks.  The
        // replayability proof below remains fail-closed for conditional,
        // short-circuit, and exception-handler paths.
        foreach (var operation in ExecutableDescendantsAndSelf(operationRoot!)
                     .Where(operation => !reachableOperationSites.Contains((
                         operation.Syntax.SyntaxTree,
                         operation.Syntax.SpanStart,
                         operation.Syntax.Span.Length))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calls = GetCalls(
                operation,
                operationFacts,
                semanticModel,
                delegateTargets,
                flowResult,
                _disposeInterfaceMethodCache,
                cancellationToken);
            if (calls.IsDefaultOrEmpty ||
                requireCallerOwnership &&
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetEnclosingSymbol(
                        operation.Syntax.SpanStart,
                        cancellationToken),
                    caller) ||
                !HasReplayablePrefix(
                    operation,
                    operationFacts,
                    flowResult,
                    blockPrefixCompletionIndices))
            {
                continue;
            }

            reachableOperationSites.Add((
                operation.Syntax.SyntaxTree,
                operation.Syntax.SpanStart,
                operation.Syntax.Span.Length));
            foreach (var call in calls)
            {
                AddOrUpgrade(
                    callSites,
                    CreateCandidate(
                        operation,
                        call,
                        call.CanReplay && HasReplayableCallEvaluation(
                            operation,
                            call,
                            operationFacts,
                            flowResult,
                            blockPrefixCompletionIndices),
                        flow: null,
                        flowAnalysis.Status,
                        cancellationToken));
            }
        }

        foreach (var property in ExecutableDescendantsAndSelf(operationRoot!)
                     .OfType<IPropertyReferenceOperation>()
                     .Where(static property =>
                         property.Parent is ICoalesceAssignmentOperation coalesce &&
                         ReferenceEquals(coalesce.Target, property))
                     .Where(property => reachableOperationSites.Contains((
                         property.Syntax.SyntaxTree,
                         property.Syntax.SpanStart,
                         property.Syntax.Span.Length)))
                     .Where(property =>
                         SymbolEqualityComparer.Default.Equals(
                             semanticModel.GetEnclosingSymbol(
                                 property.Syntax.SpanStart,
                             cancellationToken),
                             caller))
                     .Where(property =>
                         CanCoalesceGetterComplete(property, operationFacts)))
        {
            foreach (var call in GetPropertyCalls(property).Where(static call =>
                         call.TargetMethod.MethodKind == MethodKind.PropertySet))
            {
                AddOrUpgrade(callSites, CreateCandidate(
                    property,
                    call,
                    canReplay: false,
                    flow: null,
                    ManagedFlowStatus.BudgetExceeded,
                    cancellationToken));
            }
        }

        foreach (var operation in ExecutableDescendantsAndSelf(
                     operationRoot!).Where(static candidate =>
                         candidate is IForEachLoopOperation or
                             IUsingOperation or
                             IUsingDeclarationOperation or
                             IRecursivePatternOperation))
        {
            if (!operation.DescendantsAndSelf().Any(candidate =>
                    reachableOperationSites.Contains((
                        candidate.Syntax.SyntaxTree,
                        candidate.Syntax.SpanStart,
                        candidate.Syntax.Span.Length))))
            {
                continue;
            }

            foreach (var call in GetCalls(
                         operation,
                         operationFacts,
                         semanticModel,
                         delegateTargets,
                         flowResult,
                         _disposeInterfaceMethodCache,
                         cancellationToken))
            {
                var candidate = CreateCandidate(
                    operation,
                    call,
                    call.CanReplay && HasReplayableCallEvaluation(
                        operation,
                        call,
                        operationFacts,
                        flowResult,
                        blockPrefixCompletionIndices),
                        flow: null,
                        flowAnalysis.Status,
                        cancellationToken);
                AddOrUpgrade(
                    callSites,
                    candidate,
                    allowImplicitContainment: true);
            }
        }

        return [
            .. callSites.OrderBy(
                static candidate => candidate.Syntax.SpanStart)
        ];
    }

    private static RequiresCallSiteCandidate CreateCandidate(
        IOperation operation,
        RequiresCallTarget call,
        bool canReplay,
        ManagedFlowResult? flow,
        ManagedFlowStatus flowStatus,
        CancellationToken cancellationToken)
    {
        var resolvedTarget = RequiresCallSiteDispatch.ResolveExactTarget(
            call.TargetMethod,
            call.Instance,
            cancellationToken);
        return new RequiresCallSiteCandidate(
            operation,
            operation.Syntax,
            call.TargetMethod,
            call.Instance,
            call.Arguments,
            call.ExplicitArguments,
            call.ImplicitIntegerArguments,
            canReplay,
            flow,
            flowStatus)
        {
            ResolvedTargetMethod = resolvedTarget
        };
    }

    private static void AddOrUpgrade(
        List<RequiresCallSiteCandidate> callSites,
        RequiresCallSiteCandidate candidate,
        bool allowImplicitContainment = false,
        bool skipDeduplication = false)
    {
        var existingIndex = skipDeduplication
            ? -1
            : callSites.FindIndex(existing =>
                existing.Syntax.SyntaxTree == candidate.Syntax.SyntaxTree &&
                (existing.Syntax.Span == candidate.Syntax.Span ||
                 allowImplicitContainment &&
                 existing.Operation?.IsImplicit == true &&
                 candidate.Syntax.Span.Contains(existing.Syntax.Span)) &&
                SymbolEqualityComparer.Default.Equals(
                    existing.TargetMethod,
                    candidate.TargetMethod));
        if (existingIndex < 0)
        {
            callSites.Add(candidate);
        }
        else if (!callSites[existingIndex].CanReplay && candidate.CanReplay)
        {
            callSites[existingIndex] = candidate;
        }
    }

    private HashSet<(SyntaxTree Tree, int Start, int Length)>?
        GetReachableInitializerSites(
            DefiniteOperationFacts operationFacts)
    {
        if (declaration is not EqualsValueClauseSyntax initializer)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var operation = suppliedInitializerOperation ??
            semanticModel.GetOperation(
                initializer.Value,
                cancellationToken);
        return operation == null
            ? []
            : new HashSet<(SyntaxTree Tree, int Start, int Length)>(
                ExecutableUnflowedDescendantsAndSelf(
                        operation,
                        operationFacts)
                    .Select(static candidate => (
                        Tree: candidate.Syntax.SyntaxTree,
                        Start: candidate.Syntax.SpanStart,
                        Length: candidate.Syntax.Span.Length)));
    }

    private IEnumerable<IOperation> ExecutableDescendantsAndSelf(
        IOperation operation)
    {
        if (operation is IInvocationOperation invocation &&
            _invocationEmission.IsElided(invocation))
        {
            yield break;
        }
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in ExecutableDescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    internal bool TryCreateGraph(
        out IOperation? operationRoot,
        out ControlFlowGraph graph)
    {
        if (suppliedGraph != null)
        {
            operationRoot =
                suppliedOperationRoot ??
                suppliedGraph.OriginalOperation;
            graph = suppliedGraph;
            return true;
        }

        if (!TryGetOperationRoot(out operationRoot))
        {
            graph = null!;
            return false;
        }

        try
        {
            var created = operationRoot switch
            {
                IMethodBodyOperation or IConstructorBodyOperation =>
                    RoslynCfgFactory.TryCreateMethodOrConstructorGraph(
                        operationRoot, cancellationToken),
                IFieldInitializerOperation field =>
                    ControlFlowGraph.Create(field, cancellationToken),
                IPropertyInitializerOperation property =>
                    ControlFlowGraph.Create(property, cancellationToken),
                IBlockOperation block =>
                    ControlFlowGraph.Create(block, cancellationToken),
                _ => ControlFlowGraph.Create(
                    declaration,
                    semanticModel,
                    cancellationToken)
            };
            if (created == null)
            {
                graph = null!;
                return false;
            }
            graph = created;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            graph = null!;
            return false;
        }
    }

    private bool TryGetOperationRoot(
        out IOperation operationRoot)
    {
        if (suppliedOperationRoot != null)
        {
            operationRoot = suppliedOperationRoot;
            return true;
        }

        try
        {
            var flowSyntax =
                GetPropertyExpression(declaration) ??
                declaration;
            var operation = semanticModel.GetOperation(
                flowSyntax,
                cancellationToken);
            while (operation?.Parent != null)
            {
                operation = operation.Parent;
            }

            operationRoot = operation!;
            return operation != null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException)
        {
            operationRoot = null!;
            return false;
        }
    }

    private bool TryGetImplicitBaseConstructor(
        out IMethodSymbol baseConstructor)
    {
        baseConstructor = null!;
        if (declaration is not ConstructorDeclarationSyntax
            {
                Initializer: null
            } ||
            caller is not
            {
                MethodKind: MethodKind.Constructor,
                IsStatic: false
            } ||
            caller.ContainingType.TypeKind != TypeKind.Class ||
            IsRecordCopyConstructor(caller))
        {
            return false;
        }

        var candidate = RequiresCallSiteAnalyzer.TryGetImplicitBaseConstructor(caller);
        if (candidate == null)
        {
            return false;
        }

        baseConstructor = candidate;
        return true;
    }

    private static bool IsRecordCopyConstructor(
        IMethodSymbol constructor)
    {
        return constructor.ContainingType.IsRecord &&
            constructor.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(
                constructor.Parameters[0].Type,
                constructor.ContainingType);
    }

    private bool HasReplayablePrefix(
        IOperation callSite,
        DefiniteOperationFacts operationFacts,
        ManagedFlowResult? flowResult,
        Dictionary<BlockSyntax, BlockPrefixCompletionIndex>
            blockPrefixCompletionIndices)
    {
        if (declaration is EqualsValueClauseSyntax equalsValue)
        {
            return equalsValue.Value is ExpressionSyntax initializerExpression &&
                IsReplayableCallExpression(
                    initializerExpression,
                    callSite,
                    operationFacts);
        }

        var body =
            ContractClauseInventoryBuilder.GetBody(
                declaration);
        if (body is ExpressionSyntax expression)
        {
            return HasTransparentReplayableExpressionWrappers(
                    expression,
                    callSite.Syntax) &&
                IsReplayableCallExpression(
                    expression,
                    callSite,
                    operationFacts);
        }

        if (declaration is ConstructorDeclarationSyntax constructor &&
            constructor.Initializer is { } initializer &&
            ReferenceEquals(initializer.Parent, constructor))
        {
            if (callSite.Syntax is ConstructorInitializerSyntax callInitializer &&
                ReferenceEquals(callInitializer, initializer))
            {
                return true;
            }

            return IsReplayableConstructorInitializerArgument(
                initializer,
                callSite,
                operationFacts);
        }

        if (body is not BlockSyntax block)
        {
            return false;
        }

        var statement = callSite.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (statement == null ||
            !IsReplayableStatementContext(
                statement,
                callSite,
                operationFacts))
        {
            return false;
        }

        SyntaxNode directNode = statement;
        while (directNode.Parent is { } parent &&
               !ReferenceEquals(parent, block))
        {
            directNode = parent;
        }

        if (directNode is not StatementSyntax directStatement ||
            !ReferenceEquals(directStatement.Parent, block))
        {
            return false;
        }

        if (!blockPrefixCompletionIndices.TryGetValue(
                block,
                out var prefixCompletionIndex))
        {
            prefixCompletionIndex = new BlockPrefixCompletionIndex(
                this,
                block,
                operationFacts,
                flowResult,
                semanticModel,
                cancellationToken);
            blockPrefixCompletionIndices.Add(
                block,
                prefixCompletionIndex);
        }

        return prefixCompletionIndex.CanReplayBefore(
            directStatement,
            callSite);
    }

    private bool IsReplayableConstructorInitializerArgument(
        ConstructorInitializerSyntax initializer,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        var arguments = initializer.ArgumentList.Arguments;
        for (var index = 0; index < arguments.Count; index++)
        {
            var expression = arguments[index].Expression;
            if (!expression.Span.Contains(callSite.Syntax.Span))
            {
                continue;
            }

            if (!HasTransparentReplayableExpressionWrappers(
                    expression,
                    callSite.Syntax) ||
                !IsReplayableCallExpression(
                    expression,
                    callSite,
                    operationFacts))
            {
                return false;
            }

            for (var prior = 0; prior < index; prior++)
            {
                var priorOperation = semanticModel.GetOperation(
                    arguments[prior].Expression,
                    cancellationToken);
                if (!operationFacts.CompletesNormally(priorOperation))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private sealed class BlockPrefixCompletionIndex
    {
        private readonly RequiresCallSiteDiscovery _owner;
        private readonly BlockSyntax _block;
        private readonly DefiniteOperationFacts _operationFacts;
        private readonly ManagedFlowResult? _flowResult;
        private readonly SemanticModel _semanticModel;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<StatementSyntax, int> _statementIndices =
            new(ReferenceComparer<StatementSyntax>.Instance);
        private int _computedPrefixLength;
        private int _firstNonCompletingStatement = -1;

        internal BlockPrefixCompletionIndex(
            RequiresCallSiteDiscovery owner,
            BlockSyntax block,
            DefiniteOperationFacts operationFacts,
            ManagedFlowResult? flowResult,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _block = block;
            _operationFacts = operationFacts;
            _flowResult = flowResult;
            _semanticModel = semanticModel;
            _cancellationToken = cancellationToken;
            for (var index = 0; index < block.Statements.Count; index++)
            {
                _statementIndices.Add(block.Statements[index], index);
            }
        }

        internal bool CanReplayBefore(
            StatementSyntax statement,
            IOperation fallbackFlowOrigin)
        {
            if (!_statementIndices.TryGetValue(statement, out var statementIndex))
            {
                return false;
            }

            while (_computedPrefixLength < statementIndex &&
                   _firstNonCompletingStatement < 0)
            {
                var prior = _block.Statements[_computedPrefixLength];
                var completes = prior is EmptyStatementSyntax or
                    LocalFunctionStatementSyntax;
                if (!completes)
                {
                    var priorOperation = _semanticModel.GetOperation(
                        prior,
                        _cancellationToken);
                    completes = _owner.ReachesNextStatement(
                        priorOperation,
                        _operationFacts,
                        _flowResult,
                        priorOperation ?? fallbackFlowOrigin);
                }

                if (!completes)
                {
                    _firstNonCompletingStatement = _computedPrefixLength;
                }
                _computedPrefixLength++;
            }

            return _firstNonCompletingStatement < 0 ||
                statementIndex <= _firstNonCompletingStatement;
        }
    }

    private bool ReachesNextStatement(
        IOperation? operation,
        DefiniteOperationFacts operationFacts,
        ManagedFlowResult? flowResult,
        IOperation flowOrigin)
    {
        return operation != null &&
            !HasReachableControlFlowExit(operation, flowResult) &&
            operationFacts.CompletesNormally(
                operation,
                flowResult,
                flowOrigin);
    }

    private bool HasReachableControlFlowExit(
        IOperation operation,
        ManagedFlowResult? flowResult)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation is IAnonymousFunctionOperation or
            ILocalFunctionOperation)
        {
            return false;
        }

        if (operation is IReturnOperation or IThrowOperation or
            IBranchOperation)
        {
            return !IsControlFlowExitInfeasible(operation, flowResult);
        }

        return operation.ChildOperations.Any(child =>
            HasReachableControlFlowExit(child, flowResult));
    }

    private bool IsControlFlowExitInfeasible(
        IOperation exit,
        ManagedFlowResult? flowResult)
    {
        foreach (var conditional in exit.Syntax.AncestorsAndSelf()
                     .OfType<IfStatementSyntax>())
        {
            var inThen = conditional.Statement.Span.Contains(
                exit.Syntax.Span);
            var inElse = conditional.Else?.Statement.Span.Contains(
                exit.Syntax.Span) == true;
            if (!inThen && !inElse)
            {
                continue;
            }

            var conditionOperation = semanticModel.GetOperation(
                conditional.Condition,
                cancellationToken);
            bool? conditionValue = null;
            var constantValue = semanticModel.GetConstantValue(
                conditional.Condition,
                cancellationToken);
            if (constantValue.HasValue &&
                constantValue.Value is bool constant)
            {
                conditionValue = constant;
            }
            if (conditionValue == null &&
                conditionOperation != null &&
                flowResult != null &&
                flowResult.TryEvaluateAtOrigin(
                    conditionOperation,
                    conditionOperation,
                    out var abstractCondition) &&
                abstractCondition.TryGetBoolean(out var provenCondition))
            {
                conditionValue = provenCondition;
            }

            if (conditionValue.HasValue &&
                conditionValue.Value != inThen)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessorCall(IMethodSymbol method)
    {
        return method.MethodKind is
            MethodKind.PropertyGet or
            MethodKind.PropertySet or
            MethodKind.EventAdd or
            MethodKind.EventRemove;
    }

    private bool HasReplayableAccessorEvaluation(
        IOperation operation,
        RequiresCallTarget call,
        DefiniteOperationFacts operationFacts,
        ManagedFlowResult? flowResult)
    {
        var isInitializerMemberCall = IsInitializerMemberCall(
            operation,
            call.Instance);
        return (call.Instance == null ||
                operationFacts.CompletesNormally(
                    call.Instance,
                    flowResult,
                    operation) ||
                isInitializerMemberCall) &&
            call.Arguments.All(argument =>
                operationFacts.CompletesNormally(
                    argument.Value,
                    flowResult,
                    operation) ||
                isInitializerMemberCall &&
                CompletesInitializerCapture(
                    argument.Value,
                    operationFacts,
                    flowResult,
                    operation)) &&
            call.ExplicitArguments.Values.All(
                value => operationFacts.CompletesNormally(
                    value,
                    flowResult,
                    operation) ||
                    isInitializerMemberCall &&
                    CompletesInitializerCapture(
                        value,
                        operationFacts,
                        flowResult,
                        operation));
    }

    private bool CompletesInitializerCapture(
        IOperation operation,
        DefiniteOperationFacts operationFacts,
        ManagedFlowResult? flowResult,
        IOperation flowOrigin)
    {
        return operation is IFlowCaptureReferenceOperation &&
            semanticModel.GetOperation(operation.Syntax, cancellationToken)
                is { } source &&
            operationFacts.CompletesNormally(
                source,
                flowResult,
                flowOrigin);
    }

    private static bool IsInitializerMemberCall(
        IOperation operation,
        IOperation? instance)
    {
        if (instance is not IFlowCaptureReferenceOperation)
        {
            return false;
        }

        var isPropertyAssignment = operation is IPropertyReferenceOperation
        {
            Parent: ISimpleAssignmentOperation assignment
        } property && ReferenceEquals(assignment.Target, property);
        var isCollectionAdd = operation is IInvocationOperation
        {
            IsImplicit: true
        };
        return (isPropertyAssignment || isCollectionAdd) &&
            (OperationAncestors.Of(operation).Any(static ancestor =>
                 ancestor is IObjectOrCollectionInitializerOperation) ||
             operation.Syntax.AncestorsAndSelf().Any(static syntax =>
                 syntax is InitializerExpressionSyntax));
    }

    private bool HasReplayableCallEvaluation(
        IOperation operation,
        RequiresCallTarget call,
        DefiniteOperationFacts operationFacts,
        ManagedFlowResult? flowResult,
        Dictionary<BlockSyntax, BlockPrefixCompletionIndex>
            blockPrefixCompletionIndices)
    {
        if (operation is IUsingOperation or IUsingDeclarationOperation)
        {
            return operationFacts.MayCompleteNormally(
                operation is IUsingOperation usingOperation
                    ? usingOperation.Resources
                    : ((IUsingDeclarationOperation)operation)
                        .DeclarationGroup);
        }
        if (IsAccessorCall(call.TargetMethod) ||
            operation is IListPatternOperation or IForEachLoopOperation or
                IRecursivePatternOperation ||
            operation is IInvocationOperation invocation &&
                invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                !SymbolEqualityComparer.Default.Equals(
                    call.TargetMethod,
                    invocation.TargetMethod) ||
            operation.IsImplicit)
        {
            return HasReplayableAccessorEvaluation(
                operation,
                call,
                operationFacts,
                flowResult);
        }
        return HasReplayablePrefix(
            operation,
            operationFacts,
            flowResult,
            blockPrefixCompletionIndices);
    }

    private static bool CanCoalesceGetterComplete(
        IPropertyReferenceOperation property,
        DefiniteOperationFacts operationFacts)
    {
        return (property.Instance == null ||
                operationFacts.MayCompleteNormally(property.Instance)) &&
            property.Arguments.All(argument =>
                operationFacts.MayCompleteNormally(argument.Value)) &&
            property.Parent is ICoalesceAssignmentOperation coalesce &&
            operationFacts.MayCompleteNormally(coalesce.Value) &&
            property.Property.GetMethod is { } getter &&
            operationFacts.MethodCanCompleteNormally(getter);
    }

    private bool IsDirectReplayableStatement(
        StatementSyntax statement,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        return statement switch
        {
            ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } when assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression) =>
                IsReplayableCallExpression(
                    assignment.Right,
                    callSite,
                    operationFacts) &&
                operationFacts.CompletesNormally(
                    semanticModel.GetOperation(
                        assignment.Left,
                        cancellationToken)),
            ExpressionStatementSyntax expression =>
                IsReplayableCallExpression(
                    expression.Expression,
                    callSite,
                    operationFacts),
            LocalDeclarationStatementSyntax local =>
                local.Declaration.Variables.Count == 1 &&
                IsReplayableCallExpression(
                    local.Declaration.Variables[0]
                        .Initializer?.Value,
                    callSite,
                    operationFacts),
            ReturnStatementSyntax returned =>
                IsReplayableCallExpression(
                    returned.Expression,
                    callSite,
                    operationFacts),
            ThrowStatementSyntax thrown =>
                IsReplayableCallExpression(
                    thrown.Expression,
                    callSite,
                    operationFacts),
            IfStatementSyntax conditional =>
                IsReplayableCallExpression(
                    conditional.Condition,
                    callSite,
                    operationFacts),
            WhileStatementSyntax loop =>
                IsReplayableCallExpression(
                    loop.Condition,
                    callSite,
                    operationFacts),
            DoStatementSyntax loop =>
                IsReplayableCallExpression(
                    loop.Condition,
                    callSite,
                    operationFacts),
            ForStatementSyntax loop =>
                IsReplayableForExpression(loop, callSite, operationFacts),
            ForEachStatementSyntax loop =>
                IsReplayableCallExpression(
                    loop.Expression,
                    callSite,
                    operationFacts),
            UsingStatementSyntax usingStatement =>
                IsReplayableUsingExpression(
                    usingStatement,
                    callSite,
                    operationFacts),
            LockStatementSyntax lockStatement =>
                IsReplayableCallExpression(
                    lockStatement.Expression,
                    callSite,
                    operationFacts),
            FixedStatementSyntax fixedStatement =>
                fixedStatement.Declaration.Variables.Any(variable =>
                    IsReplayableCallExpression(
                        variable.Initializer?.Value,
                        callSite,
                        operationFacts)),
            SwitchStatementSyntax switchStatement =>
                IsReplayableCallExpression(
                    switchStatement.Expression,
                    callSite,
                    operationFacts),
            _ => false
        };
    }

    private bool IsReplayableStatementContext(
        StatementSyntax statement,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        if (!IsDirectReplayableStatement(
                statement,
                callSite,
                operationFacts))
        {
            return false;
        }

        foreach (var ancestor in statement.Ancestors())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (ancestor)
            {
                case CatchClauseSyntax:
                    return false;
                case IfStatementSyntax conditional
                    when conditional.Statement.Span.Contains(callSite.Syntax.Span) ||
                         conditional.Else?.Statement.Span.Contains(
                             callSite.Syntax.Span) == true:
                    if (!IsDefinitelySelectedBranch(
                            conditional,
                            callSite.Syntax))
                    {
                        return false;
                    }
                    break;
                case WhileStatementSyntax loop
                    when loop.Statement.Span.Contains(callSite.Syntax.Span):
                    if (!IsConstantBoolean(loop.Condition, true))
                    {
                        return false;
                    }
                    break;
                case ForStatementSyntax loop
                    when loop.Statement.Span.Contains(callSite.Syntax.Span):
                    if (loop.Condition != null &&
                        !IsConstantBoolean(loop.Condition, true))
                    {
                        return false;
                    }
                    break;
                case ForEachStatementSyntax loop
                    when loop.Statement.Span.Contains(callSite.Syntax.Span):
                    return false;
                case UsingStatementSyntax usingStatement
                    when usingStatement.Statement.Span.Contains(
                        callSite.Syntax.Span):
                    if (!CompletesSyntax(
                            usingStatement.Expression,
                            operationFacts))
                    {
                        return false;
                    }
                    break;
                case LockStatementSyntax lockStatement
                    when lockStatement.Statement.Span.Contains(
                        callSite.Syntax.Span):
                    if (!CompletesSyntax(
                            lockStatement.Expression,
                            operationFacts))
                    {
                        return false;
                    }
                    break;
                case SwitchStatementSyntax switchStatement
                    when switchStatement.Sections.Any(section =>
                        section.Span.Contains(callSite.Syntax.Span)):
                    return false;
            }
        }

        return true;
    }

    private bool IsReplayableForExpression(
        ForStatementSyntax statement,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        return statement.Initializers.Any(initializer =>
                   IsReplayableCallExpression(
                       initializer,
                       callSite,
                       operationFacts)) ||
               statement.Declaration?.Variables.Any(variable =>
                   IsReplayableCallExpression(
                       variable.Initializer?.Value,
                       callSite,
                       operationFacts)) == true ||
               IsReplayableCallExpression(
                   statement.Condition,
                   callSite,
                   operationFacts) ||
               statement.Incrementors.Any(incrementor =>
                   IsReplayableCallExpression(
                       incrementor,
                       callSite,
                       operationFacts));
    }

    private bool IsReplayableUsingExpression(
        UsingStatementSyntax statement,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        return statement.Expression != null &&
            IsReplayableCallExpression(
                statement.Expression,
                callSite,
                operationFacts);
    }

    private bool CompletesSyntax(
        SyntaxNode? syntax,
        DefiniteOperationFacts operationFacts)
    {
        return syntax != null &&
            operationFacts.CompletesNormally(
                semanticModel.GetOperation(syntax, cancellationToken));
    }

    private bool IsDefinitelySelectedBranch(
        IfStatementSyntax statement,
        SyntaxNode callSiteSyntax)
    {
        var value = semanticModel.GetConstantValue(
            statement.Condition,
            cancellationToken);
        if (value is not { HasValue: true, Value: bool condition })
        {
            return false;
        }

        var inThen = statement.Statement.Span.Contains(callSiteSyntax.Span);
        return condition == inThen;
    }

    private bool IsConstantBoolean(
        ExpressionSyntax condition,
        bool expected)
    {
        return semanticModel.GetConstantValue(
                condition,
                cancellationToken) is
        { HasValue: true, Value: bool value } &&
            value == expected;
    }

    private bool IsReplayableCallExpression(
        ExpressionSyntax? expression,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        var callSiteSyntax = callSite.Syntax;
        if (expression == null ||
            !expression.Span.Contains(callSiteSyntax.Span))
        {
            return false;
        }

        var current = callSiteSyntax;
        while (!ReferenceEquals(current, expression))
        {
            if (current.Parent is not { } parent ||
                !expression.Span.Contains(parent.Span))
            {
                return false;
            }

            switch (parent)
            {
                case BinaryExpressionSyntax binary
                    when binary.IsKind(
                             SyntaxKind.LogicalAndExpression) ||
                         binary.IsKind(
                             SyntaxKind.LogicalOrExpression) ||
                         binary.IsKind(
                             SyntaxKind.CoalesceExpression):
                    if (binary.Right.Span.Contains(callSiteSyntax.Span))
                    {
                        return false;
                    }
                    break;
                case ConditionalExpressionSyntax conditional:
                    if (!conditional.Condition.Span.Contains(
                            callSiteSyntax.Span))
                    {
                        var value = semanticModel.GetConstantValue(
                            conditional.Condition,
                            cancellationToken);
                        if (value is not
                            { HasValue: true, Value: bool condition } ||
                            condition != conditional.WhenTrue.Span.Contains(
                                callSiteSyntax.Span))
                        {
                            return false;
                        }
                    }
                    break;
                case ConditionalAccessExpressionSyntax:
                    return false;
                case SwitchExpressionArmSyntax or
                    SwitchExpressionSyntax:
                    return false;
            }

            current = parent;
        }

        return PrecedingExpressionOperationsComplete(
            callSite,
            expression,
            operationFacts);
    }

    private static bool HasTransparentReplayableExpressionWrappers(
        ExpressionSyntax expression,
        SyntaxNode callSiteSyntax)
    {
        var current = callSiteSyntax;
        while (!ReferenceEquals(current, expression))
        {
            if (current.Parent is not { } parent ||
                !expression.Span.Contains(parent.Span))
            {
                return false;
            }

            if (parent is ParenthesizedExpressionSyntax parenthesized &&
                parenthesized.Expression.Span.Contains(current.Span))
            {
                current = parent;
                continue;
            }

            if (parent is CastExpressionSyntax or
                CheckedExpressionSyntax ||
                parent is PostfixUnaryExpressionSyntax postfix &&
                postfix.IsKind(
                    SyntaxKind.SuppressNullableWarningExpression))
            {
                return false;
            }

            current = parent;
        }

        return true;
    }

    private static bool PrecedingExpressionOperationsComplete(
        IOperation callSite,
        ExpressionSyntax expression,
        DefiniteOperationFacts operationFacts)
    {
        var current = callSite;
        while (current.Parent is { } parent &&
               expression.Span.Contains(parent.Syntax.Span))
        {
            var foundCurrent = false;
            foreach (var child in parent.ChildOperations)
            {
                if (ReferenceEquals(child, current))
                {
                    foundCurrent = true;
                    break;
                }

                if (!operationFacts.CompletesNormally(child))
                {
                    return false;
                }
            }

            if (!foundCurrent)
            {
                return false;
            }

            current = parent;
        }

        return true;
    }

    private static ImmutableArray<RequiresCallTarget> GetCalls(
        IOperation operation,
        DefiniteOperationFacts? operationFacts = null,
        SemanticModel? semanticModel = null,
        IReadOnlyDictionary<ILocalSymbol,
            DirectDelegateTarget>?
            delegateTargets = null,
        ManagedFlowResult? flowResult = null,
        Dictionary<bool,
            (INamedTypeSymbol? Interface, IMethodSymbol? Method)>?
            disposeInterfaceMethodCache = null,
        CancellationToken cancellationToken = default)
    {
        return operation switch
        {
            IInvocationOperation invocation => GetInvocationCalls(
                invocation,
                delegateTargets),
            IObjectCreationOperation
            {
                Constructor: { } constructor
            } creation => [new(
                constructor,
                null,
                creation.Arguments,
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                true)],
            IPropertyReferenceOperation property =>
                GetPropertyCalls(property),
            IEventReferenceOperation eventReference =>
                GetEventCalls(eventReference),
            ICompoundAssignmentOperation
            {
                OperatorMethod: { } method
            } compound => CreateImplicitOperatorCalls(
                method,
                compound,
                compound.IsLifted,
                flowResult,
                compound.Target,
                compound.Value),
            IIncrementOrDecrementOperation
            {
                OperatorMethod: { } method
            } increment => CreateImplicitOperatorCalls(
                method,
                increment,
                increment.IsLifted,
                flowResult,
                increment.Target),
            IBinaryOperation
            {
                OperatorMethod: { } method
            } binary => CreateImplicitOperatorCalls(
                method,
                binary,
                binary.IsLifted,
                flowResult,
                binary.LeftOperand,
                binary.RightOperand),
            IUnaryOperation
            {
                OperatorMethod: { } method
            } unary => CreateImplicitOperatorCalls(
                method,
                unary,
                unary.IsLifted,
                flowResult,
                unary.Operand),
            IConversionOperation
            {
                OperatorMethod: { } method
            } conversion => CreateImplicitOperatorCalls(
                method,
                conversion,
                IsLiftedUserDefinedConversion(conversion, method),
                flowResult,
                conversion.Operand),
            IForEachLoopOperation forEach => GetForEachCalls(
                forEach,
                operationFacts,
                semanticModel,
                disposeInterfaceMethodCache,
                cancellationToken),
            IUsingOperation usingOperation => GetUsingCalls(
                usingOperation.Resources,
                usingOperation.IsAsynchronous,
                semanticModel?.Compilation,
                operationFacts,
                flowResult,
                disposeInterfaceMethodCache),
            IUsingDeclarationOperation usingDeclaration => GetUsingCalls(
                usingDeclaration.DeclarationGroup,
                usingDeclaration.IsAsynchronous,
                semanticModel?.Compilation,
                operationFacts,
                flowResult,
                disposeInterfaceMethodCache),
            IRecursivePatternOperation
            {
                DeconstructSymbol: IMethodSymbol deconstruct
            } recursivePattern => GetRecursivePatternCalls(
                recursivePattern,
                deconstruct,
                flowResult),
            IListPatternOperation listPattern => GetListPatternCalls(
                listPattern,
                operationFacts,
                semanticModel?.Compilation,
                cancellationToken),
            _ => []
        };
    }

    private static ImmutableArray<RequiresCallTarget> GetInvocationCalls(
        IInvocationOperation invocation,
        IReadOnlyDictionary<ILocalSymbol,
            DirectDelegateTarget>?
            delegateTargets)
    {
        var ordinary = new RequiresCallTarget(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true);
        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke ||
            !TryResolveDirectDelegateTarget(
                invocation,
                delegateTargets,
                out var target))
        {
            return [ordinary];
        }

        return [ordinary, new RequiresCallTarget(
            target.Method,
            target.Instance,
            invocation.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true)];
    }

    private static ImmutableArray<RequiresCallTarget>
        CreateImplicitOperatorCalls(
            IMethodSymbol method,
            IOperation operation,
            bool isLifted,
            ManagedFlowResult? flowResult,
            IOperation firstOperand,
            IOperation? secondOperand = null)
    {
        if (isLifted &&
            (DefiniteOperationFacts.IsDefinitelyNull(firstOperand) ||
             flowResult?.ProvesNull(operation, firstOperand) == true ||
             secondOperand != null &&
             (DefiniteOperationFacts.IsDefinitelyNull(secondOperand) ||
              flowResult?.ProvesNull(operation, secondOperand) == true)))
        {
            return [];
        }

        return [CreateImplicitOperatorCall(method, firstOperand, secondOperand)];
    }

    private static bool IsLiftedUserDefinedConversion(
        IConversionOperation conversion,
        IMethodSymbol method)
    {
        var operandType = CompilerIdentityBridge.GetNullableUnderlyingType(
            conversion.Operand.Type);
        var resultType = CompilerIdentityBridge.GetNullableUnderlyingType(
            conversion.Type);
        return method.Parameters.Length == 1 &&
            operandType != null &&
            resultType != null &&
            SymbolEqualityComparer.Default.Equals(
                operandType,
                method.Parameters[0].Type) &&
            SymbolEqualityComparer.Default.Equals(
                resultType,
                method.ReturnType);
    }

    private static RequiresCallTarget CreateImplicitOperatorCall(
        IMethodSymbol method,
        IOperation firstOperand,
        IOperation? secondOperand = null)
    {
        var arguments = ImmutableDictionary.CreateBuilder<int, IOperation>();
        if (method.Parameters.Length > 0)
        {
            arguments.Add(0, firstOperand);
        }
        if (method.Parameters.Length > 1 && secondOperand != null)
        {
            arguments.Add(1, secondOperand);
        }
        return new RequiresCallTarget(
            method,
            Instance: null,
            Arguments: [],
            arguments.ToImmutable(),
            ImmutableDictionary<int, long>.Empty,
            CanReplay: true);
    }

    private sealed record DirectDelegateTarget(
        IMethodSymbol Method,
        IOperation? Instance,
        ImmutableArray<IOperation> Invalidations,
        bool HasGoto);

    private static Dictionary<ILocalSymbol, DirectDelegateTarget>
        GetDirectDelegateTargets(IOperation operationRoot)
    {
        var declarations = new List<(
            ILocalSymbol Symbol,
            IMethodSymbol Method,
            IOperation? Instance)>();
        var invalidations = new Dictionary<ILocalSymbol, List<IOperation>>(
            SymbolEqualityComparer.Default);
        var hasGoto = false;
        foreach (var operation in operationRoot.DescendantsAndSelf())
        {
            if (operation is IBranchOperation
                {
                    BranchKind: BranchKind.GoTo
                })
            {
                hasGoto = true;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                declarator.Initializer?.Value is { } value &&
                TryGetMethodReference(value, out var reference))
            {
                declarations.Add((
                    declarator.Symbol,
                    reference.Method,
                    reference.Instance));
            }

            var target = operation switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation increment => increment.Target,
                IArgumentOperation
                {
                    Parameter.RefKind: not RefKind.None
                } argument => argument.Value,
                IVariableDeclaratorOperation
                {
                    Symbol.RefKind: not RefKind.None,
                    Initializer.Value: { } initializerValue
                } => initializerValue,
                _ => null
            };
            if (TryGetLocalReference(target, out var local))
            {
                if (!invalidations.TryGetValue(local, out var operations))
                {
                    operations = [];
                    invalidations.Add(local, operations);
                }
                operations.Add(operation);
            }
        }

        var targets = new Dictionary<ILocalSymbol, DirectDelegateTarget>(
            SymbolEqualityComparer.Default);
        var ambiguous = new HashSet<ILocalSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var declaration in declarations)
        {
            if (ambiguous.Contains(declaration.Symbol))
            {
                continue;
            }

            if (targets.ContainsKey(declaration.Symbol))
            {
                targets.Remove(declaration.Symbol);
                ambiguous.Add(declaration.Symbol);
            }
            else
            {
                targets.Add(
                    declaration.Symbol,
                    new DirectDelegateTarget(
                        declaration.Method,
                        declaration.Instance,
                        invalidations.TryGetValue(
                            declaration.Symbol,
                            out var operations)
                            ? [.. operations]
                            : [],
                        hasGoto));
            }
        }

        return targets;
    }

    private static bool TryResolveDirectDelegateTarget(
        IInvocationOperation invocation,
        IReadOnlyDictionary<ILocalSymbol,
            DirectDelegateTarget>? targets,
        out (IMethodSymbol Method, IOperation? Instance) target)
    {
        var instance = invocation.Instance;
        if (instance != null &&
            TryGetMethodReference(instance, out var reference))
        {
            target = (reference.Method, reference.Instance);
            return true;
        }
        if (targets != null &&
            TryGetLocalReference(instance, out var local) &&
            targets.TryGetValue(local, out var known) &&
            IsStableAtInvocation(invocation, known))
        {
            target = (known.Method, known.Instance);
            return true;
        }

        target = default;
        return false;
    }

    private static bool IsStableAtInvocation(
        IInvocationOperation invocation,
        DirectDelegateTarget target)
    {
        var invocationTree = invocation.Syntax.SyntaxTree;
        var invocationInsideLoop = IsInsideLoop(invocation);
        var invocationInsideNestedCallable = IsInsideNestedCallable(invocation);
        foreach (var invalidation in target.Invalidations)
        {
            if (invalidation.Syntax.SyntaxTree != invocationTree ||
                invalidation.Syntax.SpanStart <=
                    invocation.Syntax.SpanStart ||
                invocationInsideLoop ||
                IsInsideLoop(invalidation) ||
                invocationInsideNestedCallable ||
                IsInsideNestedCallable(invalidation) ||
                target.HasGoto)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsInsideLoop(IOperation operation)
    {
        return OperationAncestors.Of(operation).Any(static ancestor =>
            ancestor is ILoopOperation);
    }

    private static bool IsInsideNestedCallable(IOperation operation)
    {
        return OperationAncestors.Of(operation).Any(static ancestor =>
            ancestor is IAnonymousFunctionOperation or
                ILocalFunctionOperation);
    }

    private static bool TryGetMethodReference(
        IOperation operation,
        out IMethodReferenceOperation reference)
    {
        while (true)
        {
            switch (operation)
            {
                case IMethodReferenceOperation methodReference:
                    reference = methodReference;
                    return true;
                case IDelegateCreationOperation delegateCreation:
                    operation = delegateCreation.Target;
                    continue;
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                default:
                    reference = null!;
                    return false;
            }
        }
    }

    private static bool TryGetLocalReference(
        IOperation? operation,
        out ILocalSymbol local)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        if (operation is ILocalReferenceOperation reference)
        {
            local = reference.Local;
            return true;
        }

        local = null!;
        return false;
    }

    private static ImmutableArray<RequiresCallTarget> GetForEachCalls(
        IForEachLoopOperation loop,
        DefiniteOperationFacts? operationFacts,
        SemanticModel? semanticModel,
        Dictionary<bool,
            (INamedTypeSymbol? Interface, IMethodSymbol? Method)>?
            disposeInterfaceMethodCache,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null ||
            loop.Syntax is not CommonForEachStatementSyntax syntax)
        {
            return [];
        }

        var info = semanticModel.GetForEachStatementInfo(syntax);
        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        if (info.GetEnumeratorMethod == null)
        {
            return [];
        }

        Add(info.GetEnumeratorMethod, loop.Collection);
        if (operationFacts != null &&
            !operationFacts.MethodCanCompleteNormally(
                info.GetEnumeratorMethod))
        {
            return calls.ToImmutable();
        }

        if (info.MoveNextMethod != null)
        {
            Add(info.MoveNextMethod, instance: null);
        }
        if (info.CurrentProperty?.GetMethod is { } current &&
            (info.MoveNextMethod == null ||
             MethodMayReturnTrue(
                 info.MoveNextMethod,
                 semanticModel.Compilation,
                 cancellationToken) &&
             (operationFacts == null ||
              operationFacts.MethodCanCompleteNormally(
                  info.MoveNextMethod))))
        {
            Add(current, instance: null);
        }
        Add(
            ResolveDisposeMethod(
                info.GetEnumeratorMethod.ReturnType,
                loop.IsAsynchronous,
                semanticModel.Compilation,
                disposeInterfaceMethodCache: disposeInterfaceMethodCache) ??
                info.DisposeMethod,
            instance: null);
        return calls.ToImmutable();

        void Add(IMethodSymbol? method, IOperation? instance)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (method == null)
            {
                return;
            }
            calls.Add(new RequiresCallTarget(
                method,
                instance,
                [],
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                true));
        }
    }

    private static bool MethodMayReturnTrue(
        IMethodSymbol? method,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (method?.ReturnType.SpecialType !=
                SpecialType.System_Boolean ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return true;
        }

        var declaration = method.DeclaringSyntaxReferences[0]
            .GetSyntax(cancellationToken);
        if (declaration is not MethodDeclarationSyntax methodDeclaration ||
            methodDeclaration.ExpressionBody?.Expression is not { } expression)
        {
            return true;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, expression.SyntaxTree);
        var constant = model.GetConstantValue(expression, cancellationToken);
        return !constant.HasValue || constant.Value is not false;
    }

    private static ImmutableArray<RequiresCallTarget> GetUsingCalls(
        IOperation resources,
        bool isAsynchronous,
        Compilation? compilation,
        DefiniteOperationFacts? operationFacts,
        ManagedFlowResult? flowResult,
        Dictionary<bool,
            (INamedTypeSymbol? Interface, IMethodSymbol? Method)>?
            disposeInterfaceMethodCache)
    {
        if (compilation == null)
        {
            return [];
        }

        var acquired = new List<(
            ITypeSymbol Type,
            IOperation Resource,
            IOperation Origin)>();
        if (resources is IVariableDeclarationGroupOperation group)
        {
            foreach (var declarator in group.Declarations.SelectMany(
                         static declaration => declaration.Declarators))
            {
                var resource = declarator.Initializer?.Value;
                if (resource == null ||
                    operationFacts != null &&
                    !operationFacts.MayCompleteNormally(resource))
                {
                    break;
                }
                acquired.Add((
                    declarator.Symbol.Type,
                    resource,
                    declarator));
            }
        }
        else if ((operationFacts == null ||
                  operationFacts.MayCompleteNormally(resources)) &&
                 resources.Type is { } resourceType)
        {
            acquired.Add((resourceType, resources, resources));
        }

        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        foreach (var item in acquired.AsEnumerable().Reverse())
        {
            if (DefiniteOperationFacts.IsDefinitelyNull(item.Resource) ||
                flowResult?.ProvesNull(
                    item.Origin,
                    item.Resource) == true)
            {
                continue;
            }
            var method = ResolveDisposeMethod(
                item.Type,
                isAsynchronous,
                compilation,
                disposeInterfaceMethodCache: disposeInterfaceMethodCache);
            if (method != null)
            {
                calls.Add(new RequiresCallTarget(
                    method,
                    item.Resource,
                    Arguments: [],
                    ImmutableDictionary<int, IOperation>.Empty,
                    ImmutableDictionary<int, long>.Empty,
                    CanReplay: true));
            }
        }
        return calls.ToImmutable();
    }

    private static IMethodSymbol? ResolveDisposeMethod(
        ITypeSymbol resourceType,
        bool isAsynchronous,
        Compilation compilation,
        HashSet<ITypeSymbol>? visited = null,
        Dictionary<bool,
            (INamedTypeSymbol? Interface, IMethodSymbol? Method)>?
            disposeInterfaceMethodCache = null)
    {
        visited ??= new HashSet<ITypeSymbol>(
            SymbolEqualityComparer.Default);
        if (!visited.Add(resourceType))
        {
            return null;
        }
        resourceType = CompilerIdentityBridge.GetNullableUnderlyingType(
            resourceType) ?? resourceType;
        if (resourceType is ITypeParameterSymbol typeParameter)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                var constrained = ResolveDisposeMethod(
                    constraint,
                    isAsynchronous,
                    compilation,
                    visited,
                    disposeInterfaceMethodCache);
                if (constrained != null)
                {
                    return constrained;
                }
            }
            return null;
        }

        var interfaceName = isAsynchronous
            ? FrameworkTypeMetadataNames.IAsyncDisposable
            : FrameworkTypeMetadataNames.IDisposable;
        var methodName = isAsynchronous
            ? "DisposeAsync"
            : "Dispose";
        var interfaceLookup = GetDisposeInterfaceMethod(
            compilation,
            isAsynchronous,
            interfaceName,
            methodName,
            disposeInterfaceMethodCache);
        var disposable = interfaceLookup.Interface;
        var interfaceMethod = interfaceLookup.Method;
        if (interfaceMethod != null &&
            resourceType is INamedTypeSymbol named &&
            named.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    disposable!.OriginalDefinition)))
        {
            return named.FindImplementationForInterfaceMember(
                    interfaceMethod) as IMethodSymbol ??
                interfaceMethod;
        }

        return resourceType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method =>
                !method.IsStatic &&
                method.Arity == 0 &&
                method.Parameters.IsEmpty);
    }

    private static (
        INamedTypeSymbol? Interface,
        IMethodSymbol? Method) GetDisposeInterfaceMethod(
        Compilation compilation,
        bool isAsynchronous,
        string interfaceName,
        string methodName,
        Dictionary<bool,
            (INamedTypeSymbol? Interface, IMethodSymbol? Method)>?
            disposeInterfaceMethodCache)
    {
        if (disposeInterfaceMethodCache != null &&
            disposeInterfaceMethodCache.TryGetValue(
                isAsynchronous,
                out var cached))
        {
            return cached;
        }

        var disposable = compilation.GetTypeByMetadataName(interfaceName);
        var interfaceMethod = disposable?.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .SingleOrDefault(static method => method.Parameters.IsEmpty);
        var result = (disposable, interfaceMethod);
        disposeInterfaceMethodCache?.Add(isAsynchronous, result);
        return result;
    }

    private static ImmutableArray<RequiresCallTarget>
        GetRecursivePatternCalls(
            IRecursivePatternOperation pattern,
            IMethodSymbol deconstruct,
            ManagedFlowResult? flowResult)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        var governingValue = instance ??
            GetRootPatternGoverningValue(pattern);
        if (governingValue != null &&
            (DefiniteOperationFacts.IsDefinitelyNull(governingValue) ||
             flowResult?.ProvesNull(pattern, governingValue) == true))
        {
            return [];
        }

        return [new RequiresCallTarget(
            deconstruct,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true)];
    }

    private static IOperation? GetRootPatternGoverningValue(
        IPatternOperation pattern)
    {
        IOperation current = pattern;
        while (current.Parent is IPatternOperation or
               IPropertySubpatternOperation)
        {
            current = current.Parent;
        }
        return current is IPatternOperation root
            ? SwitchExpressionFacts.GetGoverningValue(root)
            : null;
    }

    private static ImmutableArray<RequiresCallTarget> GetListPatternCalls(
        IListPatternOperation pattern,
        DefiniteOperationFacts? operationFacts,
        Compilation? compilation,
        CancellationToken cancellationToken)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (instance != null &&
            DefiniteOperationFacts.IsDefinitelyNull(instance))
        {
            return [];
        }

        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        var length = SwitchExpressionFacts.GetCallableListPatternMember(
            pattern.LengthSymbol);
        if (length != null)
        {
            calls.Add(CreateImplicitListPatternCall(
                length,
                instance,
                ImmutableDictionary<int, long>.Empty));
            if (operationFacts != null &&
                !operationFacts.MethodCanCompleteNormally(length))
            {
                return calls.ToImmutable();
            }
        }

        var requiredLength = 0;
        var hasSlice = false;
        var sliceIndex = -1;
        for (var index = 0; index < pattern.Patterns.Length; index++)
        {
            if (pattern.Patterns[index] is ISlicePatternOperation)
            {
                hasSlice = true;
                sliceIndex = sliceIndex < 0 ? index : sliceIndex;
            }
            else
            {
                requiredLength++;
            }
        }
        long knownLength = 0;
        var hasKnownLength = compilation != null &&
            TryGetKnownListLength(
                pattern,
                instance,
                compilation!,
                cancellationToken,
                out knownLength);
        if (hasKnownLength &&
            (hasSlice
                ? knownLength < requiredLength
                : knownLength != requiredLength))
        {
            return calls.ToImmutable();
        }

        for (var itemIndex = 0;
             itemIndex < pattern.Patterns.Length;
             itemIndex++)
        {
            var item = pattern.Patterns[itemIndex];
            var member = SwitchExpressionFacts.GetCallableListPatternMember(
                pattern,
                item);
            if (member == null)
            {
                continue;
            }
            ImmutableDictionary<int, long> implicitArguments;
            if (item is ISlicePatternOperation)
            {
                implicitArguments = CreateImplicitListPatternArguments(
                    member,
                    itemIndex,
                    hasKnownLength
                        ? knownLength - requiredLength
                        : null);
            }
            else
            {
                long? implicitIndex = sliceIndex < 0 ||
                    itemIndex < sliceIndex
                        ? itemIndex
                        : hasKnownLength
                            ? knownLength -
                                (pattern.Patterns.Length - itemIndex)
                            : null;
                implicitArguments = CreateImplicitListPatternArguments(
                    member,
                    implicitIndex);
            }
            calls.Add(CreateImplicitListPatternCall(
                member,
                instance,
                implicitArguments));
            if (operationFacts != null &&
                !operationFacts.MethodCanCompleteNormally(member))
            {
                break;
            }
        }
        return calls.ToImmutable();
    }

    private static RequiresCallTarget CreateImplicitListPatternCall(
        IMethodSymbol method,
        IOperation? instance,
        ImmutableDictionary<int, long> implicitArguments)
    {
        return new RequiresCallTarget(
            method,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
            implicitArguments,
            true);
    }

    private static ImmutableDictionary<int, long>
        CreateImplicitListPatternArguments(
            IMethodSymbol method,
            long? firstValue,
            long? secondValue = null)
    {
        var arguments = ImmutableDictionary.CreateBuilder<int, long>();
        if (method.Parameters.Length > 0 &&
            firstValue.HasValue &&
            method.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
        {
            arguments.Add(0, firstValue.Value);
        }
        if (method.Parameters.Length > 1 &&
            secondValue.HasValue &&
            method.Parameters[1].Type.SpecialType == SpecialType.System_Int32)
        {
            arguments.Add(1, secondValue.Value);
        }
        return arguments.ToImmutable();
    }

    private static bool TryGetKnownListLength(
        IListPatternOperation pattern,
        IOperation? instance,
        Compilation compilation,
        CancellationToken cancellationToken,
        out long length)
    {
        instance = instance == null
            ? null
            : DefiniteOperationFacts.UnwrapHarmlessValue(instance);
        if (SharpProof.Effects.ArrayLengthFacts.TryGetConstantLength(
                instance,
                out length))
        {
            return true;
        }
        if (pattern.LengthSymbol is not IPropertySymbol
            { GetMethod: { } getter } ||
            getter.IsVirtual && !getter.IsSealed ||
            getter.DeclaringSyntaxReferences.Length != 1)
        {
            length = 0;
            return false;
        }

        var declaration = getter.DeclaringSyntaxReferences[0]
            .GetSyntax(cancellationToken);
        var expression = declaration switch
        {
            PropertyDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            AccessorDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            ArrowExpressionClauseSyntax
            { Expression: { } body } => body,
            AccessorDeclarationSyntax
            { Body.Statements.Count: 1 } accessor
                when accessor.Body!.Statements[0] is ReturnStatementSyntax
                { Expression: { } body } => body,
            _ => null
        };
        if (expression == null)
        {
            length = 0;
            return false;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, expression.SyntaxTree);
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue || constant.Value == null)
        {
            length = 0;
            return false;
        }
        return SharpProof.Effects.EffectContractMetadata.TryConvertInt64(
                   constant.Value,
                   out length) &&
            length >= 0;
    }

    internal static ImmutableArray<RequiresCallSiteCandidate>
        CreateUnflowedCandidates(
            IOperation operation,
            SemanticModel? semanticModel = null)
    {
        return [.. GetCalls(
            operation,
            semanticModel: semanticModel).Select(call =>
            new RequiresCallSiteCandidate(
                operation,
                operation.Syntax,
                call.TargetMethod,
                call.Instance,
                call.Arguments,
                call.ExplicitArguments,
                call.ImplicitIntegerArguments,
                call.CanReplay,
                Flow: null,
                ManagedFlowStatus.BudgetExceeded))];
    }

    internal static IEnumerable<IOperation>
        ExecutableUnflowedDescendantsAndSelf(
            IOperation operation,
            DefiniteOperationFacts operationFacts)
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
        {
            yield break;
        }

        IEnumerable<IOperation> Descend(IOperation child)
        {
            return ExecutableUnflowedDescendantsAndSelf(
                child,
                operationFacts);
        }

        IEnumerable<IOperation> DescendOptional(IOperation? child)
        {
            if (child is null)
            {
                yield break;
            }

            foreach (var descendant in Descend(child))
            {
                yield return descendant;
            }
        }

        IEnumerable<IOperation> DescendInputs(
            IOperation? instance,
            IEnumerable<IArgumentOperation> arguments)
        {
            if (instance is { } value)
            {
                foreach (var descendant in Descend(value))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(value))
                {
                    yield break;
                }
            }

            foreach (var argument in arguments)
            {
                foreach (var descendant in Descend(argument.Value))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(argument.Value))
                {
                    yield break;
                }
            }
        }

        if (operation is IInvocationOperation invocation)
        {
            foreach (var descendant in DescendInputs(
                         invocation.Instance,
                         invocation.Arguments))
            {
                yield return descendant;
            }
            yield return invocation;
            yield break;
        }

        if (operation is IObjectCreationOperation creation)
        {
            foreach (var descendant in DescendInputs(
                         instance: null,
                         arguments: creation.Arguments))
            {
                yield return descendant;
            }
            yield return creation;
            if (creation.Constructor is { } constructor &&
                !operationFacts.MethodCanCompleteNormally(constructor))
            {
                yield break;
            }
            if (creation.Initializer != null)
            {
                foreach (var descendant in
                         Descend(creation.Initializer))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is IObjectOrCollectionInitializerOperation initializer)
        {
            yield return initializer;
            foreach (var item in initializer.Initializers)
            {
                foreach (var descendant in
                         Descend(item))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(item))
                {
                    yield break;
                }
            }
            yield break;
        }

        if (operation is ISimpleAssignmentOperation
            {
                Target: IPropertyReferenceOperation property
            } assignment)
        {
            yield return assignment;
            foreach (var descendant in DescendInputs(
                         property.Instance,
                         property.Arguments))
            {
                yield return descendant;
            }
            foreach (var descendant in
                     Descend(assignment.Value))
            {
                yield return descendant;
            }
            if (operationFacts.MayCompleteNormally(assignment.Value))
            {
                yield return property;
            }
            yield break;
        }

        if (operation is IPropertyReferenceOperation propertyReference)
        {
            foreach (var descendant in DescendInputs(
                         propertyReference.Instance,
                         propertyReference.Arguments))
            {
                yield return descendant;
            }
            yield return propertyReference;
            yield break;
        }

        if (operation is IConditionalOperation factConditional)
        {
            yield return factConditional;
            foreach (var descendant in
                     Descend(factConditional.Condition))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(
                    factConditional.Condition))
            {
                yield break;
            }
            if (factConditional.Condition.ConstantValue is
                { HasValue: true, Value: bool factCondition })
            {
                var branch = factCondition
                    ? factConditional.WhenTrue
                    : factConditional.WhenFalse;
                foreach (var descendant in DescendOptional(branch))
                {
                    yield return descendant;
                }
                yield break;
            }
            foreach (var branch in new[]
                     {
                         factConditional.WhenTrue,
                         factConditional.WhenFalse
                     })
            {
                foreach (var descendant in DescendOptional(branch))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd or
                    BinaryOperatorKind.ConditionalOr
            } factBinary)
        {
            yield return factBinary;
            foreach (var descendant in
                     Descend(factBinary.LeftOperand))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(factBinary.LeftOperand))
            {
                yield break;
            }
            var skipRight = factBinary.LeftOperand.ConstantValue is
            { HasValue: true, Value: bool leftValue } &&
                leftValue == (factBinary.OperatorKind ==
                    BinaryOperatorKind.ConditionalOr);
            if (!skipRight)
            {
                foreach (var descendant in
                         Descend(factBinary.RightOperand))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is ICoalesceOperation factCoalesce)
        {
            yield return factCoalesce;
            foreach (var descendant in
                     Descend(factCoalesce.Value))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(factCoalesce.Value))
            {
                yield break;
            }
            if (!factCoalesce.Value.ConstantValue.HasValue ||
                factCoalesce.Value.ConstantValue.Value == null)
            {
                foreach (var descendant in
                         Descend(factCoalesce.WhenNull))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is IConditionalAccessOperation factAccess)
        {
            yield return factAccess;
            foreach (var descendant in
                     Descend(factAccess.Operation))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(factAccess.Operation) ||
                factAccess.Operation.ConstantValue is
                { HasValue: true, Value: null })
            {
                yield break;
            }
            foreach (var descendant in
                     Descend(factAccess.WhenNotNull))
            {
                yield return descendant;
            }
            yield break;
        }

        yield return operation;

        if (operation is ISwitchExpressionOperation switchExpression &&
            switchExpression.Value.ConstantValue.HasValue)
        {
            foreach (var descendant in
                     Descend(switchExpression.Value))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(switchExpression.Value))
            {
                yield break;
            }
            var input = switchExpression.Value.ConstantValue.Value;
            var switchCompilation = switchExpression.SemanticModel?.Compilation;
            foreach (var arm in switchExpression.Arms)
            {
                var match = switchCompilation == null
                    ? SwitchExpressionSelection.Maybe
                    : SwitchExpressionFacts.GetPatternSelection(
                        switchCompilation,
                        arm.Pattern,
                        input,
                        switchExpression.Value.Type);
                if (match == SwitchExpressionSelection.Never)
                {
                    continue;
                }
                if (arm.Guard != null)
                {
                    foreach (var descendant in
                             Descend(arm.Guard))
                    {
                        yield return descendant;
                    }
                    if (!operationFacts.MayCompleteNormally(arm.Guard))
                    {
                        if (match == SwitchExpressionSelection.Always)
                        {
                            yield break;
                        }
                        continue;
                    }
                    if (arm.Guard.ConstantValue is
                        { HasValue: true, Value: false })
                    {
                        continue;
                    }
                }
                foreach (var descendant in
                         Descend(arm.Value))
                {
                    yield return descendant;
                }
                var guardIsTrue = arm.Guard == null ||
                    arm.Guard.ConstantValue is { HasValue: true, Value: true };
                if (match == SwitchExpressionSelection.Always && guardIsTrue)
                {
                    break;
                }
            }
            yield break;
        }

        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in
                     Descend(child))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(child))
            {
                yield break;
            }
        }
    }

    private static ImmutableArray<RequiresCallTarget> GetPropertyCalls(
        IPropertyReferenceOperation property)
    {
        var getter = property.Property.GetMethod;
        var setter = property.Property.SetMethod;
        if (property.Parent is ISimpleAssignmentOperation assignment &&
            ReferenceEquals(assignment.Target, property))
        {
            return setter == null
                ? []
                : [CreateSetterCall(property, setter, assignment.Value, true)];
        }
        if (property.Parent is ICoalesceAssignmentOperation coalesce &&
            ReferenceEquals(coalesce.Target, property))
        {
            return CreatePropertyReadWriteCalls(
                property, getter, setter, coalesce.Value);
        }
        if (property.Parent is ICompoundAssignmentOperation compound &&
            ReferenceEquals(compound.Target, property) ||
            property.Parent is IIncrementOrDecrementOperation increment &&
            ReferenceEquals(increment.Target, property))
        {
            return CreatePropertyReadWriteCalls(
                property, getter, setter, null);
        }
        if (getter == null ||
            OperationAncestors.Of(property).Any(static ancestor =>
                ancestor is INameOfOperation))
        {
            return [];
        }
        return [CreateGetterCall(property, getter)];
    }

    private static ImmutableArray<RequiresCallTarget>
        CreatePropertyReadWriteCalls(
            IPropertyReferenceOperation property,
            IMethodSymbol? getter,
            IMethodSymbol? setter,
            IOperation? setterValue)
    {
        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>(2);
        if (getter != null)
        {
            calls.Add(CreateGetterCall(property, getter));
        }
        if (setter != null)
        {
            calls.Add(CreateSetterCall(
                property, setter, setterValue, canReplay: false));
        }
        return calls.ToImmutable();
    }

    private static RequiresCallTarget CreateGetterCall(
        IPropertyReferenceOperation property,
        IMethodSymbol getter)
    {
        return new RequiresCallTarget(
            getter,
            property.Instance,
            property.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true);
    }

    private static RequiresCallTarget CreateSetterCall(
        IPropertyReferenceOperation property,
        IMethodSymbol setter,
        IOperation? value,
        bool canReplay)
    {
        var explicitArguments = value == null
            ? ImmutableDictionary<int, IOperation>.Empty
            : ImmutableDictionary<int, IOperation>.Empty.Add(
                setter.Parameters.Length - 1,
                value);
        return new RequiresCallTarget(
            setter,
            property.Instance,
            property.Arguments,
            explicitArguments,
            ImmutableDictionary<int, long>.Empty,
            canReplay);
    }


    private static ImmutableArray<RequiresCallTarget> GetEventCalls(
        IEventReferenceOperation eventReference)
    {
        if (eventReference.Parent is not IEventAssignmentOperation assignment ||
            !ReferenceEquals(assignment.EventReference, eventReference))
        {
            return [];
        }
        var target = assignment.Adds
            ? eventReference.Event.AddMethod
            : eventReference.Event.RemoveMethod;
        if (target == null)
        {
            return [];
        }
        return [new RequiresCallTarget(
            target,
            eventReference.Instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty.Add(
                0,
                assignment.HandlerValue),
            ImmutableDictionary<int, long>.Empty,
            true)];
    }

    private static ExpressionSyntax? GetPropertyExpression(
        SyntaxNode declaration)
    {
        return declaration switch
        {
            PropertyDeclarationSyntax property =>
                property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer =>
                indexer.ExpressionBody?.Expression,
            _ => null
        };
    }

    private static bool IsInsideExceptionHandler(IOperation operation)
    {
        return operation.Syntax.AncestorsAndSelf().Any(
            static syntax =>
                syntax is CatchClauseSyntax or
                    CatchFilterClauseSyntax or
                    FinallyClauseSyntax);
    }

}

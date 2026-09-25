using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static SharpProof.Effects.ManagedAbstractValue;

namespace SharpProof.Effects;

/// <summary>
/// Bounded scalar facts computed by the shared deterministic dataflow engine.
/// Unsupported, over-budget, and cyclic bodies return an explicit incomplete result.
/// </summary>
internal sealed class ManagedAbstractFlow
{
    internal const int MaxAnalyzedBlocks = 256;
    internal const int MaxAnalyzedOperations = 4096;

    /// <summary>
    /// Ceiling on expression/statement nesting walked recursively, matching the
    /// verifier's expression-depth budget. Deeply nested trees abstain instead
    /// of exhausting the stack, because StackOverflowException is uncatchable
    /// and would take the compiler host down with it.
    /// </summary>
    private const int MaximumWalkDepth = 256;

    // Instances are shared per compilation across Roslyn's concurrent analysis
    // threads, so the recursion guard cannot live in an instance field.
    [ThreadStatic]
    private static int s_walkDepth;

    // Roslyn's CFG copies do not retain a semantic model. Keep the method being
    // solved on the analysis thread so those copies can still be classified as
    // receiver-backed primary-constructor parameters.
    [ThreadStatic]
    private static IMethodSymbol? s_currentMethod;

    private static readonly ConditionalWeakTable<Compilation, ManagedAbstractFlow> Sessions = new();
    private static readonly ConditionalWeakTable<
        Compilation,
        ConcurrentDictionary<(SyntaxTree Tree, int Start, int Length), bool>>
        CompileTimeUnreachableStatementCache = new();
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _contractApi;
    private readonly INamedTypeSymbol? _inRangeAttribute;
    private readonly INamedTypeSymbol? _notNullAttribute;
    private readonly INamedTypeSymbol? _positiveAttribute;
    private readonly TrustedBoundaryPolicy _trustedBoundaries;
    private readonly DefiniteOperationFacts _completionFacts;

    private ManagedAbstractFlow(Compilation compilation)
        : this(compilation, new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation))
    {
    }

    private ManagedAbstractFlow(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _compilation = compilation;
        _apiSpecs = ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs));
        var contractApi = ContractApiIdentityResolver.ForCompilation(compilation);
        _contractApi = contractApi.Contract;
        _notNullAttribute = contractApi.ResolveAttribute(ContractApiMetadata.NotNull);
        _positiveAttribute = contractApi.ResolveAttribute(ContractApiMetadata.Positive);
        _inRangeAttribute = contractApi.ResolveAttribute(ContractApiMetadata.InRange);
        _trustedBoundaries =
            TrustedBoundaryPolicy.ForCompilation(compilation);
        _completionFacts = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);
    }

    internal static ManagedAbstractFlow ForCompilation(Compilation compilation)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        return Sessions.GetValue(compilation, static value => new(value));
    }

    internal static ManagedAbstractFlow Create(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs)
    {
        return new(compilation, apiSpecs);
    }

    internal ManagedFlowState CreateEntryState(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));

        var state = ManagedFlowState.Empty;
        foreach (var parameter in method.Parameters)
        {
            var value = TopForType(parameter.Type);
            if (parameter.RefKind == RefKind.None)
            {
                value = ApplyAttributes(value, parameter.GetAttributes());
            }

            state = state.Set(parameter, value);
        }
        return state;
    }

    /// <summary>
    /// Overrides the solver's iteration bound so the non-convergence path can be
    /// exercised without changing the production default.
    /// </summary>
    internal ManagedFlowAnalysis AnalyzeWithIterationLimitForTesting(
        IMethodSymbol method,
        ControlFlowGraph graph,
        ManagedFlowState? entryState,
        int maxIterations,
        CancellationToken cancellationToken)
    {
        return Analyze(method, graph, entryState, cancellationToken, maxIterations);
    }

    internal ManagedFlowAnalysis Analyze(
        IMethodSymbol method, ControlFlowGraph graph, ManagedFlowState? entryState, CancellationToken cancellationToken,
        int? maxIterationsOverride = null)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        graph = ArgumentNullGuard.NotNull(graph, nameof(graph));

        var budgetReason = CheckBudget(graph, cancellationToken);
        if (budgetReason != EffectAnalysisIncompleteReason.None)
        {
            return ManagedFlowAnalysis.BudgetExceeded(budgetReason);
        }

        if (!IsAcyclic(graph))
        {
            return ManagedFlowAnalysis.Cyclic();
        }

        var result = new ManagedFlowResult(this, method);

        // CheckBudget bounds the source CFG, but CreateDataflowGraph adds a
        // synthetic block per edge, so the iteration limit has to be taken from
        // the expanded graph rather than from MaxAnalyzedBlocks.
        var previousMethod = s_currentMethod;
        s_currentMethod = method;
        try
        {
            var dataflowGraph = CreateDataflowGraph(
                method,
                graph,
                result,
                cancellationToken);
            try
            {
                var converged = ForwardDataflowAnalysis.AnalyzeWithoutResult(dataflowGraph,
                    FlowDomain.Instance, entryState ?? CreateEntryState(method),
                    new ForwardDataflowAnalysisOptions(
                        maxIterations: maxIterationsOverride
                            ?? dataflowGraph.Blocks.Length * 4));
                if (!converged)
                {
                    // A domain-order defect must not escape the analyzer
                    // callback. Preserve the fail-closed incomplete-summary
                    // path used for other managed-flow limits.
                    return ManagedFlowAnalysis.BudgetExceeded(
                        EffectAnalysisIncompleteReason.BlockBudgetExceeded);
                }
            }
            catch (DataflowConvergenceException)
            {
                // Every other resource limit here degrades to an incomplete summary.
                // Reaching the iteration bound must not escape as AD0001.
                return ManagedFlowAnalysis.BudgetExceeded(
                    EffectAnalysisIncompleteReason.BlockBudgetExceeded);
            }

            return ManagedFlowAnalysis.Complete(result);
        }
        finally
        {
            s_currentMethod = previousMethod;
        }
    }

    internal ManagedAbstractValue Evaluate(
        IOperation operation,
        ManagedFlowState state,
        IMethodSymbol? currentMethod = null)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        state = ArgumentNullGuard.NotNull(state, nameof(state));
        var previousMethod = s_currentMethod;
        if (currentMethod is not null)
        {
            s_currentMethod = currentMethod;
        }

        try
        {
            return EvaluateCore(operation, state);
        }
        finally
        {
            s_currentMethod = previousMethod;
        }
    }

    private DataflowGraph<ManagedFlowState> CreateDataflowGraph(
        IMethodSymbol method,
        ControlFlowGraph graph,
        ManagedFlowResult result,
        CancellationToken cancellationToken)
    {
        var blocks = ImmutableArray.CreateBuilder<DataflowBlock<ManagedFlowState>>();
        var edges = ImmutableArray.CreateBuilder<DataflowEdge>();
        var entryBlockIds = new int[graph.Blocks.Length];
        var transferBlockIds = new int[graph.Blocks.Length];
        var elidedInvocations = graph.OriginalOperation.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Where(invocation =>
                !IsAssumption(invocation) &&
                _completionFacts.IsConditionallyElided(invocation))
            .ToImmutableArray();
        foreach (var block in graph.Blocks)
        {
            var captured = block;
            entryBlockIds[block.Ordinal] = blocks.Count;
            blocks.Add(new(blocks.Count, static state => state));
            transferBlockIds[block.Ordinal] = blocks.Count;
            blocks.Add(new(
                blocks.Count,
                state => TransferBlock(state, captured, result, cancellationToken)));
            edges.Add(new(entryBlockIds[block.Ordinal], transferBlockIds[block.Ordinal]));
        }

        var finallyContinuations = new Dictionary<ControlFlowRegion, HashSet<int>>();
        foreach (var block in graph.Blocks)
        {
            foreach (var (branch, expected) in Successors(block))
            {
                var condition = block.BranchValue;
                var edgeBlock = blocks.Count;
                blocks.Add(new(edgeBlock, state => expected.HasValue && condition != null &&
                    !result.HasMutation(condition)
                    ? Assume(state, condition, expected.Value) : state));
                edges.Add(new(transferBlockIds[block.Ordinal], edgeBlock));
                if (branch.FinallyRegions.IsDefaultOrEmpty)
                {
                    edges.Add(new(
                        edgeBlock,
                        entryBlockIds[branch.Destination!.Ordinal]));
                    continue;
                }

                var finalRegions = branch.FinallyRegions;
                edges.Add(new(
                    edgeBlock,
                    entryBlockIds[finalRegions[0].FirstBlockOrdinal]));
                for (var index = 0; index < finalRegions.Length; index++)
                {
                    var continuation = index + 1 < finalRegions.Length
                        ? finalRegions[index + 1].FirstBlockOrdinal
                        : branch.Destination!.Ordinal;
                    if (!finallyContinuations.TryGetValue(
                            finalRegions[index],
                            out var targets))
                    {
                        targets = [];
                        finallyContinuations.Add(finalRegions[index], targets);
                    }
                    targets.Add(continuation);
                }
            }
        }

        // Roslyn lowers conditional arguments into CFG blocks outside the
        // invocation operation. If the invocation is elided, those blocks are
        // not executed, so their branch refinements must not be the only input
        // to the continuation. Add a direct state-preserving edge from every
        // lowered block in the elided call back to the call block. The regular
        // edge remains for the emitted case; joining this edge restores the
        // pre-call facts when the call is omitted.
        foreach (var invocation in elidedInvocations)
        {
            var callBlock = graph.Blocks.FirstOrDefault(block =>
                BlockOperations(block).Any(operation =>
                    operation is IInvocationOperation candidate &&
                    SameSyntax(candidate.Syntax, invocation.Syntax)));
            if (callBlock == null)
            {
                continue;
            }

            foreach (var block in graph.Blocks)
            {
                if (block.Ordinal != callBlock.Ordinal &&
                    BlockOperations(block).Any(operation =>
                        IsWithin(operation.Syntax, invocation.Syntax)))
                {
                    edges.Add(new(
                        transferBlockIds[block.Ordinal],
                        entryBlockIds[callBlock.Ordinal]));
                }
            }
        }

        AddExceptionalHandlerEntries();
        AddFinallyContinuations();

        return new(blocks, edges);

        static IEnumerable<IOperation> BlockOperations(BasicBlock block)
        {
            return block.Operations
                .SelectMany(static operation => operation.DescendantsAndSelf())
                .Append(block.BranchValue)
                .Where(static operation => operation != null)
                .Select(static operation => operation!);
        }

        static bool SameSyntax(SyntaxNode left, SyntaxNode right)
        {
            return left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
        }

        static bool IsWithin(SyntaxNode candidate, SyntaxNode container)
        {
            return SameSyntax(candidate, container) ||
                candidate.SyntaxTree == container.SyntaxTree &&
                container.Span.Contains(candidate.Span);
        }

        void AddExceptionalHandlerEntries()
        {
            var regions = GetControlFlowRegions(graph.Root);
            var catches = graph.OriginalOperation.DescendantsAndSelf()
                .OfType<ICatchClauseOperation>()
                .Where(catchClause =>
                    !ConversionOwnershipClassifier.IsInsideNestedCallable(
                        catchClause,
                        graph.OriginalOperation))
                .OrderBy(static catchClause => catchClause.Syntax.SpanStart)
                .ToArray();
            var tryOperations = graph.OriginalOperation.DescendantsAndSelf()
                .OfType<ITryOperation>()
                .Where(tryOperation =>
                    !ConversionOwnershipClassifier.IsInsideNestedCallable(
                        tryOperation,
                        graph.OriginalOperation))
                .OrderBy(static tryOperation => tryOperation.Syntax.SpanStart)
                .ToArray();
            var tryRegions = regions
                .Where(static region =>
                    region.Kind == ControlFlowRegionKind.Try)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray();
            var writtenStoragesByTry = new Dictionary<
                ControlFlowRegion,
                (bool ForgetAll, ImmutableArray<ISymbol> Storages)>();
            for (var index = 0; index < tryRegions.Length; index++)
            {
                var writtenStorages = index < tryOperations.Length
                    ? GetWrittenStorages(tryOperations[index])
                    : (true, ImmutableArray<ISymbol>.Empty);
                writtenStoragesByTry.Add(
                    tryRegions[index],
                    writtenStorages);
            }

            var handlerEntries = new List<(
                ControlFlowRegion Region,
                IOperation? Operation,
                bool IsFilter,
                bool HasFilter)>();
            var catchRegions = regions
                .Where(static region =>
                    region.Kind == ControlFlowRegionKind.Catch)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray();
            for (var index = 0; index < catchRegions.Length; index++)
            {
                var catchClause = index < catches.Length
                    ? catches[index]
                    : null;
                handlerEntries.Add((
                    catchRegions[index],
                    catchClause?.Handler,
                    IsFilter: false,
                    HasFilter: catchClause?.Filter != null));
            }

            var filterRegions = regions
                .Where(static region =>
                    region.Kind == ControlFlowRegionKind.Filter)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray();
            var filteredCatches = catches
                .Where(static catchClause => catchClause.Filter != null)
                .ToArray();
            for (var index = 0; index < filterRegions.Length; index++)
            {
                handlerEntries.Add((
                    filterRegions[index],
                    index < filteredCatches.Length
                        ? filteredCatches[index].Filter
                        : null,
                    IsFilter: true,
                    HasFilter: true));
            }

            if (handlerEntries.Count == 0)
            {
                return;
            }

            var reachability = OperationEffectScanner.CreateReachabilityProbe(
                _compilation,
                method,
                graph.OriginalOperation,
                result);
            foreach (var handler in handlerEntries)
            {
                // A filtered catch is reached through its filter region. An
                // edge straight into its handler would bypass the filter test.
                if (handler.HasFilter && !handler.IsFilter ||
                    handler.Operation != null &&
                    !reachability.IsReachable(handler.Operation))
                {
                    continue;
                }

                var tryRegion = handler.Region.EnclosingRegion?
                    .NestedRegions.FirstOrDefault(static region =>
                        region.Kind == ControlFlowRegionKind.Try);
                if (tryRegion == null)
                {
                    continue;
                }

                (bool ForgetAll, ImmutableArray<ISymbol> Storages) writes =
                    writtenStoragesByTry.TryGetValue(
                    tryRegion,
                    out var writtenStorages)
                    ? writtenStorages
                    : (true, ImmutableArray<ISymbol>.Empty);
                var havocBlock = blocks.Count;
                blocks.Add(new(
                    havocBlock,
                    state => writes.ForgetAll
                        ? state.Forget()
                        : ForgetStorages(state, writes.Storages)));
                edges.Add(new(
                    entryBlockIds[tryRegion.FirstBlockOrdinal],
                    havocBlock));
                edges.Add(new(
                    havocBlock,
                    entryBlockIds[handler.Region.FirstBlockOrdinal]));
            }

            (bool ForgetAll, ImmutableArray<ISymbol> Storages)
                GetWrittenStorages(IOperation operation)
            {
                var storages = new HashSet<ISymbol>(
                    SymbolEqualityComparer.Default);
                var pending = new Stack<IOperation>();
                pending.Push(operation);
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    if (current is IAnonymousFunctionOperation or
                        ILocalFunctionOperation)
                    {
                        continue;
                    }

                    switch (current)
                    {
                        case IVariableDeclaratorOperation declarator:
                            storages.Add(declarator.Symbol);
                            break;
                        case ISimpleAssignmentOperation assignment:
                            if (!AddTrackedStorage(assignment.Target))
                            {
                                return (true, []);
                            }
                            break;
                        case ICompoundAssignmentOperation compound:
                            if (!AddTrackedStorage(compound.Target))
                            {
                                return (true, []);
                            }
                            break;
                        case IIncrementOrDecrementOperation increment:
                            if (!AddTrackedStorage(increment.Target))
                            {
                                return (true, []);
                            }
                            break;
                        case IArgumentOperation argument when
                            argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out:
                            if (!AddTrackedStorage(argument.Value))
                            {
                                return (true, []);
                            }
                            break;
                        case IInvocationOperation invocation when
                            invocation.TargetMethod.MethodKind == MethodKind.LocalFunction ||
                            invocation.TargetMethod.ContainingType.TypeKind ==
                                TypeKind.Delegate ||
                            invocation.Arguments.Any(static argument =>
                                CanCarryDelegate(argument.Value)):
                        case IDynamicInvocationOperation or
                            IFunctionPointerInvocationOperation:
                            return (true, []);
                    }

                    foreach (var child in current.ChildOperations)
                    {
                        pending.Push(child);
                    }
                }

                return (false, [.. storages]);

                bool AddTrackedStorage(IOperation target)
                {
                    target = Unwrap(target);
                    if (target is IFlowCaptureReferenceOperation capture)
                    {
                        var resolved = result.ResolveCoalesceAssignmentTarget(
                            capture);
                        return !ReferenceEquals(resolved, capture) &&
                            AddTrackedStorage(resolved);
                    }

                    if (target is IConditionalOperation conditional)
                    {
                        return conditional.WhenFalse != null &&
                            AddTrackedStorage(conditional.WhenTrue) &&
                            AddTrackedStorage(conditional.WhenFalse);
                    }

                    switch (target)
                    {
                        case ILocalReferenceOperation local:
                            storages.Add(local.Local);
                            break;
                        case IParameterReferenceOperation parameter:
                            storages.Add(parameter.Parameter);
                            break;
                    }

                    return true;
                }
            }

            static ManagedFlowState ForgetStorages(
                ManagedFlowState state,
                ImmutableArray<ISymbol> storages)
            {
                foreach (var storage in storages)
                {
                    var type = storage switch
                    {
                        ILocalSymbol local => local.Type,
                        IParameterSymbol parameter => parameter.Type,
                        _ => null
                    };
                    state = state.Set(storage, TopForType(type));
                }

                return state;
            }
        }

        void AddFinallyContinuations()
        {
            foreach (var continuation in finallyContinuations)
            {
                var region = continuation.Key;
                var targets = continuation.Value;
                foreach (var block in graph.Blocks)
                {
                    if (!IsFinallyRegionExit(block, region))
                    {
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        edges.Add(new(
                            transferBlockIds[block.Ordinal],
                            entryBlockIds[target]));
                    }
                }
            }
        }

        static ControlFlowRegion[] GetControlFlowRegions(
            ControlFlowRegion root)
        {
            var regions = new List<ControlFlowRegion>();
            var pending = new Stack<ControlFlowRegion>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var region = pending.Pop();
                regions.Add(region);
                foreach (var nested in region.NestedRegions)
                {
                    pending.Push(nested);
                }
            }

            return [.. regions];
        }

        static bool IsFinallyRegionExit(
            BasicBlock block,
            ControlFlowRegion region)
        {
            var isWithinRegion = false;
            for (var enclosing = block.EnclosingRegion;
                 enclosing != null;
                 enclosing = enclosing.EnclosingRegion)
            {
                if (enclosing.Kind == region.Kind &&
                    enclosing.FirstBlockOrdinal == region.FirstBlockOrdinal &&
                    enclosing.LastBlockOrdinal == region.LastBlockOrdinal)
                {
                    isWithinRegion = true;
                    break;
                }

                if (enclosing.Kind == ControlFlowRegionKind.Finally)
                {
                    return false;
                }
            }

            return isWithinRegion &&
                (block.FallThroughSuccessor?.Semantics ==
                     ControlFlowBranchSemantics.StructuredExceptionHandling ||
                 block.ConditionalSuccessor?.Semantics ==
                     ControlFlowBranchSemantics.StructuredExceptionHandling);
        }
    }

    private ManagedFlowState TransferBlock(
        ManagedFlowState state, BasicBlock block, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        state = TransferMany(state, block.Operations, result, cancellationToken);
        return block.BranchValue == null
            ? state
            : Transfer(state, block.BranchValue, result, cancellationToken);
    }

    private ManagedFlowState Transfer(
        ManagedFlowState state, IOperation operation, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        if (state.IsBottom)
        {
            return state;
        }

        if (!TryEnterWalk())
        {
            // Abandoning the sub-tree means we no longer know what it wrote, so
            // every fact has to be dropped rather than carried forward stale.
            return state.Forget();
        }

        try
        {
            return TransferCore(state, operation, result, cancellationToken);
        }
        finally
        {
            ExitWalk();
        }
    }

    private ManagedFlowState TransferCore(
        ManagedFlowState state, IOperation operation, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!(operation is IInvocationOperation assumption && IsAssumption(assumption)) &&
            _completionFacts.IsConditionallyElided(operation))
        {
            result.Record(operation, state);
            return state;
        }
        switch (operation)
        {
            case IAnonymousFunctionOperation or ILocalFunctionOperation:
                break;
            case IVariableDeclaratorOperation declarator:
                if (IsUntrackedManagedReference(declarator.Symbol.RefKind))
                {
                    state = state.WithUntrackedAlias();
                }

                if (declarator.Initializer == null)
                {
                    state = state.Set(declarator.Symbol, ManagedAbstractValue.TopForType(declarator.Symbol.Type));
                }
                else
                {
                    var hasMutation = result.HasMutation(
                        declarator.Initializer.Value);
                    state = Transfer(state, declarator.Initializer.Value, result, cancellationToken);
                    state = state.Set(
                        declarator.Symbol,
                        hasMutation
                            ? TopForType(declarator.Symbol.Type)
                            : EvaluateCore(declarator.Initializer.Value, state));
                }
                break;
            case IFlowCaptureOperation capture:
                result.RecordCoalesceAssignmentCapture(capture);
                var captureHasMutation = result.HasMutation(
                    capture.Value);
                state = Transfer(state, capture.Value, result, cancellationToken);
                state = state.Set(
                    capture.Id,
                    captureHasMutation
                        ? TopForType(capture.Value.Type)
                        : EvaluateCore(capture.Value, state));
                break;
            case ISimpleAssignmentOperation assignment:
                state = MarkUntrackedAlias(
                    state,
                    assignment.Target,
                    out var aliasesUntrackedStorage);

                var valueHasMutation = result.HasMutation(
                    assignment.Value);
                state = TransferMany(state, assignment.ChildOperations, result, cancellationToken);
                var assignedValue = valueHasMutation
                    ? TopForType(assignment.Type)
                    : EvaluateCore(assignment.Value, state);
                if (!aliasesUntrackedStorage)
                {
                    state = SetStorage(
                        state,
                        assignment.Target,
                        assignedValue);
                    state = SetStorage(
                        state,
                        result.ResolveCoalesceAssignmentTarget(assignment.Target),
                        assignedValue);
                }
                if (IsNonLocalTarget(assignment.Target))
                {
                    state = state.ForgetByReferenceParameters();
                }
                break;
            case ICompoundAssignmentOperation compound:
                state = MarkUntrackedAlias(
                    state,
                    compound.Target,
                    out var compoundAliasesUntrackedStorage);

                state = TransferMany(state, compound.ChildOperations, result, cancellationToken);
                if (!compoundAliasesUntrackedStorage)
                {
                    var compoundResult = TopForType(compound.Type);
                    state = SetStorage(
                        state,
                        compound.Target,
                        compoundResult);
                    state = SetStorage(
                        state,
                        result.ResolveCoalesceAssignmentTarget(compound.Target),
                        compoundResult);
                }
                if (IsNonLocalTarget(compound.Target))
                {
                    state = state.ForgetByReferenceParameters();
                }
                break;
            case IIncrementOrDecrementOperation increment:
                state = MarkUntrackedAlias(
                    state,
                    increment.Target,
                    out var incrementAliasesUntrackedStorage);

                state = Transfer(state, increment.Target, result, cancellationToken);
                if (!incrementAliasesUntrackedStorage)
                {
                    state = SetStorage(state, increment.Target, Increment(increment, state));
                }
                if (IsNonLocalTarget(increment.Target))
                {
                    state = state.ForgetByReferenceParameters();
                }
                break;
            case IInvocationOperation invocation:
                state = TransferMany(state, invocation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return IsAssumption(invocation) ? Assume(state, invocation.Arguments[0].Value, true)
                    : HavocCall(state, invocation.TargetMethod, invocation.Arguments);
            case IObjectCreationOperation creation:
                state = TransferMany(state, creation.Arguments, result, cancellationToken);
                result.Record(operation, state);
                state = HavocArguments(state, creation.Arguments);
                state = state.ForgetByReferenceParameters();
                return creation.Initializer == null ? state
                    : Transfer(state, creation.Initializer, result, cancellationToken);
            case IPropertyReferenceOperation:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return state.ForgetByReferenceParameters();
            case IDynamicInvocationOperation or IFunctionPointerInvocationOperation:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return state.Forget();
            case IReturnOperation or IThrowOperation:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return ManagedFlowState.Bottom;
            default:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                break;
        }
        result.Record(operation, state);
        return state;
    }

    private ManagedFlowState TransferMany(
        ManagedFlowState state, IEnumerable<IOperation> operations, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            state = Transfer(state, operation, result, cancellationToken);
            if (state.IsBottom)
            {
                break;
            }
        }
        return state;
    }

    private ManagedAbstractValue Increment(IIncrementOrDecrementOperation operation, ManagedFlowState state)
    {
        return TryIncrement(operation, state, out var updated) &&
               FitsType(updated, operation.Type)
            ? Integer(updated)
            : TopForType(operation.Type);
    }

    private bool TryIncrement(
        IIncrementOrDecrementOperation operation,
        ManagedFlowState state,
        out IntervalValue interval)
    {
        interval = default;
        var @operator = operation.Kind == OperationKind.Increment
            ? BinaryOperatorKind.Add
            : BinaryOperatorKind.Subtract;
        return EvaluateCore(operation.Target, state).TryGetInteger(out var target) &&
            TryArithmetic(
                @operator,
                target,
                IntervalValue.Constant(1),
                out interval);
    }

    internal ManagedFlowState Assume(ManagedFlowState state, IOperation condition, bool expected)
    {
        return Assume(state, condition, expected, conditionValue: null);
    }

    private ManagedFlowState Assume(
        ManagedFlowState state,
        IOperation condition,
        bool expected,
        ManagedAbstractValue? conditionValue)
    {
        condition = Unwrap(condition);
        if ((conditionValue ?? EvaluateCore(condition, state))
            .TryGetBoolean(out var constant))
        {
            return constant == expected ? state : ManagedFlowState.Bottom;
        }

        return condition switch
        {
            IUnaryOperation unary when IsBuiltinBooleanNot(unary) =>
                Assume(state, unary.Operand, !expected),
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd,
                OperatorMethod: null,
                IsLifted: false
            } binary when expected =>
                Assume(Assume(state, binary.LeftOperand, true), binary.RightOperand, true),
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalOr,
                OperatorMethod: null,
                IsLifted: false
            } binary when !expected =>
                Assume(Assume(state, binary.LeftOperand, false), binary.RightOperand, false),
            IBinaryOperation { OperatorMethod: null, IsLifted: false } binary =>
                AssumeComparison(state, binary.LeftOperand, binary.RightOperand,
                binary.OperatorKind, expected),
            IIsNullOperation isNull when TryStorage(isNull.Operand, out var storage) =>
                Refine(state, storage, BinaryOperatorKind.Equals, Null, expected),
            IIsPatternOperation pattern => AssumeNullPattern(state, pattern, expected),
            _ when TryStorage(condition, out var storage) =>
                state.Set(storage, Boolean(expected)),
            _ => state
        };
    }

    private ManagedFlowState AssumeComparison(
        ManagedFlowState state, IOperation left, IOperation right, BinaryOperatorKind @operator, bool expected)
    {
        var hasLeftStorage = TryStorage(left, out var leftStorage);
        var hasRightStorage = TryStorage(right, out var rightStorage);
        var leftValue = EvaluateCore(left, state);
        var rightValue = EvaluateCore(right, state);
        if (hasLeftStorage)
        {
            state = Refine(state, leftStorage, @operator, rightValue, expected);
        }

        return hasRightStorage
            ? Refine(
                state,
                rightStorage,
                CSharpScalarSemantics.ReverseBinary(@operator),
                leftValue,
                expected)
            : state;
    }

    private static ManagedFlowState AssumeNullPattern(
        ManagedFlowState state, IIsPatternOperation operation, bool expected)
    {
        var pattern = operation.Pattern;
        var negated = false;
        while (pattern is INegatedPatternOperation not)
        {
            negated = !negated;
            pattern = not.Pattern;
        }
        return pattern is IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } } &&
               TryStorage(operation.Value, out var storage)
            ? Refine(state, storage, BinaryOperatorKind.Equals, Null, expected != negated)
            : state;
    }

    internal static ManagedFlowState Refine(
        ManagedFlowState state, object storage, BinaryOperatorKind @operator, ManagedAbstractValue value, bool expected)
    {
        var current = state.Get(storage);
        if (value.TryGetInteger(out var integer))
        {
            return state.Set(storage,
                RefineInteger(current, @operator, integer, expected));
        }

        if (value.TryGetBoolean(out var boolean) && current.IsBoolean &&
            @operator is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
        {
            var booleanEquals = @operator == BinaryOperatorKind.Equals;
            return state.Set(storage, Boolean(
                expected == booleanEquals ? boolean : !boolean));
        }
        if (!value.IsDefinitelyNull)
        {
            return state;
        }

        if (!current.TryGetNullness(out var nullness))
        {
            return state;
        }

        if (@operator is not (
            BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return state;
        }

        var equals = expected == (@operator == BinaryOperatorKind.Equals);
        var refined = equals ? NullnessDomain.Instance.AssumeNull(nullness)
            : NullnessDomain.Instance.AssumeNonNull(nullness);
        return state.Set(storage, Reference(refined, current.Cardinality));
    }

    private static ManagedAbstractValue RefineInteger(
        ManagedAbstractValue current,
        BinaryOperatorKind @operator,
        IntervalValue value,
        bool expected)
    {
        if (!current.TryGetInteger(out var interval))
        {
            return current;
        }

        var normalized = expected
            ? @operator
            : CSharpScalarSemantics.NegateBinary(@operator);
        var domain = IntervalDomain.Instance;
        var refined = normalized switch
        {
            BinaryOperatorKind.Equals => Intersect(interval, value),
            BinaryOperatorKind.NotEquals when value.IsSingleton &&
                interval.IsSingleton &&
                interval.SingletonValue == value.SingletonValue =>
                IntervalValue.Bottom,
            BinaryOperatorKind.LessThan when
                value.UpperBound is > long.MinValue =>
                domain.AssumeAtMost(interval, value.UpperBound.Value - 1),
            BinaryOperatorKind.LessThanOrEqual when
                value.UpperBound.HasValue =>
                domain.AssumeAtMost(interval, value.UpperBound.Value),
            BinaryOperatorKind.GreaterThan when
                value.LowerBound is < long.MaxValue =>
                domain.AssumeAtLeast(interval, value.LowerBound.Value + 1),
            BinaryOperatorKind.GreaterThanOrEqual when
                value.LowerBound.HasValue =>
                domain.AssumeAtLeast(interval, value.LowerBound.Value),
            _ => interval
        };

        // The zero-exclusion fact must follow from the refined interval for
        // every bound shape: deriving it only for range bounds made a joined
        // (larger) bound more precise than a singleton one, breaking the
        // monotone transfer the fixpoint requires.
        return refined.IsBottom
            ? Bottom
            : Integer(
                refined,
                current.ExcludesZero ||
                !refined.Contains(0) ||
                (normalized == BinaryOperatorKind.NotEquals &&
                    value.IsSingleton &&
                    value.SingletonValue == 0));
    }

    private static IntervalValue Intersect(
        IntervalValue current, IntervalValue restriction)
    {
        var domain = IntervalDomain.Instance;
        var refined = restriction.LowerBound is { } lower
            ? domain.AssumeAtLeast(current, lower)
            : current;
        return restriction.UpperBound is { } upper
            ? domain.AssumeAtMost(refined, upper)
            : refined;
    }

    private ManagedAbstractValue EvaluateCore(IOperation operation, ManagedFlowState state)
    {
        if (!TryEnterWalk())
        {
            return ManagedAbstractValue.Unknown;
        }

        try
        {
            return EvaluateBounded(operation, state);
        }
        finally
        {
            ExitWalk();
        }
    }

    private static bool TryEnterWalk()
    {
        if (s_walkDepth >= MaximumWalkDepth)
        {
            return false;
        }

        s_walkDepth++;
        return true;
    }

    private static void ExitWalk()
    {
        s_walkDepth--;
    }

    private ManagedAbstractValue EvaluateBounded(IOperation operation, ManagedFlowState state)
    {
        operation = Unwrap(operation);
        if (operation.ConstantValue.HasValue)
        {
            return FromConstant(operation.ConstantValue.Value, operation.Type);
        }

        return operation switch
        {
            IParameterReferenceOperation parameter =>
                IsReceiverBackedParameter(parameter.Parameter, parameter)
                    ? TopForType(parameter.Type)
                    : state.Get(parameter.Parameter),
            ILocalReferenceOperation local => state.Get(local.Local),
            IFlowCaptureReferenceOperation capture => state.Get(capture.Id),
            IDefaultValueOperation value => DefaultForType(value.Type),
            IInstanceReferenceOperation or IConditionalAccessInstanceOperation or
                ITypeOfOperation => NonNull,
            IObjectCreationOperation creation =>
                ManagedAbstractValue.IsEmptyNullableCreation(creation)
                    ? Null
                    : NonNull,
            IArrayCreationOperation array => EvaluateArray(array, state),
            IPropertyReferenceOperation property => EvaluateProperty(property, state),
            IInvocationOperation invocation => ReturnValue(invocation.TargetMethod, invocation.Type),
            IIsNullOperation isNull => NullTest(isNull, state),
            IConversionOperation conversion => ConvertValue(conversion, state),
            IUnaryOperation unary => EvaluateUnary(unary, state),
            IBinaryOperation { OperatorMethod: null, IsLifted: false } binary =>
                Binary(binary.OperatorKind,
                EvaluateCore(binary.LeftOperand, state), EvaluateCore(binary.RightOperand, state), binary.Type),
            IConditionalOperation conditional => EvaluateConditional(conditional, state),
            ICoalesceOperation coalesce => EvaluateCoalesce(coalesce, state),
            ISimpleAssignmentOperation assignment => EvaluateCore(assignment.Value, state),
            IFlowCaptureOperation capture => EvaluateCore(capture.Value, state),
            _ => TopForType(operation.Type)
        };
    }

    private ManagedAbstractValue EvaluateArray(IArrayCreationOperation array, ManagedFlowState state)
    {
        if (array.DimensionSizes.Length != 1 ||
            !EvaluateCore(array.DimensionSizes[0], state).TryGetInteger(out var size))
        {
            return NonNull;
        }

        return Reference(NullnessValue.NonNull, IntervalDomain.Instance.AssumeAtLeast(size, 0));
    }

    private ManagedAbstractValue EvaluateProperty(IPropertyReferenceOperation property, ManagedFlowState state)
    {
        if (CompilerIdentityBridge.IsIntrinsicSequenceLength(property))
        {
            var instance = property.Instance!;
            var receiver = EvaluateCore(instance, state);
            if (receiver.TryGetCardinality(out var length))
            {
                return Integer(length);
            }

            if (instance.Type is IArrayTypeSymbol ||
                instance.Type?.SpecialType == SpecialType.System_String)
            {
                return Integer(IntervalValue.Range(
                    0, property.Type?.SpecialType == SpecialType.System_Int64 ? long.MaxValue : int.MaxValue));
            }
        }
        return ReturnValue(property.Property.GetMethod, property.Type);
    }

    private ManagedAbstractValue ReturnValue(IMethodSymbol? method, ITypeSymbol? type)
    {
        var value = TopForType(type);
        if (method != null &&
            _trustedBoundaries.AuthorizesDeclaredContracts(method))
        {
            value = ApplyAttributes(
                value,
                method.GetReturnTypeAttributes());
        }
        if (method == null ||
            !_apiSpecs.TryGet(method, out var spec) ||
            !value.TryGetNullness(out var nullness))
        {
            return value;
        }

        nullness = spec.Template.Facets.Nullness.Result switch
        {
            SpecNullness.NonNull =>
                NullnessDomain.Instance.AssumeNonNull(nullness),
            SpecNullness.Null => NullnessValue.Null,
            _ => nullness
        };
        if (nullness == NullnessValue.Null)
        {
            return Null;
        }

        var cardinality = spec.Template.Facets.Cardinality.Result switch
        {
            SpecCardinality.Empty => IntervalValue.Constant(0),
            SpecCardinality.NonEmpty => IntervalValue.Range(1, null),
            SpecCardinality.Exact when
                spec.Template.Facets.Cardinality.ExactCount is { } count =>
                IntervalValue.Constant(count),
            _ => value.Cardinality
        };
        return Reference(nullness, cardinality);
    }

    private ManagedAbstractValue NullTest(IIsNullOperation operation, ManagedFlowState state)
    {
        return EvaluateCore(operation.Operand, state).TryGetNullness(out var value)
            ? value switch
            {
                NullnessValue.Null => Boolean(true),
                NullnessValue.NonNull => Boolean(false),
                _ => BooleanUnknown
            }
            : BooleanUnknown;
    }

    private ManagedAbstractValue ConvertValue(IConversionOperation conversion, ManagedFlowState state)
    {
        var operand = EvaluateCore(conversion.Operand, state);
        if (string.Equals(
                conversion.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal) &&
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                .GetConversion(conversion).IsBoxing)
        {
            return IsNullableType(conversion.Operand.Type) &&
                   operand.TryGetNullness(out var boxedNullness)
                ? Reference(boxedNullness, operand.Cardinality)
                : NonNull;
        }

        if (IsNullableType(conversion.Type) &&
            conversion.OperatorMethod == null &&
            !conversion.Conversion.IsUserDefined)
        {
            if (operand.TryGetNullness(out var nullableNullness))
            {
                return Reference(nullableNullness, operand.Cardinality);
            }

            return IsNullableType(conversion.Operand.Type)
                ? Reference(NullnessValue.MaybeNull)
                : NonNull;
        }

        if (!conversion.IsTryCast &&
            conversion.OperatorMethod == null &&
            !conversion.Conversion.IsUserDefined &&
            string.Equals(
                conversion.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal) &&
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                .GetConversion(conversion).IsNumeric &&
            IntegerType(conversion.Operand.Type, out var sourceInteger) &&
            IntegerType(conversion.Type, out var targetInteger) &&
            operand.TryGetInteger(out var interval) &&
            (sourceInteger.Minimum >= targetInteger.Minimum &&
             sourceInteger.Maximum <= targetInteger.Maximum ||
             FitsType(interval, conversion.Type)))
        {
            return Integer(interval);
        }

        return !conversion.IsTryCast && conversion.OperatorMethod == null && conversion.Conversion.IsReference &&
               operand.TryGetNullness(out var nullness)
            ? Reference(nullness, operand.Cardinality)
            : TopForType(conversion.Type);
    }

    private ManagedAbstractValue EvaluateUnary(IUnaryOperation unary, ManagedFlowState state)
    {
        var operand = EvaluateCore(unary.Operand, state);
        if (IsBuiltinBooleanNot(unary))
        {
            return NegateBoolean(operand);
        }

        return unary.OperatorKind == UnaryOperatorKind.Minus && operand.TryGetInteger(out var interval) &&
               TryNegate(interval, out var negated)
               ? KeepWithinType(negated, unary.Type)
               : TopForType(unary.Type);
    }

    private static bool IsBuiltinBooleanNot(IUnaryOperation unary)
    {
        return unary.OperatorKind == UnaryOperatorKind.Not &&
            unary.OperatorMethod == null &&
            !unary.IsLifted &&
            unary.Type?.SpecialType == SpecialType.System_Boolean &&
            unary.Operand.Type?.SpecialType == SpecialType.System_Boolean;
    }

    private ManagedAbstractValue EvaluateConditional(IConditionalOperation operation, ManagedFlowState state)
    {
        var conditionValue = EvaluateCore(operation.Condition, state);
        if (conditionValue.TryGetBoolean(out var condition))
        {
            return EvaluateCore(condition ? operation.WhenTrue : operation.WhenFalse!, state);
        }

        return operation.WhenFalse == null
            ? Unknown
            : ManagedAbstractValue.Join(
                EvaluateCore(
                    operation.WhenTrue,
                    Assume(state, operation.Condition, true, conditionValue)),
                EvaluateCore(
                    operation.WhenFalse,
                    Assume(state, operation.Condition, false, conditionValue)));
    }

    private ManagedAbstractValue EvaluateCoalesce(ICoalesceOperation operation, ManagedFlowState state)
    {
        var value = EvaluateCore(operation.Value, state);
        if (value.IsDefinitelyNonNull)
        {
            return value;
        }

        if (value.IsDefinitelyNull)
        {
            return EvaluateCore(operation.WhenNull, state);
        }

        var whenNull = EvaluateCore(operation.WhenNull, state);
        var joined = Join(value, whenNull);

        // A coalesce cannot produce null when its fallback is definitely non-null.
        // Keep the joined reference cardinality while refining its nullness.
        return whenNull.IsDefinitelyNonNull && !joined.IsUnknown && !joined.IsBottom
            ? Reference(NullnessValue.NonNull, joined.Cardinality)
            : joined;
    }

    internal bool ProvesNoOverflow(
        IOperation operation,
        ManagedFlowState state,
        IMethodSymbol? currentMethod = null)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        state = ArgumentNullGuard.NotNull(state, nameof(state));
        var previousMethod = s_currentMethod;
        if (currentMethod is not null)
        {
            s_currentMethod = currentMethod;
        }

        try
        {
            operation = Unwrap(operation);
            IntervalValue interval;
            ITypeSymbol? type;
            switch (operation)
            {
                case IBinaryOperation binary when binary.OperatorKind is
                    BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply:
                    if (!EvaluateCore(binary.LeftOperand, state).TryGetInteger(out var left) ||
                        !EvaluateCore(binary.RightOperand, state).TryGetInteger(out var right) ||
                        !TryArithmetic(binary.OperatorKind, left, right, out interval))
                    {
                        return false;
                    }

                    type = binary.Type;
                    break;
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus } unary:
                    if (!EvaluateCore(unary.Operand, state).TryGetInteger(out var operand) ||
                        !TryNegate(operand, out interval))
                    {
                        return false;
                    }

                    type = unary.Type;
                    break;
                case IIncrementOrDecrementOperation increment:
                    if (!TryIncrement(increment, state, out interval))
                    {
                        return false;
                    }

                    type = increment.Type;
                    break;
                case IConversionOperation conversion:
                    if (!EvaluateCore(conversion.Operand, state).TryGetInteger(out interval))
                    {
                        return false;
                    }

                    type = conversion.Type;
                    break;
                default:
                    return false;
            }
            return FitsType(interval, type);
        }
        finally
        {
            s_currentMethod = previousMethod;
        }
    }

    private ManagedAbstractValue ApplyAttributes(
        ManagedAbstractValue value, ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (Matches(attribute, _notNullAttribute) && value.TryGetNullness(out var nullness))
            {
                value = Reference(
                    NullnessDomain.Instance.AssumeNonNull(nullness), value.Cardinality);
            }
            else if (Matches(attribute, _positiveAttribute) && value.TryGetInteger(out var positive))
            {
                value = Integer(IntervalDomain.Instance.AssumeAtLeast(positive, 1));
            }
            else if (Matches(attribute, _inRangeAttribute) && value.TryGetInteger(out var range) &&
                     attribute.ConstructorArguments.Length == 2 &&
                     attribute.ConstructorArguments[0].Value is long minimum &&
                     attribute.ConstructorArguments[1].Value is long maximum && minimum <= maximum)
            {
                value = Integer(IntervalDomain.Instance.AssumeAtMost(
                    IntervalDomain.Instance.AssumeAtLeast(range, minimum), maximum));
            }
        }
        return value;
    }

    private static bool Matches(AttributeData attribute, INamedTypeSymbol? expected)
    {
        return expected != null && SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition, expected.OriginalDefinition);
    }

    private bool IsAssumption(IInvocationOperation invocation)
    {
        return invocation.TargetMethod is
        {
            IsStatic: true,
            ReturnsVoid: true,
            Name: ContractApiCatalog.RequiresMethodName or ContractApiCatalog.AssumeMethodName,
            Parameters.Length: 1
        } method &&
        method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean &&
        invocation.Arguments.Length == 1 &&
        _contractApi != null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, _contractApi.OriginalDefinition);
    }

    private static ManagedFlowState HavocCall(
        ManagedFlowState state, IMethodSymbol method, ImmutableArray<IArgumentOperation> arguments)
    {
        state = method.MethodKind == MethodKind.LocalFunction ||
            method.ContainingType.TypeKind == TypeKind.Delegate ||
            arguments.Any(static argument =>
                CanCarryDelegate(argument.Value))
            ? state.Forget()
            : HavocArguments(state, arguments);
        return state.ForgetByReferenceParameters();
    }

    private static bool CanCarryDelegate(IOperation value)
    {
        value = Unwrap(value);
        return value is IDelegateCreationOperation ||
            value.Type is { } type &&
            (type.TypeKind == TypeKind.Delegate ||
             type.SpecialType is SpecialType.System_Delegate or
                 SpecialType.System_MulticastDelegate);
    }

    private static ManagedFlowState HavocArguments(
        ManagedFlowState state, ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                state = HavocArgumentStorage(state, argument.Value);
            }
        }

        return state;
    }

    private static ManagedFlowState HavocArgumentStorage(
        ManagedFlowState state,
        IOperation value)
    {
        value = Unwrap(value);
        if (value is IConditionalOperation conditional)
        {
            state = HavocArgumentStorage(state, conditional.WhenTrue);
            return conditional.WhenFalse == null
                ? state.Forget()
                : HavocArgumentStorage(state, conditional.WhenFalse);
        }

        if (value is IFlowCaptureReferenceOperation)
        {
            // A ref conditional is represented in the CFG by one capture that
            // can alias either source storage. This value domain does not
            // retain capture-to-storage aliases, so all scalar facts must be
            // forgotten rather than updating only the synthetic capture.
            return state.Forget();
        }

        return TryStorage(value, out var storage)
            ? state.Set(storage, TopForType(value.Type))
            : state;
    }

    private static ManagedFlowState SetStorage(
        ManagedFlowState state, IOperation operation, ManagedAbstractValue value)
    {
        return TryStorage(operation, out var storage) ? state.Set(storage, value) : state;
    }

    private static bool IsUntrackedRefLocal(IOperation operation)
    {
        return DefiniteOperationFacts.UnwrapHarmlessValue(operation) is ILocalReferenceOperation local &&
            IsUntrackedManagedReference(local.Local.RefKind);
    }

    private static bool IsNonLocalTarget(IOperation operation)
    {
        operation = Unwrap(operation);
        return operation is not (ILocalReferenceOperation or
            IParameterReferenceOperation or
            IFlowCaptureReferenceOperation or
            IDiscardOperation);
    }

    private static ManagedFlowState MarkUntrackedAlias(
        ManagedFlowState state,
        IOperation target,
        out bool aliased)
    {
        aliased = IsUntrackedRefLocal(target);
        // Roslyn lowers ref-local declarations and writes through ref locals
        // to assignments whose storage this domain cannot identify.
        return aliased ? state.WithUntrackedAlias() : state;
    }

    private static bool IsUntrackedManagedReference(RefKind refKind)
    {
        return refKind is RefKind.Ref or RefKind.RefReadOnly or RefKind.RefReadOnlyParameter;
    }

    private static bool TryStorage(IOperation operation, out object storage)
    {
        operation = Unwrap(operation);
        if (operation is IParameterReferenceOperation receiverBackedParameter &&
            IsReceiverBackedParameter(
                receiverBackedParameter.Parameter,
                receiverBackedParameter))
        {
            storage = null!;
            return false;
        }

        storage = operation switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation local => local.Local,
            IFlowCaptureReferenceOperation capture => capture.Id,
            _ => null!
        };
        return operation is IParameterReferenceOperation or ILocalReferenceOperation or IFlowCaptureReferenceOperation;
    }

    private static bool IsReceiverBackedParameter(
        IParameterSymbol parameter,
        IOperation operation)
    {
        return s_currentMethod is { } currentMethod
            ? PrimaryConstructorParameterOwnership.IsReceiverBacked(parameter, currentMethod)
            : PrimaryConstructorParameterOwnership.IsReceiverBacked(parameter, operation);
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else if (operation is IConversionOperation conversion && ValuePreserving(conversion))
            {
                operation = conversion.Operand;
            }
            else
            {
                return operation;
            }
        }
    }

    private static bool ValuePreserving(IConversionOperation conversion)
    {
        if (conversion is { OperatorMethod: not null } or
            { Conversion: { IsUserDefined: true } } or
            { IsTryCast: true })
        {
            return false;
        }

        if (conversion.Conversion is { IsIdentity: true } or
            { IsImplicit: true, IsReference: true })
        {
            return true;
        }

        return IntegerType(conversion.Operand.Type, out var source) &&
               IntegerType(conversion.Type, out var target) &&
               source.Minimum >= target.Minimum && source.Maximum <= target.Maximum;
    }

    internal static bool IsAcyclic(ControlFlowGraph graph)
    {
        return IsAcyclic(graph, included: null);
    }

    internal static bool IsAcyclic(
        ControlFlowGraph graph,
        ISet<int>? included)
    {
        var marks = new byte[graph.Blocks.Length];
        bool VisitIncluded(BasicBlock block)
        {
            if (marks[block.Ordinal] != 0)
            {
                return marks[block.Ordinal] == 2;
            }

            marks[block.Ordinal] = 1;
            foreach (var (branch, _) in Successors(block))
            {
                if (branch.Destination != null && !Visit(branch.Destination))
                {
                    return false;
                }
            }

            marks[block.Ordinal] = 2;
            return true;
        }

        bool Visit(BasicBlock block)
        {
            if (included != null && !included.Contains(block.Ordinal))
            {
                return true;
            }

            return VisitIncluded(block);
        }

        return graph.Blocks
            .Where(block => block.IsReachable &&
                (included == null || included.Contains(block.Ordinal)))
            .All(VisitIncluded);
    }

    private static IEnumerable<(ControlFlowBranch Branch, bool? Expected)> Successors(BasicBlock block)
    {
        bool? expected = block.ConditionKind switch
        {
            ControlFlowConditionKind.WhenTrue => true,
            ControlFlowConditionKind.WhenFalse => false,
            _ => null
        };
        var constant = block.BranchValue?.ConstantValue is { HasValue: true, Value: bool value }
            ? value
            : (bool?)null;
        if (block.FallThroughSuccessor is
            { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null } &&
            IsFeasible(!expected, constant))
        {
            yield return (block.FallThroughSuccessor!, !expected);
        }

        if (block.ConditionalSuccessor is
            { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null } &&
            IsFeasible(expected, constant))
        {
            yield return (block.ConditionalSuccessor!, expected);
        }
    }

    private static bool IsFeasible(bool? expected, bool? constant)
    {
        return !expected.HasValue ||
               !constant.HasValue ||
               expected.Value == constant.Value;
    }

    internal static bool IsCompileTimeUnreachable(
        Compilation compilation,
        IOperation operation)
    {
        var statement = operation.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (statement == null)
        {
            return false;
        }

        var key = (
            statement.SyntaxTree,
            statement.SpanStart,
            statement.Span.Length);
        var cache = CompileTimeUnreachableStatementCache.GetValue(
            compilation,
            static _ => new());
        if (cache.TryGetValue(key, out var statementUnreachable))
        {
            if (statementUnreachable)
            {
                return true;
            }
        }
        else
        {
            statementUnreachable = false;
            var statementModel = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, statement.SyntaxTree);
            try
            {
                if (statementModel.AnalyzeControlFlow(statement) is
                    { Succeeded: true, StartPointIsReachable: false })
                {
                    statementUnreachable = true;
                }
            }
            catch (ArgumentException)
            {
                // Unsupported statement shapes retain the permissive fallback.
            }

            cache.TryAdd(key, statementUnreachable);
            if (statementUnreachable)
            {
                return true;
            }
        }

        var unreachable = false;
        foreach (var syntax in operation.Syntax.Ancestors())
        {
            SyntaxNode? condition = syntax switch
            {
                WhileStatementSyntax @while
                    when @while.Statement.Span.Contains(operation.Syntax.Span) =>
                    @while.Condition,
                ForStatementSyntax @for
                    when @for.Condition != null &&
                         (@for.Statement.Span.Contains(operation.Syntax.Span) ||
                          @for.Incrementors.Any(incrementor =>
                              incrementor.Span.Contains(operation.Syntax.Span))) =>
                    @for.Condition,
                _ => null
            };
            if (condition == null)
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, condition.SyntaxTree);
            if (model.GetConstantValue(condition) is { HasValue: true, Value: false })
            {
                unreachable = true;
                break;
            }
        }

        return unreachable;
    }

    internal static bool IsGeneratedUsingDisposal(IOperation operation)
    {
        if (operation is not IInvocationOperation
            {
                IsImplicit: true,
                TargetMethod.Name: "Dispose" or "DisposeAsync"
            } invocation)
        {
            return false;
        }

        foreach (var syntax in invocation.Syntax.AncestorsAndSelf())
        {
            if (syntax is UsingStatementSyntax)
            {
                return true;
            }

            if (syntax is LocalDeclarationStatementSyntax declaration)
            {
                return declaration.UsingKeyword.RawKind != 0;
            }

            if (syntax is ForEachStatementSyntax or ForEachVariableStatementSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static EffectAnalysisIncompleteReason CheckBudget(
        ControlFlowGraph graph, CancellationToken cancellationToken)
    {
        if (graph.Blocks.Length > MaxAnalyzedBlocks)
        {
            return EffectAnalysisIncompleteReason.BlockBudgetExceeded;
        }

        var operationCount = 0;
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var root in block.Operations.Concat(
                         block.BranchValue == null ? [] : [block.BranchValue]))
            {
                foreach (var _ in root.DescendantsAndSelf())
                {
                    if (operationCount == MaxAnalyzedOperations)
                    {
                        return EffectAnalysisIncompleteReason.OperationBudgetExceeded;
                    }

                    operationCount++;
                }
            }
        }
        return EffectAnalysisIncompleteReason.None;
    }

    private static bool TryNegate(IntervalValue value, out IntervalValue result)
    {
        if (value.IsBottom || !value.LowerBound.HasValue || !value.UpperBound.HasValue ||
            value.LowerBound.Value == long.MinValue)
        {
            result = IntervalValue.Top;
            return false;
        }
        result = IntervalValue.Range(-value.UpperBound.Value, -value.LowerBound.Value);
        return true;
    }

    private sealed class FlowDomain : CanonicalAbstractDomain<ManagedFlowState>
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "SharpProof.Soundness",
            "SPMETA002",
            Justification = "ManagedFlowState.FlowDomain has no mutable instance state.")]
        internal static FlowDomain Instance { get; } = new();
        public override ManagedFlowState Bottom => ManagedFlowState.Bottom;
        public override ManagedFlowState Top => ManagedFlowState.Top;
        protected override bool IsCanonical(ManagedFlowState value)
        {
            return value.IsCanonicalRepresentation();
        }

        public override ManagedFlowState Join(ManagedFlowState left, ManagedFlowState right)
        {
            return ManagedFlowState.Join(left, right);
        }

        public override ManagedFlowState Havoc(ManagedFlowState value)
        {
            return value.Forget();
        }

        public override bool LessThanOrEqual(ManagedFlowState left, ManagedFlowState right)
        {
            return ManagedFlowState.LessThanOrEqual(left, right);
        }
    }
    internal bool IsBlockedAfterNoncompletingStatement(
        IOperation operation)
    {
        var statement = operation.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (statement?.Parent is not BlockSyntax block)
        {
            return false;
        }
        if (statement is LocalFunctionStatementSyntax ||
            operation.Syntax.Ancestors().TakeWhile(candidate =>
                candidate != statement).Any(static candidate =>
                    candidate is AnonymousFunctionExpressionSyntax))
        {
            return false;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, block.SyntaxTree);
        foreach (var prior in block.Statements
                     .TakeWhile(candidate => candidate != statement))
        {
            var priorOperation = model.GetOperation(prior);
            if (priorOperation != null &&
                !_completionFacts.MayCompleteNormally(priorOperation))
            {
                return true;
            }
        }
        return false;
    }
}

internal enum ManagedFlowStatus
{
    Complete,
    BudgetExceeded,
    Cyclic
}

internal sealed class ManagedFlowAnalysis
{
    private ManagedFlowAnalysis(
        ManagedFlowStatus status,
        EffectAnalysisIncompleteReason reason,
        ManagedFlowResult? result)
    {
        var valid = status switch
        {
            ManagedFlowStatus.Complete =>
                reason == EffectAnalysisIncompleteReason.None && result != null,
            ManagedFlowStatus.BudgetExceeded =>
                reason is EffectAnalysisIncompleteReason.BlockBudgetExceeded or
                    EffectAnalysisIncompleteReason.OperationBudgetExceeded &&
                result == null,
            ManagedFlowStatus.Cyclic =>
                reason == EffectAnalysisIncompleteReason.CyclicControlFlow && result == null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("Managed-flow status, reason, and result are inconsistent.");
        }

        Status = status;
        IncompleteReason = reason;
        Result = result;
    }

    internal ManagedFlowStatus Status
    {
        get;
    }
    internal EffectAnalysisIncompleteReason IncompleteReason
    {
        get;
    }
    internal ManagedFlowResult? Result
    {
        get;
    }
    internal bool IsComplete => Status == ManagedFlowStatus.Complete;

    internal static ManagedFlowAnalysis Complete(ManagedFlowResult result)
    {
        return new(ManagedFlowStatus.Complete, EffectAnalysisIncompleteReason.None,
            ArgumentNullGuard.NotNull(result, nameof(result)));
    }

    internal static ManagedFlowAnalysis BudgetExceeded(EffectAnalysisIncompleteReason reason)
    {
        return new(ManagedFlowStatus.BudgetExceeded, reason, null);
    }

    internal static ManagedFlowAnalysis Cyclic()
    {
        return new(ManagedFlowStatus.Cyclic, EffectAnalysisIncompleteReason.CyclicControlFlow, null);
    }
}

internal sealed class ManagedFlowResult(ManagedAbstractFlow flow, IMethodSymbol? method = null)
{
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures = new();
    private readonly Dictionary<object, ManagedFlowState> _states = new(ManagedKeyComparer.Instance);
    private readonly Dictionary<IOperation, bool> _mutationFacts = new();
    private readonly Dictionary<IOperation, bool> _reachabilityFacts = new();

    internal bool HasMutation(IOperation operation)
    {
        if (_mutationFacts.TryGetValue(operation, out var hasMutation))
        {
            return hasMutation;
        }

        hasMutation = ManagedMutationFacts.HasMutation(operation);
        _mutationFacts.Add(operation, hasMutation);
        return hasMutation;
    }

    internal void RecordCoalesceAssignmentCapture(
        IFlowCaptureOperation capture)
    {
        _coalesceCaptures.Record(capture);
    }

    internal IOperation ResolveCoalesceAssignmentTarget(IOperation target)
    {
        return _coalesceCaptures.Resolve(target);
    }

    internal void Record(IOperation operation, ManagedFlowState state)
    {
        if (state.IsBottom)
        {
            return;
        }

        Add(operation, state);
        Add(Key(operation), state);
    }

    internal bool TryGetState(IOperation operation, out ManagedFlowState state)
    {
        return _states.TryGetValue(operation, out state!) || _states.TryGetValue(Key(operation), out state!);
    }

    internal bool IsReachable(IOperation operation)
    {
        if (ManagedAbstractFlow.IsGeneratedUsingDisposal(operation))
        {
            _reachabilityFacts[operation] = false;
            return false;
        }

        if (_reachabilityFacts.TryGetValue(operation, out var reachable))
        {
            return reachable;
        }

        reachable = !flow.IsBlockedAfterNoncompletingStatement(operation) &&
            operation.DescendantsAndSelf().Any(candidate =>
                TryGetState(candidate, out var state) && !state.IsBottom);
        _reachabilityFacts.Add(operation, reachable);
        return reachable;
    }

    internal static IOperation? GetUnavoidableDirectOperation(
        IOperation root, SyntaxNode? directSyntax)
    {
        if (directSyntax == null ||
            root.Syntax.SyntaxTree != directSyntax.SyntaxTree ||
            root.Syntax.Span != directSyntax.Span ||
            root.DescendantsAndSelf().Any(IsControlFlow))
        {
            return null;
        }

        var operation = root;
        while (true)
        {
            operation = operation switch
            {
                IReturnOperation { ReturnedValue: { } value } => value,
                IExpressionStatementOperation statement => statement.Operation,
                _ => operation
            };
            var unwrapped = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
            if (ReferenceEquals(unwrapped, operation))
            {
                return operation;
            }

            operation = unwrapped;
        }
    }

    internal bool TryEvaluate(IOperation origin, IOperation value, out ManagedAbstractValue result)
    {
        return TryEvaluate(
            origin,
            value,
            HasMutation(value),
            out result);
    }

    private bool TryEvaluate(
        IOperation origin,
        IOperation value,
        bool hasMutation,
        out ManagedAbstractValue result)
    {
        if (!hasMutation &&
            (TryGetState(value, out var state) ||
             TryGetState(origin, out state)))
        {
            result = flow.Evaluate(value, state, method);
            return true;
        }

        result = Unknown;
        return false;
    }

    internal bool TryEvaluateAtOrigin(
        IOperation origin,
        IOperation value,
        out ManagedAbstractValue result)
    {
        if (!HasMutation(value) &&
            TryGetState(origin, out var state))
        {
            result = flow.Evaluate(value, state, method);
            return true;
        }
        result = Unknown;
        return false;
    }

    internal bool ProvesNonNull(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNonNull;
    }

    internal bool ProvesNull(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNull;
    }

    internal bool ProvesNonZero(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNonZero;
    }

    internal bool ProvesNonNegative(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) &&
        result.TryGetInteger(out var interval) && interval.LowerBound >= 0;
    }

    internal bool ProvesNoSignedDivisionOverflow(
        IOperation origin, IOperation left, IOperation right, long minimum)
    {
        var rightHasMutation = HasMutation(right);
        return !rightHasMutation &&
        TryEvaluate(origin, left, out var leftValue) &&
        TryEvaluate(origin, right, rightHasMutation, out var rightValue) &&
        leftValue.TryGetInteger(out var leftInterval) &&
        rightValue.TryGetInteger(out var rightInterval) &&
        (!leftInterval.Contains(minimum) || !rightInterval.Contains(-1));
    }

    internal bool ProvesArrayAccess(IArrayElementReferenceOperation element)
    {
        if (element.Indices.Length != 1)
        {
            return false;
        }

        var index = element.Indices[0];
        var indexHasMutation = HasMutation(index);
        return !indexHasMutation &&
        TryEvaluate(element, element.ArrayReference, out var array) &&
        TryEvaluate(element, index, indexHasMutation, out var indexValue) &&
        array.IsDefinitelyNonNull &&
        array.TryGetCardinality(out var length) && length.LowerBound.HasValue &&
        indexValue.TryGetInteger(out var interval) &&
        interval.LowerBound >= 0 && interval.UpperBound < length.LowerBound;
    }

    internal bool ProvesNoOverflow(IOperation operation)
    {
        if (operation is IConversionOperation conversion &&
            IsRangePreservingNumericConversion(conversion))
        {
            return true;
        }

        if (HasMutation(operation))
        {
            return false;
        }

        if (TryGetState(operation, out var state))
        {
            return flow.ProvesNoOverflow(operation, state, method);
        }

        return operation is IConversionOperation conversionOperation &&
            TryGetState(conversionOperation.Operand, out state) &&
            flow.ProvesNoOverflow(conversionOperation, state, method);
    }

    private static bool IsRangePreservingNumericConversion(
        IConversionOperation conversion)
    {
        return !conversion.IsTryCast &&
            conversion.OperatorMethod == null &&
            !conversion.Conversion.IsUserDefined &&
            string.Equals(
                conversion.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal) &&
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                .GetConversion(conversion).IsNumeric &&
            ManagedAbstractValue.IntegerType(
                conversion.Operand.Type,
                out var source) &&
            ManagedAbstractValue.IntegerType(
                conversion.Type,
                out var target) &&
            source.Minimum >= target.Minimum &&
            source.Maximum <= target.Maximum;
    }

    private static bool IsControlFlow(IOperation operation)
    {
        return operation is IConditionalOperation or IConditionalAccessOperation or ISwitchOperation or
            ISwitchExpressionOperation or ILoopOperation or ITryOperation;
    }

    private void Add(object key, ManagedFlowState state)
    {
        _states[key] = _states.TryGetValue(key, out var current) ? ManagedFlowState.Join(current, state) : state;
    }

    internal static bool HasSameIdentity(IOperation operation, IOperation? candidate)
    {
        return candidate is not null &&
            (ReferenceEquals(operation, candidate) || Key(operation) == Key(candidate));
    }

    private static (SyntaxTree, int, int, OperationKind) Key(IOperation operation)
    {
        return (operation.Syntax.SyntaxTree, operation.Syntax.SpanStart, operation.Syntax.Span.Length, operation.Kind);
    }
}

internal sealed class ManagedFlowState
{
    private static readonly ManagedKeyComparer Comparer = ManagedKeyComparer.Instance;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SharpProof.Soundness",
        "SPMETA002",
        Justification = "NoValues is an empty immutable dictionary sentinel with no object keys or mutable entries.")]
    private static readonly ImmutableDictionary<object, ManagedAbstractValue> NoValues =
        ImmutableDictionary.Create<object, ManagedAbstractValue>(Comparer);
    private readonly ImmutableDictionary<object, ManagedAbstractValue>? _values;
    private readonly bool _hasUntrackedAlias;

    private ManagedFlowState(
        ImmutableDictionary<object, ManagedAbstractValue>? values,
        bool hasUntrackedAlias = false)
    {
        _values = values;
        _hasUntrackedAlias = hasUntrackedAlias;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SharpProof.Soundness",
        "SPMETA002",
        Justification = "ManagedFlowState instances are immutable canonical value sentinels.")]
    internal static ManagedFlowState Bottom { get; } = new(null);
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SharpProof.Soundness",
        "SPMETA002",
        Justification = "ManagedFlowState instances are immutable canonical value sentinels.")]
    internal static ManagedFlowState Empty { get; } = new(NoValues);
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SharpProof.Soundness",
        "SPMETA002",
        Justification = "ManagedFlowState instances are immutable canonical value sentinels.")]
    internal static ManagedFlowState Top { get; } = new(NoValues, hasUntrackedAlias: true);
    internal bool IsBottom => _values == null;

    internal bool IsCanonicalRepresentation()
    {
        if (_values == null)
        {
            return !_hasUntrackedAlias;
        }

        return _hasUntrackedAlias
            ? _values.Count == 0
            : _values.Values.All(static value => value.IsCanonicalRepresentation());
    }

    internal ManagedAbstractValue Get(object storage)
    {
        if (_values == null)
        {
            return ManagedAbstractValue.Bottom;
        }

        if (_values.TryGetValue(storage, out var value))
        {
            return value;
        }

        return storage is ISymbol symbol
            ? TopForType(symbol is IParameterSymbol parameter ? parameter.Type :
                symbol is ILocalSymbol local ? local.Type : null)
            : Unknown;
    }

    internal ManagedFlowState Set(object storage, ManagedAbstractValue value)
    {
        if (_values == null || value.IsBottom)
        {
            return Bottom;
        }

        return _hasUntrackedAlias ? this : new(_values.SetItem(storage, value));
    }

    internal ManagedFlowState WithUntrackedAlias()
    {
        return IsBottom ? this : Top;
    }

    internal ManagedFlowState Forget()
    {
        return IsBottom || _hasUntrackedAlias ? this : Empty;
    }

    internal ManagedFlowState ForgetByReferenceParameters()
    {
        if (_values == null || _hasUntrackedAlias)
        {
            return this;
        }

        var values = _values;
        foreach (var storage in _values.Keys)
        {
            if (storage is IParameterSymbol parameter &&
                parameter.RefKind != RefKind.None)
            {
                values = values.SetItem(
                    parameter,
                    ManagedAbstractValue.TopForType(parameter.Type));
            }
        }

        return ReferenceEquals(values, _values) ? this : new(values);
    }

    internal static ManagedFlowState Join(ManagedFlowState left, ManagedFlowState right)
    {
        if (left._values == null)
        {
            return right;
        }

        if (right._values == null)
        {
            return left;
        }

        if (left._hasUntrackedAlias || right._hasUntrackedAlias)
        {
            return Top;
        }

        var result = NoValues.ToBuilder();
        foreach (var key in left._values.Keys.Union(right._values.Keys, Comparer))
        {
            result[key] = ManagedAbstractValue.Join(left.Get(key), right.Get(key));
        }

        return new(result.ToImmutable());
    }

    internal static bool LessThanOrEqual(ManagedFlowState left, ManagedFlowState right)
    {
        if (left._values == null)
        {
            return true;
        }

        if (right._values == null)
        {
            return false;
        }

        if (right._hasUntrackedAlias)
        {
            return true;
        }

        if (left._hasUntrackedAlias)
        {
            return false;
        }

        return left._values.Keys.Union(right._values.Keys, Comparer).All(key =>
        {
            var rightValue = right.Get(key);
            return ManagedAbstractValue.Join(left.Get(key), rightValue) == rightValue;
        });
    }
}

internal sealed class ManagedKeyComparer : IEqualityComparer<object>
{
    internal static ManagedKeyComparer Instance { get; } = new();
    public new bool Equals(object? left, object? right)
    {
        return left is IOperation || right is IOperation
            ? ReferenceEquals(left, right)
            : left is ISymbol leftSymbol && right is ISymbol rightSymbol
                ? SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol)
                : object.Equals(left, right);
    }

    public int GetHashCode(object value)
    {
        return value is IOperation
                ? RuntimeHelpers.GetHashCode(value)
                : value is ISymbol symbol
                    ? SymbolEqualityComparer.Default.GetHashCode(symbol)
                    : value.GetHashCode();
    }
}

internal readonly record struct ManagedAbstractValue
{
    private ManagedAbstractValue(
        IntervalValue scalar,
        NullnessValue nullness,
        IntervalValue cardinality,
        bool excludesZero,
        bool isUnknown,
        bool isBoolean)
    {
        Scalar = scalar;
        Nullness = nullness;
        Cardinality = cardinality;
        ExcludesZero = excludesZero;
        IsUnknown = isUnknown;
        IsBoolean = isBoolean;
    }

    internal IntervalValue Scalar { get; }
    internal NullnessValue Nullness { get; }
    internal IntervalValue Cardinality { get; }
    internal bool ExcludesZero { get; }
    internal bool IsUnknown { get; }
    internal bool IsBoolean { get; }

    internal static ManagedAbstractValue Bottom => default;
    internal static ManagedAbstractValue Unknown { get; } = new(default, default, default, false, true, false);
    internal static ManagedAbstractValue BooleanUnknown
    {
        get;
    } =
        new(IntervalValue.Range(0, 1), default, default, false, false, true);
    internal static ManagedAbstractValue Null => Reference(NullnessValue.Null);
    internal static ManagedAbstractValue NonNull => Reference(NullnessValue.NonNull);
    internal bool IsBottom => !IsUnknown && Scalar.IsBottom && Nullness == NullnessValue.Bottom;
    internal bool IsDefinitelyNull => Nullness == NullnessValue.Null;
    internal bool IsDefinitelyNonNull => Nullness == NullnessValue.NonNull;
    internal bool IsDefinitelyNonZero => !Scalar.IsBottom && (ExcludesZero || !Scalar.Contains(0));

    internal bool IsCanonicalRepresentation()
    {
        if (this == default)
        {
            return true;
        }

        if (IsUnknown)
        {
            return Scalar.IsBottom &&
                   Nullness == NullnessValue.Bottom &&
                   Cardinality.IsBottom &&
                   !ExcludesZero &&
                   !IsBoolean;
        }

        if (IsBoolean)
        {
            return Nullness == NullnessValue.Bottom &&
                   Cardinality.IsBottom &&
                   !ExcludesZero &&
                   (Scalar.IsSingleton
                        ? Scalar.SingletonValue is 0 or 1
                        : Scalar == IntervalValue.Range(0, 1));
        }

        if (!Scalar.IsBottom)
        {
            return Nullness == NullnessValue.Bottom &&
                   Cardinality.IsBottom;
        }

        return Nullness is (NullnessValue.Null or
            NullnessValue.NonNull or
            NullnessValue.MaybeNull) &&
            !ExcludesZero &&
            !IsBoolean;
    }

    internal static ManagedAbstractValue Boolean(bool value)
    {
        return new(IntervalValue.Constant(value ? 1 : 0), default, default, false, false, true);
    }

    internal static ManagedAbstractValue Integer(IntervalValue value, bool excludesZero = false)
    {
        return value.IsBottom
            ? Bottom
            : new(
                value,
                default,
                default,
                excludesZero || !value.Contains(0),
                false,
                false);
    }

    internal static ManagedAbstractValue Reference(
            NullnessValue value, IntervalValue cardinality = default)
    {
        return value is NullnessValue.Null or
            NullnessValue.NonNull or
            NullnessValue.MaybeNull
            ? new(default, value, cardinality, false, false, false)
            : Bottom;
    }

    internal static ManagedAbstractValue TopForType(ITypeSymbol? type)
    {
        return ValueForType(type, useDefault: false);
    }

    internal static ManagedAbstractValue DefaultForType(ITypeSymbol? type)
    {
        return ValueForType(type, useDefault: true);
    }

    private static ManagedAbstractValue ValueForType(
        ITypeSymbol? type,
        bool useDefault)
    {
        if (type?.SpecialType == SpecialType.System_Boolean)
        {
            return useDefault ? Boolean(false) : BooleanUnknown;
        }

        if (IntegerType(type, out var integer))
        {
            return Integer(useDefault
                ? IntervalValue.Constant(0)
                : IntervalValue.Range(integer.Minimum, integer.Maximum));
        }

        return type?.IsReferenceType is true || IsNullableType(type)
            ? useDefault ? Null : Reference(NullnessValue.MaybeNull)
            : Unknown;
    }

    internal static ManagedAbstractValue FromConstant(object? value, ITypeSymbol? type)
    {
        if (value == null)
        {
            return Null;
        }

        if (value is bool boolean)
        {
            return Boolean(boolean);
        }

        if (value is string text)
        {
            return Reference(NullnessValue.NonNull, IntervalValue.Constant(text.Length));
        }

        try
        {
            return IntegerType(type, out _)
                ? Integer(IntervalValue.Constant(Convert.ToInt64(value, CultureInfo.InvariantCulture)))
                : Unknown;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return Unknown;
        }
    }

    internal bool TryGetBoolean(out bool value)
    {
        value = !Scalar.IsBottom && Scalar.IsSingleton && Scalar.SingletonValue != 0;
        return IsBoolean && Scalar.IsSingleton;
    }

    internal bool TryGetInteger(out IntervalValue value)
    {
        value = Scalar;
        return !IsBoolean && !Scalar.IsBottom;
    }

    internal bool TryGetNullness(out NullnessValue value)
    {
        value = Nullness;
        return Nullness != NullnessValue.Bottom;
    }

    internal bool TryGetCardinality(out IntervalValue value)
    {
        value = Cardinality;
        return Nullness != NullnessValue.Bottom && !Cardinality.IsBottom;
    }

    internal static ManagedAbstractValue Binary(
        BinaryOperatorKind @operator, ManagedAbstractValue left, ManagedAbstractValue right, ITypeSymbol? type = null)
    {
        var unknown = type == null ? Unknown : TopForType(type);
        if (@operator is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And &&
            left.TryGetBoolean(out var leftBoolean) && right.TryGetBoolean(out var rightBoolean))
        {
            return Boolean(leftBoolean && rightBoolean);
        }

        if (@operator is BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or &&
            left.TryGetBoolean(out leftBoolean) && right.TryGetBoolean(out rightBoolean))
        {
            return Boolean(leftBoolean || rightBoolean);
        }

        if (@operator is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
        {
            return Equality(left, right, @operator == BinaryOperatorKind.NotEquals, unknown);
        }

        if (!left.TryGetInteger(out var leftInteger) || !right.TryGetInteger(out var rightInteger))
        {
            return unknown;
        }

        if (@operator is BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
            BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
        {
            return Compare(leftInteger, rightInteger, @operator, unknown);
        }

        return TryArithmetic(@operator, leftInteger, rightInteger, out var result)
            ? KeepWithinType(result, type)
            : unknown;
    }

    internal static ManagedAbstractValue NegateBoolean(ManagedAbstractValue value)
    {
        // Negation preserves the Boolean domain even when the value is not a
        // singleton. Keeping BooleanUnknown allows subsequent Boolean
        // equality/refinement instead of widening the result to an untyped
        // value.
        return value.TryGetBoolean(out var boolean)
            ? Boolean(!boolean)
            : value.IsBoolean
                ? BooleanUnknown
                : Unknown;
    }

    internal static bool TryArithmetic(
        BinaryOperatorKind @operator, IntervalValue left, IntervalValue right, out IntervalValue result)
    {
        result = IntervalValue.Top;
        if (left.LowerBound is not { } leftMinimum ||
            left.UpperBound is not { } leftMaximum ||
            right.LowerBound is not { } rightMinimum ||
            right.UpperBound is not { } rightMaximum)
        {
            return false;
        }

        var a = new BigInteger(leftMinimum);
        var b = new BigInteger(leftMaximum);
        var c = new BigInteger(rightMinimum);
        var d = new BigInteger(rightMaximum);
        BigInteger minimum;
        BigInteger maximum;
        switch (@operator)
        {
            case BinaryOperatorKind.Add:
                minimum = a + c;
                maximum = b + d;
                break;
            case BinaryOperatorKind.Subtract:
                minimum = a - d;
                maximum = b - c;
                break;
            case BinaryOperatorKind.Multiply:
                {
                    minimum = maximum = a * c;
                    Include(ref minimum, ref maximum, a * d);
                    Include(ref minimum, ref maximum, b * c);
                    Include(ref minimum, ref maximum, b * d);

                    break;
                }
            default:
                return false;
        }

        if (minimum < long.MinValue || maximum > long.MaxValue)
        {
            return false;
        }

        result = IntervalValue.Range((long)minimum, (long)maximum);
        return true;

        static void Include(
            ref BigInteger minimum,
            ref BigInteger maximum,
            BigInteger candidate)
        {
            if (candidate < minimum)
            {
                minimum = candidate;
            }
            else if (candidate > maximum)
            {
                maximum = candidate;
            }
        }
    }

    /// <summary>
    /// Binary evaluation over IR scalars, where no Roslyn type symbol is
    /// available to bound the result. The IR integer domain is exactly Int64 —
    /// the frontend admits exact arithmetic only for <c>long</c>, see
    /// <c>CSharpScalarSemantics.SupportsExactIrArithmetic</c> — and
    /// <see cref="TryArithmetic"/> already refuses any interval that leaves that
    /// range, so a computed interval is kept rather than discarded for want of a
    /// type to check it against.
    /// </summary>
    internal static ManagedAbstractValue BinaryOverIrScalars(
        BinaryOperatorKind @operator, ManagedAbstractValue left, ManagedAbstractValue right)
    {
        if (@operator is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
                BinaryOperatorKind.Multiply &&
            left.TryGetInteger(out var leftInteger) &&
            right.TryGetInteger(out var rightInteger) &&
            TryArithmetic(@operator, leftInteger, rightInteger, out var result))
        {
            return Integer(result);
        }

        return Binary(@operator, left, right);
    }

    internal static ManagedAbstractValue KeepWithinType(IntervalValue value, ITypeSymbol? type)
    {
        return FitsType(value, type) ? Integer(value) : TopForType(type);
    }

    internal static bool FitsType(IntervalValue value, ITypeSymbol? type)
    {
        return IntegerType(type, out var semantics) &&
        value.LowerBound >= semantics.Minimum && value.UpperBound <= semantics.Maximum;
    }

    internal static ManagedAbstractValue Join(ManagedAbstractValue left, ManagedAbstractValue right)
    {
        if (left.IsBottom || right.IsBottom)
        {
            return left.IsBottom ? right : left;
        }

        if (left.IsUnknown || right.IsUnknown)
        {
            return Unknown;
        }

        if (!left.Scalar.IsBottom && !right.Scalar.IsBottom)
        {
            if (left.IsBoolean != right.IsBoolean)
            {
                return Unknown;
            }

            var scalar = IntervalDomain.Instance.Join(left.Scalar, right.Scalar);
            return left.IsBoolean
                ? scalar.IsSingleton ? Boolean(scalar.SingletonValue != 0) : BooleanUnknown
                : Integer(
                    scalar,
                    left.IsDefinitelyNonZero && right.IsDefinitelyNonZero);
        }
        if (left.Nullness == NullnessValue.Bottom || right.Nullness == NullnessValue.Bottom)
        {
            return Unknown;
        }

        var nullness = NullnessDomain.Instance.Join(left.Nullness, right.Nullness);
        if (!left.Cardinality.IsBottom && !right.Cardinality.IsBottom)
        {
            return Reference(nullness, IntervalDomain.Instance.Join(left.Cardinality, right.Cardinality));
        }

        if (left.Nullness == NullnessValue.Null && !right.Cardinality.IsBottom)
        {
            return Reference(nullness, right.Cardinality);
        }

        if (right.Nullness == NullnessValue.Null && !left.Cardinality.IsBottom)
        {
            return Reference(nullness, left.Cardinality);
        }

        return Reference(nullness);
    }

    private static ManagedAbstractValue Equality(
        ManagedAbstractValue left, ManagedAbstractValue right, bool negate, ManagedAbstractValue unknown)
    {
        bool? equal = null;
        if (left.TryGetBoolean(out var leftBoolean) && right.TryGetBoolean(out var rightBoolean))
        {
            equal = leftBoolean == rightBoolean;
        }
        else if (left.TryGetInteger(out var leftInteger) && right.TryGetInteger(out var rightInteger))
        {
            if (leftInteger.IsSingleton && rightInteger.IsSingleton)
            {
                equal = leftInteger.SingletonValue == rightInteger.SingletonValue;
            }
            else if (Disjoint(leftInteger, rightInteger))
            {
                equal = false;
            }
        }
        else if (left.TryGetNullness(out var leftNullness) && right.TryGetNullness(out var rightNullness))
        {
            if (leftNullness == NullnessValue.Null && rightNullness == NullnessValue.Null)
            {
                equal = true;
            }
            else if (leftNullness == NullnessValue.Null && rightNullness == NullnessValue.NonNull ||
                     rightNullness == NullnessValue.Null && leftNullness == NullnessValue.NonNull)
            {
                equal = false;
            }
        }
        return equal is bool established
            ? Boolean(negate != established)
            : unknown;
    }

    private static ManagedAbstractValue Compare(
        IntervalValue left, IntervalValue right, BinaryOperatorKind @operator, ManagedAbstractValue unknown)
    {
        bool? value = @operator switch
        {
            BinaryOperatorKind.LessThan when left.UpperBound < right.LowerBound => true,
            BinaryOperatorKind.LessThan when left.LowerBound >= right.UpperBound => false,
            BinaryOperatorKind.LessThanOrEqual when left.UpperBound <= right.LowerBound => true,
            BinaryOperatorKind.LessThanOrEqual when left.LowerBound > right.UpperBound => false,
            BinaryOperatorKind.GreaterThan when left.LowerBound > right.UpperBound => true,
            BinaryOperatorKind.GreaterThan when left.UpperBound <= right.LowerBound => false,
            BinaryOperatorKind.GreaterThanOrEqual when left.LowerBound >= right.UpperBound => true,
            BinaryOperatorKind.GreaterThanOrEqual when left.UpperBound < right.LowerBound => false,
            _ => null
        };
        return value.HasValue ? Boolean(value.Value) : unknown;
    }

    private static bool Disjoint(IntervalValue left, IntervalValue right)
    {
        return left.UpperBound.HasValue && right.LowerBound.HasValue && left.UpperBound.Value < right.LowerBound.Value ||
        right.UpperBound.HasValue && left.LowerBound.HasValue && right.UpperBound.Value < left.LowerBound.Value;
    }

    internal static bool IntegerType(ITypeSymbol? type, out CSharpIntegerSemantics semantics)
    {
        return CSharpScalarSemantics.TryGetInteger(type?.SpecialType ?? SpecialType.None, out semantics);
    }

    internal static bool IsNullableType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        };
    }

    internal static bool IsEmptyNullableCreation(IObjectCreationOperation creation)
    {
        return IsNullableType(creation.Type) && creation.Arguments.IsEmpty;
    }
}

/// <summary>Fail-closed execution facts shared by analyzer and effect witnesses.</summary>
internal sealed class DefiniteOperationFacts(Compilation compilation, CancellationToken cancellationToken)
{
    // Method and operation recursion consume one shared per-thread limit.
    internal const int MaximumCompletionFactsDepth = 256;
    private const int MaximumCompletionGraphMethods = 4096;
    private const int MaximumCompletionGraphEdges = 65536;

    private readonly InvocationEmissionPolicy _invocationEmission = new(compilation);
    private readonly ResolvedApiSpecTable _apiSpecs =
        new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);

    private sealed class CompletionTraversalState
    {
        internal int Depth { get; set; }
        internal bool Exhausted { get; set; }
    }

    private sealed class CompletionGraphMethod
    {
        internal bool CanCompleteNormally { get; set; } = true;
        internal bool Queued { get; set; }
        internal HashSet<IMethodSymbol> Dependencies { get; } =
            new(SymbolEqualityComparer.Default);
        internal HashSet<IMethodSymbol> Dependents { get; } =
            new(SymbolEqualityComparer.Default);
    }

    private sealed class CompletionGraphState
    {
        internal Dictionary<IMethodSymbol, CompletionGraphMethod> Methods { get; } =
            new(SymbolEqualityComparer.Default);
        internal Queue<IMethodSymbol> Worklist { get; } = new();
        internal IMethodSymbol? CurrentMethod { get; set; }
        internal int EdgeCount { get; set; }
        internal bool Exhausted { get; set; }
    }

    internal bool IsConditionallyElided(IOperation operation)
    {
        return _invocationEmission.IsElided(operation);
    }

    // Definite-completion recursion and may-completion graph solving are
    // re-entrant per analysis thread. ManagedAbstractFlow shares this class
    // across Roslyn's concurrent analysis threads, so keep their state both
    // thread-local and scoped by this instance.
    [ThreadStatic]
    private static Dictionary<DefiniteOperationFacts, HashSet<IMethodSymbol>>?
        s_activeMethods;
    [ThreadStatic]
    private static Dictionary<DefiniteOperationFacts, CompletionTraversalState>?
        s_completionTraversals;
    [ThreadStatic]
    private static Dictionary<DefiniteOperationFacts, CompletionGraphState>?
        s_completionGraphs;
    private readonly ConcurrentDictionary<IMethodSymbol, bool>
        _methodCompletionCache = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, bool>
        _definiteMethodCompletionCache = new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? _contractApi =
        ContractApiIdentityResolver.ForCompilation(compilation).Contract;

    private bool TryEnterCompletionTraversal(
        out CompletionTraversalState traversal,
        out bool ownsTraversal)
    {
        var traversals = s_completionTraversals ??= [];
        if (!traversals.TryGetValue(this, out traversal!))
        {
            traversal = new CompletionTraversalState();
            traversals.Add(this, traversal);
            ownsTraversal = true;
        }
        else
        {
            ownsTraversal = false;
        }

        if (traversal.Exhausted ||
            traversal.Depth >= MaximumCompletionFactsDepth ||
            (traversal.Depth & 15) == 0 &&
            !HasSufficientExecutionStack())
        {
            traversal.Exhausted = true;
            if (ownsTraversal)
            {
                traversals.Remove(this);
            }
            return false;
        }

        traversal.Depth++;
        return true;
    }

    private static bool HasSufficientExecutionStack()
    {
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            return true;
        }
        catch (InsufficientExecutionStackException)
        {
            return false;
        }
    }

    private void ExitCompletionTraversal(
        CompletionTraversalState traversal,
        bool ownsTraversal)
    {
        traversal.Depth--;
        if (ownsTraversal)
        {
            s_completionTraversals?.Remove(this);
        }
    }

    private bool TryEnterMethod(IMethodSymbol method)
    {
        var active = s_activeMethods ??= [];
        if (!active.TryGetValue(this, out var methods))
        {
            // Explicit comparer: if the guard ever degraded to reference
            // equality the failure mode would be unbounded recursion rather
            // than a wrong answer.
            methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            active.Add(this, methods);
        }

        return methods.Add(method);
    }

    private void ExitMethod(IMethodSymbol method)
    {
        var active = s_activeMethods!;
        var methods = active[this];
        methods.Remove(method);
        if (methods.Count == 0)
        {
            active.Remove(this);
        }
    }

    internal bool CompletesNormally(IOperation? operation)
    {
        return CompletesNormally(operation, flow: null, flowOrigin: null);
    }

    internal bool CompletesNormally(
        IOperation? operation,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryEnterCompletionTraversal(
                out var traversal,
                out var ownsTraversal))
        {
            return false;
        }

        try
        {
            var result = CompletesNormallyCore(
                operation,
                flow,
                flowOrigin);
            return traversal.Exhausted ? false : result;
        }
        finally
        {
            ExitCompletionTraversal(traversal, ownsTraversal);
        }
    }

    private bool CompletesNormallyCore(
        IOperation? operation,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation?.ConstantValue.HasValue == true)
        {
            return true;
        }
        var emittedOperation = operation is IExpressionStatementOperation statement
            ? statement.Operation
            : operation;
        if (emittedOperation != null && IsConditionallyElided(emittedOperation))
        {
            return true;
        }
        return operation switch
        {
            null => false,
            ILiteralOperation or IInterpolatedStringTextOperation or
                ILocalReferenceOperation or IParameterReferenceOperation or
                IDiscardOperation or IInstanceReferenceOperation or IDefaultValueOperation or
                ITypeOfOperation or INameOfOperation => true,
            IInvocationOperation invocation =>
                CompletesNormally(invocation, flow, flowOrigin),
            IObjectCreationOperation creation =>
                creation.Arguments.All(argument =>
                    CompletesNormally(argument.Value, flow, flowOrigin)) &&
                (creation.Constructor?.ContainingType.StaticConstructors.Length ??
                    (creation.Type as INamedTypeSymbol)?.StaticConstructors.Length ??
                    0) == 0 &&
                (creation.Constructor == null ||
                 CompletesNormally(creation.Constructor)) &&
                (creation.Initializer == null ||
                 CompletesNormally(
                     creation.Initializer,
                     flow,
                     flowOrigin)),
            IArrayCreationOperation array =>
                ArrayCreationCompletesNormally(array, flow, flowOrigin),
            IArrayElementReferenceOperation element =>
                ArrayAccessCompletesNormally(element, flow, flowOrigin),
            IFieldReferenceOperation field =>
                FieldAccessCompletesNormally(field, flow, flowOrigin),
            IPropertyReferenceOperation property =>
                PropertyAccessCompletesNormally(
                    property,
                    property.Property.GetMethod,
                    flow,
                    flowOrigin),
            IMethodReferenceOperation methodReference =>
                ChildrenCompleteNormally(methodReference, flow, flowOrigin) &&
                (methodReference.Method.IsStatic ||
                 methodReference.Instance != null &&
                 IsDefinitelyNonNull(
                     methodReference.Instance,
                     flow,
                     flowOrigin)),
            ISimpleAssignmentOperation assignment =>
                AssignmentCompletesNormally(
                    assignment,
                    flow,
                    flowOrigin),
            ICompoundAssignmentOperation compound =>
                CompoundAssignmentCompletesNormally(
                    compound,
                    flow,
                    flowOrigin),
            IBinaryOperation binary =>
                binary.OperatorMethod == null && !binary.IsChecked &&
                !IsDecimalType(binary.Type) &&
                binary.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) &&
                ChildrenCompleteNormally(binary, flow, flowOrigin),
            IUnaryOperation unary =>
                unary.OperatorMethod == null && !unary.IsChecked &&
                !IsDecimalType(unary.Type) &&
                ChildrenCompleteNormally(unary, flow, flowOrigin),
            IIncrementOrDecrementOperation increment =>
                IncrementCompletesNormally(increment, flow, flowOrigin),
            IConversionOperation conversion =>
                ConversionCompletesNormally(conversion) &&
                CompletesNormally(conversion.Operand, flow, flowOrigin),
            IConditionalAccessOperation conditionalAccess =>
                ConditionalAccessCompletesNormally(
                    conditionalAccess,
                    flow,
                    flowOrigin),
            IForLoopOperation loop =>
                ForLoopCompletesNormally(loop, flow, flowOrigin),
            IBlockOperation or IExpressionStatementOperation or IReturnOperation or
                IVariableDeclarationGroupOperation or IVariableDeclarationOperation or
                IVariableDeclaratorOperation or IVariableInitializerOperation or IArgumentOperation or
                IArrayInitializerOperation or
                IObjectOrCollectionInitializerOperation or
                IParenthesizedOperation or IConditionalOperation =>
                ChildrenCompleteNormally(operation, flow, flowOrigin),
            IUsingDeclarationOperation usingDeclaration =>
                CompletesNormally(
                    usingDeclaration.DeclarationGroup,
                    flow,
                    flowOrigin),
            _ => false
        };
    }

    private bool CompletesNormally(
        IInvocationOperation invocation,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        if (IsContractClause(invocation, flow, flowOrigin))
        {
            return true;
        }

        var target = GetExactInvocationTarget(invocation);
        return target != null &&
            (invocation.Instance == null ||
             CompletesNormally(invocation.Instance, flow, flowOrigin) &&
             IsDefinitelyNonNull(invocation.Instance, flow, flowOrigin)) &&
            invocation.Arguments.All(argument =>
                CompletesNormally(argument.Value, flow, flowOrigin)) &&
            CompletesNormally(target);
    }

    private static IMethodSymbol? GetExactInvocationTarget(
        IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        if (!invocation.IsVirtual || target.IsSealed)
        {
            return target;
        }

        if (invocation.Instance?.Type is not INamedTypeSymbol
            { IsSealed: true } receiverType)
        {
            return null;
        }

        return ResolveSealedDispatchTarget(target, receiverType);
    }

    private static IMethodSymbol? GetExactPropertyAccessor(
        IPropertyReferenceOperation property,
        IMethodSymbol accessor)
    {
        if (!accessor.IsVirtual || accessor.IsSealed)
        {
            return accessor;
        }

        return property.Instance?.Type is
            INamedTypeSymbol { IsSealed: true } receiverType
            ? ResolveSealedDispatchTarget(accessor, receiverType)
            : null;
    }

    private static IMethodSymbol? ResolveSealedDispatchTarget(
        IMethodSymbol target,
        INamedTypeSymbol receiverType)
    {
        if (target.ContainingType.TypeKind == TypeKind.Interface)
        {
            return receiverType.FindImplementationForInterfaceMember(target)
                as IMethodSymbol;
        }

        for (var type = receiverType; type != null; type = type.BaseType)
        {
            var implementation = type.GetMembers(target.Name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    OverridesOrMatches(candidate, target));
            if (implementation != null)
            {
                return implementation;
            }
        }

        return target;
    }

    private static bool OverridesOrMatches(
        IMethodSymbol candidate,
        IMethodSymbol target)
    {
        for (var method = candidate; method != null;
             method = method.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    method.OriginalDefinition,
                    target.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDefinitelyNonNull(
        IOperation operation,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return IsDefinitelyNonNull(operation) ||
            flowOrigin != null &&
            flow?.ProvesNonNull(flowOrigin, operation) == true;
    }

    private bool ArrayCreationCompletesNormally(
        IArrayCreationOperation array,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return array.DimensionSizes.All(size =>
                CompletesNormally(size, flow, flowOrigin) &&
                (size.ConstantValue is { HasValue: true, Value: int length } &&
                 length >= 0 ||
                 flowOrigin != null &&
                 flow?.ProvesNonNegative(flowOrigin, size) == true)) &&
            (array.Initializer == null ||
             CompletesNormally(array.Initializer, flow, flowOrigin));
    }

    private bool ArrayAccessCompletesNormally(
        IArrayElementReferenceOperation element,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return ChildrenCompleteNormally(element, flow, flowOrigin) &&
            (flow?.ProvesArrayAccess(element) == true ||
             ArrayLengthFacts.TryGetConstantLength(
                 element.ArrayReference,
                 out var length) &&
             IsDefinitelyNonNull(
                 element.ArrayReference,
                 flow,
                 flowOrigin) &&
            element.Indices.Length == 1 &&
            element.Indices[0].ConstantValue is { HasValue: true, Value: int index } &&
             index >= 0 && index < length);
    }

    private bool FieldAccessCompletesNormally(
        IFieldReferenceOperation field,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        if (field.Field.IsConst)
        {
            return true;
        }

        if (field.Field.IsStatic)
        {
            return field.Field.ContainingType.StaticConstructors.Length == 0;
        }

        return field.Instance != null &&
            CompletesNormally(field.Instance, flow, flowOrigin) &&
            IsDefinitelyNonNull(field.Instance, flow, flowOrigin);
    }

    private bool PropertyAccessCompletesNormally(
        IPropertyReferenceOperation property,
        IMethodSymbol? accessor,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return property.Arguments.All(argument =>
                CompletesNormally(argument.Value, flow, flowOrigin)) &&
            (property.Property.IsStatic
                ? property.Property.ContainingType.StaticConstructors.Length == 0
                : property.Instance != null &&
                  CompletesNormally(property.Instance, flow, flowOrigin) &&
                  IsDefinitelyNonNull(property.Instance, flow, flowOrigin)) &&
            (accessor == null ||
             GetExactPropertyAccessor(property, accessor) is { } exactAccessor &&
             CompletesNormally(exactAccessor));
    }

    private bool AssignmentCompletesNormally(
        ISimpleAssignmentOperation assignment,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return CompletesNormally(assignment.Value, flow, flowOrigin) &&
            StoreTargetCompletesNormally(
                assignment.Target,
                flow,
                flowOrigin);
    }

    private bool StoreTargetCompletesNormally(
        IOperation target,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return target switch
        {
            ILocalReferenceOperation or IParameterReferenceOperation or
                IDiscardOperation => true,
            IFieldReferenceOperation field =>
                FieldAccessCompletesNormally(field, flow, flowOrigin),
            IArrayElementReferenceOperation element =>
                ArrayAccessCompletesNormally(element, flow, flowOrigin),
            IPropertyReferenceOperation property =>
                PropertyAccessCompletesNormally(
                    property,
                    property.Property.SetMethod,
                    flow,
                    flowOrigin),
            _ => false
        };
    }

    private bool CompoundAssignmentCompletesNormally(
        ICompoundAssignmentOperation assignment,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return !assignment.IsChecked &&
            !IsDecimalType(assignment.Type) &&
            !IsDecimalType(assignment.Target.Type) &&
            (assignment.OperatorMethod == null ||
             CompletesNormally(assignment.OperatorMethod)) &&
            ReadWriteTargetCompletesNormally(
                assignment.Target,
                flow,
                flowOrigin) &&
            CompletesNormally(assignment.Value, flow, flowOrigin) &&
            assignment.OperatorKind is not (
                BinaryOperatorKind.Divide or
                BinaryOperatorKind.Remainder);
    }

    private bool IncrementCompletesNormally(
        IIncrementOrDecrementOperation increment,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return !increment.IsChecked &&
            !IsDecimalType(increment.Type) &&
            !IsDecimalType(increment.Target.Type) &&
            (increment.OperatorMethod == null ||
             CompletesNormally(increment.OperatorMethod)) &&
            ReadWriteTargetCompletesNormally(
                increment.Target,
                flow,
                flowOrigin);
    }

    private bool ReadWriteTargetCompletesNormally(
        IOperation target,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        return target is IPropertyReferenceOperation property
            ? PropertyAccessCompletesNormally(
                  property,
                  property.Property.GetMethod,
                  flow,
                  flowOrigin) &&
              PropertyAccessCompletesNormally(
                  property,
                  property.Property.SetMethod,
                  flow,
                  flowOrigin)
            : StoreTargetCompletesNormally(target, flow, flowOrigin);
    }

    private static bool ConversionCompletesNormally(
        IConversionOperation conversion)
    {
        if (HarmlessConversion(conversion))
        {
            return true;
        }

        var csharpConversion =
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(
                conversion);
        return conversion.OperatorMethod == null &&
            !conversion.IsChecked &&
            !csharpConversion.IsUserDefined &&
            !csharpConversion.IsDynamic &&
            (csharpConversion.IsBoxing ||
             !IsDecimalType(conversion.Operand.Type) &&
             !IsDecimalType(conversion.Type) &&
             (csharpConversion.IsNumeric ||
              csharpConversion.IsEnumeration));
    }

    private static bool IsDecimalType(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_Decimal ||
            type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType ==
                SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1 &&
            named.TypeArguments[0].SpecialType == SpecialType.System_Decimal;
    }

    private bool ConditionalAccessCompletesNormally(
        IConditionalAccessOperation conditionalAccess,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        var receiver = conditionalAccess.Operation;
        return CompletesNormally(receiver, flow, flowOrigin) &&
            (IsDefinitelyNull(receiver) ||
             flowOrigin != null &&
             flow?.ProvesNull(flowOrigin, receiver) == true ||
             CompletesNormally(
                 conditionalAccess.WhenNotNull,
                 flow,
                 flowOrigin));
    }

    private bool ForLoopCompletesNormally(
        IForLoopOperation loop,
        ManagedFlowResult? flow,
        IOperation? flowOrigin)
    {
        if (!TryGetForLoopIterations(loop, out var iterations) ||
            !loop.Before.All(operation =>
                CompletesNormally(operation, flow, flowOrigin)) ||
            loop.Condition != null &&
            !CompletesNormally(loop.Condition, flow, flowOrigin))
        {
            return false;
        }

        if (iterations.IsZero)
        {
            return true;
        }

        return loop.Body != null &&
            !loop.Body.DescendantsAndSelf().Any(operation =>
                operation is IReturnOperation or IThrowOperation or
                    IBranchOperation or IAnonymousFunctionOperation or
                    ILocalFunctionOperation) &&
            !LoopBodyWritesCounter(loop) &&
            CompletesNormally(loop.Body, flow, flowOrigin) &&
            loop.AtLoopBottom.All(operation =>
                CompletesNormally(operation, flow, flowOrigin));
    }

    private static bool TryGetForLoopIterations(
        IForLoopOperation loop,
        out BigInteger iterations)
    {
        iterations = BigInteger.Zero;
        if (loop.Condition is not IBinaryOperation condition ||
            condition.OperatorMethod != null || condition.IsChecked ||
            !TryGetLoopCounterAndBound(condition, out var counter,
                out var bound, out var comparison))
        {
            return false;
        }

        var initializers = loop.Before
            .SelectMany(static operation => operation.DescendantsAndSelf())
            .OfType<IVariableDeclaratorOperation>()
            .Where(declaration => SymbolEqualityComparer.Default.Equals(
                declaration.Symbol,
                counter))
            .Select(static declaration => declaration.Initializer?.Value)
            .OfType<IOperation>()
            .ToArray();
        if (initializers.Length != 1 ||
            !TryGetIntegralConstant(initializers[0], out var initialValue) ||
            loop.AtLoopBottom.Length != 1 ||
            !TryGetLoopStep(loop.AtLoopBottom[0], counter, out var step) ||
            !ManagedAbstractValue.IntegerType(
                counter.Type,
                out var semantics))
        {
            return false;
        }

        if (step > 0 && comparison is
                BinaryOperatorKind.LessThan or
                BinaryOperatorKind.LessThanOrEqual)
        {
            if (comparison == BinaryOperatorKind.LessThan)
            {
                iterations = initialValue < bound
                    ? CeilingDivide(bound - initialValue, step)
                    : BigInteger.Zero;
            }
            else
            {
                iterations = initialValue <= bound
                    ? (bound - initialValue) / step + BigInteger.One
                    : BigInteger.Zero;
            }
        }
        else if (step < 0 && comparison is
                     BinaryOperatorKind.GreaterThan or
                     BinaryOperatorKind.GreaterThanOrEqual)
        {
            var magnitude = BigInteger.Negate(step);
            if (comparison == BinaryOperatorKind.GreaterThan)
            {
                iterations = initialValue > bound
                    ? CeilingDivide(initialValue - bound, magnitude)
                    : BigInteger.Zero;
            }
            else
            {
                iterations = initialValue >= bound
                    ? (initialValue - bound) / magnitude + BigInteger.One
                    : BigInteger.Zero;
            }
        }
        else
        {
            return false;
        }

        var finalValue = initialValue + iterations * step;
        return initialValue >= semantics.Minimum &&
            initialValue <= semantics.Maximum &&
            finalValue >= semantics.Minimum &&
            finalValue <= semantics.Maximum;
    }

    private static bool TryGetLoopCounterAndBound(
        IBinaryOperation condition,
        out ILocalSymbol counter,
        out BigInteger bound,
        out BinaryOperatorKind comparison)
    {
        counter = null!;
        bound = BigInteger.Zero;
        comparison = condition.OperatorKind;
        if (TryGetLocal(condition.LeftOperand, out counter) &&
            TryGetIntegralConstant(condition.RightOperand, out bound))
        {
            return comparison is BinaryOperatorKind.LessThan or
                BinaryOperatorKind.LessThanOrEqual or
                BinaryOperatorKind.GreaterThan or
                BinaryOperatorKind.GreaterThanOrEqual;
        }

        if (!TryGetLocal(condition.RightOperand, out counter) ||
            !TryGetIntegralConstant(condition.LeftOperand, out bound))
        {
            return false;
        }

        comparison = comparison switch
        {
            BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual =>
                BinaryOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
            BinaryOperatorKind.GreaterThanOrEqual =>
                BinaryOperatorKind.LessThanOrEqual,
            _ => comparison
        };
        return comparison is BinaryOperatorKind.LessThan or
            BinaryOperatorKind.LessThanOrEqual or
            BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.GreaterThanOrEqual;
    }

    private static bool TryGetLoopStep(
        IOperation operation,
        ILocalSymbol counter,
        out BigInteger step)
    {
        operation = operation is IExpressionStatementOperation statement
            ? statement.Operation
            : operation;
        if (operation is IIncrementOrDecrementOperation increment &&
            increment.OperatorMethod == null &&
            TryGetLocal(increment.Target, out var incremented) &&
            SymbolEqualityComparer.Default.Equals(incremented, counter))
        {
            step = increment.Kind == OperationKind.Increment ? 1 : -1;
            return true;
        }

        if (operation is ICompoundAssignmentOperation assignment &&
            assignment.OperatorMethod == null &&
            !assignment.IsChecked &&
            TryGetLocal(assignment.Target, out var assigned) &&
            SymbolEqualityComparer.Default.Equals(assigned, counter) &&
            assignment.OperatorKind is BinaryOperatorKind.Add or
                BinaryOperatorKind.Subtract &&
            TryGetIntegralConstant(assignment.Value, out var amount))
        {
            step = assignment.OperatorKind == BinaryOperatorKind.Add
                ? amount
                : BigInteger.Negate(amount);
            return !step.IsZero;
        }

        step = BigInteger.Zero;
        return false;
    }

    private static bool TryGetLocal(
        IOperation operation,
        out ILocalSymbol local)
    {
        operation = UnwrapSimpleConversions(operation);
        if (operation is ILocalReferenceOperation reference)
        {
            local = reference.Local;
            return true;
        }

        local = null!;
        return false;
    }

    private static bool TryGetIntegralConstant(
        IOperation operation,
        out BigInteger value)
    {
        value = BigInteger.Zero;
        if (operation.ConstantValue is not { HasValue: true, Value: { } constant })
        {
            return false;
        }

        try
        {
            value = new BigInteger(Convert.ToInt64(
                constant,
                CultureInfo.InvariantCulture));
            return true;
        }
        catch (Exception exception) when (exception is
            InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static BigInteger CeilingDivide(
        BigInteger dividend,
        BigInteger divisor)
    {
        return (dividend + divisor - BigInteger.One) / divisor;
    }

    private static bool LoopBodyWritesCounter(IForLoopOperation loop)
    {
        var condition = (IBinaryOperation)loop.Condition!;
        _ = TryGetLoopCounterAndBound(condition, out var counter,
            out _, out _);
        return loop.Body!.DescendantsAndSelf().Any(operation =>
            operation switch
            {
                ISimpleAssignmentOperation assignment =>
                    WritesCounter(assignment.Target, counter),
                ICompoundAssignmentOperation assignment =>
                    WritesCounter(assignment.Target, counter),
                IIncrementOrDecrementOperation increment =>
                    WritesCounter(increment.Target, counter),
                IArgumentOperation argument =>
                    argument.Parameter?.RefKind != RefKind.None &&
                    WritesCounter(argument.Value, counter),
                _ => false
            });
    }

    private static bool WritesCounter(
        IOperation operation,
        ILocalSymbol counter)
    {
        return TryGetLocal(operation, out var written) &&
            SymbolEqualityComparer.Default.Equals(written, counter);
    }

    private bool CompletesImplicitParameterlessConstructor(
        IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (!method.IsImplicitlyDeclared ||
            method.MethodKind != MethodKind.Constructor ||
            method.Parameters.Length != 0 ||
            containingType.DeclaringSyntaxReferences.Length == 0 ||
            containingType.IsStatic ||
            containingType.StaticConstructors.Length != 0)
        {
            return false;
        }

        if (containingType.TypeKind == TypeKind.Struct)
        {
            return true;
        }

        if (containingType.TypeKind != TypeKind.Class)
        {
            return false;
        }

        var baseType = containingType.BaseType;
        if (baseType == null)
        {
            return true;
        }

        var baseConstructor = baseType.InstanceConstructors
            .FirstOrDefault(static constructor =>
                constructor.Parameters.Length == 0);
        return baseConstructor != null &&
            CompletesNormally(baseConstructor);
    }

    private bool CompletesNormally(IMethodSymbol method)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = method.OriginalDefinition;
        if (_definiteMethodCompletionCache.TryGetValue(
                normalized,
                out var cached))
        {
            return cached;
        }
        if (!TryEnterCompletionTraversal(
                out var traversal,
                out var ownsTraversal))
        {
            return false;
        }

        try
        {
            if (_definiteMethodCompletionCache.TryGetValue(
                    normalized,
                    out cached))
            {
                return cached;
            }

            if (method.IsStatic &&
                method.ContainingType.StaticConstructors.Length != 0)
            {
                _definiteMethodCompletionCache.TryAdd(normalized, false);
                return false;
            }

            if (normalized.DeclaringSyntaxReferences.Length != 1)
            {
                if (CompletesImplicitParameterlessConstructor(normalized))
                {
                    _definiteMethodCompletionCache.TryAdd(normalized, true);
                    return true;
                }

                var specified = _apiSpecs.IsNonThrowingAndTerminating(method);
                _definiteMethodCompletionCache.TryAdd(normalized, specified);
                return specified;
            }

            if (!normalized.IsAbstract && !normalized.IsExtern &&
                AutoPropertyFacts.IsAccessor(
                    normalized,
                    cancellationToken))
            {
                _definiteMethodCompletionCache.TryAdd(normalized, true);
                return true;
            }

            if (!TryEnterMethod(normalized))
            {
                // A recursive re-entry cannot establish definite completion.
                _definiteMethodCompletionCache.TryAdd(normalized, false);
                return false;
            }

            bool result;
            try
            {
                var declaration = normalized.DeclaringSyntaxReferences[0]
                    .GetSyntax(cancellationToken);
                if (DefersBodyCompletion(normalized, declaration))
                {
                    result = true;
                }
                else if (ExecutableBodySyntax.Get(declaration) is not { } body)
                {
                    result = false;
                }
                else
                {
                    var model = SharpProof.Frontend.Host
                        .CompilationModelProvider.GetSemanticModel(
                            compilation,
                            body.SyntaxTree);
                    result = CompletesNormally(
                        model.GetOperation(body, cancellationToken));
                }
            }
            finally
            {
                ExitMethod(normalized);
            }

            if (traversal.Exhausted)
            {
                return false;
            }

            _definiteMethodCompletionCache.TryAdd(normalized, result);
            return result;
        }
        finally
        {
            ExitCompletionTraversal(traversal, ownsTraversal);
        }
    }

    /// <summary>
    /// Returns whether a source method has a reachable normal exit.  This is
    /// intentionally a control-flow fact rather than a may-throw fact: a
    /// method with both a throwing and a returning branch can still permit the
    /// caller's next source-order step. Async and iterator bodies execute
    /// behind a deferred call boundary, so their body termination cannot make
    /// the invocation itself noncompleting.
    /// </summary>
    internal bool MethodCanCompleteNormally(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = method.OriginalDefinition;
        if (s_completionGraphs != null &&
            s_completionGraphs.TryGetValue(this, out var activeGraph))
        {
            return GetCompletionGraphValue(normalized, activeGraph);
        }
        if (TryGetSimpleCompletionValue(normalized, out var known))
        {
            return known;
        }
        if (!TryEnterCompletionTraversal(
                out var traversal,
                out var ownsTraversal))
        {
            return true;
        }

        try
        {
            if (TryGetSimpleCompletionValue(normalized, out known))
            {
                return known;
            }
            return SolveCompletionGraph(normalized, traversal);
        }
        finally
        {
            ExitCompletionTraversal(traversal, ownsTraversal);
        }
    }

    private bool TryGetSimpleCompletionValue(
        IMethodSymbol method,
        out bool result)
    {
        method = method.OriginalDefinition;
        if (_methodCompletionCache.TryGetValue(method, out result))
        {
            return true;
        }

        var isImplicitConstructor = EffectMethodNodeBuilder
            .IsSourceImplicitParameterlessConstructor(method);
        if (!isImplicitConstructor &&
            method.DeclaringSyntaxReferences.Length != 1)
        {
            _methodCompletionCache.TryAdd(method, true);
            result = true;
            return true;
        }
        if (!isImplicitConstructor &&
            HasUnconditionalSelfInvocation(method))
        {
            _methodCompletionCache.TryAdd(method, false);
            result = false;
            return true;
        }
        result = default;
        return false;
    }

    private bool GetCompletionGraphValue(
        IMethodSymbol method,
        CompletionGraphState graph)
    {
        cancellationToken.ThrowIfCancellationRequested();
        method = method.OriginalDefinition;
        if (graph.Exhausted)
        {
            return true;
        }

        if (!graph.Methods.TryGetValue(method, out var node))
        {
            if (TryGetSimpleCompletionValue(method, out var known))
            {
                return known;
            }

            if (graph.Methods.Count >= MaximumCompletionGraphMethods)
            {
                graph.Exhausted = true;
                return true;
            }

            node = new CompletionGraphMethod();
            graph.Methods.Add(method, node);
            EnqueueCompletionGraphMethod(method, node, graph);
        }

        var currentMethod = graph.CurrentMethod;
        if (currentMethod != null &&
            graph.Methods.TryGetValue(currentMethod, out var currentNode) &&
            currentNode.Dependencies.Add(method))
        {
            graph.EdgeCount++;
            if (graph.EdgeCount > MaximumCompletionGraphEdges)
            {
                graph.Exhausted = true;
                return true;
            }
            node.Dependents.Add(currentMethod);
        }

        return node.CanCompleteNormally;
    }

    private static void EnqueueCompletionGraphMethod(
        IMethodSymbol method,
        CompletionGraphMethod node,
        CompletionGraphState graph)
    {
        if (node.Queued)
        {
            return;
        }
        node.Queued = true;
        graph.Worklist.Enqueue(method);
    }

    private bool SolveCompletionGraph(
        IMethodSymbol root,
        CompletionTraversalState traversal)
    {
        var graphs = s_completionGraphs ??= [];
        var graph = new CompletionGraphState();
        var rootNode = new CompletionGraphMethod();
        graph.Methods.Add(root, rootNode);
        EnqueueCompletionGraphMethod(root, rootNode, graph);
        graphs.Add(this, graph);
        try
        {
            while (graph.Worklist.Count != 0 && !graph.Exhausted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = graph.Worklist.Dequeue();
                var node = graph.Methods[method];
                node.Queued = false;
                graph.CurrentMethod = method;
                bool result;
                try
                {
                    result = EvaluateMethodCompletion(method);
                }
                finally
                {
                    graph.CurrentMethod = null;
                }

                if (traversal.Exhausted)
                {
                    graph.Exhausted = true;
                    break;
                }
                if (node.CanCompleteNormally && !result)
                {
                    node.CanCompleteNormally = false;
                    foreach (var dependent in node.Dependents)
                    {
                        if (graph.Methods.TryGetValue(
                                dependent,
                                out var dependentNode))
                        {
                            EnqueueCompletionGraphMethod(
                                dependent,
                                dependentNode,
                                graph);
                        }
                    }
                }
                else if (!node.CanCompleteNormally && result)
                {
                    // The completion evaluator is monotone. A value that rises
                    // after starting from the conservative true assignment
                    // means an unsupported dependency shape was observed.
                    graph.Exhausted = true;
                }
            }

            if (graph.Exhausted || traversal.Exhausted)
            {
                return true;
            }

            foreach (var pair in graph.Methods)
            {
                _methodCompletionCache.TryAdd(
                    pair.Key,
                    pair.Value.CanCompleteNormally);
            }
            return graph.Methods[root].CanCompleteNormally;
        }
        finally
        {
            graphs.Remove(this);
            if (graphs.Count == 0)
            {
                s_completionGraphs = null;
            }
        }
    }

    private bool EvaluateMethodCompletion(IMethodSymbol method)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isImplicitConstructor = EffectMethodNodeBuilder
            .IsSourceImplicitParameterlessConstructor(method);
        if (isImplicitConstructor)
        {
            return ImplicitConstructorMayCompleteNormally(method);
        }

        try
        {
            var declaration = method.DeclaringSyntaxReferences[0]
                .GetSyntax(cancellationToken);
            if (DefersBodyCompletion(method, declaration))
            {
                return true;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, declaration.SyntaxTree);
            var operation = model.GetOperation(declaration, cancellationToken) ??
                (ExecutableBodySyntax.Get(declaration) is { } methodBody
                    ? model.GetOperation(methodBody, cancellationToken)
                    : null);
            return operation == null
                ? true
                : method.MethodKind == MethodKind.Constructor &&
                  operation is IConstructorBodyOperation constructorBody
                    ? ConstructorMayCompleteNormally(method, constructorBody)
                    : MayCompleteNormally(operation);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private bool HasUnconditionalSelfInvocation(IMethodSymbol method)
    {
        try
        {
            var declaration = method.DeclaringSyntaxReferences[0]
                .GetSyntax(cancellationToken);
            ExpressionSyntax? expression = declaration switch
            {
                MethodDeclarationSyntax
                { ExpressionBody.Expression: { } body } => body,
                MethodDeclarationSyntax
                { Body.Statements.Count: 1 } body when
                    body.Body!.Statements[0] is ExpressionStatementSyntax
                    { Expression: { } statement } => statement,
                _ => null
            };
            if (expression == null)
            {
                return false;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, expression.SyntaxTree);
            return model.GetOperation(expression, cancellationToken) is
                IInvocationOperation invocation &&
                !invocation.IsVirtual &&
                !IsConditionallyElided(invocation) &&
                SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.OriginalDefinition,
                    method.OriginalDefinition);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool ImplicitConstructorMayCompleteNormally(
        IMethodSymbol constructor)
    {
        if (constructor.ContainingType.IsValueType)
        {
            return true;
        }

        var baseConstructor = EffectMethodNodeBuilder
            .GetUniqueParameterlessBaseConstructor(constructor);
        if (baseConstructor == null ||
            !MethodCanCompleteNormally(baseConstructor))
        {
            return false;
        }

        foreach (var operation in EffectMethodNodeBuilder
                     .GetMemberInitializerOperations(
                         compilation,
                         constructor.ContainingType,
                         staticInitializers: false,
                         cancellationToken))
        {
            if (operation != null && !MayCompleteNormally(operation))
            {
                return false;
            }
        }

        return true;
    }

    private bool ConstructorMayCompleteNormally(
        IMethodSymbol constructor,
        IConstructorBodyOperation body)
    {
        if (!MayCompleteNormally(body.Initializer))
        {
            return false;
        }

        var initializer = EffectMethodNodeBuilder
            .GetConstructorInitializerInvocation(body);
        var delegatesToThis = initializer != null &&
            SymbolEqualityComparer.Default.Equals(
                initializer.TargetMethod.ContainingType.OriginalDefinition,
                constructor.ContainingType.OriginalDefinition);
        if (!delegatesToThis)
        {
            foreach (var operation in EffectMethodNodeBuilder
                         .GetMemberInitializerOperations(
                             compilation,
                             constructor.ContainingType,
                             staticInitializers: false,
                             cancellationToken))
            {
                if (operation != null && !MayCompleteNormally(operation))
                {
                    return false;
                }
            }
        }

        return MayCompleteNormally(body.BlockBody) &&
            MayCompleteNormally(body.ExpressionBody);
    }

    private static bool DefersBodyCompletion(
        IMethodSymbol method,
        SyntaxNode declaration)
    {
        if (method.IsAsync)
        {
            return true;
        }

        var body = ExecutableBodySyntax.Get(declaration);
        return body != null && body.DescendantNodesAndSelf(
                descendIntoChildren: static node =>
                    node is not AnonymousFunctionExpressionSyntax and
                    not LocalFunctionStatementSyntax)
            .Any(static node => node is YieldStatementSyntax);
    }

    /// <summary>
    /// Computes a deliberately permissive normal-completion fact.  This is
    /// used only to suppress effects after a call that is proven never to
    /// return, so uncertainty must retain the later effects.  In particular,
    /// ordinary assignments, writes, external calls, and unsupported shapes
    /// are all treated as potentially completing.
    /// </summary>
    internal bool MayCompleteNormally(IOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryEnterCompletionTraversal(
                out var traversal,
                out var ownsTraversal))
        {
            return true;
        }

        try
        {
            var result = MayCompleteNormallyCore(operation);
            return traversal.Exhausted ? true : result;
        }
        finally
        {
            ExitCompletionTraversal(traversal, ownsTraversal);
        }
    }

    private bool MayCompleteNormallyCore(IOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation != null && IsConditionallyElided(operation))
        {
            return true;
        }
        return operation switch
        {
            null => true,
            IThrowOperation => false,
            IReturnOperation returnOperation =>
                ChildrenMayCompleteNormally(returnOperation),
            IMethodBodyOperation body =>
                SequenceMayCompleteNormally(body.ChildOperations),
            IConstructorBodyOperation body =>
                SequenceMayCompleteNormally(body.ChildOperations),
            IBlockOperation block =>
                SequenceMayCompleteNormally(block.ChildOperations),
            IConditionalOperation conditional =>
                MayCompleteNormally(conditional.Condition) &&
                (MayCompleteNormally(conditional.WhenTrue) ||
                 MayCompleteNormally(conditional.WhenFalse)),
            IConditionalAccessOperation conditionalAccess =>
                MayCompleteNormally(conditionalAccess.Operation) &&
                (MayCompleteNormally(conditionalAccess.WhenNotNull) ||
                 !DefiniteOperationFacts.IsDefinitelyNonNull(
                     conditionalAccess.Operation)),
            ISwitchExpressionOperation switchExpression =>
                MayCompleteSwitchExpression(switchExpression),
            ISwitchExpressionArmOperation arm =>
                MayCompleteNormally(arm.Pattern) &&
                (arm.Guard == null || MayCompleteNormally(arm.Guard)) &&
                MayCompleteNormally(arm.Value),
            IIsPatternOperation isPattern =>
                MayCompleteNormally(isPattern.Value) &&
                MayCompleteNormally(isPattern.Pattern),
            IPropertySubpatternOperation propertySubpattern =>
                MayCompleteNormally(propertySubpattern.Member) &&
                MayCompleteNormally(propertySubpattern.Pattern),
            IBinaryPatternOperation binaryPattern =>
                MayCompleteBinaryPattern(binaryPattern),
            IListPatternOperation listPattern =>
                MayCompleteListPattern(listPattern),
            IRecursivePatternOperation recursivePattern =>
                MayCompleteRecursivePattern(recursivePattern),
            IPatternOperation pattern =>
                ChildrenMayCompleteNormally(pattern),
            ICoalesceOperation coalesce =>
                MayCompleteNormally(coalesce.Value) &&
                (!IsDefinitelyNull(coalesce.Value) ||
                 MayCompleteNormally(coalesce.WhenNull)),
            IInvocationOperation invocation =>
                InvocationMayCompleteNormally(invocation),
            IAnonymousObjectCreationOperation or
                IDelegateCreationOperation =>
                ChildrenMayCompleteNormally(operation),
            IMethodReferenceOperation methodReference =>
                ChildrenMayCompleteNormally(methodReference) &&
                (methodReference.Method.IsStatic ||
                 methodReference.Instance == null ||
                 !IsDefinitelyNull(methodReference.Instance)),
            IObjectCreationOperation creation =>
                CreationMayCompleteNormally(creation),
            IArrayCreationOperation array =>
                ChildrenMayCompleteNormally(array),
            ILockOperation @lock =>
                MayCompleteNormally(@lock.LockedValue) &&
                MayCompleteNormally(@lock.Body),
            IBinaryOperation binary =>
                BinaryMayCompleteNormally(binary),
            IUnaryOperation or IConversionOperation or
                IIncrementOrDecrementOperation or ICompoundAssignmentOperation or
                ISimpleAssignmentOperation or IArrayElementReferenceOperation or
                IFieldReferenceOperation or
                IFlowCaptureOperation or IParenthesizedOperation or
                IArgumentOperation =>
                ChildrenMayCompleteNormally(operation),
            IPropertyReferenceOperation property =>
                ChildrenMayCompleteNormally(property) &&
                (property.Property.IsStatic ||
                 property.Instance == null ||
                 !IsDefinitelyNull(property.Instance)) &&
                (property.Property.GetMethod == null ||
                 MethodCanCompleteNormally(property.Property.GetMethod)),
            IObjectOrCollectionInitializerOperation initializer =>
                SequenceMayCompleteNormally(initializer.ChildOperations),
            IExpressionStatementOperation or
                IVariableDeclarationGroupOperation or
                IVariableDeclarationOperation or
                IVariableDeclaratorOperation or
                IVariableInitializerOperation =>
                ChildrenMayCompleteNormally(operation),
            ILabeledOperation labeled =>
                ChildrenMayCompleteNormally(labeled),
            ILoopOperation loop when
                LoopConditionIsAlwaysTrue(loop) &&
                loop.Body != null &&
                !LoopHasReachableExit(loop) => false,
            ITryOperation @try => TryMayCompleteNormally(@try),
            ILoopOperation or ISwitchOperation => true,
            _ => true
        };
    }

    private bool MayCompleteBinaryPattern(
        IBinaryPatternOperation pattern)
    {
        if (!MayCompleteNormally(pattern.LeftPattern))
        {
            return false;
        }

        var input = SwitchExpressionFacts.GetGoverningValue(pattern);
        var leftSelection =
            SwitchExpressionFacts.GetPatternSelectionForUnknownValue(
                pattern.LeftPattern,
                pattern.LeftPattern.InputType,
                input != null && IsDefinitelyNonNull(input));
        var rightIsRequired =
            pattern.OperatorKind == BinaryOperatorKind.And &&
            leftSelection == SwitchExpressionSelection.Always ||
            pattern.OperatorKind == BinaryOperatorKind.Or &&
            leftSelection == SwitchExpressionSelection.Never;
        return !rightIsRequired ||
            MayCompleteNormally(pattern.RightPattern);
    }

    private bool MayCompleteSwitchExpression(
        ISwitchExpressionOperation switchExpression)
    {
        if (!MayCompleteNormally(switchExpression.Value))
        {
            return false;
        }

        return SwitchExpressionFacts.GetReachableArms(
                switchExpression,
                MayCompleteNormally,
                IsDefinitelyNonNull(switchExpression.Value))
            .Any(MayCompleteNormally);
    }

    private bool TryMayCompleteNormally(ITryOperation @try)
    {
        return TryCompletionFacts.CanComplete(@try, MayCompleteNormally);
    }

    private bool MayCompleteRecursivePattern(
        IRecursivePatternOperation pattern)
    {
        if (HasNullableNullMismatchPath(pattern))
        {
            return true;
        }
        if (pattern.DeconstructSymbol is IMethodSymbol deconstruct &&
            !MethodCanCompleteNormally(deconstruct))
        {
            return false;
        }
        return pattern.DeconstructionSubpatterns.All(MayCompleteNormally) &&
            pattern.PropertySubpatterns.All(MayCompleteNormally);
    }

    private bool MayCompleteListPattern(IListPatternOperation pattern)
    {
        var value = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (HasNullableNullMismatchPath(pattern, value))
        {
            return true;
        }
        if (pattern.InputType?.IsValueType != true &&
            value?.Syntax.ToString().IndexOf(
                "null",
                StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        var lengthMethod =
            SwitchExpressionFacts.GetCallableListPatternMember(
                pattern.LengthSymbol);
        if (lengthMethod != null &&
            !MethodCanCompleteNormally(lengthMethod))
        {
            return false;
        }

        if (TryGetListPatternLength(pattern, out var length))
        {
            var (requiredLength, hasSlice) =
                SwitchExpressionFacts.GetListPatternShape(pattern);
            if (SwitchExpressionFacts.HasListPatternLengthMismatch(
                    requiredLength,
                    hasSlice,
                    length))
            {
                return true;
            }
        }

        foreach (var item in pattern.Patterns)
        {
            var method = SwitchExpressionFacts.GetCallableListPatternMember(
                pattern,
                item);
            if (method != null && !MethodCanCompleteNormally(method))
            {
                return false;
            }
            var nested = item is ISlicePatternOperation nestedSlice
                ? nestedSlice.Pattern
                : item;
            if (nested != null && !MayCompleteNormally(nested))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasNullableNullMismatchPath(
        IPatternOperation pattern,
        IOperation? value = null)
    {
        value ??= SwitchExpressionFacts.GetGoverningValue(pattern);
        return (ManagedAbstractValue.IsNullableType(pattern.InputType) ||
                ManagedAbstractValue.IsNullableType(value?.Type)) &&
            (value == null || !IsDefinitelyNonNull(value));
    }

    private bool TryGetListPatternLength(
        IListPatternOperation pattern,
        out long length)
    {
        var value = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (ArrayLengthFacts.TryGetConstantLength(value, out var arrayLength))
        {
            length = arrayLength;
            return true;
        }

        if (pattern.LengthSymbol is IPropertySymbol
            { GetMethod: { } getter } &&
            getter.DeclaringSyntaxReferences.Length == 1)
        {
            var syntax = getter.DeclaringSyntaxReferences[0].GetSyntax();
            ExpressionSyntax? expression = syntax switch
            {
                PropertyDeclarationSyntax property
                    when property.ExpressionBody != null =>
                    property.ExpressionBody.Expression,
                AccessorDeclarationSyntax accessor
                    when accessor.ExpressionBody != null =>
                    accessor.ExpressionBody.Expression,
                _ => null
            };
            if (expression is { } constantExpression)
            {
                var constant = SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(
                        compilation,
                        constantExpression.SyntaxTree)
                    .GetConstantValue(constantExpression);
                if (constant is { HasValue: true, Value: int constantLength })
                {
                    length = constantLength;
                    return length >= 0;
                }
            }
        }
        length = 0;
        return false;
    }

    private static bool LoopConditionIsAlwaysTrue(ILoopOperation loop)
    {
        return loop switch
        {
            IWhileLoopOperation
            {
                ConditionIsUntil: false,
                Condition.ConstantValue: { HasValue: true, Value: true }
            } => true,
            IForLoopOperation { Condition: null } => true,
            IForLoopOperation
            {
                Condition.ConstantValue: { HasValue: true, Value: true }
            } => true,
            _ => false
        };
    }

    private bool LoopHasReachableExit(ILoopOperation loop)
    {
        return HasReachableExit(loop.Body);

        bool HasReachableExit(IOperation operation)
        {
            if (operation is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                return false;
            }

            if (operation is IReturnOperation)
            {
                return MandatoryFinallysMayComplete(operation);
            }

            if (operation is IBranchOperation branch &&
                (SymbolEqualityComparer.Default.Equals(
                     branch.Target,
                     loop.ExitLabel) ||
                 IsOutwardGoto(branch)))
            {
                return MandatoryFinallysMayComplete(branch);
            }

            return operation.ChildOperations.Any(HasReachableExit);
        }

        bool IsOutwardGoto(IBranchOperation branch)
        {
            return branch.BranchKind == BranchKind.GoTo &&
                branch.Target.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree == loop.Syntax.SyntaxTree &&
                    !loop.Syntax.Span.Contains(reference.Span));
        }

        bool MandatoryFinallysMayComplete(IOperation exit)
        {
            for (var parent = exit.Parent;
                 parent != null && !ReferenceEquals(parent, loop);
                 parent = parent.Parent)
            {
                if (parent is ITryOperation { Finally: { } @finally } &&
                    !MayCompleteNormally(@finally))
                {
                    return false;
                }
            }
            return true;
        }
    }

    private bool InvocationMayCompleteNormally(IInvocationOperation invocation)
    {
        if (!MayCompleteNormally(invocation.Instance) ||
            invocation.Arguments.Any(argument =>
                !MayCompleteNormally(argument.Value)))
        {
            return false;
        }

        if (!invocation.TargetMethod.IsStatic &&
            invocation.TargetMethod.ReducedFrom == null &&
            invocation.Instance != null &&
            IsDefinitelyNull(invocation.Instance))
        {
            return false;
        }

        var target = invocation.TargetMethod.OriginalDefinition;
        return invocation.IsVirtual &&
            target.ContainingType?.IsSealed != true &&
            !target.IsSealed ||
            !HasSourceCompletionFlow(target) ||
            MethodCanCompleteNormally(target);
    }

    private bool CreationMayCompleteNormally(IObjectCreationOperation creation)
    {
        if (creation.Arguments.Any(argument =>
                !MayCompleteNormally(argument.Value)))
        {
            return false;
        }

        if (creation.Constructor is { } constructor &&
            HasSourceCompletionFlow(constructor) &&
            !MethodCanCompleteNormally(constructor))
        {
            return false;
        }

        return creation.Initializer == null ||
            MayCompleteNormally(creation.Initializer);
    }

    internal static bool HasSourceCompletionFlow(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        return method.DeclaringSyntaxReferences.Length != 0 ||
            EffectMethodNodeBuilder
                .IsSourceImplicitParameterlessConstructor(method);
    }

    private bool SequenceMayCompleteNormally(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (ManagedAbstractFlow.IsCompileTimeUnreachable(
                    compilation,
                    operation))
            {
                continue;
            }

            if (!MayCompleteNormally(operation))
            {
                return false;
            }
        }

        return true;
    }

    private bool ChildrenMayCompleteNormally(IOperation operation)
    {
        return operation.ChildOperations.All(MayCompleteNormally);
    }

    private bool BinaryMayCompleteNormally(IBinaryOperation binary)
    {
        if (!MayCompleteNormally(binary.LeftOperand) ||
            IsDefinitelyZeroDivision(binary))
        {
            return false;
        }

        if (ConversionEffectClassifier.SkipsLiftedOperator(
                binary,
                flow: null))
        {
            return ChildrenMayCompleteNormally(binary);
        }

        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or
                BinaryOperatorKind.ConditionalOr &&
            binary.OperatorMethod != null)
        {
            var truthOperator = ConditionalTruthOperatorFacts.Resolve(binary);
            if (truthOperator != null &&
                !MethodCanCompleteNormally(truthOperator))
            {
                return false;
            }

            if (truthOperator == null ||
                !ConditionalTruthOperatorFacts.ReturnsConstant(
                    compilation,
                    truthOperator,
                    out var truthResult))
            {
                // An unknown truth result retains the short-circuit path.
                return true;
            }

            return truthResult ||
                MayCompleteNormally(binary.RightOperand) &&
                MethodCanCompleteNormally(binary.OperatorMethod);
        }

        return MayCompleteNormally(binary.RightOperand) &&
            (binary.OperatorMethod == null ||
             MethodCanCompleteNormally(binary.OperatorMethod));
    }

    private static bool IsDefinitelyZeroDivision(IBinaryOperation binary)
    {
        return binary.OperatorKind is BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder &&
            binary.RightOperand.ConstantValue is { HasValue: true, Value: 0 };
    }

    private bool ChildrenCompleteNormally(
        IOperation operation,
        ManagedFlowResult? flow = null,
        IOperation? flowOrigin = null)
    {
        return operation.ChildOperations.All(child =>
            CompletesNormally(child, flow, flowOrigin));
    }

    private bool IsContractClause(
        IInvocationOperation invocation,
        ManagedFlowResult? flow = null,
        IOperation? flowOrigin = null)
    {
        return invocation.TargetMethod is
        {
            IsStatic: true,
            Name: ContractApiCatalog.RequiresMethodName or
                ContractApiCatalog.EnsuresMethodName or
                ContractApiCatalog.AssumeMethodName
        } method &&
        _contractApi != null &&
        SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, _contractApi.OriginalDefinition) &&
        invocation.Arguments.All(argument =>
            CompletesNormally(argument.Value, flow, flowOrigin));
    }

    internal static bool IsHarmlessValue(IOperation operation)
    {
        operation = UnwrapHarmlessWrappers(operation);
        return operation is
            ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or
            IInstanceReferenceOperation or IDefaultValueOperation or ITypeOfOperation or
            INameOfOperation or ISizeOfOperation;
    }

    private static IOperation UnwrapHarmlessWrappers(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion when HarmlessConversion(conversion):
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    internal static bool IsDirectArrayCreationComplete(
        IArrayCreationOperation creation)
    {
        return creation.DimensionSizes.All(static size =>
            size.ConstantValue is { HasValue: true, Value: int length } &&
            length >= 0) &&
        creation.Initializer?.ElementValues.All(IsHarmlessValue) != false;
    }

    internal static IOperation UnwrapHarmlessValue(IOperation operation)
    {
        return UnwrapHarmlessWrappers(operation);
    }

    /// <summary>
    /// Reports whether the value is certainly a string, including a string that
    /// has been converted to a wider reference type. Callers use this to decide
    /// that a cast back to <c>string</c> cannot fail, so both call-site and
    /// effect-precondition analysis must agree on it.
    /// </summary>
    internal static bool IsDefinitelyString(IOperation operation)
    {
        operation = UnwrapHarmlessValue(operation);
        return operation.Type?.SpecialType == SpecialType.System_String ||
            operation is IConversionOperation
            {
                Operand.Type.SpecialType: SpecialType.System_String
            };
    }

    internal static bool IsDefinitelyNonNull(IOperation operation)
    {
        operation = UnwrapSimpleConversions(operation);
        return operation is IInstanceReferenceOperation or IConditionalAccessInstanceOperation ||
            operation is IObjectCreationOperation creation &&
                !ManagedAbstractValue.IsEmptyNullableCreation(creation) ||
            operation is IArrayCreationOperation or ITypeOfOperation ||
            operation.ConstantValue is { HasValue: true, Value: not null };
    }

    internal static bool IsDefinitelyNull(IOperation operation)
    {
        operation = UnwrapSimpleConversions(operation);
        return operation is IObjectCreationOperation creation &&
                ManagedAbstractValue.IsEmptyNullableCreation(creation) ||
            operation.ConstantValue is { HasValue: true, Value: null };
    }

    private static IOperation UnwrapSimpleConversions(IOperation operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation)
        {
            if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else if (operation is IConversionOperation
            {
                OperatorMethod: null,
                IsTryCast: false
            } conversion)
            {
                operation = conversion.Operand;
            }
            else
            {
                break;
            }
        }
        return operation;
    }

    private static bool HarmlessConversion(IConversionOperation conversion)
    {
        return conversion.OperatorMethod == null &&
        !conversion.IsChecked &&
        !conversion.Conversion.IsUserDefined &&
        (conversion.Conversion.IsIdentity ||
         conversion.Conversion.IsImplicit) &&
        conversion.Operand.Type?.TypeKind != TypeKind.Dynamic &&
        conversion.Type?.TypeKind != TypeKind.Dynamic &&
        !(conversion.Operand.Type?.IsValueType is true && conversion.Type?.IsReferenceType is true);
    }

}

namespace SharpProof.Symbolic.Ir;

/// <summary>Projects symbolic program-point state from the compiler CFG runner.</summary>
internal static class CompilerProgramPointAnalysis {
    internal static SymbolicLoweringResult<SymbolicState> Collect(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState = null,
        bool includeCurrentStatementCompletionFacts = false,
        bool forInitialEntry = false) {
        cancellationToken.ThrowIfCancellationRequested();
        var searchNode = site is LocalFunctionStatementSyntax ? site.Parent ?? site : site;
        var root = CSharpSyntaxFacts.GetContainingExecutionRoot(searchNode, ExecutionRootPolicy.Callable);
        if (root == null) return Unsupported(site, "execution-root");
        ControlFlowGraph? graph;
        try { graph = ControlFlowGraph.Create(root, semanticModel, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            return Unsupported(site, "cfg");
        }
        var owner = semanticModel.GetDeclaredSymbol(root, cancellationToken) ??
                    semanticModel.GetEnclosingSymbol(root.SpanStart, cancellationToken);
        if (graph == null || owner == null) return Unsupported(site, "cfg-empty");
        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state, site, semanticModel, cancellationToken);
        var domain = new ProgramPointDomain(
            site, semanticModel, cancellationToken, includeCurrentStatementCompletionFacts, forInitialEntry,
            state.NormalizedProofKey);
        var result = AnalyzerUtilitiesControlFlowAnalysis.Run(
            graph, state, domain, semanticModel.Compilation, owner, cancellationToken);
        var captured = domain.CapturedState;
        if (captured == null && domain.TargetIsCompilerUnreachable)
            captured = result.ExitState.MarkContradictory();
        return captured == null
            ? Unsupported(site, result.Truncated ? "iteration-limit" : "target-block")
            : SymbolicLoweringResult<SymbolicState>.Exact(
                captured.Normalize(), new("compiler-cfg-program-point", site.Span, "exact"));
    }

    private static SymbolicLoweringResult<SymbolicState> Unsupported(SyntaxNode site, string detail) =>
        SymbolicLoweringResult<SymbolicState>.Unsupported(
            new SymbolicLoweringProvenance("compiler-cfg-program-point", site.Span, detail));

    private sealed class ProgramPointDomain(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeCompletion,
        bool forInitialEntry,
        string initialKey) : IControlFlowDomain<SymbolicState> {
        private SymbolicState? captured;
        private bool targetIsCompilerUnreachable;
        private readonly Stack<Dictionary<IParameterSymbol, Int32BoundFacts>> parameterBoundScopes = new();
        internal bool TargetIsCompilerUnreachable => targetIsCompilerUnreachable;
        internal SymbolicState? CapturedState => captured;
        public void SetControlFlowGraph(ControlFlowGraph graph, PointsToAnalysisResult? pointsToAnalysisResult) =>
            targetIsCompilerUnreachable = graph.Blocks.Any(block => !block.IsReachable &&
                block.Operations.Append(block.BranchValue).Where(static operation => operation != null)
                    .Any(operation => Contains(operation!.Syntax, site)));
        public SymbolicState Transfer(SymbolicState state, IOperation operation) {
            if (state.IsContradictory || operation is ILocalFunctionOperation) return state;
            var target = IsTarget(operation);
            if (target && !includeCompletion) Capture(state);
            var updated = state;
            if (!SymbolicCfgProgramPointStateCollector.TryApplyOperation(
                    ref updated, operation, null, true, true, true,
                    semanticModel, cancellationToken, "compiler-flow", out _)) {
                SymbolicStateInvalidator.InvalidateNestedMutations(
                    ref updated, operation.Syntax, semanticModel, cancellationToken);
            }
            if (target && includeCompletion) Capture(updated);
            return updated;
        }
        public SymbolicState Refine(
            SymbolicState state,
            IOperation? condition,
            ControlFlowConditionKind kind,
            bool conditionalSuccessor,
            BasicBlock _) {
            if (condition?.Syntax is not ExpressionSyntax expression) return state;
            if (forInitialEntry && site is ForStatementSyntax statement &&
                statement.Condition != null && Contains(condition.Syntax, statement.Condition))
                Capture(state);
            var branchWhenTrue = kind switch {
                ControlFlowConditionKind.WhenTrue => conditionalSuccessor,
                ControlFlowConditionKind.WhenFalse => !conditionalSuccessor,
                _ => conditionalSuccessor
            };
            SymbolicReachabilityLowerer.Apply(
                ref state, expression, branchWhenTrue, semanticModel, cancellationToken);
            return state;
        }
        public SymbolicState Merge(SymbolicState current, SymbolicState incoming) {
            if (current.NormalizedProofKey == initialKey && incoming.NormalizedProofKey != initialKey ||
                IsSubset(current, incoming))
                return incoming;
            if (IsSubset(incoming, current)) return current;
            return SymbolicStateMerger.MergePathStatesAcrossAll(
                [current, incoming], static (left, right) => SymbolicState.CreateProofFactKey(left) ==
                                                         SymbolicState.CreateProofFactKey(right), site.SpanStart);
        }
        public SymbolicState CompleteBlock(SymbolicState state, BasicBlock block) => state;
        public bool Equivalent(SymbolicState left, SymbolicState right) =>
            string.Equals(left.NormalizedProofKey, right.NormalizedProofKey, StringComparison.Ordinal);
        public bool IsUnreachable(SymbolicState state) => state.IsContradictory;
        public string GetKey(SymbolicState state) => state.NormalizedProofKey;
        private bool IsTarget(IOperation operation) => !forInitialEntry &&
            SymbolicCfgProgramPointStateCollector.IsTargetOperation(
                operation, site, includeCompletion, semanticModel, cancellationToken);
        private void Capture(SymbolicState state) {
            ApplyContainingSequencePredicateFacts(ref state);
            ApplyContainingSwitchFacts(ref state);
            foreach (var conditional in site.Ancestors().OfType<ConditionalExpressionSyntax>()) {
                bool? branchWhenTrue = conditional.WhenTrue.Span.Contains(site.SpanStart)
                    ? true
                    : conditional.WhenFalse.Span.Contains(site.SpanStart)
                        ? false
                        : null;
                if (!branchWhenTrue.HasValue) continue;
                SymbolicReachabilityLowerer.ApplyConditionOnly(
                    ref state,
                    conditional.Condition,
                    branchWhenTrue.Value,
                    semanticModel,
                    cancellationToken);
            }
            if (site.FirstAncestorOrSelf<StatementSyntax>() is { Parent: BlockSyntax block } target) {
                foreach (var statement in block.Statements.TakeWhile(candidate => !ReferenceEquals(candidate, target))) {
                    if (statement is not IfStatementSyntax { Else: null } conditional ||
                        !AlwaysCompletes(conditional.Statement))
                        continue;
                    SymbolicReachabilityLowerer.ApplyConditionOnly(
                        ref state, conditional.Condition, false, semanticModel, cancellationToken);
                }
            }
            captured = state;
        }
        private readonly record struct SequencePredicateStep(
            ExpressionSyntax Predicate,
            bool GuaranteesTruth);
        private void ApplyContainingSequencePredicateFacts(ref SymbolicState state) {
            foreach (var loop in site.Ancestors().OfType<ForEachStatementSyntax>()) {
                if (!loop.Statement.Span.Contains(site.SpanStart) ||
                    semanticModel.GetDeclaredSymbol(loop, cancellationToken) is not ILocalSymbol iterationVariable ||
                    !TryGetSequencePredicateSteps(
                        loop.Expression,
                        out var predicateSteps,
                        out var definitelyNoElements))
                    continue;
                if (definitelyNoElements) {
                    state = state.MarkContradictory();
                    continue;
                }
                if (SymbolicMutationInventory.Create(loop.Statement, semanticModel, cancellationToken)
                        .InvalidatesBetween(loop.Statement.SpanStart - 1, site.SpanStart, iterationVariable, true) ||
                    !SymbolicStateFactBuilder.TryCreateSymbolTerm(iterationVariable, out var iterationTerm))
                    continue;
                var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
                foreach (var step in predicateSteps) {
                    if (!SymbolicSourcePredicateLowerer.TryLowerSequencePredicate(
                            step.Predicate,
                            iterationTerm,
                            context,
                            out var condition))
                        break;
                    if (step.GuaranteesTruth)
                        state = state.AddPathCondition(condition).Normalize();
                }
            }
        }
        private bool TryGetSequencePredicateSteps(
            ExpressionSyntax collection,
            out ImmutableArray<SequencePredicateStep> steps,
            out bool definitelyNoElements) =>
            TryGetSequencePredicateSteps(
                collection,
                [],
                out steps,
                out definitelyNoElements);
        private bool TryGetSequencePredicateSteps(
            ExpressionSyntax collection,
            HashSet<SyntaxNode> visited,
            out ImmutableArray<SequencePredicateStep> steps,
            out bool definitelyNoElements) {
            var builder = ImmutableArray.CreateBuilder<SequencePredicateStep>();
            var resolvedUses = new List<(ISymbol Symbol, int Position)>();
            var resolutionUsePosition = collection.SpanStart;
            SymbolicMutationInventory? resolutionMutations = null;
            definitelyNoElements = false;
            var preservesElementIdentity = true;
            while (true) {
                collection = SymbolicConversionLowerer.UnwrapIdentityConversions(
                    collection,
                    semanticModel,
                    cancellationToken);
                if (!visited.Add(collection))
                    break;
                if (TryGetSequencePredicateStep(
                        collection,
                        out var source,
                        out var predicate,
                        out var guaranteesTruth)) {
                    if (preservesElementIdentity)
                        builder.Add(new(predicate, guaranteesTruth));
                    collection = source;
                    continue;
                }
                if (TryGetLazySequencePreservingStep(
                        collection,
                        out source,
                        out var stepProducesNoElements)) {
                    definitelyNoElements |= stepProducesNoElements;
                    collection = source;
                    continue;
                }
                if (TryGetSequenceProjectionStep(
                        collection,
                        out source,
                        out var projectionPreservesElementIdentity)) {
                    preservesElementIdentity &= projectionPreservesElementIdentity;
                    collection = source;
                    continue;
                }
                if (TryGetSequenceCombinationStep(
                        collection,
                        visited,
                        out source,
                        out var combinationPreservesElementIdentity)) {
                    preservesElementIdentity &= combinationPreservesElementIdentity;
                    collection = source;
                    continue;
                }
                if (TryGetZeroPreservingCoreSequenceConstructionStep(
                        collection,
                        out source,
                        out var constructionProducesNoElements)) {
                    definitelyNoElements |= constructionProducesNoElements;
                    preservesElementIdentity = false;
                    collection = source;
                    continue;
                }
                if (TryGetZeroPreservingCollectionWrapperStep(collection, out source)) {
                    preservesElementIdentity = false;
                    collection = source;
                    continue;
                }
                if (TryGetZeroPreservingSequenceViewStep(
                        collection,
                        out source,
                        out var viewProducesNoElements)) {
                    definitelyNoElements |= viewProducesNoElements;
                    preservesElementIdentity = false;
                    collection = source;
                    continue;
                }
                if (TryGetZeroPreservingImmutableFactoryStep(collection, out source)) {
                    preservesElementIdentity = false;
                    collection = source;
                    continue;
                }
                if (TryGetZeroPreservingSequenceOperatorStep(
                        collection,
                        visited,
                        out source,
                        out var operatorProducesNoElements)) {
                    definitelyNoElements |= operatorProducesNoElements;
                    preservesElementIdentity = false;
                    collection = source;
                    continue;
                }
                if (TryGetElementTypeOperatorStep(
                        collection,
                        out source,
                        out var operatorPreservesElementIdentity)) {
                    preservesElementIdentity &= operatorPreservesElementIdentity;
                    collection = source;
                    continue;
                }
                if (DefinitelyProducesNoElements(collection)) {
                    definitelyNoElements = true;
                    break;
                }
                collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
                var symbol = semanticModel.GetSymbolInfo(collection, cancellationToken).Symbol?.OriginalDefinition;
                var usePosition = collection.SpanStart;
                if (symbol is not (ILocalSymbol or IParameterSymbol) ||
                    resolvedUses.Any(candidate =>
                        candidate.Position == usePosition &&
                        SymbolEqualityComparer.Default.Equals(candidate.Symbol, symbol)) ||
                    usePosition < resolutionUsePosition &&
                    (resolutionMutations ??= SymbolicMutationInventory.Create(
                        CSharpSyntaxFacts.GetContainingExecutionRoot(collection),
                        semanticModel,
                        cancellationToken)).InvalidatesCapturedValueBetween(
                        usePosition,
                        resolutionUsePosition,
                        symbol) ||
                    !SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                        symbol,
                        collection,
                        semanticModel,
                        cancellationToken,
                        true,
                        true,
                        out collection))
                    break;
                resolvedUses.Add((symbol, usePosition));
            }
            steps = builder.ToImmutable();
            return steps.Length != 0 || definitelyNoElements;
        }
        private bool TryGetSequenceCombinationStep(
            ExpressionSyntax collection,
            HashSet<SyntaxNode> visited,
            out ExpressionSyntax source,
            out bool preservesElementIdentity) {
            source = null!;
            preservesElementIdentity = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.ContainingType.ToDisplayString() is not
                    ("System.Linq.Enumerable" or "System.Linq.Queryable") ||
                definition.Name is not ("Concat" or "Union" or "UnionBy") ||
                !TryGetStandardSequenceSource(invocation, operation, out source))
                return false;
            if (definition.Parameters.Length < 2 ||
                !TryGetInvocationArgument(operation, definition.Parameters[1], out var second))
                return false;
            if (TryGetSequencePredicateSteps(
                    second,
                    [.. visited],
                    out _,
                    out var secondProducesNoElements) &&
                secondProducesNoElements) {
                preservesElementIdentity = definition.Name == "Concat";
                return true;
            }
            if (definition.Name != "Concat" ||
                !TryGetSequencePredicateSteps(
                    source,
                    [.. visited],
                    out _,
                    out var sourceProducesNoElements) ||
                !sourceProducesNoElements)
                return false;
            source = second;
            preservesElementIdentity = true;
            return true;
        }
        private bool TryGetZeroPreservingCoreSequenceConstructionStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source,
            out bool definitelyNoElements) {
            source = null!;
            definitelyNoElements = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (semanticModel.GetOperation(collection, cancellationToken) is not
                IObjectCreationOperation {
                    Constructor: { Parameters.Length: > 0 } constructor,
                    Type: INamedTypeSymbol type
                } creation ||
                creation.Initializer is { Initializers.Length: > 0 } ||
                !IsKnownCoreSequenceType(type) ||
                constructor.Parameters[0].Type is not IArrayTypeSymbol)
                return false;
            var sourceParameter = constructor.Parameters[0];
            source = creation.Arguments
                .FirstOrDefault(argument =>
                    argument.Parameter != null &&
                    SymbolEqualityComparer.Default.Equals(
                        argument.Parameter.OriginalDefinition,
                        sourceParameter.OriginalDefinition))
                ?.Value.Syntax as ExpressionSyntax ?? null!;
            if (source == null)
                return false;
            var limitParameter = constructor.Parameters.FirstOrDefault(parameter =>
                parameter.Name is "count" or "length");
            if (limitParameter != null &&
                creation.Arguments.FirstOrDefault(argument =>
                    argument.Parameter != null &&
                    SymbolEqualityComparer.Default.Equals(
                        argument.Parameter.OriginalDefinition,
                        limitParameter.OriginalDefinition))?.Value.Syntax is ExpressionSyntax limit)
                definitelyNoElements = DefinitelyCapsSequenceAtZero(limit);
            return true;
        }
        private bool TryGetZeroPreservingCollectionWrapperStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source) {
            source = null!;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (semanticModel.GetOperation(collection, cancellationToken) is not
                IObjectCreationOperation {
                    Constructor: { Parameters.Length: 1 } constructor,
                    Type: INamedTypeSymbol type
                } creation ||
                creation.Initializer is { Initializers.Length: > 0 } ||
                !IsKnownCollectionWrapperType(type))
                return false;
            var parameter = constructor.Parameters[0];
            source = creation.Arguments
                .FirstOrDefault(argument =>
                    argument.Parameter != null &&
                    SymbolEqualityComparer.Default.Equals(
                        argument.Parameter.OriginalDefinition,
                        parameter.OriginalDefinition))
                ?.Value.Syntax as ExpressionSyntax ?? null!;
            return source != null;
        }
        private static bool IsKnownCollectionWrapperType(INamedTypeSymbol candidate) {
            var type = candidate.OriginalDefinition;
            if (type.ContainingAssembly?.Name is not (
                    "System.ObjectModel" or
                    "System.Private.CoreLib" or
                    "System.Runtime" or
                    "mscorlib"))
                return false;
            return (type.ContainingNamespace.ToDisplayString(), type.Name, type.Arity) is
                ("System.Collections.ObjectModel", "Collection", 1) or
                ("System.Collections.ObjectModel", "ReadOnlyCollection", 1) or
                ("System.Collections.ObjectModel", "ReadOnlyDictionary", 2) or
                ("System.Collections.ObjectModel", "ReadOnlyObservableCollection", 1);
        }
        private bool TryGetZeroPreservingSequenceViewStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source,
            out bool definitelyNoElements) {
            source = null!;
            definitelyNoElements = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(collection, cancellationToken).Symbol is
                    IPropertySymbol {
                        IsStatic: false,
                        Parameters.Length: 0,
                        GetMethod: not null
                    } property) {
                var supportedProperty =
                    property.Name == "Span" &&
                    property.ContainingType.OriginalDefinition.Name is "Memory" or "ReadOnlyMemory" &&
                    IsKnownCoreSequenceType(property.ContainingType) ||
                    property.Name is "Keys" or "Values" &&
                    IsKnownDictionaryViewType(property.ContainingType);
                if (supportedProperty) {
                    source = memberAccess.Expression;
                    return true;
                }
            }
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (!IsCoreLibraryType(definition.ContainingType) ||
                definition.ContainingNamespace.ToDisplayString() != "System")
                return false;
            var supported =
                definition.ContainingType.Name == "MemoryExtensions" &&
                definition.Name is "AsMemory" or "AsSpan" or "ToArray" ||
                IsKnownCoreSequenceType(definition.ContainingType) &&
                definition.Name is "Slice" or "ToArray" ||
                definition.ContainingType.SpecialType == SpecialType.System_String &&
                definition.Name == "ToCharArray";
            if (!supported ||
                !TryGetStandardSequenceSource(invocation, operation, out source))
                return false;
            var lengthParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                parameter.Name == "length");
            if (lengthParameter != null &&
                TryGetInvocationArgument(operation, lengthParameter, out var length))
                definitelyNoElements = DefinitelyCapsSequenceAtZero(length);
            return true;
        }
        private static bool IsKnownDictionaryViewType(INamedTypeSymbol candidate) {
            var type = candidate.OriginalDefinition;
            if (type.ContainingAssembly?.Name is not (
                    "System.Collections" or
                    "System.Collections.Concurrent" or
                    "System.Collections.Immutable" or
                    "System.ObjectModel" or
                    "System.Private.CoreLib" or
                    "System.Runtime" or
                    "mscorlib"))
                return false;
            return (type.ContainingNamespace.ToDisplayString(), type.Name, type.Arity) is
                ("System.Collections", "Hashtable", 0) or
                ("System.Collections", "IDictionary", 0) or
                ("System.Collections", "SortedList", 0) or
                ("System.Collections.Concurrent", "ConcurrentDictionary", 2) or
                ("System.Collections.Frozen", "FrozenDictionary", 2) or
                ("System.Collections.Generic", "Dictionary", 2) or
                ("System.Collections.Generic", "IDictionary", 2) or
                ("System.Collections.Generic", "IReadOnlyDictionary", 2) or
                ("System.Collections.Generic", "SortedDictionary", 2) or
                ("System.Collections.Generic", "SortedList", 2) or
                ("System.Collections.Immutable", "ImmutableDictionary", 2) or
                ("System.Collections.Immutable", "ImmutableSortedDictionary", 2) or
                ("System.Collections.ObjectModel", "ReadOnlyDictionary", 2);
        }
        private bool TryGetZeroPreservingImmutableFactoryStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source) {
            source = null!;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.ContainingAssembly?.Name != "System.Collections.Immutable" ||
                definition.ContainingNamespace.ToDisplayString() != "System.Collections.Immutable" ||
                definition.Name is not (
                    "CreateRange" or
                    "ToImmutableArray" or
                    "ToImmutableDictionary" or
                    "ToImmutableHashSet" or
                    "ToImmutableList" or
                    "ToImmutableQueue" or
                    "ToImmutableSortedDictionary" or
                    "ToImmutableSortedSet" or
                    "ToImmutableStack") ||
                targetMethod.ReturnType is not INamedTypeSymbol returnType ||
                !IsKnownImmutableCollectionType(returnType))
                return false;
            return TryGetStandardSequenceSource(invocation, operation, out source);
        }
        private bool TryGetZeroPreservingSequenceOperatorStep(
            ExpressionSyntax collection,
            HashSet<SyntaxNode> visited,
            out ExpressionSyntax source,
            out bool definitelyNoElements) {
            source = null!;
            definitelyNoElements = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.ContainingType.ToDisplayString() is not
                    ("System.Linq.Enumerable" or "System.Linq.Queryable") ||
                definition.Name is not (
                    "Chunk" or
                    "Distinct" or
                    "DistinctBy" or
                    "Except" or
                    "ExceptBy" or
                    "GroupBy" or
                    "GroupJoin" or
                    "Intersect" or
                    "IntersectBy" or
                    "Join" or
                    "Order" or
                    "OrderBy" or
                    "OrderByDescending" or
                    "OrderDescending" or
                    "Reverse" or
                    "SkipLast" or
                    "TakeLast" or
                    "ThenBy" or
                    "ThenByDescending" or
                    "ToArray" or
                    "ToDictionary" or
                    "ToHashSet" or
                    "ToList" or
                    "ToLookup" or
                    "Zip"))
                return false;
            if (!TryGetStandardSequenceSource(invocation, operation, out source))
                return false;
            if (definition.Name is "Intersect" or "IntersectBy" or "Join" or "Zip" &&
                definition.Parameters.Length >= 2 &&
                TryGetInvocationArgument(operation, definition.Parameters[1], out var second) &&
                TryGetSequencePredicateSteps(
                    second,
                    [.. visited],
                    out _,
                    out var secondProducesNoElements))
                definitelyNoElements = secondProducesNoElements;
            if (definition.Name == "TakeLast" &&
                targetMethod.Parameters.Length != 0 &&
                TryGetInvocationArgument(
                    operation,
                    targetMethod.Parameters[targetMethod.Parameters.Length - 1],
                    out var count) &&
                DefinitelyCapsSequenceAtZero(count))
                definitelyNoElements = true;
            return true;
        }
        private bool DefinitelyProducesNoElements(ExpressionSyntax collection) {
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (IsDefinitelyEmptyString(collection))
                return true;
            if (IsKnownCoreEmptySingleton(collection))
                return true;
            if (IsKnownImmutableEmptySingleton(collection))
                return true;
            if (IsKnownDefaultEmptyCoreSequenceValue(collection))
                return true;
            if (IsKnownEmptyCollectionConstruction(collection))
                return true;
            if (collection is CollectionExpressionSyntax { Elements.Count: 0 })
                return true;
            if (collection is ArrayCreationExpressionSyntax arrayCreation) {
                if (arrayCreation.Initializer is { Expressions.Count: 0 })
                    return true;
                if (arrayCreation.Type.RankSpecifiers
                    .SelectMany(rank => rank.Sizes)
                    .Any(DefinitelyCapsSequenceAtZero))
                    return true;
            }
            if (collection is StackAllocArrayCreationExpressionSyntax stackAllocation) {
                if (stackAllocation.Initializer is { Expressions.Count: 0 })
                    return true;
                if (stackAllocation.Type is ArrayTypeSyntax arrayType &&
                    arrayType.RankSpecifiers
                        .SelectMany(rank => rank.Sizes)
                        .Any(DefinitelyCapsSequenceAtZero))
                    return true;
            }
            if (collection is ImplicitStackAllocArrayCreationExpressionSyntax {
                Initializer.Expressions.Count: 0
            })
                return true;
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            if (IsKnownEmptyRuntimeArrayFactory(operation, targetMethod))
                return true;
            if (IsKnownImmutableEmptyFactory(targetMethod))
                return true;
            var definition = targetMethod.OriginalDefinition;
            var containingType = definition.ContainingType.ToDisplayString();
            if (definition.Name == nameof(Enumerable.Empty) &&
                definition.Parameters.Length == 0 &&
                containingType is "System.Linq.Enumerable" or "System.Array")
                return true;
            if (containingType != "System.Linq.Enumerable" ||
                definition.Name is not (nameof(Enumerable.Range) or nameof(Enumerable.Repeat)))
                return false;
            var countParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                parameter.Name == "count");
            return countParameter != null &&
                   TryGetInvocationArgument(operation, countParameter, out var count) &&
                   DefinitelyCapsSequenceAtZero(count);
        }
        private bool IsKnownEmptyRuntimeArrayFactory(
            IInvocationOperation operation,
            IMethodSymbol targetMethod) {
            var definition = targetMethod.OriginalDefinition;
            var containingType = definition.ContainingType;
            var knownFactory =
                (IsCoreLibraryType(containingType) &&
                 containingType.ContainingNamespace.ToDisplayString() == "System" &&
                 containingType.Name == "GC" &&
                 definition.Name is "AllocateArray" or "AllocateUninitializedArray" &&
                 targetMethod.ReturnType is IArrayTypeSymbol) ||
                (containingType.SpecialType == SpecialType.System_Array &&
                 definition.Name == "CreateInstance");
            if (!knownFactory)
                return false;
            foreach (var parameter in targetMethod.Parameters) {
                if (parameter.Type.SpecialType == SpecialType.System_Int32 &&
                    parameter.Name.StartsWith("length", StringComparison.Ordinal) &&
                    TryGetInvocationArgument(operation, parameter, out var length) &&
                    DefinitelyCapsSequenceAtZero(length))
                    return true;
                if (parameter is {
                    Name: "lengths",
                    Type: IArrayTypeSymbol {
                        Rank: 1,
                        ElementType.SpecialType: SpecialType.System_Int32
                    }
                } &&
                    TryGetInvocationArgument(operation, parameter, out var lengths) &&
                    DefinitelyPreventsRuntimeArrayElements(lengths))
                    return true;
            }
            return false;
        }
        private readonly record struct ArrayDimensionVectorFacts(
            bool DefinitelyEmpty,
            bool DefinitelyContainsNonPositive,
            bool PreventsNonEmptyAllPositive,
            bool DefinitelyAllNonPositive);
        private bool DefinitelyPreventsRuntimeArrayElements(ExpressionSyntax dimensions) =>
            GetArrayDimensionVectorFacts(dimensions, [], null).PreventsNonEmptyAllPositive;
        private ArrayDimensionVectorFacts GetArrayDimensionVectorFacts(
            ExpressionSyntax dimensions,
            HashSet<SyntaxNode> visited,
            SyntaxNode? deferredUse) {
            dimensions = SymbolicConversionLowerer.UnwrapIdentityConversions(
                dimensions,
                semanticModel,
                cancellationToken);
            if (!visited.Add(dimensions))
                return default;
            if (TryGetSequencePredicateSteps(
                    dimensions,
                    [],
                    out _,
                    out var definitelyNoDimensions) &&
                definitelyNoDimensions)
                return new(true, false, true, true);
            if (TryGetValuePreservingArrayDimensionProducerFacts(
                    dimensions,
                    visited,
                    deferredUse,
                    out var producerFacts))
                return producerFacts;
            switch (dimensions) {
                case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                    return GetExplicitArrayDimensionVectorFacts(initializer.Expressions);
                case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }:
                    return GetExplicitArrayDimensionVectorFacts(initializer.Expressions);
                case ArrayCreationExpressionSyntax { Initializer: null } array:
                    var size = array.Type.RankSpecifiers
                        .SelectMany(rank => rank.Sizes)
                        .FirstOrDefault();
                    if (size != null && DefinitelyCapsSequenceAtZero(size))
                        return new(true, false, true, true);
                    return size != null && DefinitelyProvidesPositiveDimensionCount(size)
                        ? new(false, true, true, true)
                        : new(false, false, true, true);
                case CollectionExpressionSyntax collection:
                    var hasExpression = false;
                    var definitelyContainsNonPositive = false;
                    var definitelyAllNonPositive = true;
                    var allSpreadsEmpty = true;
                    var allSpreadsPreventNonEmptyAllPositive = true;
                    foreach (var element in collection.Elements) {
                        if (element is ExpressionElementSyntax expression) {
                            hasExpression = true;
                            var nonPositive = DefinitelyCapsSequenceAtZero(expression.Expression);
                            definitelyContainsNonPositive |= nonPositive;
                            definitelyAllNonPositive &= nonPositive;
                            continue;
                        }
                        if (element is not SpreadElementSyntax spread)
                            return default;
                        var spreadFacts = GetArrayDimensionVectorFacts(
                            spread.Expression,
                            [.. visited],
                            deferredUse);
                        definitelyContainsNonPositive |= spreadFacts.DefinitelyContainsNonPositive;
                        definitelyAllNonPositive &= spreadFacts.DefinitelyAllNonPositive;
                        allSpreadsEmpty &= spreadFacts.DefinitelyEmpty;
                        allSpreadsPreventNonEmptyAllPositive &=
                            spreadFacts.PreventsNonEmptyAllPositive;
                    }
                    var definitelyEmpty = !hasExpression && allSpreadsEmpty;
                    var preventsNonEmptyAllPositive =
                        definitelyEmpty ||
                        definitelyContainsNonPositive ||
                        definitelyAllNonPositive ||
                        !hasExpression && allSpreadsPreventNonEmptyAllPositive;
                    return new(
                        definitelyEmpty,
                        definitelyContainsNonPositive,
                        preventsNonEmptyAllPositive,
                        definitelyAllNonPositive);
            }
            dimensions = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(dimensions);
            var symbol = semanticModel.GetSymbolInfo(dimensions, cancellationToken).Symbol?.OriginalDefinition;
            return symbol is ILocalSymbol or IParameterSymbol &&
                   SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                       symbol,
                       deferredUse ?? dimensions,
                       semanticModel,
                       cancellationToken,
                       false,
                       true,
                       out var resolved)
                ? GetArrayDimensionVectorFacts(resolved, visited, deferredUse)
                : default;
        }
        private ArrayDimensionVectorFacts GetExplicitArrayDimensionVectorFacts(
            SeparatedSyntaxList<ExpressionSyntax> dimensions) {
            if (dimensions.Count == 0)
                return new(true, false, true, true);
            var definitelyContainsNonPositive = dimensions.Any(DefinitelyCapsSequenceAtZero);
            var definitelyAllNonPositive = dimensions.All(DefinitelyCapsSequenceAtZero);
            return new(
                false,
                definitelyContainsNonPositive,
                definitelyContainsNonPositive,
                definitelyAllNonPositive);
        }
        private bool TryGetValuePreservingArrayDimensionProducerFacts(
            ExpressionSyntax dimensions,
            HashSet<SyntaxNode> visited,
            SyntaxNode? deferredUse,
            out ArrayDimensionVectorFacts facts) {
            facts = default;
            if (dimensions is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.ContainingType.ToDisplayString() != "System.Linq.Enumerable")
                return false;
            if (definition.Name == nameof(Enumerable.Repeat)) {
                var elementParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "element");
                var countParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "count");
                if (elementParameter == null ||
                    countParameter == null ||
                    !TryGetInvocationArgument(operation, elementParameter, out var element) ||
                    !TryGetInvocationArgument(operation, countParameter, out var count) ||
                    !DefinitelyCapsSequenceAtZero(element))
                    return false;
                facts = DefinitelyCapsSequenceAtZero(count)
                    ? new(true, false, true, true)
                    : DefinitelyProvidesPositiveDimensionCount(count)
                        ? new(false, true, true, true)
                        : new(false, false, true, true);
                return true;
            }
            if (definition.Name == nameof(Enumerable.Select)) {
                var selectorParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "selector");
                if (selectorParameter == null ||
                    !TryGetStandardSequenceSource(invocation, operation, out var projectedSource) ||
                    !TryGetInvocationArgument(operation, selectorParameter, out var selector))
                    return false;
                var projectedSourceFacts = GetArrayDimensionVectorFacts(
                    projectedSource,
                    [.. visited],
                    deferredUse);
                if (projectedSourceFacts.DefinitelyEmpty) {
                    facts = new(true, false, true, true);
                    return true;
                }
                if (!IsNonPositiveDimensionSelector(
                        selector,
                        projectedSourceFacts.DefinitelyAllNonPositive))
                    return false;
                facts = new(
                    false,
                    false,
                    true,
                    true);
                return true;
            }
            if (definition.Name == nameof(Enumerable.Concat)) {
                var secondParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "second");
                if (secondParameter == null ||
                    !TryGetStandardSequenceSource(invocation, operation, out var first) ||
                    !TryGetInvocationArgument(operation, secondParameter, out var second))
                    return false;
                var firstFacts = GetArrayDimensionVectorFacts(
                    first,
                    [.. visited],
                    deferredUse);
                var secondFacts = GetArrayDimensionVectorFacts(
                    second,
                    [.. visited],
                    deferredUse);
                var definitelyEmpty = firstFacts.DefinitelyEmpty && secondFacts.DefinitelyEmpty;
                var definitelyContainsNonPositive =
                    firstFacts.DefinitelyContainsNonPositive ||
                    secondFacts.DefinitelyContainsNonPositive;
                var definitelyAllNonPositive =
                    firstFacts.DefinitelyAllNonPositive &&
                    secondFacts.DefinitelyAllNonPositive;
                var preventsNonEmptyAllPositive =
                    definitelyEmpty ||
                    definitelyContainsNonPositive ||
                    definitelyAllNonPositive ||
                    firstFacts.PreventsNonEmptyAllPositive &&
                    secondFacts.PreventsNonEmptyAllPositive;
                facts = new(
                    definitelyEmpty,
                    definitelyContainsNonPositive,
                    preventsNonEmptyAllPositive,
                    definitelyAllNonPositive);
                return facts != default;
            }
            if (definition.Name is nameof(Enumerable.Union) or "UnionBy") {
                var secondParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "second");
                if (secondParameter == null ||
                    !TryGetStandardSequenceSource(invocation, operation, out var first) ||
                    !TryGetInvocationArgument(operation, secondParameter, out var second))
                    return false;
                var firstFacts = GetArrayDimensionVectorFacts(
                    first,
                    [.. visited],
                    deferredUse);
                var secondFacts = GetArrayDimensionVectorFacts(
                    second,
                    [.. visited],
                    deferredUse);
                var definitelyEmpty = firstFacts.DefinitelyEmpty && secondFacts.DefinitelyEmpty;
                var definitelyAllNonPositive =
                    firstFacts.DefinitelyAllNonPositive &&
                    secondFacts.DefinitelyAllNonPositive;
                facts = new(
                    definitelyEmpty,
                    false,
                    definitelyEmpty || definitelyAllNonPositive,
                    definitelyAllNonPositive);
                return facts != default;
            }
            if (definition.Name is "Append" or "Prepend") {
                var elementParameter = targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "element");
                if (elementParameter == null ||
                    !TryGetStandardSequenceSource(invocation, operation, out var augmentedSource) ||
                    !TryGetInvocationArgument(operation, elementParameter, out var element))
                    return false;
                var augmentedSourceFacts = GetArrayDimensionVectorFacts(
                    augmentedSource,
                    [.. visited],
                    deferredUse);
                var elementIsNonPositive = DefinitelyCapsSequenceAtZero(element);
                var definitelyContainsNonPositive =
                    augmentedSourceFacts.DefinitelyContainsNonPositive ||
                    elementIsNonPositive;
                var definitelyAllNonPositive =
                    augmentedSourceFacts.DefinitelyAllNonPositive &&
                    elementIsNonPositive;
                facts = new(
                    false,
                    definitelyContainsNonPositive,
                    definitelyContainsNonPositive || definitelyAllNonPositive,
                    definitelyAllNonPositive);
                return facts != default;
            }
            var preservesEveryElement = definition.Name is
                    nameof(Enumerable.AsEnumerable) or
                    nameof(Enumerable.OrderBy) or
                    nameof(Enumerable.OrderByDescending) or
                    nameof(Enumerable.Reverse) or
                    nameof(Enumerable.ThenBy) or
                    nameof(Enumerable.ThenByDescending) or
                    nameof(Enumerable.ToArray) or
                    nameof(Enumerable.ToList);
            var producesSubset = definition.Name is
                    nameof(Enumerable.Distinct) or
                    nameof(Enumerable.Except) or
                    nameof(Enumerable.Intersect) or
                    nameof(Enumerable.Skip) or
                    "SkipLast" or
                    nameof(Enumerable.SkipWhile) or
                    nameof(Enumerable.Take) or
                    "TakeLast" or
                    nameof(Enumerable.TakeWhile) or
                    nameof(Enumerable.Where);
            if (!preservesEveryElement &&
                !producesSubset ||
                !TryGetStandardSequenceSource(invocation, operation, out var source))
                return false;
            var sourceUse = definition.Name is
                    nameof(Enumerable.ToArray) or
                    nameof(Enumerable.ToList)
                ? dimensions
                : deferredUse;
            var sourceFacts = GetArrayDimensionVectorFacts(
                source,
                visited,
                sourceUse);
            if (preservesEveryElement) {
                if (sourceFacts == default)
                    return false;
                facts = sourceFacts;
                return true;
            }
            var predicateEstablishesNonPositive =
                (definition.Name is nameof(Enumerable.Where) or
                    nameof(Enumerable.TakeWhile)) &&
                targetMethod.Parameters.FirstOrDefault(parameter =>
                    parameter.Name == "predicate") is { } predicateParameter &&
                TryGetInvocationArgument(
                    operation,
                    predicateParameter,
                    out var predicate) &&
                PredicateTruthGuaranteesNonPositiveElement(predicate);
            if (!sourceFacts.DefinitelyAllNonPositive &&
                !predicateEstablishesNonPositive)
                return false;
            facts = new(
                sourceFacts.DefinitelyEmpty,
                false,
                true,
                true);
            return true;
        }
        private bool PredicateTruthGuaranteesNonPositiveElement(
            ExpressionSyntax predicate) =>
            PredicateTruthGuaranteesNonPositiveElement(
                predicate,
                [],
                new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        private bool PredicateTruthGuaranteesNonPositiveElement(
            ExpressionSyntax predicate,
            HashSet<SyntaxNode> visited,
            HashSet<ISymbol> callPath) {
            predicate = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(predicate);
            if (!visited.Add(predicate))
                return false;
            var predicateSymbol = semanticModel.GetSymbolInfo(
                predicate,
                cancellationToken).Symbol?.OriginalDefinition;
            if (predicateSymbol is ILocalSymbol or IParameterSymbol &&
                SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                    predicateSymbol,
                    predicate,
                    semanticModel,
                    cancellationToken,
                    out var resolvedPredicate))
                return PredicateTruthGuaranteesNonPositiveElement(
                    resolvedPredicate,
                    visited,
                    callPath);
            var parameter = predicate switch {
                SimpleLambdaExpressionSyntax simple =>
                    semanticModel.GetDeclaredSymbol(simple.Parameter, cancellationToken),
                ParenthesizedLambdaExpressionSyntax parenthesized =>
                    parenthesized.ParameterList.Parameters.FirstOrDefault() is { } syntax
                        ? semanticModel.GetDeclaredSymbol(syntax, cancellationToken)
                        : null,
                AnonymousMethodExpressionSyntax anonymous =>
                    anonymous.ParameterList?.Parameters.FirstOrDefault() is { } syntax
                        ? semanticModel.GetDeclaredSymbol(syntax, cancellationToken)
                        : null,
                _ => null
            };
            if (parameter is {
                Type.SpecialType: SpecialType.System_Int32
            }) {
                var body = predicate switch {
                    LambdaExpressionSyntax lambda => lambda.Body,
                    AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
                    _ => null
                };
                return body != null &&
                       PredicateBodyTruthGuaranteesNonPositiveElement(
                           body,
                           parameter,
                           semanticModel,
                           callPath);
            }
            return semanticModel.GetSymbolInfo(predicate, cancellationToken).Symbol is
                       IMethodSymbol method &&
                   method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                   method.Parameters.FirstOrDefault() is {
                       Type.SpecialType: SpecialType.System_Int32
                   } &&
                   method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction &&
                   IsStaticallyDispatchedBoundSource(method) &&
                   SourcePredicateTruthGuaranteesNonPositiveElement(method, callPath);
        }
        private bool SourcePredicateTruthGuaranteesNonPositiveElement(
            IMethodSymbol method,
            HashSet<ISymbol> callPath) {
            var nextCallPath = new HashSet<ISymbol>(
                callPath,
                SymbolEqualityComparer.Default);
            if (!nextCallPath.Add(method.OriginalDefinition))
                return false;
            var foundBody = false;
            foreach (var syntaxReference in method.DeclaringSyntaxReferences) {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var model = semanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var parameter = declaration switch {
                    MethodDeclarationSyntax methodDeclaration =>
                        methodDeclaration.ParameterList.Parameters.FirstOrDefault() is { } syntax
                            ? model.GetDeclaredSymbol(syntax, cancellationToken)
                            : null,
                    LocalFunctionStatementSyntax localFunction =>
                        localFunction.ParameterList.Parameters.FirstOrDefault() is { } syntax
                            ? model.GetDeclaredSymbol(syntax, cancellationToken)
                            : null,
                    _ => null
                };
                if (parameter is not {
                    Type.SpecialType: SpecialType.System_Int32
                })
                    return false;
                SyntaxNode? body = declaration switch {
                    MethodDeclarationSyntax {
                        ExpressionBody.Expression: { } expression
                    } => expression,
                    LocalFunctionStatementSyntax {
                        ExpressionBody.Expression: { } expression
                    } => expression,
                    MethodDeclarationSyntax { Body: { } block } => block,
                    LocalFunctionStatementSyntax { Body: { } block } => block,
                    _ => null
                };
                if (body == null)
                    continue;
                foundBody = true;
                if (!PredicateBodyTruthGuaranteesNonPositiveElement(
                        body,
                        parameter,
                        model,
                        nextCallPath))
                    return false;
            }
            return foundBody;
        }
        private bool PredicateBodyTruthGuaranteesNonPositiveElement(
            SyntaxNode body,
            IParameterSymbol parameter,
            SemanticModel model,
            HashSet<ISymbol> callPath) {
            if (SymbolicMutationInventory.Create(
                    body,
                    model,
                    cancellationToken).MutatesSymbol(parameter))
                return false;
            if (body is ExpressionSyntax expression)
                return model.GetOperation(expression, cancellationToken) is { } operation &&
                       BooleanResultGuaranteesParameterNonPositive(
                           operation,
                           true,
                           parameter,
                           model,
                           callPath);
            if (body is not BlockSyntax block)
                return false;
            var returns = block.DescendantNodes(node =>
                    node is not AnonymousFunctionExpressionSyntax and
                        not LocalFunctionStatementSyntax)
                .OfType<ReturnStatementSyntax>()
                .ToArray();
            return returns.Length != 0 &&
                   returns.All(statement =>
                       statement.Expression != null &&
                       model.GetOperation(
                           statement.Expression,
                           cancellationToken) is { } operation &&
                       BooleanResultGuaranteesParameterNonPositive(
                           operation,
                           true,
                           parameter,
                           model,
                           callPath));
        }
        private bool BooleanResultGuaranteesParameterNonPositive(
            IOperation operation,
            bool result,
            IParameterSymbol parameter,
            SemanticModel model,
            HashSet<ISymbol> callPath) {
            while (operation is IConversionOperation {
                Conversion.IsIdentity: true
            } conversion)
                operation = conversion.Operand;
            if (operation is IParenthesizedOperation parenthesized)
                return BooleanResultGuaranteesParameterNonPositive(
                    parenthesized.Operand,
                    result,
                    parameter,
                    model,
                    callPath);
            if (operation.ConstantValue is {
                HasValue: true,
                Value: bool constant
            })
                return constant != result;
            if (operation is IThrowOperation or
                IConversionOperation { Operand: IThrowOperation })
                return true;
            if (operation is IUnaryOperation {
                OperatorKind: UnaryOperatorKind.Not,
                OperatorMethod: null
            } unary)
                return BooleanResultGuaranteesParameterNonPositive(
                    unary.Operand,
                    !result,
                    parameter,
                    model,
                    callPath);
            if (operation is IConditionalOperation {
                WhenTrue: { } whenTrue,
                WhenFalse: { } whenFalse
            })
                return BooleanResultGuaranteesParameterNonPositive(
                           whenTrue,
                           result,
                           parameter,
                           model,
                           callPath) &&
                       BooleanResultGuaranteesParameterNonPositive(
                           whenFalse,
                           result,
                           parameter,
                           model,
                           callPath);
            if (operation is not IBinaryOperation {
                OperatorMethod: null
            } binary)
                return false;
            if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or
                    BinaryOperatorKind.And)
                return result
                    ? BooleanResultGuaranteesParameterNonPositive(
                          binary.LeftOperand, true, parameter, model, callPath) ||
                      BooleanResultGuaranteesParameterNonPositive(
                          binary.RightOperand, true, parameter, model, callPath)
                    : BooleanResultGuaranteesParameterNonPositive(
                          binary.LeftOperand, false, parameter, model, callPath) &&
                      BooleanResultGuaranteesParameterNonPositive(
                          binary.RightOperand, false, parameter, model, callPath);
            if (binary.OperatorKind is BinaryOperatorKind.ConditionalOr or
                    BinaryOperatorKind.Or)
                return result
                    ? BooleanResultGuaranteesParameterNonPositive(
                          binary.LeftOperand, true, parameter, model, callPath) &&
                      BooleanResultGuaranteesParameterNonPositive(
                          binary.RightOperand, true, parameter, model, callPath)
                    : BooleanResultGuaranteesParameterNonPositive(
                          binary.LeftOperand, false, parameter, model, callPath) ||
                      BooleanResultGuaranteesParameterNonPositive(
                          binary.RightOperand, false, parameter, model, callPath);
            var comparison = result
                ? binary.OperatorKind
                : binary.OperatorKind switch {
                    BinaryOperatorKind.LessThan =>
                        BinaryOperatorKind.GreaterThanOrEqual,
                    BinaryOperatorKind.LessThanOrEqual =>
                        BinaryOperatorKind.GreaterThan,
                    BinaryOperatorKind.GreaterThan =>
                        BinaryOperatorKind.LessThanOrEqual,
                    BinaryOperatorKind.GreaterThanOrEqual =>
                        BinaryOperatorKind.LessThan,
                    BinaryOperatorKind.Equals =>
                        BinaryOperatorKind.NotEquals,
                    BinaryOperatorKind.NotEquals =>
                        BinaryOperatorKind.Equals,
                    _ => binary.OperatorKind
                };
            var leftIsParameter = IsDirectParameterReference(
                binary.LeftOperand,
                parameter);
            var rightIsParameter = IsDirectParameterReference(
                binary.RightOperand,
                parameter);
            return comparison switch {
                BinaryOperatorKind.LessThan when leftIsParameter =>
                    OperationDefinitelyProducesAtMostOne(
                        binary.RightOperand,
                        model,
                        callPath),
                BinaryOperatorKind.LessThanOrEqual when leftIsParameter =>
                    OperationDefinitelyProducesNonPositiveInt32(
                        binary.RightOperand,
                        model,
                        callPath),
                BinaryOperatorKind.GreaterThan when rightIsParameter =>
                    OperationDefinitelyProducesAtMostOne(
                        binary.LeftOperand,
                        model,
                        callPath),
                BinaryOperatorKind.GreaterThanOrEqual when rightIsParameter =>
                    OperationDefinitelyProducesNonPositiveInt32(
                        binary.LeftOperand,
                        model,
                        callPath),
                BinaryOperatorKind.Equals when leftIsParameter =>
                    OperationDefinitelyProducesNonPositiveInt32(
                        binary.RightOperand,
                        model,
                        callPath),
                BinaryOperatorKind.Equals when rightIsParameter =>
                    OperationDefinitelyProducesNonPositiveInt32(
                        binary.LeftOperand,
                        model,
                        callPath),
                _ => false
            };
        }
        private static bool IsDirectParameterReference(
            IOperation operation,
            IParameterSymbol parameter) {
            while (operation is IConversionOperation {
                Conversion.IsIdentity: true
            } conversion)
                operation = conversion.Operand;
            while (operation is IParenthesizedOperation parenthesized)
                operation = parenthesized.Operand;
            return operation is IParameterReferenceOperation reference &&
                   SymbolEqualityComparer.Default.Equals(
                       reference.Parameter.OriginalDefinition,
                       parameter.OriginalDefinition);
        }
        private bool OperationDefinitelyProducesAtMostOne(
            IOperation operation,
            SemanticModel model,
            HashSet<ISymbol> callPath) =>
            operation.ConstantValue is {
                HasValue: true,
                Value: int constant
            } && constant <= 1 ||
            OperationDefinitelyProducesNonPositiveInt32(
                operation,
                model,
                callPath);
        private bool IsNonPositiveDimensionSelector(
            ExpressionSyntax selector,
            bool sourceElementsAreNonPositive) =>
            IsNonPositiveDimensionSelector(
                selector,
                sourceElementsAreNonPositive,
                []);
        private bool IsNonPositiveDimensionSelector(
            ExpressionSyntax selector,
            bool sourceElementsAreNonPositive,
            HashSet<SyntaxNode> visited) {
            selector = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(selector);
            if (!visited.Add(selector))
                return false;
            var selectorSymbol = semanticModel.GetSymbolInfo(
                selector,
                cancellationToken).Symbol?.OriginalDefinition;
            if (selectorSymbol is ILocalSymbol or IParameterSymbol &&
                SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                    selectorSymbol,
                    selector,
                    semanticModel,
                    cancellationToken,
                    out var resolvedSelector))
                return IsNonPositiveDimensionSelector(
                    resolvedSelector,
                    sourceElementsAreNonPositive,
                    visited);
            var callPath = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var parameterFacts = CreateSelectorParameterBoundFacts(
                selector,
                sourceElementsAreNonPositive);
            parameterBoundScopes.Push(parameterFacts);
            try {
                if (selector is LambdaExpressionSyntax { Body: ExpressionSyntax projectedValue })
                    return ExpressionDefinitelyProducesNonPositiveInt32(
                        projectedValue,
                        semanticModel,
                        callPath);
                if (selector is LambdaExpressionSyntax { Body: BlockSyntax lambdaBody })
                    return BlockReturnsOnlyNonPositiveValues(
                        lambdaBody,
                        semanticModel,
                        callPath);
                if (selector is AnonymousMethodExpressionSyntax { Block: { } anonymousBody })
                    return BlockReturnsOnlyNonPositiveValues(
                        anonymousBody,
                        semanticModel,
                        callPath);
                return semanticModel.GetSymbolInfo(selector, cancellationToken).Symbol is
                           IMethodSymbol {
                               ReturnType.SpecialType: SpecialType.System_Int32,
                               MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction
                           } method &&
                       IsStaticallyDispatchedBoundSource(method) &&
                       SourceMethodReturnsOnlyNonPositiveValues(method, callPath);
            }
            finally {
                parameterBoundScopes.Pop();
            }
        }
        private Dictionary<IParameterSymbol, Int32BoundFacts> CreateSelectorParameterBoundFacts(
            ExpressionSyntax selector,
            bool sourceElementsAreNonPositive) {
            var facts = new Dictionary<IParameterSymbol, Int32BoundFacts>(
                SymbolEqualityComparer.Default);
            IEnumerable<IParameterSymbol> parameters = selector switch {
                SimpleLambdaExpressionSyntax simple =>
                    GetDeclaredParameters([simple.Parameter]),
                ParenthesizedLambdaExpressionSyntax parenthesized =>
                    GetDeclaredParameters(parenthesized.ParameterList.Parameters),
                AnonymousMethodExpressionSyntax {
                    ParameterList: { } parameterList
                } =>
                    GetDeclaredParameters(parameterList.Parameters),
                _ => semanticModel.GetSymbolInfo(selector, cancellationToken).Symbol is
                    IMethodSymbol method
                        ? method.Parameters
                        : []
            };
            var parameterArray = parameters.ToArray();
            if (sourceElementsAreNonPositive &&
                parameterArray.FirstOrDefault() is {
                    Type.SpecialType: SpecialType.System_Int32
                } element)
                facts[element.OriginalDefinition] = Int32BoundFacts.NonPositive;
            if (parameterArray.ElementAtOrDefault(1) is {
                Type.SpecialType: SpecialType.System_Int32
            } index)
                facts[index.OriginalDefinition] = Int32BoundFacts.NonNegative;
            return facts;
        }
        private IEnumerable<IParameterSymbol> GetDeclaredParameters(
            IEnumerable<ParameterSyntax> parameters) {
            foreach (var parameter in parameters)
                if (semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is
                    IParameterSymbol symbol)
                    yield return symbol;
        }
        private static bool IsStaticallyDispatchedBoundSource(IMethodSymbol method) =>
            method.MethodKind == MethodKind.LocalFunction ||
            method.IsStatic ||
            !method.IsAbstract &&
            (!method.IsVirtual &&
             !method.IsOverride ||
             method.IsSealed ||
             method.ContainingType.IsSealed);
        private enum Int32Bound {
            NonPositive,
            NonNegative
        }
        [Flags]
        private enum Int32BoundFacts {
            None = 0,
            NonPositive = 1,
            NonNegative = 2
        }
        private bool SourceMethodReturnsOnlyNonPositiveValues(
            IMethodSymbol method,
            HashSet<ISymbol> callPath) =>
            SourceMethodReturnsOnlyBoundValues(
                method,
                callPath,
                Int32Bound.NonPositive);
        private bool SourceMethodReturnsOnlyBoundValues(
            IMethodSymbol method,
            HashSet<ISymbol> callPath,
            Int32Bound bound,
            IInvocationOperation? invocation = null,
            SemanticModel? invocationModel = null) {
            Dictionary<IParameterSymbol, Int32BoundFacts>? parameterFacts = null;
            if (invocation != null && invocationModel != null) {
                parameterFacts = CreateArgumentParameterBoundFacts(
                    invocation.Arguments,
                    invocationModel,
                    callPath);
                parameterBoundScopes.Push(parameterFacts);
            }
            try {
                return SourceMethodReturnsOnlyBoundValuesCore(
                    method,
                    callPath,
                    bound);
            }
            finally {
                if (parameterFacts != null)
                    parameterBoundScopes.Pop();
            }
        }
        private bool SourceMethodReturnsOnlyBoundValuesCore(
            IMethodSymbol method,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            var nextCallPath = new HashSet<ISymbol>(callPath, SymbolEqualityComparer.Default);
            if (!nextCallPath.Add(method.OriginalDefinition))
                return false;
            var foundBody = false;
            foreach (var syntaxReference in method.DeclaringSyntaxReferences) {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var model = semanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var expressionBody = declaration switch {
                    MethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
                    LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
                    _ => null
                };
                if (expressionBody != null) {
                    foundBody = true;
                    if (!ExpressionDefinitelyProducesInt32Bound(
                            expressionBody,
                            model,
                            nextCallPath,
                            bound))
                        return false;
                    continue;
                }
                var body = declaration switch {
                    MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body,
                    LocalFunctionStatementSyntax localFunction => localFunction.Body,
                    _ => null
                };
                if (body == null)
                    continue;
                foundBody = true;
                if (!BlockReturnsOnlyInt32BoundValues(
                        body,
                        model,
                        nextCallPath,
                        bound))
                    return false;
            }
            return foundBody;
        }
        private Dictionary<IParameterSymbol, Int32BoundFacts> CreateArgumentParameterBoundFacts(
            ImmutableArray<IArgumentOperation> arguments,
            SemanticModel model,
            HashSet<ISymbol> callPath) {
            var facts = new Dictionary<IParameterSymbol, Int32BoundFacts>(
                SymbolEqualityComparer.Default);
            foreach (var argument in arguments) {
                if (argument.Parameter is not {
                    RefKind: RefKind.None,
                    Type.SpecialType: SpecialType.System_Int32
                } parameter)
                    continue;
                var argumentFacts = Int32BoundFacts.None;
                if (OperationDefinitelyProducesInt32Bound(
                        argument.Value,
                        model,
                        callPath,
                        Int32Bound.NonPositive))
                    argumentFacts |= Int32BoundFacts.NonPositive;
                if (OperationDefinitelyProducesInt32Bound(
                        argument.Value,
                        model,
                        callPath,
                        Int32Bound.NonNegative))
                    argumentFacts |= Int32BoundFacts.NonNegative;
                if (argumentFacts != Int32BoundFacts.None)
                    facts[parameter.OriginalDefinition] = argumentFacts;
            }
            return facts;
        }
        private bool BlockReturnsOnlyNonPositiveValues(
            BlockSyntax body,
            SemanticModel model,
            HashSet<ISymbol> callPath) =>
            BlockReturnsOnlyInt32BoundValues(
                body,
                model,
                callPath,
                Int32Bound.NonPositive);
        private bool BlockReturnsOnlyInt32BoundValues(
            BlockSyntax body,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            var returns = body.DescendantNodes(node =>
                    node is not AnonymousFunctionExpressionSyntax and
                        not LocalFunctionStatementSyntax)
                .OfType<ReturnStatementSyntax>()
                .ToArray();
            return returns.Length != 0 &&
                   returns.All(statement =>
                       statement.Expression != null &&
                       ExpressionDefinitelyProducesInt32Bound(
                           statement.Expression,
                           model,
                           callPath,
                           bound));
        }
        private bool ExpressionDefinitelyProducesNonPositiveInt32(
            ExpressionSyntax expression,
            SemanticModel model,
            HashSet<ISymbol> callPath) =>
            ExpressionDefinitelyProducesInt32Bound(
                expression,
                model,
                callPath,
                Int32Bound.NonPositive);
        private bool ExpressionDefinitelyProducesInt32Bound(
            ExpressionSyntax expression,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
            while (expression is CheckedExpressionSyntax checkedExpression)
                expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(
                    checkedExpression.Expression);
            if (model.GetConstantValue(expression, cancellationToken) is {
                HasValue: true,
                Value: int constant
            })
                return Int32ConstantSatisfiesBound(constant, bound);
            var operation = model.GetOperation(expression, cancellationToken);
            return operation != null &&
                   OperationDefinitelyProducesInt32Bound(
                       operation,
                       model,
                       callPath,
                       bound);
        }
        private bool OperationDefinitelyProducesNonPositiveInt32(
            IOperation operation,
            SemanticModel model,
            HashSet<ISymbol> callPath) =>
            OperationDefinitelyProducesInt32Bound(
                operation,
                model,
                callPath,
                Int32Bound.NonPositive);
        private bool OperationDefinitelyProducesInt32Bound(
            IOperation operation,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            while (operation is IConversionOperation {
                Conversion.IsIdentity: true
            } conversion)
                operation = conversion.Operand;
            if (operation.ConstantValue is { HasValue: true } constant &&
                IntegralConstantSatisfiesBound(constant.Value, bound))
                return true;
            if (bound == Int32Bound.NonNegative &&
                IsUnsignedIntegralType(operation.Type))
                return true;
            return operation switch {
                IConversionOperation { Operand: IThrowOperation } => true,
                IConversionOperation conversion
                    when BuiltInIntegralConversionDefinitelyProducesInt32Bound(
                        conversion,
                        model,
                        callPath,
                        bound) =>
                    true,
                IParenthesizedOperation parenthesized =>
                    OperationDefinitelyProducesInt32Bound(
                        parenthesized.Operand,
                        model,
                        callPath,
                        bound),
                IConditionalOperation {
                    WhenTrue: { } whenTrue,
                    WhenFalse: { } whenFalse
                } =>
                    OperationDefinitelyProducesInt32Bound(
                        whenTrue, model, callPath, bound) &&
                    OperationDefinitelyProducesInt32Bound(
                        whenFalse, model, callPath, bound),
                ISwitchExpressionOperation { Arms.Length: > 0 } switchExpression =>
                    switchExpression.Arms.All(arm =>
                        OperationDefinitelyProducesInt32Bound(
                            arm.Value, model, callPath, bound)),
                IThrowOperation => true,
                IBinaryOperation binary
                    when BuiltInBinaryOperationDefinitelyProducesInt32Bound(
                        binary,
                        model,
                        callPath,
                        bound) =>
                    true,
                IUnaryOperation {
                    OperatorKind: UnaryOperatorKind.Plus,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Int32,
                    Operand.Type.SpecialType: SpecialType.System_Int32
                } unary =>
                    OperationDefinitelyProducesInt32Bound(
                        unary.Operand, model, callPath, bound),
                IUnaryOperation {
                    OperatorKind: UnaryOperatorKind.Minus,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Int32,
                    Operand.Type.SpecialType: SpecialType.System_Int32
                } unary
                    when NegatedOperationDefinitelyProducesInt32Bound(
                        unary.Operand,
                        unary.IsChecked,
                        model,
                        callPath,
                        bound) =>
                    true,
                IUnaryOperation {
                    OperatorKind: UnaryOperatorKind.BitwiseNegation,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Int32,
                    Operand.Type.SpecialType: SpecialType.System_Int32
                } unary
                    when bound == Int32Bound.NonPositive &&
                         OperationDefinitelyProducesInt32Bound(
                             unary.Operand,
                             model,
                             callPath,
                             Int32Bound.NonNegative) =>
                    true,
                ILocalReferenceOperation {
                    Local.Type.SpecialType: SpecialType.System_Int32
                } local =>
                    LocalReferenceDefinitelyProducesInt32Bound(
                        local, model, callPath, bound),
                IParameterReferenceOperation {
                    Parameter.Type.SpecialType: SpecialType.System_Int32
                } parameter =>
                    ParameterReferenceDefinitelyProducesInt32Bound(
                        parameter,
                        model,
                        callPath,
                        bound),
                IFieldReferenceOperation tupleElement
                    when TupleLiteralElementDefinitelyProducesInt32Bound(
                        tupleElement,
                        model,
                        callPath,
                        bound) =>
                    true,
                IArrayElementReferenceOperation arrayElement
                    when FreshArrayElementDefinitelyProducesInt32Bound(
                        arrayElement,
                        model,
                        callPath,
                        bound) =>
                    true,
                IPropertyReferenceOperation {
                    Property.Type.SpecialType: SpecialType.System_Int32
                } property
                    when SourcePropertyReturnsOnlyBoundValues(
                        property,
                        model,
                        callPath,
                        bound) =>
                    true,
                IInvocationOperation math
                    when KnownMathInvocationDefinitelyProducesInt32Bound(
                        math, model, callPath, bound) =>
                    true,
                IInvocationOperation {
                    TargetMethod: {
                        ReturnType.SpecialType: SpecialType.System_Int32,
                        MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction
                    } targetMethod
                } invocation when IsStaticallyDispatchedBoundSource(targetMethod) =>
                    SourceMethodReturnsOnlyBoundValues(
                        targetMethod,
                        callPath,
                        bound,
                        invocation,
                        model),
                _ => false
            };
        }
        private bool TupleLiteralElementDefinitelyProducesInt32Bound(
            IFieldReferenceOperation reference,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (reference.Instance is not { } instance ||
                reference.Field.ContainingType is not {
                    IsTupleType: true
                } tupleType)
                return false;
            var elements = tupleType.TupleElements;
            var elementIndex = -1;
            for (var index = 0; index < elements.Length; index++) {
                if (SymbolEqualityComparer.Default.Equals(
                        elements[index],
                        reference.Field) ||
                    SymbolEqualityComparer.Default.Equals(
                        elements[index].CorrespondingTupleField,
                        reference.Field.CorrespondingTupleField)) {
                    elementIndex = index;
                    break;
                }
            }
            if (elementIndex < 0)
                return false;
            while (instance is IConversionOperation {
                Conversion.IsIdentity: true
            } conversion)
                instance = conversion.Operand;
            while (instance is IParenthesizedOperation parenthesized)
                instance = parenthesized.Operand;
            return instance is ITupleOperation tuple &&
                   elementIndex < tuple.Elements.Length &&
                   OperationDefinitelyProducesInt32Bound(
                       tuple.Elements[elementIndex],
                       model,
                       callPath,
                       bound);
        }
        private bool FreshArrayElementDefinitelyProducesInt32Bound(
            IArrayElementReferenceOperation reference,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (reference.ArrayReference is not IArrayCreationOperation {
                Type: IArrayTypeSymbol {
                    Rank: 1,
                    ElementType.SpecialType: SpecialType.System_Int32
                },
                Initializer: var initializer
            } ||
                reference.Indices.Length != 1)
                return false;
            if (initializer == null)
                return true;
            var values = initializer.ElementValues;
            if (TryGetInt32Constant(reference.Indices[0], out var index))
                return index < 0 ||
                       index >= values.Length ||
                       OperationDefinitelyProducesInt32Bound(
                           values[index],
                           model,
                           callPath,
                           bound);
            return values.All(value =>
                OperationDefinitelyProducesInt32Bound(
                    value,
                    model,
                    callPath,
                    bound));
        }
        private bool SourcePropertyReturnsOnlyBoundValues(
            IPropertyReferenceOperation reference,
            SemanticModel invocationModel,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            var parameterFacts = CreateArgumentParameterBoundFacts(
                reference.Arguments,
                invocationModel,
                callPath);
            foreach (var argument in reference.Arguments) {
                if (argument.Parameter is not { } parameter ||
                    !parameterFacts.TryGetValue(
                        parameter.OriginalDefinition,
                        out var facts))
                    continue;
                var ordinal = parameter.Ordinal;
                if (ordinal < reference.Property.Parameters.Length)
                    parameterFacts[
                        reference.Property.Parameters[ordinal].OriginalDefinition] = facts;
                if (reference.Property.GetMethod is { } getter &&
                    ordinal < getter.Parameters.Length)
                    parameterFacts[
                        getter.Parameters[ordinal].OriginalDefinition] = facts;
            }
            parameterBoundScopes.Push(parameterFacts);
            try {
                return SourcePropertyReturnsOnlyBoundValuesCore(
                    reference.Property,
                    callPath,
                    bound);
            }
            finally {
                parameterBoundScopes.Pop();
            }
        }
        private bool SourcePropertyReturnsOnlyBoundValuesCore(
            IPropertySymbol property,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (property.GetMethod is not { } getter ||
                !IsStaticallyDispatchedBoundSource(getter))
                return false;
            var nextCallPath = new HashSet<ISymbol>(callPath, SymbolEqualityComparer.Default);
            if (!nextCallPath.Add(getter.OriginalDefinition))
                return false;
            var foundBody = false;
            foreach (var syntaxReference in property.DeclaringSyntaxReferences) {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var model = semanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var expressionBody = declaration switch {
                    PropertyDeclarationSyntax {
                        ExpressionBody.Expression: { } expression
                    } => expression,
                    IndexerDeclarationSyntax {
                        ExpressionBody.Expression: { } expression
                    } => expression,
                    _ => null
                };
                if (expressionBody != null) {
                    foundBody = true;
                    if (!ExpressionDefinitelyProducesInt32Bound(
                            expressionBody,
                            model,
                            nextCallPath,
                            bound))
                        return false;
                    continue;
                }
                var accessorList = declaration switch {
                    PropertyDeclarationSyntax propertyDeclaration =>
                        propertyDeclaration.AccessorList,
                    IndexerDeclarationSyntax indexerDeclaration =>
                        indexerDeclaration.AccessorList,
                    _ => null
                };
                var getterDeclaration = accessorList?.Accessors.FirstOrDefault(accessor =>
                    accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getterDeclaration?.ExpressionBody?.Expression is { } getterExpression) {
                    foundBody = true;
                    if (!ExpressionDefinitelyProducesInt32Bound(
                            getterExpression,
                            model,
                            nextCallPath,
                            bound))
                        return false;
                    continue;
                }
                if (getterDeclaration?.Body is not { } getterBody)
                    continue;
                foundBody = true;
                if (!BlockReturnsOnlyInt32BoundValues(
                        getterBody,
                        model,
                        nextCallPath,
                        bound))
                    return false;
            }
            return foundBody;
        }
        private bool BuiltInIntegralConversionDefinitelyProducesInt32Bound(
            IConversionOperation operation,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (operation.OperatorMethod != null ||
                !IsBuiltInIntegralType(operation.Type) ||
                !IsBuiltInIntegralType(operation.Operand.Type) ||
                !operation.Conversion.IsNumeric)
                return false;
            return (operation.IsChecked ||
                    IntegralConversionPreservesSign(
                        operation.Operand.Type!.SpecialType,
                        operation.Type!.SpecialType)) &&
                   OperationDefinitelyProducesInt32Bound(
                       operation.Operand,
                       model,
                       callPath,
                       bound);
        }
        private static bool IntegralConversionPreservesSign(
            SpecialType source,
            SpecialType target) =>
            source == target ||
            source == SpecialType.System_SByte &&
            target is SpecialType.System_Int16 or
                SpecialType.System_Int32 or
                SpecialType.System_Int64 ||
            source == SpecialType.System_Byte &&
            target is SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 ||
            source == SpecialType.System_Int16 &&
            target is SpecialType.System_Int32 or
                SpecialType.System_Int64 ||
            source is SpecialType.System_UInt16 or SpecialType.System_Char &&
            target is SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 ||
            source == SpecialType.System_Int32 &&
            target == SpecialType.System_Int64 ||
            source == SpecialType.System_UInt32 &&
            target is SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        private static bool IsBuiltInIntegralType(ITypeSymbol? type) =>
            type?.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Char or
                SpecialType.System_IntPtr or
                SpecialType.System_UIntPtr;
        private static bool IsUnsignedIntegralType(ITypeSymbol? type) =>
            type?.SpecialType is
                SpecialType.System_Byte or
                SpecialType.System_UInt16 or
                SpecialType.System_UInt32 or
                SpecialType.System_UInt64 or
                SpecialType.System_Char or
                SpecialType.System_UIntPtr;
        private static bool IntegralConstantSatisfiesBound(
            object? value,
            Int32Bound bound) =>
            value switch {
                sbyte signed => bound == Int32Bound.NonPositive
                    ? signed <= 0
                    : signed >= 0,
                byte unsigned => bound == Int32Bound.NonNegative || unsigned == 0,
                short signed => bound == Int32Bound.NonPositive
                    ? signed <= 0
                    : signed >= 0,
                ushort unsigned => bound == Int32Bound.NonNegative || unsigned == 0,
                int signed => Int32ConstantSatisfiesBound(signed, bound),
                uint unsigned => bound == Int32Bound.NonNegative || unsigned == 0,
                long signed => bound == Int32Bound.NonPositive
                    ? signed <= 0
                    : signed >= 0,
                ulong unsigned => bound == Int32Bound.NonNegative || unsigned == 0,
                char unsigned => bound == Int32Bound.NonNegative || unsigned == 0,
                _ => false
            };
        private bool BuiltInBinaryOperationDefinitelyProducesInt32Bound(
            IBinaryOperation operation,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (operation.OperatorMethod != null ||
                operation.Type?.SpecialType != SpecialType.System_Int32 ||
                operation.LeftOperand.Type?.SpecialType != SpecialType.System_Int32 ||
                operation.RightOperand.Type?.SpecialType != SpecialType.System_Int32)
                return false;
            var left = operation.LeftOperand;
            var right = operation.RightOperand;
            var leftIsZero = OperationHasInt32Constant(left, 0);
            var rightIsZero = OperationHasInt32Constant(right, 0);
            var leftIsOne = OperationHasInt32Constant(left, 1);
            var rightIsOne = OperationHasInt32Constant(right, 1);
            var leftIsNegativeOne = OperationHasInt32Constant(left, -1);
            var rightIsNegativeOne = OperationHasInt32Constant(right, -1);
            var leftIsMinimum = OperationHasInt32Constant(left, int.MinValue);
            var rightIsMinimum = OperationHasInt32Constant(right, int.MinValue);
            switch (operation.OperatorKind) {
                case BinaryOperatorKind.Add:
                    if (leftIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            right, model, callPath, bound);
                    if (rightIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    return operation.IsChecked &&
                           OperationDefinitelyProducesInt32Bound(
                               left, model, callPath, bound) &&
                           OperationDefinitelyProducesInt32Bound(
                               right, model, callPath, bound);
                case BinaryOperatorKind.Subtract:
                    if (rightIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    if (leftIsZero)
                        return NegatedOperationDefinitelyProducesInt32Bound(
                            right,
                            operation.IsChecked,
                            model,
                            callPath,
                            bound);
                    return operation.IsChecked &&
                           OperationDefinitelyProducesInt32Bound(
                               left, model, callPath, bound) &&
                           OperationDefinitelyProducesInt32Bound(
                               right,
                               model,
                               callPath,
                               OppositeInt32Bound(bound));
                case BinaryOperatorKind.Multiply:
                    if (leftIsZero || rightIsZero)
                        return true;
                    if (leftIsOne)
                        return OperationDefinitelyProducesInt32Bound(
                            right, model, callPath, bound);
                    if (rightIsOne)
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    if (leftIsNegativeOne)
                        return NegatedOperationDefinitelyProducesInt32Bound(
                            right,
                            operation.IsChecked,
                            model,
                            callPath,
                            bound);
                    if (rightIsNegativeOne)
                        return NegatedOperationDefinitelyProducesInt32Bound(
                            left,
                            operation.IsChecked,
                            model,
                            callPath,
                            bound);
                    if (!operation.IsChecked)
                        return false;
                    return BinarySignCombinationDefinitelyProducesInt32Bound(
                        left,
                        right,
                        model,
                        callPath,
                        bound);
                case BinaryOperatorKind.Divide:
                    if (leftIsZero)
                        return true;
                    if (rightIsOne)
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    if (rightIsNegativeOne)
                        return OperationDefinitelyProducesInt32Bound(
                            left,
                            model,
                            callPath,
                            OppositeInt32Bound(bound));
                    return BinarySignCombinationDefinitelyProducesInt32Bound(
                        left,
                        right,
                        model,
                        callPath,
                        bound);
                case BinaryOperatorKind.Remainder:
                    if (leftIsZero ||
                        rightIsOne ||
                        rightIsNegativeOne)
                        return true;
                    return OperationDefinitelyProducesInt32Bound(
                        left, model, callPath, bound);
                case BinaryOperatorKind.And:
                    if (leftIsZero || rightIsZero)
                        return true;
                    if (bound == Int32Bound.NonNegative)
                        return OperationDefinitelyProducesInt32Bound(
                                   left, model, callPath, bound) ||
                               OperationDefinitelyProducesInt32Bound(
                                   right, model, callPath, bound);
                    return OperationDefinitelyProducesInt32Bound(
                               left, model, callPath, bound) &&
                           OperationDefinitelyProducesInt32Bound(
                               right, model, callPath, bound);
                case BinaryOperatorKind.Or:
                    if (leftIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            right, model, callPath, bound);
                    if (rightIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    return OperationDefinitelyProducesInt32Bound(
                               left, model, callPath, bound) &&
                           OperationDefinitelyProducesInt32Bound(
                               right, model, callPath, bound);
                case BinaryOperatorKind.ExclusiveOr:
                    if (leftIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            right, model, callPath, bound);
                    if (rightIsZero)
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    if (bound == Int32Bound.NonNegative)
                        return OperationDefinitelyProducesInt32Bound(
                                   left, model, callPath, bound) &&
                               OperationDefinitelyProducesInt32Bound(
                                   right, model, callPath, bound);
                    if (leftIsNegativeOne || leftIsMinimum)
                        return OperationDefinitelyProducesInt32Bound(
                            right,
                            model,
                            callPath,
                            Int32Bound.NonNegative);
                    if (rightIsNegativeOne || rightIsMinimum)
                        return OperationDefinitelyProducesInt32Bound(
                            left,
                            model,
                            callPath,
                            Int32Bound.NonNegative);
                    return false;
                case BinaryOperatorKind.LeftShift:
                    if (leftIsZero)
                        return true;
                    return rightIsZero &&
                           OperationDefinitelyProducesInt32Bound(
                               left, model, callPath, bound);
                case BinaryOperatorKind.RightShift:
                    return OperationDefinitelyProducesInt32Bound(
                        left, model, callPath, bound);
                case BinaryOperatorKind.UnsignedRightShift:
                    if (TryGetInt32Constant(right, out var shift)) {
                        if ((shift & 31) != 0)
                            return bound == Int32Bound.NonNegative;
                        return OperationDefinitelyProducesInt32Bound(
                            left, model, callPath, bound);
                    }
                    return bound == Int32Bound.NonNegative &&
                           OperationDefinitelyProducesInt32Bound(
                               left, model, callPath, bound);
                default:
                    return false;
            }
        }
        private bool BinarySignCombinationDefinitelyProducesInt32Bound(
            IOperation left,
            IOperation right,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) =>
            OperationDefinitelyProducesInt32Bound(
                left, model, callPath, bound) &&
            OperationDefinitelyProducesInt32Bound(
                right, model, callPath, Int32Bound.NonNegative) ||
            OperationDefinitelyProducesInt32Bound(
                left, model, callPath, OppositeInt32Bound(bound)) &&
            OperationDefinitelyProducesInt32Bound(
                right, model, callPath, Int32Bound.NonPositive);
        private bool NegatedOperationDefinitelyProducesInt32Bound(
            IOperation operand,
            bool overflowThrows,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) =>
            (bound == Int32Bound.NonPositive || overflowThrows) &&
            OperationDefinitelyProducesInt32Bound(
                operand,
                model,
                callPath,
                OppositeInt32Bound(bound));
        private static Int32Bound OppositeInt32Bound(Int32Bound bound) =>
            bound == Int32Bound.NonPositive
                ? Int32Bound.NonNegative
                : Int32Bound.NonPositive;
        private static bool OperationHasInt32Constant(
            IOperation operation,
            int expected) =>
            TryGetInt32Constant(operation, out var value) &&
            value == expected;
        private static bool TryGetInt32Constant(
            IOperation operation,
            out int value) {
            if (operation.ConstantValue is {
                HasValue: true,
                Value: int constant
            }) {
                value = constant;
                return true;
            }
            value = default;
            return false;
        }
        private static bool Int32ConstantSatisfiesBound(
            int constant,
            Int32Bound bound) =>
            bound == Int32Bound.NonPositive
                ? constant <= 0
                : constant >= 0;
        private bool KnownMathInvocationDefinitelyProducesInt32Bound(
            IInvocationOperation invocation,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            var method = invocation.TargetMethod.OriginalDefinition;
            if (method.ReturnType.SpecialType != SpecialType.System_Int32 ||
                method.Parameters.Any(parameter =>
                    parameter.Type.SpecialType != SpecialType.System_Int32) ||
                method.ContainingType is not { } containingType ||
                !IsCoreLibraryType(containingType) ||
                containingType.ContainingNamespace.ToDisplayString() != "System" ||
                containingType.Name != "Math")
                return false;
            if (method.Name == nameof(Math.Abs))
                return bound == Int32Bound.NonNegative &&
                       invocation.Arguments.Length == 1;
            if (method.Name == nameof(Math.Sign))
                return invocation.Arguments.Length == 1 &&
                       OperationDefinitelyProducesInt32Bound(
                           invocation.Arguments[0].Value,
                           model,
                           callPath,
                           bound);
            if (method.Name is nameof(Math.Min) or nameof(Math.Max)) {
                var values = invocation.Arguments
                    .Select(argument => argument.Value)
                    .ToArray();
                if (values.Length != 2)
                    return false;
                var oneBoundArgumentIsEnough =
                    method.Name == nameof(Math.Min) &&
                    bound == Int32Bound.NonPositive ||
                    method.Name == nameof(Math.Max) &&
                    bound == Int32Bound.NonNegative;
                return oneBoundArgumentIsEnough
                    ? values.Any(value =>
                        OperationDefinitelyProducesInt32Bound(
                            value, model, callPath, bound))
                    : values.All(value =>
                        OperationDefinitelyProducesInt32Bound(
                            value, model, callPath, bound));
            }
            if (method.Name != "Clamp")
                return false;
            var boundingParameter = bound == Int32Bound.NonPositive
                ? "max"
                : "min";
            var boundingArgument = invocation.Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Name == boundingParameter)?.Value;
            return boundingArgument != null &&
                   OperationDefinitelyProducesInt32Bound(
                       boundingArgument,
                       model,
                       callPath,
                       bound);
        }
        private bool ParameterReferenceDefinitelyProducesInt32Bound(
            IParameterReferenceOperation reference,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (reference.Syntax is not ExpressionSyntax use)
                return false;
            var parameter = reference.Parameter.OriginalDefinition;
            var nextCallPath = new HashSet<ISymbol>(callPath, SymbolEqualityComparer.Default);
            if (!nextCallPath.Add(parameter))
                return false;
            if (SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                    parameter,
                    use,
                    model,
                    cancellationToken,
                    true,
                    out var assignedValue))
                return ExpressionDefinitelyProducesInt32Bound(
                    assignedValue,
                    model,
                    nextCallPath,
                    bound);
            var root = CSharpSyntaxFacts.GetContainingExecutionRoot(use);
            if (root == null)
                return false;
            var mutations = SymbolicMutationInventory.Create(
                root,
                model,
                cancellationToken);
            if (mutations.MutatesBetween(
                    root.SpanStart - 1,
                    use.SpanStart,
                    parameter) ||
                root.DescendantNodes()
                    .Where(CSharpSyntaxFacts.IsNestedLocalCallableBoundary)
                    .Any(callable =>
                        SymbolicMutationInventory.Create(
                                callable,
                                model,
                                cancellationToken)
                            .MutatesSymbol(parameter)))
                return false;
            var requiredFact = bound == Int32Bound.NonPositive
                ? Int32BoundFacts.NonPositive
                : Int32BoundFacts.NonNegative;
            foreach (var scope in parameterBoundScopes)
                if (scope.TryGetValue(parameter, out var facts))
                    return (facts & requiredFact) != 0;
            return false;
        }
        private bool LocalReferenceDefinitelyProducesInt32Bound(
            ILocalReferenceOperation reference,
            SemanticModel model,
            HashSet<ISymbol> callPath,
            Int32Bound bound) {
            if (reference.Syntax is not ExpressionSyntax use ||
                reference.Local.DeclaringSyntaxReferences.FirstOrDefault() is not { } declarationReference)
                return false;
            var declaration = declarationReference.GetSyntax(cancellationToken);
            if (!ReferenceEquals(
                    CSharpSyntaxFacts.GetContainingExecutionRoot(declaration),
                    CSharpSyntaxFacts.GetContainingExecutionRoot(use)))
                return false;
            var local = reference.Local.OriginalDefinition;
            var nextCallPath = new HashSet<ISymbol>(callPath, SymbolEqualityComparer.Default);
            if (!nextCallPath.Add(local) ||
                !SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                    local,
                    use,
                    model,
                    cancellationToken,
                    true,
                    out var value))
                return false;
            return ExpressionDefinitelyProducesInt32Bound(
                value,
                model,
                nextCallPath,
                bound);
        }
        private bool DefinitelyProvidesPositiveDimensionCount(ExpressionSyntax count) {
            var visited = new HashSet<SyntaxNode>();
            while (true) {
                count = SymbolicConversionLowerer.UnwrapIdentityConversions(
                    count,
                    semanticModel,
                    cancellationToken);
                if (semanticModel.GetConstantValue(count, cancellationToken) is {
                    HasValue: true,
                    Value: int constant
                } &&
                    constant > 0)
                    return true;
                count = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(count);
                var symbol = semanticModel.GetSymbolInfo(count, cancellationToken).Symbol?.OriginalDefinition;
                if (symbol is not (ILocalSymbol or IParameterSymbol) ||
                    !visited.Add(count) ||
                    !SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                        symbol,
                        count,
                        semanticModel,
                        cancellationToken,
                        true,
                        out count))
                    return false;
            }
        }
        private bool IsKnownDefaultEmptyCoreSequenceValue(ExpressionSyntax collection) {
            if (semanticModel.GetOperation(collection, cancellationToken) is
                    IObjectCreationOperation {
                        Constructor.Parameters.Length: 0,
                        Type: INamedTypeSymbol constructedType
                    } creation &&
                creation.Initializer is not { Initializers.Length: > 0 })
                return IsKnownCoreSequenceType(constructedType);
            if (collection is not DefaultExpressionSyntax &&
                !collection.IsKind(SyntaxKind.DefaultLiteralExpression))
                return false;
            var typeInfo = semanticModel.GetTypeInfo(collection, cancellationToken);
            return (typeInfo.Type ?? typeInfo.ConvertedType) is INamedTypeSymbol defaultType &&
                   IsKnownCoreSequenceType(defaultType);
        }
        private bool IsKnownEmptyCollectionConstruction(ExpressionSyntax collection) {
            if (semanticModel.GetOperation(collection, cancellationToken) is not
                IObjectCreationOperation {
                    Constructor: { } constructor,
                    Type: INamedTypeSymbol type
                } creation ||
                creation.Initializer is { Initializers.Length: > 0 } ||
                !IsKnownMutableCollectionType(type))
                return false;
            foreach (var parameter in constructor.Parameters) {
                if (IsNonPopulatingCollectionConstructorParameter(parameter))
                    continue;
                if (!CopiesCollectionConstructorSources(type))
                    return false;
                var argument = creation.Arguments.FirstOrDefault(candidate =>
                    candidate.Parameter != null &&
                    SymbolEqualityComparer.Default.Equals(
                        candidate.Parameter.OriginalDefinition,
                        parameter.OriginalDefinition));
                if (argument?.Value.Syntax is not ExpressionSyntax source ||
                    !TryGetSequencePredicateSteps(
                        source,
                        out _,
                        out var sourceProducesNoElements) ||
                    !sourceProducesNoElements)
                    return false;
            }
            return true;
        }
        private static bool CopiesCollectionConstructorSources(INamedTypeSymbol candidate) {
            var type = candidate.OriginalDefinition;
            return (type.ContainingNamespace.ToDisplayString(), type.Name, type.Arity) is not
                ("System.Collections.ObjectModel", "Collection", 1);
        }
        private static bool IsKnownMutableCollectionType(INamedTypeSymbol candidate) {
            var type = candidate.OriginalDefinition;
            if (type.ContainingAssembly?.Name is not (
                    "System.Collections" or
                    "System.Collections.Concurrent" or
                    "System.ObjectModel" or
                    "System.Private.CoreLib" or
                    "mscorlib"))
                return false;
            return (type.ContainingNamespace.ToDisplayString(), type.Name, type.Arity) is
                ("System.Collections", "ArrayList", 0) or
                ("System.Collections", "Hashtable", 0) or
                ("System.Collections", "Queue", 0) or
                ("System.Collections", "SortedList", 0) or
                ("System.Collections", "Stack", 0) or
                ("System.Collections.Concurrent", "ConcurrentBag", 1) or
                ("System.Collections.Concurrent", "ConcurrentDictionary", 2) or
                ("System.Collections.Concurrent", "ConcurrentQueue", 1) or
                ("System.Collections.Concurrent", "ConcurrentStack", 1) or
                ("System.Collections.Generic", "Dictionary", 2) or
                ("System.Collections.Generic", "HashSet", 1) or
                ("System.Collections.Generic", "LinkedList", 1) or
                ("System.Collections.Generic", "List", 1) or
                ("System.Collections.Generic", "PriorityQueue", 2) or
                ("System.Collections.Generic", "Queue", 1) or
                ("System.Collections.Generic", "SortedDictionary", 2) or
                ("System.Collections.Generic", "SortedList", 2) or
                ("System.Collections.Generic", "SortedSet", 1) or
                ("System.Collections.Generic", "Stack", 1) or
                ("System.Collections.ObjectModel", "Collection", 1) or
                ("System.Collections.ObjectModel", "ObservableCollection", 1);
        }
        private static bool IsNonPopulatingCollectionConstructorParameter(IParameterSymbol parameter) {
            if (parameter.Type.SpecialType is
                SpecialType.System_Int32 or
                SpecialType.System_Single)
                return true;
            if (parameter.Type is not INamedTypeSymbol type)
                return false;
            var definition = type.OriginalDefinition;
            return (definition.ContainingNamespace.ToDisplayString(), definition.Name, definition.Arity) is
                ("System.Collections", "IComparer", 0) or
                ("System.Collections", "IEqualityComparer", 0) or
                ("System.Collections.Generic", "IComparer", 1) or
                ("System.Collections.Generic", "IEqualityComparer", 1);
        }
        private bool IsDefinitelyEmptyString(ExpressionSyntax collection) {
            if (semanticModel.GetConstantValue(collection, cancellationToken) is { HasValue: true, Value: string constant } &&
                constant.Length == 0)
                return true;
            return semanticModel.GetSymbolInfo(collection, cancellationToken).Symbol is
                IFieldSymbol {
                    Name: "Empty",
                    IsStatic: true,
                    IsReadOnly: true,
                    ContainingType.SpecialType: SpecialType.System_String
                };
        }
        private bool IsKnownCoreEmptySingleton(ExpressionSyntax collection) {
            if (semanticModel.GetSymbolInfo(collection, cancellationToken).Symbol is not
                IPropertySymbol {
                    Name: "Empty",
                    IsStatic: true,
                    Parameters.Length: 0,
                    GetMethod: not null
                } property)
                return false;
            var type = property.ContainingType.OriginalDefinition;
            return IsKnownCoreSequenceType(type);
        }
        private static bool IsKnownCoreSequenceType(INamedTypeSymbol candidate) {
            var type = candidate.OriginalDefinition;
            return IsCoreLibraryType(type) &&
                   type.ContainingNamespace.ToDisplayString() == "System" &&
                   type.Arity == 1 &&
                   type.Name is
                       "ArraySegment" or
                       "Memory" or
                       "ReadOnlyMemory" or
                       "ReadOnlySpan" or
                       "Span";
        }
        private static bool IsCoreLibraryType(INamedTypeSymbol type) =>
            type.ContainingAssembly?.Name is
                "System.Private.CoreLib" or "System.Runtime" or "mscorlib";
        private bool IsKnownImmutableEmptySingleton(ExpressionSyntax collection) {
            var symbol = semanticModel.GetSymbolInfo(collection, cancellationToken).Symbol;
            var isEmptySingleton = symbol switch {
                IFieldSymbol { Name: "Empty", IsStatic: true, IsReadOnly: true } => true,
                IPropertySymbol {
                    Name: "Empty",
                    IsStatic: true,
                    Parameters.Length: 0,
                    GetMethod: not null
                } => true,
                _ => false
            };
            return isEmptySingleton &&
                   IsKnownImmutableCollectionType(symbol!.ContainingType);
        }
        private static bool IsKnownImmutableEmptyFactory(IMethodSymbol targetMethod) {
            var definition = targetMethod.OriginalDefinition;
            return definition.Name == "Create" &&
                   definition.IsStatic &&
                   definition.Parameters.Length == 0 &&
                   definition.ContainingType.Arity == 0 &&
                   targetMethod.ReturnType is INamedTypeSymbol returnType &&
                   definition.ContainingType.Name == returnType.OriginalDefinition.Name &&
                   IsKnownImmutableCollectionType(returnType);
        }
        private static bool IsKnownImmutableCollectionType(INamedTypeSymbol candidate) {
            var type = candidate.OriginalDefinition;
            if (type.ContainingAssembly?.Name != "System.Collections.Immutable" ||
                type.ContainingNamespace.ToDisplayString() != "System.Collections.Immutable")
                return false;
            return (type.Name, type.Arity) is
                ("ImmutableArray", 1) or
                ("ImmutableDictionary", 2) or
                ("ImmutableHashSet", 1) or
                ("ImmutableList", 1) or
                ("ImmutableQueue", 1) or
                ("ImmutableSortedDictionary", 2) or
                ("ImmutableSortedSet", 1) or
                ("ImmutableStack", 1);
        }
        private bool TryGetElementTypeOperatorStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source,
            out bool preservesElementIdentity) {
            source = null!;
            preservesElementIdentity = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.Name is not (nameof(Enumerable.Cast) or nameof(Enumerable.OfType)) ||
                definition.ContainingType.ToDisplayString() is not
                    ("System.Linq.Enumerable" or "System.Linq.Queryable") ||
                targetMethod.TypeArguments.Length != 1 ||
                !TryGetStandardSequenceSource(invocation, operation, out source))
                return false;
            var sourceTypeInfo = semanticModel.GetTypeInfo(source, cancellationToken);
            preservesElementIdentity =
                TryGetUniqueEnumerableElementType(
                    sourceTypeInfo.Type ?? sourceTypeInfo.ConvertedType,
                    out var sourceElement) &&
                SymbolEqualityComparer.Default.Equals(
                    sourceElement,
                    targetMethod.TypeArguments[0]);
            return true;
        }
        private static bool TryGetUniqueEnumerableElementType(
            ITypeSymbol? sequenceType,
            out ITypeSymbol elementType) {
            elementType = null!;
            if (sequenceType is IArrayTypeSymbol array) {
                elementType = array.ElementType;
                return true;
            }
            if (sequenceType == null) return false;
            var candidates = new List<ITypeSymbol>();
            void AddCandidate(ITypeSymbol candidate) {
                if (!candidates.Any(existing =>
                        SymbolEqualityComparer.Default.Equals(existing, candidate)))
                    candidates.Add(candidate);
            }
            if (sequenceType is INamedTypeSymbol named &&
                named.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IEnumerable_T)
                AddCandidate(named.TypeArguments[0]);
            foreach (var candidate in sequenceType.AllInterfaces)
                if (candidate.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IEnumerable_T)
                    AddCandidate(candidate.TypeArguments[0]);
            if (candidates.Count != 1) return false;
            elementType = candidates[0];
            return true;
        }
        private bool TryGetSequenceProjectionStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source,
            out bool preservesElementIdentity) {
            source = null!;
            preservesElementIdentity = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation ||
                targetMethod.Parameters.Length == 0)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.Name is not (nameof(Enumerable.Select) or nameof(Enumerable.SelectMany)) ||
                definition.ContainingType.ToDisplayString() is not
                    ("System.Linq.Enumerable" or "System.Linq.Queryable") ||
                !TryGetStandardSequenceSource(invocation, operation, out source))
                return false;
            preservesElementIdentity =
                definition.Name == nameof(Enumerable.Select) &&
                TryGetInvocationArgument(
                    operation,
                    targetMethod.Parameters[targetMethod.Parameters.Length - 1],
                    out var selector) &&
                SymbolicSourcePredicateLowerer.IsIdentitySequenceSelector(
                    selector,
                    new(semanticModel, cancellationToken));
            return true;
        }
        private bool TryGetLazySequencePreservingStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source,
            out bool definitelyNoElements) {
            source = null!;
            definitelyNoElements = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            var containingType = definition.ContainingType.ToDisplayString();
            var supported = containingType switch {
                "System.Linq.Enumerable" => definition.Name is
                    nameof(Enumerable.AsEnumerable) or nameof(Enumerable.Skip) or nameof(Enumerable.Take),
                "System.Linq.Queryable" => definition.Name is
                    nameof(Queryable.AsQueryable) or nameof(Queryable.Skip) or nameof(Queryable.Take),
                _ => false
            };
            if (!supported)
                return false;
            if (!TryGetStandardSequenceSource(invocation, operation, out source))
                return false;
            if (definition.Name == nameof(Enumerable.Take) &&
                targetMethod.Parameters.Length != 0 &&
                TryGetInvocationArgument(
                    operation,
                    targetMethod.Parameters[targetMethod.Parameters.Length - 1],
                    out var count) &&
                DefinitelyCapsSequenceAtZero(count))
                definitelyNoElements = true;
            return true;
        }
        private bool DefinitelyCapsSequenceAtZero(ExpressionSyntax limit) {
            var resolvedUses = new List<(ISymbol Symbol, int Position)>();
            while (true) {
                limit = SymbolicConversionLowerer.UnwrapIdentityConversions(
                    limit,
                    semanticModel,
                    cancellationToken);
                if (semanticModel.GetConstantValue(limit, cancellationToken) is { HasValue: true, Value: int constant } &&
                    constant <= 0 ||
                    SymbolicIndexingLowerer.DefinitelyProducesNoElements(
                        limit,
                        new(semanticModel, cancellationToken)))
                    return true;
                limit = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(limit);
                var symbol = semanticModel.GetSymbolInfo(limit, cancellationToken).Symbol?.OriginalDefinition;
                var usePosition = limit.SpanStart;
                if (symbol is not (ILocalSymbol or IParameterSymbol) ||
                    resolvedUses.Any(candidate =>
                        candidate.Position == usePosition &&
                        SymbolEqualityComparer.Default.Equals(candidate.Symbol, symbol)) ||
                    !SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                        symbol,
                        limit,
                        semanticModel,
                        cancellationToken,
                        true,
                        out limit))
                    return false;
                resolvedUses.Add((symbol, usePosition));
            }
        }
        private bool TryGetSequencePredicateStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source,
            out ExpressionSyntax predicate,
            out bool guaranteesTruth) {
            source = null!;
            predicate = null!;
            guaranteesTruth = false;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation ||
                targetMethod.Parameters.Length == 0 ||
                !TryGetInvocationArgument(
                    operation,
                    targetMethod.Parameters[targetMethod.Parameters.Length - 1],
                    out var candidatePredicate))
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            var containingType = definition.ContainingType.ToDisplayString();
            var supported = containingType switch {
                "System.Linq.Enumerable" => definition.Name is
                    nameof(Enumerable.Where) or nameof(Enumerable.TakeWhile) or nameof(Enumerable.SkipWhile),
                "System.Linq.ImmutableArrayExtensions" =>
                    definition.Name == nameof(Enumerable.Where),
                "System.Linq.Queryable" =>
                    definition.Name == nameof(Queryable.Where),
                _ => false
            };
            if (!supported)
                return false;
            if (!TryGetStandardSequenceSource(invocation, operation, out source)) return false;
            predicate = candidatePredicate;
            guaranteesTruth = definition.Name != nameof(Enumerable.SkipWhile);
            return true;
        }
        private bool TryGetStandardSequenceSource(
            InvocationExpressionSyntax invocation,
            IInvocationOperation operation,
            out ExpressionSyntax source) {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not
                    INamedTypeSymbol) {
                source = memberAccess.Expression;
                return true;
            }
            if (operation.TargetMethod.Parameters.Length != 0)
                return TryGetInvocationArgument(
                    operation,
                    operation.TargetMethod.Parameters[0],
                    out source);
            source = null!;
            return false;
        }
        private static bool TryGetInvocationArgument(
            IInvocationOperation invocation,
            IParameterSymbol parameter,
            out ExpressionSyntax expression) {
            expression = invocation.Arguments
                .FirstOrDefault(argument =>
                    argument.Parameter != null &&
                    SymbolEqualityComparer.Default.Equals(
                        argument.Parameter.OriginalDefinition,
                        parameter.OriginalDefinition))
                ?.Value.Syntax as ExpressionSyntax ?? null!;
            return expression != null;
        }
        private void ApplyContainingSwitchFacts(ref SymbolicState state) {
            foreach (var arm in site.Ancestors().OfType<SwitchExpressionArmSyntax>()) {
                if (arm.Parent is not SwitchExpressionSyntax switchExpression ||
                    !SymbolicReachabilityLowerer.TryGetBooleanPatternValue(
                        arm.Pattern,
                        semanticModel,
                        cancellationToken,
                        out var value))
                    continue;
                SymbolicReachabilityLowerer.Apply(
                    ref state,
                    switchExpression.GoverningExpression,
                    value,
                    semanticModel,
                    cancellationToken);
                if (arm.WhenClause is { } whenClause &&
                    !whenClause.Span.Contains(site.SpanStart))
                    SymbolicStateInvalidator.InvalidateNestedMutations(
                        ref state,
                        whenClause.Condition,
                        semanticModel,
                        cancellationToken);
            }
            foreach (var section in site.Ancestors().OfType<SwitchSectionSyntax>()) {
                if (section.Parent is not SwitchStatementSyntax switchStatement ||
                    !TryGetSectionBooleanValue(section, out var value))
                    continue;
                SymbolicReachabilityLowerer.Apply(
                    ref state,
                    switchStatement.Expression,
                    value,
                    semanticModel,
                    cancellationToken);
                foreach (var patternLabel in section.Labels.OfType<CasePatternSwitchLabelSyntax>())
                    if (patternLabel.WhenClause is { } whenClause &&
                        !whenClause.Span.Contains(site.SpanStart))
                        SymbolicStateInvalidator.InvalidateNestedMutations(
                            ref state,
                            whenClause.Condition,
                            semanticModel,
                            cancellationToken);
            }
        }
        private bool TryGetSectionBooleanValue(SwitchSectionSyntax section, out bool value) {
            value = false;
            bool? sectionValue = null;
            foreach (var label in section.Labels) {
                bool? labelValue = label switch {
                    CasePatternSwitchLabelSyntax patternLabel =>
                        SymbolicReachabilityLowerer.TryGetBooleanPatternValue(
                            patternLabel.Pattern,
                            semanticModel,
                            cancellationToken,
                            out var patternValue)
                            ? patternValue
                            : null,
                    CaseSwitchLabelSyntax constantLabel
                        when semanticModel.GetConstantValue(constantLabel.Value, cancellationToken) is { HasValue: true, Value: bool constantValue } => constantValue,
                    _ => null
                };
                if (!labelValue.HasValue ||
                    sectionValue.HasValue && sectionValue.Value != labelValue.Value)
                    return false;
                sectionValue = labelValue.Value;
            }
            if (!sectionValue.HasValue) return false;
            value = sectionValue.Value;
            return true;
        }
        private static bool AlwaysCompletes(StatementSyntax statement) => statement switch {
            ReturnStatementSyntax or ThrowStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax => true,
            BlockSyntax block => block.Statements.LastOrDefault() is { } last && AlwaysCompletes(last),
            _ => false
        };
        private static bool IsSubset(SymbolicState subset, SymbolicState superset) {
            if (subset.IsContradictory && !superset.IsContradictory) return false;
            var factKeys = new HashSet<string>(superset.Facts.Select(SymbolicState.CreateProofFactKey), StringComparer.Ordinal);
            var conditionKeys = new HashSet<string>(
                superset.PathConditions.Select(SymbolicState.CreateProofConditionKey), StringComparer.Ordinal);
            return subset.Facts.All(fact => factKeys.Contains(SymbolicState.CreateProofFactKey(fact))) &&
                   subset.PathConditions.All(condition => conditionKeys.Contains(SymbolicState.CreateProofConditionKey(condition)));
        }
        private static bool Contains(SyntaxNode container, SyntaxNode candidate) =>
            container.Span.Contains(candidate.SpanStart) || candidate.Span.Contains(container.SpanStart);
    }
}

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
                if (TryGetZeroPreservingCoreSequenceViewStep(collection, out source)) {
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
                    !SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                        symbol,
                        collection,
                        semanticModel,
                        cancellationToken,
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
        private bool TryGetZeroPreservingCoreSequenceViewStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source) {
            source = null!;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(collection, cancellationToken).Symbol is
                    IPropertySymbol {
                        Name: "Span",
                        IsStatic: false,
                        Parameters.Length: 0,
                        GetMethod: not null
                    } property &&
                property.ContainingType.OriginalDefinition.Name is "Memory" or "ReadOnlyMemory" &&
                IsKnownCoreSequenceType(property.ContainingType)) {
                source = memberAccess.Expression;
                return true;
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
                definition.Name is "Slice" or "ToArray";
            return supported &&
                   TryGetStandardSequenceSource(invocation, operation, out source);
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
            if (collection is CollectionExpressionSyntax { Elements.Count: 0 })
                return true;
            if (collection is ArrayCreationExpressionSyntax arrayCreation) {
                if (arrayCreation.Initializer is { Expressions.Count: 0 })
                    return true;
                if (arrayCreation.Type.RankSpecifiers
                    .SelectMany(rank => rank.Sizes)
                    .Any(size =>
                        semanticModel.GetConstantValue(size, cancellationToken) is { HasValue: true, Value: int length } &&
                        length == 0))
                    return true;
            }
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod } operation)
                return false;
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
                   semanticModel.GetConstantValue(count, cancellationToken) is { HasValue: true, Value: int constantCount } &&
                   constantCount <= 0;
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

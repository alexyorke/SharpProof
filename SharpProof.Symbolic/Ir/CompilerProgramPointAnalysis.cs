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
                    !TryGetSequencePredicateSteps(loop.Expression, out var predicateSteps) ||
                    SymbolicMutationInventory.Create(loop.Statement, semanticModel, cancellationToken)
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
            out ImmutableArray<SequencePredicateStep> steps) {
            var builder = ImmutableArray.CreateBuilder<SequencePredicateStep>();
            var resolvedUses = new List<(ISymbol Symbol, int Position)>();
            while (true) {
                if (TryGetSequencePredicateStep(
                        collection,
                        out var source,
                        out var predicate,
                        out var guaranteesTruth)) {
                    builder.Add(new(predicate, guaranteesTruth));
                    collection = source;
                    continue;
                }
                if (TryGetLazySequencePreservingStep(collection, out source)) {
                    collection = source;
                    continue;
                }
                if (TryGetIdentitySequenceProjectionStep(collection, out source)) {
                    collection = source;
                    continue;
                }
                if (TryGetSameElementSequenceCastStep(collection, out source)) {
                    collection = source;
                    continue;
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
            return steps.Length != 0;
        }
        private bool TryGetSameElementSequenceCastStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source) {
            source = null!;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod })
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.Name != nameof(Enumerable.Cast) ||
                definition.ContainingType.ToDisplayString() is not
                    ("System.Linq.Enumerable" or "System.Linq.Queryable") ||
                targetMethod.TypeArguments.Length != 1 ||
                !TryGetStandardSequenceSource(invocation, out source))
                return false;
            var sourceTypeInfo = semanticModel.GetTypeInfo(source, cancellationToken);
            return TryGetUniqueEnumerableElementType(
                       sourceTypeInfo.Type ?? sourceTypeInfo.ConvertedType,
                       out var sourceElement) &&
                   SymbolEqualityComparer.Default.Equals(
                       sourceElement,
                       targetMethod.TypeArguments[0]);
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
        private bool TryGetIdentitySequenceProjectionStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source) {
            source = null!;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                invocation.ArgumentList.Arguments.LastOrDefault()?.Expression is not { } selector ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod })
                return false;
            var definition = targetMethod.ReducedFrom ?? targetMethod;
            if (definition.Name != nameof(Enumerable.Select) ||
                definition.ContainingType.ToDisplayString() is not
                    ("System.Linq.Enumerable" or "System.Linq.Queryable") ||
                !SymbolicSourcePredicateLowerer.IsIdentitySequenceSelector(
                    selector,
                    new(semanticModel, cancellationToken)))
                return false;
            return TryGetStandardSequenceSource(invocation, out source);
        }
        private bool TryGetLazySequencePreservingStep(
            ExpressionSyntax collection,
            out ExpressionSyntax source) {
            source = null!;
            collection = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(collection);
            if (collection is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod })
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
            return TryGetStandardSequenceSource(invocation, out source);
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
                invocation.ArgumentList.Arguments.LastOrDefault()?.Expression is not { } candidatePredicate ||
                semanticModel.GetOperation(invocation, cancellationToken) is not
                    IInvocationOperation { TargetMethod: { } targetMethod })
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
            if (!TryGetStandardSequenceSource(invocation, out source)) return false;
            predicate = candidatePredicate;
            guaranteesTruth = definition.Name != nameof(Enumerable.SkipWhile);
            return true;
        }
        private bool TryGetStandardSequenceSource(
            InvocationExpressionSyntax invocation,
            out ExpressionSyntax source) {
            source = invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                     semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not
                         INamedTypeSymbol
                ? memberAccess.Expression
                : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression!;
            return source != null;
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

using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicLoopStateTransfer
{
    private delegate bool TryLowerLoopInitializerBound(
        ExpressionSyntax expression,
        ISymbol initializedSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm bound,
        out IReadOnlyList<ISymbol> boundSymbols);

    private static void AddForLoopBodyInvariantConditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var initializerBounds = EnumerateLoopBoundTerms(
            forStatement,
            semanticModel,
            cancellationToken,
            TryLowerInitializerBoundTerm);
        AddLoopMonotonicBoundConditions(
            conditions,
            forStatement,
            forStatement.Statement,
            initializerBounds,
            SymbolicRelationOperator.GreaterThanOrEqual,
            MonotonicDirection.NonDecreasing,
            "ir.path.for-loop-invariant.lower-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundConditions(
            conditions,
            forStatement,
            forStatement.Statement,
            initializerBounds,
            SymbolicRelationOperator.LessThanOrEqual,
            MonotonicDirection.NonIncreasing,
            "ir.path.for-loop-invariant.initial-upper-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundConditions(
            conditions,
            forStatement,
            forStatement.Statement,
            EnumerateLoopBoundTerms(
                forStatement, semanticModel, cancellationToken, TryGetStrictUpperBoundInitializerTerm),
            SymbolicRelationOperator.LessThan,
            MonotonicDirection.NonIncreasing,
            "ir.path.for-loop-invariant.strict-upper-bound",
            semanticModel,
            cancellationToken);
    }

    private static void AddPreLoopBodyInvariantConditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        string provenancePrefix,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var initializerBounds = EnumerateLoopBoundTerms(
            loopStatement,
            semanticModel,
            cancellationToken,
            TryLowerInitializerBoundTerm);
        AddLoopMonotonicBoundConditions(
            conditions,
            loopStatement,
            loopBody,
            initializerBounds,
            SymbolicRelationOperator.GreaterThanOrEqual,
            MonotonicDirection.NonDecreasing,
            provenancePrefix + ".lower-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundConditions(
            conditions,
            loopStatement,
            loopBody,
            initializerBounds,
            SymbolicRelationOperator.LessThanOrEqual,
            MonotonicDirection.NonIncreasing,
            provenancePrefix + ".initial-upper-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundConditions(
            conditions,
            loopStatement,
            loopBody,
            EnumerateLoopBoundTerms(
                loopStatement, semanticModel, cancellationToken, TryGetStrictUpperBoundInitializerTerm),
            SymbolicRelationOperator.LessThan,
            MonotonicDirection.NonIncreasing,
            provenancePrefix + ".strict-upper-bound",
            semanticModel,
            cancellationToken);
    }

    private static void AddLoopMonotonicBoundConditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)> initializers,
        SymbolicRelationOperator relation,
        MonotonicDirection direction,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var forStatement = loopStatement as ForStatementSyntax;
        foreach (var initializer in initializers)
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.Bound.Kind != SmtValueKind.Int)
                continue;

            var initializerIsInvalidated = forStatement != null
                ? SymbolicMutationInventory.Create(loopBody, semanticModel, cancellationToken)
                      .MutatesSymbol(initializer.Symbol) ||
                  ForLoopConditionInvalidatesSymbolValue(
                      forStatement,
                      initializer.Symbol,
                      semanticModel,
                      cancellationToken)
                : LoopHeaderInvalidatesSymbolValue(
                    loopStatement,
                    initializer.Symbol,
                    semanticModel,
                    cancellationToken);
            var boundIsInvalidated = initializer.BoundSymbols.Any(symbol =>
                SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(
                    loopBody,
                    symbol,
                    semanticModel,
                    cancellationToken) ||
                (forStatement != null
                    ? ForLoopConditionInvalidatesSymbolValue(
                          forStatement,
                          symbol,
                          semanticModel,
                          cancellationToken) ||
                      forStatement.Incrementors.Any(incrementor =>
                          SymbolicMutationInventory.Create(incrementor, semanticModel, cancellationToken)
                              .InvalidatesSymbol(symbol, mutableExposures: false))
                    : LoopHeaderInvalidatesSymbolValue(
                        loopStatement,
                        symbol,
                        semanticModel,
                        cancellationToken)));
            var mutationsPreserveBound = forStatement != null
                ? ForLoopIncrementorsPreserveBound(
                    forStatement,
                    initializer.Symbol,
                    direction,
                    semanticModel,
                    cancellationToken)
                : LoopBodyMutationsPreserveBound(
                    loopBody,
                    initializer.Symbol,
                    direction,
                    semanticModel,
                    cancellationToken);
            if (initializerIsInvalidated ||
                boundIsInvalidated ||
                !mutationsPreserveBound)
                continue;

            conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                relation,
                symbolTerm,
                initializer.Bound,
                loopStatement,
                provenance));
        }
    }

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumerateLoopBoundTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            TryLowerLoopInitializerBound tryLowerBound)
    {
        foreach (var initializer in EnumerateLoopInitializers(loopStatement, semanticModel, cancellationToken))
        {
            if (!tryLowerBound(
                    initializer.Value,
                    initializer.Symbol,
                    semanticModel,
                    cancellationToken,
                    out var bound,
                    out var boundSymbols) ||
                initializer.StatementIndex is int &&
                AnyPriorStatementsInvalidateInitializer(
                    (initializer.Symbol, initializer.Value, initializer.StatementIndex.Value),
                    loopStatement,
                    boundSymbols,
                    semanticModel,
                    cancellationToken))
                continue;

            yield return (initializer.Symbol, bound, boundSymbols);
        }
    }

    private static IEnumerable<(ISymbol Symbol, ExpressionSyntax Value, int? StatementIndex)>
        EnumerateLoopInitializers(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        if (loopStatement is ForStatementSyntax forStatement)
        {
            foreach (var initializer in EnumerateForLoopInitializers(forStatement, semanticModel, cancellationToken))
                yield return (initializer.Symbol, initializer.Value, null);
            yield break;
        }

        foreach (var initializer in EnumeratePreLoopInitializerExpressions(
                     loopStatement, semanticModel, cancellationToken))
            yield return (initializer.Symbol, initializer.Value, initializer.StatementIndex);
    }

    private static IEnumerable<(ISymbol Symbol, ExpressionSyntax Value, bool IsDeclaration)>
        EnumerateForLoopInitializers(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (forStatement.Declaration != null)
            foreach (var declarator in forStatement.Declaration.Variables)
                if (declarator.Initializer != null &&
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol local)
                    yield return (local.OriginalDefinition, declarator.Initializer.Value, true);

        foreach (var expression in forStatement.Initializers)
            if (expression is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is { } symbol &&
                symbol is ILocalSymbol or IParameterSymbol)
                yield return (symbol.OriginalDefinition, assignment.Right, false);
    }

    private static bool TryGetStrictUpperBoundInitializerTerm(
        ExpressionSyntax expression,
        ISymbol initializedSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm upperBound,
        out IReadOnlyList<ISymbol> upperBoundSymbols)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is not BinaryExpressionSyntax binaryExpression)
        {
            upperBound = null!;
            upperBoundSymbols = Array.Empty<ISymbol>();
            return false;
        }

        if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractedValue) &&
            subtractedValue > 0 &&
            TryLowerInitializerBoundTerm(
                binaryExpression.Left,
                initializedSymbol,
                semanticModel,
                cancellationToken,
                out upperBound,
                out upperBoundSymbols))
            return true;

        if (binaryExpression.IsKind(SyntaxKind.AddExpression))
        {
            if (SymbolicLoweringValueFacts.TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                rightValue < 0 &&
                TryLowerInitializerBoundTerm(
                    binaryExpression.Left,
                    initializedSymbol,
                    semanticModel,
                    cancellationToken,
                    out upperBound,
                    out upperBoundSymbols))
                return true;

            if (SymbolicLoweringValueFacts.TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
                leftValue < 0 &&
                TryLowerInitializerBoundTerm(
                    binaryExpression.Right,
                    initializedSymbol,
                    semanticModel,
                    cancellationToken,
                    out upperBound,
                    out upperBoundSymbols))
                return true;
        }

        upperBound = null!;
        upperBoundSymbols = Array.Empty<ISymbol>();
        return false;
    }

    private static bool TryLowerInitializerBoundTerm(
        ExpressionSyntax expression,
        ISymbol initializedSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm bound,
        out IReadOnlyList<ISymbol> boundSymbols)
    {
        var referencedSymbols = SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
            expression,
            semanticModel,
            cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (referencedSymbols.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, initializedSymbol)) ||
            lowering is not { IsExact: true, Value: { } candidate } ||
            candidate.Kind != SmtValueKind.Int)
        {
            bound = null!;
            boundSymbols = Array.Empty<ISymbol>();
            return false;
        }

        bound = candidate;
        boundSymbols = referencedSymbols;
        return true;
    }

    internal static void AddForeachBodyEntryStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expressionSyntax,
        StatementSyntax foreachStatement,
        StatementSyntax foreachBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        state = SymbolicSourceCompletionLowerer.ApplyThrowGuard(
            state,
            expressionSyntax,
            foreachBody,
            semanticModel,
            cancellationToken,
            "ir.path.foreach-entry.throw-guarded-not-null").State;
        SymbolicProgramPointFacts.AddReferenceNullCondition(
            ref state,
            expressionSyntax,
            false,
            semanticModel,
            cancellationToken,
            "ir.path.foreach-entry.not-null");
        AddFiniteForeachIterationStateFact(
            ref state,
            expressionSyntax,
            foreachStatement,
            semanticModel,
            cancellationToken);
        AddForeachLengthPositiveStateFact(
            ref state,
            expressionSyntax,
            foreachStatement,
            semanticModel,
            cancellationToken);
    }

    internal static bool TryApplyLoopBodyEntryStateFacts(
        ref SymbolicState state,
        SyntaxNode candidate,
        int? siteSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (candidate)
        {
            case WhileStatementSyntax loop when ContainsSite(loop.Statement, siteSpanStart):
                if (!ReferencesAreAssignedBeforeSite(loop.Condition, loop.Statement, siteSpanStart, semanticModel, cancellationToken))
                {
                    SymbolicProgramPointFacts.AddReachabilityCondition(
                        ref state, loop.Condition, true, semanticModel, cancellationToken);
                    ApplyLoopBodyInvariantStateFacts(
                        ref state, loop, SymbolicLoopEdgeKind.Entry, semanticModel, cancellationToken);
                }
                return true;
            case DoStatementSyntax loop when ContainsSite(loop.Statement, siteSpanStart):
                ApplyLoopBodyInvariantStateFacts(
                    ref state, loop, SymbolicLoopEdgeKind.Entry, semanticModel, cancellationToken);
                return true;
            case ForStatementSyntax loop when ContainsSite(loop.Statement, siteSpanStart):
                if (loop.Condition != null &&
                    !ReferencesAreAssignedBeforeSite(loop.Condition, loop.Statement, siteSpanStart, semanticModel, cancellationToken))
                    SymbolicProgramPointFacts.AddReachabilityCondition(
                        ref state, loop.Condition, true, semanticModel, cancellationToken);
                ApplyLoopBodyInvariantStateFacts(
                    ref state, loop, SymbolicLoopEdgeKind.Entry, semanticModel, cancellationToken);
                return true;
            case ForEachStatementSyntax loop when ContainsSite(loop.Statement, siteSpanStart):
                if (!ReferencesAreAssignedBeforeSite(loop.Expression, loop.Statement, siteSpanStart, semanticModel, cancellationToken))
                    AddForeachBodyEntryStateFacts(
                        ref state, loop.Expression, loop, loop.Statement, semanticModel, cancellationToken);
                return true;
            case ForEachVariableStatementSyntax loop when ContainsSite(loop.Statement, siteSpanStart):
                if (!ReferencesAreAssignedBeforeSite(loop.Expression, loop.Statement, siteSpanStart, semanticModel, cancellationToken))
                    AddForeachBodyEntryStateFacts(
                        ref state, loop.Expression, loop, loop.Statement, semanticModel, cancellationToken);
                return true;
            default:
                return false;
        }
    }

    internal static void ApplyLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        SymbolicLoopEdgeKind edgeKind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var lowering = LowerLoopBodyInvariants(loopStatement, semanticModel, cancellationToken);
        if (lowering is not { IsExact: true, Value: { } plan })
            return;
        foreach (var condition in plan.Conditions)
            state = SymbolicOperationTransferKernel.TransitionLoopEdge(
                state,
                edgeKind,
                condition,
                loopStatement.Span,
                "ir.path.loop-invariant").State;
    }

    private static bool ContainsSite(StatementSyntax body, int? siteSpanStart) =>
        !siteSpanStart.HasValue || body.Span.Contains(siteSpanStart.Value);

    private static bool ReferencesAreAssignedBeforeSite(
        ExpressionSyntax expression,
        StatementSyntax body,
        int? siteSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        siteSpanStart.HasValue && AnyReferencedSymbolAssignedBeforeUse(
            expression,
            body,
            siteSpanStart.Value,
            semanticModel,
            cancellationToken);

    private static void AddFiniteForeachIterationStateFact(
        ref SymbolicState state,
        ExpressionSyntax expressionSyntax,
        StatementSyntax foreachStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var lowering = SymbolicFiniteDomainLowerer.LowerForeachDomain(
            expressionSyntax,
            foreachStatement,
            semanticModel,
            cancellationToken);
        if (lowering is not { IsExact: true, Value: { } plan })
            return;

        foreach (var condition in plan.Conditions)
            state = SymbolicOperationTransferKernel.TransitionLoopEdge(
                state, SymbolicLoopEdgeKind.Entry, condition, foreachStatement.Span,
                "ir.path.foreach-entry.finite-domain").State;
    }

    private static void AddForeachLengthPositiveStateFact(
        ref SymbolicState state,
        ExpressionSyntax expressionSyntax,
        StatementSyntax foreachStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (AnyConditionSymbolInvalidatedInStatement(expressionSyntax, foreachStatement, semanticModel,
                cancellationToken)) return;

        var typeInfo = semanticModel.GetTypeInfo(expressionSyntax, cancellationToken);
        if (!TryCreateForeachLengthTerm(expressionSyntax, typeInfo.Type, semanticModel, cancellationToken,
                out var length) &&
            !TryCreateForeachLengthTerm(expressionSyntax, typeInfo.ConvertedType, semanticModel, cancellationToken,
                out length))
            return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                length,
                new SymbolicIntegerConstantTerm(0)),
            expressionSyntax,
            "ir.path.foreach-entry.length-positive");
        state = SymbolicOperationTransferKernel.TransitionLoopEdge(
            state, SymbolicLoopEdgeKind.Entry, new SymbolicFactCondition(fact), foreachStatement.Span,
            "ir.path.foreach-entry.length-positive").State;
    }

    private static bool TryCreateForeachLengthTerm(
        ExpressionSyntax expressionSyntax,
        ITypeSymbol? type,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm length)
    {
        length = null!;
        if (!SymbolicProgramPointFacts.IsSupportedForeachLengthReceiver(expressionSyntax) &&
            !SymbolicProgramPointFacts.IsSupportedForeachLengthReceiver(type))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lengthLowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(expressionSyntax, context);
        if (lengthLowering is { IsExact: true, Value: { } loweredLength })
        {
            length = loweredLength;
            return true;
        }

        var receiverLowering = SymbolicSemanticPipeline.LowerTerm(expressionSyntax, context);
        if (receiverLowering is not { IsExact: true, Value: { } receiver }) return false;

        if (type?.SpecialType == SpecialType.System_String)
        {
            if (receiver.Kind == SmtValueKind.String)
            {
                length = new SymbolicLengthTerm(receiver);
                return true;
            }

            var projection = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(type, receiver, expressionSyntax);
            if (projection is not { IsExact: true, Value: { } projectedLength }) return false;

            length = projectedLength;
            return true;
        }

        var receiverProjection = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(type, receiver, expressionSyntax);
        if (receiverProjection is not { IsExact: true, Value: { } receiverLength }) return false;

        length = receiverLength;
        return true;
    }

    internal static SymbolicState CollectForInitializerState(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var state = new SymbolicState();
        foreach (var initializer in EnumerateForLoopInitializers(forStatement, semanticModel, cancellationToken))
        {
            SymbolicStateInvalidator.InvalidateNestedMutations(
                ref state,
                initializer.Value,
                semanticModel,
                cancellationToken);
            if (semanticModel.GetOperation(initializer.Value, cancellationToken) is { } valueOperation)
                SymbolicCfgProgramPointStateCollector.TryApplyAssignment(
                    ref state,
                    initializer.Symbol,
                    valueOperation,
                    guard: null,
                    allowGuardedReferenceAssignments: true,
                    allowGuardMutation: true,
                    semanticModel,
                    cancellationToken,
                    "ir.path.for-initializer",
                    out _);
            if (initializer.IsDeclaration)
                state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
                    state,
                    initializer.Value,
                    forStatement.Statement,
                    includeThrowGuardFacts: false,
                    semanticModel,
                    cancellationToken).State;
        }

        return state.Normalize();
    }

    internal static void InvalidateForLoopInitializerTargets(
        ref SymbolicState state,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var targets = EnumerateForLoopInitializers(forStatement, semanticModel, cancellationToken)
            .Select(static initializer => new SymbolicInvalidationTarget(
                SymbolicFactFactory.GetSmtVariableName(initializer.Symbol)))
            .ToImmutableArray();
        if (!targets.IsEmpty)
            state = SymbolicOperationTransferKernel.Invalidate(
                state,
                targets,
                forStatement.Span,
                "ir.path.for-initializer-entry").State;
    }

    internal static SymbolicLoweringResult<SymbolicLoopInvariantPlan> LowerLoopBodyInvariants(
        StatementSyntax loopStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        switch (loopStatement)
        {
            case ForStatementSyntax forStatement:
                AddForLoopBodyInvariantConditions(
                    conditions,
                    forStatement,
                    semanticModel,
                    cancellationToken);
                break;
            case WhileStatementSyntax whileStatement:
                AddPreLoopBodyInvariantConditions(
                    conditions,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case DoStatementSyntax doStatement:
                AddPreLoopBodyInvariantConditions(
                    conditions,
                    doStatement,
                    doStatement.Statement,
                    "ir.path.do-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
        }

        return SymbolicLoweringResult<SymbolicLoopInvariantPlan>.Exact(
            new SymbolicLoopInvariantPlan(conditions.ToImmutable()),
            new SymbolicLoweringProvenance("loop-invariants", loopStatement.Span, "exact"));
    }

    private static IEnumerable<(ISymbol Symbol, ExpressionSyntax Value, int StatementIndex)>
        EnumeratePreLoopInitializerExpressions(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        if (loopStatement.Parent is not BlockSyntax containingBlock) yield break;

        var loopIndex = containingBlock.Statements.IndexOf(loopStatement);
        for (var statementIndex = 0; statementIndex < loopIndex; statementIndex++)
        {
            var statement = containingBlock.Statements[statementIndex];
            if (statement is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } localDeclaration)
            {
                var declarator = localDeclaration.Declaration.Variables[0];
                if (declarator.Initializer != null &&
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    yield return (localSymbol.OriginalDefinition, declarator.Initializer.Value, statementIndex);

                continue;
            }

            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is { } symbol &&
                symbol is ILocalSymbol or IParameterSymbol)
                yield return (symbol.OriginalDefinition, assignment.Right, statementIndex);
        }
    }

    private static bool AnyPriorStatementsInvalidateInitializer(
        (ISymbol Symbol, ExpressionSyntax Value, int StatementIndex) initializer,
        StatementSyntax loopStatement,
        IReadOnlyList<ISymbol> boundSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (loopStatement.Parent is not BlockSyntax containingBlock) return true;

        var loopIndex = containingBlock.Statements.IndexOf(loopStatement);
        for (var statementIndex = initializer.StatementIndex + 1; statementIndex < loopIndex; statementIndex++)
        {
            var statement = containingBlock.Statements[statementIndex];
            if (SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(statement, initializer.Symbol, semanticModel, cancellationToken) ||
                boundSymbols.Any(symbol =>
                    SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken)))
                return true;
        }

        return false;
    }

    private static bool LoopHeaderInvalidatesSymbolValue(
        StatementSyntax loopStatement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var headerExpression = loopStatement switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition,
            DoStatementSyntax doStatement => doStatement.Condition,
            ForStatementSyntax forStatement => forStatement.Condition,
            _ => null
        };

        return headerExpression != null && SymbolicMutationInventory
            .Create(headerExpression, semanticModel, cancellationToken)
            .InvalidatesSymbol(symbol, mutableExposures: false);
    }

    private static bool ForLoopIncrementorsPreserveBound(
        ForStatementSyntax forStatement,
        ISymbol symbol,
        MonotonicDirection direction,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var incrementor in forStatement.Incrementors)
        {
            if (!SymbolMutationFacts.ExpressionReferencesSymbol(incrementor, symbol, semanticModel, cancellationToken)) continue;

            if (!IncrementorPreservesBound(incrementor, symbol, direction, semanticModel, cancellationToken)) return false;
        }

        return true;
    }

    private static bool LoopBodyMutationsPreserveBound(
        StatementSyntax loopBody,
        ISymbol symbol,
        MonotonicDirection direction,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var inventory = SymbolicMutationInventory.Create(loopBody, semanticModel, cancellationToken);
        if (inventory.ExposesSymbol(symbol, mutableOnly: false)) return false;
        foreach (var source in inventory.MutationSources(symbol))
            if (source is not ExpressionSyntax expression ||
                !IncrementorPreservesBound(expression, symbol, direction, semanticModel, cancellationToken))
                return false;

        return true;
    }

    private static bool IncrementorPreservesBound(
        ExpressionSyntax expression,
        ISymbol symbol,
        MonotonicDirection direction,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (SymbolMutationFacts.TryGetIncrementedOrDecrementedSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var unarySymbol,
                out var delta) &&
            SymbolEqualityComparer.Default.Equals(unarySymbol, symbol))
            return IsCompatibleDelta(delta, direction);

        if (expression is not AssignmentExpressionSyntax assignment ||
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
            !SymbolEqualityComparer.Default.Equals(assignedSymbol.OriginalDefinition, symbol))
            return false;

        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var addedValue))
            return IsCompatibleDelta(addedValue, direction);

        if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var subtractedValue))
            return IsCompatibleSubtrahend(subtractedValue, direction);

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return TryIsSelfPlusCompatibleConstant(
                assignment.Right,
                symbol,
                direction,
                semanticModel,
                cancellationToken);

        return false;
    }

    private static bool TryIsSelfPlusCompatibleConstant(
        ExpressionSyntax expression,
        ISymbol symbol,
        MonotonicDirection direction,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is not BinaryExpressionSyntax binaryExpression) return false;

        if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            return (IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                    SymbolicLoweringValueFacts.TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken,
                        out var rightValue) &&
                    IsCompatibleDelta(rightValue, direction)) ||
                   (IsSymbolReference(binaryExpression.Right, symbol, semanticModel, cancellationToken) &&
                    SymbolicLoweringValueFacts.TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken,
                        out var leftValue) &&
                    IsCompatibleDelta(leftValue, direction));

        return binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
               IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
               SymbolicLoweringValueFacts.TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken,
                   out var subtractValue) &&
               IsCompatibleSubtrahend(subtractValue, direction);
    }

    private static bool IsCompatibleDelta(long delta, MonotonicDirection direction) =>
        direction == MonotonicDirection.NonDecreasing ? delta >= 0 : delta <= 0;

    private static bool IsCompatibleSubtrahend(long subtrahend, MonotonicDirection direction) =>
        direction == MonotonicDirection.NonDecreasing ? subtrahend <= 0 : subtrahend >= 0;

    private enum MonotonicDirection
    {
        NonDecreasing,
        NonIncreasing
    }

    private static bool IsSymbolReference(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionSymbol = semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression), cancellationToken).Symbol;
        return expressionSymbol != null &&
               SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol);
    }

    internal static bool IsLoopBodyBlock(BlockSyntax block)
    {
        return block.Parent switch
        {
            WhileStatementSyntax whileStatement => ReferenceEquals(whileStatement.Statement, block),
            ForStatementSyntax forStatement => ReferenceEquals(forStatement.Statement, block),
            ForEachStatementSyntax forEachStatement => ReferenceEquals(forEachStatement.Statement, block),
            DoStatementSyntax doStatement => ReferenceEquals(doStatement.Statement, block),
            _ => false
        };
    }

    internal static bool AnyConditionSymbolMutatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        return SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken)
            .MutatesAny(conditionSymbols, exactTargets: true);
    }

    internal static bool AnyConditionSymbolInvalidatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        return conditionSymbols.Count != 0 &&
               conditionSymbols.Any(symbol =>
                   SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken));
    }

    internal static bool ReferenceIdentityFactIsInvalidatedInStatement(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression), cancellationToken).Symbol
            ?.OriginalDefinition;
        if (symbol is ILocalSymbol or IParameterSymbol)
            return SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken)
                .MutatesSymbol(symbol);

        return AnyConditionSymbolInvalidatedInStatement(
            expression,
            statement,
            semanticModel,
            cancellationToken);
    }

    internal static bool ExpressionMutatesAnySymbol(
        ExpressionSyntax expression,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicMutationInventory.Create(expression, semanticModel, cancellationToken)
            .MutatesAny(symbols);
    }

    private static bool ForLoopConditionInvalidatesSymbolValue(
        ForStatementSyntax forStatement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return forStatement.Condition != null &&
               ExpressionMutatesAnySymbol(
                   forStatement.Condition,
                   new[] { symbol },
                   semanticModel,
                   cancellationToken);
    }

    internal static bool IsLocalOrParameterReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression), cancellationToken).Symbol
            ?.OriginalDefinition;
        return symbol is ILocalSymbol or IParameterSymbol;
    }

    internal static bool AnyReferencedSymbolAssignedBeforeUse(
        SyntaxNode condition,
        SyntaxNode branchRoot,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var dependencySymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        return AnySymbolAssignedBeforeUse(
            dependencySymbols,
            branchRoot,
            useSpanStart,
            semanticModel,
            cancellationToken);
    }

    internal static bool AnySwitchStatementConditionSymbolAssignedBeforeUse(
        SwitchStatementSyntax switchStatement,
        SwitchSectionSyntax section,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return AnySymbolAssignedBeforeUse(
            SymbolicBranchCompletionStateTransfer.GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken),
            section,
            useSpanStart,
            semanticModel,
            cancellationToken);
    }

    internal static bool AnySwitchExpressionConditionSymbolAssignedBeforeUse(
        SwitchExpressionSyntax switchExpression,
        SwitchExpressionArmSyntax arm,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return AnySymbolAssignedBeforeUse(
            SymbolicBranchCompletionStateTransfer.GetSwitchExpressionConditionSymbols(switchExpression, semanticModel, cancellationToken),
            arm,
            useSpanStart,
            semanticModel,
            cancellationToken);
    }

    private static bool AnySymbolAssignedBeforeUse(
        IReadOnlyList<ISymbol> symbols,
        SyntaxNode branchRoot,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0) return false;
        var inventory = SymbolicMutationInventory.Create(branchRoot, semanticModel, cancellationToken);
        return symbols.Any(symbol =>
            inventory.MutatesBetween(branchRoot.SpanStart - 1, useSpanStart, symbol));
    }

    internal static bool IsSymbolAssignedBetween(
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicMutationInventory.Create(root, semanticModel, cancellationToken)
            .MutatesBetween(afterSpanStart, beforeSpanStart, symbol);
    }

    internal static IReadOnlyList<ISymbol> GetConditionDependencySymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        SymbolicBranchCompletionStateTransfer.AddReferencedSymbols(root, semanticModel, cancellationToken, symbols);
        SymbolicBranchCompletionStateTransfer.AddDeclaredPatternSymbols(root, semanticModel, cancellationToken, symbols);
        SymbolicBranchCompletionStateTransfer.AddMemberNotNullWhenTargetSymbols(root, semanticModel, cancellationToken, symbols);
        return symbols;
    }

}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
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

    private static void AddForLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var initializerBounds = EnumerateForLoopInitializerBoundTerms(
            forStatement,
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundStateFacts(
            ref state,
            forStatement,
            forStatement.Statement,
            initializerBounds,
            SymbolicRelationOperator.GreaterThanOrEqual,
            MonotonicDirection.NonDecreasing,
            "ir.path.for-loop-invariant.lower-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundStateFacts(
            ref state,
            forStatement,
            forStatement.Statement,
            initializerBounds,
            SymbolicRelationOperator.LessThanOrEqual,
            MonotonicDirection.NonIncreasing,
            "ir.path.for-loop-invariant.initial-upper-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundStateFacts(
            ref state,
            forStatement,
            forStatement.Statement,
            EnumerateForLoopStrictUpperBoundInitializerTerms(forStatement, semanticModel, cancellationToken),
            SymbolicRelationOperator.LessThan,
            MonotonicDirection.NonIncreasing,
            "ir.path.for-loop-invariant.strict-upper-bound",
            semanticModel,
            cancellationToken);
    }

    private static void AddPreLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        string provenancePrefix,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var initializerBounds = EnumeratePreLoopInitializerBoundTerms(
            loopStatement,
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundStateFacts(
            ref state,
            loopStatement,
            loopBody,
            initializerBounds,
            SymbolicRelationOperator.GreaterThanOrEqual,
            MonotonicDirection.NonDecreasing,
            provenancePrefix + ".lower-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundStateFacts(
            ref state,
            loopStatement,
            loopBody,
            initializerBounds,
            SymbolicRelationOperator.LessThanOrEqual,
            MonotonicDirection.NonIncreasing,
            provenancePrefix + ".initial-upper-bound",
            semanticModel,
            cancellationToken);
        AddLoopMonotonicBoundStateFacts(
            ref state,
            loopStatement,
            loopBody,
            EnumeratePreLoopStrictUpperBoundInitializerTerms(loopStatement, semanticModel, cancellationToken),
            SymbolicRelationOperator.LessThan,
            MonotonicDirection.NonIncreasing,
            provenancePrefix + ".strict-upper-bound",
            semanticModel,
            cancellationToken);
    }

    private static void AddLoopMonotonicBoundStateFacts(
        ref SymbolicState state,
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
                ? StatementMutatesSymbol(
                      loopBody,
                      initializer.Symbol,
                      semanticModel,
                      cancellationToken) ||
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
                      ForLoopIncrementorsInvalidateSymbolValue(
                          forStatement,
                          symbol,
                          semanticModel,
                          cancellationToken)
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

            AddRelationPathFact(
                ref state,
                relation,
                symbolTerm,
                initializer.Bound,
                loopStatement,
                provenance);
        }
    }

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumerateForLoopInitializerBoundTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken) =>
        EnumerateForLoopBoundTerms(
            forStatement,
            semanticModel,
            cancellationToken,
            TryLowerInitializerBoundTerm);

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm UpperBound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumerateForLoopStrictUpperBoundInitializerTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken) =>
        EnumerateForLoopBoundTerms(
            forStatement,
            semanticModel,
            cancellationToken,
            TryGetStrictUpperBoundInitializerTerm);

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumerateForLoopBoundTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            TryLowerLoopInitializerBound tryLowerBound)
    {
        if (forStatement.Declaration != null)
            foreach (var declarator in forStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                    !tryLowerBound(
                        declarator.Initializer.Value,
                        localSymbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var bound,
                        out var boundSymbols))
                    continue;

                yield return (localSymbol.OriginalDefinition, bound, boundSymbols);
            }

        foreach (var expression in forStatement.Initializers)
        {
            if (expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                symbol is not ILocalSymbol and not IParameterSymbol ||
                !tryLowerBound(
                    assignment.Right,
                    symbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken,
                    out var bound,
                    out var boundSymbols))
                continue;

            yield return (symbol.OriginalDefinition, bound, boundSymbols);
        }
    }

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumeratePreLoopInitializerBoundTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken) =>
        EnumeratePreLoopBoundTerms(
            loopStatement,
            semanticModel,
            cancellationToken,
            TryLowerInitializerBoundTerm);

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm UpperBound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumeratePreLoopStrictUpperBoundInitializerTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken) =>
        EnumeratePreLoopBoundTerms(
            loopStatement,
            semanticModel,
            cancellationToken,
            TryGetStrictUpperBoundInitializerTerm);

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumeratePreLoopBoundTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            TryLowerLoopInitializerBound tryLowerBound)
    {
        foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel,
                     cancellationToken))
            if (tryLowerBound(
                    initializer.Value,
                    initializer.Symbol,
                    semanticModel,
                    cancellationToken,
                    out var bound,
                    out var boundSymbols) &&
                !AnyPriorStatementsInvalidateInitializer(
                    initializer,
                    loopStatement,
                    boundSymbols,
                    semanticModel,
                    cancellationToken))
                yield return (initializer.Symbol, bound, boundSymbols);
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
        AddThrowGuardedExpressionStateFacts(
            ref state,
            expressionSyntax,
            foreachBody,
            semanticModel,
            cancellationToken);
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
        foreach (var condition in CollectLoopBodyInvariantState(loopStatement, semanticModel, cancellationToken).PathConditions)
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
        if (foreachStatement is not ForEachStatementSyntax forEachStatement ||
            semanticModel.GetDeclaredSymbol(forEachStatement, cancellationToken) is not ILocalSymbol iterationSymbol ||
            !TryCreateSymbolTerm(iterationSymbol.OriginalDefinition, out var iterationTerm))
            return;

        if (!SymbolicProgramPointFacts.TryGetFiniteElementExpressions(expressionSyntax, out var elementExpressions) &&
            !SymbolicProgramPointFacts.TryGetPriorAssignedFiniteElementExpressions(
                expressionSyntax,
                foreachStatement,
                semanticModel,
                cancellationToken,
                out elementExpressions))
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        SymbolicCondition? finiteDomain = null;
        var allReferenceElementsDefinitelyNonNull =
            SymbolicFactFactory.GetTrackedSymbolType(iterationSymbol.OriginalDefinition)?.IsReferenceType == true;
        foreach (var elementExpression in elementExpressions)
        {
            if (SymbolMutationFacts.ExpressionReferencesSymbol(
                    elementExpression,
                    iterationSymbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken))
                return;

            var lowering = SymbolicSemanticPipeline.LowerTerm(elementExpression, context);
            if (lowering is { IsExact: true, Value: { } elementTerm } &&
                CanCompareIrTerms(iterationTerm, elementTerm))
            {
                var elementCondition = (SymbolicCondition)new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(SymbolicRelationOperator.Equal, iterationTerm, elementTerm),
                    elementExpression,
                    "ir.path.foreach-entry.finite-domain"));
                finiteDomain = finiteDomain == null
                    ? elementCondition
                    : new SymbolicBinaryCondition(
                        SymbolicConditionOperator.Or,
                        finiteDomain,
                        elementCondition);
            }
            else if (!allReferenceElementsDefinitelyNonNull)
            {
                return;
            }

            allReferenceElementsDefinitelyNonNull =
                allReferenceElementsDefinitelyNonNull &&
                NullableFlowFacts.IsDefinitelyNotNullReferenceValue(elementExpression, semanticModel, cancellationToken);
        }

        if (finiteDomain != null) state = state.AddPathCondition(finiteDomain);

        if (allReferenceElementsDefinitelyNonNull && iterationTerm.Kind == SmtValueKind.Reference)
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                iterationTerm,
                new SymbolicNullTerm(),
                foreachStatement,
                "ir.path.foreach-entry.finite-domain-not-null");
    }

    internal static void AddThrowGuardedExpressionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax guardedStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string nonNullProvenance = "ir.path.foreach-entry.throw-guarded-not-null")
    {
        var throwGuardedValue = SymbolicAssignmentStateTransfer.GetThrowGuardedValue(expression);
        if (!throwGuardedValue.HasGuard) return;

        if (throwGuardedValue.GuardExpression != null)
        {
            if (!AnyConditionSymbolInvalidatedInStatement(throwGuardedValue.GuardExpression, guardedStatement, semanticModel,
                    cancellationToken))
                SymbolicProgramPointFacts.AddReachabilityCondition(ref state, throwGuardedValue.GuardExpression,
                    throwGuardedValue.GuardBranchWhenTrue, semanticModel,
                    cancellationToken);
        }
        else if (throwGuardedValue.RequiresNonNullValue &&
                 !ReferenceIdentityFactIsInvalidatedInStatement(
                     throwGuardedValue.EffectiveValueExpression,
                     guardedStatement,
                     semanticModel,
                     cancellationToken))
        {
            SymbolicProgramPointFacts.AddReferenceNullCondition(
                ref state,
                throwGuardedValue.EffectiveValueExpression,
                false,
                semanticModel,
                cancellationToken,
                nonNullProvenance);
        }
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
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
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
        if (forStatement.Declaration != null)
            SymbolicAssignmentStateTransfer.AddVariableDeclarationInitializerStateFacts(
                ref state,
                forStatement.Declaration,
                forStatement.Statement,
                semanticModel,
                cancellationToken,
                "ir.path.for-initializer");

        foreach (var initializer in forStatement.Initializers)
        {
            if (initializer is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;

            SymbolicStateInvalidator.InvalidateNestedAssignmentMutations(
                ref state,
                assignment,
                semanticModel,
                cancellationToken);
            var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                SymbolicAssignmentStateTransfer.AddAssignedValueStateFacts(
                    ref state,
                    assignedSymbol.OriginalDefinition,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    "ir.path.for-initializer");
        }

        return state.Normalize();
    }

    internal static SymbolicState CollectLoopBodyInvariantState(
        StatementSyntax loopStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var state = new SymbolicState();
        switch (loopStatement)
        {
            case ForStatementSyntax forStatement:
                AddForLoopBodyInvariantStateFacts(ref state, forStatement, semanticModel, cancellationToken);
                break;
            case WhileStatementSyntax whileStatement:
                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case DoStatementSyntax doStatement:
                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    doStatement,
                    doStatement.Statement,
                    "ir.path.do-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
        }

        return state.Normalize();
    }

    internal static SymbolicState CollectCompletedLoopExitInvariantState(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var state = new SymbolicState();
        SymbolicControlFlowCompletionStateTransfer.AddCompletedLoopStatementStateFacts(ref state, statement, semanticModel, cancellationToken);
        return state.Normalize();
    }

    private static bool ForLoopIncrementorsInvalidateSymbolValue(
        ForStatementSyntax forStatement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var incrementor in forStatement.Incrementors)
            if (NodeMutatesSymbol(incrementor, symbol, semanticModel, cancellationToken) ||
                SymbolicStateInvalidator.NodeMayMutateThroughReference(incrementor, symbol, semanticModel, cancellationToken))
                return true;

        return false;
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

        return headerExpression != null &&
               SyntaxNodeInvalidatesSymbolValue(headerExpression, symbol, semanticModel, cancellationToken);
    }

    private static bool SyntaxNodeInvalidatesSymbolValue(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            if (NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken) ||
                SymbolicStateInvalidator.NodeMayMutateThroughReference(node, symbol, semanticModel, cancellationToken))
                return true;

        return false;
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
        foreach (var node in loopBody.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (SymbolicStateInvalidator.NodeMayMutateThroughReference(node, symbol, semanticModel, cancellationToken)) return false;

            if (!NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken)) continue;

            if (node is not ExpressionSyntax expression ||
                !IncrementorPreservesBound(expression, symbol, direction, semanticModel, cancellationToken))
                return false;
        }

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

    private static bool IsCompatibleDelta(long delta, MonotonicDirection direction)
    {
        return direction == MonotonicDirection.NonDecreasing ? delta >= 0 : delta <= 0;
    }

    private static bool IsCompatibleSubtrahend(long subtrahend, MonotonicDirection direction)
    {
        return direction == MonotonicDirection.NonDecreasing ? subtrahend <= 0 : subtrahend >= 0;
    }

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
        if (conditionSymbols.Count == 0) return false;

        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (node is AssignmentExpressionSyntax tupleAssignment &&
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(tupleAssignment.Left) is TupleExpressionSyntax leftTuple &&
                leftTuple.Arguments.Any(argument =>
                    SymbolicAssignmentStateTransfer.ExpressionReferencesAnySymbol(argument.Expression, conditionSymbols, semanticModel,
                        cancellationToken)))
                return true;

            if (!SymbolMutationFacts.TryGetMutationTarget(node, out var mutatedExpression)) continue;

            var mutatedSymbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol
                ?.OriginalDefinition;
            if (mutatedSymbol != null &&
                conditionSymbols.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, mutatedSymbol)))
                return true;
        }

        return false;
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
            return StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken);

        return AnyConditionSymbolInvalidatedInStatement(
            expression,
            statement,
            semanticModel,
            cancellationToken);
    }

    internal static bool StatementMutatesSymbol(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (SymbolMutationFacts.TryGetMutationTarget(node, out var mutatedExpression) &&
                SymbolMutationFacts.ExpressionReferencesSymbol(mutatedExpression, symbol, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    internal static bool ExpressionMutatesAnySymbol(
        ExpressionSyntax expression,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0) return false;

        foreach (var node in expression.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            if (symbols.Any(symbol => NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken)))
                return true;

        return false;
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

        foreach (var symbol in symbols)
            if (IsSymbolAssignedBetween(
                    branchRoot,
                    branchRoot.SpanStart - 1,
                    useSpanStart,
                    symbol,
                    semanticModel,
                    cancellationToken))
                return true;

        return false;
    }

    internal static bool IsSymbolAssignedBetween(
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (node.SpanStart <= afterSpanStart || node.SpanStart >= beforeSpanStart) continue;

            if (NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken)) return true;
        }

        return false;
    }

    private static bool NodeMutatesSymbol(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolMutationFacts.TryGetMutationTarget(node, out var mutatedExpression) &&
               SymbolMutationFacts.ExpressionReferencesSymbol(mutatedExpression, symbol, semanticModel, cancellationToken);
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

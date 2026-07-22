using static SharpProof.Symbolic.Ir.SymbolicStatefulAssignmentTransfer;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicCfgProgramPointStateCollector {
    internal static bool TryCollectAbruptIfCompletionState(
        IfStatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicLoweringResult<SymbolicState> result) {
        var trueExits = SymbolicControlFlowFacts.StatementDefinitelyExits(statement.Statement, semanticModel, cancellationToken);
        var falseStatement = statement.Else?.Statement;
        var falseExits = falseStatement != null &&
                         SymbolicControlFlowFacts.StatementDefinitelyExits(falseStatement, semanticModel, cancellationToken);
        if (!trueExits && !falseExits) {
            result = null!;
            return false;
        }
        if (trueExits && falseExits) {
            result = Exact(SymbolicOperationTransferKernel.Complete(entryState, statement.Span).State, statement);
            return true;
        }
        var survivingStatement = trueExits ? falseStatement : statement.Statement;
        var transition = SymbolicReachabilityLowerer.ApplyCondition(
            entryState,
            statement.Condition,
            branchWhenTrue: !trueExits,
            semanticModel,
            cancellationToken);
        if (!transition.IsExact) {
            result = Unsupported(statement, "statement-region.if-abrupt-condition");
            return true;
        }
        var state = transition.State;
        if (survivingStatement != null) {
            SymbolicStatementStateTransfer.AddPriorStatementStateFacts(ref state, survivingStatement, semanticModel, cancellationToken);
            foreach (var hiddenSymbol in SymbolicBranchCompletionStateTransfer.GetLocalsDeclaredInside(
                         survivingStatement,
                         semanticModel,
                         cancellationToken))
                SymbolicStateInvalidator.InvalidateSymbol(ref state, hiddenSymbol, survivingStatement);
        }
        result = Exact(state, statement);
        return true;
    }
    private static bool TryCreateFinallyLocalTargetPlan(
        SyntaxNode site,
        SyntaxNode executionRoot,
        FinallyClauseSyntax finallyClause,
        ControlFlowGraph graph,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out CfgFinallyLocalTargetPlan? plan) {
        plan = null;
        var targetStatement = site.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (targetStatement == null ||
            !ReferenceEquals(targetStatement.Parent, finallyClause.Block) ||
            finallyClause.Parent is not TryStatementSyntax tryStatement ||
            !ReferenceEquals(tryStatement.Finally, finallyClause) ||
            tryStatement.Catches.Count != 0 ||
            CSharpSyntaxFacts.GetBlockBody(executionRoot) is not { } rootBlock ||
            !ReferenceEquals(tryStatement.Parent, rootBlock) ||
            !tryStatement.Block.Statements.All(statement => SupportsFinallyLinearStatement(statement, semanticModel, cancellationToken)) ||
            !finallyClause.Block.Statements.All(statement => SupportsFinallyLinearStatement(statement, semanticModel, cancellationToken)))
            return false;

        var regions = EnumerateRegions(graph.Root)
            .Where(region => region.Kind == ControlFlowRegionKind.Finally && RegionContainsSyntax(region, graph, finallyClause.Block))
            .ToArray();
        if (regions.Length != 1)
            return false;

        var protectedMutations = SymbolicStateInvalidator.LowerNestedMutations(tryStatement.Block, semanticModel, cancellationToken);
        if (protectedMutations.HasUnsupportedMutation)
            return false;

        plan = new CfgFinallyLocalTargetPlan(regions[0], protectedMutations);
        return true;
    }
    private static bool SupportsFinallyLinearStatement(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (statement is LocalDeclarationStatementSyntax declaration &&
            declaration.UsingKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) &&
            declaration.AwaitKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)) {
            return declaration.Declaration.Variables.All(variable =>
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol { RefKind: RefKind.None } &&
                (variable.Initializer == null ||
                 SupportsFinallyLinearValue(variable.Initializer.Value, semanticModel, cancellationToken)));
        }
        if (statement is not ExpressionStatementSyntax {
            Expression: AssignmentExpressionSyntax assignment
        } ||
            !assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) ||
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(assignment.Left) is not IdentifierNameSyntax left ||
            semanticModel.GetSymbolInfo(left, cancellationToken).Symbol is not
                (ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }))
            return false;

        return SupportsFinallyLinearValue(assignment.Right, semanticModel, cancellationToken);
    }
    private static bool SupportsFinallyLinearValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is LiteralExpressionSyntax)
            return true;
        if (expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not
                (ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }))
            return false;

        var typeInfo = semanticModel.GetTypeInfo(identifier, cancellationToken);
        return typeInfo.Type != null &&
               SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType);
    }
    internal static IEnumerable<ControlFlowRegion> EnumerateRegions(ControlFlowRegion region) {
        yield return region;
        foreach (var nested in region.NestedRegions)
            foreach (var descendant in EnumerateRegions(nested))
                yield return descendant;
    }
    internal static bool RegionContainsSyntax(ControlFlowRegion region, ControlFlowGraph graph, SyntaxNode syntax) {
        for (var ordinal = region.FirstBlockOrdinal; ordinal <= region.LastBlockOrdinal; ordinal++) {
            var block = graph.Blocks[ordinal];
            if (block.Operations.Any(operation => syntax.Span.Contains(operation.Syntax.SpanStart)) ||
                block.BranchValue != null && syntax.Span.Contains(block.BranchValue.Syntax.SpanStart))
                return true;
        }
        return false;
    }
    private static bool IsSupportedFinallyLocalContinuation(CfgFinallyContinuation continuation, CfgFinallyLocalTargetPlan plan) =>
        continuation.Regions.Length == 1 &&
        continuation.RegionIndex == 0 &&
        ReferenceEquals(continuation.Regions[0], plan.Region) &&
        continuation.Parent == null &&
        continuation.TerminalBranch == null;

    private static bool TryObserveFinallyLocalTarget(
        CfgFinallyContinuation? continuation,
        CfgFinallyLocalTargetPlan plan,
        ref CfgFinallyContinuation? observed) {
        if (continuation == null || !IsSupportedFinallyLocalContinuation(continuation, plan))
            return false;
        if (observed == null) {
            observed = continuation;
            return true;
        }
        return observed == continuation;
    }
    internal readonly record struct CfgTraversalPoint(BasicBlock Block, CfgFinallyContinuation? Continuation, int OperationIndex = 0);

    internal sealed record CfgFinallyLocalTargetPlan(ControlFlowRegion Region, SymbolicNestedMutationInvalidationPlan ProtectedMutations);

    internal sealed record CfgCatchLocalTargetPlan(
        CatchClauseSyntax Clause,
        ControlFlowRegion TryRegion,
        ControlFlowRegion CatchRegion,
        SymbolicNestedMutationInvalidationPlan ProtectedMutations);

    private static bool TryCreateCatchLocalTargetPlan(
        SyntaxNode site,
        ControlFlowGraph graph,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out CfgCatchLocalTargetPlan? plan) {
        plan = null;
        var clause = site.AncestorsAndSelf().OfType<CatchClauseSyntax>().FirstOrDefault();
        if (clause == null)
            return true;
        if (clause.Parent is not TryStatementSyntax tryStatement)
            return false;

        var catchRegions = EnumerateRegions(graph.Root)
            .Where(region => region.Kind == ControlFlowRegionKind.Catch && RegionContainsSyntax(region, graph, clause.Block))
            .ToArray();
        if (catchRegions.Length != 1)
            return false;
        var tryAndCatchRegion = catchRegions[0].EnclosingRegion;
        while (tryAndCatchRegion?.Kind != ControlFlowRegionKind.TryAndCatch)
            tryAndCatchRegion = tryAndCatchRegion?.EnclosingRegion;
        var tryRegion = tryAndCatchRegion?.NestedRegions
            .FirstOrDefault(static region => region.Kind == ControlFlowRegionKind.Try);
        if (tryRegion == null)
            return false;

        var protectedMutations = SymbolicStateInvalidator.LowerNestedMutations(tryStatement.Block, semanticModel, cancellationToken);
        plan = new CfgCatchLocalTargetPlan(clause, tryRegion, catchRegions[0], protectedMutations);
        return true;
    }
    private static bool IsCatchLocalInitialization(
        IOperation operation,
        CatchClauseSyntax clause,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (clause.Declaration == null ||
            !clause.Declaration.Span.Contains(operation.Syntax.SpanStart) ||
            operation is not ISimpleAssignmentOperation assignment ||
            !TryGetDirectTarget(assignment.Target, out var target))
            return false;
        return SymbolEqualityComparer.Default.Equals(target, semanticModel.GetDeclaredSymbol(clause.Declaration, cancellationToken));
    }
    internal static void ApplyCatchEntryFacts(
        ref SymbolicState state,
        CatchClauseSyntax clause,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (clause.Declaration != null &&
            semanticModel.GetDeclaredSymbol(clause.Declaration, cancellationToken) is ILocalSymbol localSymbol &&
            !SymbolicLoopStateTransfer.IsSymbolAssignedBetween(
                clause.Block,
                clause.Block.SpanStart - 1,
                useSpanStart,
                localSymbol.OriginalDefinition,
                semanticModel,
                cancellationToken))
            SymbolicStateFactBuilder.AddSymbolReferenceNullCondition(
                ref state,
                localSymbol.OriginalDefinition,
                clause.Declaration,
                false,
                "ir.path.catch-entry.exception-not-null");

        if (clause.Filter?.FilterExpression is { } filterExpression &&
            !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                filterExpression,
                clause.Block,
                useSpanStart,
                semanticModel,
                cancellationToken))
            SymbolicProgramPointFacts.AddReachabilityCondition(ref state, filterExpression, true, semanticModel, cancellationToken);
    }
    internal sealed record CfgFinallyContinuation(
        ControlFlowBranch OriginBranch,
        ImmutableArray<ControlFlowRegion> Regions,
        int RegionIndex,
        BasicBlock? Destination,
        ControlFlowBranch? TerminalBranch,
        CfgFinallyContinuation? Parent);

    internal static bool TryApplyPriorStatementCompletion(
        ref SymbolicState state,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (statement is LocalDeclarationStatementSyntax declaration)
            return TryApplyCurrentDeclarationCompletion(
                ref state,
                declaration,
                guard: null,
                allowGuardedReferenceAssignments: true,
                semanticModel,
                cancellationToken);
        if (semanticModel.GetOperation(statement, cancellationToken) is not { } operation)
            return false;
        var nextState = state;
        if (!TryApplyOperation(
                ref nextState, operation,
                guard: null,
                allowGuardedReferenceAssignments: true,
                allowGuardMutation: true,
                allowExpressionStatementCompletion: true,
                semanticModel,
                cancellationToken,
                "ir.path.prior-statement",
                out _)) {
            if (statement is not ExpressionStatementSyntax {
                Expression: AssignmentExpressionSyntax
            })
                return false;
            nextState = state;
            SymbolicStateInvalidator.InvalidateNestedMutations(ref nextState, statement, semanticModel, cancellationToken);
        }
        AddOperationNormalCompletionFacts(ref nextState, operation, semanticModel, cancellationToken);
        state = nextState;
        return true;
    }
    internal static bool TryApplyOperation(
        ref SymbolicState state,
        IOperation operation,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        bool allowExpressionStatementCompletion,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string assignmentProvenance,
        out ISymbol? invalidatedGuardTarget) {
        invalidatedGuardTarget = null;
        if (operation is IVariableDeclarationGroupOperation declarations) {
            foreach (var declarator in declarations.Declarations
                         .SelectMany(static declaration => declaration.Declarators)) {
                if (declarator.Initializer?.Value is not { } value)
                    continue;
                if (!TryApplyAssignment(
                        ref state,
                        declarator.Symbol,
                        value,
                        guard,
                        allowGuardedReferenceAssignments,
                        allowGuardMutation,
                        semanticModel,
                        cancellationToken,
                        assignmentProvenance,
                        out var declaratorInvalidatedGuardTarget))
                    return false;
                invalidatedGuardTarget ??= declaratorInvalidatedGuardTarget;
            }
            return true;
        }
        if (allowExpressionStatementCompletion &&
            operation.DescendantsAndSelf().OfType<IFlowCaptureReferenceOperation>().Any() &&
            operation.Syntax.FirstAncestorOrSelf<StatementSyntax>() is { } capturedStatement &&
            capturedStatement is ExpressionStatementSyntax or LocalDeclarationStatementSyntax &&
            semanticModel.GetOperation(capturedStatement, cancellationToken) is { } sourceOperation &&
            !sourceOperation.DescendantsAndSelf().OfType<IFlowCaptureReferenceOperation>().Any())
            return TryApplyOperation(
                ref state,
                sourceOperation,
                guard,
                allowGuardedReferenceAssignments,
                allowGuardMutation,
                allowExpressionStatementCompletion,
                semanticModel,
                cancellationToken,
                assignmentProvenance,
                out invalidatedGuardTarget);

        var expressionOperation = operation is IExpressionStatementOperation expressionStatement
            ? expressionStatement.Operation
            : operation;
        if (expressionOperation is IDeconstructionAssignmentOperation deconstruction)
            return TryApplyDeconstructionAssignment(
                ref state,
                deconstruction,
                guard,
                semanticModel,
                cancellationToken,
                out invalidatedGuardTarget);

        if (expressionOperation is ICoalesceAssignmentOperation coalesce)
            return TryApplyCoalesceAssignment(
                ref state,
                coalesce,
                guard,
                allowGuardedReferenceAssignments,
                allowGuardMutation,
                semanticModel,
                cancellationToken,
                out invalidatedGuardTarget);

        if (expressionOperation is ISimpleAssignmentOperation assignment)
            return TryGetDirectTarget(assignment.Target, out var target)
                ? TryApplyAssignment(
                    ref state,
                    target,
                    assignment.Value,
                    guard,
                    allowGuardedReferenceAssignments,
                    allowGuardMutation,
                    semanticModel,
                    cancellationToken,
                    assignmentProvenance,
                    out invalidatedGuardTarget)
                : TryApplyExplicitTargetAssignment(
                    ref state,
                    assignment,
                    guard,
                    semanticModel,
                    cancellationToken,
                    out invalidatedGuardTarget);

        var computedUpdate = expressionOperation is IIncrementOrDecrementOperation or ICompoundAssignmentOperation
            ? expressionOperation
            : null;
        var computedTarget = computedUpdate switch {
            IIncrementOrDecrementOperation increment => increment.Target,
            ICompoundAssignmentOperation compound => compound.Target,
            _ => null
        };
        if (computedUpdate != null) {
            if (computedTarget == null ||
                !TryGetDirectTarget(computedTarget, out var computedTargetSymbol) ||
                computedUpdate.Syntax is not ExpressionSyntax expression)
                return false;
            if (!TryApplyComputedUpdate(ref state, computedTargetSymbol, computedUpdate, semanticModel, cancellationToken)) {
                if (guard != null) return false;
                SymbolicStateInvalidator.InvalidateSymbol(ref state, computedTargetSymbol, expression);
                SymbolicStateInvalidator.InvalidateNestedMutations(ref state, expression, semanticModel, cancellationToken);
            }
            if (GuardReferencesTarget(guard, computedTargetSymbol))
                invalidatedGuardTarget = computedTargetSymbol;
            return true;
        }
        if (allowExpressionStatementCompletion &&
            operation is IExpressionStatementOperation
            expressionStatementOperation)
            return TryApplyExpressionStatement(ref state, expressionStatementOperation, guard, semanticModel, cancellationToken);

        return false;
    }
    private static bool TryApplyExpressionStatement(
        ref SymbolicState state,
        IExpressionStatementOperation operation,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (operation.Syntax is not ExpressionStatementSyntax expressionStatement)
            return false;
        var invalidations = SymbolicStateInvalidator.LowerNestedMutations(expressionStatement, semanticModel, cancellationToken);
        if (guard != null &&
            (invalidations.HasUnsupportedMutation || invalidations.Steps
                .SelectMany(static step => step.Targets)
                .Any(target => SymbolicIrReferenceScanner.ContainsVariableOrMember(guard, target.Key))))
            return false;
        state = SymbolicStateInvalidator.ApplyNestedMutationInvalidations(state, invalidations);
        state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
            state,
            expressionStatement.Expression,
            expressionStatement,
            includeThrowGuardFacts: true,
            semanticModel,
            cancellationToken).State;
        if (SymbolicControlFlowFacts.ExpressionStatementDefinitelyExits(expressionStatement, semanticModel, cancellationToken))
            state = state.MarkContradictory();
        return true;
    }
    private static bool TryApplyCurrentCompletion(
        ref SymbolicState state,
        SyntaxNode site,
        IOperation operation,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (site is ExpressionStatementSyntax capturedStatement &&
            operation is IExpressionStatementOperation &&
            semanticModel.GetOperation(capturedStatement.Expression, cancellationToken) is { } sourceOperation)
            operation = sourceOperation;
        if (site is ExpressionSyntax expression &&
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression) is not AssignmentExpressionSyntax)
            return TryApplyFrameworkExpressionCompletion(ref state, expression, semanticModel, cancellationToken);

        var completedState = state;
        if (!TryApplyOperation(
                ref completedState,
                operation,
                guard,
                allowGuardedReferenceAssignments,
                allowGuardedReferenceAssignments,
                false,
                semanticModel,
                cancellationToken,
                "ir.path.prior-statement",
                out var invalidatedGuardTarget) ||
            invalidatedGuardTarget != null && !allowGuardedReferenceAssignments)
            return false;

        state = completedState;

        if (site is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } statement)
            state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
                state,
                assignment.Right,
                statement,
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is
                    not (ILocalSymbol or IParameterSymbol),
                semanticModel,
                cancellationToken).State;
        else if (site is BlockSyntax)
            AddOperationNormalCompletionFacts(ref state, operation, semanticModel, cancellationToken);

        return true;
    }
    private static bool TryApplyFrameworkExpressionCompletion(
        ref SymbolicState state,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var lowering = SymbolicFrameworkPostconditionLowerer.LowerMemberNotNull(expression, semanticModel, cancellationToken);
        if (lowering is not { IsExact: true, Value: { } plan })
            return false;
        SymbolicStateInvalidator.InvalidateNestedMutations(ref state, expression, semanticModel, cancellationToken);
        state = SymbolicSourceCompletionLowerer.ApplyConditions(
            state,
            plan.AfterDoesNotReturnIf,
            expression,
            "ir.path.expression-completion.member-not-null").State;
        return true;
    }
    internal static void AddOperationNormalCompletionFacts(
        ref SymbolicState state,
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (operation.Syntax.FirstAncestorOrSelf<ExpressionStatementSyntax>() is not
            { Expression: AssignmentExpressionSyntax assignment } statement)
            return;
        var target = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
            state,
            assignment.Right,
            statement,
            target is not (ILocalSymbol or IParameterSymbol),
            semanticModel,
            cancellationToken).State;
    }
    private static bool IsForInitializerSyntax(SyntaxNode syntax, ForStatementSyntax forStatement) =>
        forStatement.Declaration?.Variables.Any(variable => variable.Span.Contains(syntax.SpanStart)) == true ||
        forStatement.Initializers.Any(initializer => initializer.Span.Contains(syntax.SpanStart));

    private static bool TryApplyForInitializers(
        ref SymbolicState state,
        ForStatementSyntax statement,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var variable in statement.Declaration?.Variables ?? default) {
            if (variable is not { Initializer.Value: { } value } ||
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not { } target ||
                semanticModel.GetOperation(value, cancellationToken) is not { } valueOperation ||
                !TryApplyAssignment(
                    ref state,
                    target,
                    valueOperation,
                    guard,
                    true,
                    false,
                    semanticModel,
                    cancellationToken,
                    "ir.path.for-initializer",
                    out var invalidatedGuardTarget) ||
                invalidatedGuardTarget != null)
                return false;
            state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
                state,
                value,
                statement.Statement,
                includeThrowGuardFacts: false,
                semanticModel,
                cancellationToken).State;
        }
        foreach (var initializer in statement.Initializers) {
            if (semanticModel.GetOperation(initializer, cancellationToken) is not { } operation ||
                !TryApplyOperation(
                    ref state,
                    operation,
                    guard,
                    allowGuardedReferenceAssignments: true,
                    allowGuardMutation: false,
                    allowExpressionStatementCompletion: false,
                    semanticModel,
                    cancellationToken,
                    "ir.path.for-initializer",
                    out var invalidatedGuardTarget) ||
                invalidatedGuardTarget != null)
                return false;
        }
        return true;
    }
    private static bool TryApplyCurrentDeclarationCompletion(
        ref SymbolicState state,
        LocalDeclarationStatementSyntax declaration,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var completedState = RemoveMatchingThrowGuard(state, declaration, guard, semanticModel, cancellationToken);
        foreach (var declarator in declaration.Declaration.Variables) {
            if (declarator.Initializer is not { } initializer ||
                semanticModel.GetOperation(declarator, cancellationToken) is not
                    IVariableDeclaratorOperation {
                        Symbol: var declaratorSymbol,
                        Initializer.Value: { } value
                    })
                return false;
            SymbolicStateInvalidator.InvalidateNestedMutations(ref completedState, value.Syntax, semanticModel, cancellationToken);
            if (!TryApplyAssignment(
                    ref completedState,
                    declaratorSymbol,
                    value,
                    guard,
                    allowGuardedReferenceAssignments,
                    allowGuardMutation: false,
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement",
                    out _)) {
                if (guard != null)
                    return false;
                completedState = SymbolicStateValueFacts.RemoveReferences(completedState, declaratorSymbol);
            }
            completedState = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
                completedState,
                initializer.Value,
                declaration,
                false,
                semanticModel,
                cancellationToken).State;
        }
        state = completedState;
        return true;
    }
    private static SymbolicState RemoveMatchingThrowGuard(
        SymbolicState state,
        LocalDeclarationStatementSyntax declaration,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (guard == null) return state;

        var guardKey = SymbolicState.CreateProofConditionKey(guard);
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        foreach (var variable in declaration.Declaration.Variables) {
            if (variable.Initializer is not { Value: { } value } ||
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not { } target)
                continue;
            var completion = SymbolicOperationLowerer.LowerThrowGuardedAssignmentPostcondition(
                target,
                SymbolicAssignmentStateTransfer.GetThrowGuardedValue(value),
                context,
                "ir.path.prior-statement");
            if (completion == null ||
                SymbolicState.CreateProofConditionKey(completion) != guardKey)
                continue;

            return new SymbolicState(
                state.Facts,
                state.PathConditions.Where(condition => SymbolicState.CreateProofConditionKey(condition) != guardKey),
                state.SymbolVersions,
                state.IsContradictory);
        }
        return state;
    }
    internal static bool TryApplyAssignment(
        ref SymbolicState state,
        ISymbol target,
        IOperation value,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out ISymbol? invalidatedGuardTarget) {
        if (RequiresStructuralAssignmentFallback(target, guard, allowGuardedReferenceAssignments, allowGuardMutation)) {
            invalidatedGuardTarget = null;
            return false;
        }
        invalidatedGuardTarget = GuardReferencesTarget(guard, target) ? target : null;
        if (value.Syntax is not ExpressionSyntax expression)
            return false;

        SymbolicTerm? previousValue = null;
        var isSelfReferential = SymbolMutationFacts.ExpressionReferencesSymbol(expression, target, semanticModel, cancellationToken);
        if (isSelfReferential &&
            (!SymbolicStateValueFacts.TryGetCurrentValue(state, target, out previousValue) ||
             previousValue.Kind != SharpProof.ProofCore.Smt.SmtValueKind.Int)) {
            ApplyUnsupportedSelfReferentialCompletion(ref state, target, expression, semanticModel, cancellationToken, provenance);
            return true;
        }
        var transition = SymbolicOperationTransfer.ApplyAssignment(
            state,
            target,
            expression,
            semanticModel,
            cancellationToken,
            provenance: provenance,
            bindingProvenance: provenance + ".assigned-value",
            asExpressionProvenanceRoot: provenance + ".as",
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic,
            preInvalidationTargetValue: previousValue);
        if (!transition.IsExact)
            return false;

        state = transition.State;
        return true;
    }
    private static void ApplyUnsupportedSelfReferentialCompletion(
        ref SymbolicState state,
        ISymbol target,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) {
        state = SymbolicStateValueFacts.RemoveReferences(state, target);
        var condition = SymbolicOperationLowerer.LowerThrowGuardedAssignmentPostcondition(
            target,
            SymbolicAssignmentStateTransfer.GetThrowGuardedValue(expression),
            new SymbolicLoweringContext(semanticModel, cancellationToken),
            provenance);
        if (condition != null)
            state = SymbolicOperationTransferKernel.Assume(state, condition, assumeTrue: true, expression.Span, provenance).State;
    }
    private static bool TryApplyExplicitTargetAssignment(
        ref SymbolicState state,
        ISimpleAssignmentOperation assignment,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? invalidatedGuardTarget) {
        invalidatedGuardTarget = null;
        if (guard != null || assignment.Syntax is not AssignmentExpressionSyntax syntax)
            return false;

        SymbolicStateInvalidator.InvalidateMutationTarget(ref state, syntax.Left, semanticModel, cancellationToken);
        SymbolicStateInvalidator.InvalidateNestedAssignmentMutations(ref state, syntax, semanticModel, cancellationToken);
        var transition = SymbolicOperationTransfer.ApplyLowering(
            state,
            SymbolicOperationLowerer.LowerExplicitTargetAssignment(syntax, new SymbolicLoweringContext(semanticModel, cancellationToken)));
        if (!transition.IsExact)
            return false;

        state = transition.State;
        return true;
    }
    internal static bool RequiresStructuralAssignmentFallback(
        ISymbol target,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation) {
        var type = target switch {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };
        return guard != null &&
            type?.IsReferenceType == true &&
            (!allowGuardedReferenceAssignments ||
             !allowGuardMutation && GuardReferencesTarget(guard, target));
    }
    internal static bool GuardReferencesTarget(SymbolicCondition? guard, ISymbol target) =>
        guard != null &&
        SymbolicIrReferenceScanner.ContainsVariableOrMember(guard, SymbolicFactFactory.GetSmtVariableName(target));

    internal static bool TryGetDirectTarget(IOperation operation, out ISymbol target) {
        target = operation switch {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null!
        };
        return target != null;
    }
    private static bool ContainsSite(SyntaxNode container, SyntaxNode site) =>
        container.Span.Contains(site.SpanStart) || site.Span.Contains(container.SpanStart);

    private static bool TryGetForInitialEntryHeader(ControlFlowGraph graph, ForStatementSyntax forStatement, out BasicBlock header) {
        var matches = graph.Blocks.Where(block =>
            block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null &&
            ContainsSite(block.BranchValue.Syntax, forStatement.Condition!)).ToArray();
        if (matches.Length != 1) {
            header = null!;
            return false;
        }
        header = matches[0];
        return true;
    }
    internal static bool TryCreateRegionPlan(
        ControlFlowGraph graph,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out CfgRegionPlan plan,
        out string failure) {
        failure = GetStatementShapeFailure(graph, statement, semanticModel, cancellationToken) ?? string.Empty;
        if (failure.Length != 0)
            return Fail(failure, out plan, out failure);

        var directSlices = new Dictionary<int, CfgBlockSlice>();
        foreach (var block in graph.Blocks) {
            if (!block.IsReachable)
                continue;
            var ownedOperationIndexes = block.Operations
                .Select((operation, index) => (operation, index))
                .Where(candidate => statement.Span.Contains(candidate.operation.Syntax.SpanStart))
                .Where(candidate => statement is not ForStatementSyntax forStatement ||
                    !IsForInitializerSyntax(candidate.operation.Syntax, forStatement))
                .Select(static candidate => candidate.index)
                .ToArray();
            var ownsBranchValue = block.BranchValue != null &&
                                  statement.Span.Contains(block.BranchValue.Syntax.SpanStart);
            if (ownedOperationIndexes.Length == 0 && !ownsBranchValue)
                continue;

            var firstOperation = ownedOperationIndexes.Length == 0
                ? block.Operations.Length
                : ownedOperationIndexes[0];
            var endOperation = ownedOperationIndexes.Length == 0
                ? firstOperation
                : ownedOperationIndexes[ownedOperationIndexes.Length - 1] + 1;
            if (!ownedOperationIndexes.SequenceEqual(Enumerable.Range(firstOperation, ownedOperationIndexes.Length)))
                return Fail("statement-region.operation-slice", out plan, out failure);

            directSlices.Add(
                block.Ordinal,
                new CfgBlockSlice(
                    firstOperation,
                    endOperation,
                    endOperation < block.Operations.Length ||
                    !ownsBranchValue && block.BranchValue != null));
        }
        if (directSlices.Count == 0)
            return Fail("statement-region.empty", out plan, out failure);

        var connectorCandidates = new HashSet<int>(graph.Blocks
            .Where(static block => block.Kind == BasicBlockKind.Block && block.Operations.IsDefaultOrEmpty && block.BranchValue == null)
            .Select(static block => block.Ordinal));
        var forwardConnectors = CollectConnectorClosure(graph, directSlices.Keys, connectorCandidates, forward: true);
        forwardConnectors.IntersectWith(CollectConnectorClosure(graph, directSlices.Keys, connectorCandidates, forward: false));

        var slices = new Dictionary<int, CfgBlockSlice>(directSlices);
        foreach (var ordinal in forwardConnectors)
            slices.Add(ordinal, new CfgBlockSlice(0, 0, false));

        var entryPoints = new HashSet<CfgTraversalPoint>();
        foreach (var entry in slices) {
            var ordinal = entry.Key;
            var slice = entry.Value;
            var block = graph.Blocks[ordinal];
            if (slice.FirstOperationIndex != 0 ||
                block.Predecessors.Any(predecessor => !slices.ContainsKey(predecessor.Source.Ordinal)))
                entryPoints.Add(new CfgTraversalPoint(block, null, slice.FirstOperationIndex));
        }
        if (entryPoints.Count != 1)
            return Fail("statement-region.entry", out plan, out failure);
        var entryPoint = entryPoints.Single();
        var reachableSlices = CollectConnectorClosure(
            graph,
            new[] { entryPoint.Block.Ordinal },
            new HashSet<int>(slices.Keys),
            forward: true);
        reachableSlices.Add(entryPoint.Block.Ordinal);
        if (!reachableSlices.SetEquals(slices.Keys))
            return Fail("statement-region.disconnected", out plan, out failure);

        failure = GetRegionEdgeFailure(graph, slices, directSlices.Keys) ?? string.Empty;
        if (failure.Length != 0)
            return Fail(failure, out plan, out failure);

        plan = new CfgRegionPlan(statement, entryPoint, slices);
        failure = string.Empty;
        return true;
    }
    private static string? GetRegionEdgeFailure(
        ControlFlowGraph graph,
        IReadOnlyDictionary<int, CfgBlockSlice> slices,
        IEnumerable<int> directOrdinals) {
        var direct = new HashSet<int>(directOrdinals);
        var hasExit = slices.Values.Any(static slice => slice.HasCursorExit);
        foreach (var entry in slices) {
            var ordinal = entry.Key;
            var slice = entry.Value;
            if (slice.HasCursorExit)
                continue;
            var block = graph.Blocks[ordinal];
            if (!direct.Contains(ordinal) &&
                (!block.Operations.IsDefaultOrEmpty || block.BranchValue != null))
                return "statement-region.connector";
            foreach (var branch in GetSuccessors(block)) {
                if (branch.FinallyRegions.Any(region => Enumerable.Range(
                        region.FirstBlockOrdinal,
                        region.LastBlockOrdinal - region.FirstBlockOrdinal + 1)
                    .Any(candidate => !slices.ContainsKey(candidate))))
                    return "statement-region.finally-ownership";
                if (IsTerminalCompletionBranch(branch)) {
                    hasExit = true;
                    continue;
                }
                if (branch.Semantics is not (ControlFlowBranchSemantics.Regular or
                    ControlFlowBranchSemantics.StructuredExceptionHandling))
                    return "statement-region.exit";
                if (branch.Destination == null) {
                    if (!IsWithinRegion(block, ControlFlowRegionKind.Finally))
                        return "statement-region.exit";
                    continue;
                }
                hasExit |= !slices.ContainsKey(branch.Destination.Ordinal);
            }
        }
        return hasExit ? null : "statement-region.exit";
    }
    private static SymbolicLoweringResult<SymbolicState> CollectProtocolCompletionState(
        StatementSyntax statement,
        SymbolicState entryState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var (expression, body, provenance) = statement switch {
            ForEachStatementSyntax loop =>
                (loop.Expression, loop.Statement, "ir.path.foreach-completion.not-null"),
            ForEachVariableStatementSyntax loop =>
                (loop.Expression, loop.Statement, "ir.path.foreach-completion.not-null"),
            LockStatementSyntax lockStatement =>
                (lockStatement.Expression, lockStatement.Statement, "ir.path.lock-completion.not-null"),
            _ => (null!, null!, string.Empty)
        };
        if (expression == null)
            return Unsupported(statement, "statement-region.protocol-kind");

        var state = entryState;
        SymbolicStateInvalidator.InvalidateNestedMutations(ref state, statement, semanticModel, cancellationToken);
        if (SymbolicLoopStateTransfer.IsLocalOrParameterReference(expression, semanticModel, cancellationToken) &&
            !SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(expression, body, semanticModel, cancellationToken))
            SymbolicProgramPointFacts.AddReferenceNullCondition(ref state, expression, false, semanticModel, cancellationToken, provenance);

        return Exact(state, statement);
    }
    internal static bool TryApplyCompletedSwitchExitExclusions(
        ref SymbolicState state,
        SwitchStatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var exitingSections = statement.Sections.Where(section =>
            section.Statements.LastOrDefault() is { } last &&
            SymbolicControlFlowFacts.UnwrapSingleStatementBlock(last) is not BreakStatementSyntax &&
            SymbolicControlFlowFacts.StatementDefinitelyExits(last, semanticModel, cancellationToken)).ToArray();
        if (exitingSections.Length == 0)
            return true;

        var conditionSymbols = SymbolicBranchCompletionStateTransfer.GetSwitchConditionSymbols(statement, semanticModel, cancellationToken);
        if (statement.Sections.Except(exitingSections)
            .SelectMany(static section => section.Statements)
            .Any(sectionStatement => conditionSymbols.Any(symbol =>
                SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(sectionStatement, symbol, semanticModel, cancellationToken))))
            return true;

        foreach (var section in exitingSections) {
            if (!SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                    statement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var condition))
                return false;
            var transition = SymbolicOperationTransferKernel.Assume(
                state,
                condition,
                assumeTrue: false,
                section.Span,
                "cfg-program-point.switch-exit-exclusion");
            if (!transition.IsExact)
                return false;
            state = transition.State;
        }
        return true;
    }
    private static string? GetStatementShapeFailure(
        ControlFlowGraph graph,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var flowCaptureIds = new HashSet<CaptureId>();
        var operations = graph.Blocks.SelectMany(static block => block.Operations).ToArray();
        var captures = operations
            .OfType<IFlowCaptureOperation>()
            .Where(capture => statement.Span.Contains(capture.Syntax.SpanStart))
            .ToArray();
        IEnumerable<SwitchStatementSyntax> switches;
        if (statement is SwitchStatementSyntax switchStatement) {
            if (switchStatement.DescendantNodes().OfType<SwitchStatementSyntax>().Any())
                return "statement-region.switch-nested";
            if (switchStatement.Sections.Any(static section => section.Labels.Count != 1))
                return "statement-region.switch-multi-label";
            if (HasUnsupportedAbruptTransfer(switchStatement, allowBreak: true))
                return "statement-region.switch-abrupt";
            switches = new[] { switchStatement };
        }
        else {
            if (statement is IfStatementSyntax && HasUnsupportedAbruptTransfer(statement, allowBreak: false))
                return "statement-region.if-abrupt";
            if (statement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax &&
                statement.DescendantNodes().Any(static node => node is GotoStatementSyntax or YieldStatementSyntax))
                return "statement-region.loop-abrupt";
            switches = statement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                ? statement.DescendantNodes().OfType<SwitchStatementSyntax>()
                : Array.Empty<SwitchStatementSyntax>();
        }
        foreach (var nestedSwitch in switches) {
            var governingValue = UnwrapConversion(semanticModel.GetOperation(nestedSwitch.Expression, cancellationToken));
            var switchCaptures = captures.Where(capture => nestedSwitch.Span.Contains(capture.Syntax.SpanStart)).ToArray();
            if (governingValue is not (ILocalReferenceOperation or
                    IParameterReferenceOperation or ILiteralOperation))
                return "statement-region.switch-governing-value";
            if (switchCaptures.Length != 1 ||
                UnwrapConversion(switchCaptures[0].Value) is not (ILocalReferenceOperation or
                    IParameterReferenceOperation or ILiteralOperation))
                return "statement-region.switch-capture-shape";
            if (nestedSwitch.Sections.SelectMany(static section => section.Labels)
                .Where(static label => label is not DefaultSwitchLabelSyntax)
                .Any(label => !SwitchPathConditionBuilder.TryCreateSwitchStatementLabelSymbolicCondition(
                    nestedSwitch.Expression,
                    label,
                    semanticModel,
                    cancellationToken,
                    out _)))
                return "statement-region.switch-label-shape";
            flowCaptureIds.Add(switchCaptures[0].Id);
        }
        var expressionOwners = operations
            .OfType<IExpressionStatementOperation>()
            .Where(operation => statement.Span.Contains(operation.Syntax.SpanStart))
            .GroupBy(static operation => operation.Syntax)
            .ToDictionary(static group => group.Key, static group => group.Count());
        foreach (var capture in captures) {
            if (flowCaptureIds.Contains(capture.Id))
                continue;
            var owner = capture.Syntax.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            if (owner == null ||
                !expressionOwners.TryGetValue(owner, out var ownerCount) ||
                ownerCount != 1)
                return "statement-region.flow-capture";
            flowCaptureIds.Add(capture.Id);
        }
        return null;
    }
    private static IOperation? UnwrapConversion(IOperation? operation) =>
        operation is IConversionOperation conversion
            ? UnwrapConversion(conversion.Operand)
            : operation;

    private static bool HasUnsupportedAbruptTransfer(StatementSyntax statement, bool allowBreak) =>
        statement.DescendantNodes().Any(node =>
            node is GotoStatementSyntax or ContinueStatementSyntax or YieldStatementSyntax ||
            !allowBreak && node is BreakStatementSyntax);

    private static HashSet<int> CollectConnectorClosure(
        ControlFlowGraph graph,
        IEnumerable<int> directOrdinals,
        ISet<int> connectorCandidates,
        bool forward) {
        var connectors = new HashSet<int>();
        var queue = new Queue<BasicBlock>(directOrdinals.Select(ordinal => graph.Blocks[ordinal]));
        while (queue.Count != 0) {
            var block = queue.Dequeue();
            var adjacent = forward
                ? GetSuccessors(block).Select(branch => GetStatementRegionDestination(graph, branch))
                : block.Predecessors.Select(static branch => branch.Source);
            foreach (var candidate in adjacent) {
                if (candidate == null ||
                    !connectorCandidates.Contains(candidate.Ordinal) ||
                    !connectors.Add(candidate.Ordinal))
                    continue;
                queue.Enqueue(candidate);
            }
        }
        return connectors;
    }
    private static BasicBlock? GetStatementRegionDestination(ControlFlowGraph graph, ControlFlowBranch branch) =>
        branch.FinallyRegions.IsDefaultOrEmpty
            ? branch.Destination
            : graph.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal];

    private static bool Fail(string failureDetail, out CfgRegionPlan plan, out string failure) {
        plan = null!;
        failure = failureDetail;
        return false;
    }
    private static IEnumerable<ControlFlowBranch> GetSuccessors(BasicBlock block) {
        if (block.FallThroughSuccessor != null)
            yield return block.FallThroughSuccessor;
        if (block.ConditionalSuccessor != null &&
            !ReferenceEquals(block.ConditionalSuccessor, block.FallThroughSuccessor))
            yield return block.ConditionalSuccessor;
    }
    private static bool IsTerminalCompletionBranch(ControlFlowBranch branch) =>
        branch.Semantics is
            ControlFlowBranchSemantics.Return or
            ControlFlowBranchSemantics.Throw or
            ControlFlowBranchSemantics.Rethrow or
            ControlFlowBranchSemantics.ProgramTermination;

    private static bool IsWithinRegion(BasicBlock block, ControlFlowRegionKind kind) {
        for (var region = block.EnclosingRegion; region != null; region = region.EnclosingRegion)
            if (region.Kind == kind)
                return true;
        return false;
    }
    internal sealed record CfgRegionPlan(
        StatementSyntax Target,
        CfgTraversalPoint EntryPoint,
        IReadOnlyDictionary<int, CfgBlockSlice> Blocks) {
        internal bool InvalidatesExitedLocals => Target is IfStatementSyntax or SwitchStatementSyntax;

        internal List<(ControlFlowBranch Branch, CfgPathState Path)> CompletedPaths { get; } = [];

        internal List<CfgPathState> TerminalPaths { get; } = [];

        internal CfgTraversalPoint GetEntryPoint(BasicBlock block, CfgFinallyContinuation? continuation) =>
            Blocks.TryGetValue(block.Ordinal, out var slice)
                ? new CfgTraversalPoint(block, continuation, slice.FirstOperationIndex)
                : default;
    }
    internal readonly record struct CfgBlockSlice(int FirstOperationIndex, int EndOperationIndexExclusive, bool HasCursorExit);

    internal static bool IsTargetOperation(
        IOperation operation,
        SyntaxNode site,
        bool includeCurrentStatementCompletionFacts,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (!includeCurrentStatementCompletionFacts || site is not LocalDeclarationStatementSyntax declaration)
            return ContainsSite(operation.Syntax, site);
        if (operation is IVariableDeclarationGroupOperation)
            return ContainsSite(operation.Syntax, declaration);

        ISymbol? target = operation switch {
            IVariableDeclaratorOperation declarator => declarator.Symbol,
            ISimpleAssignmentOperation assignment when TryGetDirectTarget(assignment.Target, out var symbol) =>
                symbol,
            _ => null
        };
        return target != null && declaration.Declaration.Variables.Any(variable =>
            SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(variable, cancellationToken), target));
    }
    private static bool ShouldSkipScopedBlockCompletionOperation(IOperation operation, SyntaxNode site) {
        var statement = operation.Syntax.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent is not BlockSyntax block ||
            block.Parent is not BlockSyntax ||
            block.Span.Contains(site.SpanStart) ||
            block.Span.End > site.SpanStart)
            return false;

        var statementIndex = block.Statements.IndexOf(statement);
        var limit = SymbolicAnalysisLimitContext.Limits.MaxScopedBlockCompletionStatements;
        if (statementIndex < 0 || statementIndex < limit)
            return false;

        SymbolicAnalysisLimitContext.Record(
            SymbolicAnalysisLimitKind.ScopedBlockCompletionStatements,
            limit,
            block.Statements.Count,
            block,
            "program_point.completed_block_state");
        return true;
    }
    internal static SymbolicLoweringResult<SymbolicState> Exact(SymbolicState state, SyntaxNode site) =>
        SymbolicLoweringResult<SymbolicState>.Exact(state.Normalize(), Provenance(site, "exact"));

    internal static SymbolicLoweringResult<SymbolicState> Unsupported(SyntaxNode site, string detail) =>
        SymbolicLoweringResult<SymbolicState>.Unsupported(Provenance(site, detail));

    internal static SymbolicLoweringProvenance Provenance(SyntaxNode site, string detail) =>
        new("cfg-program-point", site.Span, detail);

    internal enum CfgProgramPointTargetKind {
        BeforeCurrent,
        CurrentCompletion,
        ForInitialEntry
    }
}

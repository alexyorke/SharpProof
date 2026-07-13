using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal static partial class SymbolicProgramPointFacts
{
    private const string ImplicitThisVariableName = "this";

    internal static SymbolicState CollectPriorAssignmentState(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null)
    {
        var state = initialState ?? new SymbolicState();
        AddMethodEntryNullableFlowStateFacts(ref state, site, semanticModel, cancellationToken);
        foreach (var containingBlock in CSharpSyntaxFacts
                     .EnumerateContainingBlocks(site, stopAtExecutionRoot: true)
                     .Reverse())
        {
            if (IsLoopBodyBlock(containingBlock.Block))
                RemoveStateFactsInvalidatedByNestedMutations(
                    ref state,
                    containingBlock.Block,
                    semanticModel,
                    cancellationToken);

            ApplyContainingBlockEntryStateFacts(
                ref state,
                containingBlock.Block,
                semanticModel,
                cancellationToken);

            foreach (var statement in containingBlock.Block.Statements)
            {
                if (ReferenceEquals(statement, containingBlock.ContainingStatement))
                {
                    InvalidateStateForTryRegionEntry(
                        ref state,
                        site,
                        statement,
                        semanticModel,
                        cancellationToken);
                    if (includeCurrentStatementCompletionFacts &&
                        ReferenceEquals(site, statement) &&
                        SupportsCurrentStatementCompletionFacts(statement))
                        AddPriorStatementStateFacts(
                            ref state,
                            statement,
                            semanticModel,
                            cancellationToken);

                    break;
                }

                AddPriorStatementStateFacts(
                    ref state,
                    statement,
                    semanticModel,
                    cancellationToken);
            }
        }

        if (site is BlockSyntax siteBlock)
        {
            ApplyContainingBlockEntryStateFacts(
                ref state,
                siteBlock,
                semanticModel,
                cancellationToken);

            if (includeCurrentStatementCompletionFacts)
                AddCompletedBlockStateFacts(
                    ref state,
                    siteBlock,
                    semanticModel,
                    cancellationToken);
        }
        else if (includeCurrentStatementCompletionFacts &&
                 site is ExpressionSyntax siteExpression)
        {
            AddCompletedExpressionStateFacts(
                ref state,
                siteExpression,
                semanticModel,
                cancellationToken);
        }

        return state.Normalize();
    }

    private static void InvalidateStateForTryRegionEntry(
        ref SymbolicState state,
        SyntaxNode site,
        StatementSyntax containingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (containingStatement is not TryStatementSyntax tryStatement ||
            tryStatement.Block.Span.Contains(site.SpanStart))
            return;

        RemoveStateFactsInvalidatedByNestedMutations(
            ref state,
            tryStatement.Block,
            semanticModel,
            cancellationToken);
        if (tryStatement.Finally?.Block.Span.Contains(site.SpanStart) != true) return;

        foreach (var catchClause in tryStatement.Catches)
            RemoveStateFactsInvalidatedByNestedMutations(
                ref state,
                catchClause.Block,
                semanticModel,
                cancellationToken);
    }

    internal static SymbolicState CollectForInitialEntryState(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var state = CollectAncestorReachabilityState(forStatement, semanticModel, cancellationToken);
        state = MergeStates(
            state,
            CollectPriorAssignmentState(forStatement, semanticModel, cancellationToken));
        state = MergeStates(
            state,
            CollectForInitializerState(forStatement, semanticModel, cancellationToken));
        return state;
    }

    internal static SymbolicState MergeStates(SymbolicState left, SymbolicState right)
    {
        var symbolVersions = left.SymbolVersions.SetItems(right.SymbolVersions);
        return new SymbolicState(
            left.Facts.Concat(right.Facts),
            left.PathConditions.Concat(right.PathConditions),
            symbolVersions,
            left.IsContradictory || right.IsContradictory).Normalize();
    }

    private static bool SupportsCurrentStatementCompletionFacts(StatementSyntax statement)
    {
        return statement is LocalDeclarationStatementSyntax or
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax };
    }

    private static void ApplyContainingBlockEntryStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        RemoveStateFactsInvalidatedByForLoopEntry(ref state, block, semanticModel, cancellationToken);
        if (TryAddContainingBlockEntryInlineAssignmentStateFacts(
                ref state,
                block,
                semanticModel,
                cancellationToken))
            return;

        RemoveStateFactsInvalidatedByContainingBlockEntry(ref state, block, semanticModel, cancellationToken);
        AddContainingBlockEntryStateFacts(ref state, block, semanticModel, cancellationToken);
    }

    private static void RemoveStateFactsInvalidatedByForLoopEntry(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (block.Parent is not ForStatementSyntax forStatement ||
            !ReferenceEquals(forStatement.Statement, block))
            return;

        foreach (var symbol in GetForLoopInitializerAssignedSymbols(forStatement, semanticModel, cancellationToken))
            state = RemoveStateFactsReferencingSymbol(state, symbol);
    }

    private static IEnumerable<ISymbol> GetForLoopInitializerAssignedSymbols(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (forStatement.Declaration != null)
            foreach (var declarator in forStatement.Declaration.Variables)
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    yield return localSymbol.OriginalDefinition;

        foreach (var initializer in forStatement.Initializers)
            if (initializer is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is { } assignedSymbol &&
                assignedSymbol is ILocalSymbol or IParameterSymbol)
                yield return assignedSymbol.OriginalDefinition;
    }

    public static SymbolicState CollectAncestorReachabilityState(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var state = new SymbolicState();

        foreach (var ancestor in syntaxNode.Ancestors())
            if (ancestor is IfStatementSyntax ifStatementSyntax)
            {
                if (ifStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                    !AnyReferencedSymbolAssignedBeforeUse(
                        ifStatementSyntax.Condition,
                        ifStatementSyntax.Statement,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    AddReachabilityCondition(ref state, ifStatementSyntax.Condition, true, semanticModel,
                        cancellationToken);
                else if (ifStatementSyntax.Else?.Statement is { } elseStatement &&
                         elseStatement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             ifStatementSyntax.Condition,
                             elseStatement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                    AddReachabilityCondition(ref state, ifStatementSyntax.Condition, false, semanticModel,
                        cancellationToken);
            }
            else if (ancestor is ConditionalExpressionSyntax conditionalExpressionSyntax)
            {
                if (conditionalExpressionSyntax.WhenTrue.Span.Contains(syntaxNode.Span) &&
                    !AnyReferencedSymbolAssignedBeforeUse(
                        conditionalExpressionSyntax.Condition,
                        conditionalExpressionSyntax.WhenTrue,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    AddReachabilityCondition(ref state, conditionalExpressionSyntax.Condition, true, semanticModel,
                        cancellationToken);
                else if (conditionalExpressionSyntax.WhenFalse.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             conditionalExpressionSyntax.Condition,
                             conditionalExpressionSyntax.WhenFalse,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                    AddReachabilityCondition(ref state, conditionalExpressionSyntax.Condition, false, semanticModel,
                        cancellationToken);
            }
            else if (ancestor is BinaryExpressionSyntax binaryExpressionSyntax &&
                     binaryExpressionSyntax.Right.Span.Contains(syntaxNode.Span))
            {
                if (AnyReferencedSymbolAssignedBeforeUse(
                        binaryExpressionSyntax.Left,
                        binaryExpressionSyntax.Right,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    continue;

                if (binaryExpressionSyntax.IsKind(SyntaxKind.LogicalAndExpression))
                    AddReachabilityCondition(ref state, binaryExpressionSyntax.Left, true, semanticModel,
                        cancellationToken);
                else if (binaryExpressionSyntax.IsKind(SyntaxKind.LogicalOrExpression))
                    AddReachabilityCondition(ref state, binaryExpressionSyntax.Left, false, semanticModel,
                        cancellationToken);
                else if (binaryExpressionSyntax.IsKind(SyntaxKind.CoalesceExpression))
                    AddCoalesceRightStateCondition(
                        ref state,
                        binaryExpressionSyntax.Left,
                        semanticModel,
                        cancellationToken);
            }
            else if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpressionSyntax &&
                     conditionalAccessExpressionSyntax.WhenNotNull.Span.Contains(syntaxNode.SpanStart) &&
                     !AnyReferencedSymbolAssignedBeforeUse(
                         conditionalAccessExpressionSyntax.Expression,
                         conditionalAccessExpressionSyntax.WhenNotNull,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                AddReferenceNullCondition(ref state, conditionalAccessExpressionSyntax.Expression, false, semanticModel,
                    cancellationToken);
            }
            else if (ancestor is LockStatementSyntax lockStatementSyntax &&
                     lockStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                     IsLocalOrParameterReference(lockStatementSyntax.Expression, semanticModel, cancellationToken) &&
                     !AnyReferencedSymbolAssignedBeforeUse(
                         lockStatementSyntax.Expression,
                         lockStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                AddReferenceNullCondition(
                    ref state,
                    lockStatementSyntax.Expression,
                    false,
                    semanticModel,
                    cancellationToken,
                    "ir.path.lock-entry.not-null");
            }
            else if (ancestor is CatchClauseSyntax catchClauseSyntax &&
                     catchClauseSyntax.Block.Span.Contains(syntaxNode.Span))
            {
                AddCatchBodyEntryStateFacts(
                    ref state,
                    catchClauseSyntax,
                    syntaxNode.SpanStart,
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is UsingStatementSyntax usingStatementSyntax &&
                     usingStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
            {
                if (usingStatementSyntax.Declaration != null)
                {
                    AddUsingStatementDeclarationStateFacts(
                        ref state,
                        usingStatementSyntax,
                        semanticModel,
                        cancellationToken);
                }
                else if (usingStatementSyntax.Expression != null &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             usingStatementSyntax.Expression,
                             usingStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddUsingStatementExpressionStateFacts(
                        ref state,
                        usingStatementSyntax.Expression,
                        usingStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                }
            }
            else if (ancestor is WhileStatementSyntax whileStatementSyntax &&
                     whileStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                     !AnyReferencedSymbolAssignedBeforeUse(
                         whileStatementSyntax.Condition,
                         whileStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                AddReachabilityCondition(ref state, whileStatementSyntax.Condition, true, semanticModel,
                    cancellationToken);
                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatementSyntax,
                    whileStatementSyntax.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is DoStatementSyntax doStatementSyntax &&
                     doStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
            {
                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    doStatementSyntax,
                    doStatementSyntax.Statement,
                    "ir.path.do-loop-invariant",
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is ForStatementSyntax forStatementSyntax &&
                     forStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
            {
                if (forStatementSyntax.Condition != null &&
                    !AnyReferencedSymbolAssignedBeforeUse(
                        forStatementSyntax.Condition,
                        forStatementSyntax.Statement,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    AddReachabilityCondition(ref state, forStatementSyntax.Condition, true, semanticModel,
                        cancellationToken);

                AddForLoopBodyInvariantStateFacts(
                    ref state,
                    forStatementSyntax,
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is ForEachStatementSyntax forEachStatementSyntax &&
                     forEachStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                     !AnyReferencedSymbolAssignedBeforeUse(
                         forEachStatementSyntax.Expression,
                         forEachStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachStatementSyntax.Expression,
                    forEachStatementSyntax,
                    forEachStatementSyntax.Statement,
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is ForEachVariableStatementSyntax forEachVariableStatementSyntax &&
                     forEachVariableStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                     !AnyReferencedSymbolAssignedBeforeUse(
                         forEachVariableStatementSyntax.Expression,
                         forEachVariableStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachVariableStatementSyntax.Expression,
                    forEachVariableStatementSyntax,
                    forEachVariableStatementSyntax.Statement,
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is SwitchStatementSyntax switchStatementSyntax)
            {
                var matchingSection = switchStatementSyntax.Sections
                    .FirstOrDefault(section => section.Span.Contains(syntaxNode.SpanStart));
                if (matchingSection != null &&
                    !AnySwitchStatementConditionSymbolAssignedBeforeUse(
                        switchStatementSyntax,
                        matchingSection,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                {
                    AddSwitchStatementSectionStateFacts(
                        ref state,
                        switchStatementSyntax.Expression,
                        matchingSection,
                        semanticModel,
                        cancellationToken);
                }
            }
            else if (ancestor is SwitchExpressionSyntax switchExpressionSyntax)
            {
                var matchingArm = switchExpressionSyntax.Arms
                    .FirstOrDefault(arm => arm.Expression.Span.Contains(syntaxNode.SpanStart));
                if (matchingArm != null &&
                    !AnySwitchExpressionConditionSymbolAssignedBeforeUse(
                        switchExpressionSyntax,
                        matchingArm,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken) &&
                    SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                        switchExpressionSyntax.GoverningExpression,
                        matchingArm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition))
                {
                    state = state.AddPathCondition(armCondition);
                    AddSwitchBranchPatternBindingStateFacts(
                        ref state,
                        switchExpressionSyntax.GoverningExpression,
                        matchingArm.Pattern,
                        matchingArm,
                        semanticModel,
                        cancellationToken);
                    AddSwitchBranchGuardStateFacts(
                        ref state,
                        matchingArm.WhenClause?.Condition,
                        semanticModel,
                        cancellationToken);
                }
            }

        return state;
    }

    private static void AddReachabilityCondition(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool mustBeTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        condition = UnwrapExpression(condition);
        if (mustBeTrue &&
            condition is BinaryExpressionSyntax andCondition &&
            andCondition.IsKind(SyntaxKind.LogicalAndExpression))
        {
            AddReachabilityCondition(ref state, andCondition.Left, true, semanticModel, cancellationToken);
            AddReachabilityCondition(ref state, andCondition.Right, true, semanticModel, cancellationToken);
            return;
        }

        if (!mustBeTrue &&
            condition is BinaryExpressionSyntax orCondition &&
            orCondition.IsKind(SyntaxKind.LogicalOrExpression))
        {
            AddReachabilityCondition(ref state, orCondition.Left, false, semanticModel, cancellationToken);
            AddReachabilityCondition(ref state, orCondition.Right, false, semanticModel, cancellationToken);
            return;
        }

        if (TryAddInlineAssignmentReachabilityState(
                ref state,
                condition,
                mustBeTrue,
                semanticModel,
                cancellationToken))
            return;

        if (SymbolicReachabilityService.ApplyBranchFacts(
                state,
                condition,
                mustBeTrue,
                semanticModel,
                cancellationToken) is { IsExact: true, Value: { } branchState })
        {
            state = branchState;
            AddBranchPatternBindingStateFacts(
                ref state,
                condition,
                mustBeTrue,
                semanticModel,
                cancellationToken);
            return;
        }

    }

    private static void AddBranchPatternBindingStateFacts(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        condition = UnwrapExpression(condition);
        if (condition is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
        {
            AddBranchPatternBindingStateFacts(
                ref state,
                negation.Operand,
                !branchWhenTrue,
                semanticModel,
                cancellationToken);
            return;
        }

        if (!branchWhenTrue ||
            condition is not IsPatternExpressionSyntax isPatternExpression)
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(isPatternExpression.Expression, context);
        if (lowering is not { IsExact: true, Value: { } matchedTerm }) return;

        var typeInfo = semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken);
        TryAddIrPatternBindingStateFacts(
            ref state,
            matchedTerm,
            typeInfo.ConvertedType ?? typeInfo.Type,
            isPatternExpression.Pattern,
            semanticModel,
            cancellationToken);
    }

    private static bool TryAddInlineAssignmentReachabilityState(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!TryCollectInlineAssignmentBranchState(
                state,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                out var branchState))
            return false;

        state = branchState;
        return true;
    }

    private static bool TryCollectInlineAssignmentBranchState(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState branchState)
    {
        branchState = state;
        condition = UnwrapExpression(condition);
        if (condition is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
            return TryCollectInlineAssignmentBranchState(
                state,
                negation.Operand,
                !branchWhenTrue,
                semanticModel,
                cancellationToken,
                out branchState);

        if (condition is not BinaryExpressionSyntax comparison ||
            !TryGetInlineAssignmentComparisonRelationOperator(
                comparison.Kind(),
                out var relationOperator))
            return false;

        var leftAssignment = UnwrapExpression(comparison.Left) as AssignmentExpressionSyntax;
        var rightAssignment = UnwrapExpression(comparison.Right) as AssignmentExpressionSyntax;
        if (leftAssignment is null == rightAssignment is null) return false;

        var assignmentIsLeft = leftAssignment != null;
        var assignment = assignmentIsLeft ? leftAssignment! : rightAssignment!;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
            assignedSymbol is not ILocalSymbol and not IParameterSymbol)
            return false;

        assignedSymbol = assignedSymbol.OriginalDefinition;
        var siblingExpression = assignmentIsLeft
            ? comparison.Right
            : comparison.Left;
        if (!assignmentIsLeft &&
            ExpressionReferencesSymbol(
                siblingExpression,
                assignedSymbol,
                semanticModel,
                cancellationToken))
            return false;

        if (!TryCreateAssignedValueComparisonTerm(
                state,
                assignedSymbol,
                assignment.Right,
                semanticModel,
                cancellationToken,
                out _) ||
            !TryCreateSymbolTerm(assignedSymbol, out var assignedTerm))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var siblingLowering = SymbolicSemanticPipeline.LowerTerm(siblingExpression, context);
        if (siblingLowering is not { IsExact: true, Value: { } siblingTerm }) return false;

        var leftTerm = assignmentIsLeft
            ? assignedTerm
            : siblingTerm;
        var rightTerm = assignmentIsLeft
            ? siblingTerm
            : assignedTerm;
        if (!CanCompareIrTerms(leftTerm, rightTerm)) return false;

        branchState = state;
        AddAssignedValueStateFacts(
            ref branchState,
            assignedSymbol,
            assignment.Right,
            semanticModel,
            cancellationToken,
            "ir.path.inline-assignment");
        var branchCondition = (SymbolicCondition)new SymbolicFactCondition(
            SymbolicFact.Exact(
                new SymbolicRelationAtom(relationOperator, leftTerm, rightTerm),
                comparison,
                "ir.path.inline-assignment.comparison"));
        if (!branchWhenTrue) branchCondition = new SymbolicNotCondition(branchCondition);

        branchState = branchState.AddPathCondition(branchCondition);
        return true;
    }

    private static bool TryCreateAssignedValueComparisonTerm(
        SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm assignedValueTerm)
    {
        assignedValueTerm = null!;
        var hasThrowGuard = TryGetThrowGuardedValue(
            valueExpression,
            out var throwGuardedValue,
            out var guardExpression,
            out var guardBranchWhenTrue,
            out var requiresNonNullValue);
        var effectiveValueExpression = hasThrowGuard
            ? throwGuardedValue
            : valueExpression;
        if (ExpressionReferencesSymbol(
                effectiveValueExpression,
                assignedSymbol,
                semanticModel,
                cancellationToken))
            return TryGetCurrentStateSymbolValueTerm(
                       state,
                       assignedSymbol,
                       out var previousValueTerm) &&
                   TryCreateSelfReferentialAssignedValueStateTerm(
                       previousValueTerm,
                       assignedSymbol,
                       effectiveValueExpression,
                       semanticModel,
                       cancellationToken,
                       out assignedValueTerm);

        var lowering = SymbolicSemanticPipeline.LowerTerm(
            effectiveValueExpression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is { IsExact: true, Value: { } loweredAssignedValueTerm })
        {
            assignedValueTerm = loweredAssignedValueTerm;
            return true;
        }

        if (TryCreateSymbolTerm(assignedSymbol, out var assignedTerm) &&
            assignedTerm.Kind == SmtValueKind.Reference &&
            effectiveValueExpression is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression))
        {
            assignedValueTerm = assignedTerm;
            return true;
        }

        return false;
    }

    private static bool TryGetInlineAssignmentComparisonRelationOperator(
        SyntaxKind syntaxKind,
        out SymbolicRelationOperator relationOperator)
    {
        switch (syntaxKind)
        {
            case SyntaxKind.EqualsExpression:
                relationOperator = SymbolicRelationOperator.Equal;
                return true;
            case SyntaxKind.NotEqualsExpression:
                relationOperator = SymbolicRelationOperator.NotEqual;
                return true;
            case SyntaxKind.GreaterThanExpression:
                relationOperator = SymbolicRelationOperator.GreaterThan;
                return true;
            case SyntaxKind.GreaterThanOrEqualExpression:
                relationOperator = SymbolicRelationOperator.GreaterThanOrEqual;
                return true;
            case SyntaxKind.LessThanExpression:
                relationOperator = SymbolicRelationOperator.LessThan;
                return true;
            case SyntaxKind.LessThanOrEqualExpression:
                relationOperator = SymbolicRelationOperator.LessThanOrEqual;
                return true;
            default:
                relationOperator = default;
                return false;
        }
    }

    private static void AddReferenceNullCondition(
        ref SymbolicState state,
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? provenance = null)
    {
        if (IsDefinitelyNullReferenceValue(expression, semanticModel, cancellationToken))
        {
            if (!isNull) state = MarkContradictory(state);

            return;
        }

        if (IsDefinitelyNonNullReferenceValue(expression, semanticModel, cancellationToken))
        {
            if (isNull) state = MarkContradictory(state);

            return;
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { } subject } ||
            subject.Kind != SmtValueKind.Reference)
            return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                subject,
                new SymbolicNullTerm()),
            expression,
            provenance ?? (isNull ? "ir.path.reference-null" : "ir.path.reference-not-null"));
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    private static void AddCoalesceRightStateCondition(
        ref SymbolicState state,
        ExpressionSyntax leftExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var originalKey = state.NormalizedProofKey;
        AddReferenceNullCondition(
            ref state,
            leftExpression,
            true,
            semanticModel,
            cancellationToken,
            "ir.path.coalesce.fallback-null");
        if (state.IsContradictory ||
            !string.Equals(originalKey, state.NormalizedProofKey, StringComparison.Ordinal))
            return;

        leftExpression = UnwrapExpression(leftExpression);
        if (leftExpression is not ConditionalAccessExpressionSyntax conditionalAccess ||
            !ConditionalAccessFallbackRequiresNullReceiver(
                conditionalAccess,
                semanticModel,
                cancellationToken))
            return;

        AddReferenceNullCondition(
            ref state,
            conditionalAccess.Expression,
            true,
            semanticModel,
            cancellationToken,
            "ir.path.coalesce.conditional-access-null-receiver");
    }

    private static bool ConditionalAccessFallbackRequiresNullReceiver(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(conditionalAccess.WhenNotNull, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type == null)
        {
            var symbol = semanticModel.GetSymbolInfo(conditionalAccess.WhenNotNull, cancellationToken).Symbol;
            type = symbol switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IEventSymbol @event => @event.Type,
                IMethodSymbol method => method.ReturnType,
                _ => null
            };
        }

        return type?.IsValueType == true &&
               type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    private static SymbolicState MarkContradictory(SymbolicState state)
    {
        return new SymbolicState(
            state.Facts,
            state.PathConditions,
            state.SymbolVersions,
            true);
    }

    private static void AddSwitchStatementSectionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax governingExpression,
        SwitchSectionSyntax section,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                governingExpression,
                section,
                semanticModel,
                cancellationToken,
                out var sectionCondition))
            state = state.AddPathCondition(sectionCondition);

        if (section.Labels.Count != 1) return;

        if (section.Labels[0] is not CasePatternSwitchLabelSyntax patternLabel) return;

        AddSwitchBranchPatternBindingStateFacts(
            ref state,
            governingExpression,
            patternLabel.Pattern,
            patternLabel,
            semanticModel,
            cancellationToken);
        AddSwitchBranchGuardStateFacts(
            ref state,
            patternLabel.WhenClause?.Condition,
            semanticModel,
            cancellationToken);
    }

    private static void AddSwitchBranchPatternBindingStateFacts(
        ref SymbolicState state,
        ExpressionSyntax governingExpression,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (TryAddIrSwitchExpressionPatternBindingStateFacts(
                ref state,
                governingExpression,
                pattern,
                semanticModel,
                cancellationToken))
            return;

    }

    private static bool TryAddIrSwitchExpressionPatternBindingStateFacts(
        ref SymbolicState state,
        ExpressionSyntax governingExpression,
        PatternSyntax pattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(governingExpression, context);
        if (lowering is not { IsExact: true, Value: { } matchedTerm }) return false;

        var matchedType = semanticModel.GetTypeInfo(governingExpression, cancellationToken).ConvertedType ??
                          semanticModel.GetTypeInfo(governingExpression, cancellationToken).Type;
        return TryAddIrPatternBindingStateFacts(
            ref state,
            matchedTerm,
            matchedType,
            pattern,
            semanticModel,
            cancellationToken);
    }

    private static bool TryAddIrPatternBindingStateFacts(
        ref SymbolicState state,
        SymbolicTerm matchedTerm,
        ITypeSymbol? matchedType,
        PatternSyntax pattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        pattern = UnwrapPattern(pattern);
        var canonicalContext = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var canonicalLowering = SymbolicSemanticPipeline.LowerPatternCondition(
                matchedTerm,
                matchedType,
                pattern,
                pattern,
                canonicalContext);
        if (canonicalLowering is { IsExact: true, Value: { } canonicalCondition })
        {
            state = state.AddPathCondition(canonicalCondition);
            if (pattern is RecursivePatternSyntax recursivePattern)
                TryAddIrRecursivePatternBindingStateFacts(
                    ref state,
                    matchedTerm,
                    matchedType,
                    recursivePattern,
                    semanticModel,
                    cancellationToken);
            else if (pattern is ListPatternSyntax listPattern)
                TryAddIrListPatternBindingStateFacts(
                    ref state,
                    matchedTerm,
                    matchedType,
                    listPattern,
                    semanticModel,
                    cancellationToken);

            return true;
        }

        switch (pattern)
        {
            case VarPatternSyntax varPattern:
                return TryAddIrDesignationBindingStateFacts(
                    ref state,
                    matchedTerm,
                    varPattern.Designation,
                    varPattern,
                    semanticModel,
                    cancellationToken,
                    false);
            case DeclarationPatternSyntax declarationPattern:
                return TryAddIrDesignationBindingStateFacts(
                    ref state,
                    matchedTerm,
                    declarationPattern.Designation,
                    declarationPattern,
                    semanticModel,
                    cancellationToken,
                    true);
            case RelationalPatternSyntax relationalPattern:
                return TryAddIrRelationalPatternStateFact(
                    ref state,
                    matchedTerm,
                    relationalPattern,
                    semanticModel,
                    cancellationToken);
            case RecursivePatternSyntax recursivePattern
                :
                return TryAddIrRecursivePatternBindingStateFacts(
                    ref state,
                    matchedTerm,
                    matchedType,
                    recursivePattern,
                    semanticModel,
                    cancellationToken);
            case BinaryPatternSyntax binaryPattern when binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword):
                return TryAddIrPatternBindingStateFacts(
                           ref state,
                           matchedTerm,
                           matchedType,
                           binaryPattern.Left,
                           semanticModel,
                           cancellationToken) &&
                       TryAddIrPatternBindingStateFacts(
                           ref state,
                           matchedTerm,
                           matchedType,
                           binaryPattern.Right,
                           semanticModel,
                           cancellationToken);
            case ListPatternSyntax listPattern:
                return TryAddIrListPatternBindingStateFacts(
                    ref state,
                    matchedTerm,
                    matchedType,
                    listPattern,
                    semanticModel,
                    cancellationToken);
            default:
                var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
                var patternLowering = SymbolicSemanticPipeline.LowerPatternCondition(
                    matchedTerm,
                    pattern,
                    pattern,
                    context);
                if (patternLowering is not { IsExact: true, Value: { } patternCondition })
                    return false;

                if (patternCondition is SymbolicFactCondition factCondition)
                    state = state.AddFact(factCondition.Fact);
                else
                    state = state.AddPathCondition(patternCondition);

                return true;
        }
    }

    private static bool TryAddIrListPatternBindingStateFacts(
        ref SymbolicState state,
        SymbolicTerm matchedTerm,
        ITypeSymbol? matchedType,
        ListPatternSyntax listPattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (matchedTerm.Kind != SmtValueKind.Reference ||
            !SymbolicIrLowerer.TryGetListPatternShape(
                matchedTerm,
                matchedType,
                out _,
                out var elementType,
                out var elementKind))
            return false;

        var addedAny = false;
        for (var index = 0; index < listPattern.Patterns.Count; index++)
        {
            if (listPattern.Patterns[index] is SlicePatternSyntax) continue;

            if (!CSharpSyntaxFacts.TryGetListPatternElementPosition(
                    listPattern,
                    index,
                    out var elementIndex,
                    out var fromEnd))
                continue;

            SymbolicTerm elementIndexTerm;
            if (fromEnd)
                elementIndexTerm = new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Subtract,
                    new SymbolicLengthTerm(matchedTerm),
                    new SymbolicIntegerConstantTerm(elementIndex));
            else
                elementIndexTerm = new SymbolicIntegerConstantTerm(elementIndex);

            var elementTerm = new SymbolicElementTerm(
                matchedTerm,
                elementIndexTerm,
                elementKind);
            addedAny |= TryAddIrPatternBindingStateFacts(
                ref state,
                elementTerm,
                elementType,
                listPattern.Patterns[index],
                semanticModel,
                cancellationToken);
        }

        return addedAny;
    }

    private static bool TryAddIrRecursivePatternBindingStateFacts(
        ref SymbolicState state,
        SymbolicTerm matchedTerm,
        ITypeSymbol? matchedType,
        RecursivePatternSyntax recursivePattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var addedBindableFacts = false;

        if (matchedTerm.Kind == SmtValueKind.Reference)
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                matchedTerm,
                new SymbolicNullTerm(),
                recursivePattern,
                "ir.path.switch-pattern-binding.recursive.non-null");

        if (TryAddIrDesignationBindingStateFacts(
                ref state,
                matchedTerm,
                recursivePattern.Designation,
                recursivePattern,
                semanticModel,
                cancellationToken,
                true))
            addedBindableFacts = true;

        if (recursivePattern.PositionalPatternClause is { } positionalClause)
        {
            var loweringContext = new SymbolicLoweringContext(semanticModel, cancellationToken);
            for (var index = 0; index < positionalClause.Subpatterns.Count; index++)
            {
                var subpattern = positionalClause.Subpatterns[index];
                if (!SymbolicIrLowerer.TryCreateRecursivePatternPositionalTerm(
                        matchedTerm,
                        matchedType,
                        recursivePattern,
                        index,
                        loweringContext,
                        out var componentTerm,
                        out var componentType))
                    continue;

                if (TryAddIrPatternBindingStateFacts(
                        ref state,
                        componentTerm,
                        componentType,
                        subpattern.Pattern,
                        semanticModel,
                        cancellationToken))
                    addedBindableFacts = true;
            }
        }

        if (recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 }) return addedBindableFacts;

        foreach (var subpattern in recursivePattern.PropertyPatternClause.Subpatterns)
        {
            if (!TryResolveIrPropertySubpatternTerm(
                    matchedTerm,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    out var memberTerm,
                    out var memberType))
                continue;

            if (TryAddIrPatternBindingStateFacts(
                    ref state,
                    memberTerm,
                    memberType,
                    subpattern.Pattern,
                    semanticModel,
                    cancellationToken))
                addedBindableFacts = true;
        }

        return addedBindableFacts;
    }

    private static bool TryAddIrRelationalPatternStateFact(
        ref SymbolicState state,
        SymbolicTerm matchedTerm,
        RelationalPatternSyntax relationalPattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (matchedTerm.Kind != SmtValueKind.Int ||
            !TryGetIrRelationalPatternOperator(relationalPattern.OperatorToken.Kind(), out var relationOperator))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(relationalPattern.Expression, context);
        if (lowering is not { IsExact: true, Value: { } relationalValue } ||
            relationalValue.Kind != SmtValueKind.Int)
            return false;

        AddRelationPathFact(
            ref state,
            relationOperator,
            matchedTerm,
            relationalValue,
            relationalPattern,
            "ir.path.switch-pattern-binding.relational");
        return true;
    }

    private static bool TryGetIrRelationalPatternOperator(
        SyntaxKind operatorKind,
        out SymbolicRelationOperator relationOperator)
    {
        relationOperator = operatorKind switch
        {
            SyntaxKind.GreaterThanToken => SymbolicRelationOperator.GreaterThan,
            SyntaxKind.GreaterThanEqualsToken => SymbolicRelationOperator.GreaterThanOrEqual,
            SyntaxKind.LessThanToken => SymbolicRelationOperator.LessThan,
            SyntaxKind.LessThanEqualsToken => SymbolicRelationOperator.LessThanOrEqual,
            _ => default
        };

        return operatorKind is
            SyntaxKind.GreaterThanToken or
            SyntaxKind.GreaterThanEqualsToken or
            SyntaxKind.LessThanToken or
            SyntaxKind.LessThanEqualsToken;
    }

    private static bool TryAddIrDesignationBindingStateFacts(
        ref SymbolicState state,
        SymbolicTerm matchedTerm,
        VariableDesignationSyntax? designation,
        SyntaxNode source,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool addNonNullFact)
    {
        if (designation == null) return false;

        if (designation is DiscardDesignationSyntax) return true;

        if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
            singleVariableDesignation.Identifier.ValueText == "_" ||
            semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol
                localSymbol ||
            !TryCreateSymbolTerm(localSymbol.OriginalDefinition, out var localTerm) ||
            !CanCompareIrTerms(localTerm, matchedTerm))
            return false;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            localTerm,
            matchedTerm,
            source,
            "ir.path.switch-pattern-binding.designation");

        if (localTerm.Kind == SmtValueKind.Reference && matchedTerm.Kind == SmtValueKind.Reference)
        {
            if (SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(localSymbol.Type, localTerm, source) is
                    { IsExact: true, Value: { } localLength } &&
                SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(localSymbol.Type, matchedTerm, source) is
                    { IsExact: true, Value: { } matchedLength } &&
                CanCompareIrTerms(localLength, matchedLength))
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.Equal,
                    localLength,
                    matchedLength,
                    source,
                    "ir.path.switch-pattern-binding.designation-length");

            if (localSymbol.Type.SpecialType == SpecialType.System_String &&
                SymbolicSemanticPipeline.ProjectStringContentTerm(localTerm, source) is
                    { IsExact: true, Value: { } localString } &&
                SymbolicSemanticPipeline.ProjectStringContentTerm(matchedTerm, source) is
                    { IsExact: true, Value: { } matchedString })
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.Equal,
                    localString,
                    matchedString,
                    source,
                    "ir.path.switch-pattern-binding.designation-string");
        }

        if (addNonNullFact &&
            localTerm.Kind == SmtValueKind.Reference)
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                localTerm,
                new SymbolicNullTerm(),
                source,
                "ir.path.switch-pattern-binding.non-null");

        return true;
    }

    private static bool TryResolveIrPropertySubpatternTerm(
        SymbolicTerm receiver,
        SubpatternSyntax subpattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm memberTerm,
        out ITypeSymbol? memberType)
    {
        memberTerm = null!;
        memberType = null;

        if (subpattern.NameColon?.Name is not ExpressionSyntax memberSyntax) return false;

        var memberSymbol = semanticModel.GetSymbolInfo(memberSyntax, cancellationToken).Symbol;
        var resolvedMemberType = memberSymbol switch
        {
            IPropertySymbol propertySymbol => propertySymbol.Type,
            IFieldSymbol fieldSymbol => fieldSymbol.Type,
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            _ => null
        };
        if (memberSymbol == null ||
            resolvedMemberType == null ||
            !TryGetValueKind(resolvedMemberType, out var memberKind))
            return false;

        if (memberKind == SmtValueKind.Int &&
            receiver.Kind == SmtValueKind.Reference &&
            string.Equals(memberSymbol.Name, "Count", StringComparison.Ordinal))
        {
            memberTerm = new SymbolicCountTerm(receiver);
            memberType = resolvedMemberType;
            return true;
        }

        if (!TryCreateMemberDerivedTerm(receiver, memberSymbol, memberKind, out memberTerm)) return false;

        memberType = resolvedMemberType;
        return true;
    }

    private static void AddSwitchBranchGuardStateFacts(
        ref SymbolicState state,
        ExpressionSyntax? guardCondition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (guardCondition == null) return;

        AddReachabilityCondition(
            ref state,
            guardCondition,
            true,
            semanticModel,
            cancellationToken);
    }

    private static void AddSymbolReferenceNullCondition(
        ref SymbolicState state,
        ISymbol symbol,
        SyntaxNode source,
        bool isNull,
        string provenance)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !TryGetValueKind(type, out var kind) ||
            kind != SmtValueKind.Reference)
            return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), SmtValueKind.Reference),
                new SymbolicNullTerm()),
            source,
            provenance);
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    private static void AddCatchBodyEntryStateFacts(
        ref SymbolicState state,
        CatchClauseSyntax catchClause,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (catchClause.Declaration != null &&
            semanticModel.GetDeclaredSymbol(catchClause.Declaration, cancellationToken) is ILocalSymbol localSymbol &&
            !IsSymbolAssignedBetween(
                catchClause.Block,
                catchClause.Block.SpanStart - 1,
                useSpanStart,
                localSymbol.OriginalDefinition,
                semanticModel,
                cancellationToken))
            AddSymbolReferenceNullCondition(
                ref state,
                localSymbol.OriginalDefinition,
                catchClause.Declaration,
                false,
                "ir.path.catch-entry.exception-not-null");

        if (catchClause.Filter?.FilterExpression is { } filterExpression &&
            !AnyReferencedSymbolAssignedBeforeUse(
                filterExpression,
                catchClause.Block,
                useSpanStart,
                semanticModel,
                cancellationToken))
            AddReachabilityCondition(ref state, filterExpression, true, semanticModel, cancellationToken);
    }

    private static void AddUsingStatementExpressionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddThrowGuardedExpressionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.using-entry.throw-guarded-not-null");
    }

    private static void AddUsingStatementDeclarationStateFacts(
        ref SymbolicState state,
        UsingStatementSyntax usingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (usingStatement.Declaration == null) return;

        foreach (var declarator in usingStatement.Declaration.Variables)
        {
            if (declarator.Initializer == null ||
                semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol)
                continue;

            AddUsingDeclarationInitializerStateFacts(
                ref state,
                localSymbol,
                declarator.Initializer.Value,
                usingStatement.Statement,
                semanticModel,
                cancellationToken);
        }
    }

    private static void AddUsingDeclarationInitializerStateFacts(
        ref SymbolicState state,
        ILocalSymbol localSymbol,
        ExpressionSyntax initializer,
        StatementSyntax usingBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var effectiveInitializer = initializer;
        if (TryGetThrowGuardedValue(
                initializer,
                out var guardedValue,
                out _,
                out _,
                out _))
        {
            effectiveInitializer = guardedValue;
            AddThrowGuardedExpressionStateFacts(
                ref state,
                initializer,
                usingBody,
                semanticModel,
                cancellationToken,
                "ir.path.using-entry.throw-guarded-not-null");
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(effectiveInitializer, context);
        if (!TryCreateLocalSymbolTerm(localSymbol, out var target) ||
            lowering is not { IsExact: true, Value: { } value } ||
            !CanCompareIrTerms(target, value))
            return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                target,
                value),
            initializer,
            "ir.path.using-entry.declaration-alias");
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    private static bool TryCreateLocalSymbolTerm(
        ILocalSymbol localSymbol,
        out SymbolicTerm term)
    {
        if (!TryGetValueKind(localSymbol.Type, out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(localSymbol), kind);
        return true;
    }

    private static bool CanCompareIrTerms(SymbolicTerm left, SymbolicTerm right)
    {
        return left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    }

    private static void AddForLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddForLoopMonotonicLowerBoundStateFacts(ref state, forStatement, semanticModel, cancellationToken);
        AddForLoopMonotonicUpperBoundStateFacts(ref state, forStatement, semanticModel, cancellationToken);
    }

    private static void AddPreLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        string provenancePrefix,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddPreLoopMonotonicLowerBoundStateFacts(
            ref state,
            loopStatement,
            loopBody,
            provenancePrefix,
            semanticModel,
            cancellationToken);
        AddPreLoopMonotonicUpperBoundStateFacts(
            ref state,
            loopStatement,
            loopBody,
            provenancePrefix,
            semanticModel,
            cancellationToken);
    }

    private static void AddPreLoopMonotonicLowerBoundStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        string provenancePrefix,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var initializer in EnumeratePreLoopInitializerBoundTerms(loopStatement, semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.Bound.Kind != SmtValueKind.Int ||
                LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                initializer.BoundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                !LoopBodyMutationsPreserveLowerBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThanOrEqual,
                symbolTerm,
                initializer.Bound,
                loopStatement,
                provenancePrefix + ".lower-bound");
        }
    }

    private static void AddPreLoopMonotonicUpperBoundStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        string provenancePrefix,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddPreLoopMonotonicInitialUpperBoundStateFacts(
            ref state,
            loopStatement,
            loopBody,
            provenancePrefix,
            semanticModel,
            cancellationToken);

        foreach (var initializer in EnumeratePreLoopStrictUpperBoundInitializerTerms(loopStatement, semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.UpperBound.Kind != SmtValueKind.Int ||
                LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                initializer.BoundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                !LoopBodyMutationsPreserveUpperBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.LessThan,
                symbolTerm,
                initializer.UpperBound,
                loopStatement,
                provenancePrefix + ".strict-upper-bound");
        }
    }

    private static void AddPreLoopMonotonicInitialUpperBoundStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        string provenancePrefix,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var initializer in EnumeratePreLoopInitializerBoundTerms(loopStatement, semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.Bound.Kind != SmtValueKind.Int ||
                LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                initializer.BoundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                !LoopBodyMutationsPreserveUpperBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.LessThanOrEqual,
                symbolTerm,
                initializer.Bound,
                loopStatement,
                provenancePrefix + ".initial-upper-bound");
        }
    }

    private static void AddForLoopMonotonicLowerBoundStateFacts(
        ref SymbolicState state,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var initializer in EnumerateForLoopInitializerBoundTerms(forStatement, semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.Bound.Kind != SmtValueKind.Int ||
                StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                ForLoopConditionInvalidatesSymbolValue(forStatement, initializer.Symbol, semanticModel,
                    cancellationToken) ||
                initializer.BoundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                    ForLoopConditionInvalidatesSymbolValue(forStatement, symbol, semanticModel, cancellationToken) ||
                    ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                !ForLoopIncrementorsPreserveLowerBound(forStatement, initializer.Symbol, semanticModel,
                    cancellationToken))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThanOrEqual,
                symbolTerm,
                initializer.Bound,
                forStatement,
                "ir.path.for-loop-invariant.lower-bound");
        }
    }

    private static void AddForLoopMonotonicUpperBoundStateFacts(
        ref SymbolicState state,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddForLoopMonotonicInitialUpperBoundStateFacts(ref state, forStatement, semanticModel, cancellationToken);

        foreach (var initializer in EnumerateForLoopStrictUpperBoundInitializerTerms(forStatement, semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.UpperBound.Kind != SmtValueKind.Int ||
                StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                ForLoopConditionInvalidatesSymbolValue(forStatement, initializer.Symbol, semanticModel,
                    cancellationToken) ||
                initializer.BoundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                    ForLoopConditionInvalidatesSymbolValue(forStatement, symbol, semanticModel, cancellationToken) ||
                    ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                !ForLoopIncrementorsPreserveUpperBound(forStatement, initializer.Symbol, semanticModel,
                    cancellationToken))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.LessThan,
                symbolTerm,
                initializer.UpperBound,
                forStatement,
                "ir.path.for-loop-invariant.strict-upper-bound");
        }
    }

    private static void AddForLoopMonotonicInitialUpperBoundStateFacts(
        ref SymbolicState state,
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var initializer in EnumerateForLoopInitializerBoundTerms(forStatement, semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                symbolTerm.Kind != SmtValueKind.Int ||
                initializer.Bound.Kind != SmtValueKind.Int ||
                StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                ForLoopConditionInvalidatesSymbolValue(forStatement, initializer.Symbol, semanticModel,
                    cancellationToken) ||
                initializer.BoundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                    ForLoopConditionInvalidatesSymbolValue(forStatement, symbol, semanticModel, cancellationToken) ||
                    ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                !ForLoopIncrementorsPreserveUpperBound(forStatement, initializer.Symbol, semanticModel,
                    cancellationToken))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.LessThanOrEqual,
                symbolTerm,
                initializer.Bound,
                forStatement,
                "ir.path.for-loop-invariant.initial-upper-bound");
        }
    }

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumerateForLoopInitializerBoundTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        if (forStatement.Declaration != null)
            foreach (var declarator in forStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                    !TryLowerInitializerBoundTerm(
                        declarator.Initializer.Value,
                        localSymbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var lowerBound,
                        out var boundSymbols))
                    continue;

                yield return (localSymbol.OriginalDefinition, lowerBound, boundSymbols);
            }

        foreach (var expression in forStatement.Initializers)
        {
            if (expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                symbol is not ILocalSymbol and not IParameterSymbol ||
                !TryLowerInitializerBoundTerm(
                    assignment.Right,
                    symbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken,
                    out var lowerBound,
                    out var boundSymbols))
                continue;

            yield return (symbol.OriginalDefinition, lowerBound, boundSymbols);
        }
    }

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm UpperBound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumerateForLoopStrictUpperBoundInitializerTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        if (forStatement.Declaration != null)
            foreach (var declarator in forStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                    !TryGetStrictUpperBoundInitializerTerm(
                        declarator.Initializer.Value,
                        localSymbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var upperBound,
                        out var boundSymbols))
                    continue;

                yield return (localSymbol.OriginalDefinition, upperBound, boundSymbols);
            }

        foreach (var expression in forStatement.Initializers)
        {
            if (expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                symbol is not ILocalSymbol and not IParameterSymbol ||
                !TryGetStrictUpperBoundInitializerTerm(
                    assignment.Right,
                    symbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken,
                    out var upperBound,
                    out var boundSymbols))
                continue;

            yield return (symbol.OriginalDefinition, upperBound, boundSymbols);
        }
    }

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumeratePreLoopInitializerBoundTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel,
                     cancellationToken))
            if (TryLowerInitializerBoundTerm(
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

    private static IEnumerable<(ISymbol Symbol, SymbolicTerm UpperBound, IReadOnlyList<ISymbol> BoundSymbols)>
        EnumeratePreLoopStrictUpperBoundInitializerTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel,
                     cancellationToken))
            if (TryGetStrictUpperBoundInitializerTerm(
                    initializer.Value,
                    initializer.Symbol,
                    semanticModel,
                    cancellationToken,
                    out var upperBound,
                    out var boundSymbols) &&
                !AnyPriorStatementsInvalidateInitializer(
                    initializer,
                    loopStatement,
                    boundSymbols,
                    semanticModel,
                    cancellationToken))
                yield return (initializer.Symbol, upperBound, boundSymbols);
    }

    private static bool TryGetStrictUpperBoundInitializerTerm(
        ExpressionSyntax expression,
        ISymbol initializedSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm upperBound,
        out IReadOnlyList<ISymbol> upperBoundSymbols)
    {
        expression = UnwrapExpression(expression);
        if (expression is not BinaryExpressionSyntax binaryExpression)
        {
            upperBound = null!;
            upperBoundSymbols = Array.Empty<ISymbol>();
            return false;
        }

        if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
            TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractedValue) &&
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
            if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                rightValue < 0 &&
                TryLowerInitializerBoundTerm(
                    binaryExpression.Left,
                    initializedSymbol,
                    semanticModel,
                    cancellationToken,
                    out upperBound,
                    out upperBoundSymbols))
                return true;

            if (TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
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
        var referencedSymbols = GetReferencedLocalAndParameterSymbols(expression, semanticModel, cancellationToken);
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

    private static bool TryCreateSymbolTerm(ISymbol symbol, out SymbolicTerm term)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !TryGetValueKind(type, out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), kind);
        return true;
    }

    private static void AddRelationPathFact(
        ref SymbolicState state,
        SymbolicRelationOperator op,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode source,
        string provenance)
    {
        if (!CanCompareIrTerms(left, right)) return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(op, left, right),
            source,
            provenance);
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    private static SymbolicState RemoveStateFactsReferencingSymbol(SymbolicState state, ISymbol symbol)
    {
        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        return SymbolicIrReferenceScanner.RemoveVariableReferences(state, symbolName);
    }

    private static SymbolicState RemoveStateFactsReferencingImplicitThisMember(SymbolicState state, string memberName)
    {
        var variableName = ImplicitThisVariableName + "." + memberName;
        return SymbolicIrReferenceScanner.RemoveVariableOrMemberReferences(state, variableName);
    }

    private static bool ReferencesStateSymbol(SymbolicFact fact, string symbolName)
    {
        return ReferencesStateSymbol(fact.Atom, symbolName);
    }

    private static bool ReferencesStateSymbol(SymbolicAtom atom, string symbolName)
    {
        return atom switch
        {
            SymbolicTruthAtom truth => ReferencesStateSymbol(truth.Condition, symbolName),
            SymbolicRelationAtom relation => ReferencesStateSymbol(relation.Left, symbolName) ||
                                             ReferencesStateSymbol(relation.Right, symbolName),
            SymbolicStringPredicateAtom predicate => ReferencesStateSymbol(predicate.Value, symbolName) ||
                                                     ReferencesStateSymbol(predicate.Argument, symbolName),
            SymbolicBoundsAtom bounds => ReferencesStateSymbol(bounds.Index, symbolName) ||
                                         ReferencesStateSymbol(bounds.Length, symbolName),
            SymbolicFreshnessAtom freshness => ReferencesStateSymbol(freshness.Value, symbolName),
            SymbolicOwnershipAtom ownership => ReferencesStateSymbol(ownership.Value, symbolName),
            SymbolicAliasAtom alias => ReferencesStateSymbol(alias.Source, symbolName) ||
                                       ReferencesStateSymbol(alias.Target, symbolName),
            SymbolicBorrowAtom borrow => ReferencesStateSymbol(borrow.Owner, symbolName) ||
                                         ReferencesStateSymbol(borrow.Borrow, symbolName),
            SymbolicEscapeAtom escape => ReferencesStateSymbol(escape.Value, symbolName),
            SymbolicReturnedOwnershipAtom returnedOwnership => ReferencesStateSymbol(returnedOwnership.Value,
                symbolName),
            SymbolicMutationAtom mutation => ReferencesStateSymbol(mutation.Target, symbolName),
            SymbolicDisposalAtom disposal => ReferencesStateSymbol(disposal.Resource, symbolName),
            SymbolicResourceLifetimeAtom lifetime => ReferencesStateSymbol(lifetime.Resource, symbolName),
            SymbolicTypeTestAtom typeTest => ReferencesStateSymbol(typeTest.Value, symbolName),
            SymbolicExceptionPreconditionAtom exceptionPrecondition =>
                (exceptionPrecondition.Subject != null &&
                 ReferencesStateSymbol(exceptionPrecondition.Subject, symbolName)) ||
                ReferencesStateSymbol(exceptionPrecondition.Trigger, symbolName),
            _ => false
        };
    }

    private static bool ReferencesStateSymbol(SymbolicCondition condition, string symbolName)
    {
        return condition switch
        {
            SymbolicConstantCondition => false,
            SymbolicFactCondition factCondition => ReferencesStateSymbol(factCondition.Fact, symbolName),
            SymbolicNotCondition notCondition => ReferencesStateSymbol(notCondition.Operand, symbolName),
            SymbolicBinaryCondition binaryCondition => ReferencesStateSymbol(binaryCondition.Left, symbolName) ||
                                                       ReferencesStateSymbol(binaryCondition.Right, symbolName),
            _ => false
        };
    }

    private static bool ReferencesStateSymbol(SymbolicTerm term, string symbolName)
    {
        return term switch
        {
            SymbolicBooleanConstantTerm => false,
            SymbolicIntegerConstantTerm => false,
            SymbolicStringConstantTerm => false,
            SymbolicNullTerm => false,
            SymbolicVariableTerm variable => string.Equals(variable.Name, symbolName, StringComparison.Ordinal),
            SymbolicMemberTerm member => ReferencesStateSymbol(member.Receiver, symbolName),
            SymbolicElementTerm element => ReferencesStateSymbol(element.Receiver, symbolName) ||
                                           ReferencesStateSymbol(element.Index, symbolName),
            SymbolicMultiElementTerm element => ReferencesStateSymbol(element.Receiver, symbolName) ||
                                                element.Indices.Any(index =>
                                                    ReferencesStateSymbol(index, symbolName)),
            SymbolicFromEndIndexTerm fromEnd => ReferencesStateSymbol(fromEnd.Value, symbolName),
            SymbolicStringContentTerm stringContent => ReferencesStateSymbol(stringContent.Reference, symbolName),
            SymbolicStringConcatTerm concat => ReferencesStateSymbol(concat.Left, symbolName) ||
                                               ReferencesStateSymbol(concat.Right, symbolName),
            SymbolicNullableHasValueTerm nullableHasValue => string.Equals(nullableHasValue.NullableName, symbolName,
                StringComparison.Ordinal),
            SymbolicNullableValueTerm nullableValue => string.Equals(nullableValue.NullableName, symbolName,
                StringComparison.Ordinal),
            SymbolicLengthTerm length => ReferencesStateSymbol(length.Value, symbolName),
            SymbolicArrayDimensionLengthTerm arrayLength => ReferencesStateSymbol(arrayLength.Value, symbolName),
            SymbolicCountTerm count => ReferencesStateSymbol(count.Value, symbolName),
            SymbolicBinaryTerm binary => ReferencesStateSymbol(binary.Left, symbolName) ||
                                         ReferencesStateSymbol(binary.Right, symbolName),
            SymbolicConditionalTerm conditional => ReferencesStateSymbol(conditional.Condition, symbolName) ||
                                                   ReferencesStateSymbol(conditional.WhenTrue, symbolName) ||
                                                   ReferencesStateSymbol(conditional.WhenFalse, symbolName),
            _ => false
        };
    }

    private static bool TryGetCurrentStateSymbolValueTerm(
        SymbolicState state,
        ISymbol symbol,
        out SymbolicTerm valueTerm)
    {
        valueTerm = null!;
        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        for (var index = state.PathConditions.Length - 1; index >= 0; index--)
            if (TryGetStateEqualityValueTerm(state.PathConditions[index], symbolName, out valueTerm))
                return true;

        for (var index = state.Facts.Length - 1; index >= 0; index--)
            if (TryGetStateEqualityValueTerm(state.Facts[index], symbolName, out valueTerm))
                return true;

        return false;
    }

    private static bool IsKnownNonNullReferenceSymbol(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownReferenceNullState(state, symbol, out var isNull) && !isNull;
    }

    private static bool IsKnownNullReferenceSymbol(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownReferenceNullState(state, symbol, out var isNull) && isNull;
    }

    private static bool TryGetKnownReferenceNullState(
        SymbolicState state,
        ISymbol symbol,
        out bool isNull)
    {
        return TryGetKnownBooleanState(state, symbol, TryGetReferenceNullFactState, out isNull);
    }

    private static bool TryGetReferenceNullFactState(
        SymbolicFact fact,
        string symbolName,
        out bool isNull)
    {
        isNull = false;
        if (fact.Atom is not SymbolicRelationAtom relation)
            return false;

        if (relation.Left is SymbolicVariableTerm { ValueKind: SmtValueKind.Reference } leftVariable &&
            string.Equals(leftVariable.Name, symbolName, StringComparison.Ordinal) &&
            relation.Right is SymbolicNullTerm)
            if (relation.Operator is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual)
            {
                isNull = (relation.Operator == SymbolicRelationOperator.Equal) == fact.Polarity;
                return true;
            }

        if (relation.Right is SymbolicVariableTerm { ValueKind: SmtValueKind.Reference } rightVariable &&
            string.Equals(rightVariable.Name, symbolName, StringComparison.Ordinal) &&
            relation.Left is SymbolicNullTerm)
            if (relation.Operator is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual)
            {
                isNull = (relation.Operator == SymbolicRelationOperator.Equal) == fact.Polarity;
                return true;
            }

        return false;
    }

    private static bool IsKnownNullableHasValueSymbol(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownNullableHasValueState(state, symbol, out var hasValue) && hasValue;
    }

    private static bool IsKnownNullableNoValueSymbol(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownNullableHasValueState(state, symbol, out var hasValue) && !hasValue;
    }

    private static bool TryGetKnownNullableHasValueState(
        SymbolicState state,
        ISymbol symbol,
        out bool hasValue)
    {
        return TryGetKnownBooleanState(state, symbol, TryGetNullableHasValueFactState, out hasValue);
    }

    private static bool TryGetKnownBooleanState(
        SymbolicState state,
        ISymbol symbol,
        TryGetBooleanFactState tryGetFactState,
        out bool value)
    {
        value = false;
        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        for (var index = state.PathConditions.Length - 1; index >= 0; index--)
            if (TryGetKnownBooleanState(state.PathConditions[index], symbolName, tryGetFactState, out value))
                return true;

        for (var index = state.Facts.Length - 1; index >= 0; index--)
            if (tryGetFactState(state.Facts[index], symbolName, out value))
                return true;

        return false;
    }

    private static bool TryGetKnownBooleanState(
        SymbolicCondition condition,
        string symbolName,
        TryGetBooleanFactState tryGetFactState,
        out bool value)
    {
        switch (condition)
        {
            case SymbolicFactCondition factCondition:
                return tryGetFactState(factCondition.Fact, symbolName, out value);
            case SymbolicNotCondition notCondition
                when TryGetKnownBooleanState(
                    notCondition.Operand,
                    symbolName,
                    tryGetFactState,
                    out value):
                value = !value;
                return true;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition:
                if (TryGetKnownBooleanState(
                        andCondition.Left,
                        symbolName,
                        tryGetFactState,
                        out value))
                    return true;

                return TryGetKnownBooleanState(
                    andCondition.Right,
                    symbolName,
                    tryGetFactState,
                    out value);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition
                when TryGetKnownBooleanState(
                         orCondition.Left,
                         symbolName,
                         tryGetFactState,
                         out var leftValue) &&
                     TryGetKnownBooleanState(
                         orCondition.Right,
                         symbolName,
                         tryGetFactState,
                         out var rightValue) &&
                     leftValue == rightValue:
                value = leftValue;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryGetNullableHasValueFactState(
        SymbolicFact fact,
        string symbolName,
        out bool hasValue)
    {
        hasValue = false;
        switch (fact.Atom)
        {
            case SymbolicTruthAtom { Condition: SymbolicNullableHasValueTerm nullableHasValue }:
                if (string.Equals(nullableHasValue.NullableName, symbolName, StringComparison.Ordinal))
                {
                    hasValue = fact.Polarity;
                    return true;
                }

                break;

            case SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicNullableHasValueTerm leftNullableHasValue,
                Right: SymbolicBooleanConstantTerm rightBoolean
            } when string.Equals(leftNullableHasValue.NullableName, symbolName, StringComparison.Ordinal):
                hasValue = rightBoolean.Value == fact.Polarity;
                return true;

            case SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicBooleanConstantTerm leftBoolean,
                Right: SymbolicNullableHasValueTerm rightNullableHasValue
            } when string.Equals(rightNullableHasValue.NullableName, symbolName, StringComparison.Ordinal):
                hasValue = leftBoolean.Value == fact.Polarity;
                return true;
        }

        return false;
    }

    private delegate bool TryGetBooleanFactState(SymbolicFact fact, string symbolName, out bool value);

    private static bool TryGetStateEqualityValueTerm(
        SymbolicCondition condition,
        string symbolName,
        out SymbolicTerm valueTerm)
    {
        valueTerm = null!;
        return condition is SymbolicFactCondition factCondition &&
               TryGetStateEqualityValueTerm(factCondition.Fact, symbolName, out valueTerm);
    }

    private static bool TryGetStateEqualityValueTerm(
        SymbolicFact fact,
        string symbolName,
        out SymbolicTerm valueTerm)
    {
        valueTerm = null!;
        if (!fact.Polarity ||
            fact.Atom is not SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: var left,
                Right: var right
            })
            return false;

        if (left is SymbolicVariableTerm { ValueKind: SmtValueKind.Int } leftVariable &&
            string.Equals(leftVariable.Name, symbolName, StringComparison.Ordinal) &&
            right.Kind == SmtValueKind.Int)
        {
            valueTerm = right;
            return true;
        }

        if (right is SymbolicVariableTerm { ValueKind: SmtValueKind.Int } rightVariable &&
            string.Equals(rightVariable.Name, symbolName, StringComparison.Ordinal) &&
            left.Kind == SmtValueKind.Int)
        {
            valueTerm = left;
            return true;
        }

        return false;
    }

    private static bool TryCreateIncrementOrDecrementStateTerm(
        SymbolicTerm previousValue,
        int delta,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        if (previousValue.Kind != SmtValueKind.Int ||
            delta is not 1 and not -1)
            return false;

        if (previousValue is SymbolicIntegerConstantTerm integerConstant)
        {
            updatedValue = new SymbolicIntegerConstantTerm(integerConstant.Value + delta);
            return true;
        }

        updatedValue = new SymbolicBinaryTerm(
            delta > 0
                ? SymbolicBinaryTermOperator.Add
                : SymbolicBinaryTermOperator.Subtract,
            previousValue,
            new SymbolicIntegerConstantTerm(1));
        return true;
    }

    private static bool TryCreateCompoundAssignmentStateTerm(
        SymbolicTerm previousValue,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISymbol targetSymbol,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            assignment.Right,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (previousValue.Kind != SmtValueKind.Int ||
            !TryGetCompoundAssignmentStateOperator(assignment.Kind(), out var binaryOperator) ||
            lowering is not { IsExact: true, Value: { } rightTerm } ||
            rightTerm.Kind != SmtValueKind.Int ||
            ReferencesStateSymbol(previousValue, SymbolicFactFactory.GetSmtVariableName(targetSymbol)) ||
            ReferencesStateSymbol(rightTerm, SymbolicFactFactory.GetSmtVariableName(targetSymbol)))
            return false;

        if (previousValue is SymbolicIntegerConstantTerm leftConstant &&
            rightTerm is SymbolicIntegerConstantTerm rightConstant)
            switch (binaryOperator)
            {
                case SymbolicBinaryTermOperator.Add:
                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value + rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Subtract:
                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value - rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Multiply:
                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value * rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Divide:
                    if (rightConstant.Value == 0) return false;

                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value / rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Remainder:
                    if (rightConstant.Value == 0) return false;

                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value % rightConstant.Value);
                    return true;
            }

        if (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder &&
            rightTerm is SymbolicIntegerConstantTerm { Value: 0 })
            return false;

        updatedValue = new SymbolicBinaryTerm(binaryOperator, previousValue, rightTerm);
        return true;
    }

    private static bool TryGetCompoundAssignmentStateOperator(
        SyntaxKind kind,
        out SymbolicBinaryTermOperator binaryOperator)
    {
        switch (kind)
        {
            case SyntaxKind.AddAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Add;
                return true;
            case SyntaxKind.SubtractAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Subtract;
                return true;
            case SyntaxKind.MultiplyAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Multiply;
                return true;
            case SyntaxKind.DivideAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Divide;
                return true;
            case SyntaxKind.ModuloAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Remainder;
                return true;
            default:
                binaryOperator = default;
                return false;
        }
    }

    private static void AddForeachBodyEntryStateFacts(
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
        AddReferenceNullCondition(
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

        if (!TryGetFiniteElementExpressions(expressionSyntax, out var elementExpressions) &&
            !TryGetPriorAssignedFiniteElementExpressions(
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
            if (ExpressionReferencesSymbol(
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
                IsDefinitelyNonNullReferenceValue(elementExpression, semanticModel, cancellationToken);
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

    private static void AddThrowGuardedExpressionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax guardedStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string nonNullProvenance = "ir.path.foreach-entry.throw-guarded-not-null")
    {
        if (!TryGetThrowGuardedValue(
                expression,
                out var effectiveValueExpression,
                out var guardExpression,
                out var guardBranchWhenTrue,
                out var requiresNonNullValue))
            return;

        if (guardExpression != null)
        {
            if (!AnyConditionSymbolInvalidatedInStatement(guardExpression, guardedStatement, semanticModel,
                    cancellationToken))
                AddReachabilityCondition(ref state, guardExpression, guardBranchWhenTrue, semanticModel,
                    cancellationToken);
        }
        else if (requiresNonNullValue &&
                 !ReferenceIdentityFactIsInvalidatedInStatement(
                     effectiveValueExpression,
                     guardedStatement,
                     semanticModel,
                     cancellationToken))
        {
            AddReferenceNullCondition(
                ref state,
                effectiveValueExpression,
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
        if (!IsSupportedForeachLengthReceiver(expressionSyntax) &&
            !IsSupportedForeachLengthReceiver(type))
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
            foreach (var declarator in forStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null) continue;

                RemoveStateFactsInvalidatedByNestedMutations(
                    ref state,
                    declarator.Initializer.Value,
                    semanticModel,
                    cancellationToken);
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    AddAssignedValueStateFacts(
                        ref state,
                        localSymbol.OriginalDefinition,
                        declarator.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        "ir.path.for-initializer");
            }

        foreach (var initializer in forStatement.Initializers)
        {
            if (initializer is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;

            RemoveStateFactsInvalidatedByNestedMutations(
                ref state,
                assignment.Left,
                semanticModel,
                cancellationToken);
            RemoveStateFactsInvalidatedByNestedMutations(
                ref state,
                assignment.Right,
                semanticModel,
                cancellationToken);
            var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                AddAssignedValueStateFacts(
                    ref state,
                    assignedSymbol.OriginalDefinition,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    "ir.path.for-initializer");
        }

        return state.Normalize();
    }

    private static void AddMethodEntryNullableFlowStateFacts(
        ref SymbolicState state,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is IMethodSymbol
            {
                IsStatic: false,
                ContainingType.IsReferenceType: true
            } method)
        {
            var thisFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.NotEqual,
                    new SymbolicVariableTerm(ImplicitThisVariableName, SmtValueKind.Reference),
                    new SymbolicNullTerm()),
                site,
                "ir.path.method-entry.this-non-null",
                method);
            state = state.AddPathCondition(new SymbolicFactCondition(thisFact));
        }

        foreach (var parameter in GetDefinitelyNotNullEntryParameters(
                     site,
                     semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(parameter, out var parameterTerm) ||
                parameterTerm.Kind != SmtValueKind.Reference)
                continue;

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.NotEqual,
                    parameterTerm,
                    new SymbolicNullTerm()),
                site,
                "ir.path.method-entry.nullability-contract",
                parameter);
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }
    }

    private static IEnumerable<IParameterSymbol> GetDefinitelyNotNullEntryParameters(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is not IMethodSymbol method)
            yield break;

        foreach (var parameter in method.Parameters)
            if (NullableFlowFacts.GetParameterInputState(parameter) == NullableFlowFactState.NotNull &&
                NullableFlowFacts.HasExplicitNotNullInputContract(parameter))
                yield return (IParameterSymbol)parameter.OriginalDefinition;
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
        AddCompletedLoopStatementStateFacts(ref state, statement, semanticModel, cancellationToken);
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
                NodeMayMutateSymbolThroughReference(incrementor, symbol, semanticModel, cancellationToken))
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
            if (StatementInvalidatesSymbolValue(statement, initializer.Symbol, semanticModel, cancellationToken) ||
                boundSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken)))
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
                NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool ForLoopIncrementorsPreserveLowerBound(
        ForStatementSyntax forStatement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var incrementor in forStatement.Incrementors)
        {
            if (!ExpressionReferencesSymbol(incrementor, symbol, semanticModel, cancellationToken)) continue;

            if (!IncrementorPreservesLowerBound(incrementor, symbol, semanticModel, cancellationToken)) return false;
        }

        return true;
    }

    private static bool LoopBodyMutationsPreserveLowerBound(
        StatementSyntax loopBody,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in loopBody.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken)) return false;

            if (!NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken)) continue;

            if (node is not ExpressionSyntax expression ||
                !IncrementorPreservesLowerBound(expression, symbol, semanticModel, cancellationToken))
                return false;
        }

        return true;
    }

    private static bool IncrementorPreservesLowerBound(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (TryGetIncrementedOrDecrementedSymbol(expression, semanticModel, cancellationToken, out var unarySymbol,
                out var delta) &&
            SymbolEqualityComparer.Default.Equals(unarySymbol, symbol))
            return delta >= 0;

        if (expression is not AssignmentExpressionSyntax assignment ||
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
            !SymbolEqualityComparer.Default.Equals(assignedSymbol.OriginalDefinition, symbol))
            return false;

        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
            TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var addedValue))
            return addedValue >= 0;

        if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
            TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var subtractedValue))
            return subtractedValue <= 0;

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return TryIsSelfPlusNonNegativeConstant(assignment.Right, symbol, semanticModel, cancellationToken);

        return false;
    }

    private static bool TryIsSelfPlusNonNegativeConstant(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (expression is not BinaryExpressionSyntax binaryExpression) return false;

        if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            return (IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                    TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken,
                        out var rightValue) &&
                    rightValue >= 0) ||
                   (IsSymbolReference(binaryExpression.Right, symbol, semanticModel, cancellationToken) &&
                    TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken,
                        out var leftValue) &&
                    leftValue >= 0);

        return binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
               IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
               TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken,
                   out var subtractValue) &&
               subtractValue <= 0;
    }

    private static bool ForLoopIncrementorsPreserveUpperBound(
        ForStatementSyntax forStatement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var incrementor in forStatement.Incrementors)
        {
            if (!ExpressionReferencesSymbol(incrementor, symbol, semanticModel, cancellationToken)) continue;

            if (!IncrementorPreservesUpperBound(incrementor, symbol, semanticModel, cancellationToken)) return false;
        }

        return true;
    }

    private static bool LoopBodyMutationsPreserveUpperBound(
        StatementSyntax loopBody,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in loopBody.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken)) return false;

            if (!NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken)) continue;

            if (node is not ExpressionSyntax expression ||
                !IncrementorPreservesUpperBound(expression, symbol, semanticModel, cancellationToken))
                return false;
        }

        return true;
    }

    private static bool IncrementorPreservesUpperBound(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (TryGetIncrementedOrDecrementedSymbol(expression, semanticModel, cancellationToken, out var unarySymbol,
                out var delta) &&
            SymbolEqualityComparer.Default.Equals(unarySymbol, symbol))
            return delta <= 0;

        if (expression is not AssignmentExpressionSyntax assignment ||
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
            !SymbolEqualityComparer.Default.Equals(assignedSymbol.OriginalDefinition, symbol))
            return false;

        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
            TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var addedValue))
            return addedValue <= 0;

        if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
            TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var subtractedValue))
            return subtractedValue >= 0;

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return TryIsSelfPlusNonPositiveConstant(assignment.Right, symbol, semanticModel, cancellationToken);

        return false;
    }

    private static bool TryIsSelfPlusNonPositiveConstant(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (expression is not BinaryExpressionSyntax binaryExpression) return false;

        if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            return (IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                    TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken,
                        out var rightValue) &&
                    rightValue <= 0) ||
                   (IsSymbolReference(binaryExpression.Right, symbol, semanticModel, cancellationToken) &&
                    TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken,
                        out var leftValue) &&
                    leftValue <= 0);

        return binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
               IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
               TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken,
                   out var subtractValue) &&
               subtractValue >= 0;
    }

    private static bool IsSymbolReference(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol;
        return expressionSymbol != null &&
               SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol);
    }

    private static bool IsLoopBodyBlock(BlockSyntax block)
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

    private static bool AnyConditionSymbolMutatedInStatement(
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
                UnwrapExpression(tupleAssignment.Left) is TupleExpressionSyntax leftTuple &&
                leftTuple.Arguments.Any(argument =>
                    ExpressionReferencesAnySymbol(argument.Expression, conditionSymbols, semanticModel,
                        cancellationToken)))
                return true;

            if (!TryGetMutatedExpression(node, out var mutatedExpression)) continue;

            var mutatedSymbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol
                ?.OriginalDefinition;
            if (mutatedSymbol != null &&
                conditionSymbols.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, mutatedSymbol)))
                return true;
        }

        return false;
    }

    private static bool AnyConditionSymbolInvalidatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        return conditionSymbols.Count != 0 &&
               conditionSymbols.Any(symbol =>
                   StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken));
    }

    private static bool ReferenceIdentityFactIsInvalidatedInStatement(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol
            ?.OriginalDefinition;
        if (symbol is ILocalSymbol or IParameterSymbol)
            return StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken);

        return AnyConditionSymbolInvalidatedInStatement(
            expression,
            statement,
            semanticModel,
            cancellationToken);
    }

    private static bool StatementMutatesSymbol(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (TryGetMutatedExpression(node, out var mutatedExpression) &&
                ExpressionReferencesSymbol(mutatedExpression, symbol, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool ExpressionMutatesAnySymbol(
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

    private static bool IsLocalOrParameterReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol
            ?.OriginalDefinition;
        return symbol is ILocalSymbol or IParameterSymbol;
    }

    private static bool AnyReferencedSymbolAssignedBeforeUse(
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

    private static bool AnySwitchStatementConditionSymbolAssignedBeforeUse(
        SwitchStatementSyntax switchStatement,
        SwitchSectionSyntax section,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return AnySymbolAssignedBeforeUse(
            GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken),
            section,
            useSpanStart,
            semanticModel,
            cancellationToken);
    }

    private static bool AnySwitchExpressionConditionSymbolAssignedBeforeUse(
        SwitchExpressionSyntax switchExpression,
        SwitchExpressionArmSyntax arm,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return AnySymbolAssignedBeforeUse(
            GetSwitchExpressionConditionSymbols(switchExpression, semanticModel, cancellationToken),
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

    private static bool IsSymbolAssignedBetween(
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
        return TryGetMutatedExpression(node, out var mutatedExpression) &&
               ExpressionReferencesSymbol(mutatedExpression, symbol, semanticModel, cancellationToken);
    }

    private static bool TryGetMutatedExpression(SyntaxNode node, out ExpressionSyntax expression)
    {
        switch (node)
        {
            case AssignmentExpressionSyntax assignment:
                expression = assignment.Left;
                return true;
            case PrefixUnaryExpressionSyntax prefixUnary
                when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixUnary.IsKind(SyntaxKind.PreDecrementExpression):
                expression = prefixUnary.Operand;
                return true;
            case PostfixUnaryExpressionSyntax postfixUnary
                when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixUnary.IsKind(SyntaxKind.PostDecrementExpression):
                expression = postfixUnary.Operand;
                return true;
            case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                expression = argument.Expression;
                return true;
            default:
                expression = null!;
                return false;
        }
    }

    private static IReadOnlyList<ISymbol> GetReferencedLocalAndParameterSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (node is not ExpressionSyntax expression) continue;

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
            if (symbol is ILocalSymbol or IParameterSymbol &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);
        }

        return symbols;
    }

    private static IReadOnlyList<ISymbol> GetConditionDependencySymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        AddReferencedSymbols(root, semanticModel, cancellationToken, symbols);
        AddDeclaredPatternSymbols(root, semanticModel, cancellationToken, symbols);
        AddMemberNotNullWhenTargetSymbols(root, semanticModel, cancellationToken, symbols);
        return symbols;
    }

    private static void RemoveStateFactsInvalidatedByContainingBlockEntry(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in GetContainingBlockEntryAssignedSymbols(block, semanticModel, cancellationToken))
            state = RemoveStateFactsReferencingSymbol(state, symbol);
    }

    private static bool TryAddContainingBlockEntryInlineAssignmentStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                return TryAddInlineAssignmentReachabilityState(
                    ref state,
                    ifStatement.Condition,
                    true,
                    semanticModel,
                    cancellationToken);
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                return TryAddInlineAssignmentReachabilityState(
                    ref state,
                    ifStatement.Condition,
                    false,
                    semanticModel,
                    cancellationToken);
            case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                if (!TryAddInlineAssignmentReachabilityState(
                        ref state,
                        whileStatement.Condition,
                        true,
                        semanticModel,
                        cancellationToken))
                    return false;

                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                return true;
            case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                if (forStatement.Condition == null ||
                    !TryAddInlineAssignmentReachabilityState(
                        ref state,
                        forStatement.Condition,
                        true,
                        semanticModel,
                        cancellationToken))
                    return false;

                AddForLoopBodyInvariantStateFacts(
                    ref state,
                    forStatement,
                    semanticModel,
                    cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private static IEnumerable<ISymbol> GetContainingBlockEntryAssignedSymbols(
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax? condition = null;
        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                condition = ifStatement.Condition;
                break;
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                condition = ifStatement.Condition;
                break;
            case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                condition = whileStatement.Condition;
                break;
            case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                condition = forStatement.Condition;
                break;
        }

        if (condition == null) yield break;

        foreach (var assignment in condition
                     .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;

            var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (assignedSymbol is ILocalSymbol or IParameterSymbol) yield return assignedSymbol.OriginalDefinition;
        }
    }

    private static void AddContainingBlockEntryStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                AddReachabilityCondition(ref state, ifStatement.Condition, true, semanticModel, cancellationToken);
                break;
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                AddReachabilityCondition(ref state, ifStatement.Condition, false, semanticModel, cancellationToken);
                break;
            case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                AddReachabilityCondition(ref state, whileStatement.Condition, true, semanticModel, cancellationToken);
                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case DoStatementSyntax doStatement when ReferenceEquals(doStatement.Statement, block):
                AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    doStatement,
                    doStatement.Statement,
                    "ir.path.do-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                if (forStatement.Condition != null)
                    AddReachabilityCondition(ref state, forStatement.Condition, true, semanticModel, cancellationToken);

                AddForLoopBodyInvariantStateFacts(
                    ref state,
                    forStatement,
                    semanticModel,
                    cancellationToken);
                break;
            case ForEachStatementSyntax forEachStatement when ReferenceEquals(forEachStatement.Statement, block):
                AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachStatement.Expression,
                    forEachStatement,
                    forEachStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
            case ForEachVariableStatementSyntax forEachVariableStatement
                when ReferenceEquals(forEachVariableStatement.Statement, block):
                AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachVariableStatement.Expression,
                    forEachVariableStatement,
                    forEachVariableStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
        }
    }

    private static void AddPriorStatementStateFacts(
        ref SymbolicState state,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var declarator in localDeclaration.Declaration.Variables)
            {
                if (declarator.Initializer == null) continue;

                RemoveStateFactsInvalidatedByNestedMutations(
                    ref state,
                    declarator.Initializer.Value,
                    semanticModel,
                    cancellationToken);
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    AddAssignedValueStateFacts(
                        ref state,
                        localSymbol.OriginalDefinition,
                        declarator.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        "ir.path.prior-statement");

                AddNormalCompletionStateFacts(
                    ref state,
                    declarator.Initializer.Value,
                    localDeclaration,
                    false,
                    semanticModel,
                    cancellationToken);
            }

            return;
        }

        if (statement is ExpressionStatementSyntax expressionStatement &&
            expressionStatement.Expression is AssignmentExpressionSyntax assignment)
        {
            AddAssignmentExpressionStateFacts(
                ref state,
                assignment,
                expressionStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is ExpressionStatementSyntax unaryExpressionStatement &&
            TryGetIncrementedOrDecrementedSymbol(
                unaryExpressionStatement.Expression,
                semanticModel,
                cancellationToken,
                out var mutatedSymbol,
                out var delta))
        {
            if (TryGetCurrentStateSymbolValueTerm(state, mutatedSymbol, out var previousValueTerm) &&
                TryCreateIncrementOrDecrementStateTerm(previousValueTerm, delta, out var updatedValueTerm) &&
                TryCreateSymbolTerm(mutatedSymbol, out var targetTerm) &&
                targetTerm.Kind == SmtValueKind.Int &&
                !ReferencesStateSymbol(updatedValueTerm, SymbolicFactFactory.GetSmtVariableName(mutatedSymbol)))
            {
                state = RemoveStateFactsReferencingSymbol(state, mutatedSymbol);
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.Equal,
                    targetTerm,
                    updatedValueTerm,
                    unaryExpressionStatement.Expression,
                    delta >= 0
                        ? "ir.path.prior-statement.increment"
                        : "ir.path.prior-statement.decrement");
                return;
            }

            state = RemoveStateFactsReferencingSymbol(state, mutatedSymbol);
            return;
        }

        if (statement is BlockSyntax completedBlock)
        {
            AddCompletedBlockStateFacts(
                ref state,
                completedBlock,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is TryStatementSyntax completedTryStatement)
        {
            AddCompletedTryStatementStateFacts(
                ref state,
                completedTryStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        var stateBeforeStatement = state;
        RemoveStateFactsInvalidatedByNestedMutations(
            ref state,
            statement,
            semanticModel,
            cancellationToken);
        if (statement is UsingStatementSyntax completedUsingStatement)
        {
            if (completedUsingStatement.Expression != null)
                AddNormalCompletionStateFacts(
                    ref state,
                    completedUsingStatement.Expression,
                    completedUsingStatement,
                    true,
                    semanticModel,
                    cancellationToken);

            if (completedUsingStatement.Declaration != null)
                foreach (var declarator in completedUsingStatement.Declaration.Variables)
                    if (declarator.Initializer != null)
                        AddNormalCompletionStateFacts(
                            ref state,
                            declarator.Initializer.Value,
                            completedUsingStatement,
                            true,
                            semanticModel,
                            cancellationToken);

            return;
        }

        if (statement is IfStatementSyntax completedIfStatement)
        {
            AddCompletedIfStatementStateFacts(
                ref state,
                completedIfStatement,
                stateBeforeStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is SwitchStatementSyntax completedSwitchStatement)
        {
            AddCompletedSwitchStatementStateFacts(
                ref state,
                completedSwitchStatement,
                stateBeforeStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is ExpressionStatementSyntax completedExpressionStatement)
            AddNormalCompletionStateFacts(
                ref state,
                completedExpressionStatement.Expression,
                completedExpressionStatement,
                true,
                semanticModel,
                cancellationToken);
        else
            AddCompletedLoopStatementStateFacts(
                ref state,
                statement,
                semanticModel,
                cancellationToken);
    }

    private static void AddCompletedTryStatementStateFacts(
        ref SymbolicState state,
        TryStatementSyntax tryStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var entryState = state;
        var completionStates = new List<SymbolicState>();

        if (!StatementDefinitelyExits(tryStatement.Block, semanticModel, cancellationToken))
        {
            var tryState = entryState;
            AddCompletedBlockStateFacts(
                ref tryState,
                tryStatement.Block,
                semanticModel,
                cancellationToken);
            if (!tryState.IsContradictory) completionStates.Add(tryState);
        }

        foreach (var catchClause in tryStatement.Catches)
        {
            var branchLimit = SymbolicAnalysisLimitContext.Limits.MaxTryCompletionBranches;
            if (completionStates.Count >= branchLimit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryCompletionBranches,
                    branchLimit,
                    completionStates.Count + 1,
                    tryStatement,
                    "program_point.try_completion_branches");
                break;
            }

            if (!CatchClauseCanHandleKnownThrow(tryStatement, catchClause, semanticModel, cancellationToken) ||
                StatementDefinitelyExits(catchClause.Block, semanticModel, cancellationToken))
                continue;

            var catchState = entryState;
            RemoveStateFactsInvalidatedByNestedMutations(
                ref catchState,
                tryStatement.Block,
                semanticModel,
                cancellationToken);
            AddCompletedBlockStateFacts(
                ref catchState,
                catchClause.Block,
                semanticModel,
                cancellationToken);
            if (!catchState.IsContradictory) completionStates.Add(catchState);
        }

        if (completionStates.Count == 0)
        {
            state = MarkContradictory(entryState);
            return;
        }

        state = MergeCompletedAlternativeStates(completionStates, entryState, tryStatement);
        if (tryStatement.Finally?.Block is { } finallyBlock)
        {
            AddCompletedBlockStateFacts(
                ref state,
                finallyBlock,
                semanticModel,
                cancellationToken);
            if (StatementDefinitelyExits(finallyBlock, semanticModel, cancellationToken))
                state = MarkContradictory(state);
        }

        foreach (var hiddenSymbol in GetLocalsDeclaredInside(tryStatement, semanticModel, cancellationToken))
            state = RemoveStateFactsReferencingSymbol(state, hiddenSymbol);
    }

    private static bool CatchClauseCanHandleKnownThrow(
        TryStatementSyntax tryStatement,
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (catchClause.Filter?.FilterExpression is { } filterExpression)
        {
            var filterValue = semanticModel.GetConstantValue(filterExpression, cancellationToken);
            if (filterValue is { HasValue: true, Value: false }) return false;
        }

        if (catchClause.Declaration?.Type is not { } caughtTypeSyntax ||
            tryStatement.Block.Statements.Count != 1 ||
            tryStatement.Block.Statements[0] is not ThrowStatementSyntax { Expression: { } thrownExpression })
            return true;

        var thrownType = semanticModel.GetTypeInfo(thrownExpression, cancellationToken).Type;
        var caughtType = semanticModel.GetTypeInfo(caughtTypeSyntax, cancellationToken).Type;
        if (thrownType == null || caughtType == null) return true;

        return semanticModel.Compilation.ClassifyConversion(thrownType, caughtType).IsImplicit;
    }

    private static SymbolicState MergeCompletedAlternativeStates(
        IReadOnlyList<SymbolicState> states,
        SymbolicState entryState,
        TryStatementSyntax tryStatement)
    {
        if (states.Count == 1) return states[0];

        var commonFactKeys = new HashSet<string>(
            states[0].Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        for (var index = 1; index < states.Count; index++)
            commonFactKeys.IntersectWith(states[index].Facts.Select(SymbolicState.CreateProofFactKey));

        var commonFacts = states[0].Facts
            .Where(fact => commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact)))
            .ToArray();
        var commonConditions = SymbolicStateMerger.MergePathConditionsAcrossAll(states);
        var entryFactKeys = new HashSet<string>(
            entryState.Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        var entryConditionKeys = new HashSet<string>(
            entryState.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        var retainedFacts = entryState.Facts.ToList();
        var retainedConditions = entryState.PathConditions.ToList();
        var retainedFactKeys = new HashSet<string>(entryFactKeys, StringComparer.Ordinal);
        var retainedConditionKeys = new HashSet<string>(entryConditionKeys, StringComparer.Ordinal);
        var addedCount = 0;
        var mergeLimit = SymbolicAnalysisLimitContext.Limits.MaxMergedTryFacts;

        foreach (var fact in commonFacts)
        {
            var key = SymbolicState.CreateProofFactKey(fact);
            if (!retainedFactKeys.Add(key)) continue;

            if (addedCount >= mergeLimit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge,
                    mergeLimit,
                    addedCount + 1,
                    tryStatement,
                    "program_point.try_fact_merge");
                break;
            }

            retainedFacts.Add(fact);
            addedCount++;
        }

        foreach (var condition in commonConditions)
        {
            var key = SymbolicState.CreateProofConditionKey(condition);
            if (!retainedConditionKeys.Add(key)) continue;

            if (addedCount >= mergeLimit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge,
                    mergeLimit,
                    addedCount + 1,
                    tryStatement,
                    "program_point.try_fact_merge");
                break;
            }

            retainedConditions.Add(condition);
            addedCount++;
        }

        var commonVersions = states[0].SymbolVersions
            .Where(pair => states.Skip(1).All(state =>
                state.SymbolVersions.TryGetValue(pair.Key, out var version) && version == pair.Value))
            .ToArray();

        return new SymbolicState(
            retainedFacts,
            retainedConditions,
            commonVersions,
            states.All(static candidate => candidate.IsContradictory)).Normalize();
    }

    private static void AddCompletedLoopStatementStateFacts(
        ref SymbolicState state,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (statement is WhileStatementSyntax or ForStatementSyntax or DoStatementSyntax &&
            StatementDefinitelyExits(statement, semanticModel, cancellationToken))
        {
            state = MarkContradictory(state);
            return;
        }

        switch (statement)
        {
            case WhileStatementSyntax whileStatement
                when CanAssumeLoopConditionFalseAfterNormalExit(whileStatement, whileStatement.Statement):
                AddReachabilityCondition(
                    ref state,
                    whileStatement.Condition,
                    false,
                    semanticModel,
                    cancellationToken);
                AddLoopBodyInvariantStateFacts(ref state, whileStatement, semanticModel, cancellationToken);
                break;
            case WhileStatementSyntax whileStatement
                when TryCreateGuardedBreakLoopExitSymbolicCondition(
                    whileStatement,
                    whileStatement.Statement,
                    whileStatement.Condition,
                    semanticModel,
                    cancellationToken,
                    out var exitCondition):
                state = state.AddPathCondition(exitCondition);
                AddLoopBodyInvariantStateFacts(ref state, whileStatement, semanticModel, cancellationToken);
                break;
            case ForStatementSyntax { Condition: { } condition } forStatement
                when CanAssumeLoopConditionFalseAfterNormalExit(forStatement, forStatement.Statement):
                AddReachabilityCondition(
                    ref state,
                    condition,
                    false,
                    semanticModel,
                    cancellationToken);
                AddLoopBodyInvariantStateFacts(ref state, forStatement, semanticModel, cancellationToken);
                break;
            case ForStatementSyntax { Condition: { } condition } forStatement
                when TryCreateGuardedBreakLoopExitSymbolicCondition(
                    forStatement,
                    forStatement.Statement,
                    condition,
                    semanticModel,
                    cancellationToken,
                    out var exitCondition):
                state = state.AddPathCondition(exitCondition);
                AddLoopBodyInvariantStateFacts(ref state, forStatement, semanticModel, cancellationToken);
                break;
            case ForStatementSyntax { Condition: null } forStatement
                when TryCreateGuardedBreakLoopExitSymbolicCondition(
                    forStatement,
                    forStatement.Statement,
                    null,
                    semanticModel,
                    cancellationToken,
                    out var exitCondition):
                state = state.AddPathCondition(exitCondition);
                AddLoopBodyInvariantStateFacts(ref state, forStatement, semanticModel, cancellationToken);
                break;
            case DoStatementSyntax doStatement
                when CanAssumeLoopConditionFalseAfterNormalExit(doStatement, doStatement.Statement):
                AddReachabilityCondition(
                    ref state,
                    doStatement.Condition,
                    false,
                    semanticModel,
                    cancellationToken);
                AddLoopBodyInvariantStateFacts(ref state, doStatement, semanticModel, cancellationToken);
                break;
            case DoStatementSyntax doStatement
                when TryCreateGuardedBreakLoopExitSymbolicCondition(
                    doStatement,
                    doStatement.Statement,
                    doStatement.Condition,
                    semanticModel,
                    cancellationToken,
                    out var exitCondition):
                state = state.AddPathCondition(exitCondition);
                AddLoopBodyInvariantStateFacts(ref state, doStatement, semanticModel, cancellationToken);
                break;
            case ForEachStatementSyntax forEachStatement:
                AddCompletedForeachStatementStateFacts(
                    ref state,
                    forEachStatement.Expression,
                    forEachStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
            case ForEachVariableStatementSyntax forEachVariableStatement:
                AddCompletedForeachStatementStateFacts(
                    ref state,
                    forEachVariableStatement.Expression,
                    forEachVariableStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
            case LockStatementSyntax lockStatement:
                AddCompletedLockStatementStateFacts(
                    ref state,
                    lockStatement,
                    semanticModel,
                    cancellationToken);
                break;
        }
    }

    private static void AddLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
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
    }

    private static void AddCompletedForeachStatementStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax foreachBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (ReferenceIdentityFactIsInvalidatedInStatement(
                expression,
                foreachBody,
                semanticModel,
                cancellationToken))
            return;

        AddReferenceNullCondition(
            ref state,
            expression,
            false,
            semanticModel,
            cancellationToken,
            "ir.path.foreach-completion.not-null");
    }

    private static bool TryCreateGuardedBreakLoopExitSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        ExpressionSyntax? condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition exitCondition)
    {
        exitCondition = null!;
        if (LoopBodyContainsGoto(loopBody) ||
            !TryCreateTopLevelGuardedBreakSymbolicCondition(
                loopStatement,
                loopBody,
                semanticModel,
                cancellationToken,
                out var breakCondition))
            return false;

        if (condition == null)
        {
            exitCondition = breakCondition;
            return true;
        }

        if (!TryCreateBranchSymbolicCondition(
                condition,
                false,
                semanticModel,
                cancellationToken,
                out var normalExitCondition))
            return false;

        exitCondition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            normalExitCondition,
            breakCondition);
        return true;
    }

    private static bool TryCreateTopLevelGuardedBreakSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        breakCondition = null!;
        var loopBreaks = loopBody
            .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .OfType<BreakStatementSyntax>()
            .Where(breakStatement => BreakTargetsLoop(breakStatement, loopStatement))
            .ToArray();
        if (loopBreaks.Length == 0) return false;

        SymbolicCondition? combinedCondition = null;
        foreach (var breakStatement in loopBreaks)
        {
            if (!TryCreateGuardedBreakSymbolicCondition(
                    breakStatement,
                    loopStatement,
                    loopBody,
                    semanticModel,
                    cancellationToken,
                    out var guardedBreakCondition))
                return false;

            combinedCondition = combinedCondition == null
                ? guardedBreakCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    combinedCondition,
                    guardedBreakCondition);
        }

        breakCondition = combinedCondition!;
        return true;
    }

    private static bool TryCreateGuardedBreakSymbolicCondition(
        BreakStatementSyntax breakStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        return TryCreateDirectGuardedBreakSymbolicCondition(
                   breakStatement,
                   loopStatement,
                   loopBody,
                   semanticModel,
                   cancellationToken,
                   out breakCondition) ||
               TryCreateNestedGuardedBreakSymbolicCondition(
                   breakStatement,
                   loopStatement,
                   loopBody,
                   semanticModel,
                   cancellationToken,
                   out breakCondition) ||
               TryCreateGuardedContinueBeforeBreakSymbolicCondition(
                   loopStatement,
                   loopBody,
                   breakStatement,
                   semanticModel,
                   cancellationToken,
                   out breakCondition);
    }

    private static bool TryCreateDirectGuardedBreakSymbolicCondition(
        BreakStatementSyntax breakStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        breakCondition = null!;
        var ifStatement = breakStatement.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
        if (ifStatement == null ||
            !IsTopLevelLoopBodyStatement(ifStatement, loopBody) ||
            !TryGetDirectBreakBranch(ifStatement, breakStatement, out var branchWhenTrue) ||
            AnyConditionSymbolInvalidatedBeforeStatement(
                ifStatement.Condition,
                loopBody,
                ifStatement.SpanStart,
                semanticModel,
                cancellationToken) ||
            !TryCreateBranchSymbolicCondition(
                ifStatement.Condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                out breakCondition))
        {
            breakCondition = null!;
            return false;
        }

        if (TryCreateGuardedContinueFallThroughBeforeStatementSymbolicCondition(
                loopStatement,
                loopBody,
                ifStatement,
                semanticModel,
                cancellationToken,
                out var fallThroughCondition))
            breakCondition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                fallThroughCondition,
                breakCondition);

        return true;
    }

    private static bool TryCreateNestedGuardedBreakSymbolicCondition(
        BreakStatementSyntax breakStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        breakCondition = null!;
        var guards = new List<(IfStatementSyntax IfStatement, bool BranchWhenTrue)>();
        StatementSyntax currentStatement = breakStatement;
        while (TryGetOnlyParentIfBranch(currentStatement, out var ifStatement, out var branchWhenTrue))
        {
            guards.Add((ifStatement, branchWhenTrue));
            currentStatement = ifStatement;
        }

        if (guards.Count <= 1 || !IsTopLevelLoopBodyStatement(currentStatement, loopBody)) return false;

        SymbolicCondition? combinedCondition = null;
        for (var index = guards.Count - 1; index >= 0; index--)
        {
            var guard = guards[index];
            if (AnyConditionSymbolInvalidatedBeforeStatement(
                    guard.IfStatement.Condition,
                    loopBody,
                    guard.IfStatement.SpanStart,
                    semanticModel,
                    cancellationToken) ||
                !TryCreateBranchSymbolicCondition(
                    guard.IfStatement.Condition,
                    guard.BranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var guardCondition))
                return false;

            combinedCondition = combinedCondition == null
                ? guardCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    combinedCondition,
                    guardCondition);
        }

        if (combinedCondition == null) return false;

        if (TryCreateGuardedContinueFallThroughBeforeStatementSymbolicCondition(
                loopStatement,
                loopBody,
                currentStatement,
                semanticModel,
                cancellationToken,
                out var fallThroughCondition))
            combinedCondition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                fallThroughCondition,
                combinedCondition);

        breakCondition = combinedCondition;
        return true;
    }

    private static bool TryCreateGuardedContinueBeforeBreakSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        BreakStatementSyntax breakStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        breakCondition = null!;
        if (loopBody is not BlockSyntax block) return false;

        var breakIndex = -1;
        for (var index = 0; index < block.Statements.Count; index++)
            if (StatementDirectlyContainsOnlyBreak(block.Statements[index], breakStatement))
            {
                breakIndex = index;
                break;
            }

        if (breakIndex <= 0) return false;

        return TryCreateGuardedContinueFallThroughBeforeStatementSymbolicCondition(
            loopStatement,
            loopBody,
            block.Statements[breakIndex],
            semanticModel,
            cancellationToken,
            out breakCondition);
    }

    private static bool TryCreateGuardedContinueFallThroughBeforeStatementSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        StatementSyntax targetStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition fallThroughCondition)
    {
        fallThroughCondition = null!;
        if (loopBody is not BlockSyntax block) return false;

        var targetIndex = -1;
        for (var index = 0; index < block.Statements.Count; index++)
            if (ReferenceEquals(block.Statements[index], targetStatement))
            {
                targetIndex = index;
                break;
            }

        if (targetIndex <= 0) return false;

        SymbolicCondition? combinedCondition = null;
        for (var index = targetIndex - 1; index >= 0; index--)
        {
            if (block.Statements[index] is not IfStatementSyntax ifStatement ||
                !TryCreateGuardedContinueFallThroughSymbolicCondition(
                    ifStatement,
                    loopStatement,
                    loopBody,
                    targetStatement,
                    semanticModel,
                    cancellationToken,
                    out var guardFallThroughCondition))
                break;

            combinedCondition = combinedCondition == null
                ? guardFallThroughCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    guardFallThroughCondition,
                    combinedCondition);
        }

        if (combinedCondition == null) return false;

        fallThroughCondition = combinedCondition;
        return true;
    }

    private static bool TryCreateGuardedContinueFallThroughSymbolicCondition(
        IfStatementSyntax ifStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        StatementSyntax targetStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition fallThroughCondition)
    {
        fallThroughCondition = null!;
        if (TryGetDirectContinueBranch(ifStatement, loopStatement, out var continueBranchWhenTrue))
        {
            if (AnyConditionSymbolInvalidatedBeforeStatement(
                    ifStatement.Condition,
                    loopBody,
                    targetStatement.SpanStart,
                    semanticModel,
                    cancellationToken) ||
                !TryCreateBranchSymbolicCondition(
                    ifStatement.Condition,
                    !continueBranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    out fallThroughCondition))
            {
                fallThroughCondition = null!;
                return false;
            }

            return true;
        }

        if (!TryCreateNestedGuardedContinueSymbolicCondition(
                ifStatement,
                loopStatement,
                loopBody,
                targetStatement,
                semanticModel,
                cancellationToken,
                out var continueCondition))
            return false;

        fallThroughCondition = new SymbolicNotCondition(continueCondition);
        return true;
    }

    private static bool TryCreateNestedGuardedContinueSymbolicCondition(
        IfStatementSyntax ifStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        StatementSyntax targetStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition continueCondition)
    {
        continueCondition = null!;
        var continueStatements = ifStatement
            .DescendantNodes(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .OfType<ContinueStatementSyntax>()
            .Where(continueStatement => ContinueTargetsLoop(continueStatement, loopStatement))
            .ToArray();
        if (continueStatements.Length != 1) return false;

        var guards = new List<(IfStatementSyntax IfStatement, bool BranchWhenTrue)>();
        StatementSyntax currentStatement = continueStatements[0];
        while (TryGetOnlyParentIfBranch(currentStatement, out var parentIf, out var branchWhenTrue))
        {
            guards.Add((parentIf, branchWhenTrue));
            currentStatement = parentIf;
        }

        if (guards.Count <= 1 || !ReferenceEquals(currentStatement, ifStatement)) return false;

        SymbolicCondition? combinedCondition = null;
        for (var index = guards.Count - 1; index >= 0; index--)
        {
            var guard = guards[index];
            if (AnyConditionSymbolInvalidatedBeforeStatement(
                    guard.IfStatement.Condition,
                    loopBody,
                    targetStatement.SpanStart,
                    semanticModel,
                    cancellationToken) ||
                !TryCreateBranchSymbolicCondition(
                    guard.IfStatement.Condition,
                    guard.BranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var guardCondition))
                return false;

            combinedCondition = combinedCondition == null
                ? guardCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    combinedCondition,
                    guardCondition);
        }

        if (combinedCondition == null) return false;

        continueCondition = combinedCondition;
        return true;
    }

    private static void AddCompletedLockStatementStateFacts(
        ref SymbolicState state,
        LockStatementSyntax lockStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!IsLocalOrParameterReference(lockStatement.Expression, semanticModel, cancellationToken) ||
            ReferenceIdentityFactIsInvalidatedInStatement(
                lockStatement.Expression,
                lockStatement.Statement,
                semanticModel,
                cancellationToken))
            return;

        AddReferenceNullCondition(
            ref state,
            lockStatement.Expression,
            false,
            semanticModel,
            cancellationToken,
            "ir.path.lock-completion.not-null");
    }

    private static void AddCompletedExpressionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (expression is AssignmentExpressionSyntax assignment)
        {
            AddAssignmentExpressionStateFacts(
                ref state,
                assignment,
                null,
                semanticModel,
                cancellationToken);
            return;
        }

        RemoveStateFactsInvalidatedByNestedMutations(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
        AddTopLevelMemberNotNullNormalCompletionStateFacts(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
    }

    private static void AddAssignmentExpressionStateFacts(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        ExpressionStatementSyntax? containingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (TryHandleTupleDeconstructionDeclarationState(ref state, assignment, semanticModel, cancellationToken))
            return;

        if (TryHandleTupleAssignmentState(ref state, assignment, semanticModel, cancellationToken)) return;

        var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        if (assignedSymbol != null) assignedSymbol = NormalizeMutatedSymbol(assignedSymbol);

        SymbolicTerm? previousAssignedValueTerm = null;
        if (assignedSymbol is ILocalSymbol or IParameterSymbol &&
            TryGetCurrentStateSymbolValueTerm(state, assignedSymbol.OriginalDefinition, out var previousStateValueTerm))
            previousAssignedValueTerm = previousStateValueTerm;

        var coalesceAssignmentIsKnownNoOp = assignedSymbol is ILocalSymbol or IParameterSymbol &&
                                            assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                                            (IsKnownNonNullReferenceSymbol(state, assignedSymbol.OriginalDefinition) ||
                                             IsKnownNullableHasValueSymbol(state, assignedSymbol.OriginalDefinition));
        var coalesceAssignmentIsKnownNullableNoValue = assignedSymbol is ILocalSymbol or IParameterSymbol &&
                                                       assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                                                       IsKnownNullableNoValueSymbol(state,
                                                           assignedSymbol.OriginalDefinition);
        var coalesceAssignmentIsKnownNullReference = assignedSymbol is ILocalSymbol or IParameterSymbol &&
                                                     assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                                                     IsKnownNullReferenceSymbol(state,
                                                         assignedSymbol.OriginalDefinition);

        if (coalesceAssignmentIsKnownNoOp) return;

        RemoveStateFactsInvalidatedByMutationTarget(
            ref state,
            assignment.Left,
            semanticModel,
            cancellationToken);
        RemoveStateFactsInvalidatedByNestedMutations(
            ref state,
            assignment.Left,
            semanticModel,
            cancellationToken);
        RemoveStateFactsInvalidatedByNestedMutations(
            ref state,
            assignment.Right,
            semanticModel,
            cancellationToken);

        if (assignedSymbol is IFieldSymbol or IPropertySymbol &&
            IsCurrentInstanceMemberReference(assignment.Left, semanticModel, cancellationToken))
            state = RemoveStateFactsReferencingImplicitThisMember(state, assignedSymbol.Name);

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                AddAssignedValueStateFacts(
                    ref state,
                    assignedSymbol.OriginalDefinition,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement",
                    previousAssignedValueTerm);
            else if (assignedSymbol is IFieldSymbol or IPropertySymbol &&
                     IsCurrentInstanceMemberReference(assignment.Left, semanticModel, cancellationToken) &&
                     TryCreateImplicitThisMemberTerm(assignedSymbol, out var memberTerm))
                AddAssignedCurrentInstanceMemberStateFacts(
                    ref state,
                    memberTerm,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement");
        }
        else if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                 assignedSymbol is ILocalSymbol or IParameterSymbol &&
                 (coalesceAssignmentIsKnownNullableNoValue || coalesceAssignmentIsKnownNullReference))
        {
            AddAssignedValueStateFacts(
                ref state,
                assignedSymbol.OriginalDefinition,
                assignment.Right,
                semanticModel,
                cancellationToken,
                "ir.path.prior-statement.coalesce-assignment",
                previousAssignedValueTerm);
        }
        else if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                 assignedSymbol is ILocalSymbol or IParameterSymbol)
        {
            AddCoalesceAssignmentStateFacts(
                ref state,
                assignedSymbol.OriginalDefinition,
                assignment.Right,
                semanticModel,
                cancellationToken);
        }
        else if (assignedSymbol is ILocalSymbol or IParameterSymbol &&
                 previousAssignedValueTerm != null &&
                 TryCreateCompoundAssignmentStateTerm(
                     previousAssignedValueTerm,
                     assignment,
                     semanticModel,
                     cancellationToken,
                     assignedSymbol.OriginalDefinition,
                     out var compoundAssignmentValueTerm) &&
                 TryCreateSymbolTerm(assignedSymbol.OriginalDefinition, out var targetTerm) &&
                 targetTerm.Kind == SmtValueKind.Int &&
                 !ReferencesStateSymbol(
                     compoundAssignmentValueTerm,
                     SymbolicFactFactory.GetSmtVariableName(assignedSymbol.OriginalDefinition)))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                compoundAssignmentValueTerm,
                assignment,
                "ir.path.prior-statement.compound-assignment");
        }

        AddElementAssignmentStateFact(
            ref state,
            assignment,
            semanticModel,
            cancellationToken);

        if (containingStatement != null)
            AddNormalCompletionStateFacts(
                ref state,
                assignment.Right,
                containingStatement,
                assignedSymbol is not ILocalSymbol and not IParameterSymbol,
                semanticModel,
                cancellationToken);
    }

    private static void AddCoalesceAssignmentStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (UnwrapExpression(rightExpression) is ThrowExpressionSyntax)
        {
            if (TryCreateNullableSymbolTerms(assignedSymbol, out var completedHasValue, out _))
            {
                state = state.AddPathCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicTruthAtom(completedHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.throw-completion-has-value",
                    assignedSymbol)));
                return;
            }

            if (TryCreateSymbolTerm(assignedSymbol, out var completedReference) &&
                completedReference.Kind == SmtValueKind.Reference)
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.NotEqual,
                    completedReference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    "ir.path.coalesce-assignment.throw-completion-non-null");

            return;
        }

        if (TryCreateNullableSymbolTerms(assignedSymbol, out var targetHasValue, out var targetValue))
        {
            SymbolicTerm? rightHasValue = null;
            var hasValueLowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(rightExpression, context);
            if (hasValueLowering is { IsExact: true, Value: { } nullableRightHasValue })
                rightHasValue = nullableRightHasValue;
            else if (SymbolicSemanticPipeline.LowerTerm(rightExpression, context) is
                     { IsExact: true, Value: { } wrappedRightValue } &&
                     wrappedRightValue.Kind == targetValue.Kind)
                rightHasValue = new SymbolicBooleanConstantTerm(true);

            if (rightHasValue == null) return;

            if (rightHasValue is SymbolicBooleanConstantTerm { Value: true })
            {
                var fact = SymbolicFact.Exact(
                    new SymbolicTruthAtom(targetHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.nullable-has-value",
                    assignedSymbol);
                state = state.AddPathCondition(new SymbolicFactCondition(fact));
            }
            else
            {
                var targetHasValueFact = SymbolicFact.Exact(
                    new SymbolicTruthAtom(targetHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.target-has-value",
                    assignedSymbol);
                var rightHasNoValueFact = SymbolicFact.Exact(
                    new SymbolicTruthAtom(rightHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.right-has-value");
                state = state.AddPathCondition(new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    new SymbolicFactCondition(targetHasValueFact),
                    new SymbolicNotCondition(new SymbolicFactCondition(rightHasNoValueFact))));
            }

            return;
        }

        if (!TryCreateSymbolTerm(assignedSymbol, out var target) ||
            target.Kind != SmtValueKind.Reference)
            return;

        if (IsDefinitelyNonNullReferenceValue(rightExpression, semanticModel, cancellationToken))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                rightExpression,
                "ir.path.coalesce-assignment.non-null");
            return;
        }

        var rightLowering = SymbolicSemanticPipeline.LowerReferenceTerm(rightExpression, context);
        if (rightLowering is not { IsExact: true, Value: { } right }) return;

        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm()),
            rightExpression,
            "ir.path.coalesce-assignment.target-non-null",
            assignedSymbol));
        var targetEqualsRight = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, target, right),
            rightExpression,
            "ir.path.coalesce-assignment.target-equals-right",
            assignedSymbol));
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            targetNonNull,
            targetEqualsRight));
    }

    private static void AddCompletedBlockStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var processedStatementCount = 0;
        foreach (var statement in block.Statements)
        {
            var limit = SymbolicAnalysisLimitContext.Limits.MaxScopedBlockCompletionStatements;
            if (processedStatementCount >= limit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.ScopedBlockCompletionStatements,
                    limit,
                    block.Statements.Count,
                    block,
                    "program_point.completed_block_state");
                return;
            }

            processedStatementCount++;
            AddPriorStatementStateFacts(
                ref state,
                statement,
                semanticModel,
                cancellationToken);
            if (StatementDefinitelyExits(statement, semanticModel, cancellationToken)) return;
        }
    }

    private static void AddNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        bool includeThrowGuardFacts,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (includeThrowGuardFacts)
            AddTopLevelThrowGuardNormalCompletionStateFacts(
                ref state,
                expression,
                statement,
                semanticModel,
                cancellationToken);

        AddTopLevelNotNullParameterNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        AddTopLevelKnownGuardNormalCompletionStateFacts(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
        AddTopLevelDoesNotReturnIfNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        AddTopLevelMemberNotNullNormalCompletionStateFacts(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
        AddTopLevelArrayCreationNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
        AddTopLevelDereferenceNormalCompletionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken);
    }

    private static void AddTopLevelNotNullParameterNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { IsParams: false } parameter ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
                !ArgumentRefKindMatches(parameter, argumentSyntax) ||
                !HasNotNullNormalCompletionPostcondition(parameter, cancellationToken) ||
                parameter.RefKind != RefKind.None &&
                !IsUniqueOutputArgumentTarget(
                    invocationOperation,
                    argument,
                    semanticModel,
                    cancellationToken))
                continue;

            AddStableReferenceNonNullStateFact(
                ref state,
                argumentSyntax.Expression,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.parameter-not-null",
                parameter.RefKind != RefKind.None);
        }
    }

    private static bool HasNotNullNormalCompletionPostcondition(
        IParameterSymbol parameter,
        CancellationToken cancellationToken)
    {
        return parameter.RefKind == RefKind.None
            ? NullableFlowFacts.HasNotNullPostcondition(parameter) ||
              NullableFlowFacts.HasInferredNotNullNormalCompletionPostcondition(
                  parameter,
                  cancellationToken)
            : NullableFlowFacts.GetParameterOutputState(parameter) == NullableFlowFactState.NotNull;
    }

    private static bool ArgumentRefKindMatches(IParameterSymbol parameter, ArgumentSyntax argument)
    {
        return parameter.RefKind switch
        {
            RefKind.None => argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefKind.Ref => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword),
            RefKind.Out => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
            _ => false
        };
    }

    private static bool IsUniqueOutputArgumentTarget(
        IInvocationOperation invocation,
        IArgumentOperation argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (argument.Syntax is not ArgumentSyntax argumentSyntax ||
            !NullableFlowFacts.TryGetArgumentTargetSymbol(
                argumentSyntax.Expression,
                semanticModel,
                cancellationToken,
                out var target))
            return false;

        foreach (var otherArgument in invocation.Arguments)
        {
            if (ReferenceEquals(argument, otherArgument) ||
                otherArgument.Syntax is not ArgumentSyntax otherArgumentSyntax ||
                !NullableFlowFacts.TryGetArgumentTargetSymbol(
                    otherArgumentSyntax.Expression,
                    semanticModel,
                    cancellationToken,
                    out var otherTarget))
                continue;

            if (SymbolEqualityComparer.Default.Equals(target, otherTarget)) return false;
        }

        return true;
    }

    private static void AddTopLevelMemberNotNullNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod.IsStatic ||
            !IsCurrentInstanceInvocation(invocation))
            return;

        var memberTargets = NullableFlowFacts.GetMemberNotNullTargets(invocationOperation.TargetMethod);
        foreach (var memberTarget in memberTargets)
        {
            if (!NullableFlowFacts.TryResolveInstanceMemberTarget(
                    invocationOperation.TargetMethod.ContainingType,
                    memberTarget,
                    out var member) ||
                !NullableFlowFacts.TryGetMemberType(member, out var type) ||
                !TryGetValueKind(type, out var kind) ||
                kind != SmtValueKind.Reference)
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                new SymbolicMemberTerm(
                    new SymbolicVariableTerm(ImplicitThisVariableName, SmtValueKind.Reference),
                    member.Name,
                    kind),
                new SymbolicNullTerm(),
                invocation,
                "ir.path.normal-completion.member-not-null");
        }
    }

    private static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
    {
        var invokedExpression = UnwrapExpression(invocation.Expression);
        return invokedExpression is IdentifierNameSyntax ||
               invokedExpression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

    private static bool TryCreateImplicitThisMemberTerm(ISymbol member, out SymbolicTerm term)
    {
        if (!NullableFlowFacts.TryGetMemberType(member, out var type) ||
            !TryGetValueKind(type, out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicMemberTerm(
            new SymbolicVariableTerm(ImplicitThisVariableName, SmtValueKind.Reference),
            member.Name,
            kind);
        return true;
    }

    private static void AddTopLevelDoesNotReturnIfNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { RefKind: RefKind.None, IsParams: false } parameter ||
                !NullableFlowFacts.TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
                !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                AnyConditionSymbolInvalidatedInStatement(argumentSyntax.Expression, statement, semanticModel,
                    cancellationToken))
                continue;

            AddReachabilityCondition(
                ref state,
                argumentSyntax.Expression,
                !doesNotReturnWhen,
                semanticModel,
                cancellationToken);
        }
    }

    private static void AddTopLevelArrayCreationNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapAwaitedNormalCompletionExpression(expression);
        if (expression is not ArrayCreationExpressionSyntax arrayCreation) return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        foreach (var sizeExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
        {
            var lowering = SymbolicSemanticPipeline.LowerTerm(sizeExpression, context);
            if (AnyConditionSymbolInvalidatedInStatement(sizeExpression, statement, semanticModel, cancellationToken) ||
                lowering is not { IsExact: true, Value: { } sizeTerm } ||
                sizeTerm.Kind != SmtValueKind.Int)
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThanOrEqual,
                sizeTerm,
                new SymbolicIntegerConstantTerm(0),
                sizeExpression,
                "ir.path.normal-completion.array-length.non-negative");
        }
    }

    private static void AddTopLevelThrowGuardNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddThrowGuardedExpressionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.normal-completion.throw-guarded-not-null");
    }

    private static void AddTopLevelDereferenceNormalCompletionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            var awaitableExpression = UnwrapExpression(awaitExpression.Expression);
            AddStableReferenceNonNullStateFact(
                ref state,
                awaitableExpression,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.awaitable-not-null");
            expression = awaitableExpression;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            !AnyConditionSymbolInvalidatedInStatement(elementAccess, statement, semanticModel, cancellationToken) &&
            elementAccess.ArgumentList.Arguments.Count == 1)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(elementAccess, context) is
                { IsExact: true, Value: { } inRangeCondition })
                state = state.AddPathCondition(inRangeCondition);
        }

        if (!TryGetTopLevelDereferenceReceiver(expression, semanticModel, cancellationToken, out var receiver)) return;

        AddStableReferenceNonNullStateFact(
            ref state,
            receiver,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.normal-completion.dereference.receiver-not-null");
    }

    private static bool AddStableReferenceNonNullStateFact(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        bool allowArgumentMutation = false)
    {
        if (!NullableFlowFacts.TryGetArgumentTargetSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var symbol) ||
            !allowArgumentMutation &&
            AnyConditionSymbolMutatedInStatement(expression, statement, semanticModel, cancellationToken))
            return false;

        if (!TryCreateSymbolTerm(symbol, out var symbolTerm) ||
            symbolTerm.Kind != SmtValueKind.Reference)
            return false;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.NotEqual,
            symbolTerm,
            new SymbolicNullTerm(),
            expression,
            provenance);
        return true;
    }

    private static bool TryGetTopLevelDereferenceReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax receiver)
    {
        expression = UnwrapExpression(expression);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when UnwrapExpression(invocation.Expression) is MemberAccessExpressionSyntax memberAccess &&
                     !IsReducedExtensionMethodInvocation(invocation, semanticModel, cancellationToken):
                receiver = memberAccess.Expression;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                receiver = memberAccess.Expression;
                return true;
            case ElementAccessExpressionSyntax elementAccess:
                receiver = elementAccess.Expression;
                return true;
            default:
                receiver = null!;
                return false;
        }
    }

    private static ExpressionSyntax UnwrapAwaitedNormalCompletionExpression(ExpressionSyntax expression)
    {
        expression = UnwrapExpression(expression);
        return expression is AwaitExpressionSyntax awaitExpression
            ? UnwrapExpression(awaitExpression.Expression)
            : expression;
    }

    private static bool IsReducedExtensionMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
               invocationOperation.TargetMethod.ReducedFrom != null;
    }

    private static void AddCompletedIfStatementStateFacts(
        ref SymbolicState state,
        IfStatementSyntax ifStatement,
        SymbolicState stateBeforeStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var trueBranchExits = StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken);
        var falseBranchStatement = ifStatement.Else?.Statement;
        var falseBranchExits = falseBranchStatement != null &&
                               StatementDefinitelyExits(falseBranchStatement, semanticModel, cancellationToken);

        if (trueBranchExits && falseBranchExits)
        {
            state = MarkContradictory(stateBeforeStatement);
            return;
        }

        if (trueBranchExits && !falseBranchExits &&
            TryCollectCompletedBranchState(
                stateBeforeStatement,
                ifStatement.Condition,
                false,
                falseBranchStatement,
                semanticModel,
                cancellationToken,
                out var survivingFalseState))
        {
            if (falseBranchStatement != null)
                RemoveConditionFactsInvalidatedByStatement(
                    ref survivingFalseState,
                    ifStatement.Condition,
                    falseBranchStatement,
                    semanticModel,
                    cancellationToken);
            state = survivingFalseState;
            return;
        }

        if (falseBranchExits && !trueBranchExits &&
            TryCollectCompletedBranchState(
                stateBeforeStatement,
                ifStatement.Condition,
                true,
                ifStatement.Statement,
                semanticModel,
                cancellationToken,
                out var survivingTrueState))
        {
            RemoveConditionFactsInvalidatedByStatement(
                ref survivingTrueState,
                ifStatement.Condition,
                ifStatement.Statement,
                semanticModel,
                cancellationToken);
            state = survivingTrueState;
            return;
        }

        if (trueBranchExits || falseBranchExits) return;

        if (!TryCollectCompletedBranchState(
                stateBeforeStatement,
                ifStatement.Condition,
                true,
                ifStatement.Statement,
                semanticModel,
                cancellationToken,
                out var trueBranchState) ||
            !TryCollectCompletedBranchState(
                stateBeforeStatement,
                ifStatement.Condition,
                false,
                falseBranchStatement,
                semanticModel,
                cancellationToken,
                out var falseBranchState))
            return;

        AddIdenticalIfBranchStateFacts(ref state, trueBranchState, falseBranchState);

        if (AnyConditionSymbolInvalidatedInStatement(
                ifStatement.Condition,
                ifStatement.Statement,
                semanticModel,
                cancellationToken) ||
            (falseBranchStatement != null &&
             AnyConditionSymbolInvalidatedInStatement(
                 ifStatement.Condition,
                 falseBranchStatement,
                 semanticModel,
                 cancellationToken)) ||
            !TryCreateBranchSymbolicCondition(
                ifStatement.Condition,
                true,
                semanticModel,
                cancellationToken,
                out var trueCondition) ||
            !TryCreateBranchSymbolicCondition(
                ifStatement.Condition,
                false,
                semanticModel,
                cancellationToken,
                out var falseCondition))
            return;

        AddConditionalIfBranchStateFacts(
            ref state,
            trueBranchState,
            falseBranchState,
            trueCondition,
            falseCondition,
            ifStatement);
    }

    private static bool TryCollectCompletedBranchState(
        SymbolicState stateBeforeStatement,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        StatementSyntax? branchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState branchState)
    {
        var branchLowering = SymbolicReachabilityService.ApplyBranchFacts(
                stateBeforeStatement,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken);
        if (branchLowering is not { IsExact: true, Value: { } loweredBranchState })
        {
            branchState = stateBeforeStatement;
            return false;
        }

        branchState = loweredBranchState;

        if (branchStatement == null) return true;

        foreach (var statement in EnumerateBranchStatements(branchStatement))
            AddPriorStatementStateFacts(
                ref branchState,
                statement,
                semanticModel,
                cancellationToken);

        foreach (var hiddenSymbol in GetLocalsDeclaredInside(branchStatement, semanticModel, cancellationToken))
            branchState = RemoveStateFactsReferencingSymbol(branchState, hiddenSymbol);

        return true;
    }

    private static void RemoveConditionFactsInvalidatedByStatement(
        ref SymbolicState state,
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in GetConditionDependencySymbols(condition, semanticModel, cancellationToken))
            if (StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken))
                state = RemoveStateFactsReferencingSymbol(state, symbol);
    }

    private static bool TryCreateBranchSymbolicCondition(
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition branchCondition)
    {
        var lowering = SymbolicSemanticPipeline.LowerCondition(
            condition,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } loweredCondition })
        {
            branchCondition = null!;
            return false;
        }

        branchCondition = branchWhenTrue
            ? loweredCondition
            : new SymbolicNotCondition(loweredCondition);
        return true;
    }

    private static void AddIdenticalIfBranchStateFacts(
        ref SymbolicState state,
        SymbolicState trueBranchState,
        SymbolicState falseBranchState)
    {
        var falseFactKeys = new HashSet<string>(
            falseBranchState.Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        var falseConditionKeys = new HashSet<string>(
            falseBranchState.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);

        foreach (var fact in trueBranchState.Facts)
            if (falseFactKeys.Contains(SymbolicState.CreateProofFactKey(fact)))
                state = state.AddFact(fact);

        foreach (var condition in trueBranchState.PathConditions)
            if (falseConditionKeys.Contains(SymbolicState.CreateProofConditionKey(condition)))
                state = state.AddPathCondition(condition);
    }

    private static void AddConditionalIfBranchStateFacts(
        ref SymbolicState state,
        SymbolicState trueBranchState,
        SymbolicState falseBranchState,
        SymbolicCondition trueCondition,
        SymbolicCondition falseCondition,
        IfStatementSyntax ifStatement)
    {
        var commonFactKeys = new HashSet<string>(
            trueBranchState.Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        commonFactKeys.IntersectWith(falseBranchState.Facts.Select(SymbolicState.CreateProofFactKey));
        var commonConditionKeys = new HashSet<string>(
            trueBranchState.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        commonConditionKeys.IntersectWith(
            falseBranchState.PathConditions.Select(SymbolicState.CreateProofConditionKey));

        var addedCount = 0;
        if (!TryAddConditionalIfBranchStateFacts(
                ref state,
                trueBranchState,
                trueCondition,
                commonFactKeys,
                commonConditionKeys,
                ifStatement,
                ref addedCount))
            return;

        TryAddConditionalIfBranchStateFacts(
            ref state,
            falseBranchState,
            falseCondition,
            commonFactKeys,
            commonConditionKeys,
            ifStatement,
            ref addedCount);
    }

    private static bool TryAddConditionalIfBranchStateFacts(
        ref SymbolicState state,
        SymbolicState branchState,
        SymbolicCondition branchCondition,
        ISet<string> commonFactKeys,
        ISet<string> commonConditionKeys,
        IfStatementSyntax ifStatement,
        ref int addedCount)
    {
        foreach (var fact in branchState.Facts)
        {
            if (commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact))) continue;

            if (!TryAddConditionalIfBranchStateFact(
                    ref state,
                    branchCondition,
                    new SymbolicFactCondition(fact),
                    ifStatement,
                    ref addedCount))
                return false;
        }

        var branchConditionKey = SymbolicState.CreateProofConditionKey(branchCondition);
        foreach (var condition in branchState.PathConditions)
        {
            var conditionKey = SymbolicState.CreateProofConditionKey(condition);
            if (commonConditionKeys.Contains(conditionKey) ||
                string.Equals(conditionKey, branchConditionKey, StringComparison.Ordinal))
                continue;

            if (!TryAddConditionalIfBranchStateFact(
                    ref state,
                    branchCondition,
                    condition,
                    ifStatement,
                    ref addedCount))
                return false;
        }

        return true;
    }

    private static bool TryAddConditionalIfBranchStateFact(
        ref SymbolicState state,
        SymbolicCondition branchCondition,
        SymbolicCondition branchFact,
        IfStatementSyntax ifStatement,
        ref int addedCount)
    {
        var limit = SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts;
        if (addedCount >= limit)
        {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.IfElseFactMerge,
                limit,
                addedCount + 1,
                ifStatement,
                "program_point.if_else_state_fact_merge");
            return false;
        }

        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(branchCondition),
            branchFact));
        addedCount++;
        return true;
    }

    private static IReadOnlyList<ISymbol> GetLocalsDeclaredInside(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            var symbol = node switch
            {
                VariableDeclaratorSyntax declarator => semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
                SingleVariableDesignationSyntax designation => semanticModel.GetDeclaredSymbol(designation,
                    cancellationToken),
                ForEachStatementSyntax forEachStatement => semanticModel.GetDeclaredSymbol(forEachStatement,
                    cancellationToken),
                CatchDeclarationSyntax catchDeclaration => semanticModel.GetDeclaredSymbol(catchDeclaration,
                    cancellationToken),
                _ => null
            };

            if (symbol is ILocalSymbol &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol.OriginalDefinition)))
                symbols.Add(symbol.OriginalDefinition);
        }

        return symbols;
    }

    private static IEnumerable<StatementSyntax> EnumerateBranchStatements(StatementSyntax branchStatement)
    {
        if (branchStatement is BlockSyntax block)
        {
            foreach (var statement in block.Statements) yield return statement;

            yield break;
        }

        yield return branchStatement;
    }

    private static void AddCompletedSwitchStatementStateFacts(
        ref SymbolicState state,
        SwitchStatementSyntax switchStatement,
        SymbolicState stateBeforeStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        AddCompletedSwitchExitExclusionStateFacts(
            ref state,
            switchStatement,
            semanticModel,
            cancellationToken);

        if (!SwitchStatementHasDefaultOrExhaustiveBooleanLabels(
                switchStatement,
                semanticModel,
                cancellationToken))
            return;

        var branches = new List<SwitchBranchState>();
        var conditionSymbols = GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken);
        foreach (var section in switchStatement.Sections)
        {
            if (!SectionBreaksFromSwitch(section, switchStatement)) continue;

            if (!SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                    switchStatement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition))
                return;

            var sectionMutatesConditionSymbols = SectionMutatesAnySymbolBeforeSwitchBreak(
                section,
                switchStatement,
                conditionSymbols,
                semanticModel,
                cancellationToken);
            var sectionState = stateBeforeStatement;
            if (!sectionMutatesConditionSymbols)
                sectionState = sectionState.AddPathCondition(sectionCondition);

            foreach (var statement in section.Statements)
            {
                if (statement is BreakStatementSyntax breakStatement &&
                    BreakTargetsSwitch(breakStatement, switchStatement))
                    break;

                AddPriorStatementStateFacts(
                    ref sectionState,
                    statement,
                    semanticModel,
                    cancellationToken);
            }

            branches.Add(new SwitchBranchState(
                sectionCondition,
                sectionState,
                sectionMutatesConditionSymbols));
        }

        if (branches.Count == 0) return;

        AddIdenticalSwitchBranchStateFacts(ref state, branches);
        if (branches.All(static branch => !branch.ConditionSymbolsMutated))
            AddConditionalSwitchBranchStateFacts(ref state, branches, switchStatement);
    }

    private static void AddCompletedSwitchExitExclusionStateFacts(
        ref SymbolicState state,
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (SwitchContinuingSectionsMutateConditionSymbols(switchStatement, semanticModel, cancellationToken)) return;

        foreach (var section in switchStatement.Sections)
        {
            if (!SectionDefinitelyExitsFromSwitch(section, switchStatement, semanticModel, cancellationToken) ||
                !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                    switchStatement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition))
                continue;

            state = state.AddPathCondition(new SymbolicNotCondition(sectionCondition));
        }
    }

    private static void AddIdenticalSwitchBranchStateFacts(
        ref SymbolicState state,
        IReadOnlyList<SwitchBranchState> branches)
    {
        var commonFactKeys = new HashSet<string>(
            branches[0].State.Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        var commonConditionKeys = new HashSet<string>(
            branches[0].State.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        for (var index = 1; index < branches.Count; index++)
        {
            commonFactKeys.IntersectWith(branches[index].State.Facts.Select(SymbolicState.CreateProofFactKey));
            commonConditionKeys.IntersectWith(
                branches[index].State.PathConditions.Select(SymbolicState.CreateProofConditionKey));
        }

        foreach (var fact in branches[0].State.Facts)
            if (commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact)))
                state = state.AddFact(fact);

        foreach (var condition in branches[0].State.PathConditions)
            if (commonConditionKeys.Contains(SymbolicState.CreateProofConditionKey(condition)))
                state = state.AddPathCondition(condition);
    }

    private static void AddConditionalSwitchBranchStateFacts(
        ref SymbolicState state,
        IReadOnlyList<SwitchBranchState> branches,
        SwitchStatementSyntax switchStatement)
    {
        var commonFactKeys = new HashSet<string>(
            branches[0].State.Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        var commonConditionKeys = new HashSet<string>(
            branches[0].State.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        for (var index = 1; index < branches.Count; index++)
        {
            commonFactKeys.IntersectWith(branches[index].State.Facts.Select(SymbolicState.CreateProofFactKey));
            commonConditionKeys.IntersectWith(
                branches[index].State.PathConditions.Select(SymbolicState.CreateProofConditionKey));
        }

        var addedCount = 0;
        foreach (var branch in branches)
        {
            foreach (var fact in branch.State.Facts)
            {
                if (commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact))) continue;

                if (!TryAddConditionalSwitchBranchStateFact(
                        ref state,
                        branch.Condition,
                        new SymbolicFactCondition(fact),
                        switchStatement,
                        ref addedCount))
                    return;
            }

            var branchConditionKey = SymbolicState.CreateProofConditionKey(branch.Condition);
            foreach (var condition in branch.State.PathConditions)
            {
                var conditionKey = SymbolicState.CreateProofConditionKey(condition);
                if (commonConditionKeys.Contains(conditionKey) ||
                    string.Equals(conditionKey, branchConditionKey, StringComparison.Ordinal))
                    continue;

                if (!TryAddConditionalSwitchBranchStateFact(
                        ref state,
                        branch.Condition,
                        condition,
                        switchStatement,
                        ref addedCount))
                    return;
            }
        }
    }

    private static bool TryAddConditionalSwitchBranchStateFact(
        ref SymbolicState state,
        SymbolicCondition branchCondition,
        SymbolicCondition branchFact,
        SwitchStatementSyntax switchStatement,
        ref int addedCount)
    {
        var limit = SymbolicAnalysisLimitContext.Limits.MaxMergedSwitchFacts;
        if (addedCount >= limit)
        {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.SwitchFactMerge,
                limit,
                addedCount + 1,
                switchStatement,
                "program_point.switch_state_fact_merge");
            return false;
        }

        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(branchCondition),
            branchFact));
        addedCount++;
        return true;
    }

    private static bool SwitchStatementHasDefaultOrExhaustiveBooleanLabels(
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (switchStatement.Sections.Any(static section =>
                section.Labels.Any(static label => label is DefaultSwitchLabelSyntax))) return true;

        var typeInfo = semanticModel.GetTypeInfo(switchStatement.Expression, cancellationToken);
        var switchType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (switchType?.SpecialType != SpecialType.System_Boolean) return false;

        var hasTrue = false;
        var hasFalse = false;
        foreach (var section in switchStatement.Sections)
            foreach (var label in section.Labels)
            {
                if (label is not CaseSwitchLabelSyntax caseLabel ||
                    semanticModel.GetConstantValue(caseLabel.Value, cancellationToken) is not
                    { HasValue: true, Value: bool value })
                    continue;

                if (value)
                    hasTrue = true;
                else
                    hasFalse = true;
            }

        return hasTrue && hasFalse;
    }

    private static bool SwitchContinuingSectionsMutateConditionSymbols(
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken);
        if (conditionSymbols.Count == 0) return false;

        foreach (var section in switchStatement.Sections)
        {
            if (SectionDefinitelyExitsFromSwitch(section, switchStatement, semanticModel, cancellationToken)) continue;

            if (SectionMutatesAnySymbolBeforeSwitchBreak(
                    section,
                    switchStatement,
                    conditionSymbols,
                    semanticModel,
                    cancellationToken))
                return true;
        }

        return false;
    }

    private static bool SectionMutatesAnySymbolBeforeSwitchBreak(
        SwitchSectionSyntax section,
        SwitchStatementSyntax switchStatement,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0) return false;

        foreach (var statement in section.Statements)
        {
            if (statement is BreakStatementSyntax breakStatement &&
                BreakTargetsSwitch(breakStatement, switchStatement))
                break;

            if (symbols.Any(symbol => StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken)))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<ISymbol> GetSwitchConditionSymbols(
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        AddReferencedSymbols(switchStatement.Expression, semanticModel, cancellationToken, symbols);
        foreach (var section in switchStatement.Sections)
            foreach (var label in section.Labels)
                switch (label)
                {
                    case CaseSwitchLabelSyntax caseLabel:
                        AddReferencedSymbols(caseLabel.Value, semanticModel, cancellationToken, symbols);
                        break;
                    case CasePatternSwitchLabelSyntax patternLabel:
                        AddReferencedSymbols(patternLabel.Pattern, semanticModel, cancellationToken, symbols);
                        AddDeclaredPatternSymbols(patternLabel.Pattern, semanticModel, cancellationToken, symbols);
                        if (patternLabel.WhenClause != null)
                            AddReferencedSymbols(patternLabel.WhenClause.Condition, semanticModel, cancellationToken,
                                symbols);

                        break;
                }

        return symbols;
    }

    private static IReadOnlyList<ISymbol> GetSwitchExpressionConditionSymbols(
        SwitchExpressionSyntax switchExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        AddReferencedSymbols(switchExpression.GoverningExpression, semanticModel, cancellationToken, symbols);

        foreach (var arm in switchExpression.Arms)
        {
            AddReferencedSymbols(arm.Pattern, semanticModel, cancellationToken, symbols);
            AddDeclaredPatternSymbols(arm.Pattern, semanticModel, cancellationToken, symbols);
            if (arm.WhenClause != null)
                AddReferencedSymbols(arm.WhenClause.Condition, semanticModel, cancellationToken, symbols);
        }

        return symbols;
    }

    private static void AddReferencedSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols)
    {
        foreach (var symbol in GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            AddSymbolIfAbsent(symbols, symbol);
    }

    private static void AddDeclaredPatternSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols)
    {
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            if (node is SingleVariableDesignationSyntax singleVariableDesignation &&
                singleVariableDesignation.Identifier.ValueText != "_" &&
                semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is ILocalSymbol
                    localSymbol)
                AddSymbolIfAbsent(symbols, localSymbol.OriginalDefinition);
    }

    private static void AddMemberNotNullWhenTargetSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols)
    {
        foreach (var invocation in root
                     .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation
                    invocationOperation ||
                invocationOperation.TargetMethod.IsStatic ||
                !IsCurrentInstanceInvocation(invocation))
                continue;

            foreach (var target in NullableFlowFacts.GetMemberNotNullWhenTargets(
                         invocationOperation.TargetMethod))
                if (NullableFlowFacts.TryResolveInstanceMemberTarget(
                        invocationOperation.TargetMethod.ContainingType,
                        target,
                        out var member))
                    AddSymbolIfAbsent(symbols, member);
        }
    }

    private static void AddSymbolIfAbsent(ICollection<ISymbol> symbols, ISymbol symbol)
    {
        if (symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol))) symbols.Add(symbol);
    }

    private static bool SectionBreaksFromSwitch(
        SwitchSectionSyntax section,
        SwitchStatementSyntax switchStatement)
    {
        return section.Statements.Count > 0 &&
               section.Statements[section.Statements.Count - 1] is BreakStatementSyntax breakStatement &&
               BreakTargetsSwitch(breakStatement, switchStatement);
    }

    private static bool SectionDefinitelyExitsFromSwitch(
        SwitchSectionSyntax section,
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return section.Statements.Count > 0 &&
               StatementDefinitelyExitsFromSwitch(section.Statements[section.Statements.Count - 1], switchStatement,
                   semanticModel, cancellationToken);
    }

    private static bool StatementDefinitelyExitsFromSwitch(
        StatementSyntax statement,
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        statement = UnwrapSingleStatementBlock(statement);
        return statement switch
        {
            ReturnStatementSyntax => true,
            ThrowStatementSyntax => true,
            BreakStatementSyntax breakStatement => !BreakTargetsSwitch(breakStatement, switchStatement),
            ContinueStatementSyntax => true,
            ExpressionStatementSyntax expressionStatement => ExpressionStatementDefinitelyExits(expressionStatement,
                semanticModel, cancellationToken),
            BlockSyntax block when block.Statements.Count > 0 => StatementDefinitelyExitsFromSwitch(
                block.Statements[block.Statements.Count - 1], switchStatement, semanticModel, cancellationToken),
            IfStatementSyntax ifStatement when ifStatement.Else != null =>
                StatementDefinitelyExitsFromSwitch(ifStatement.Statement, switchStatement, semanticModel,
                    cancellationToken) &&
                StatementDefinitelyExitsFromSwitch(ifStatement.Else.Statement, switchStatement, semanticModel,
                    cancellationToken),
            _ => false
        };
    }

    private static bool BreakTargetsSwitch(
        BreakStatementSyntax breakStatement,
        SwitchStatementSyntax switchStatement)
    {
        for (var ancestor = breakStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, switchStatement)) return true;

            if (ancestor is SwitchStatementSyntax ||
                IsLoopStatement(ancestor))
                return false;
        }

        return false;
    }

    private static bool CanAssumeLoopConditionFalseAfterNormalExit(
        StatementSyntax loopStatement,
        StatementSyntax loopBody)
    {
        foreach (var node in loopBody.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            switch (node)
            {
                case GotoStatementSyntax:
                    return false;
                case BreakStatementSyntax breakStatement when BreakTargetsLoop(breakStatement, loopStatement):
                    return false;
            }

        return true;
    }

    private static bool IsTopLevelLoopBodyStatement(
        StatementSyntax statement,
        StatementSyntax loopBody)
    {
        return ReferenceEquals(statement, loopBody) ||
               (loopBody is BlockSyntax block &&
                ReferenceEquals(statement.Parent, block));
    }

    private static bool TryGetOnlyParentIfBranch(
        StatementSyntax statement,
        out IfStatementSyntax ifStatement,
        out bool branchWhenTrue)
    {
        ifStatement = null!;
        branchWhenTrue = false;

        var branchStatement = statement;
        if (branchStatement.Parent is BlockSyntax block)
        {
            if (block.Statements.Count != 1 ||
                !ReferenceEquals(block.Statements[0], branchStatement))
                return false;

            branchStatement = block;
        }

        if (branchStatement.Parent is not IfStatementSyntax parentIf) return false;

        if (ReferenceEquals(parentIf.Statement, branchStatement))
        {
            ifStatement = parentIf;
            branchWhenTrue = true;
            return true;
        }

        if (parentIf.Else?.Statement is { } elseStatement &&
            ReferenceEquals(elseStatement, branchStatement))
        {
            ifStatement = parentIf;
            branchWhenTrue = false;
            return true;
        }

        return false;
    }

    private static bool TryGetDirectBreakBranch(
        IfStatementSyntax ifStatement,
        BreakStatementSyntax breakStatement,
        out bool branchWhenTrue)
    {
        if (StatementDirectlyContainsOnlyBreak(ifStatement.Statement, breakStatement))
        {
            branchWhenTrue = true;
            return true;
        }

        if (ifStatement.Else?.Statement is { } elseStatement &&
            StatementDirectlyContainsOnlyBreak(elseStatement, breakStatement))
        {
            branchWhenTrue = false;
            return true;
        }

        branchWhenTrue = false;
        return false;
    }

    private static bool TryGetDirectContinueBranch(
        IfStatementSyntax ifStatement,
        StatementSyntax loopStatement,
        out bool branchWhenTrue)
    {
        if (StatementDirectlyContainsOnlyContinue(ifStatement.Statement, loopStatement))
        {
            branchWhenTrue = true;
            return true;
        }

        if (ifStatement.Else?.Statement is { } elseStatement &&
            StatementDirectlyContainsOnlyContinue(elseStatement, loopStatement))
        {
            branchWhenTrue = false;
            return true;
        }

        branchWhenTrue = false;
        return false;
    }

    private static bool StatementDirectlyContainsOnlyBreak(
        StatementSyntax statement,
        BreakStatementSyntax breakStatement)
    {
        statement = UnwrapSingleStatementBlock(statement);
        return ReferenceEquals(statement, breakStatement);
    }

    private static bool StatementDirectlyContainsOnlyContinue(
        StatementSyntax statement,
        StatementSyntax loopStatement)
    {
        statement = UnwrapSingleStatementBlock(statement);
        return statement is ContinueStatementSyntax continueStatement &&
               ContinueTargetsLoop(continueStatement, loopStatement);
    }

    private static bool AnyConditionSymbolInvalidatedBeforeStatement(
        ExpressionSyntax condition,
        StatementSyntax root,
        int beforeSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        if (conditionSymbols.Count == 0) return false;

        foreach (var statement in EnumerateStatementsBefore(root, beforeSpanStart))
            if (conditionSymbols.Any(symbol =>
                    StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken)))
                return true;

        return false;
    }

    private static IEnumerable<StatementSyntax> EnumerateStatementsBefore(
        StatementSyntax root,
        int beforeSpanStart)
    {
        if (root is BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement.SpanStart >= beforeSpanStart) yield break;

                yield return statement;
            }

            yield break;
        }

        if (root.SpanStart < beforeSpanStart) yield return root;
    }

    private static bool LoopBodyContainsGoto(StatementSyntax loopBody)
    {
        return loopBody
            .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .Any(static node => node is GotoStatementSyntax);
    }

    private static bool BreakTargetsLoop(
        BreakStatementSyntax breakStatement,
        StatementSyntax loopStatement)
    {
        for (var ancestor = breakStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, loopStatement)) return true;

            if (ancestor is SwitchStatementSyntax ||
                IsLoopStatement(ancestor))
                return false;
        }

        return false;
    }

    private static bool ContinueTargetsLoop(
        ContinueStatementSyntax continueStatement,
        StatementSyntax loopStatement)
    {
        for (var ancestor = continueStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, loopStatement)) return true;

            if (IsLoopStatement(ancestor)) return false;
        }

        return false;
    }

    private static bool IsLoopStatement(SyntaxNode node)
    {
        return node is WhileStatementSyntax or
            ForStatementSyntax or
            ForEachStatementSyntax or
            DoStatementSyntax;
    }

    private static PatternSyntax UnwrapPattern(PatternSyntax pattern)
    {
        while (pattern is ParenthesizedPatternSyntax parenthesizedPattern) pattern = parenthesizedPattern.Pattern;

        return pattern;
    }

    private static bool TryGetFiniteElementExpressions(
        ExpressionSyntax expressionSyntax,
        out ImmutableArray<ExpressionSyntax> elementExpressions)
    {
        expressionSyntax = UnwrapExpression(expressionSyntax);
        SeparatedSyntaxList<ExpressionSyntax>? initializerExpressions = null;
        switch (expressionSyntax)
        {
            case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                initializerExpressions = initializer.Expressions;
                break;
            case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }:
                initializerExpressions = initializer.Expressions;
                break;
            case CollectionExpressionSyntax collectionExpression:
                return TryGetFiniteCollectionExpressionElements(collectionExpression, out elementExpressions);
        }

        if (initializerExpressions is not { } expressions ||
            expressions.Count == 0)
        {
            elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
            return false;
        }

        var limit = SymbolicAnalysisLimitContext.Limits.MaxFiniteForeachElementFacts;
        if (expressions.Count > limit)
        {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.ForeachElementFacts,
                limit,
                expressions.Count,
                expressionSyntax,
                "program_point.foreach_element_facts");
            elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
            return false;
        }

        elementExpressions = expressions.ToImmutableArray();
        return true;
    }

    private static bool TryGetPriorAssignedFiniteElementExpressions(
        ExpressionSyntax expressionSyntax,
        StatementSyntax foreachStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ImmutableArray<ExpressionSyntax> elementExpressions)
    {
        elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
        if (foreachStatement.Parent is not BlockSyntax containingBlock ||
            semanticModel.GetSymbolInfo(UnwrapExpression(expressionSyntax), cancellationToken).Symbol
                ?.OriginalDefinition is not { } receiverSymbol ||
            receiverSymbol is not ILocalSymbol and not IParameterSymbol)
            return false;

        for (var index = containingBlock.Statements.Count - 1; index >= 0; index--)
        {
            var statement = containingBlock.Statements[index];
            if (statement.SpanStart >= foreachStatement.SpanStart) continue;

            if (TryGetFiniteElementsFromAssignmentStatement(statement, receiverSymbol, semanticModel, cancellationToken,
                    out elementExpressions))
            {
                if (AnyStatementInvalidatesPriorAssignedFiniteElements(
                        containingBlock,
                        index + 1,
                        foreachStatement.SpanStart,
                        receiverSymbol,
                        semanticModel,
                        cancellationToken) ||
                    AnyReferencedElementSymbolInvalidatedAfterAssignment(
                        elementExpressions,
                        containingBlock,
                        index + 1,
                        foreachStatement.SpanStart,
                        receiverSymbol,
                        semanticModel,
                        cancellationToken))
                {
                    elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                    return false;
                }

                return true;
            }

            if (StatementInvalidatesPriorAssignedFiniteElements(statement, receiverSymbol, semanticModel,
                    cancellationToken))
            {
                elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                return false;
            }
        }

        return false;
    }

    private static bool AnyStatementInvalidatesPriorAssignedFiniteElements(
        BlockSyntax containingBlock,
        int firstStatementIndex,
        int beforeSpanStart,
        ISymbol receiverSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        for (var index = firstStatementIndex; index < containingBlock.Statements.Count; index++)
        {
            var statement = containingBlock.Statements[index];
            if (statement.SpanStart >= beforeSpanStart) break;

            if (StatementInvalidatesPriorAssignedFiniteElements(statement, receiverSymbol, semanticModel,
                    cancellationToken)) return true;
        }

        return false;
    }

    private static bool StatementInvalidatesPriorAssignedFiniteElements(
        StatementSyntax statement,
        ISymbol receiverSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return StatementInvalidatesSymbolValue(statement, receiverSymbol, semanticModel, cancellationToken);
    }

    private static bool StatementInvalidatesSymbolValue(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken) ||
               StatementMayMutateSymbolThroughReference(statement, symbol, semanticModel, cancellationToken);
    }

    private static bool AnyReferencedElementSymbolInvalidatedAfterAssignment(
        ImmutableArray<ExpressionSyntax> elementExpressions,
        BlockSyntax containingBlock,
        int firstStatementIndex,
        int beforeSpanStart,
        ISymbol receiverSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var referencedSymbols = ImmutableArray.CreateBuilder<ISymbol>();
        foreach (var elementExpression in elementExpressions)
            foreach (var referencedSymbol in GetReferencedLocalAndParameterSymbols(elementExpression, semanticModel,
                         cancellationToken))
            {
                if (SymbolEqualityComparer.Default.Equals(referencedSymbol, receiverSymbol)) return true;

                if (referencedSymbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, referencedSymbol)))
                    referencedSymbols.Add(referencedSymbol);
            }

        foreach (var referencedSymbol in referencedSymbols)
            if (AnyStatementInvalidatesPriorAssignedFiniteElements(
                    containingBlock,
                    firstStatementIndex,
                    beforeSpanStart,
                    referencedSymbol,
                    semanticModel,
                    cancellationToken))
                return true;

        return false;
    }

    private static bool TryGetFiniteElementsFromAssignmentStatement(
        StatementSyntax statement,
        ISymbol receiverSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ImmutableArray<ExpressionSyntax> elementExpressions)
    {
        elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var declarator in localDeclaration.Declaration.Variables)
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken)?.OriginalDefinition is
                    { } declaredSymbol &&
                    SymbolEqualityComparer.Default.Equals(declaredSymbol, receiverSymbol))
                    return declarator.Initializer != null &&
                           TryGetFiniteElementExpressions(declarator.Initializer.Value, out elementExpressions);

            return false;
        }

        if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol?.OriginalDefinition is
            { } assignedSymbol &&
            SymbolEqualityComparer.Default.Equals(assignedSymbol, receiverSymbol))
            return TryGetFiniteElementExpressions(assignment.Right, out elementExpressions);

        return false;
    }

    private static bool TryGetFiniteCollectionExpressionElements(
        CollectionExpressionSyntax collectionExpression,
        out ImmutableArray<ExpressionSyntax> elementExpressions)
    {
        if (collectionExpression.Elements.Count == 0)
        {
            elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
            return false;
        }

        var limit = SymbolicAnalysisLimitContext.Limits.MaxFiniteForeachElementFacts;
        if (collectionExpression.Elements.Count > limit)
        {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.ForeachElementFacts,
                limit,
                collectionExpression.Elements.Count,
                collectionExpression,
                "program_point.foreach_collection_element_facts");
            elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>(collectionExpression.Elements.Count);
        foreach (var element in collectionExpression.Elements)
            switch (element)
            {
                case ExpressionElementSyntax expressionElement:
                    builder.Add(expressionElement.Expression);
                    break;
                default:
                    elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                    return false;
            }

        elementExpressions = builder.ToImmutable();
        return true;
    }

    private static bool IsSupportedForeachLengthReceiver(ExpressionSyntax expressionSyntax)
    {
        expressionSyntax = UnwrapExpression(expressionSyntax);
        return expressionSyntax is ArrayCreationExpressionSyntax or
            ImplicitArrayCreationExpressionSyntax or
            CollectionExpressionSyntax;
    }

    private static bool IsSupportedForeachLengthReceiver(ITypeSymbol? type)
    {
        return type is IArrayTypeSymbol { Rank: 1 } ||
               type?.SpecialType == SpecialType.System_String;
    }

    private static bool StatementDefinitelyExits(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (statement is ReturnStatementSyntax or
            ThrowStatementSyntax or
            BreakStatementSyntax or
            ContinueStatementSyntax)
            return true;

        statement = UnwrapSingleStatementBlock(statement);
        if (statement is ExpressionStatementSyntax expressionStatement &&
            ExpressionStatementDefinitelyExits(expressionStatement, semanticModel, cancellationToken))
            return true;

        try
        {
            var controlFlow = semanticModel.AnalyzeControlFlow(statement);
            if (controlFlow is { Succeeded: true }) return !controlFlow.EndPointIsReachable;
        }
        catch (ArgumentException)
        {
        }

        return false;
    }

    private static bool ExpressionStatementDefinitelyExits(
        ExpressionStatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expression = UnwrapExpression(statement.Expression);
        return expression is InvocationExpressionSyntax invocation &&
               semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
               NullableFlowFacts.HasDoesNotReturn(invocationOperation.TargetMethod);
    }

    private static StatementSyntax UnwrapSingleStatementBlock(StatementSyntax statement)
    {
        while (statement is BlockSyntax { Statements.Count: 1 } block) statement = block.Statements[0];

        return statement;
    }

    private static void RemoveStateFactsInvalidatedByNestedMutations(
        ref SymbolicState state,
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (TryGetMutatedExpression(node, out var mutatedExpression))
            {
                var mutatedSymbol = GetMutatedSymbol(mutatedExpression, semanticModel, cancellationToken);
                if (mutatedSymbol is ILocalSymbol or IParameterSymbol)
                    state = RemoveStateFactsReferencingSymbol(state, mutatedSymbol.OriginalDefinition);

                if (mutatedSymbol is IFieldSymbol or IPropertySymbol &&
                    IsCurrentInstanceMemberReference(mutatedExpression, semanticModel, cancellationToken))
                    state = RemoveStateFactsReferencingImplicitThisMember(state, mutatedSymbol.Name);

                foreach (var receiverSymbol in GetMutatedReceiverSymbols(mutatedExpression, semanticModel,
                             cancellationToken)) state = RemoveStateFactsReferencingSymbol(state, receiverSymbol);
            }

            foreach (var receiverSymbol in GetPotentiallyMutatedArraySymbols(node, semanticModel, cancellationToken))
                state = RemoveStateFactsReferencingSymbol(state, receiverSymbol);
        }
    }

    private static void RemoveStateFactsInvalidatedByMutationTarget(
        ref SymbolicState state,
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var mutatedSymbol = GetMutatedSymbol(mutatedExpression, semanticModel, cancellationToken);
        if (mutatedSymbol is ILocalSymbol or IParameterSymbol)
            state = RemoveStateFactsReferencingSymbol(state, mutatedSymbol.OriginalDefinition);
        else if (mutatedSymbol is IFieldSymbol or IPropertySymbol &&
                 IsCurrentInstanceMemberReference(mutatedExpression, semanticModel, cancellationToken))
            state = RemoveStateFactsReferencingImplicitThisMember(state, mutatedSymbol.Name);

        foreach (var receiverSymbol in GetMutatedReceiverSymbols(mutatedExpression, semanticModel, cancellationToken))
            state = RemoveStateFactsReferencingSymbol(state, receiverSymbol);
    }

    private static ISymbol? GetMutatedSymbol(
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol;
        if (symbol != null) return NormalizeMutatedSymbol(symbol);

        return semanticModel.GetOperation(mutatedExpression, cancellationToken) switch
        {
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null
        };
    }

    private static ISymbol NormalizeMutatedSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol { AssociatedSymbol: IPropertySymbol property }
            ? property
            : symbol;
    }

    private static bool IsCurrentInstanceMemberReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (expression is IdentifierNameSyntax &&
            GetMutatedSymbol(expression, semanticModel, cancellationToken) is { IsStatic: false }
                and (IFieldSymbol or IPropertySymbol))
            return true;

        return expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

    private static IEnumerable<ISymbol> GetMutatedReceiverSymbols(
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var receiverExpression = UnwrapExpression(mutatedExpression) switch
        {
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            _ => null
        };

        if (receiverExpression == null) yield break;

        var receiverSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(receiverExpression), cancellationToken).Symbol
            ?.OriginalDefinition;
        if (receiverSymbol is ILocalSymbol or IParameterSymbol) yield return receiverSymbol;
    }

    private static IEnumerable<ISymbol> GetPotentiallyMutatedArraySymbols(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    foreach (var symbol in GetReferencedArraySymbols(memberAccess.Expression, semanticModel,
                                 cancellationToken))
                        yield return symbol;

                foreach (var argument in invocation.ArgumentList.Arguments)
                    foreach (var symbol in GetReferencedArraySymbols(argument.Expression, semanticModel, cancellationToken))
                        yield return symbol;

                break;
            case ObjectCreationExpressionSyntax { ArgumentList: { } argumentList }:
                foreach (var argument in argumentList.Arguments)
                    foreach (var symbol in GetReferencedArraySymbols(argument.Expression, semanticModel, cancellationToken))
                        yield return symbol;

                break;
        }
    }

    private static IEnumerable<ISymbol> GetReferencedArraySymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is IArrayTypeSymbol)
                yield return symbol;
    }

    private static bool StatementMayMutateSymbolThroughReference(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!IsPotentiallyMutableThroughReference(SymbolicFactFactory.GetTrackedSymbolType(symbol))) return false;

        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            if (NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool NodeMayMutateSymbolThroughReference(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    ExpressionReferencesSymbol(memberAccess.Expression, symbol, semanticModel, cancellationToken))
                    return true;

                return invocation.ArgumentList.Arguments.Any(argument =>
                    ExpressionReferencesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
            case ObjectCreationExpressionSyntax { ArgumentList: { } argumentList }:
                return argumentList.Arguments.Any(argument =>
                    ExpressionReferencesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
            default:
                return false;
        }
    }

    private static bool IsPotentiallyMutableThroughReference(ITypeSymbol? type)
    {
        return type is IArrayTypeSymbol ||
               (type?.IsReferenceType == true &&
                type.SpecialType != SpecialType.System_String);
    }

    private static void AddAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot,
        SymbolicTerm? previousValueOverride = null)
    {
        var previousValueTerm = previousValueOverride;
        var hadPreviousValueTerm = previousValueTerm != null ||
                                   TryGetCurrentStateSymbolValueTerm(
                                       state,
                                       assignedSymbol,
                                       out previousValueTerm);
        state = RemoveStateFactsReferencingSymbol(state, assignedSymbol);

        var hasThrowGuard = TryGetThrowGuardedValue(
            valueExpression,
            out var throwGuardedValue,
            out var guardExpression,
            out var guardBranchWhenTrue,
            out var requiresNonNullValue);
        var effectiveValueExpression = hasThrowGuard
            ? throwGuardedValue
            : valueExpression;
        var effectiveValueIsAssignedSymbol =
            SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                UnwrapExpression(effectiveValueExpression),
                semanticModel,
                cancellationToken,
                out var effectiveValueSymbol) &&
            SymbolEqualityComparer.Default.Equals(effectiveValueSymbol, assignedSymbol);
        var isSelfReferential = ExpressionReferencesSymbol(
            effectiveValueExpression,
            assignedSymbol,
            semanticModel,
            cancellationToken);
        SymbolicTerm? selfReferentialValueTerm = null;
        if (isSelfReferential &&
            (!hadPreviousValueTerm ||
             !TryCreateSelfReferentialAssignedValueStateTerm(
                 previousValueTerm,
                 assignedSymbol,
                 effectiveValueExpression,
                 semanticModel,
                 cancellationToken,
                 out selfReferentialValueTerm)))
        {
            AddThrowGuardedAssignmentCompletionStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                effectiveValueIsAssignedSymbol,
                guardExpression,
                guardBranchWhenTrue,
                requiresNonNullValue,
                semanticModel,
                cancellationToken,
                provenanceRoot);
            return;
        }

        var assignedType = SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol);
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (!isSelfReferential)
            AddAssignedSourceSymbolSnapshotStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken);

        SymbolicTerm? assignedValueTerm = null;
        if (isSelfReferential)
            assignedValueTerm = selfReferentialValueTerm;
        else
        {
            if (assignedType?.SpecialType == SpecialType.System_Boolean &&
                SymbolicSemanticPipeline.LowerBooleanValueTerm(effectiveValueExpression, context) is
                { IsExact: true, Value: { } loweredBooleanValueTerm })
                assignedValueTerm = loweredBooleanValueTerm;

            if (assignedValueTerm == null &&
                SymbolicSemanticPipeline.LowerTerm(effectiveValueExpression, context) is
                { IsExact: true, Value: { } loweredValueTerm })
                assignedValueTerm = loweredValueTerm;
        }

        if (!isSelfReferential)
            AddSwitchExpressionAssignedValueStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                provenanceRoot);

        if (!isSelfReferential)
            AddAssignedNullableStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                context,
                provenanceRoot);

        if (!isSelfReferential &&
            assignedType?.IsReferenceType == true &&
            TryCreateSymbolTerm(assignedSymbol, out var assignedReferenceTarget) &&
            assignedReferenceTarget.Kind == SmtValueKind.Reference &&
            SymbolicSemanticPipeline.LowerReferenceTerm(effectiveValueExpression, context) is
            { IsExact: true, Value: { } assignedReferenceValue } &&
            CanCompareIrTerms(assignedReferenceTarget, assignedReferenceValue))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                assignedReferenceTarget,
                assignedReferenceValue,
                effectiveValueExpression,
                provenanceRoot + ".assigned-reference");
            AddConditionalReferenceAssignmentStateFacts(
                ref state,
                assignedReferenceTarget,
                assignedReferenceValue,
                effectiveValueExpression,
                provenanceRoot);
        }

        if (assignedType?.SpecialType == SpecialType.System_String &&
            assignedValueTerm is not SymbolicNullTerm &&
            !IsDefinitelyNullReferenceValue(effectiveValueExpression, semanticModel, cancellationToken))
            AddAssignedStringStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                context,
                provenanceRoot);
        else if (TryCreateSymbolTerm(assignedSymbol, out var targetTerm) &&
                 assignedValueTerm != null &&
                 assignedValueTerm.Kind == targetTerm.Kind &&
                 CanCompareIrTerms(targetTerm, assignedValueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                assignedValueTerm,
                effectiveValueExpression,
                provenanceRoot + ".assigned-value");

        AddAssignedNonNullStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddAssignedNullStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddNotNullIfNotNullAssignedStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            context,
            provenanceRoot);
        AddAssignedIntegerRangeStateFacts(
            ref state,
            assignedSymbol,
            assignedValueTerm,
            effectiveValueExpression,
            provenanceRoot);
        AddAssignedReferenceBackedStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            context,
            provenanceRoot);
        AddFiniteArrayElementAssignedValueStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddCollectionExpressionLengthLowerBoundStateFact(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            provenanceRoot);
        AddRemainderAssignedRangeStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            context,
            provenanceRoot);
        AddAssignedAsExpressionStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddAssignedLengthStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            assignedType,
            context,
            provenanceRoot);
        AddTupleElementAssignedValueStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddTupleElementSourceSymbolSnapshotStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);

        if (hasThrowGuard)
            AddThrowGuardedAssignmentCompletionStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                effectiveValueIsAssignedSymbol,
                guardExpression,
                guardBranchWhenTrue,
                requiresNonNullValue,
                semanticModel,
                cancellationToken,
                provenanceRoot);
    }

    private static void AddThrowGuardedAssignmentCompletionStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax effectiveValueExpression,
        bool effectiveValueIsAssignedSymbol,
        ExpressionSyntax? guardExpression,
        bool guardBranchWhenTrue,
        bool requiresNonNullValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (guardExpression != null)
        {
            if (!ExpressionReferencesSymbol(
                    guardExpression,
                    assignedSymbol,
                    semanticModel,
                    cancellationToken) ||
                effectiveValueIsAssignedSymbol)
                AddReachabilityCondition(
                    ref state,
                    guardExpression,
                    guardBranchWhenTrue,
                    semanticModel,
                    cancellationToken);

            return;
        }

        if (!requiresNonNullValue) return;

        AddReferenceNullCondition(
            ref state,
            effectiveValueExpression,
            false,
            semanticModel,
            cancellationToken,
            provenanceRoot + ".throw-guard.non-null");
    }

    private static void AddAssignedSourceSymbolSnapshotStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                UnwrapExpression(valueExpression),
                semanticModel,
                cancellationToken,
                out var sourceSymbol) ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, assignedSymbol))
            return;

        if (TryCreateSymbolTerm(sourceSymbol, out var sourceTerm) &&
            TryCreateSymbolTerm(assignedSymbol, out var targetTerm) &&
            CanCompareIrTerms(sourceTerm, targetTerm))
            AddSubstitutedStateFacts(ref state, sourceTerm, targetTerm);

        if (TryCreateNullableSymbolTerms(sourceSymbol, out var sourceHasValue, out var sourceValue) &&
            TryCreateNullableSymbolTerms(assignedSymbol, out var targetHasValue, out var targetValue) &&
            CanCompareIrTerms(sourceHasValue, targetHasValue) &&
            CanCompareIrTerms(sourceValue, targetValue))
        {
            AddSubstitutedStateFacts(ref state, sourceHasValue, targetHasValue);
            AddSubstitutedStateFacts(ref state, sourceValue, targetValue);
        }
    }

    private static void AddSubstitutedStateFacts(
        ref SymbolicState state,
        SymbolicTerm source,
        SymbolicTerm target)
    {
        if (!CanCompareIrTerms(source, target) ||
            string.Equals(
                SymbolicState.CreateProofTermKey(source),
                SymbolicState.CreateProofTermKey(target),
                StringComparison.Ordinal))
            return;

        var existingFacts = state.Facts;
        var existingConditions = state.PathConditions;
        foreach (var fact in existingFacts)
        {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(fact, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofFactKey(substituted),
                    SymbolicState.CreateProofFactKey(fact),
                    StringComparison.Ordinal))
                state = state.AddFact(substituted);
        }

        foreach (var condition in existingConditions)
        {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(condition, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofConditionKey(substituted),
                    SymbolicState.CreateProofConditionKey(condition),
                    StringComparison.Ordinal))
                state = state.AddPathCondition(substituted);
        }
    }

    private static void AddAssignedNullableStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryCreateNullableSymbolTerms(
                assignedSymbol,
                out var targetHasValue,
                out var targetValue))
            return;

        SymbolicTerm sourceHasValue;
        SymbolicTerm? sourceValue = null;
        var hasValueLowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(valueExpression, context);
        if (hasValueLowering is { IsExact: true, Value: { } nullableHasValue })
        {
            sourceHasValue = nullableHasValue;
            if (SymbolicSemanticPipeline.LowerNullableValueTerm(valueExpression, context) is
                { IsExact: true, Value: { } nullableValue })
                sourceValue = nullableValue;
        }
        else if (SymbolicSemanticPipeline.LowerTerm(valueExpression, context) is
                 { IsExact: true, Value: { } wrappedValue } &&
                 wrappedValue.Kind == targetValue.Kind)
        {
            sourceHasValue = new SymbolicBooleanConstantTerm(true);
            sourceValue = wrappedValue;
        }
        else
        {
            return;
        }

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetHasValue,
            sourceHasValue,
            valueExpression,
            provenanceRoot + ".nullable.has-value");

        if (sourceValue == null ||
            !CanCompareIrTerms(targetValue, sourceValue))
            return;

        var targetHasValueFact = SymbolicFact.Exact(
            new SymbolicTruthAtom(targetHasValue),
            valueExpression,
            provenanceRoot + ".nullable.value-present",
            assignedSymbol);
        var targetValueFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, targetValue, sourceValue),
            valueExpression,
            provenanceRoot + ".nullable.value",
            assignedSymbol);
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(new SymbolicFactCondition(targetHasValueFact)),
            new SymbolicFactCondition(targetValueFact)));
    }

    private static void AddConditionalReferenceAssignmentStateFacts(
        ref SymbolicState state,
        SymbolicTerm target,
        SymbolicTerm assignedValue,
        ExpressionSyntax valueExpression,
        string provenanceRoot)
    {
        if (assignedValue is not SymbolicConditionalTerm conditional ||
            target.Kind != SmtValueKind.Reference ||
            conditional.WhenTrue.Kind != SmtValueKind.Reference ||
            conditional.WhenFalse.Kind != SmtValueKind.Reference)
            return;

        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".conditional-reference.target-non-null"));
        var trueValueNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                conditional.WhenTrue,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".conditional-reference.true-value-null"));
        var falseValueNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                conditional.WhenFalse,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".conditional-reference.false-value-null"));

        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(conditional.Condition),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                targetNonNull,
                trueValueNull)));
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            conditional.Condition,
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                targetNonNull,
                falseValueNull)));
    }

    private static bool TryCreateNullableSymbolTerms(
        ISymbol symbol,
        out SymbolicTerm hasValue,
        out SymbolicTerm value)
    {
        hasValue = null!;
        value = null!;
        if (!TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(symbol), out var underlyingType) ||
            !TryGetValueKind(underlyingType, out var valueKind))
            return false;

        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol);
        hasValue = new SymbolicNullableHasValueTerm(symbolName);
        value = new SymbolicNullableValueTerm(symbolName, valueKind);
        return true;
    }

    private static void AddAssignedCurrentInstanceMemberStateFacts(
        ref SymbolicState state,
        SymbolicTerm targetTerm,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        var hasThrowGuard = TryGetThrowGuardedValue(
            valueExpression,
            out var throwGuardedValue,
            out _,
            out _,
            out _);
        var effectiveValueExpression = hasThrowGuard
            ? throwGuardedValue
            : valueExpression;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (SymbolicSemanticPipeline.LowerTerm(effectiveValueExpression, context) is
            { IsExact: true, Value: { } assignedValueTerm } &&
            assignedValueTerm.Kind == targetTerm.Kind &&
            CanCompareIrTerms(targetTerm, assignedValueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                assignedValueTerm,
                effectiveValueExpression,
                provenanceRoot + ".member.assigned-value");

        AddAssignedNonNullStateFacts(
            ref state,
            targetTerm,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot + ".member");
        AddAssignedReferenceBackedStateFacts(
            ref state,
            targetTerm,
            effectiveValueExpression,
            semanticModel,
            context,
            provenanceRoot + ".member");
    }

    private static void AddCollectionExpressionLengthLowerBoundStateFact(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        string provenanceRoot)
    {
        valueExpression = UnwrapExpression(valueExpression);
        if (valueExpression is not CollectionExpressionSyntax collectionExpression) return;

        var fixedElementCount = collectionExpression.Elements.Count(static element =>
            element is ExpressionElementSyntax);
        if (fixedElementCount == 0 ||
            !collectionExpression.Elements.Any(static element => element is SpreadElementSyntax) ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetReference) ||
            SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(
                SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol),
                targetReference,
                valueExpression) is not { IsExact: true, Value: { } targetLength })
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.GreaterThanOrEqual,
            targetLength,
            new SymbolicIntegerConstantTerm(fixedElementCount),
            valueExpression,
            provenanceRoot + ".collection-expression.fixed-lower-bound");
    }

    private static void AddFiniteArrayElementAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
            !TryGetValueKind(arrayType.ElementType, out var elementKind) ||
            !TryGetFiniteElementExpressions(valueExpression, out var elementExpressions) ||
            !TryCreateSymbolTerm(assignedSymbol, out var receiver) ||
            receiver.Kind != SmtValueKind.Reference)
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        for (var index = 0; index < elementExpressions.Length; index++)
        {
            var elementExpression = elementExpressions[index];
            var lowering = SymbolicSemanticPipeline.LowerTerm(elementExpression, context);
            if (ExpressionReferencesSymbol(
                    elementExpression,
                    assignedSymbol,
                    semanticModel,
                    cancellationToken) ||
                lowering is not { IsExact: true, Value: { } elementValue } ||
                elementValue.Kind != elementKind)
                continue;

            var targetElement = new SymbolicElementTerm(
                receiver,
                new SymbolicIntegerConstantTerm(index),
                elementKind);
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetElement,
                elementValue,
                elementExpression,
                provenanceRoot + ".finite-array-element");

            var targetFromEndElement = new SymbolicElementTerm(
                receiver,
                new SymbolicFromEndIndexTerm(
                    new SymbolicIntegerConstantTerm(elementExpressions.Length - index)),
                elementKind);
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetFromEndElement,
                elementValue,
                elementExpression,
                provenanceRoot + ".finite-array-element.from-end");

            if (elementKind == SmtValueKind.Reference &&
                IsDefinitelyNonNullReferenceValue(elementExpression, semanticModel, cancellationToken))
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.NotEqual,
                    targetElement,
                    new SymbolicNullTerm(),
                    elementExpression,
                    provenanceRoot + ".finite-array-element.non-null");
        }
    }

    private static void AddAssignedIntegerRangeStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        SymbolicTerm? assignedValueTerm,
        ExpressionSyntax valueExpression,
        string provenanceRoot)
    {
        if (assignedValueTerm is not { Kind: SmtValueKind.Int } valueTerm ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm) ||
            targetTerm.Kind != SmtValueKind.Int)
            return;

        if (StateProvesPositiveInteger(state, valueTerm))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThan,
                targetTerm,
                new SymbolicIntegerConstantTerm(0),
                valueExpression,
                provenanceRoot + ".assigned-integer.positive");
            return;
        }

        if (StateProvesNonNegativeInteger(state, valueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThanOrEqual,
                targetTerm,
                new SymbolicIntegerConstantTerm(0),
                valueExpression,
                provenanceRoot + ".assigned-integer.non-negative");
    }

    private static bool TryCreateSelfReferentialAssignedValueStateTerm(
        SymbolicTerm previousValueTerm,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm updatedValueTerm)
    {
        updatedValueTerm = null!;
        valueExpression = UnwrapExpression(valueExpression);
        if (previousValueTerm.Kind != SmtValueKind.Int) return false;

        var substitutions = new Dictionary<ISymbol, SymbolicTerm>(SymbolEqualityComparer.Default)
        {
            [assignedSymbol.OriginalDefinition] = previousValueTerm
        };
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            valueExpression,
            new SymbolicLoweringContext(
                semanticModel,
                cancellationToken,
                symbolSubstitutions: substitutions));
        if (lowering is not { IsExact: true, Value: { Kind: SmtValueKind.Int } updatedValue })
            return false;

        updatedValueTerm = updatedValue;
        return true;
    }

    private static void AddAssignedNonNullStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (!TryCreateSymbolTerm(assignedSymbol, out var targetReference)) return;

        AddAssignedNonNullStateFacts(
            ref state,
            targetReference,
            valueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
    }

    private static void AddAssignedNullStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (!IsDefinitelyNullReferenceValue(valueExpression, semanticModel, cancellationToken) ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetReference) ||
            targetReference.Kind != SmtValueKind.Reference)
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetReference,
            new SymbolicNullTerm(),
            valueExpression,
            provenanceRoot + ".assigned-null");
    }

    private static bool IsDefinitelyNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        return constant is { HasValue: true, Value: null } &&
               (semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(expression, cancellationToken).Type)?.IsReferenceType == true;
    }

    private static void AddNotNullIfNotNullAssignedStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryCreateSymbolTerm(assignedSymbol, out var target) ||
            target.Kind != SmtValueKind.Reference ||
            SymbolicSemanticPipeline.LowerNotNullIfNotNullAssignedResultTerm(valueExpression, context) is not
                { IsExact: true, Value: { Kind: SmtValueKind.Bool } resultNonNull })
            return;

        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".not-null-if-not-null.target",
            assignedSymbol));
        var targetNonNullTerm = new SymbolicConditionalTerm(
            targetNonNull,
            new SymbolicBooleanConstantTerm(true),
            new SymbolicBooleanConstantTerm(false));
        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetNonNullTerm,
            resultNonNull,
            valueExpression,
            provenanceRoot + ".not-null-if-not-null.result");
    }

    private static void AddAssignedNonNullStateFacts(
        ref SymbolicState state,
        SymbolicTerm targetReference,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (!IsDefinitelyNonNullReferenceValue(valueExpression, semanticModel, cancellationToken) ||
            targetReference.Kind != SmtValueKind.Reference)
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.NotEqual,
            targetReference,
            new SymbolicNullTerm(),
            valueExpression,
            provenanceRoot + ".assigned-non-null");
    }

    private static void AddAssignedReferenceBackedStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryCreateSymbolTerm(assignedSymbol, out var targetReference)) return;

        AddAssignedReferenceBackedStateFacts(
            ref state,
            targetReference,
            valueExpression,
            semanticModel,
            context,
            provenanceRoot);
    }

    private static void AddAssignedReferenceBackedStateFacts(
        ref SymbolicState state,
        SymbolicTerm targetReference,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (targetReference.Kind != SmtValueKind.Reference ||
            IsDefinitelyNullReferenceValue(valueExpression, semanticModel, context.CancellationToken))
            return;

        var valueType = semanticModel.GetTypeInfo(valueExpression, context.CancellationToken).ConvertedType ??
                        semanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type;
        var sourceType = semanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type;
        if (sourceType != null &&
            TryCreateReferenceBackedLengthFactsFromSourceType(sourceType, valueType))
            valueType = sourceType;

        if (valueType == null) return;

        if (SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(valueType, targetReference, valueExpression) is
                { IsExact: true, Value: { } targetLength } &&
            SymbolicSemanticPipeline.LowerBuiltInLengthTerm(valueExpression, context) is
            { IsExact: true, Value: { } valueLength } &&
            CanCompareIrTerms(targetLength, valueLength))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetLength,
                valueLength,
                valueExpression,
                provenanceRoot + ".reference-backed-length");

        AddAssignedCollectionCountStateFacts(
            ref state,
            targetReference,
            valueExpression,
            valueType,
            sourceType,
            provenanceRoot);

        if (valueType.SpecialType == SpecialType.System_String &&
            SymbolicSemanticPipeline.ProjectStringContentTerm(targetReference, valueExpression) is
                { IsExact: true, Value: { } targetString } &&
            SymbolicSemanticPipeline.LowerStringTerm(valueExpression, context) is
            { IsExact: true, Value: { } valueString })
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetString,
                valueString,
                valueExpression,
                provenanceRoot + ".reference-backed-string");

        if (valueType is not IArrayTypeSymbol { Rank: > 1 } arrayType) return;

        for (var dimension = 0; dimension < arrayType.Rank; dimension++)
        {
            var dimensionLowering =
                SymbolicSemanticPipeline.LowerArrayDimensionLengthTerm(valueExpression, dimension, context);
            if (!TryCreateArrayDimensionLengthTerm(targetReference, arrayType, dimension,
                    out var targetDimensionLength) ||
                dimensionLowering is not { IsExact: true, Value: { } valueDimensionLength } ||
                !CanCompareIrTerms(targetDimensionLength, valueDimensionLength))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetDimensionLength,
                valueDimensionLength,
                valueExpression,
                provenanceRoot + ".reference-backed-array-length");
        }
    }

    private static void AddAssignedCollectionCountStateFacts(
        ref SymbolicState state,
        SymbolicTerm targetReference,
        ExpressionSyntax valueExpression,
        ITypeSymbol targetType,
        ITypeSymbol? sourceType,
        string provenanceRoot)
    {
        if (SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(targetType, targetReference, valueExpression) is not
                { IsExact: true, Value: { } targetCount } ||
            targetCount is not SymbolicCountTerm ||
            !TryCreateExactListCreationCountTerm(valueExpression, sourceType ?? targetType, out var valueCount) ||
            !CanCompareIrTerms(targetCount, valueCount))
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetCount,
            valueCount,
            valueExpression,
            provenanceRoot + ".reference-backed-count");
    }

    private static bool TryCreateExactListCreationCountTerm(
        ExpressionSyntax valueExpression,
        ITypeSymbol? sourceType,
        out SymbolicTerm countTerm)
    {
        countTerm = null!;
        if (!IsKnownExactCountListType(sourceType)) return false;

        valueExpression = UnwrapExpression(valueExpression);
        switch (valueExpression)
        {
            case ObjectCreationExpressionSyntax objectCreation:
                return TryCreateExactListObjectCreationCountTerm(
                    objectCreation.ArgumentList?.Arguments.Count ?? 0,
                    objectCreation.Initializer,
                    out countTerm);
            case ImplicitObjectCreationExpressionSyntax implicitObjectCreation:
                return TryCreateExactListObjectCreationCountTerm(
                    implicitObjectCreation.ArgumentList?.Arguments.Count ?? 0,
                    implicitObjectCreation.Initializer,
                    out countTerm);
            default:
                return false;
        }
    }

    private static bool TryCreateExactListObjectCreationCountTerm(
        int argumentCount,
        InitializerExpressionSyntax? initializer,
        out SymbolicTerm countTerm)
    {
        countTerm = null!;
        if (argumentCount != 0) return false;

        if (initializer == null)
        {
            countTerm = new SymbolicIntegerConstantTerm(0);
            return true;
        }

        if (!initializer.IsKind(SyntaxKind.CollectionInitializerExpression)) return false;

        countTerm = new SymbolicIntegerConstantTerm(initializer.Expressions.Count);
        return true;
    }

    private static bool IsKnownExactCountListType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               string.Equals(
                   namedType.OriginalDefinition.ToDisplayString(),
                   "System.Collections.Generic.List<T>",
                   StringComparison.Ordinal);
    }

    private static bool TryCreateReferenceBackedLengthFactsFromSourceType(
        ITypeSymbol sourceType,
        ITypeSymbol? convertedType)
    {
        if (convertedType == null) return true;

        if (SymbolEqualityComparer.Default.Equals(sourceType, convertedType)) return false;

        return HasBuiltInLengthShape(sourceType) &&
               !HasBuiltInLengthShape(convertedType);
    }

    private static bool HasBuiltInLengthShape(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_String ||
               type is IArrayTypeSymbol { Rank: >= 1 };
    }

    private static void AddRemainderAssignedRangeStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        valueExpression = UnwrapExpression(valueExpression);
        if (valueExpression is not BinaryExpressionSyntax moduloExpression ||
            !moduloExpression.IsKind(SyntaxKind.ModuloExpression) ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm) ||
            targetTerm.Kind != SmtValueKind.Int)
            return;

        var dividendLowering = SymbolicSemanticPipeline.LowerTerm(moduloExpression.Left, context);
        var divisorLowering = SymbolicSemanticPipeline.LowerTerm(moduloExpression.Right, context);
        if (dividendLowering is not { IsExact: true, Value: { } dividendTerm } ||
            dividendTerm.Kind != SmtValueKind.Int ||
            divisorLowering is not { IsExact: true, Value: { } divisorTerm } ||
            divisorTerm.Kind != SmtValueKind.Int ||
            !StateProvesNonNegativeInteger(state, dividendTerm) ||
            !StateProvesPositiveInteger(state, divisorTerm))
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.GreaterThanOrEqual,
            targetTerm,
            new SymbolicIntegerConstantTerm(0),
            valueExpression,
            provenanceRoot + ".assigned-remainder.non-negative");
        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.LessThan,
            targetTerm,
            divisorTerm,
            valueExpression,
            provenanceRoot + ".assigned-remainder.upper-bound");
    }

    private static bool StateProvesPositiveInteger(SymbolicState state, SymbolicTerm term)
    {
        return StateConditionProvesIntegerRelation(state, term, true);
    }

    private static bool StateProvesNonNegativeInteger(SymbolicState state, SymbolicTerm term)
    {
        return StateConditionProvesIntegerRelation(state, term, false);
    }

    private static bool StateConditionProvesIntegerRelation(
        SymbolicState state,
        SymbolicTerm term,
        bool requireStrictlyPositive)
    {
        if (term.Kind != SmtValueKind.Int) return false;

        return state.PathConditions.Any(condition =>
                   ConditionProvesIntegerRelation(condition, term, requireStrictlyPositive)) ||
               state.Facts.Any(fact => FactProvesIntegerRelation(fact, term, requireStrictlyPositive));
    }

    private static bool ConditionProvesIntegerRelation(
        SymbolicCondition condition,
        SymbolicTerm term,
        bool requireStrictlyPositive)
    {
        return condition switch
        {
            SymbolicFactCondition factCondition => FactProvesIntegerRelation(factCondition.Fact, term,
                requireStrictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binaryCondition =>
                ConditionProvesIntegerRelation(binaryCondition.Left, term, requireStrictlyPositive) ||
                ConditionProvesIntegerRelation(binaryCondition.Right, term, requireStrictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binaryCondition =>
                ConditionProvesIntegerRelation(binaryCondition.Left, term, requireStrictlyPositive) &&
                ConditionProvesIntegerRelation(binaryCondition.Right, term, requireStrictlyPositive),
            _ => false
        };
    }

    private static bool FactProvesIntegerRelation(
        SymbolicFact fact,
        SymbolicTerm term,
        bool requireStrictlyPositive)
    {
        if (!fact.Polarity ||
            fact.Atom is not SymbolicRelationAtom relation)
            return false;

        if (Equals(relation.Left, term) &&
            relation.Right is SymbolicIntegerConstantTerm rightConstant)
            return RelationProvesIntegerRelation(
                relation.Operator,
                rightConstant.Value,
                requireStrictlyPositive,
                true);

        if (Equals(relation.Right, term) &&
            relation.Left is SymbolicIntegerConstantTerm leftConstant)
            return RelationProvesIntegerRelation(
                relation.Operator,
                leftConstant.Value,
                requireStrictlyPositive,
                false);

        return false;
    }

    private static bool RelationProvesIntegerRelation(
        SymbolicRelationOperator op,
        long constant,
        bool requireStrictlyPositive,
        bool termOnLeft)
    {
        return (termOnLeft, op) switch
        {
            (true, SymbolicRelationOperator.GreaterThan) => requireStrictlyPositive ? constant >= 0 : constant >= -1,
            (true, SymbolicRelationOperator.GreaterThanOrEqual) => requireStrictlyPositive
                ? constant > 0
                : constant >= 0,
            (true, SymbolicRelationOperator.Equal) => requireStrictlyPositive ? constant > 0 : constant >= 0,
            (false, SymbolicRelationOperator.LessThan) => requireStrictlyPositive ? constant <= 0 : constant <= -1,
            (false, SymbolicRelationOperator.LessThanOrEqual) => requireStrictlyPositive ? constant < 0 : constant <= 0,
            (false, SymbolicRelationOperator.Equal) => requireStrictlyPositive ? constant > 0 : constant >= 0,
            _ => false
        };
    }

    private static void AddAssignedAsExpressionStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        valueExpression = UnwrapExpression(valueExpression);
        if (valueExpression is not BinaryExpressionSyntax asExpression ||
            !asExpression.IsKind(SyntaxKind.AsExpression) ||
            asExpression.Right is not TypeSyntax typeSyntax ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm) ||
            targetTerm.Kind != SmtValueKind.Reference)
            return;

        var targetType = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var sourceLowering = SymbolicSemanticPipeline.LowerTerm(asExpression.Left, context);
        if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(targetType, out var typeKey) ||
            sourceLowering is not { IsExact: true, Value: { } source } ||
            source.Kind != SmtValueKind.Reference)
            return;

        var targetIsNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                targetTerm,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".as.target-null"));
        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                targetTerm,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".as.target-non-null"));
        var sourceNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                source,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".as.source-non-null"));
        var runtimeTypeTest = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicTypeTestAtom(source, typeKey),
            valueExpression,
            provenanceRoot + ".as.runtime-type",
            evidenceKey: provenanceRoot + ".as.runtime-type"));

        state = state.AddPathCondition(new SymbolicBinaryCondition(SymbolicConditionOperator.Or, targetIsNull,
            sourceNonNull));
        state = state.AddPathCondition(new SymbolicBinaryCondition(SymbolicConditionOperator.Or, targetIsNull,
            runtimeTypeTest));
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(new SymbolicBinaryCondition(SymbolicConditionOperator.And, sourceNonNull,
                runtimeTypeTest)),
            targetNonNull));
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                sourceNonNull,
                new SymbolicNotCondition(runtimeTypeTest))),
            targetIsNull));
    }

    private static void AddAssignedStringStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        var valueLowering = SymbolicSemanticPipeline.LowerStringTerm(valueExpression, context);
        if (!TryCreateSymbolTerm(assignedSymbol, out var targetReference) ||
            SymbolicSemanticPipeline.ProjectStringContentTerm(targetReference, valueExpression) is not
                { IsExact: true, Value: { } targetString } ||
            valueLowering is not { IsExact: true, Value: { } valueString })
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetString,
            valueString,
            valueExpression,
            provenanceRoot + ".assigned-string");

        if (TryIsDefinitelyNonNullStringExpression(valueExpression, context))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                targetReference,
                new SymbolicNullTerm(),
                valueExpression,
                provenanceRoot + ".assigned-string.non-null");
    }

    private static bool TryIsDefinitelyNonNullStringExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        expression = UnwrapExpression(expression);

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue) return constantValue.Value is string;

        if (expression is CastExpressionSyntax castExpression &&
            context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type?.SpecialType ==
            SpecialType.System_String)
            return TryIsDefinitelyNonNullStringExpression(castExpression.Expression, context);

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression))
            return TryIsDefinitelyNonNullStringExpression(coalesceExpression.Left, context) ||
                   TryIsDefinitelyNonNullStringExpression(coalesceExpression.Right, context);

        if (expression is ConditionalExpressionSyntax conditionalExpression)
            return TryIsDefinitelyNonNullStringExpression(conditionalExpression.WhenTrue, context) &&
                   TryIsDefinitelyNonNullStringExpression(conditionalExpression.WhenFalse, context);

        if (SymbolicSemanticPipeline.LowerStringTerm(expression, context) is
            { IsExact: true, Value: { } term })
            return term is SymbolicStringConstantTerm or SymbolicStringConcatTerm;

        return false;
    }

    private static void AddAssignedLengthStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        ITypeSymbol? assignedType,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        var lengthLowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(valueExpression, context);
        if (assignedType == null ||
            IsDefinitelyNullReferenceValue(valueExpression, context.SemanticModel, context.CancellationToken) ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetReference) ||
            SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(assignedType, targetReference, valueExpression) is not
                { IsExact: true, Value: { } targetLength } ||
            lengthLowering is not { IsExact: true, Value: { } valueLength } ||
            !CanCompareIrTerms(targetLength, valueLength))
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetLength,
            valueLength,
            valueExpression,
            provenanceRoot + ".assigned-length");
    }

    private static void AddTupleElementAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        valueExpression = UnwrapExpression(valueExpression);
        if (valueExpression is not TupleExpressionSyntax tupleExpression ||
            !TryGetTupleElementStorageNames(assignedSymbol, tupleExpression.Arguments.Count, out var elementNames))
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        for (var index = 0; index < tupleExpression.Arguments.Count; index++)
        {
            var argumentExpression = tupleExpression.Arguments[index].Expression;
            if (ExpressionReferencesSymbol(argumentExpression, assignedSymbol, semanticModel, cancellationToken))
                continue;

            AddTupleElementAssignedValueStateFacts(
                ref state,
                assignedSymbol,
                elementNames[index],
                argumentExpression,
                semanticModel,
                cancellationToken,
                context,
                provenanceRoot + ".tuple-element");
        }
    }

    private static void AddTupleElementAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol tupleSymbol,
        string elementName,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
            !TryCreateTupleElementTerm(tupleSymbol, elementName, out var targetTerm))
            return;

        if (elementType.SpecialType == SpecialType.System_String &&
            SymbolicSemanticPipeline.ProjectStringContentTerm(targetTerm, valueExpression) is
                { IsExact: true, Value: { } targetString } &&
            SymbolicSemanticPipeline.LowerStringTerm(valueExpression, context) is
            { IsExact: true, Value: { } valueString })
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetString,
                valueString,
                valueExpression,
                provenanceRoot + ".assigned-string");
        else if (SymbolicSemanticPipeline.LowerTerm(valueExpression, context) is
                 { IsExact: true, Value: { } valueTerm } &&
                 CanCompareIrTerms(targetTerm, valueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                valueTerm,
                valueExpression,
                provenanceRoot + ".assigned-value");

        if (IsDefinitelyNonNullReferenceValue(valueExpression, semanticModel, cancellationToken) &&
            targetTerm.Kind == SmtValueKind.Reference)
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                targetTerm,
                new SymbolicNullTerm(),
                valueExpression,
                provenanceRoot + ".assigned-non-null");

        if (targetTerm.Kind == SmtValueKind.Reference &&
            TryCreateBuiltInLengthTerm(targetTerm, elementType, valueExpression, out var targetLength) &&
            SymbolicSemanticPipeline.LowerBuiltInLengthTerm(valueExpression, context) is
            { IsExact: true, Value: { } valueLength } &&
            CanCompareIrTerms(targetLength, valueLength))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetLength,
                valueLength,
                valueExpression,
                provenanceRoot + ".assigned-length");

        if (targetTerm.Kind != SmtValueKind.Reference ||
            elementType is not IArrayTypeSymbol { Rank: > 1 } arrayType)
            return;

        for (var dimension = 0; dimension < arrayType.Rank; dimension++)
        {
            var dimensionLowering =
                SymbolicSemanticPipeline.LowerArrayDimensionLengthTerm(valueExpression, dimension, context);
            if (!TryCreateArrayDimensionLengthTerm(targetTerm, arrayType, dimension, out var targetDimensionLength) ||
                dimensionLowering is not { IsExact: true, Value: { } valueDimensionLength } ||
                !CanCompareIrTerms(targetDimensionLength, valueDimensionLength))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetDimensionLength,
                valueDimensionLength,
                valueExpression,
                provenanceRoot + ".assigned-dimension-length");
        }
    }

    private static void AddTupleElementSourceSymbolSnapshotStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                UnwrapExpression(valueExpression),
                semanticModel,
                cancellationToken,
                out var sourceSymbol) ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, assignedSymbol) ||
            !TryGetTupleElementStorageNames(assignedSymbol, 0, out var targetElementNames) ||
            !TryGetTupleElementStorageNames(sourceSymbol, targetElementNames.Length, out var sourceElementNames))
            return;

        for (var index = 0; index < targetElementNames.Length; index++)
        {
            if (!TryCreateTupleElementTerm(assignedSymbol, targetElementNames[index], out var targetElement) ||
                !TryCreateTupleElementTerm(sourceSymbol, sourceElementNames[index], out var sourceElement) ||
                !CanCompareIrTerms(targetElement, sourceElement))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetElement,
                sourceElement,
                valueExpression,
                provenanceRoot + ".tuple-element.snapshot");
        }
    }

    private static bool TryPrepareTupleDeconstructionDeclarationTargets(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool isDeconstructionDeclaration,
        out List<ISymbol?> targetSymbols)
    {
        isDeconstructionDeclaration = false;
        targetSymbols = new List<ISymbol?>();
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            UnwrapExpression(assignment.Left) is not DeclarationExpressionSyntax declarationExpression ||
            declarationExpression.Designation is not ParenthesizedVariableDesignationSyntax leftDesignation)
            return false;

        isDeconstructionDeclaration = true;
        foreach (var variableDesignation in leftDesignation.Variables)
        {
            if (variableDesignation is not SingleVariableDesignationSyntax singleVariableDesignation) return false;

            if (singleVariableDesignation.Identifier.ValueText == "_")
            {
                targetSymbols.Add(null);
                continue;
            }

            if (semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol
                localSymbol) return false;

            targetSymbols.Add(localSymbol.OriginalDefinition);
        }

        var nonDiscardTargets = targetSymbols.Where(static symbol => symbol != null).Cast<ISymbol>().ToArray();
        if (ExpressionReferencesAnySymbol(assignment.Right, nonDiscardTargets, semanticModel, cancellationToken))
            return false;

        return true;
    }

    private static bool TryHandleTupleDeconstructionDeclarationState(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!TryPrepareTupleDeconstructionDeclarationTargets(
                assignment,
                semanticModel,
                cancellationToken,
                out var isDeconstructionDeclaration,
                out var targetSymbols))
            return isDeconstructionDeclaration;

        AddTupleElementTargetStateFacts(
            ref state,
            targetSymbols,
            assignment.Right,
            semanticModel,
            cancellationToken,
            "ir.path.prior-statement.tuple-target");
        return true;
    }

    private static bool TryHandleTupleAssignmentState(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            UnwrapExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            return false;

        var targetSymbols = new List<ISymbol?>();
        foreach (var argument in leftTuple.Arguments)
        {
            if (argument.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "_")
            {
                targetSymbols.Add(null);
                continue;
            }

            var targetSymbol = semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol;
            if (targetSymbol is ILocalSymbol or IParameterSymbol)
            {
                targetSymbols.Add(targetSymbol.OriginalDefinition);
                continue;
            }

            return true;
        }

        foreach (var targetSymbol in targetSymbols)
            if (targetSymbol != null)
                state = RemoveStateFactsReferencingSymbol(state, targetSymbol);

        var nonDiscardTargets = targetSymbols.Where(static symbol => symbol != null).Cast<ISymbol>().ToArray();
        if (targetSymbols.All(static symbol => symbol == null) ||
            ExpressionReferencesAnySymbol(assignment.Right, nonDiscardTargets, semanticModel, cancellationToken))
            return true;

        AddTupleElementTargetStateFacts(
            ref state,
            targetSymbols,
            assignment.Right,
            semanticModel,
            cancellationToken,
            "ir.path.prior-statement.tuple-target");
        return true;
    }

    private static void AddTupleElementTargetStateFacts(
        ref SymbolicState state,
        IReadOnlyList<ISymbol?> targetSymbols,
        ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        rightExpression = UnwrapExpression(rightExpression);
        if (rightExpression is TupleExpressionSyntax rightTuple)
        {
            if (rightTuple.Arguments.Count != targetSymbols.Count) return;

            for (var index = 0; index < targetSymbols.Count; index++)
            {
                if (targetSymbols[index] == null) continue;

                AddAssignedValueStateFacts(
                    ref state,
                    targetSymbols[index]!,
                    rightTuple.Arguments[index].Expression,
                    semanticModel,
                    cancellationToken,
                    provenanceRoot);
            }

            return;
        }

        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                rightExpression,
                semanticModel,
                cancellationToken,
                out var sourceSymbol) ||
            !TryGetTupleElementStorageNames(sourceSymbol, targetSymbols.Count, out var sourceElementNames))
            return;

        for (var index = 0; index < targetSymbols.Count; index++)
        {
            if (targetSymbols[index] == null ||
                !TryCreateSymbolTerm(targetSymbols[index]!, out var targetTerm) ||
                !TryCreateTupleElementTerm(sourceSymbol, sourceElementNames[index], out var sourceElementTerm) ||
                !CanCompareIrTerms(targetTerm, sourceElementTerm))
                continue;

            AddSubstitutedStateFacts(ref state, sourceElementTerm, targetTerm);
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                sourceElementTerm,
                rightExpression,
                provenanceRoot + ".assigned-value");
        }
    }

    private static void AddSwitchExpressionAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (UnwrapExpression(valueExpression) is not SwitchExpressionSyntax switchExpression ||
            switchExpression.Arms.Count == 0 ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm))
            return;

        var conditionSymbols = GetSwitchExpressionConditionSymbols(switchExpression, semanticModel, cancellationToken);
        if (ExpressionMutatesAnySymbol(switchExpression, conditionSymbols, semanticModel, cancellationToken)) return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var addedCount = 0;
        foreach (var arm in switchExpression.Arms)
        {
            if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
                continue;

            SymbolicCondition? armFact = null;
            if (UnwrapExpression(arm.Expression) is ThrowExpressionSyntax)
            {
                armFact = new SymbolicNotCondition(armCondition);
            }
            else if (SymbolicSemanticPipeline.LowerTerm(arm.Expression, context) is
                     { IsExact: true, Value: { } armValueTerm } &&
                     armValueTerm.Kind == targetTerm.Kind &&
                     CanCompareIrTerms(targetTerm, armValueTerm))
            {
                armFact = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    new SymbolicNotCondition(armCondition),
                    new SymbolicFactCondition(SymbolicFact.Exact(
                        new SymbolicRelationAtom(SymbolicRelationOperator.Equal, targetTerm, armValueTerm),
                        arm.Expression,
                        provenanceRoot + ".switch-expression-assigned-value",
                        assignedSymbol)));
            }

            if (armFact == null) continue;

            var limit = SymbolicAnalysisLimitContext.Limits.MaxMergedSwitchFacts;
            if (addedCount >= limit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.SwitchFactMerge,
                    limit,
                    addedCount + 1,
                    switchExpression,
                    "program_point.switch_expression_state_fact_merge");
                return;
            }

            state = state.AddPathCondition(armFact);
            addedCount++;
        }
    }

    private static void AddElementAssignmentStateFact(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            UnwrapExpression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess)
            return;

        var receiverSymbols =
            GetReferencedLocalAndParameterSymbols(elementAccess.Expression, semanticModel, cancellationToken);
        if (ExpressionReferencesAnySymbol(assignment.Right, receiverSymbols, semanticModel, cancellationToken))
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var targetLowering = SymbolicSemanticPipeline.LowerTerm(elementAccess, context);
        var valueLowering = SymbolicSemanticPipeline.LowerTerm(assignment.Right, context);
        if (targetLowering is not { IsExact: true, Value: { } target } ||
            valueLowering is not { IsExact: true, Value: { } value } ||
            !CanCompareIrTerms(target, value))
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            target,
            value,
            assignment,
            "ir.path.prior-statement.element-assignment");
    }

    private static bool TryGetTupleElementStorageNames(
        ISymbol assignedSymbol,
        int expectedCount,
        out string[] elementNames)
    {
        elementNames = Array.Empty<string>();
        var type = assignedSymbol switch
        {
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            _ => null
        };

        if (type is not INamedTypeSymbol { IsTupleType: true } tupleType ||
            (expectedCount > 0 &&
             tupleType.TupleElements.Length != expectedCount))
            return false;

        elementNames = new string[tupleType.TupleElements.Length];
        for (var index = 0; index < tupleType.TupleElements.Length; index++)
        {
            var field = tupleType.TupleElements[index].CorrespondingTupleField ?? tupleType.TupleElements[index];
            if (string.IsNullOrWhiteSpace(field.Name)) return false;

            elementNames[index] = field.Name;
        }

        return true;
    }

    private static bool TryGetTupleElementType(
        ISymbol tupleSymbol,
        string elementName,
        out ITypeSymbol elementType)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(tupleSymbol);
        if (type is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            elementType = null!;
            return false;
        }

        var element = tupleType.TupleElements
            .FirstOrDefault(field =>
                string.Equals((field.CorrespondingTupleField ?? field).Name, elementName, StringComparison.Ordinal));
        if (element == null)
        {
            elementType = null!;
            return false;
        }

        elementType = element.Type;
        return true;
    }

    private static bool TryCreateTupleElementTerm(
        ISymbol tupleSymbol,
        string elementName,
        out SymbolicTerm term)
    {
        if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind))
        {
            term = null!;
            return false;
        }

        var tuple = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(tupleSymbol),
            SmtValueKind.Reference);
        term = new SymbolicMemberTerm(tuple, elementName, elementKind);
        return true;
    }

    private static bool IsDefinitelyNonNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
            expression,
            semanticModel,
            cancellationToken);
    }

    private static bool TryGetThrowGuardedValue(
        ExpressionSyntax valueExpression,
        out ExpressionSyntax effectiveValueExpression,
        out ExpressionSyntax? guardExpression,
        out bool guardBranchWhenTrue,
        out bool requiresNonNullValue)
    {
        valueExpression = UnwrapExpression(valueExpression);
        if (valueExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            UnwrapExpression(coalesceExpression.Right) is ThrowExpressionSyntax)
        {
            effectiveValueExpression = coalesceExpression.Left;
            guardExpression = null;
            guardBranchWhenTrue = true;
            requiresNonNullValue = true;
            return true;
        }

        if (valueExpression is ConditionalExpressionSyntax conditionalExpression)
        {
            if (UnwrapExpression(conditionalExpression.WhenFalse) is ThrowExpressionSyntax)
            {
                effectiveValueExpression = conditionalExpression.WhenTrue;
                guardExpression = conditionalExpression.Condition;
                guardBranchWhenTrue = true;
                requiresNonNullValue = false;
                return true;
            }

            if (UnwrapExpression(conditionalExpression.WhenTrue) is ThrowExpressionSyntax)
            {
                effectiveValueExpression = conditionalExpression.WhenFalse;
                guardExpression = conditionalExpression.Condition;
                guardBranchWhenTrue = false;
                requiresNonNullValue = false;
                return true;
            }
        }

        effectiveValueExpression = null!;
        guardExpression = null;
        guardBranchWhenTrue = true;
        requiresNonNullValue = false;
        return false;
    }

    private static bool TryGetIncrementedOrDecrementedSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol,
        out int delta)
    {
        expression = UnwrapExpression(expression);
        var operand = expression switch
        {
            PrefixUnaryExpressionSyntax prefixUnary
                when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                prefixUnary.Operand,
            PostfixUnaryExpressionSyntax postfixUnary
                when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                postfixUnary.Operand,
            _ => null
        };

        var expressionSymbol = operand == null
            ? null
            : semanticModel.GetSymbolInfo(operand, cancellationToken).Symbol;
        if (expressionSymbol is not ILocalSymbol && expressionSymbol is not IParameterSymbol)
        {
            symbol = null!;
            delta = 0;
            return false;
        }

        symbol = expressionSymbol.OriginalDefinition;
        delta = expression.IsKind(SyntaxKind.PreIncrementExpression) ||
                expression.IsKind(SyntaxKind.PostIncrementExpression)
            ? 1
            : -1;
        return true;
    }

    private static bool TryCreateBuiltInLengthTerm(
        SymbolicTerm receiver,
        ITypeSymbol? type,
        SyntaxNode source,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(type, receiver, source);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    private static bool TryCreateArrayDimensionLengthTerm(
        SymbolicTerm receiver,
        IArrayTypeSymbol arrayType,
        int dimension,
        out SymbolicTerm term)
    {
        if (dimension < 0 ||
            dimension >= arrayType.Rank)
        {
            term = null!;
            return false;
        }

        term = new SymbolicArrayDimensionLengthTerm(receiver, dimension);
        return true;
    }

    private static bool TryCreateMemberDerivedTerm(
        SymbolicTerm receiver,
        ISymbol memberSymbol,
        SmtValueKind kind,
        out SymbolicTerm output)
    {
        output = null!;
        if (receiver.Kind != SmtValueKind.Reference) return false;

        output = new SymbolicMemberTerm(receiver, memberSymbol.Name, kind);
        return true;
    }

    private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
    {
        return SymbolicFactFactory.TryGetValueKind(
            type,
            IsIntegralOrEnumType,
            IsReferenceLikeType,
            out kind);
    }

    private static bool IsReferenceLikeType(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Dynamic ||
               type.IsReferenceType ||
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type) ||
               SymbolicTypeFacts.IsSupportedTupleCarrierType(type);
    }

    private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
    {
        return SymbolicTypeFacts.TryGetNullableUnderlyingType(type, out underlyingType);
    }

    private static bool ExpressionReferencesSymbol(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
        {
            if (node is not ExpressionSyntax expression) continue;

            var expressionSymbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (expressionSymbol != null &&
                SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol))
                return true;
        }

        return false;
    }

    private static bool ExpressionReferencesAnySymbol(
        SyntaxNode root,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in symbols)
            if (ExpressionReferencesSymbol(root, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool TryGetIntegralConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long value)
    {
        var constantValue = semanticModel.GetConstantValue(UnwrapExpression(expression), cancellationToken);
        if (!constantValue.HasValue || constantValue.Value == null)
        {
            value = 0;
            return false;
        }

        switch (constantValue.Value)
        {
            case sbyte sbyteValue:
                value = sbyteValue;
                return true;
            case byte byteValue:
                value = byteValue;
                return true;
            case short shortValue:
                value = shortValue;
                return true;
            case ushort ushortValue:
                value = ushortValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case uint uintValue:
                value = uintValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case ulong ulongValue when ulongValue <= long.MaxValue:
                value = (long)ulongValue;
                return true;
            case char charValue:
                value = charValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
    {
        return SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType(typeSymbol);
    }

    private sealed class SwitchBranchState
    {
        internal SwitchBranchState(
            SymbolicCondition condition,
            SymbolicState state,
            bool conditionSymbolsMutated)
        {
            Condition = condition;
            State = state;
            ConditionSymbolsMutated = conditionSymbolsMutated;
        }

        internal SymbolicCondition Condition { get; }

        internal SymbolicState State { get; }

        internal bool ConditionSymbolsMutated { get; }
    }

}

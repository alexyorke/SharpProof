using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

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
            if (SymbolicLoopStateTransfer.IsLoopBodyBlock(containingBlock.Block))
                SymbolicStateInvalidator.InvalidateNestedMutations(
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
            SymbolicExpressionStateTransfer.AddCompletedExpressionStateFacts(
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

        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            tryStatement.Block,
            semanticModel,
            cancellationToken);
        if (tryStatement.Finally?.Block.Span.Contains(site.SpanStart) != true) return;

        foreach (var catchClause in tryStatement.Catches)
            SymbolicStateInvalidator.InvalidateNestedMutations(
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
            SymbolicLoopStateTransfer.CollectForInitializerState(forStatement, semanticModel, cancellationToken));
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
            state = SymbolicStateValueFacts.RemoveReferences(state, symbol);
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
                    !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                        ifStatementSyntax.Condition,
                        ifStatementSyntax.Statement,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    AddReachabilityCondition(ref state, ifStatementSyntax.Condition, true, semanticModel,
                        cancellationToken);
                else if (ifStatementSyntax.Else?.Statement is { } elseStatement &&
                         elseStatement.Span.Contains(syntaxNode.Span) &&
                         !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
                    !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                        conditionalExpressionSyntax.Condition,
                        conditionalExpressionSyntax.WhenTrue,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    AddReachabilityCondition(ref state, conditionalExpressionSyntax.Condition, true, semanticModel,
                        cancellationToken);
                else if (conditionalExpressionSyntax.WhenFalse.Span.Contains(syntaxNode.Span) &&
                         !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
                if (SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
                     !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
                     SymbolicLoopStateTransfer.IsLocalOrParameterReference(lockStatementSyntax.Expression, semanticModel, cancellationToken) &&
                     !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
                         !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
                     !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                         whileStatementSyntax.Condition,
                         whileStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                AddReachabilityCondition(ref state, whileStatementSyntax.Condition, true, semanticModel,
                    cancellationToken);
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
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
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
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
                    !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                        forStatementSyntax.Condition,
                        forStatementSyntax.Statement,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken))
                    AddReachabilityCondition(ref state, forStatementSyntax.Condition, true, semanticModel,
                        cancellationToken);

                SymbolicLoopStateTransfer.AddForLoopBodyInvariantStateFacts(
                    ref state,
                    forStatementSyntax,
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is ForEachStatementSyntax forEachStatementSyntax &&
                     forEachStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                     !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                         forEachStatementSyntax.Expression,
                         forEachStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                SymbolicLoopStateTransfer.AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachStatementSyntax.Expression,
                    forEachStatementSyntax,
                    forEachStatementSyntax.Statement,
                    semanticModel,
                    cancellationToken);
            }
            else if (ancestor is ForEachVariableStatementSyntax forEachVariableStatementSyntax &&
                     forEachVariableStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                     !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                         forEachVariableStatementSyntax.Expression,
                         forEachVariableStatementSyntax.Statement,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
                SymbolicLoopStateTransfer.AddForeachBodyEntryStateFacts(
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
                    !SymbolicLoopStateTransfer.AnySwitchStatementConditionSymbolAssignedBeforeUse(
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
                    !SymbolicLoopStateTransfer.AnySwitchExpressionConditionSymbolAssignedBeforeUse(
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

    internal static void AddReachabilityCondition(
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
            SymbolMutationFacts.ExpressionReferencesSymbol(
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
        SymbolicAssignmentStateTransfer.AddAssignedValueStateFacts(
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
        var hasThrowGuard = SymbolicAssignmentStateTransfer.TryGetThrowGuardedValue(
            valueExpression,
            out var throwGuardedValue,
            out var guardExpression,
            out var guardBranchWhenTrue,
            out var requiresNonNullValue);
        var effectiveValueExpression = hasThrowGuard
            ? throwGuardedValue
            : valueExpression;
        if (SymbolMutationFacts.ExpressionReferencesSymbol(
                effectiveValueExpression,
                assignedSymbol,
                semanticModel,
                cancellationToken))
            return SymbolicStateValueFacts.TryGetCurrentValue(
                       state,
                       assignedSymbol,
                       out var previousValueTerm) &&
                   SymbolicAssignmentStateTransfer.TryCreateSelfReferentialAssignedValueStateTerm(
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

    internal static void AddReferenceNullCondition(
        ref SymbolicState state,
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? provenance = null)
    {
        if (SymbolicAssignmentStateTransfer.IsDefinitelyNullReferenceValue(expression, semanticModel, cancellationToken))
        {
            if (!isNull) state = MarkContradictory(state);

            return;
        }

        if (SymbolicAssignmentStateTransfer.IsDefinitelyNonNullReferenceValue(expression, semanticModel, cancellationToken))
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

        if (!SymbolicAssignmentStateTransfer.TryCreateMemberDerivedTerm(receiver, memberSymbol, memberKind, out memberTerm)) return false;

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

    private static void AddCatchBodyEntryStateFacts(
        ref SymbolicState state,
        CatchClauseSyntax catchClause,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (catchClause.Declaration != null &&
            semanticModel.GetDeclaredSymbol(catchClause.Declaration, cancellationToken) is ILocalSymbol localSymbol &&
            !SymbolicLoopStateTransfer.IsSymbolAssignedBetween(
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
            !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
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
        SymbolicLoopStateTransfer.AddThrowGuardedExpressionStateFacts(
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
        if (SymbolicAssignmentStateTransfer.TryGetThrowGuardedValue(
                initializer,
                out var guardedValue,
                out _,
                out _,
                out _))
        {
            effectiveInitializer = guardedValue;
            SymbolicLoopStateTransfer.AddThrowGuardedExpressionStateFacts(
                ref state,
                initializer,
                usingBody,
                semanticModel,
                cancellationToken,
                "ir.path.using-entry.throw-guarded-not-null");
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(effectiveInitializer, context);
        if (!TryCreateSymbolTerm(localSymbol, out var target) ||
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

    private static void RemoveStateFactsInvalidatedByContainingBlockEntry(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in GetContainingBlockEntryAssignedSymbols(block, semanticModel, cancellationToken))
            state = SymbolicStateValueFacts.RemoveReferences(state, symbol);
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

                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
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

                SymbolicLoopStateTransfer.AddForLoopBodyInvariantStateFacts(
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
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case DoStatementSyntax doStatement when ReferenceEquals(doStatement.Statement, block):
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
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

                SymbolicLoopStateTransfer.AddForLoopBodyInvariantStateFacts(
                    ref state,
                    forStatement,
                    semanticModel,
                    cancellationToken);
                break;
            case ForEachStatementSyntax forEachStatement when ReferenceEquals(forEachStatement.Statement, block):
                SymbolicLoopStateTransfer.AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachStatement.Expression,
                    forEachStatement,
                    forEachStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
            case ForEachVariableStatementSyntax forEachVariableStatement
                when ReferenceEquals(forEachVariableStatement.Statement, block):
                SymbolicLoopStateTransfer.AddForeachBodyEntryStateFacts(
                    ref state,
                    forEachVariableStatement.Expression,
                    forEachVariableStatement,
                    forEachVariableStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
        }
    }

    internal static void AddPriorStatementStateFacts(
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

                SymbolicStateInvalidator.InvalidateNestedMutations(
                    ref state,
                    declarator.Initializer.Value,
                    semanticModel,
                    cancellationToken);
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    SymbolicAssignmentStateTransfer.AddAssignedValueStateFacts(
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
            SymbolicExpressionStateTransfer.AddAssignmentExpressionStateFacts(
                ref state,
                assignment,
                expressionStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is ExpressionStatementSyntax unaryExpressionStatement &&
            SymbolMutationFacts.TryGetIncrementedOrDecrementedSymbol(
                unaryExpressionStatement.Expression,
                semanticModel,
                cancellationToken,
                out var mutatedSymbol,
                out var delta))
        {
            if (SymbolicStateValueFacts.TryGetCurrentValue(state, mutatedSymbol, out var previousValueTerm) &&
                SymbolicAssignmentValueUpdater.TryCreateIncrementOrDecrement(
                    previousValueTerm,
                    delta,
                    unaryExpressionStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    mutatedSymbol,
                    out var updatedValueTerm) &&
                TryCreateSymbolTerm(mutatedSymbol, out var targetTerm) &&
                targetTerm.Kind == SmtValueKind.Int &&
                !SymbolicIrReferenceScanner.ContainsVariableOrMember(
                    updatedValueTerm,
                    SymbolicFactFactory.GetSmtVariableName(mutatedSymbol)))
            {
                state = SymbolicStateValueFacts.RemoveReferences(state, mutatedSymbol);
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

            state = SymbolicStateValueFacts.RemoveReferences(state, mutatedSymbol);
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
        SymbolicStateInvalidator.InvalidateNestedMutations(
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
            SymbolicBranchCompletionStateTransfer.AddCompletedIfStatementStateFacts(
                ref state,
                completedIfStatement,
                stateBeforeStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is SwitchStatementSyntax completedSwitchStatement)
        {
            SymbolicBranchCompletionStateTransfer.AddCompletedSwitchStatementStateFacts(
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
            SymbolicStateInvalidator.InvalidateNestedMutations(
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

        foreach (var hiddenSymbol in SymbolicBranchCompletionStateTransfer.GetLocalsDeclaredInside(
                     tryStatement,
                     semanticModel,
                     cancellationToken))
            state = SymbolicStateValueFacts.RemoveReferences(state, hiddenSymbol);
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

    internal static void AddCompletedLoopStatementStateFacts(
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
                SymbolicLoopStateTransfer.AddForLoopBodyInvariantStateFacts(ref state, forStatement, semanticModel, cancellationToken);
                break;
            case WhileStatementSyntax whileStatement:
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case DoStatementSyntax doStatement:
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
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
        if (SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(
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

        if (!SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
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
            !SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
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
                !SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
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
                !SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
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
                !SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
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
        if (!SymbolicLoopStateTransfer.IsLocalOrParameterReference(lockStatement.Expression, semanticModel, cancellationToken) ||
            SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(
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

    internal static void AddNormalCompletionStateFacts(
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

    internal static void AddTopLevelMemberNotNullNormalCompletionStateFacts(
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

    internal static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
    {
        var invokedExpression = UnwrapExpression(invocation.Expression);
        return invokedExpression is IdentifierNameSyntax ||
               invokedExpression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

    internal static bool TryCreateImplicitThisMemberTerm(ISymbol member, out SymbolicTerm term)
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
                SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(argumentSyntax.Expression, statement, semanticModel,
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
            if (SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(sizeExpression, statement, semanticModel, cancellationToken) ||
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
        SymbolicLoopStateTransfer.AddThrowGuardedExpressionStateFacts(
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
            !SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(elementAccess, statement, semanticModel, cancellationToken) &&
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
            SymbolicLoopStateTransfer.AnyConditionSymbolMutatedInStatement(expression, statement, semanticModel, cancellationToken))
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
        var conditionSymbols = SymbolicLoopStateTransfer.GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
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

    internal static bool IsLoopStatement(SyntaxNode node)
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

    internal static bool TryGetFiniteElementExpressions(
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

    internal static bool TryGetPriorAssignedFiniteElementExpressions(
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

    internal static bool StatementInvalidatesSymbolValue(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicLoopStateTransfer.StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken) ||
               SymbolicStateInvalidator.MayMutateThroughReference(statement, symbol, semanticModel, cancellationToken);
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
            foreach (var referencedSymbol in SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
                         elementExpression,
                         semanticModel,
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

    internal static bool IsSupportedForeachLengthReceiver(ExpressionSyntax expressionSyntax)
    {
        expressionSyntax = UnwrapExpression(expressionSyntax);
        return expressionSyntax is ArrayCreationExpressionSyntax or
            ImplicitArrayCreationExpressionSyntax or
            CollectionExpressionSyntax;
    }

    internal static bool IsSupportedForeachLengthReceiver(ITypeSymbol? type)
    {
        return type is IArrayTypeSymbol { Rank: 1 } ||
               type?.SpecialType == SpecialType.System_String;
    }

    internal static bool StatementDefinitelyExits(
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

    internal static bool ExpressionStatementDefinitelyExits(
        ExpressionStatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expression = UnwrapExpression(statement.Expression);
        return expression is InvocationExpressionSyntax invocation &&
               semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
               NullableFlowFacts.HasDoesNotReturn(invocationOperation.TargetMethod);
    }

    internal static StatementSyntax UnwrapSingleStatementBlock(StatementSyntax statement)
    {
        while (statement is BlockSyntax { Statements.Count: 1 } block) statement = block.Statements[0];

        return statement;
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }



}

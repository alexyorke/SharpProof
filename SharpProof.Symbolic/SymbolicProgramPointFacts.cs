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

internal static class SymbolicProgramPointFacts
{
    internal static SymbolicState CollectPriorAssignmentState(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null)
    {
        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(ref state, site, semanticModel, cancellationToken);
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

            SymbolicStatementStateTransfer.ApplyContainingBlockEntryStateFacts(
                ref state,
                containingBlock.Block,
                semanticModel,
                cancellationToken);

            foreach (var statement in containingBlock.Block.Statements)
            {
                if (ReferenceEquals(statement, containingBlock.ContainingStatement))
                {
                    SymbolicStatementStateTransfer.InvalidateStateForTryRegionEntry(
                        ref state,
                        site,
                        statement,
                        semanticModel,
                        cancellationToken);
                    if (includeCurrentStatementCompletionFacts &&
                        ReferenceEquals(site, statement) &&
                        SymbolicStatementStateTransfer.SupportsCurrentStatementCompletionFacts(statement))
                        SymbolicStatementStateTransfer.AddPriorStatementStateFacts(
                            ref state,
                            statement,
                            semanticModel,
                            cancellationToken);

                    break;
                }

                SymbolicStatementStateTransfer.AddPriorStatementStateFacts(
                    ref state,
                    statement,
                    semanticModel,
                    cancellationToken);
            }
        }

        if (site is BlockSyntax siteBlock)
        {
            SymbolicStatementStateTransfer.ApplyContainingBlockEntryStateFacts(
                ref state,
                siteBlock,
                semanticModel,
                cancellationToken);

            if (includeCurrentStatementCompletionFacts)
                SymbolicStatementStateTransfer.AddCompletedBlockStateFacts(
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
                SymbolicStatementStateTransfer.AddCatchBodyEntryStateFacts(
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
                    SymbolicStatementStateTransfer.AddUsingStatementDeclarationStateFacts(
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
                    SymbolicStatementStateTransfer.AddUsingStatementExpressionStateFacts(
                        ref state,
                        usingStatementSyntax.Expression,
                        usingStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                }
            }
            else if (SymbolicLoopStateTransfer.TryApplyLoopBodyEntryStateFacts(
                         ref state,
                         ancestor,
                         syntaxNode.SpanStart,
                         semanticModel,
                         cancellationToken))
            {
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

    internal static bool TryAddInlineAssignmentReachabilityState(
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
            !SymbolicOperatorLowerer.TryGetRelationOperator(
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
        var effectiveValueExpression = SymbolicAssignmentStateTransfer
            .GetThrowGuardedValue(valueExpression)
            .EffectiveValueExpression;
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

    internal static void AddReferenceNullCondition(
        ref SymbolicState state,
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? provenance = null)
    {
        if (NullableFlowFacts.IsDefinitelyNullReferenceValue(expression, semanticModel, cancellationToken))
        {
            if (!isNull)
                state = SymbolicOperationTransferKernel.Complete(state, expression.Span).State;

            return;
        }

        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(expression, semanticModel, cancellationToken))
        {
            if (isNull)
                state = SymbolicOperationTransferKernel.Complete(state, expression.Span).State;

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
            !SymbolicPatternLowerer.TryGetListPatternShape(
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
                if (!SymbolicPatternLowerer.TryCreateRecursivePatternPositionalTerm(
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
            !SymbolicOperatorLowerer.TryGetRelationalPatternOperator(
                relationalPattern.OperatorToken.Kind(),
                false,
                out var relationOperator))
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

    internal static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }



}

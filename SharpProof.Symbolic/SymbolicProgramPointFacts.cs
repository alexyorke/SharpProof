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
                    AddPatternBindingStateFacts(
                        ref state,
                        switchExpressionSyntax.GoverningExpression,
                        matchingArm.Pattern,
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
        var transition = SymbolicReachabilityLowerer.Apply(
            state,
            condition,
            mustBeTrue,
            semanticModel,
            cancellationToken);
        if (transition.IsExact)
            state = transition.State;
    }

    internal static bool TryAddInlineAssignmentReachabilityState(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!SymbolicReachabilityLowerer.TryApplyInlineAssignment(
                state,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                out var transition) ||
            !transition.IsExact)
            return false;

        state = transition.State;
        return true;
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

        AddPatternBindingStateFacts(
            ref state,
            governingExpression,
            patternLabel.Pattern,
            semanticModel,
            cancellationToken);
        AddSwitchBranchGuardStateFacts(
            ref state,
            patternLabel.WhenClause?.Condition,
            semanticModel,
            cancellationToken);
    }

    private static void AddPatternBindingStateFacts(
        ref SymbolicState state,
        ExpressionSyntax governingExpression,
        PatternSyntax pattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var term = SymbolicSemanticPipeline.LowerTerm(governingExpression, context);
        var typeInfo = semanticModel.GetTypeInfo(governingExpression, cancellationToken);
        if (term is not { IsExact: true, Value: { } matchedTerm })
            return;

        var condition = SymbolicSemanticPipeline.LowerPatternCondition(
            matchedTerm,
            typeInfo.ConvertedType ?? typeInfo.Type,
            pattern,
            pattern,
            context);
        if (condition is not { IsExact: true, Value: { } exactCondition })
            return;

        var transition = SymbolicOperationTransferKernel.Assume(
            state,
            exactCondition,
            assumeTrue: true,
            pattern.Span,
            "cfg-program-point.pattern-binding");
        if (transition.IsExact)
            state = transition.State;
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

    internal static bool StatementInvalidatesSymbolValue(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicLoopStateTransfer.StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken) ||
               SymbolicStateInvalidator.MayMutateThroughReference(statement, symbol, semanticModel, cancellationToken);
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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicBranchCompletionStateTransfer
{
    internal static void AddCompletedIfStatementStateFacts(
        ref SymbolicState state,
        IfStatementSyntax ifStatement,
        SymbolicState stateBeforeStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var trueBranchExits = SymbolicControlFlowFacts.StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken);
        var falseBranchStatement = ifStatement.Else?.Statement;
        var falseBranchExits = falseBranchStatement != null &&
                               SymbolicControlFlowFacts.StatementDefinitelyExits(falseBranchStatement, semanticModel, cancellationToken);

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

        AddCommonBranchStateFacts(ref state, new[] { trueBranchState, falseBranchState });

        if (SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                ifStatement.Condition,
                ifStatement.Statement,
                semanticModel,
                cancellationToken) ||
            (falseBranchStatement != null &&
             SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
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

        AddConditionalBranchStateFacts(
            ref state,
            new[]
            {
                new GuardedBranchState(trueCondition, trueBranchState),
                new GuardedBranchState(falseCondition, falseBranchState)
            },
            ifStatement,
            SymbolicAnalysisLimitKind.IfElseFactMerge,
            SymbolicAnalysisLimitContext.Limits.MaxMergedIfElseFacts,
            "program_point.if_else_state_fact_merge");
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
            SymbolicStatementStateTransfer.AddPriorStatementStateFacts(
                ref branchState,
                statement,
                semanticModel,
                cancellationToken);

        foreach (var hiddenSymbol in GetLocalsDeclaredInside(branchStatement, semanticModel, cancellationToken))
            branchState = SymbolicStateValueFacts.RemoveReferences(branchState, hiddenSymbol);

        return true;
    }

    private static void RemoveConditionFactsInvalidatedByStatement(
        ref SymbolicState state,
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in SymbolicLoopStateTransfer.GetConditionDependencySymbols(condition, semanticModel, cancellationToken))
            if (SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken))
                state = SymbolicStateValueFacts.RemoveReferences(state, symbol);
    }

    internal static bool TryCreateBranchSymbolicCondition(
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition branchCondition)
    {
        var lowering = SymbolicSemanticPipeline.LowerBranchCondition(
            condition,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } loweredCondition })
        {
            branchCondition = null!;
            return false;
        }

        branchCondition = loweredCondition;
        return true;
    }

    internal static IReadOnlyList<ISymbol> GetLocalsDeclaredInside(
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

    internal static void AddCompletedSwitchStatementStateFacts(
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

                SymbolicStatementStateTransfer.AddPriorStatementStateFacts(
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

        AddCommonBranchStateFacts(ref state, branches.Select(static branch => branch.State).ToArray());
        if (branches.All(static branch => !branch.ConditionSymbolsMutated))
            AddConditionalBranchStateFacts(
                ref state,
                branches.Select(static branch => new GuardedBranchState(branch.Condition, branch.State)).ToArray(),
                switchStatement,
                SymbolicAnalysisLimitKind.SwitchFactMerge,
                SymbolicAnalysisLimitContext.Limits.MaxMergedSwitchFacts,
                "program_point.switch_state_fact_merge");
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

    private static void AddCommonBranchStateFacts(
        ref SymbolicState state,
        IReadOnlyList<SymbolicState> branches)
    {
        var (factKeys, conditionKeys) = GetCommonBranchStateKeys(branches);
        foreach (var fact in branches[0].Facts)
            if (factKeys.Contains(SymbolicState.CreateProofFactKey(fact)))
                state = state.AddFact(fact);
        foreach (var condition in branches[0].PathConditions)
            if (conditionKeys.Contains(SymbolicState.CreateProofConditionKey(condition)))
                state = state.AddPathCondition(condition);
    }

    private static void AddConditionalBranchStateFacts(
        ref SymbolicState state,
        IReadOnlyList<GuardedBranchState> branches,
        SyntaxNode source,
        SymbolicAnalysisLimitKind limitKind,
        int limit,
        string provenance)
    {
        var (commonFactKeys, commonConditionKeys) =
            GetCommonBranchStateKeys(branches.Select(static branch => branch.State).ToArray());
        var addedCount = 0;
        foreach (var branch in branches)
        {
            foreach (var fact in branch.State.Facts)
                if (!commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact)) &&
                    !TryAddGuardedBranchFact(
                        ref state,
                        branch.Condition,
                        new SymbolicFactCondition(fact),
                        source,
                        limitKind,
                        limit,
                        provenance,
                        ref addedCount))
                    return;

            var branchConditionKey = SymbolicState.CreateProofConditionKey(branch.Condition);
            foreach (var condition in branch.State.PathConditions)
            {
                var conditionKey = SymbolicState.CreateProofConditionKey(condition);
                if (commonConditionKeys.Contains(conditionKey) ||
                    string.Equals(conditionKey, branchConditionKey, StringComparison.Ordinal))
                    continue;
                if (!TryAddGuardedBranchFact(
                        ref state,
                        branch.Condition,
                        condition,
                        source,
                        limitKind,
                        limit,
                        provenance,
                        ref addedCount))
                    return;
            }
        }
    }

    private static (HashSet<string> FactKeys, HashSet<string> ConditionKeys) GetCommonBranchStateKeys(
        IReadOnlyList<SymbolicState> branches)
    {
        var factKeys = new HashSet<string>(branches[0].Facts.Select(SymbolicState.CreateProofFactKey), StringComparer.Ordinal);
        var conditionKeys = new HashSet<string>(
            branches[0].PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        for (var index = 1; index < branches.Count; index++)
        {
            factKeys.IntersectWith(branches[index].Facts.Select(SymbolicState.CreateProofFactKey));
            conditionKeys.IntersectWith(branches[index].PathConditions.Select(SymbolicState.CreateProofConditionKey));
        }

        return (factKeys, conditionKeys);
    }

    private static bool TryAddGuardedBranchFact(
        ref SymbolicState state,
        SymbolicCondition branchCondition,
        SymbolicCondition branchFact,
        SyntaxNode source,
        SymbolicAnalysisLimitKind limitKind,
        int limit,
        string provenance,
        ref int addedCount)
    {
        if (addedCount >= limit)
        {
            SymbolicAnalysisLimitContext.Record(limitKind, limit, addedCount + 1, source, provenance);
            return false;
        }

        state = state.AddPathCondition(SymbolicStateMerger.CreateGuardedChoice(branchCondition, branchFact));
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

            if (symbols.Any(symbol => SymbolicLoopStateTransfer.StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken)))
                return true;
        }

        return false;
    }

    internal static IReadOnlyList<ISymbol> GetSwitchConditionSymbols(
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

    internal static IReadOnlyList<ISymbol> GetSwitchExpressionConditionSymbols(
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

    internal static void AddReferencedSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<ISymbol> symbols)
    {
        foreach (var symbol in SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
                     root,
                     semanticModel,
                     cancellationToken))
            AddSymbolIfAbsent(symbols, symbol);
    }

    internal static void AddDeclaredPatternSymbols(
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

    internal static void AddMemberNotNullWhenTargetSymbols(
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
                !SymbolicNormalCompletionStateTransfer.IsCurrentInstanceInvocation(invocation))
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
        statement = SymbolicControlFlowFacts.UnwrapSingleStatementBlock(statement);
        return statement switch
        {
            ReturnStatementSyntax => true,
            ThrowStatementSyntax => true,
            BreakStatementSyntax breakStatement => !BreakTargetsSwitch(breakStatement, switchStatement),
            ContinueStatementSyntax => true,
            ExpressionStatementSyntax expressionStatement => SymbolicControlFlowFacts.ExpressionStatementDefinitelyExits(
                expressionStatement,
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
                SymbolicControlFlowCompletionStateTransfer.IsLoopStatement(ancestor))
                return false;
        }

        return false;
    }

    private sealed record GuardedBranchState(SymbolicCondition Condition, SymbolicState State);

    private sealed record SwitchBranchState(
        SymbolicCondition Condition,
        SymbolicState State,
        bool ConditionSymbolsMutated);
}

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static bool ShouldAnalyzeExplicitConditionBranchValue(SyntaxNode branchValueSyntax)
    {
        foreach (var ancestor in branchValueSyntax.AncestorsAndSelf())
            if (ancestor is IfStatementSyntax ||
                ancestor is ConditionalExpressionSyntax ||
                ancestor is WhileStatementSyntax ||
                ancestor is DoStatementSyntax ||
                ancestor is ForStatementSyntax ||
                ancestor is WhenClauseSyntax)
                return true;

        return false;
    }

    private static bool ShouldAnalyzeStateSensitiveBranchValue(SyntaxNode branchValueSyntax)
    {
        return ShouldAnalyzeExplicitConditionBranchValue(branchValueSyntax) ||
               IsReturnExpressionBranchValue(branchValueSyntax);
    }

    private static bool IsReturnExpressionBranchValue(SyntaxNode branchValueSyntax)
    {
        foreach (var ancestor in branchValueSyntax.AncestorsAndSelf())
            if (ancestor is ReturnStatementSyntax)
                return true;

        return false;
    }

    private static bool TryGetConstantBranchDecision(
        IOperation? branchValue,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        out bool takeConditionalSuccessor)
    {
        takeConditionalSuccessor = false;

        if (branchValue?.ConstantValue.HasValue == true &&
            branchValue.ConstantValue.Value is bool constantBool)
        {
            takeConditionalSuccessor = constantBool;
            return true;
        }

        if (branchValue?.Syntax is ExpressionSyntax expressionSyntax)
        {
            if (ExecutionVisibility.IsConditionAlwaysTrueUsingSmt(expressionSyntax, semanticModel, cancellationToken,
                    smtAnalysis))
            {
                takeConditionalSuccessor = true;
                return true;
            }

            if (ExecutionVisibility.IsConditionAlwaysFalseUsingSmt(expressionSyntax, semanticModel, cancellationToken,
                    smtAnalysis))
            {
                takeConditionalSuccessor = false;
                return true;
            }
        }

        return false;
    }

    private static bool BranchTrueUsesConditionalSuccessor(IOperation? branchValue)
    {
        if (branchValue?.Syntax is not ExpressionSyntax expressionSyntax) return false;

        return !TryFindContainingCondition(expressionSyntax, out var conditionSyntax) ||
               HasOddLogicalNotAncestor(expressionSyntax, conditionSyntax);
    }

    private static bool TryFindContainingCondition(ExpressionSyntax branchValueSyntax,
        out ExpressionSyntax conditionSyntax)
    {
        foreach (var ancestor in branchValueSyntax.AncestorsAndSelf())
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                conditionSyntax = ifStatement.Condition;
                return true;
            }

            if (ancestor is ConditionalExpressionSyntax conditionalExpression)
            {
                conditionSyntax = conditionalExpression.Condition;
                return true;
            }

            if (ancestor is WhileStatementSyntax whileStatement)
            {
                conditionSyntax = whileStatement.Condition;
                return true;
            }

            if (ancestor is DoStatementSyntax doStatement)
            {
                conditionSyntax = doStatement.Condition;
                return true;
            }

            if (ancestor is ForStatementSyntax forStatement)
            {
                if (forStatement.Condition != null)
                {
                    conditionSyntax = forStatement.Condition;
                    return true;
                }

                break;
            }

            if (ancestor is WhenClauseSyntax whenClause)
            {
                conditionSyntax = whenClause.Condition;
                return true;
            }
        }

        conditionSyntax = null!;
        return false;
    }

    private static bool HasOddLogicalNotAncestor(ExpressionSyntax branchValueSyntax, ExpressionSyntax conditionSyntax)
    {
        var logicalNotCount = 0;
        for (SyntaxNode? current = branchValueSyntax;
             current != null && !ReferenceEquals(current, conditionSyntax);
             current = current.Parent)
            if (current.Parent is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
                logicalNotCount++;

        return logicalNotCount % 2 == 1;
    }

    private static bool TryCreateSuccessorState(
        PurityAnalysisState currentState,
        IOperation? branchValue,
        SemanticModel semanticModel,
        bool takeConditionalSuccessor,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        out PurityAnalysisState successorState)
    {
        successorState = currentState;

        if (branchValue?.Syntax is not ExpressionSyntax expressionSyntax) return true;

        var nextPathConditionsBuilder = currentState.PathConditions.ToBuilder();
        var nextPathState = currentState.PathState;
        var addedSymbolicBranchAssumption = SymbolicReachabilityService.TryCollectBranchState(
            currentState.PathState,
            expressionSyntax,
            takeConditionalSuccessor,
            semanticModel,
            cancellationToken,
            out var symbolicBranchState,
            currentState.GetSmtSymbolVersion);
        if (addedSymbolicBranchAssumption) nextPathState = symbolicBranchState;

        var addedBranchAssumptions = SymbolicReachabilityService.TryAddBranchConditionFacts(
            expressionSyntax,
            takeConditionalSuccessor,
            semanticModel,
            cancellationToken,
            nextPathConditionsBuilder,
            currentState.GetSmtSymbolVersion,
            addTranslatedFormulaFallback: true);

        SmtFormula branchFormula;
        if (TryTranslateBranchValueToFormula(branchValue, currentState, out var operationFormula) &&
            operationFormula != null)
        {
            branchFormula = operationFormula;
        }
        else if (TryEncodeSymbolicBranchFormula(
                     currentState.PathState,
                     symbolicBranchState,
                     addedSymbolicBranchAssumption,
                     out var symbolicBranchFormula) &&
                 symbolicBranchFormula != null)
        {
            branchFormula = symbolicBranchFormula;
        }
        else
        {
            return TryFinalizeUntranslatedSuccessorState(
                currentState,
                nextPathConditionsBuilder.ToImmutable(),
                nextPathState,
                addedBranchAssumptions,
                addedSymbolicBranchAssumption,
                smtAnalysis,
                expressionSyntax,
                out successorState);
        }

        var edgeFormula = takeConditionalSuccessor
            ? branchFormula
            : SmtFormulaFactory.CreateNot(branchFormula);
        if (!addedBranchAssumptions)
        {
            nextPathConditionsBuilder.Add(edgeFormula);
            nextPathState = AddSymbolicConditionToState(
                nextPathState,
                edgeFormula,
                expressionSyntax,
                "analyzer.branch.edge",
                "analyzer.branch.edge");
        }

        var nextPathConditions = nextPathConditionsBuilder.ToImmutable();
        if (ArePathConditionsUnsatisfiable(currentState, nextPathConditions, nextPathState, smtAnalysis,
                expressionSyntax)) return false;

        successorState = currentState.WithPathConditionsAndState(nextPathConditions, nextPathState);
        return true;
    }

    internal static bool TryFinalizeUntranslatedSuccessorState(
        PurityAnalysisState currentState,
        ImmutableArray<SmtFormula> nextPathConditions,
        SymbolicState nextPathState,
        bool addedBranchAssumptions,
        bool addedSymbolicBranchAssumption,
        SmtAnalysisService smtAnalysis,
        SyntaxNode? sourceNode,
        out PurityAnalysisState successorState)
    {
        successorState = currentState;
        if (!addedBranchAssumptions && !addedSymbolicBranchAssumption) return true;

        if (ArePathConditionsUnsatisfiable(
                currentState,
                nextPathConditions,
                nextPathState,
                smtAnalysis,
                sourceNode))
            return false;

        successorState = currentState.WithPathConditionsAndState(nextPathConditions, nextPathState);
        return true;
    }

    private static bool TryEncodeSymbolicBranchFormula(
        SymbolicState originalState,
        SymbolicState branchState,
        bool hasBranchAssumption,
        out SmtFormula? formula)
    {
        formula = null;
        if (!hasBranchAssumption ||
            branchState.PathConditions.Length <= originalState.PathConditions.Length)
            return false;

        var branchCondition = branchState.PathConditions[branchState.PathConditions.Length - 1];
        return SymbolicProofService.TryEncodeConditionWithPathState(
            branchCondition,
            originalState,
            out formula);
    }

    internal static bool TryCreateBranchAssumptionState(
        PurityAnalysisState currentState,
        IOperation? condition,
        SemanticModel semanticModel,
        bool branchWhenTrue,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        out PurityAnalysisState branchState)
    {
        return TryCreateSuccessorState(
            currentState,
            condition,
            semanticModel,
            branchWhenTrue,
            smtAnalysis,
            cancellationToken,
            out branchState);
    }

    internal static bool TryGetKnownConditionValueFromPathFacts(
        PurityAnalysisState currentState,
        IOperation? condition,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken,
        out bool value)
    {
        value = false;

        if (condition?.ConstantValue.HasValue == true &&
            condition.ConstantValue.Value is bool constantBool)
        {
            value = constantBool;
            return true;
        }

        condition = SkipImplicitConversions(condition);
        if (condition?.Syntax is not ExpressionSyntax expressionSyntax) return false;

        if (IsBranchAssumptionUnsatisfiable(currentState, expressionSyntax, true, semanticModel, smtAnalysis,
                cancellationToken))
        {
            value = false;
            return true;
        }

        if (IsBranchAssumptionUnsatisfiable(currentState, expressionSyntax, false, semanticModel, smtAnalysis,
                cancellationToken))
        {
            value = true;
            return true;
        }

        return false;
    }

    internal static bool TryCreateReferenceNullAssumptionState(
        PurityAnalysisState currentState,
        IOperation? value,
        bool isNull,
        SmtAnalysisService smtAnalysis,
        out PurityAnalysisState branchState)
    {
        branchState = currentState;

        value = SkipImplicitConversions(value);
        if (value?.ConstantValue.HasValue == true) return value.ConstantValue.Value == null == isNull;

        if (!TryCreateReferenceVariableFormula(value, currentState, out var valueFormula)) return true;

        var nullComparison = SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, isNull);
        var nextPathConditions = currentState.PathConditions.Add(nullComparison);
        var nextPathState = TryCreateReferenceNullPathState(
            currentState,
            value,
            valueFormula,
            isNull,
            out var symbolicNullState)
            ? symbolicNullState
            : currentState.PathState;
        if (ArePathConditionsUnsatisfiable(currentState, nextPathConditions, nextPathState, smtAnalysis, value?.Syntax))
            return false;

        branchState = currentState.WithPathConditionsAndState(nextPathConditions, nextPathState);
        return true;
    }

    private static bool TryCreateReferenceNullPathState(
        PurityAnalysisState currentState,
        IOperation? value,
        SmtFormula valueFormula,
        bool isNull,
        out SymbolicState pathState)
    {
        pathState = currentState.PathState;
        value = SkipImplicitConversions(value);
        if (valueFormula is not SmtVariable variable ||
            value?.Syntax is not ExpressionSyntax syntax)
            return false;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                new SymbolicVariableTerm(variable.Name, SmtValueKind.Reference),
                new SymbolicNullTerm()),
            syntax,
            "analyzer.null_assumption",
            evidenceKey: isNull ? "analyzer.path.null" : "analyzer.path.not_null");
        pathState = currentState.PathState.AddPathCondition(new SymbolicFactCondition(fact));
        return true;
    }

    internal static bool TryGetKnownReferenceNullValueFromPathFacts(
        PurityAnalysisState currentState,
        IOperation? value,
        SmtAnalysisService smtAnalysis,
        out bool isNull)
    {
        isNull = false;

        value = SkipImplicitConversions(value);
        if (value?.ConstantValue.HasValue == true)
        {
            isNull = value.ConstantValue.Value == null;
            return true;
        }

        if (!TryCreateReferenceVariableFormula(value, currentState, out var valueFormula)) return false;

        var nullPathConditions = currentState.PathConditions.Add(
            SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, true));
        var nullPathState = TryCreateReferenceNullPathState(
            currentState,
            value,
            valueFormula,
            true,
            out var symbolicNullProbeState)
            ? symbolicNullProbeState
            : currentState.PathState;
        if (ArePathConditionsUnsatisfiable(currentState, nullPathConditions, nullPathState, smtAnalysis, value?.Syntax))
        {
            isNull = false;
            return true;
        }

        var nonNullPathConditions = currentState.PathConditions.Add(
            SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, false));
        var nonNullPathState = TryCreateReferenceNullPathState(
            currentState,
            value,
            valueFormula,
            false,
            out var symbolicNonNullProbeState)
            ? symbolicNonNullProbeState
            : currentState.PathState;
        if (ArePathConditionsUnsatisfiable(currentState, nonNullPathConditions, nonNullPathState, smtAnalysis,
                value?.Syntax))
        {
            isNull = true;
            return true;
        }

        return false;
    }

    private static bool IsBranchAssumptionUnsatisfiable(
        PurityAnalysisState currentState,
        ExpressionSyntax expressionSyntax,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken)
    {
        var pathState = currentState.PathState;
        if (SymbolicReachabilityService.TryCollectBranchState(
                currentState.PathState,
                expressionSyntax,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                out var branchPathState,
                currentState.GetSmtSymbolVersion))
            pathState = branchPathState;

        var pathConditionsBuilder = currentState.PathConditions.ToBuilder();
        var addedBranchAssumptions = SymbolicReachabilityService.TryAddBranchConditionFacts(
            expressionSyntax,
            branchWhenTrue,
            semanticModel,
            cancellationToken,
            pathConditionsBuilder,
            currentState.GetSmtSymbolVersion,
            true,
            addTranslatedFormulaAlways: true);

        return addedBranchAssumptions &&
               ArePathConditionsUnsatisfiable(currentState, pathConditionsBuilder.ToImmutable(), pathState, smtAnalysis,
                   expressionSyntax);
    }

    private static bool ArePathConditionsUnsatisfiable(
        PurityAnalysisState currentState,
        ImmutableArray<SmtFormula> pathConditions,
        SmtAnalysisService smtAnalysis,
        SyntaxNode? sourceNode = null)
    {
        return ArePathConditionsUnsatisfiable(currentState, pathConditions, currentState.PathState, smtAnalysis,
            sourceNode);
    }

    private static bool ArePathConditionsUnsatisfiable(
        PurityAnalysisState currentState,
        ImmutableArray<SmtFormula> pathConditions,
        SymbolicState pathState,
        SmtAnalysisService smtAnalysis,
        SyntaxNode? sourceNode = null)
    {
        if (!pathState.PathConditions.IsDefaultOrEmpty || !pathState.Facts.IsDefaultOrEmpty)
        {
            var proof = SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis);
            if (proof.Info.Status == SymbolicProofStatus.Unreachable) return true;
        }

        var proofPathConditions = AppendDefinitelyNullFacts(currentState, pathConditions);
        return SymbolicReachabilityService.PathConditionsAreUnsatisfiableWithOptionalIrFirst(
            proofPathConditions,
            sourceNode,
            smtAnalysis,
            "analyzer.path.condition",
            "analyzer-path-condition");
    }

    private static bool TryTranslateBranchValueToFormula(
        IOperation? branchValue,
        PurityAnalysisState currentState,
        out SmtFormula? formula)
    {
        formula = null;
        branchValue = SkipImplicitConversions(branchValue);

        if (branchValue is IIsNullOperation isNullOperation &&
            TryCreateReferenceVariableFormula(isNullOperation.Operand, currentState, out var operandFormula))
        {
            formula = SmtFormulaFactory.CreateReferenceNullComparison(operandFormula, true);
            return true;
        }

        return false;
    }

    private static bool TryCreateReferenceVariableFormula(
        IOperation? operation,
        PurityAnalysisState currentState,
        out SmtFormula formula)
    {
        operation = SkipImplicitConversions(operation);

        while (operation is IParenthesizedOperation parenthesizedOperation)
            operation = SkipImplicitConversions(parenthesizedOperation.Operand);

        if (TryResolveTrackedSymbol(operation, currentState) is ILocalSymbol localSymbol &&
            localSymbol.Type?.IsReferenceType == true)
        {
            formula = SmtFormulaFactory.CreateReferenceVariable(GetSmtVariableName(localSymbol,
                currentState.GetSmtSymbolVersion));
            return true;
        }

        if (TryResolveTrackedSymbol(operation, currentState) is IParameterSymbol parameterSymbol &&
            parameterSymbol.Type?.IsReferenceType == true)
        {
            formula = SmtFormulaFactory.CreateReferenceVariable(GetSmtVariableName(parameterSymbol,
                currentState.GetSmtSymbolVersion));
            return true;
        }

        formula = null!;
        return false;
    }

    private static ImmutableArray<SmtFormula> AppendDefinitelyNullFacts(
        PurityAnalysisState currentState,
        ImmutableArray<SmtFormula> pathConditions)
    {
        if (currentState.DefinitelyNullLocalSymbols.Count == 0) return pathConditions;

        var builder =
            ImmutableArray.CreateBuilder<SmtFormula>(pathConditions.Length +
                                                     currentState.DefinitelyNullLocalSymbols.Count);
        builder.AddRange(pathConditions);

        foreach (var localSymbol in currentState.DefinitelyNullLocalSymbols.OfType<ILocalSymbol>())
        {
            if (localSymbol.Type?.IsReferenceType != true) continue;

            builder.Add(SmtFormulaFactory.CreateReferenceNullComparison(
                SmtFormulaFactory.CreateReferenceVariable(GetSmtVariableName(localSymbol,
                    currentState.GetSmtSymbolVersion)),
                true));
        }

        return builder.ToImmutable();
    }

    private static string GetSmtVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion = null)
    {
        var name = SymbolicFactFactory.GetSmtVariableName(symbol);
        var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
        return version > 0
            ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
            : name;
    }
}

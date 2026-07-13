using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
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
            if (ancestor is ReturnStatementSyntax or ArrowExpressionClauseSyntax)
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

    private static bool BranchTrueUsesConditionalSuccessor(BasicBlock block)
    {
        return block.ConditionKind == ControlFlowConditionKind.WhenTrue;
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

        if (branchValue is IIsNullOperation isNullOperation)
            return TryCreateReferenceNullAssumptionState(
                currentState,
                isNullOperation.Operand,
                takeConditionalSuccessor,
                smtAnalysis,
                out successorState);

        if (branchValue?.Syntax is not ExpressionSyntax expressionSyntax) return true;

        var branchLowering = SymbolicReachabilityService.ApplyBranchFacts(
            currentState.PathState,
            expressionSyntax,
            takeConditionalSuccessor,
            semanticModel,
            cancellationToken,
            currentState.GetSmtSymbolVersion);
        if (branchLowering is not { IsExact: true, Value: { } symbolicBranchState })
            return true;

        return TryFinalizeSymbolicSuccessorState(
            currentState,
            symbolicBranchState,
            smtAnalysis,
            expressionSyntax,
            out successorState);
    }

    internal static bool TryFinalizeSymbolicSuccessorState(
        PurityAnalysisState currentState,
        SymbolicState nextPathState,
        SmtAnalysisService smtAnalysis,
        SyntaxNode? sourceNode,
        out PurityAnalysisState successorState)
    {
        successorState = currentState;
        if (IsPathStateUnsatisfiable(currentState, nextPathState, smtAnalysis, sourceNode))
            return false;

        successorState = currentState.WithPathState(nextPathState);
        return true;
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

        if (isNull &&
            TryResolveTrackedSymbol(value, currentState) is { } ownedSymbol &&
            (HasSymbolicOwnedFactForSymbol(ownedSymbol, currentState) ||
             currentState.PathState.Facts.Any(fact =>
                 SymbolEqualityComparer.Default.Equals(fact.Symbol, ownedSymbol) &&
                 fact.Atom is SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } or
                     SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed })))
            return false;

        if (!TryCreateReferenceTerm(value, currentState, out var valueTerm)) return true;

        var nextPathState = TryCreateReferenceNullPathState(
            currentState,
            value,
            valueTerm,
            isNull,
            out var symbolicNullState)
            ? symbolicNullState
            : currentState.PathState;
        if (IsPathStateUnsatisfiable(currentState, nextPathState, smtAnalysis, value?.Syntax))
            return false;

        branchState = currentState.WithPathState(nextPathState);
        return true;
    }

    private static bool TryCreateReferenceNullPathState(
        PurityAnalysisState currentState,
        IOperation? value,
        SymbolicTerm valueTerm,
        bool isNull,
        out SymbolicState pathState)
    {
        pathState = currentState.PathState;
        value = SkipImplicitConversions(value);
        if (value?.Syntax is not ExpressionSyntax syntax ||
            valueTerm.Kind != SharpProof.ProofCore.Smt.SmtValueKind.Reference)
            return false;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                valueTerm,
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

        if (!TryCreateReferenceTerm(value, currentState, out var valueTerm)) return false;

        var nullPathState = TryCreateReferenceNullPathState(
            currentState,
            value,
            valueTerm,
            true,
            out var symbolicNullProbeState)
            ? symbolicNullProbeState
            : currentState.PathState;
        if (IsPathStateUnsatisfiable(currentState, nullPathState, smtAnalysis, value?.Syntax))
        {
            isNull = false;
            return true;
        }

        var nonNullPathState = TryCreateReferenceNullPathState(
            currentState,
            value,
            valueTerm,
            false,
            out var symbolicNonNullProbeState)
            ? symbolicNonNullProbeState
            : currentState.PathState;
        if (IsPathStateUnsatisfiable(currentState, nonNullPathState, smtAnalysis, value?.Syntax))
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
        var branchLowering = SymbolicReachabilityService.ApplyBranchFacts(
                currentState.PathState,
                expressionSyntax,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                currentState.GetSmtSymbolVersion);
        if (branchLowering is not { IsExact: true, Value: { } branchPathState })
            return false;

        return SymbolicReachabilityService.ClassifyStateFeasibility(branchPathState, smtAnalysis).Info.Status ==
               SymbolicProofStatus.Unreachable;
    }

    private static bool IsPathStateUnsatisfiable(
        PurityAnalysisState currentState,
        SymbolicState pathState,
        SmtAnalysisService smtAnalysis,
        SyntaxNode? sourceNode = null)
    {
        var proofState = AppendDefinitelyNullFacts(currentState, pathState, sourceNode);
        return SymbolicReachabilityService.ClassifyStateFeasibility(proofState, smtAnalysis).Info.Status ==
               SymbolicProofStatus.Unreachable;
    }

    private static bool TryCreateReferenceTerm(
        IOperation? operation,
        PurityAnalysisState currentState,
        out SymbolicTerm term)
    {
        operation = SkipImplicitConversions(operation);

        while (operation is IParenthesizedOperation parenthesizedOperation)
            operation = SkipImplicitConversions(parenthesizedOperation.Operand);

        if (TryResolveTrackedSymbol(operation, currentState) is { } symbol &&
            SymbolicFactFactory.GetTrackedSymbolType(symbol)?.IsReferenceType == true)
        {
            term = CreateSymbolicReferenceTerm(symbol, currentState);
            return true;
        }

        term = null!;
        return false;
    }

    private static SymbolicState AppendDefinitelyNullFacts(
        PurityAnalysisState currentState,
        SymbolicState pathState,
        SyntaxNode? sourceNode)
    {
        if (currentState.DefinitelyNullLocalSymbols.Count == 0) return pathState;

        var source = sourceNode ?? SyntaxFactory.IdentifierName("__symbolic_null_fact");

        foreach (var localSymbol in currentState.DefinitelyNullLocalSymbols.OfType<ILocalSymbol>())
        {
            if (localSymbol.Type?.IsReferenceType != true) continue;

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    CreateSymbolicReferenceTerm(localSymbol, currentState),
                    new SymbolicNullTerm()),
                source,
                "analyzer.definitely-null",
                evidenceKey: "analyzer.definitely-null");
            pathState = pathState.AddPathCondition(new SymbolicFactCondition(fact));
        }

        return pathState;
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

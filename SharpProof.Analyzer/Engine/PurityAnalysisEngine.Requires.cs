using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisState CreateInitialRequiresState(
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        var state = PurityAnalysisState.Pure;
        var contracts = RequiresContractHelpers.ValidContracts(methodSymbol, attributePolicy, cancellationToken);
        if (contracts.Length == 0) return state;

        var position = RequiresContractHelpers.GetMethodEntrySpeculativePosition(methodNode);
        foreach (var contract in contracts)
        {
            if (!RequiresContractHelpers.TryCreateConditionFormula(
                    semanticModel,
                    position,
                    contract.Condition,
                    cancellationToken,
                    out var conditionExpression,
                    out _,
                    out var formula,
                    out _) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression))
                continue;

            state = state.WithPathConditions(state.PathConditions.Add(formula));
            var lowering = SymbolicSemanticPipeline.LowerCondition(
                conditionExpression,
                new SymbolicLoweringContext(semanticModel, cancellationToken));
            if (lowering is { IsExact: true, Value: { } condition })
                state = state.WithPathConditionsAndState(
                    state.PathConditions,
                    state.PathState.AddPathCondition(condition));
        }

        return state;
    }
}

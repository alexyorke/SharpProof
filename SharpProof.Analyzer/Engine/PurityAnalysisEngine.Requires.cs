using Microsoft.CodeAnalysis;

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
            state = AddSymbolicConditionFromFormula(
                state,
                formula,
                conditionExpression,
                "requires.contract",
                "requires:" + contract.Condition);
        }

        return state;
    }
}
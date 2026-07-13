using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;

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
        var pathState = RequiresEntryStateBuilder.Create(
            methodSymbol,
            methodNode,
            semanticModel,
            attributePolicy,
            cancellationToken);
        return PurityAnalysisState.Pure.WithPathState(pathState);
    }
}

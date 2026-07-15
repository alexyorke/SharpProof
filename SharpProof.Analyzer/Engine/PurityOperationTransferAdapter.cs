using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;
using PurityAnalysisState = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisState;

namespace SharpProof.Analyzer.Engine;

internal static class PurityOperationTransferAdapter
{
    internal static PurityAnalysisState Apply(
        PurityAnalysisState state,
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        PurityAnalysisState valueState,
        out SymbolicOperationTransitionResult transition)
    {
        transition = SymbolicOperationTransferAdapter.Apply(
            state.PathState,
            operation,
            semanticModel,
            cancellationToken,
            state.GetSmtSymbolVersion,
            valueState.GetSmtSymbolVersion);
        return transition.IsUnsupported
            ? state
            : state.WithPathState(transition.State);
    }

    internal static PurityAnalysisState ApplyAssignment(
        PurityAnalysisState state,
        ISymbol targetSymbol,
        IOperation valueOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        PurityAnalysisState valueState,
        out SymbolicOperationTransitionResult transition)
    {
        transition = SymbolicOperationTransferAdapter.ApplyAssignment(
            state.PathState,
            targetSymbol,
            valueOperation,
            valueOperation.Syntax,
            semanticModel,
            cancellationToken,
            state.GetSmtSymbolVersion,
            valueState.GetSmtSymbolVersion,
            provenance: "analyzer.assignment");
        return transition.IsUnsupported
            ? state
            : state.WithPathState(transition.State);
    }
}

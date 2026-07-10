using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class DynamicOperationPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
        OperationKind.DynamicInvocation,
        OperationKind.DynamicMemberReference,
        OperationKind.DynamicObjectCreation,
        OperationKind.DynamicIndexerAccess);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            operation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "dynamic_dispatch",
                nameof(DynamicOperationPurityRule),
                operation));
    }
}
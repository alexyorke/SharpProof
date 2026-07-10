using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class ImplicitIndexerReferencePurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds =>
        ImmutableArray.Create(OperationKind.ImplicitIndexerReference);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
            implicitIndexerReferenceOperation.Instance,
            context,
            currentState);
        if (!instanceResult.IsPure) return instanceResult;

        var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
            implicitIndexerReferenceOperation.Argument,
            context,
            currentState);
        if (!argumentResult.IsPure) return argumentResult;

        if (IsPartOfAssignmentTarget(implicitIndexerReferenceOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var receiverType = DispatchedMemberResolution.GetKnownReceiverType(
            implicitIndexerReferenceOperation.Instance,
            currentState,
            context.SemanticModel.Compilation,
            out var hasStableConcreteReceiver);

        if (implicitIndexerReferenceOperation.LengthSymbol is IPropertySymbol lengthProperty)
        {
            var lengthResult = DispatchedMemberResolution.CheckGetterPurity(
                lengthProperty,
                receiverType,
                hasStableConcreteReceiver,
                implicitIndexerReferenceOperation,
                context,
                nameof(ImplicitIndexerReferencePurityRule));
            if (!lengthResult.IsPure) return lengthResult;
        }

        return CheckIndexerSymbolPurity(
            implicitIndexerReferenceOperation.IndexerSymbol,
            receiverType,
            hasStableConcreteReceiver,
            implicitIndexerReferenceOperation,
            context);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckIndexerSymbolPurity(
        ISymbol? indexerSymbol,
        INamedTypeSymbol? receiverType,
        bool hasStableConcreteReceiver,
        IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation,
        PurityAnalysisContext context)
    {
        return indexerSymbol switch
        {
            IPropertySymbol propertySymbol => DispatchedMemberResolution.CheckGetterPurity(
                propertySymbol,
                receiverType,
                hasStableConcreteReceiver,
                implicitIndexerReferenceOperation,
                context,
                nameof(ImplicitIndexerReferencePurityRule)),
            IMethodSymbol methodSymbol => DispatchedMemberResolution.CheckMethodPurity(
                methodSymbol,
                receiverType,
                hasStableConcreteReceiver,
                implicitIndexerReferenceOperation,
                context,
                nameof(ImplicitIndexerReferencePurityRule)),
            _ => PurityAnalysisEngine.PurityAnalysisResult.Impure(
                implicitIndexerReferenceOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unsupported_operation",
                    nameof(ImplicitIndexerReferencePurityRule),
                    implicitIndexerReferenceOperation,
                    symbol: indexerSymbol))
        };
    }

    private static bool IsPartOfAssignmentTarget(IOperation operation)
    {
        return operation.Parent is IAssignmentOperation assignment && assignment.Target == operation;
    }
}
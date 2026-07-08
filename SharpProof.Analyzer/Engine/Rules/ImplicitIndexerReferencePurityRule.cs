using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class ImplicitIndexerReferencePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ImplicitIndexerReference);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                implicitIndexerReferenceOperation.Instance,
                context,
                currentState);
            if (!instanceResult.IsPure)
            {
                return instanceResult;
            }

            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
                implicitIndexerReferenceOperation.Argument,
                context,
                currentState);
            if (!argumentResult.IsPure)
            {
                return argumentResult;
            }

            if (IsPartOfAssignmentTarget(implicitIndexerReferenceOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var receiverType = DispatchedMemberResolution.GetKnownReceiverType(
                implicitIndexerReferenceOperation.Instance,
                currentState,
                context.SemanticModel.Compilation,
                out var hasStableConcreteReceiver);

            if (implicitIndexerReferenceOperation.LengthSymbol is IPropertySymbol lengthProperty)
            {
                var lengthResult = CheckPropertyGetterPurity(
                    lengthProperty,
                    receiverType,
                    hasStableConcreteReceiver,
                    implicitIndexerReferenceOperation,
                    context);
                if (!lengthResult.IsPure)
                {
                    return lengthResult;
                }
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
                IPropertySymbol propertySymbol => CheckPropertyGetterPurity(
                    propertySymbol,
                    receiverType,
                    hasStableConcreteReceiver,
                    implicitIndexerReferenceOperation,
                    context),
                IMethodSymbol methodSymbol => CheckMethodPurity(
                    methodSymbol,
                    receiverType,
                    hasStableConcreteReceiver,
                    implicitIndexerReferenceOperation,
                    context),
                _ => PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    implicitIndexerReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unsupported_operation",
                        ruleName: nameof(ImplicitIndexerReferencePurityRule),
                        operation: implicitIndexerReferenceOperation,
                        symbol: indexerSymbol))
            };
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckPropertyGetterPurity(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation,
            PurityAnalysisContext context)
        {
            var getter = DispatchedMemberResolution.ResolveGetter(
                propertySymbol,
                receiverType,
                hasStableConcreteReceiver,
                context.SemanticModel.Compilation);
            if (getter == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    implicitIndexerReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        ruleName: nameof(ImplicitIndexerReferencePurityRule),
                        operation: implicitIndexerReferenceOperation,
                        symbol: propertySymbol.GetMethod));
            }

            var getterPurity = PurityAnalysisEngine.GetCalleePurity(getter, context);
            return getterPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : getterPurity.WithCallee(getter, implicitIndexerReferenceOperation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckMethodPurity(
            IMethodSymbol methodSymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation,
            PurityAnalysisContext context)
        {
            var targetMethod = DispatchedMemberResolution.ResolveMethod(
                methodSymbol,
                receiverType,
                hasStableConcreteReceiver,
                context.SemanticModel.Compilation);
            if (targetMethod == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    implicitIndexerReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        ruleName: nameof(ImplicitIndexerReferencePurityRule),
                        operation: implicitIndexerReferenceOperation,
                        symbol: methodSymbol));
            }

            var methodPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
            return methodPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : methodPurity.WithCallee(targetMethod, implicitIndexerReferenceOperation.Syntax);
        }

        private static bool IsPartOfAssignmentTarget(IOperation operation)
        {
            return operation.Parent is IAssignmentOperation assignment && assignment.Target == operation;
        }
    }
}

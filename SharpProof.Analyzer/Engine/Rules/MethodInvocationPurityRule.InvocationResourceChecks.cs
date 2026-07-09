using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class MethodInvocationPurityRule
    {

        private static bool TryCheckDoubleDispose(
            IInvocationOperation invocationOperation,
            IMethodSymbol invokedMethodSymbol,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
            if (!PurityAnalysisEngine.TryCreateDoubleDisposeEvidence(
                    invocationOperation,
                    invokedMethodSymbol,
                    currentState,
                    context.SemanticModel,
                    context.CancellationToken,
                    nameof(MethodInvocationPurityRule),
                    out var evidence))
            {
                return false;
            }

            PurityAnalysisEngine.LogDebug("  [MIR] Dispose invoked on a resource already marked disposed by symbolic ownership facts.");
            result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                invocationOperation.Syntax,
                evidence);
            return true;
        }

        private static bool TryCheckUseAfterDispose(
            IInvocationOperation invocationOperation,
            IMethodSymbol invokedMethodSymbol,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
            if (PurityAnalysisEngine.IsParameterlessDisposeInvocation(invocationOperation) ||
                invokedMethodSymbol.IsStatic ||
                invocationOperation.Instance == null ||
                invokedMethodSymbol.ContainingType?.SpecialType == SpecialType.System_Object ||
                !PurityAnalysisEngine.TryCreateUseAfterDisposeEvidence(
                    invocationOperation,
                    invocationOperation.Instance,
                    invokedMethodSymbol,
                    currentState,
                    context.SemanticModel,
                    context.CancellationToken,
                    nameof(MethodInvocationPurityRule),
                    out var evidence))
            {
                return false;
            }

            PurityAnalysisEngine.LogDebug("  [MIR] Instance invocation uses a resource already marked disposed by symbolic ownership facts.");
            result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                invocationOperation.Syntax,
                evidence);
            return true;
        }

        private static bool TryCheckByRefArgumentBorrowConflict(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
            foreach (var argument in invocationOperation.Arguments)
            {
                if (!IsRefOrOutArgument(argument))
                {
                    continue;
                }

                if (!PurityAnalysisEngine.TryCreateMutableBorrowConflictEvidence(
                        argument,
                        PurityAnalysisEngine.TryResolveTrackedSymbol(argument.Value, currentState),
                        currentState,
                        context.SemanticModel,
                        context.CancellationToken,
                        nameof(MethodInvocationPurityRule),
                        out var borrowConflictEvidence))
                {
                    continue;
                }

                PurityAnalysisEngine.LogDebug($"  [MIR]   By-reference argument '{argument.Syntax}' mutates a symbol with an active mutable borrow.");
                result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    argument.Syntax,
                    borrowConflictEvidence);
                return true;
            }

            return false;
        }

        private static bool IsRefOrOutArgument(IArgumentOperation argument)
        {
            return argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref ||
                   argument.Syntax is ArgumentSyntax argumentSyntax &&
                   argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                   argument.Syntax is ArgumentSyntax outArgumentSyntax &&
                   outArgumentSyntax.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
        }

        private static INamedTypeSymbol? GetTrackedLocalReceiverType(
            IOperation? invocationInstance,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            Compilation compilation)
        {
            return PurityAnalysisEngine.TryResolveKnownConcreteType(invocationInstance, currentState, compilation, out var concreteType)
                ? concreteType
                : null;
        }

        private static INamedTypeSymbol? GetStableInitializerReceiverType(
            IOperation? invocationInstance,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var normalizedInstance = NormalizeReceiverOperation(invocationInstance);
            if (normalizedInstance is not IFieldReferenceOperation fieldReference ||
                !fieldReference.Field.IsReadOnly ||
                !FieldOrPropertyInitializerOperationHelper.TryGetFieldOrPropertyInitializerOperation(
                    fieldReference,
                    context,
                    out var initializerOperation))
            {
                return null;
            }

            if (PurityAnalysisEngine.TryResolveKnownConcreteType(initializerOperation, currentState, context.SemanticModel.Compilation, out var concreteType))
            {
                return concreteType;
            }

            return GetKnownReceiverType(initializerOperation);
        }
    }
}

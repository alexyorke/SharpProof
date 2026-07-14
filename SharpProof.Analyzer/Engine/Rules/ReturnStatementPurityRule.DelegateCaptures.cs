using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule : IPurityRule
{
    private static bool TryFindReturnedDelegateCapture(
        IOperation? returnedValue,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        ReturnedDelegateCaptureKind captureKind,
        out SyntaxNode captureSyntax,
        out ILocalSymbol capturedLocal)
    {
        var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
        var delegateTarget = unwrappedReturnedValue is IDelegateCreationOperation delegateCreation
            ? PurityAnalysisEngine.SkipImplicitConversions(delegateCreation.Target)
            : unwrappedReturnedValue;

        switch (delegateTarget)
        {
            case IAnonymousFunctionOperation anonymousFunction:
                return captureKind == ReturnedDelegateCaptureKind.FreshMutableObject
                    ? DelegateCreationPurityRule.TryFindCapturedFreshMutableObject(
                        anonymousFunction,
                        currentState,
                        delegateTarget.Syntax,
                        context.SemanticModel,
                        context.CancellationToken,
                        out captureSyntax,
                        out capturedLocal)
                    : DelegateCreationPurityRule.TryFindCapturedOwnedLocalArray(
                        anonymousFunction,
                        currentState,
                        context.SemanticModel,
                        context.CancellationToken,
                        out captureSyntax,
                        out capturedLocal);

            case IFlowAnonymousFunctionOperation flowAnonymousFunction:
                return captureKind == ReturnedDelegateCaptureKind.FreshMutableObject
                    ? DelegateCreationPurityRule.TryFindCapturedFreshMutableObject(
                        flowAnonymousFunction,
                        currentState,
                        delegateTarget.Syntax,
                        context.SemanticModel,
                        context.CancellationToken,
                        out captureSyntax,
                        out capturedLocal)
                    : DelegateCreationPurityRule.TryFindCapturedOwnedLocalArray(
                        flowAnonymousFunction,
                        currentState,
                        context.SemanticModel,
                        context.CancellationToken,
                        out captureSyntax,
                        out capturedLocal);

            case IMethodReferenceOperation methodReference
                when methodReference.Method.MethodKind == MethodKind.LocalFunction:
                return captureKind == ReturnedDelegateCaptureKind.FreshMutableObject
                    ? DelegateCreationPurityRule.TryFindLocalFunctionCapturedFreshMutableObject(
                        methodReference.Method,
                        currentState,
                        delegateTarget.Syntax,
                        context,
                        out captureSyntax,
                        out capturedLocal)
                    : DelegateCreationPurityRule.TryFindLocalFunctionCapturedOwnedLocalArray(
                        methodReference.Method,
                        context,
                        currentState,
                        out captureSyntax,
                        out capturedLocal);

            default:
                captureSyntax = null!;
                capturedLocal = null!;
                return false;
        }
    }

    private enum ReturnedDelegateCaptureKind
    {
        FreshMutableObject,
        OwnedLocalArray
    }
}

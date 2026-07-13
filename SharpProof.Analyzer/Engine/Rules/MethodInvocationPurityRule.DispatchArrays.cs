using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    internal static bool TryCheckArrayInterfaceGetEnumeratorPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        var hasOperationArrayReceiver = TryGetKnownArrayReceiverType(invocationOperation.Instance, out _);
        var hasSyntaxArrayReceiver = TryGetKnownArrayReceiverTypeFromSyntax(
            invocationOperation,
            context.SemanticModel,
            context.CancellationToken,
            out _);
        if (!IsGetEnumeratorMethodName(methodSymbol) ||
            methodSymbol.Parameters.Length != 0 ||
            (!IsEnumerableGetEnumeratorDispatchTarget(methodSymbol) && !hasSyntaxArrayReceiver) ||
            (!hasOperationArrayReceiver && !hasSyntaxArrayReceiver))
            return false;

        var arrayGetEnumerator = context.SemanticModel.Compilation
            .GetSpecialType(SpecialType.System_Array)
            .GetMembers("GetEnumerator")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => candidate.Parameters.Length == 0);
        if (arrayGetEnumerator == null) return false;

        var purity = PurityCalleeResolver.GetCalleePurity(arrayGetEnumerator.OriginalDefinition, context);
        result = purity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : purity.WithCallee(arrayGetEnumerator.OriginalDefinition, invocationOperation.Syntax);
        return true;
    }

    private static bool IsEnumerableGetEnumeratorDispatchTarget(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null) return false;

        if (containingType.SpecialType == SpecialType.System_Collections_IEnumerable) return true;

        return containingType is INamedTypeSymbol namedContainingType &&
               (namedContainingType.OriginalDefinition.SpecialType ==
                SpecialType.System_Collections_Generic_IEnumerable_T ||
                string.Equals(namedContainingType.OriginalDefinition.ToDisplayString(),
                    "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal));
    }

    private static bool IsGetEnumeratorMethodName(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Name == "GetEnumerator" ||
               methodSymbol.ToDisplayString().IndexOf(".GetEnumerator(", StringComparison.Ordinal) >= 0;
    }

    private static bool TryGetKnownArrayReceiverType(
        IOperation? invocationInstance,
        out IArrayTypeSymbol arrayType)
    {
        var current = invocationInstance;

        while (true)
        {
            current = NormalizeReceiverOperation(current);
            if (current == null)
            {
                arrayType = null!;
                return false;
            }

            if (current is IConditionalOperation conditional)
            {
                if (TryGetKnownArrayReceiverType(conditional.WhenTrue, out var whenTrueType) &&
                    TryGetKnownArrayReceiverType(conditional.WhenFalse, out var whenFalseType) &&
                    SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
                {
                    arrayType = whenTrueType;
                    return true;
                }

                arrayType = null!;
                return false;
            }

            if (TryUnwrapReceiverOperation(current, out var unwrapped))
            {
                current = unwrapped;
                continue;
            }

            if (current.Type is IArrayTypeSymbol resolvedArrayType)
            {
                arrayType = resolvedArrayType;
                return true;
            }

            arrayType = null!;
            return false;
        }
    }

    private static bool TryGetKnownArrayReceiverTypeFromSyntax(
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType)
    {
        arrayType = null!;
        var invocationSyntax = invocationOperation.Syntax as InvocationExpressionSyntax ??
                               invocationOperation.Syntax.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocationSyntax == null ||
            invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var receiverExpression = CSharpSyntaxFacts.UnwrapParentheses(memberAccess.Expression);
        if (receiverExpression is not CastExpressionSyntax castExpression) return false;

        var operandType = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).ConvertedType ??
                          semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).Type;
        if (operandType is not IArrayTypeSymbol resolvedArrayType) return false;

        arrayType = resolvedArrayType;
        return true;
    }

}

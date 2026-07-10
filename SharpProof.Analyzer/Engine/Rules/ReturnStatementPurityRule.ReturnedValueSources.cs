using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule : IPurityRule
{
    private static IOperation? GetSourceReturnedValueOperation(
        IReturnOperation returnOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionSyntax = returnOperation.Syntax switch
        {
            ReturnStatementSyntax returnStatementSyntax => returnStatementSyntax.Expression,
            ArrowExpressionClauseSyntax arrowExpressionClauseSyntax => arrowExpressionClauseSyntax.Expression,
            _ => null
        };

        return expressionSyntax == null
            ? returnOperation.ReturnedValue
            : semanticModel.GetOperation(expressionSyntax, cancellationToken) ?? returnOperation.ReturnedValue;
    }

    private static bool IsAwaiterFactoryReturn(
        IMethodSymbol containingMethodSymbol,
        IOperation? returnedValue,
        Compilation compilation)
    {
        if (containingMethodSymbol.Name != "GetAwaiter" ||
            containingMethodSymbol.Parameters.Length != 0)
            return false;

        var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
        if (unwrappedReturnedValue is not IObjectCreationOperation objectCreationOperation ||
            objectCreationOperation.Type is not INamedTypeSymbol awaiterType)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(containingMethodSymbol.ReturnType, awaiterType)) return false;

        return HasAwaiterPattern(awaiterType, compilation);
    }

    private static bool HasAwaiterPattern(INamedTypeSymbol awaiterType, Compilation compilation)
    {
        var hasIsCompleted = awaiterType.GetMembers("IsCompleted")
            .OfType<IPropertySymbol>()
            .Any(property => property.Type.SpecialType == SpecialType.System_Boolean);
        if (!hasIsCompleted) return false;

        var hasGetResult = awaiterType.GetMembers("GetResult")
            .OfType<IMethodSymbol>()
            .Any(method => method.Parameters.Length == 0);
        if (!hasGetResult) return false;

        var notifyCompletion = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.INotifyCompletion");
        var criticalNotifyCompletion =
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ICriticalNotifyCompletion");

        return (notifyCompletion != null &&
                awaiterType.AllInterfaces.Contains(notifyCompletion, SymbolEqualityComparer.Default)) ||
               (criticalNotifyCompletion != null &&
                awaiterType.AllInterfaces.Contains(criticalNotifyCompletion, SymbolEqualityComparer.Default));
    }

    private static bool IsKnownPureArrayFactoryReturn(
        IOperation? returnedValue,
        Compilation compilation,
        out IMethodSymbol factoryMethod)
    {
        return TryMatchReturnedValueAlternative(
            returnedValue,
            PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions,
            IsKnownPureArrayFactory,
            out factoryMethod);

        bool IsKnownPureArrayFactory(IOperation? operation, out IMethodSymbol methodSymbol)
        {
            return PurityAnalysisEngine.IsTrustedFreshArrayFactoryOperation(operation, compilation, out methodSymbol);
        }
    }

    private static bool IsSpanToArrayReturn(
        IOperation? returnedValue,
        out IMethodSymbol methodSymbol)
    {
        return TryMatchReturnedValueAlternative(
            returnedValue,
            PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions,
            IsSpanToArray,
            out methodSymbol);

        static bool IsSpanToArray(IOperation? operation, out IMethodSymbol methodSymbol)
        {
            if (operation is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol &&
                invocationOperation.TargetMethod?.OriginalDefinition is { } targetMethod &&
                targetMethod.Name == "ToArray" &&
                !targetMethod.IsStatic &&
                targetMethod.ContainingType?.OriginalDefinition.ToDisplayString() is "System.Span<T>"
                    or "System.ReadOnlySpan<T>")
            {
                methodSymbol = targetMethod;
                return true;
            }

            methodSymbol = null!;
            return false;
        }
    }

    private static bool TryMatchReturnedValueAlternative<TResult>(
        IOperation? returnedValue,
        Func<IOperation?, IOperation?> normalize,
        ReturnedValueMatcher<TResult> match,
        out TResult result)
    {
        var normalizedReturnedValue = normalize(returnedValue);
        if (match(normalizedReturnedValue, out result)) return true;

        if (normalizedReturnedValue is IConditionalOperation conditionalOperation)
        {
            if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                return TryMatchReturnedValueAlternative(
                    conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                    normalize,
                    match,
                    out result);

            return TryMatchReturnedValueAlternative(
                       conditionalOperation.WhenTrue,
                       normalize,
                       match,
                       out result) ||
                   TryMatchReturnedValueAlternative(
                       conditionalOperation.WhenFalse,
                       normalize,
                       match,
                       out result);
        }

        if (normalizedReturnedValue is ICoalesceOperation coalesceOperation)
            return TryMatchReturnedValueAlternative(
                       coalesceOperation.Value,
                       normalize,
                       match,
                       out result) ||
                   TryMatchReturnedValueAlternative(
                       coalesceOperation.WhenNull,
                       normalize,
                       match,
                       out result);

        result = default!;
        return false;
    }
}
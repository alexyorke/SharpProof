using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardSyntaxFacts
{
    internal static bool HasLaterLoopAssignmentOfMissingNullableValue(
        ExpressionSyntax nullableExpression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        nullableExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(nullableExpression);
        if (!SymbolMutationFacts.TryGetLocalOrParameterSymbol(
                nullableExpression,
                semanticModel,
                cancellationToken,
                out var symbol) ||
            !SymbolicTypeFacts.IsNullableType(SymbolicFactFactory.GetTrackedSymbolType(symbol)) ||
            CSharpSyntaxFacts.GetContainingLoopBody(useNode) is not { } loopBody)
            return false;

        return CSharpSyntaxFacts.DescendantNodesInExecution(loopBody, includeSelf: false)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.SpanStart > useNode.SpanStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                SymbolMutationFacts.ExpressionMatchesSymbol(
                    assignment.Left,
                    symbol,
                    semanticModel,
                    cancellationToken) &&
                IsMissingNullableValueExpression(assignment.Right, semanticModel, cancellationToken));
    }

    private static bool IsMissingNullableValueExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (!SymbolicTypeFacts.IsNullableType(
                CSharpSyntaxFacts.GetExpressionType(expression, semanticModel, cancellationToken)))
            return false;

        if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true, Value: null })
            return true;

        return expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               expression is DefaultExpressionSyntax ||
               expression is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } ||
               expression is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 };
    }

    internal static bool IsBuiltInSequenceElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0) return false;

        var receiverType = CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
        if (receiverType is IArrayTypeSymbol arrayType) return arrayType.Rank == argumentCount;

        return argumentCount == 1 &&
               (receiverType?.SpecialType == SpecialType.System_String ||
                SymbolicTypeFacts.IsBuiltInSpanType(receiverType));
    }

    internal static bool TryGetIndexOrRangeHazardMetadata(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicRuntimeHazardKind kind,
        out string exceptionType,
        out string category)
    {
        kind = default;
        exceptionType = string.Empty;
        category = string.Empty;

        if (IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken))
        {
            var isRange = elementAccess.ArgumentList.Arguments.Count == 1 &&
                          IsBuiltInRangeAccessArgument(
                              elementAccess.ArgumentList.Arguments[0].Expression,
                              semanticModel,
                              cancellationToken);
            if (isRange)
            {
                kind = SymbolicRuntimeHazardKind.ArgumentOutOfRange;
                exceptionType = ExceptionTypes.ArgumentOutOfRangeException;
                category = ExceptionCategories.DefiniteRangeOutOfRange;
                return true;
            }

            kind = SymbolicRuntimeHazardKind.IndexOutOfRange;
            exceptionType = ExceptionTypes.IndexOutOfRangeException;
            category = ExceptionCategories.DefiniteIndexOutOfRange;
            return true;
        }

        if (IsCountBackedIntIndexerElementAccess(elementAccess, semanticModel, cancellationToken))
        {
            kind = SymbolicRuntimeHazardKind.ArgumentOutOfRange;
            exceptionType = ExceptionTypes.ArgumentOutOfRangeException;
            category = ExceptionCategories.DefiniteCountIndexOutOfRange;
            return true;
        }

        return false;
    }

    internal static bool TryGetSlicingInvocationShape(
        IInvocationOperation invocationOperation,
        out ExpressionSyntax sourceExpression,
        out ExpressionSyntax startExpression,
        out ExpressionSyntax? countExpression,
        out bool oneArgumentUpperBoundIsInclusive,
        out string category)
    {
        sourceExpression = null!;
        startExpression = null!;
        countExpression = null;
        oneArgumentUpperBoundIsInclusive = true;
        category = string.Empty;

        var method = invocationOperation.TargetMethod;
        if (TryGetMemoryExtensionsViewSlicingShape(
                invocationOperation,
                method,
                out sourceExpression,
                out startExpression,
                out countExpression))
        {
            category = method.Name == "AsMemory"
                ? ExceptionCategories.DefiniteMemoryExtensionsAsMemoryOutOfRange
                : ExceptionCategories.DefiniteMemoryExtensionsAsSpanOutOfRange;
            return true;
        }

        if (method.IsStatic ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax instanceExpression ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, 0,
                out var firstArgument))
            return false;

        if (IsStringSlicingInvocation(method, "Substring"))
        {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            oneArgumentUpperBoundIsInclusive = true;
            category = ExceptionCategories.DefiniteStringSubstringOutOfRange;
            return TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression);
        }

        if (IsStringSlicingInvocation(method, "Remove"))
        {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            category = ExceptionCategories.DefiniteStringRemoveOutOfRange;
            if (!TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression)) return false;

            oneArgumentUpperBoundIsInclusive = true;
            return true;
        }

        if (IsBuiltInSpanOrMemorySliceInvocation(method))
        {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            oneArgumentUpperBoundIsInclusive = true;
            category = ExceptionCategories.DefiniteSliceOutOfRange;
            return TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression);
        }

        return false;
    }

    internal static bool TryGetMemoryExtensionsViewSlicingShape(
        IInvocationOperation invocationOperation,
        IMethodSymbol method,
        out ExpressionSyntax sourceExpression,
        out ExpressionSyntax startExpression,
        out ExpressionSyntax? countExpression)
    {
        sourceExpression = null!;
        startExpression = null!;
        countExpression = null;

        if (!IsMemoryExtensionsViewInvocation(method)) return false;

        if (!TryGetMemoryExtensionsViewSourceExpression(invocationOperation, out sourceExpression)) return false;

        var intArguments = invocationOperation.Arguments
            .Where(static argument => argument.Parameter?.Type.SpecialType == SpecialType.System_Int32)
            .Select(static argument => argument.Value.Syntax)
            .OfType<ExpressionSyntax>()
            .ToArray();
        if (intArguments.Length is not (1 or 2)) return false;

        startExpression = intArguments[0];
        countExpression = intArguments.Length == 2 ? intArguments[1] : null;
        return true;
    }

    internal static bool TryGetMemoryExtensionsViewSourceExpression(
        IInvocationOperation invocationOperation,
        out ExpressionSyntax sourceExpression)
    {
        if (invocationOperation.Instance?.Syntax is ExpressionSyntax instanceExpression &&
            IsMemoryExtensionsViewSourceType(invocationOperation.Instance.Type))
        {
            sourceExpression = instanceExpression;
            return true;
        }

        foreach (var argument in invocationOperation.Arguments)
            if ((argument.Parameter?.Ordinal == 0 ||
                 IsMemoryExtensionsViewSourceType(argument.Value.Type)) &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression &&
                IsMemoryExtensionsViewSourceType(argument.Value.Type))
            {
                sourceExpression = argumentExpression;
                return true;
            }

        sourceExpression = null!;
        return false;
    }

    internal static bool TryGetOptionalSecondIntArgument(
        IInvocationOperation invocationOperation,
        IMethodSymbol method,
        out ExpressionSyntax? secondArgument)
    {
        secondArgument = null;
        if (method.Parameters.Length == 1)
            return invocationOperation.Arguments.Length == 1 &&
                   method.Parameters[0].Type.SpecialType == SpecialType.System_Int32;

        if (method.Parameters.Length != 2 ||
            invocationOperation.Arguments.Length != 2 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
            method.Parameters[1].Type.SpecialType != SpecialType.System_Int32)
            return false;

        return SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, 1,
            out secondArgument);
    }

    internal static bool IsStringSlicingInvocation(IMethodSymbol method, string methodName)
    {
        return method.Name == methodName &&
               method.ContainingType?.SpecialType == SpecialType.System_String &&
               method.ReturnType.SpecialType == SpecialType.System_String &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    internal static bool IsBuiltInSpanOrMemorySliceInvocation(IMethodSymbol method)
    {
        return method.Name == "Slice" &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) &&
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ContainingType) &&
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ReturnType);
    }

    internal static bool IsMemoryExtensionsViewInvocation(IMethodSymbol method)
    {
        return method.Name is "AsSpan" or "AsMemory" &&
               method.ContainingType?.OriginalDefinition.ToDisplayString() == "System.MemoryExtensions" &&
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ReturnType) &&
               method.Parameters.Count(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) is
                   1 or 2 &&
               method.Parameters.Any(static parameter => IsMemoryExtensionsViewSourceType(parameter.Type));
    }

    internal static bool IsMemoryExtensionsViewSourceType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.SpecialType == SpecialType.System_String ||
               typeSymbol is IArrayTypeSymbol;
    }

    internal static bool IsArrayGetValueInvocation(IMethodSymbol method)
    {
        return method.Name == "GetValue" &&
               !method.IsStatic &&
               method.ContainingType?.SpecialType == SpecialType.System_Array &&
               method.ReturnType.SpecialType == SpecialType.System_Object &&
               method.Parameters.Length > 0 &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    internal static bool IsCountBackedIntIndexerElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (elementAccess.ArgumentList.Arguments.Count != 1) return false;

        var argumentType = CSharpSyntaxFacts.GetExpressionType(
            elementAccess.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken);
        if (argumentType?.SpecialType != SpecialType.System_Int32 &&
            !SymbolicTypeFacts.IsSystemIndexType(argumentType))
            return false;

        var receiverType = CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
        return SymbolicTypeFacts.HasInstanceInt32Member(receiverType, "Count") &&
               SymbolicTypeFacts.HasInt32Indexer(receiverType);
    }

    internal static bool IsBuiltInRangeAccessArgument(
        ExpressionSyntax argumentExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        argumentExpression = UnwrapExpression(argumentExpression);
        if (argumentExpression is RangeExpressionSyntax) return true;

        var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
        return SymbolicTypeFacts.IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
    }

    internal static bool IsNullableValueCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsNonNullableValueType(targetType) &&
               TryGetNullableUnderlyingType(
                   SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel,
                       cancellationToken), out _);
    }

    internal static bool IsUnboxingCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var operandType = CSharpSyntaxFacts.GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
        return IsNonNullableValueType(targetType) &&
               IsReferenceType(operandType);
    }

    internal static bool TryGetConversionOperation(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IConversionOperation conversionOperation)
    {
        if (semanticModel.GetOperation(castExpression, cancellationToken) is IConversionOperation operation)
        {
            conversionOperation = operation;
            return true;
        }

        conversionOperation = null!;
        return false;
    }

    internal static bool TryGetBuiltInNonIdentityConversion(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IConversionOperation conversionOperation,
        out ITypeSymbol targetType)
    {
        targetType = null!;
        if (!TryGetConversionOperation(
                castExpression,
                semanticModel,
                cancellationToken,
                out conversionOperation) ||
            conversionOperation.Conversion.IsUserDefined ||
            conversionOperation.Conversion.IsIdentity ||
            conversionOperation.Type is not { TypeKind: not TypeKind.Dynamic } resolvedTargetType)
            return false;

        targetType = resolvedTargetType;
        return true;
    }

    internal static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsThrowingDivideByZeroType(typeSymbol);
    }

    internal static bool IsIntegralOrDecimalZero(object? value)
    {
        return SymbolicValueFacts.IsIntegralOrDecimalZero(value);
    }

    internal static bool IsReferenceType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsReferenceType(typeSymbol);
    }

    internal static bool IsReferenceLikeType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsReferenceLikeType(typeSymbol);
    }

    internal static bool IsDynamicExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicTypeFacts.IsDynamicExpression(
            expression,
            semanticModel,
            cancellationToken,
            UnwrapDynamicExpression);
    }

    internal static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is { IsValueType: true, TypeKind: not TypeKind.TypeParameter } &&
               typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    internal static bool TryGetNullableUnderlyingType(ITypeSymbol? typeSymbol, out ITypeSymbol underlyingType)
    {
        return SymbolicTypeFacts.TryGetNullableUnderlyingType(typeSymbol, out underlyingType);
    }


    internal static IEnumerable<ExpressionSyntax> GetStackAllocLengthExpressions(
        StackAllocArrayCreationExpressionSyntax stackAllocCreation)
    {
        if (stackAllocCreation.Type is not ArrayTypeSyntax arrayType) yield break;

        foreach (var rankSpecifier in arrayType.RankSpecifiers)
            foreach (var size in rankSpecifier.Sizes)
                if (!size.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    yield return size;
    }

    internal static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapExpression(expression, ExpressionCastUnwrapPolicy.All);
    }

    internal static ExpressionSyntax UnwrapDynamicExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }
}

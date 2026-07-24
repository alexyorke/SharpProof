namespace SharpProof.Symbolic;
internal static class SymbolicRuntimeHazardSyntaxFacts {
    internal static bool TryGetArrayElementStoreType(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType) {
        arrayType = null!;
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0 ||
            CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is not
                IArrayTypeSymbol candidate ||
            candidate.Rank != argumentCount)
            return false;
        arrayType = candidate;
        return true;
    }
    internal static bool HasLaterLoopAssignmentOfMissingNullableValue(
        ExpressionSyntax nullableExpression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        nullableExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(nullableExpression);
        if (!SymbolMutationFacts.TryGetLocalOrParameterSymbol(nullableExpression, semanticModel, cancellationToken, out var symbol) ||
            !SymbolicTypeFacts.IsNullableType(SymbolicFactFactory.GetTrackedSymbolType(symbol)) ||
            CSharpSyntaxFacts.GetContainingLoopBody(useNode) is not { } loopBody)
            return false;
        return CSharpSyntaxFacts.DescendantNodesInExecution(loopBody, includeSelf: false)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.SpanStart > useNode.SpanStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                SymbolMutationFacts.ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken) &&
                IsMissingNullableValueExpression(assignment.Right, semanticModel, cancellationToken));
    }
    private static bool IsMissingNullableValueExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (!SymbolicTypeFacts.IsNullableType(CSharpSyntaxFacts.GetExpressionType(expression, semanticModel, cancellationToken)))
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
        CancellationToken cancellationToken) {
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
        out string category) {
        kind = default;
        exceptionType = string.Empty;
        category = string.Empty;
        if (IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken)) {
            var isRange = elementAccess.ArgumentList.Arguments.Count == 1 &&
                          IsBuiltInRangeAccessArgument(elementAccess.ArgumentList.Arguments[0].Expression,
                              semanticModel, cancellationToken);
            if (isRange) {
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
        if (IsCountBackedIntIndexerElementAccess(elementAccess, semanticModel, cancellationToken)) {
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
        out string category) {
        oneArgumentUpperBoundIsInclusive = true;
        category = string.Empty;
        var method = invocationOperation.TargetMethod;
        if (TryGetMemoryExtensionsViewSlicingShape(
                invocationOperation,
                method,
                out sourceExpression,
                out startExpression,
                out countExpression)) {
            category = method.Name == "AsMemory"
                ? ExceptionCategories.DefiniteMemoryExtensionsAsMemoryOutOfRange
                : ExceptionCategories.DefiniteMemoryExtensionsAsSpanOutOfRange;
            return true;
        }
        if (method.IsStatic ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax instanceExpression ||
            !TryGetIntSlicingArguments(invocationOperation, 0, out var firstArgument, out countExpression))
            return false;
        if (IsStringSlicingInvocation(method, "Substring")) {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            oneArgumentUpperBoundIsInclusive = true;
            category = ExceptionCategories.DefiniteStringSubstringOutOfRange;
            return true;
        }
        if (IsStringSlicingInvocation(method, "Remove")) {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            category = ExceptionCategories.DefiniteStringRemoveOutOfRange;
            oneArgumentUpperBoundIsInclusive = true;
            return true;
        }
        if (IsBuiltInSpanOrMemorySliceInvocation(method)) {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            oneArgumentUpperBoundIsInclusive = true;
            category = ExceptionCategories.DefiniteSliceOutOfRange;
            return true;
        }
        return false;
    }
    internal static bool TryGetMemoryExtensionsViewSlicingShape(
        IInvocationOperation invocationOperation,
        IMethodSymbol method,
        out ExpressionSyntax sourceExpression,
        out ExpressionSyntax startExpression,
        out ExpressionSyntax? countExpression) {
        sourceExpression = null!;
        startExpression = null!;
        countExpression = null;
        if (!IsMemoryExtensionsViewInvocation(method)) return false;
        if (!TryGetMemoryExtensionsViewSourceExpression(
                invocationOperation, out sourceExpression, out var firstArgumentIndex) ||
            !TryGetIntSlicingArguments(
                invocationOperation, firstArgumentIndex, out startExpression, out countExpression))
            return false;
        return true;
    }
    private static bool TryGetIntSlicingArguments(
        IInvocationOperation operation,
        int firstParameterIndex,
        out ExpressionSyntax firstArgument,
        out ExpressionSyntax? secondArgument) {
        firstArgument = null!;
        secondArgument = null;
        var parameters = operation.TargetMethod.Parameters;
        var count = parameters.Length - firstParameterIndex;
        if (count is not (1 or 2) ||
            !parameters.Skip(firstParameterIndex)
                .All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, firstParameterIndex, out firstArgument))
            return false;
        return count == 1 ||
               SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, firstParameterIndex + 1, out secondArgument);
    }
    internal static bool IsStringSlicingInvocation(IMethodSymbol method, string methodName) => method.Name == methodName &&
               method.ContainingType?.SpecialType == SpecialType.System_String &&
               method.ReturnType.SpecialType == SpecialType.System_String &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    internal static bool IsBuiltInSpanOrMemorySliceInvocation(IMethodSymbol method) => method.Name == "Slice" &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) &&
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ContainingType) &&
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ReturnType);
    internal static bool IsMemoryExtensionsViewMethod(IMethodSymbol method) {
        var definition = method.ReducedFrom ?? method;
        return definition.Name is "AsSpan" or "AsMemory" &&
               definition.IsExtensionMethod &&
               definition.ContainingType?.ToDisplayString() == "System.MemoryExtensions";
    }
    internal static bool IsMemoryExtensionsViewInvocation(IMethodSymbol method) => IsMemoryExtensionsViewMethod(method) &&
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ReturnType) &&
               method.Parameters.Count(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) is
                   1 or 2 &&
               method.Parameters.Any(static parameter => IsMemoryExtensionsViewSourceType(parameter.Type));
    internal static bool TryGetMemoryExtensionsViewSourceExpression(
        IInvocationOperation operation,
        out ExpressionSyntax sourceExpression,
        out int firstArgumentIndex) {
        if (operation.Instance?.Syntax is ExpressionSyntax instance &&
            IsMemoryExtensionsViewSourceType(operation.Instance.Type)) {
            sourceExpression = instance;
            firstArgumentIndex = 0;
            return true;
        }
        foreach (var argument in operation.Arguments)
            if (argument.Parameter?.Ordinal == 0 &&
                argument.Value.Syntax is ExpressionSyntax expression &&
                IsMemoryExtensionsViewSourceType(argument.Value.Type)) {
                sourceExpression = expression;
                firstArgumentIndex = 1;
                return true;
            }
        sourceExpression = null!;
        firstArgumentIndex = 0;
        return false;
    }
    internal static bool IsMemoryExtensionsViewSourceType(ITypeSymbol? typeSymbol)
        => typeSymbol?.SpecialType == SpecialType.System_String ||
               typeSymbol is IArrayTypeSymbol;
    internal static bool IsArrayGetValueInvocation(IMethodSymbol method) => method.Name == "GetValue" &&
               !method.IsStatic &&
               method.ContainingType?.SpecialType == SpecialType.System_Array &&
               method.ReturnType.SpecialType == SpecialType.System_Object &&
               method.Parameters.Length > 0 &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    internal static bool IsCountBackedIntIndexerElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
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
        CancellationToken cancellationToken) {
        argumentExpression = CSharpSyntaxFacts.UnwrapExpression(argumentExpression, ExpressionCastUnwrapPolicy.All);
        if (argumentExpression is RangeExpressionSyntax) return true;
        var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
        return SymbolicTypeFacts.IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
    }
    internal static bool IsNullableValueCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => IsNonNullableValueType(targetType) &&
               SymbolicTypeFacts.TryGetNullableUnderlyingType(
                   SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel, cancellationToken), out _);
    internal static bool IsUnboxingCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var operandType = CSharpSyntaxFacts.GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
        return IsNonNullableValueType(targetType) &&
               SymbolicTypeFacts.IsReferenceType(operandType);
    }
    internal static bool IsDynamicExpression(ExpressionSyntax expression, SemanticModel semanticModel,
        CancellationToken cancellationToken) => SymbolicTypeFacts.IsDynamicExpression(
            expression,
            semanticModel,
            cancellationToken,
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression);
    internal static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
        => typeSymbol is { IsValueType: true, TypeKind: not TypeKind.TypeParameter } &&
               typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    internal static IEnumerable<ExpressionSyntax> GetStackAllocLengthExpressions(StackAllocArrayCreationExpressionSyntax
        stackAllocCreation) {
        if (stackAllocCreation.Type is not ArrayTypeSyntax arrayType) yield break;
        foreach (var rankSpecifier in arrayType.RankSpecifiers)
            foreach (var size in rankSpecifier.Sizes)
                if (!size.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    yield return size;
    }
}

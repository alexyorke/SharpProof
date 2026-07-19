namespace SharpProof.Analyzer.Engine;

internal static class PurityKnownBclSemantics
{
    internal static bool IsTrackedOwnedArrayValue(
        IOperation? valueOperation,
        PurityAnalysisState currentState)
    {
        var unwrappedValue = UnwrapArrayOwnershipPreservingConversions(valueOperation);
        if (unwrappedValue == null) return false;

        if (unwrappedValue is IArrayCreationOperation ||
            PurityConcreteReceiverResolver.IsArrayCollectionExpressionOperation(unwrappedValue) ||
            IsArrayEmptyInvocation(unwrappedValue))
            return true;

        if (unwrappedValue is IFlowCaptureReferenceOperation flowCaptureReference &&
            currentState.IsOwnedArrayFlowCapture(flowCaptureReference.Id))
            return true;

        return unwrappedValue is ILocalReferenceOperation localReference &&
               currentState.IsOwnedLocalArraySymbol(localReference.Local);
    }

    internal static bool IsOwnedLocalArrayValue(
        IOperation? valueOperation,
        PurityAnalysisState currentState,
        Compilation compilation)
    {
        var unwrappedValue = UnwrapArrayOwnershipPreservingConversions(valueOperation);
        if (unwrappedValue == null) return false;

        if (IsTrackedOwnedArrayValue(unwrappedValue, currentState) ||
            PurityConcreteReceiverResolver.IsTrustedFreshArrayFactoryOperation(unwrappedValue, compilation, out _))
            return true;

        if (unwrappedValue is IInvocationOperation invocationOperation &&
            invocationOperation.Type is IArrayTypeSymbol &&
            PurityAnalysisEngine.IsTrustedGeneratedFreshOwnedArrayReturningMember(invocationOperation.TargetMethod.OriginalDefinition,
                compilation))
            return true;

        return unwrappedValue is ILocalReferenceOperation localReference &&
               currentState.IsOwnedLocalArraySymbol(localReference.Local);
    }

    internal static bool IsOwnedArrayValueOrTrustedFactory(
        IOperation? valueOperation,
        PurityAnalysisState currentState,
        Compilation compilation)
    {
        return IsOwnedLocalArrayValue(valueOperation, currentState, compilation);
    }

    internal static IOperation? UnwrapArrayOwnershipPreservingConversions(IOperation? operation)
    {
        while (operation is IConversionOperation conversion &&
               (conversion.IsImplicit ||
                (!conversion.Conversion.IsUserDefined &&
                 (conversion.Conversion.IsIdentity ||
                  conversion.Conversion.IsReference))))
            operation = conversion.Operand;

        return operation;
    }

    internal static bool IsArrayAsReadOnlyInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.Name != "AsReadOnly" ||
            targetMethod.ContainingType?.ToDisplayString() != "System.Array" ||
            invocationOperation.Arguments.Length != 1)
            return false;

        return true;
    }

    internal static bool IsArrayInterfaceGetEnumeratorInvocation(
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.Name != "GetEnumerator" ||
            targetMethod.Parameters.Length != 0)
            return false;

        return TryGetExplicitlyCastArrayReceiverType(
            invocationOperation,
            semanticModel,
            cancellationToken,
            out _);
    }

    internal static bool TryGetExplicitlyCastArrayReceiverType(
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType)
    {
        arrayType = null!;
        var invocationSyntax = invocationOperation.Syntax as InvocationExpressionSyntax ??
                               invocationOperation.Syntax.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocationSyntax?.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var receiverExpression = CSharpSyntaxFacts.UnwrapParentheses(memberAccess.Expression);
        if (receiverExpression is not CastExpressionSyntax castExpression) return false;

        var operandTypeInfo = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken);
        var operandType = operandTypeInfo.ConvertedType ?? operandTypeInfo.Type;
        if (operandType is not IArrayTypeSymbol resolvedArrayType) return false;

        arrayType = resolvedArrayType;
        return true;
    }

    private static bool IsArrayEmptyInvocation(IOperation? operation)
    {
        var unwrappedOperation = UnwrapArrayOwnershipPreservingConversions(operation);
        return unwrappedOperation is IInvocationOperation invocation &&
               PurityConcreteReceiverResolver.IsArrayEmptyFactory(invocation.TargetMethod.OriginalDefinition);
    }

    internal static bool IsTimeSpanInvariantCultureParseInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.ContainingType?.ToDisplayString() != "System.TimeSpan" ||
            invocationOperation.Arguments.Length < 2)
            return false;

        if (targetMethod.Name == "Parse" &&
            targetMethod.Parameters.Length == 2 &&
            invocationOperation.Arguments.Length == 2)
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);

        if (targetMethod.Name == "ParseExact" &&
            targetMethod.Parameters.Length == 3 &&
            invocationOperation.Arguments.Length == 3 &&
            targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
            IsSingleTimeSpanConstantFormat(invocationOperation.Arguments[1].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        if (targetMethod.Name == "ParseExact" &&
            targetMethod.Parameters.Length == 4 &&
            invocationOperation.Arguments.Length == 4 &&
            (targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String ||
             SymbolicTypeFacts.IsReadOnlySpanOfCharType(targetMethod.Parameters[0].Type)) &&
            IsSingleTimeSpanConstantFormat(invocationOperation.Arguments[1].Value) &&
            IsTimeSpanStylesNone(invocationOperation.Arguments[3].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        return false;
    }

    internal static bool IsInvariantCultureDeterministicParseInvocation(IInvocationOperation invocationOperation)
    {
        return IsInvariantCultureNumericParseInvocation(invocationOperation) ||
               IsTimeSpanInvariantCultureParseInvocation(invocationOperation) ||
               IsDateOnlyInvariantCultureParseInvocation(invocationOperation) ||
               IsTimeOnlyInvariantCultureParseInvocation(invocationOperation) ||
               IsDateTimeOffsetInvariantCultureParseExactInvocation(invocationOperation);
    }

    internal static bool TryGetSemanticKnownImpureCatalogSource(
        IInvocationOperation invocationOperation,
        out string catalogSource)
    {
        if (IsCurrentCultureSensitiveNumericParseOrFormatInvocation(invocationOperation) ||
            IsCurrentCultureSensitiveDateLikeParseOrFormatInvocation(invocationOperation))
        {
            catalogSource = "current_culture_semantic_rule";
            return true;
        }

        catalogSource = string.Empty;
        return false;
    }

    private static bool IsInvariantCultureNumericParseInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.Name != "Parse" ||
            !IsCultureSensitiveNumericType(targetMethod.ContainingType))
            return false;

        if (targetMethod.Parameters.Length == 2 &&
            invocationOperation.Arguments.Length == 2 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);

        if (targetMethod.Parameters.Length == 3 &&
            invocationOperation.Arguments.Length == 3 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
            targetMethod.Parameters[1].Type.ToDisplayString() == "System.Globalization.NumberStyles")
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        return false;
    }

    private static bool IsCurrentCultureSensitiveNumericParseOrFormatInvocation(
        IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            !IsCultureSensitiveNumericType(targetMethod.ContainingType))
            return IsCurrentCultureSensitiveConvertNumericInvocation(invocationOperation);

        if (targetMethod.Name == "Parse" &&
            targetMethod.Parameters.Length == 1 &&
            invocationOperation.Arguments.Length == 1 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            return true;

        if (targetMethod.Name == "TryParse" &&
            targetMethod.Parameters.Length == 2 &&
            invocationOperation.Arguments.Length == 2 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            return true;

        if (IsCurrentCultureSensitiveToStringInvocation(targetMethod, invocationOperation)) return true;

        return false;
    }

    private static bool IsCurrentCultureSensitiveConvertNumericInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.ContainingType?.ToDisplayString() != "System.Convert" ||
            !IsCurrentCultureSensitiveConvertNumericMethodName(targetMethod.Name) ||
            targetMethod.Parameters.Length != 1 ||
            invocationOperation.Arguments.Length != 1)
            return false;

        return targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String;
    }

    private static bool IsCurrentCultureSensitiveConvertNumericMethodName(string methodName)
    {
        return methodName is
            "ToByte" or
            "ToDecimal" or
            "ToDouble" or
            "ToInt16" or
            "ToInt32" or
            "ToInt64" or
            "ToSByte" or
            "ToSingle" or
            "ToUInt16" or
            "ToUInt32" or
            "ToUInt64";
    }

    private static bool IsCurrentCultureSensitiveDateLikeParseOrFormatInvocation(
        IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            !IsCultureSensitiveDateLikeType(targetMethod.ContainingType))
            return IsCurrentCultureSensitiveConvertDateLikeInvocation(invocationOperation);

        if (IsInvariantCultureDeterministicParseInvocation(invocationOperation)) return false;

        if (targetMethod.Name == "Parse" &&
            invocationOperation.Arguments.Length >= 1 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            return invocationOperation.Arguments.Length == 1 ||
                   HasFormatProviderParameter(targetMethod);

        if (targetMethod.Name == "TryParse" &&
            invocationOperation.Arguments.Length >= 2 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            return invocationOperation.Arguments.Length == 2 ||
                   HasFormatProviderParameter(targetMethod);

        if ((targetMethod.Name == "ParseExact" || targetMethod.Name == "TryParseExact") &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
            IsFormatSpecifierType(targetMethod.Parameters[1].Type))
            return HasFormatProviderParameter(targetMethod) ||
                   (IsDateOnlyOrTimeOnlyType(targetMethod.ContainingType) &&
                    invocationOperation.Arguments.Length == (targetMethod.Name == "ParseExact" ? 2 : 3));

        if (IsCurrentCultureSensitiveToStringInvocation(targetMethod, invocationOperation)) return true;

        if (invocationOperation.Arguments.Length == 0 &&
            targetMethod.Name is "ToLongDateString" or "ToShortDateString" or "ToLongTimeString" or "ToShortTimeString")
            return true;

        return false;
    }

    private static bool IsCurrentCultureSensitiveConvertDateLikeInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.ContainingType?.ToDisplayString() != "System.Convert" ||
            targetMethod.Name != "ToDateTime" ||
            targetMethod.Parameters.Length != 1 ||
            invocationOperation.Arguments.Length != 1)
            return false;

        return targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String;
    }

    private static bool IsCurrentCultureSensitiveToStringInvocation(
        IMethodSymbol targetMethod,
        IInvocationOperation invocationOperation)
    {
        return targetMethod.Name == "ToString" &&
               ((targetMethod.Parameters.Length == 0 &&
                 invocationOperation.Arguments.Length == 0) ||
                (targetMethod.Parameters.Length == 1 &&
                 invocationOperation.Arguments.Length == 1 &&
                 targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String));
    }

    private static bool IsCultureSensitiveNumericType(ITypeSymbol? containingType)
    {
        return containingType?.SpecialType is SpecialType.System_Byte or
                   SpecialType.System_Decimal or
                   SpecialType.System_Double or
                   SpecialType.System_Int16 or
                   SpecialType.System_Int32 or
                   SpecialType.System_Int64 or
                   SpecialType.System_SByte or
                   SpecialType.System_Single or
                   SpecialType.System_UInt16 or
                   SpecialType.System_UInt32 or
                   SpecialType.System_UInt64 ||
               containingType?.ToDisplayString() is "System.Half" or "System.Numerics.BigInteger";
    }

    private static bool IsCultureSensitiveDateLikeType(ITypeSymbol? containingType)
    {
        return containingType?.ToDisplayString() is "System.DateOnly" or
            "System.DateTime" or
            "System.DateTimeOffset" or
            "System.TimeOnly" or
            "System.TimeSpan";
    }

    private static bool IsDateOnlyOrTimeOnlyType(ITypeSymbol? containingType)
    {
        return containingType?.ToDisplayString() is "System.DateOnly" or "System.TimeOnly";
    }

    private static bool IsFormatSpecifierType(ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType == SpecialType.System_String ||
               SymbolicTypeFacts.IsReadOnlySpanOfCharType(typeSymbol) ||
               (typeSymbol is IArrayTypeSymbol arrayType &&
                arrayType.ElementType.SpecialType == SpecialType.System_String);
    }

    private static bool HasFormatProviderParameter(IMethodSymbol methodSymbol)
    {
        foreach (var parameter in methodSymbol.Parameters)
            if (parameter.Type.Name == "IFormatProvider" &&
                parameter.Type.ContainingNamespace?.ToDisplayString() == "System")
                return true;

        return false;
    }

    private static bool IsTimeOnlyInvariantCultureParseInvocation(IInvocationOperation invocationOperation)
    {
        return IsDateOrTimeOnlyInvariantCultureParseInvocation(
            invocationOperation,
            "System.TimeOnly",
            IsSingleTimeOnlyInvariantFormat);
    }

    private static bool IsDateOnlyInvariantCultureParseInvocation(IInvocationOperation invocationOperation)
    {
        return IsDateOrTimeOnlyInvariantCultureParseInvocation(
            invocationOperation,
            "System.DateOnly",
            IsSingleDateOnlyInvariantFormat);
    }

    private static bool IsDateOrTimeOnlyInvariantCultureParseInvocation(
        IInvocationOperation invocationOperation,
        string containingTypeName,
        Func<IOperation, bool> isSingleInvariantFormat)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.ContainingType?.ToDisplayString() != containingTypeName ||
            targetMethod.Name is not ("Parse" or "ParseExact"))
            return false;

        if (targetMethod.Name == "Parse" &&
            targetMethod.Parameters.Length == 2 &&
            invocationOperation.Arguments.Length == 2 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);

        if (targetMethod.Name == "Parse" &&
            targetMethod.Parameters.Length == 3 &&
            invocationOperation.Arguments.Length == 3 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
            IsDateTimeStylesNone(invocationOperation.Arguments[2].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);

        if (targetMethod.Parameters.Length == 3 &&
            invocationOperation.Arguments.Length == 3 &&
            targetMethod.Name == "ParseExact" &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
            isSingleInvariantFormat(invocationOperation.Arguments[1].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        if (targetMethod.Parameters.Length == 4 &&
            invocationOperation.Arguments.Length == 4 &&
            targetMethod.Name == "ParseExact" &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
            isSingleInvariantFormat(invocationOperation.Arguments[1].Value) &&
            IsDateTimeStylesNone(invocationOperation.Arguments[3].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        return false;
    }

    private static bool IsDateTimeOffsetInvariantCultureParseExactInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.ContainingType?.ToDisplayString() != "System.DateTimeOffset" ||
            targetMethod.Name != "ParseExact")
            return false;

        if (targetMethod.Parameters.Length == 3 &&
            invocationOperation.Arguments.Length == 3 &&
            targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
            IsSingleDateTimeOffsetRoundtripFormat(invocationOperation.Arguments[1].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        if (targetMethod.Parameters.Length == 4 &&
            invocationOperation.Arguments.Length == 4 &&
            SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
            IsSingleDateTimeOffsetRoundtripFormat(invocationOperation.Arguments[1].Value) &&
            IsDateTimeStylesNone(invocationOperation.Arguments[3].Value))
            return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);

        return false;
    }

    private static bool IsSingleTimeSpanConstantFormat(IOperation? operation)
    {
        return IsSingleStringConstant(operation, static format => format is "c" or "g" or "G");
    }

    private static bool IsSingleDateOnlyInvariantFormat(IOperation? operation)
    {
        return IsSingleStringConstant(operation, static format => format == "d");
    }

    private static bool IsSingleTimeOnlyInvariantFormat(IOperation? operation)
    {
        return IsSingleStringConstant(operation, static format => format == "t");
    }

    private static bool IsSingleDateTimeOffsetRoundtripFormat(IOperation? operation)
    {
        return IsSingleStringConstant(operation, static format => format is "O" or "o");
    }

    private static bool IsSingleStringConstant(IOperation? operation, Func<string, bool> matchesFormat)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation?.ConstantValue.HasValue == true &&
               unwrappedOperation.ConstantValue.Value is string format &&
               matchesFormat(format);
    }

    private static bool IsZeroStyle(IOperation? operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation?.ConstantValue.HasValue == true &&
               unwrappedOperation.ConstantValue.Value is int styles &&
               styles == 0;
    }

    private static bool IsTimeSpanStylesNone(IOperation? operation)
    {
        return IsZeroStyle(operation);
    }

    private static bool IsDateTimeStylesNone(IOperation? operation)
    {
        return IsZeroStyle(operation);
    }

    private static bool IsCultureInfoInvariantCulture(IOperation? operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation is IPropertyReferenceOperation propertyReference &&
               propertyReference.Property.Name == "InvariantCulture" &&
               propertyReference.Property.ContainingType?.ToDisplayString() == "System.Globalization.CultureInfo";
    }
}

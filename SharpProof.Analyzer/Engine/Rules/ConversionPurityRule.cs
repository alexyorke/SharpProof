namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class ConversionPurityRule : PurityRuleBase<IConversionOperation>
{
    protected override OperationKind Kind => OperationKind.Conversion;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IConversionOperation conversionOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {

        if (conversionOperation.Operand == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var operandResult =
            PurityAnalysisEngine.CheckSingleOperation(conversionOperation.Operand, context, currentState);


        if (!operandResult.IsPure) return operandResult;

        if (conversionOperation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
            conversionOperation.Type?.TypeKind == TypeKind.Dynamic)
            return PurityAnalysisEngine.ImpureResult(
                conversionOperation,
                "dynamic_dispatch",
                nameof(ConversionPurityRule));


        if (conversionOperation.Conversion.IsUserDefined && conversionOperation.Conversion.MethodSymbol != null)
        {
            var operatorMethod = conversionOperation.Conversion.MethodSymbol;


            if (IsPurityNeutralIntrinsicConversion(operatorMethod))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.Conversion))
                return PurityAnalysisEngine.ImpureResult(
                    conversionOperation,
                    "unknown_external_call",
                    nameof(ConversionPurityRule),
                    operatorMethod);

            var operatorResult = PurityCalleeResolver.GetCalleePurityAtUse(operatorMethod, conversionOperation.Syntax, context);
            if (!operatorResult.IsPure) return operatorResult;

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }


        return operandResult;
    }

    private static bool IsPurityNeutralIntrinsicConversion(IMethodSymbol operatorMethod)
    {
        if (operatorMethod.Name != "op_Implicit" ||
            operatorMethod.Parameters.Length != 1)
            return false;

        if (operatorMethod.ContainingType?.SpecialType == SpecialType.System_String &&
            operatorMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
            operatorMethod.ReturnType is INamedTypeSymbol stringSpanReturnType &&
            stringSpanReturnType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>" &&
            stringSpanReturnType.TypeArguments.Length == 1 &&
            stringSpanReturnType.TypeArguments[0].SpecialType == SpecialType.System_Char)
            return true;

        if (operatorMethod.ContainingType is not INamedTypeSymbol containingType ||
            operatorMethod.ReturnType is not INamedTypeSymbol returnType ||
            operatorMethod.Parameters[0].Type is not INamedTypeSymbol parameterType)
            return false;

        var containingTypeDefinition = containingType.OriginalDefinition.ToDisplayString();
        var parameterTypeDefinition = parameterType.OriginalDefinition.ToDisplayString();
        var returnTypeDefinition = returnType.OriginalDefinition.ToDisplayString();

        return containingTypeDefinition switch
        {
            "System.Span<T>" =>
                parameterTypeDefinition == "System.Span<T>" &&
                returnTypeDefinition == "System.ReadOnlySpan<T>",
            "System.Memory<T>" =>
                parameterTypeDefinition == "System.Memory<T>" &&
                returnTypeDefinition == "System.ReadOnlyMemory<T>",
            _ => false
        };
    }
}

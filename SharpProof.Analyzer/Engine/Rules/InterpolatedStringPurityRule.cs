using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class InterpolatedStringPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
        OperationKind.InterpolatedString,
        OperationKind.InterpolatedStringHandlerCreation,
        OperationKind.InterpolatedStringHandlerArgumentPlaceholder);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is IInterpolatedStringHandlerArgumentPlaceholderOperation)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (operation is IInterpolatedStringHandlerCreationOperation handlerCreation)
        {
            var handlerCreationResult = PurityAnalysisEngine.CheckSingleOperation(
                handlerCreation.HandlerCreation,
                context,
                currentState);
            if (!handlerCreationResult.IsPure) return handlerCreationResult;

            return PurityAnalysisEngine.CheckSingleOperation(
                handlerCreation.Content,
                context,
                currentState);
        }

        if (!(operation is IInterpolatedStringOperation interpolatedString))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        var isFormattableStringInvariantArgument = IsFormattableStringInvariantArgument(interpolatedString);

        foreach (var part in interpolatedString.Parts)
        {
            PurityAnalysisEngine.PurityAnalysisResult partResult;

            if (part is IInterpolatedStringTextOperation)
            {
                partResult = PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }
            else if (part is IInterpolationOperation interpolation)
            {
                partResult = PurityAnalysisEngine.CheckSingleOperation(interpolation.Expression, context, currentState);

                if (partResult.IsPure) partResult = CheckImplicitFormattingPurity(interpolation, context);

                if (partResult.IsPure && interpolation.Alignment != null)
                {
                    partResult =
                        PurityAnalysisEngine.CheckSingleOperation(interpolation.Alignment, context, currentState);
                    if (partResult.IsPure && !isFormattableStringInvariantArgument)
                        partResult = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            interpolation.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "reflection_environment_source",
                                nameof(InterpolatedStringPurityRule),
                                interpolation,
                                interpolation.Syntax,
                                catalogSource: "interpolation_formatting"));
                }

                if (partResult.IsPure && interpolation.FormatString != null)
                {
                    partResult =
                        PurityAnalysisEngine.CheckSingleOperation(interpolation.FormatString, context, currentState);
                    if (partResult.IsPure && !isFormattableStringInvariantArgument)
                        partResult = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            interpolation.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "reflection_environment_source",
                                nameof(InterpolatedStringPurityRule),
                                interpolation,
                                interpolation.Syntax,
                                catalogSource: "interpolation_formatting"));
                }
            }
            else
            {
                partResult = PurityAnalysisEngine.CheckSingleOperation(part, context, currentState);
            }

            if (!partResult.IsPure)
                return PurityAnalysisEngine.ImpureResult(
                    partResult.ImpureSyntaxNode ?? part.Syntax,
                    partResult.Evidence);
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckImplicitFormattingPurity(
        IInterpolationOperation interpolation,
        PurityAnalysisContext context)
    {
        var expression = PurityAnalysisEngine.SkipImplicitConversions(interpolation.Expression);
        if (expression == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var expressionType = expression.Type;
        if (expressionType == null ||
            expressionType.SpecialType == SpecialType.System_String)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (expressionType.TypeKind == TypeKind.Dynamic ||
            expressionType.TypeKind == TypeKind.TypeParameter ||
            expressionType.SpecialType == SpecialType.System_Object)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "dynamic_dispatch",
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    PurityAnalysisEngine.TryResolveSymbol(expression)));

        if (expressionType is not INamedTypeSymbol namedType) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var toStringMethod = FindParameterlessToString(namedType);
        if (toStringMethod == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call",
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    expressionType));

        if (namedType.TypeKind == TypeKind.Class &&
            !namedType.IsSealed &&
            toStringMethod.IsVirtual &&
            !toStringMethod.IsSealed)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "dynamic_dispatch",
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    toStringMethod));

        var originalDefinition = toStringMethod.OriginalDefinition;
        if (PurityAnalysisEngine.HasPureExternalAttribute(originalDefinition))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (IsPrimitiveOrEnumInterpolationValue(expressionType)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var trustedMetadataPurity = PurityAnalysisEngine.GetTrustedMethodPurityMetadata(
            originalDefinition,
            context.SemanticModel.Compilation);
        var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
        var generatedPurity = trustedMetadataPurity.GeneratedPurity;

        if (trustedMetadataPurity.HasConfiguredKnownImpureMember)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "catalog_hit",
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    originalDefinition,
                    trustedMetadataPurity.KnownImpureMemberSource));

        if (hasTrustedGeneratedPurity)
        {
            if (generatedPurity.IsPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    generatedPurity.PrimaryCategory,
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    originalDefinition,
                    "generated_purity_summary"));
        }

        if (PurityAnalysisEngine.IsKnownImpure(originalDefinition))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "catalog_hit",
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    originalDefinition,
                    PurityAnalysisEngine.GetKnownImpureMemberSource(
                        originalDefinition) ?? "known_impure"));

        if (IsFrameworkType(expressionType)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (originalDefinition.DeclaringSyntaxReferences.Length == 0)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call",
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    originalDefinition,
                    "metadata"));

        var calleePurity = PurityAnalysisEngine.GetCalleePurity(originalDefinition, context);
        return calleePurity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : calleePurity.WithCallee(originalDefinition, interpolation.Syntax);
    }

    private static IMethodSymbol? FindParameterlessToString(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            foreach (var member in current.GetMembers(nameof(ToString)))
                if (member is IMethodSymbol method &&
                    !method.IsStatic &&
                    method.Parameters.Length == 0 &&
                    method.ReturnType.SpecialType == SpecialType.System_String)
                    return method;

            current = current.BaseType;
        }

        return null;
    }

    private static bool IsFrameworkType(ITypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace?.ToDisplayString();
        return namespaceName == "System" ||
               namespaceName?.StartsWith("System.", StringComparison.Ordinal) == true;
    }

    private static bool IsPrimitiveOrEnumInterpolationValue(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return true;

        return type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Char or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Decimal or
            SpecialType.System_Single or
            SpecialType.System_Double;
    }

    private static bool IsFormattableStringInvariantArgument(IInterpolatedStringOperation interpolatedString)
    {
        IOperation current = interpolatedString;
        while (current.Parent is IConversionOperation conversion &&
               ReferenceEquals(conversion.Operand, current))
            current = conversion;

        if (current.Parent is not IArgumentOperation argumentOperation ||
            !ReferenceEquals(argumentOperation.Value, current))
            return false;

        if (argumentOperation.Parent is not IInvocationOperation invocationOperation) return false;

        var targetMethod = invocationOperation.TargetMethod;
        return targetMethod.Name == "Invariant" &&
               targetMethod.ContainingType?.Name == "FormattableString" &&
               targetMethod.ContainingType.ContainingNamespace?.ToDisplayString() == "System";
    }
}
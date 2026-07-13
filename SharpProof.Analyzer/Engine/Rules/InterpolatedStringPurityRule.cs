using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;

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
        if (IsPrimitiveOrEnumInterpolationValue(expressionType)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var policy = PurityPolicyResolver.Resolve(
            originalDefinition,
            context.SemanticModel.Compilation,
            context.AttributePolicy);
        if (policy is { Decision: PurityPolicyDecision.Impure, Winner: { } impurePolicy })
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                interpolation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    impurePolicy.Category,
                    nameof(InterpolatedStringPurityRule),
                    interpolation,
                    interpolation.Syntax,
                    originalDefinition,
                    impurePolicy.CatalogSource));

        if (policy.Decision == PurityPolicyDecision.Pure)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var calleePurity = PurityCalleeResolver.GetCalleePurity(originalDefinition, context);
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

    private static bool IsPrimitiveOrEnumInterpolationValue(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return true;

        return SymbolicTypeFacts.IsBuiltInIntegralType(type) ||
               type.SpecialType is
            SpecialType.System_Boolean or
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

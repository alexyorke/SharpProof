using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class InterpolatedStringPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.InterpolatedString);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IInterpolatedStringOperation interpolatedString))
            {
                PurityAnalysisEngine.LogDebug($"WARNING: InterpolatedStringPurityRule called with unexpected operation type: {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"InterpolatedStringPurityRule: Analyzing {interpolatedString.Syntax}");






            PurityAnalysisEngine.LogDebug($"InterpolatedStringPurityRule: Assuming interpolation operation itself is pure for {interpolatedString.Syntax}. Part purity handled elsewhere.");

            var isFormattableStringInvariantArgument = IsFormattableStringInvariantArgument(interpolatedString);

            foreach (var part in interpolatedString.Parts)
            {
                PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Checking part: {part.Syntax} ({part.Kind})");

                PurityAnalysisEngine.PurityAnalysisResult partResult;

                if (part is IInterpolatedStringTextOperation)
                {

                    partResult = PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
                else if (part is IInterpolationOperation interpolation)
                {

                    PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Checking Interpolation Expression: {interpolation.Expression.Syntax}");
                    partResult = PurityAnalysisEngine.CheckSingleOperation(interpolation.Expression, context, currentState);

                    if (partResult.IsPure)
                    {
                        partResult = CheckImplicitFormattingPurity(interpolation, context);
                    }

                    if (partResult.IsPure && interpolation.Alignment != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Checking Interpolation Alignment: {interpolation.Alignment.Syntax}");
                        partResult = PurityAnalysisEngine.CheckSingleOperation(interpolation.Alignment, context, currentState);
                        if (partResult.IsPure && !isFormattableStringInvariantArgument)
                        {
                            PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Non-null interpolation alignment implies formatting semantics. Marking impure.");
                            partResult = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                                interpolation.Syntax,
                                PurityAnalysisEngine.PurityEvidence.Create(
                                    "reflection_environment_source",
                                    ruleName: nameof(InterpolatedStringPurityRule),
                                    operation: interpolation,
                                    syntaxNode: interpolation.Syntax,
                                    catalogSource: "interpolation_formatting"));
                        }
                    }
                    if (partResult.IsPure && interpolation.FormatString != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Checking Interpolation FormatString: {interpolation.FormatString.Syntax}");
                        partResult = PurityAnalysisEngine.CheckSingleOperation(interpolation.FormatString, context, currentState);
                        if (partResult.IsPure && !isFormattableStringInvariantArgument)
                        {
                            PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Non-null interpolation format string implies formatting semantics. Marking impure.");
                            partResult = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                                interpolation.Syntax,
                                PurityAnalysisEngine.PurityEvidence.Create(
                                    "reflection_environment_source",
                                    ruleName: nameof(InterpolatedStringPurityRule),
                                    operation: interpolation,
                                    syntaxNode: interpolation.Syntax,
                                    catalogSource: "interpolation_formatting"));
                        }
                    }
                }
                else
                {

                    PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Unexpected part kind: {part.Kind}. Checking generically.");
                    partResult = PurityAnalysisEngine.CheckSingleOperation(part, context, currentState);
                }

                if (!partResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [InterpStrRule] Part is IMPURE. Interpolated string is Impure.");
                    return PurityAnalysisEngine.ImpureResult(
                        partResult.ImpureSyntaxNode ?? part.Syntax,
                        partResult.Evidence);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckImplicitFormattingPurity(
            IInterpolationOperation interpolation,
            PurityAnalysisContext context)
        {
            var expression = PurityAnalysisEngine.SkipImplicitConversions(interpolation.Expression);
            if (expression == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var expressionType = expression.Type;
            if (expressionType == null ||
                expressionType.SpecialType == SpecialType.System_String)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (expressionType.TypeKind == TypeKind.Dynamic ||
                expressionType.TypeKind == TypeKind.TypeParameter ||
                expressionType.SpecialType == SpecialType.System_Object)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: PurityAnalysisEngine.TryResolveSymbol(expression)));
            }

            if (expressionType is not INamedTypeSymbol namedType)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var toStringMethod = FindParameterlessToString(namedType);
            if (toStringMethod == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: expressionType));
            }

            if (namedType.TypeKind == TypeKind.Class &&
                !namedType.IsSealed &&
                toStringMethod.IsVirtual &&
                !toStringMethod.IsSealed)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: toStringMethod));
            }

            var originalDefinition = toStringMethod.OriginalDefinition;
            if (PurityAnalysisEngine.HasPureExternalAttribute(originalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var trustedMetadataPurity = PurityAnalysisEngine.GetTrustedMethodPurityMetadata(
                originalDefinition,
                context.SemanticModel.Compilation);
            var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
            var generatedPurity = trustedMetadataPurity.GeneratedPurity;

            if (trustedMetadataPurity.HasConfiguredKnownImpureMember)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: originalDefinition,
                        catalogSource: trustedMetadataPurity.KnownImpureMemberSource));
            }

            if (hasTrustedGeneratedPurity)
            {
                if (generatedPurity.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (generatedPurity.IsNonPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        interpolation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            nameof(InterpolatedStringPurityRule),
                            interpolation,
                            syntaxNode: interpolation.Syntax,
                            symbol: originalDefinition,
                        catalogSource: "generated_purity_summary"));
                }
            }

            if (PurityAnalysisEngine.IsKnownImpure(originalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: originalDefinition,
                        catalogSource: PurityAnalysisEngine.GetKnownImpureMemberSource(originalDefinition) ?? "known_impure"));
            }

            if (IsFrameworkType(expressionType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (namedType.TypeKind == TypeKind.Class &&
                !namedType.IsSealed &&
                toStringMethod.IsVirtual &&
                !toStringMethod.IsSealed)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: toStringMethod));
            }

            if (originalDefinition.DeclaringSyntaxReferences.Length == 0)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    interpolation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(InterpolatedStringPurityRule),
                        interpolation,
                        syntaxNode: interpolation.Syntax,
                        symbol: originalDefinition,
                        catalogSource: "metadata"));
            }

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
                {
                    if (member is IMethodSymbol method &&
                        !method.IsStatic &&
                        method.Parameters.Length == 0 &&
                        method.ReturnType.SpecialType == SpecialType.System_String)
                    {
                        return method;
                    }
                }

                current = current.BaseType;
            }

            return null;
        }

        private static bool IsFrameworkType(ITypeSymbol type)
        {
            var namespaceName = type.ContainingNamespace?.ToDisplayString();
            return namespaceName == "System" ||
                namespaceName?.StartsWith("System.", System.StringComparison.Ordinal) == true;
        }

        private static bool IsFormattableStringInvariantArgument(IInterpolatedStringOperation interpolatedString)
        {
            IOperation current = interpolatedString;
            while (current.Parent is IConversionOperation conversion &&
                   ReferenceEquals(conversion.Operand, current))
            {
                current = conversion;
            }

            if (current.Parent is not IArgumentOperation argumentOperation ||
                !ReferenceEquals(argumentOperation.Value, current))
            {
                return false;
            }

            if (argumentOperation.Parent is not IInvocationOperation invocationOperation)
            {
                return false;
            }

            var targetMethod = invocationOperation.TargetMethod;
            return targetMethod.Name == "Invariant" &&
                   targetMethod.ContainingType?.Name == "FormattableString" &&
                   targetMethod.ContainingType.ContainingNamespace?.ToDisplayString() == "System";
        }
    }
}

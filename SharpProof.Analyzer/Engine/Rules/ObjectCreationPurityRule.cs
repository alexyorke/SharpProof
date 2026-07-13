using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules;

internal class ObjectCreationPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
        OperationKind.ObjectCreation,
        OperationKind.TypeParameterObjectCreation);

    public PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        if (operation is ITypeParameterObjectCreationOperation typeParameterObjectCreationOperation)
        {
            if (typeParameterObjectCreationOperation.Initializer != null)
            {
                var initializerResult = CheckSingleOperation(typeParameterObjectCreationOperation.Initializer, context,
                    currentState);
                if (!initializerResult.IsPure) return initializerResult;
            }

            return PurityAnalysisResult.Impure(
                typeParameterObjectCreationOperation.Syntax,
                PurityEvidence.Create(
                    "unsupported_operation",
                    nameof(ObjectCreationPurityRule),
                    typeParameterObjectCreationOperation,
                    typeParameterObjectCreationOperation.Syntax,
                    typeParameterObjectCreationOperation.Type,
                    "generic_type_construction"));
        }

        if (!(operation is IObjectCreationOperation objectCreationOperation)) return PurityAnalysisResult.Pure;

        if (IsImplicitExhaustiveSwitchExpressionFallback(objectCreationOperation)) return PurityAnalysisResult.Pure;


        var freshArrayForImmutableWrapper = false;
        var isReadOnlyCollectionConstructor = false;
        var isReadOnlyMemoryOrMemoryConstructor = false;
        var isSpanOrReadOnlySpanArrayConstructor = false;
        if (objectCreationOperation.Arguments.Length > 0)
        {
            isReadOnlyCollectionConstructor = IsReadOnlyCollectionConstructor(objectCreationOperation);
            isReadOnlyMemoryOrMemoryConstructor = IsReadOnlyMemoryOrMemoryConstructor(objectCreationOperation);
            isSpanOrReadOnlySpanArrayConstructor = IsSpanOrReadOnlySpanArrayConstructor(objectCreationOperation);
            for (var argumentIndex = 0; argumentIndex < objectCreationOperation.Arguments.Length; argumentIndex++)
            {
                var argument = objectCreationOperation.Arguments[argumentIndex];
                if (argument.Value == null) return PurityAnalysisResult.Impure(objectCreationOperation.Syntax);

                if (IsPureTransientCharArrayForStringConstructor(objectCreationOperation, argument, context,
                        currentState)) continue;

                if (isReadOnlyCollectionConstructor)
                {
                    if (IsPureReadOnlyCollectionArrayConstructorArgument(objectCreationOperation, argument,
                            currentState))
                    {
                        freshArrayForImmutableWrapper = true;
                        continue;
                    }

                    var readOnlyCollectionArgumentResult = CheckSingleOperation(argument.Value, context, currentState);
                    if (!readOnlyCollectionArgumentResult.IsPure) return readOnlyCollectionArgumentResult;

                    return PurityAnalysisResult.Impure(
                        objectCreationOperation.Syntax,
                        PurityEvidence.Create(
                            "mutable_state_read",
                            nameof(ObjectCreationPurityRule),
                            objectCreationOperation,
                            objectCreationOperation.Syntax,
                            objectCreationOperation.Constructor,
                            "read_only_collection_external_source"));
                }

                if (isReadOnlyMemoryOrMemoryConstructor)
                {
                    var readOnlyMemoryArgumentResult = CheckSingleOperation(argument.Value, context, currentState);
                    if (!readOnlyMemoryArgumentResult.IsPure) return readOnlyMemoryArgumentResult;

                    if (argumentIndex == 0) freshArrayForImmutableWrapper = true;

                    continue;
                }

                if (isSpanOrReadOnlySpanArrayConstructor && argumentIndex == 0)
                {
                    var spanArgumentResult = CheckSingleOperation(argument.Value, context, currentState);
                    if (!spanArgumentResult.IsPure) return spanArgumentResult;

                    freshArrayForImmutableWrapper = true;
                    continue;
                }

                var argumentResult = CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentResult.IsPure) return argumentResult;
            }
        }


        if (objectCreationOperation.Initializer != null)
        {
            var initializerResult = CheckSingleOperation(objectCreationOperation.Initializer, context, currentState);
            if (!initializerResult.IsPure) return initializerResult;
        }


        if (freshArrayForImmutableWrapper) return PurityAnalysisResult.Pure;

        var constructorSymbol = objectCreationOperation.Constructor;
        var constructorWasProvenPure = false;
        if (constructorSymbol != null)
        {
            var cctorResult = CheckStaticConstructorPurity(constructorSymbol.ContainingType, context, currentState);
            if (!cctorResult.IsPure) return cctorResult;

            var policy = PurityPolicyResolver.Resolve(
                constructorSymbol,
                context.SemanticModel.Compilation,
                context.AttributePolicy);
            if (policy is { Decision: PurityPolicyDecision.Impure, Winner: { } impurePolicy })
                return PurityAnalysisResult.Impure(
                    objectCreationOperation.Syntax,
                    PurityEvidence.Create(
                        impurePolicy.Category,
                        nameof(ObjectCreationPurityRule),
                        objectCreationOperation,
                        objectCreationOperation.Syntax,
                        constructorSymbol,
                        impurePolicy.CatalogSource));

            if (policy.Decision == PurityPolicyDecision.Pure) return PurityAnalysisResult.Pure;

            var trustedMetadataPurity = GetTrustedMethodPurityMetadata(
                constructorSymbol,
                context.SemanticModel.Compilation);

            if (trustedMetadataPurity.AllowsKnownPureFallback &&
                TryCreateBclFallbackImpurity(
                    constructorSymbol,
                    objectCreationOperation.Syntax,
                    objectCreationOperation,
                    nameof(ObjectCreationPurityRule),
                    out var bclFallbackConstructorResult))
                return bclFallbackConstructorResult;

            var constructorPurity = GetCalleePurity(constructorSymbol, context);


            if (!constructorPurity.IsPure) return constructorPurity;

            constructorWasProvenPure = true;
        }
        else
        {
            if (objectCreationOperation.Type?.TypeKind == TypeKind.TypeParameter)
                return PurityAnalysisResult.Impure(
                    objectCreationOperation.Syntax,
                    PurityEvidence.Create(
                        "unsupported_operation",
                        nameof(ObjectCreationPurityRule),
                        objectCreationOperation,
                        objectCreationOperation.Syntax,
                        objectCreationOperation.Type,
                        "generic_type_construction"));
        }


        if (objectCreationOperation.Type is IArrayTypeSymbol)
            return PurityAnalysisResult.Impure(
                objectCreationOperation.Syntax,
                PurityEvidence.Create(
                    "mutable_state_write",
                    nameof(ObjectCreationPurityRule),
                    objectCreationOperation,
                    objectCreationOperation.Syntax,
                    objectCreationOperation.Type,
                    "array_creation"));


        if (objectCreationOperation.Type != null && IsInImpureNamespaceOrType(objectCreationOperation.Type))
        {
            if (constructorWasProvenPure) return PurityAnalysisResult.Pure;

            return PurityAnalysisResult.Impure(
                objectCreationOperation.Syntax,
                PurityEvidence.Create(
                    "catalog_hit",
                    nameof(ObjectCreationPurityRule),
                    objectCreationOperation,
                    objectCreationOperation.Syntax,
                    constructorSymbol ?? (ISymbol)objectCreationOperation.Type,
                    "known_impure_namespace_or_type"));
        }


        return PurityAnalysisResult.Pure;
    }

    private static bool IsPureTransientCharArrayForStringConstructor(
        IObjectCreationOperation objectCreationOperation,
        IArgumentOperation argument,
        PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        var constructorSymbol = objectCreationOperation.Constructor;
        if (constructorSymbol?.ContainingType?.SpecialType != SpecialType.System_String ||
            objectCreationOperation.Arguments.Length != 1)
            return false;

        var argumentValue = SkipImplicitConversions(argument.Value) ?? argument.Value;
        if (argumentValue is not IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod == null ||
            invocationOperation.Type is not IArrayTypeSymbol arrayType ||
            arrayType.ElementType.SpecialType != SpecialType.System_Char)
            return false;

        var enumerableType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var targetMethod = invocationOperation.TargetMethod.ReducedFrom ?? invocationOperation.TargetMethod;
        var targetDefinition = targetMethod.OriginalDefinition;
        if (enumerableType == null ||
            targetDefinition.Name != "ToArray" ||
            !SymbolEqualityComparer.Default.Equals(targetDefinition.ContainingType?.OriginalDefinition, enumerableType))
            return false;

        var sourceOperation = invocationOperation.Instance;
        if (sourceOperation == null && invocationOperation.Arguments.Length > 0)
            sourceOperation = invocationOperation.Arguments[0].Value;

        if (sourceOperation == null) return false;

        var sourceResult = CheckSingleOperation(sourceOperation, context, currentState);
        return sourceResult.IsPure;
    }

    private static bool IsPureReadOnlyCollectionArrayConstructorArgument(
        IObjectCreationOperation objectCreationOperation,
        IArgumentOperation argument,
        PurityAnalysisState currentState)
    {
        if (!IsReadOnlyCollectionConstructor(objectCreationOperation)) return false;

        return PurityKnownBclSemantics.IsTrackedOwnedArrayValue(argument.Value, currentState);
    }

    private static bool IsReadOnlyCollectionConstructor(IObjectCreationOperation objectCreationOperation)
    {
        var constructorSymbol = objectCreationOperation.Constructor?.OriginalDefinition;
        return constructorSymbol?.ContainingType?.OriginalDefinition.ToDisplayString() ==
               "System.Collections.ObjectModel.ReadOnlyCollection<T>" &&
               constructorSymbol.Parameters.Length == 1;
    }

    private static bool IsReadOnlyMemoryOrMemoryConstructor(IObjectCreationOperation objectCreationOperation)
    {
        var typeName = objectCreationOperation.Constructor?.ContainingType?.OriginalDefinition.ToDisplayString();
        return (typeName == "System.ReadOnlyMemory<T>" || typeName == "System.Memory<T>") &&
               (objectCreationOperation.Arguments.Length == 1 || objectCreationOperation.Arguments.Length == 3);
    }

    private static bool IsSpanOrReadOnlySpanArrayConstructor(IObjectCreationOperation objectCreationOperation)
    {
        var constructorSymbol = objectCreationOperation.Constructor?.OriginalDefinition;
        if (constructorSymbol == null ||
            constructorSymbol.MethodKind != MethodKind.Constructor ||
            constructorSymbol.Parameters.Length is not (1 or 3) ||
            constructorSymbol.Parameters[0].Type is not IArrayTypeSymbol)
            return false;

        var typeName = constructorSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        return typeName == "System.Span<T>" || typeName == "System.ReadOnlySpan<T>";
    }

    private static bool IsImplicitExhaustiveSwitchExpressionFallback(IObjectCreationOperation objectCreationOperation)
    {
        if (!objectCreationOperation.IsImplicit ||
            objectCreationOperation.Constructor?.ContainingType?.ToDisplayString() !=
            "System.Runtime.CompilerServices.SwitchExpressionException")
            return false;

        return objectCreationOperation.Syntax
            .AncestorsAndSelf()
            .OfType<SwitchExpressionSyntax>()
            .Any(HasUnconditionalDiscardArm);
    }

    private static bool HasUnconditionalDiscardArm(SwitchExpressionSyntax switchExpression)
    {
        return switchExpression.Arms.Any(static arm =>
            arm.WhenClause == null &&
            arm.Pattern.IsKind(SyntaxKind.DiscardPattern));
    }
}

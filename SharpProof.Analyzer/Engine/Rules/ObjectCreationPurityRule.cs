using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class ObjectCreationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
            OperationKind.ObjectCreation,
            OperationKind.TypeParameterObjectCreation);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is ITypeParameterObjectCreationOperation typeParameterObjectCreationOperation)
            {
                if (typeParameterObjectCreationOperation.Initializer != null)
                {
                    var initializerResult = PurityAnalysisEngine.CheckSingleOperation(typeParameterObjectCreationOperation.Initializer, context, currentState);
                    if (!initializerResult.IsPure)
                    {
                        return initializerResult;
                    }
                }

                return PurityAnalysisResult.Impure(
                    typeParameterObjectCreationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unsupported_operation",
                        ruleName: nameof(ObjectCreationPurityRule),
                        operation: typeParameterObjectCreationOperation,
                        syntaxNode: typeParameterObjectCreationOperation.Syntax,
                        symbol: typeParameterObjectCreationOperation.Type,
                        catalogSource: "generic_type_construction"));
            }

            if (!(operation is IObjectCreationOperation objectCreationOperation))
            {
                return PurityAnalysisResult.Pure;
            }

            if (IsImplicitExhaustiveSwitchExpressionFallback(objectCreationOperation))
            {
                return PurityAnalysisResult.Pure;
            }


            bool freshArrayForImmutableWrapper = false;
            bool isReadOnlyCollectionConstructor = false;
            bool isReadOnlyMemoryOrMemoryConstructor = false;
            bool isSpanOrReadOnlySpanArrayConstructor = false;
            if (objectCreationOperation.Arguments.Length > 0)
            {
                isReadOnlyCollectionConstructor = IsReadOnlyCollectionConstructor(objectCreationOperation);
                isReadOnlyMemoryOrMemoryConstructor = IsReadOnlyMemoryOrMemoryConstructor(objectCreationOperation);
                isSpanOrReadOnlySpanArrayConstructor = IsSpanOrReadOnlySpanArrayConstructor(objectCreationOperation);
                for (var argumentIndex = 0; argumentIndex < objectCreationOperation.Arguments.Length; argumentIndex++)
                {
                    var argument = objectCreationOperation.Arguments[argumentIndex];
                    if (argument.Value == null)
                    {
                        return PurityAnalysisResult.Impure(objectCreationOperation.Syntax);
                    }

                    if (IsPureTransientCharArrayForStringConstructor(objectCreationOperation, argument, context, currentState))
                    {
                        continue;
                    }

                    if (isReadOnlyCollectionConstructor)
                    {
                        if (IsPureReadOnlyCollectionArrayConstructorArgument(objectCreationOperation, argument, currentState))
                        {
                            freshArrayForImmutableWrapper = true;
                            continue;
                        }

                        var readOnlyCollectionArgumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                        if (!readOnlyCollectionArgumentResult.IsPure)
                        {
                            return readOnlyCollectionArgumentResult;
                        }

                        return PurityAnalysisResult.Impure(
                            objectCreationOperation.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_read",
                                ruleName: nameof(ObjectCreationPurityRule),
                                operation: objectCreationOperation,
                                syntaxNode: objectCreationOperation.Syntax,
                                symbol: objectCreationOperation.Constructor,
                                catalogSource: "read_only_collection_external_source"));
                    }

                    if (isReadOnlyMemoryOrMemoryConstructor)
                    {
                        var readOnlyMemoryArgumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                        if (!readOnlyMemoryArgumentResult.IsPure)
                        {
                            return readOnlyMemoryArgumentResult;
                        }

                        if (argumentIndex == 0)
                        {
                            freshArrayForImmutableWrapper = true;
                        }

                        continue;
                    }

                    if (isSpanOrReadOnlySpanArrayConstructor && argumentIndex == 0)
                    {
                        var spanArgumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                        if (!spanArgumentResult.IsPure)
                        {
                            return spanArgumentResult;
                        }

                        freshArrayForImmutableWrapper = true;
                        continue;
                    }

                    var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                    if (!argumentResult.IsPure)
                    {
                        return argumentResult;
                    }
                }
            }
            else
            {
            }


            if (objectCreationOperation.Initializer != null)
            {
                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(objectCreationOperation.Initializer, context, currentState);
                if (!initializerResult.IsPure)
                {
                    return initializerResult;
                }
            }


            if (freshArrayForImmutableWrapper)
            {
                return PurityAnalysisResult.Pure;
            }

            IMethodSymbol? constructorSymbol = objectCreationOperation.Constructor;
            var constructorWasProvenPure = false;
            if (constructorSymbol != null)
            {

                var cctorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(constructorSymbol.ContainingType, context, currentState);
                if (!cctorResult.IsPure)
                {
                    return cctorResult;
                }

                var trustedMetadataPurity = PurityAnalysisEngine.GetTrustedMethodPurityMetadata(
                    constructorSymbol,
                    context.SemanticModel.Compilation);
                var knownImpureMemberSource = trustedMetadataPurity.KnownImpureMemberSource;
                var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
                var generatedPurity = trustedMetadataPurity.GeneratedPurity;

                if (trustedMetadataPurity.HasConfiguredKnownImpureMember)
                {
                    return PurityAnalysisResult.Impure(
                        objectCreationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "catalog_hit",
                            ruleName: nameof(ObjectCreationPurityRule),
                            operation: objectCreationOperation,
                            syntaxNode: objectCreationOperation.Syntax,
                            symbol: constructorSymbol,
                            catalogSource: knownImpureMemberSource));
                }

                if (trustedMetadataPurity.AllowsKnownPureFallback &&
                    PurityAnalysisEngine.IsKnownPureBCLMember(
                        constructorSymbol,
                        context.SemanticModel.Compilation))
                {
                    return PurityAnalysisResult.Pure;
                }

                if (hasTrustedGeneratedPurity)
                {
                    if (generatedPurity.IsPure)
                    {
                        return PurityAnalysisResult.Pure;
                    }

				if (!generatedPurity.IsPure)
				{
					return PurityAnalysisEngine.PurityAnalysisResult.Impure(
						objectCreationOperation.Syntax,
						PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            ruleName: nameof(ObjectCreationPurityRule),
                            operation: objectCreationOperation,
                            syntaxNode: objectCreationOperation.Syntax,
                            symbol: constructorSymbol,
                                catalogSource: "generated_purity_summary"));
                    }
                }

                if (knownImpureMemberSource != null)
                {
                    return PurityAnalysisResult.Impure(
                        objectCreationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            string.Equals(
                                knownImpureMemberSource,
                                "assembly_load_context_semantic_rule",
                                StringComparison.Ordinal)
                                ? "reflection_environment_source"
                                : "catalog_hit",
                            ruleName: nameof(ObjectCreationPurityRule),
                            operation: objectCreationOperation,
                            syntaxNode: objectCreationOperation.Syntax,
                            symbol: constructorSymbol,
                            catalogSource: knownImpureMemberSource));
                }

                if (trustedMetadataPurity.AllowsKnownPureFallback &&
                    PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                        constructorSymbol,
                        objectCreationOperation.Syntax,
                        objectCreationOperation,
                        nameof(ObjectCreationPurityRule),
                        out var bclFallbackConstructorResult))
                {
                    return bclFallbackConstructorResult;
                }

                var constructorPurity = PurityAnalysisEngine.GetCalleePurity(constructorSymbol, context);


                if (!constructorPurity.IsPure)
                {
                    return constructorPurity;
                }

                constructorWasProvenPure = true;
            }
            else
            {

                if (objectCreationOperation.Type?.TypeKind == TypeKind.TypeParameter)
                {
                    return PurityAnalysisResult.Impure(
                        objectCreationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unsupported_operation",
                            ruleName: nameof(ObjectCreationPurityRule),
                            operation: objectCreationOperation,
                            syntaxNode: objectCreationOperation.Syntax,
                            symbol: objectCreationOperation.Type,
                            catalogSource: "generic_type_construction"));
                }
            }


            if (objectCreationOperation.Type is IArrayTypeSymbol)
            {
                return PurityAnalysisResult.Impure(
                    objectCreationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "mutable_state_write",
                        ruleName: nameof(ObjectCreationPurityRule),
                        operation: objectCreationOperation,
                        syntaxNode: objectCreationOperation.Syntax,
                        symbol: objectCreationOperation.Type,
                        catalogSource: "array_creation"));
            }





            if (objectCreationOperation.Type != null && PurityAnalysisEngine.IsInImpureNamespaceOrType(objectCreationOperation.Type))
            {
                if (constructorWasProvenPure)
                {
                    return PurityAnalysisResult.Pure;
                }

                if (constructorSymbol != null &&
                    (PurityAnalysisEngine.HasPureExternalAttribute(constructorSymbol) ||
                     PurityAnalysisEngine.IsPureEnforced(
                         constructorSymbol,
                         context.EnforcePureAttributeSymbol,
                         context.PureAttributeSymbol)))
                {
                    return PurityAnalysisResult.Pure;
                }

                return PurityAnalysisResult.Impure(
                    objectCreationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        ruleName: nameof(ObjectCreationPurityRule),
                        operation: objectCreationOperation,
                        syntaxNode: objectCreationOperation.Syntax,
                        symbol: constructorSymbol ?? (ISymbol)objectCreationOperation.Type,
                        catalogSource: "known_impure_namespace_or_type"));
            }


            return PurityAnalysisResult.Pure;
        }

        private static bool IsPureTransientCharArrayForStringConstructor(
            IObjectCreationOperation objectCreationOperation,
            IArgumentOperation argument,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var constructorSymbol = objectCreationOperation.Constructor;
            if (constructorSymbol?.ContainingType?.SpecialType != SpecialType.System_String ||
                objectCreationOperation.Arguments.Length != 1)
            {
                return false;
            }

            var argumentValue = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
            if (argumentValue is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod == null ||
                invocationOperation.Type is not IArrayTypeSymbol arrayType ||
                arrayType.ElementType.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var enumerableType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            var targetMethod = invocationOperation.TargetMethod.ReducedFrom ?? invocationOperation.TargetMethod;
            var targetDefinition = targetMethod.OriginalDefinition;
            if (enumerableType == null ||
                targetDefinition.Name != "ToArray" ||
                !SymbolEqualityComparer.Default.Equals(targetDefinition.ContainingType?.OriginalDefinition, enumerableType))
            {
                return false;
            }

            var sourceOperation = invocationOperation.Instance;
            if (sourceOperation == null && invocationOperation.Arguments.Length > 0)
            {
                sourceOperation = invocationOperation.Arguments[0].Value;
            }

            if (sourceOperation == null)
            {
                return false;
            }

            var sourceResult = PurityAnalysisEngine.CheckSingleOperation(sourceOperation, context, currentState);
            return sourceResult.IsPure;
        }

        private static bool IsPureReadOnlyCollectionArrayConstructorArgument(
            IObjectCreationOperation objectCreationOperation,
            IArgumentOperation argument,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!IsReadOnlyCollectionConstructor(objectCreationOperation))
            {
                return false;
            }

            return PurityAnalysisEngine.IsTrackedOwnedArrayValue(argument.Value, currentState);
        }

        private static bool IsReadOnlyCollectionConstructor(IObjectCreationOperation objectCreationOperation)
        {
            var constructorSymbol = objectCreationOperation.Constructor?.OriginalDefinition;
            return constructorSymbol?.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Collections.ObjectModel.ReadOnlyCollection<T>" &&
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
            {
                return false;
            }

            var typeName = constructorSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
            return typeName == "System.Span<T>" || typeName == "System.ReadOnlySpan<T>";
        }

        private static bool IsImplicitExhaustiveSwitchExpressionFallback(IObjectCreationOperation objectCreationOperation)
        {
            if (!objectCreationOperation.IsImplicit ||
                objectCreationOperation.Constructor?.ContainingType?.ToDisplayString() !=
                "System.Runtime.CompilerServices.SwitchExpressionException")
            {
                return false;
            }

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
}

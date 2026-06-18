using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using static PurelySharp.Analyzer.Engine.PurityAnalysisEngine;

namespace PurelySharp.Analyzer.Engine.Rules
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

                PurityAnalysisEngine.LogDebug("    [ObjCreateRule] Type parameter object creation cannot be verified; treating as impure.");
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


            if (objectCreationOperation.Arguments.Length > 0)
            {
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Checking {objectCreationOperation.Arguments.Length} constructor arguments...");
                foreach (var argument in objectCreationOperation.Arguments)
                {
                    PurityAnalysisEngine.LogDebug($"      [ObjCreateRule.Args] Checking Argument: {argument.Syntax} ({argument.Value?.Kind})");
                    if (argument.Value == null)
                    {
                        return PurityAnalysisResult.Impure(objectCreationOperation.Syntax);
                    }

                    if (IsPureTransientCharArrayForStringConstructor(objectCreationOperation, argument, context, currentState))
                    {
                        PurityAnalysisEngine.LogDebug($"      [ObjCreateRule.Args] Treating transient char[] materialization as PURE for string construction.");
                        continue;
                    }

                    if (IsReadOnlyCollectionConstructor(objectCreationOperation))
                    {
                        if (IsPureReadOnlyCollectionArrayConstructorArgument(objectCreationOperation, argument, currentState))
                        {
                            PurityAnalysisEngine.LogDebug($"      [ObjCreateRule.Args] Treating fresh array as PURE for ReadOnlyCollection construction.");
                            continue;
                        }

                        var readOnlyCollectionArgumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                        if (!readOnlyCollectionArgumentResult.IsPure)
                        {
                            return readOnlyCollectionArgumentResult;
                        }

                        PurityAnalysisEngine.LogDebug($"      [ObjCreateRule.Args] ReadOnlyCollection source is not analyzer-owned. Treating wrapper construction as IMPURE.");
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

                    var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                    if (!argumentResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"      [ObjCreateRule.Args] Argument '{argument.Syntax}' is IMPURE. Object creation is Impure.");
                        return argumentResult;
                    }
                }
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] No arguments to check.");
            }


            if (objectCreationOperation.Initializer != null)
            {
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Checking Initializer: {objectCreationOperation.Initializer.Syntax} ({objectCreationOperation.Initializer.Kind})");
                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(objectCreationOperation.Initializer, context, currentState);
                if (!initializerResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Initializer expression is IMPURE. Object creation is Impure.");
                    return initializerResult;
                }
            }


            IMethodSymbol? constructorSymbol = objectCreationOperation.Constructor;
            if (constructorSymbol != null)
            {
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Checking Constructor: {constructorSymbol.ToDisplayString()}");

                if (PurityAnalysisEngine.IsKnownPureBCLMember(constructorSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor '{constructorSymbol.ToDisplayString()}' is a known-pure BCL member; skipping recursive callee analysis.");
                    return PurityAnalysisResult.Pure;
                }

                if (IsKnownPureStringBuilderConstructor(constructorSymbol) ||
                    IsKnownPureStringReadOnlySpanConstructor(constructorSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor '{constructorSymbol.ToDisplayString()}' is a reviewed pure constructor.");
                    return PurityAnalysisResult.Pure;
                }

                var cctorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(constructorSymbol.ContainingType, context, currentState);
                if (!cctorResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor invocation IMPURE due to impure static constructor in {constructorSymbol.ContainingType?.Name}.");
                    return cctorResult;
                }

                if (constructorSymbol.Locations.FirstOrDefault()?.IsInMetadata == true &&
                    PurityAnalysisEngine.TryGetTrustedGeneratedPurity(
                        constructorSymbol,
                        context.SemanticModel.Compilation,
                        out var generatedPurity))
                {
                    if (generatedPurity.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor '{constructorSymbol.ToDisplayString()}' is trusted pure from generated purity summary.");
                        return PurityAnalysisResult.Pure;
                    }

                    if (generatedPurity.IsImpure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor '{constructorSymbol.ToDisplayString()}' is trusted impure from generated purity summary.");
                        return PurityAnalysisResult.Impure(
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

                var constructorPurity = PurityAnalysisEngine.GetCalleePurity(constructorSymbol, context);


                if (!constructorPurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor '{constructorSymbol.ToDisplayString()}' determined IMPURE by recursive check. Result: Impure.");
                    return constructorPurity;
                }
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor '{constructorSymbol.ToDisplayString()}' determined PURE by recursive check. Trusting result.");
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Constructor symbol not resolved (e.g., implicit struct/default or generic construction). Falling back to conservative analysis.");

                if (objectCreationOperation.Type?.TypeKind == TypeKind.TypeParameter)
                {
                    PurityAnalysisEngine.LogDebug("    [ObjCreateRule] Generic type construction cannot be verified; treating as impure.");
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
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Object creation '{objectCreationOperation.Syntax}' is IMPURE because it creates an array.");
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


            string? typeName = objectCreationOperation.Type?.OriginalDefinition.ToDisplayString();
            if (typeName != null && (
                typeName.StartsWith("System.Collections.Generic.List<") ||
                typeName.StartsWith("System.Collections.Generic.Dictionary<") ||
                typeName.StartsWith("System.Collections.Generic.HashSet<") ||
                typeName.StartsWith("System.Collections.Generic.Queue<") ||
                typeName.StartsWith("System.Collections.Generic.Stack<")
            ))
            {
                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Object creation '{objectCreationOperation.Syntax}' is IMPURE because it creates a known mutable collection type '{typeName}'. StringBuilder is handled separately or by usage.");
                return PurityAnalysisResult.Impure(
                    objectCreationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        ruleName: nameof(ObjectCreationPurityRule),
                        operation: objectCreationOperation,
                        syntaxNode: objectCreationOperation.Syntax,
                        symbol: constructorSymbol ?? (ISymbol?)objectCreationOperation.Type,
                        catalogSource: "known_mutable_collection"));
            }


            if (objectCreationOperation.Type != null && PurityAnalysisEngine.IsInImpureNamespaceOrType(objectCreationOperation.Type))
            {
                if (constructorSymbol != null &&
                    (PurityAnalysisEngine.HasPureExternalAttribute(constructorSymbol) ||
                     PurityAnalysisEngine.IsPureEnforced(
                         constructorSymbol,
                         context.EnforcePureAttributeSymbol,
                         context.PureAttributeSymbol)))
                {
                    PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Object creation '{objectCreationOperation.Syntax}' has an explicit pure constructor boundary. Skipping impure namespace/type fallback.");
                    return PurityAnalysisResult.Pure;
                }

                PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Object creation '{objectCreationOperation.Syntax}' is IMPURE because type '{objectCreationOperation.Type.ToDisplayString()}' is in a known impure namespace/type.");
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


            PurityAnalysisEngine.LogDebug($"    [ObjCreateRule] Object creation '{objectCreationOperation.Syntax}' determined to be Pure (Arguments & Constructor pure, Type not known impure).");
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

            if (argument.Value is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod == null ||
                invocationOperation.Type is not IArrayTypeSymbol arrayType ||
                arrayType.ElementType.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var enumerableType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            var targetMethod = invocationOperation.TargetMethod.OriginalDefinition;
            if (enumerableType == null ||
                !targetMethod.IsExtensionMethod ||
                targetMethod.Name != "ToArray" ||
                !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType?.OriginalDefinition, enumerableType))
            {
                return false;
            }

            if (invocationOperation.Arguments.Length == 0 || invocationOperation.Arguments[0].Value == null)
            {
                return false;
            }

            var sourceResult = PurityAnalysisEngine.CheckSingleOperation(invocationOperation.Arguments[0].Value, context, currentState);
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

            var argumentValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(argument.Value);
            return argumentValue is IArrayCreationOperation ||
                argumentValue is ILocalReferenceOperation localReference &&
                currentState.IsOwnedLocalArraySymbol(localReference.Local);
        }

        private static bool IsReadOnlyCollectionConstructor(IObjectCreationOperation objectCreationOperation)
        {
            var constructorSymbol = objectCreationOperation.Constructor?.OriginalDefinition;
            return constructorSymbol?.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Collections.ObjectModel.ReadOnlyCollection<T>" &&
                constructorSymbol.Parameters.Length == 1;
        }

        private static bool IsKnownPureStringBuilderConstructor(IMethodSymbol? constructorSymbol)
        {
            if (constructorSymbol?.MethodKind != MethodKind.Constructor ||
                constructorSymbol.ContainingType?.ToDisplayString() != "System.Text.StringBuilder")
            {
                return false;
            }

            return constructorSymbol.Parameters.Length == 0 ||
                (constructorSymbol.Parameters.Length == 1 &&
                 constructorSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String);
        }

        private static bool IsKnownPureStringReadOnlySpanConstructor(IMethodSymbol? constructorSymbol)
        {
            if (constructorSymbol?.MethodKind != MethodKind.Constructor ||
                constructorSymbol.ContainingType?.SpecialType != SpecialType.System_String ||
                constructorSymbol.Parameters.Length != 1)
            {
                return false;
            }

            if (constructorSymbol.Parameters[0].Type is not INamedTypeSymbol parameterType)
            {
                return false;
            }

            return parameterType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>" &&
                parameterType.TypeArguments[0].SpecialType == SpecialType.System_Char;
        }
    }
}

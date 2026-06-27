using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal sealed class ImplicitIndexerReferencePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ImplicitIndexerReference);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                implicitIndexerReferenceOperation.Instance,
                context,
                currentState);
            if (!instanceResult.IsPure)
            {
                return instanceResult;
            }

            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
                implicitIndexerReferenceOperation.Argument,
                context,
                currentState);
            if (!argumentResult.IsPure)
            {
                return argumentResult;
            }

            if (IsPartOfAssignmentTarget(implicitIndexerReferenceOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var receiverType = GetKnownReceiverType(
                implicitIndexerReferenceOperation.Instance,
                currentState,
                context.SemanticModel.Compilation,
                out var hasStableConcreteReceiver);

            if (implicitIndexerReferenceOperation.LengthSymbol is IPropertySymbol lengthProperty)
            {
                var lengthResult = CheckPropertyGetterPurity(
                    lengthProperty,
                    receiverType,
                    hasStableConcreteReceiver,
                    implicitIndexerReferenceOperation,
                    context);
                if (!lengthResult.IsPure)
                {
                    return lengthResult;
                }
            }

            return CheckIndexerSymbolPurity(
                implicitIndexerReferenceOperation.IndexerSymbol,
                receiverType,
                hasStableConcreteReceiver,
                implicitIndexerReferenceOperation,
                context);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckIndexerSymbolPurity(
            ISymbol? indexerSymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation,
            PurityAnalysisContext context)
        {
            return indexerSymbol switch
            {
                IPropertySymbol propertySymbol => CheckPropertyGetterPurity(
                    propertySymbol,
                    receiverType,
                    hasStableConcreteReceiver,
                    implicitIndexerReferenceOperation,
                    context),
                IMethodSymbol methodSymbol => CheckMethodPurity(
                    methodSymbol,
                    receiverType,
                    hasStableConcreteReceiver,
                    implicitIndexerReferenceOperation,
                    context),
                _ => PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    implicitIndexerReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unsupported_operation",
                        ruleName: nameof(ImplicitIndexerReferencePurityRule),
                        operation: implicitIndexerReferenceOperation,
                        symbol: indexerSymbol))
            };
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckPropertyGetterPurity(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation,
            PurityAnalysisContext context)
        {
            var getter = ResolveGetter(
                propertySymbol,
                receiverType,
                hasStableConcreteReceiver,
                context.SemanticModel.Compilation);
            if (getter == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    implicitIndexerReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        ruleName: nameof(ImplicitIndexerReferencePurityRule),
                        operation: implicitIndexerReferenceOperation,
                        symbol: propertySymbol.GetMethod));
            }

            var getterPurity = PurityAnalysisEngine.GetCalleePurity(getter, context);
            return getterPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : getterPurity.WithCallee(getter, implicitIndexerReferenceOperation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckMethodPurity(
            IMethodSymbol methodSymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            IImplicitIndexerReferenceOperation implicitIndexerReferenceOperation,
            PurityAnalysisContext context)
        {
            var targetMethod = ResolveMethod(
                methodSymbol,
                receiverType,
                hasStableConcreteReceiver,
                context.SemanticModel.Compilation);
            if (targetMethod == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    implicitIndexerReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        ruleName: nameof(ImplicitIndexerReferencePurityRule),
                        operation: implicitIndexerReferenceOperation,
                        symbol: methodSymbol));
            }

            var methodPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
            return methodPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : methodPurity.WithCallee(targetMethod, implicitIndexerReferenceOperation.Syntax);
        }

        private static IMethodSymbol? ResolveGetter(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            Compilation compilation)
        {
            if (propertySymbol.GetMethod == null)
            {
                return null;
            }

            if (!IsPotentiallyDispatchedGetter(propertySymbol.GetMethod, compilation))
            {
                return propertySymbol.GetMethod.OriginalDefinition;
            }

            if (receiverType == null || !hasStableConcreteReceiver)
            {
                return null;
            }

            if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var implementation = receiverType.FindImplementationForInterfaceMember(propertySymbol) ??
                    receiverType.FindImplementationForInterfaceMember(propertySymbol.GetMethod);
                return implementation switch
                {
                    IPropertySymbol implementationProperty when implementationProperty.GetMethod != null =>
                        implementationProperty.GetMethod.OriginalDefinition,
                    IMethodSymbol implementationMethod => implementationMethod.OriginalDefinition,
                    _ => null
                };
            }

            var rootProperty = GetRootOverriddenProperty(propertySymbol);
            for (var current = receiverType; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(rootProperty.Name))
                {
                    if (member is IPropertySymbol candidate &&
                        (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, rootProperty.OriginalDefinition) ||
                         OverridesProperty(candidate, rootProperty)) &&
                        candidate.GetMethod != null)
                    {
                        return candidate.GetMethod.OriginalDefinition;
                    }
                }
            }

            return propertySymbol.GetMethod.IsAbstract ? null : propertySymbol.GetMethod.OriginalDefinition;
        }

        private static IMethodSymbol? ResolveMethod(
            IMethodSymbol methodSymbol,
            INamedTypeSymbol? receiverType,
            bool hasStableConcreteReceiver,
            Compilation compilation)
        {
            if (!IsPotentiallyDispatchedMethod(methodSymbol, compilation))
            {
                return methodSymbol.OriginalDefinition;
            }

            if (receiverType == null || !hasStableConcreteReceiver)
            {
                return null;
            }

            if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                return receiverType.FindImplementationForInterfaceMember(methodSymbol) as IMethodSymbol;
            }

            var rootMethod = GetRootOverriddenMethod(methodSymbol);
            for (var current = receiverType; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(rootMethod.Name))
                {
                    if (member is IMethodSymbol candidate &&
                        candidate.Parameters.Length == rootMethod.Parameters.Length &&
                        (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, rootMethod.OriginalDefinition) ||
                         OverridesMethod(candidate, rootMethod)))
                    {
                        return candidate.OriginalDefinition;
                    }
                }
            }

            return methodSymbol.IsAbstract ? null : methodSymbol.OriginalDefinition;
        }

        private static INamedTypeSymbol? GetKnownReceiverType(
            IOperation? instanceOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            Compilation compilation,
            out bool hasStableConcreteReceiver)
        {
            if (PurityAnalysisEngine.TryResolveKnownConcreteType(instanceOperation, currentState, compilation, out var concreteType))
            {
                hasStableConcreteReceiver = true;
                return concreteType;
            }

            var receiverType = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation)?.Type as INamedTypeSymbol;
            hasStableConcreteReceiver = receiverType != null &&
                                        (receiverType.TypeKind == TypeKind.Struct || receiverType.IsSealed);
            return receiverType;
        }

        private static bool IsPotentiallyDispatchedGetter(IMethodSymbol getterSymbol, Compilation compilation)
        {
            if (getterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                getterSymbol.IsAbstract)
            {
                return true;
            }

            if (!getterSymbol.IsVirtual && !getterSymbol.IsOverride)
            {
                return false;
            }

            if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(getterSymbol, compilation, out var canBeOverridden))
            {
                return canBeOverridden;
            }

            return !getterSymbol.IsSealed;
        }

        private static bool IsPotentiallyDispatchedMethod(IMethodSymbol methodSymbol, Compilation compilation)
        {
            if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                methodSymbol.IsAbstract)
            {
                return true;
            }

            if (!methodSymbol.IsVirtual && !methodSymbol.IsOverride)
            {
                return false;
            }

            if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(methodSymbol, compilation, out var canBeOverridden))
            {
                return canBeOverridden;
            }

            return !methodSymbol.IsSealed;
        }

        private static IPropertySymbol GetRootOverriddenProperty(IPropertySymbol propertySymbol)
        {
            var current = propertySymbol;
            while (current.OverriddenProperty != null)
            {
                current = current.OverriddenProperty;
            }

            return current.OriginalDefinition;
        }

        private static IMethodSymbol GetRootOverriddenMethod(IMethodSymbol methodSymbol)
        {
            var current = methodSymbol;
            while (current.OverriddenMethod != null)
            {
                current = current.OverriddenMethod;
            }

            return current.OriginalDefinition;
        }

        private static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
        {
            for (var current = property; current != null; current = current.OverriddenProperty)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverridesMethod(IMethodSymbol method, IMethodSymbol target)
        {
            for (var current = method; current != null; current = current.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPartOfAssignmentTarget(IOperation operation)
        {
            return operation.Parent is IAssignmentOperation assignment && assignment.Target == operation;
        }
    }
}

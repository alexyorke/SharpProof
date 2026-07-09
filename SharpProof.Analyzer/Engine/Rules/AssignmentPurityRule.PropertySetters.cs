using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class AssignmentPurityRule : IPurityRule
    {
        private static PurityAnalysisEngine.PurityAnalysisResult CheckPropertySetterPurity(
            IOperation targetOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (targetOperation is not IPropertyReferenceOperation propertyReference ||
                propertyReference.Property.SetMethod is not { } setter)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsValueTypeWithInitializerAssignment(propertyReference, context))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsSourceAutoPropertySetter(propertyReference.Property, context.CancellationToken))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsPotentiallyDispatchedSetter(setter))
            {
                return CheckDispatchedSetterPurity(propertyReference, context, currentState);
            }

            var setterResult = PurityAnalysisEngine.GetCalleePurity(setter.OriginalDefinition, context);
            return setterResult.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : setterResult.WithCallee(setter.OriginalDefinition, targetOperation.Syntax);
        }

        private static bool IsSourceAutoPropertySetter(IPropertySymbol propertySymbol, CancellationToken cancellationToken)
        {
            if (propertySymbol.SetMethod == null ||
                propertySymbol.SetMethod.IsAbstract ||
                propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                return false;
            }

            foreach (var syntaxReference in propertySymbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (syntaxReference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax propertyDeclaration ||
                    propertyDeclaration.AccessorList == null)
                {
                    continue;
                }

                var setterAccessor = propertyDeclaration.AccessorList.Accessors
                    .FirstOrDefault(accessor =>
                        accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration) ||
                        accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration));
                if (setterAccessor != null &&
                    setterAccessor.Body == null &&
                    setterAccessor.ExpressionBody == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPotentiallyDispatchedSetter(IMethodSymbol setterSymbol)
        {
            return setterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                   setterSymbol.IsVirtual ||
                   setterSymbol.IsAbstract ||
                   setterSymbol.IsOverride;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedSetterPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var candidates = ResolvePotentialSetterTargets(
                propertyReferenceOperation.Property,
                context.SemanticModel,
                GetTrackedLocalReceiverType(propertyReferenceOperation.Instance, currentState, context.SemanticModel.Compilation) ??
                    PropertyDispatchHelper.GetKnownReceiverType(propertyReferenceOperation.Instance),
                context.CancellationToken);

            if (candidates.IsDefaultOrEmpty)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(AssignmentPurityRule),
                        propertyReferenceOperation,
                        symbol: propertyReferenceOperation.Property.SetMethod));
            }

            foreach (var setterCandidate in candidates)
            {
                var setterResult = PurityAnalysisEngine.GetCalleePurity(setterCandidate, context);
                if (!setterResult.IsPure)
                {
                    return setterResult.WithCallee(setterCandidate, propertyReferenceOperation.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static INamedTypeSymbol? GetTrackedLocalReceiverType(
            IOperation? instanceOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            Compilation compilation)
        {
            return PurityAnalysisEngine.TryResolveKnownConcreteType(instanceOperation, currentState, compilation, out var concreteType)
                ? concreteType
                : null;
        }

        private static ImmutableArray<IMethodSymbol> ResolvePotentialSetterTargets(
            IPropertySymbol propertySymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol? knownReceiverType,
            CancellationToken cancellationToken)
        {
            var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var targetProperty = propertySymbol.OriginalDefinition;

            if (knownReceiverType != null &&
                (knownReceiverType.TypeKind == TypeKind.Struct || knownReceiverType.IsSealed))
            {
                AddSetterForReceiverType(knownReceiverType, targetProperty, targets);
                return targets.ToImmutableArray();
            }

            if (targetProperty.ContainingType?.TypeKind == TypeKind.Interface)
            {
                foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(semanticModel.Compilation.Assembly.GlobalNamespace, cancellationToken))
                {
                    if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
                    {
                        continue;
                    }

                    if (!TypeHierarchyEnumeration.ImplementsInterface(type, targetProperty.ContainingType))
                    {
                        continue;
                    }

                    AddSetterForReceiverType(type, targetProperty, targets);
                }

                if (targetProperty.SetMethod != null && !targetProperty.SetMethod.IsAbstract)
                {
                    targets.Add(targetProperty.SetMethod.OriginalDefinition);
                }

                return targets.ToImmutableArray();
            }

            var baseProperty = DispatchedMemberResolution.GetRootOverriddenProperty(targetProperty);
            var baseType = baseProperty.ContainingType;
            if (baseType != null)
            {
                foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(semanticModel.Compilation.Assembly.GlobalNamespace, cancellationToken))
                {
                    if (!TypeHierarchyEnumeration.DerivesFrom(type, baseType, includeSelf: true))
                    {
                        continue;
                    }

                    foreach (var property in type.GetMembers(baseProperty.Name).OfType<IPropertySymbol>())
                    {
                        if (DispatchedMemberResolution.OverridesProperty(property, baseProperty) && property.SetMethod != null)
                        {
                            targets.Add(property.SetMethod.OriginalDefinition);
                        }
                    }
                }
            }

            if (baseProperty.SetMethod != null && !baseProperty.SetMethod.IsAbstract)
            {
                targets.Add(baseProperty.SetMethod.OriginalDefinition);
            }

            return targets.ToImmutableArray();
        }

        private static void AddSetterForReceiverType(
            INamedTypeSymbol receiverType,
            IPropertySymbol targetProperty,
            HashSet<IMethodSymbol> targets)
        {
            var setter = PurityAnalysisEngine.ResolvePropertyAccessorTargetForConcreteReceiver(
                targetProperty,
                receiverType,
                preferSetter: true);
            if (setter != null)
            {
                targets.Add(setter.OriginalDefinition);
            }
        }

    }
}

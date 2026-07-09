using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class MethodInvocationPurityRule
    {

        private static bool IsDynamicInvocationReceiver(IOperation? operation)
        {
            var current = operation;

            while (current != null)
            {
                current = NormalizeReceiverOperation(current);
                if (current == null)
                {
                    return false;
                }

                if (current.Type?.TypeKind == TypeKind.Dynamic)
                {
                    return true;
                }

                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                    continue;
                }

                if (TryGetAsConversion(current, out var asOperand, out _))
                {
                    if (asOperand?.Type?.TypeKind == TypeKind.Dynamic)
                    {
                        return true;
                    }

                    current = asOperand;
                    continue;
                }

                if (current is IConversionOperation conversion)
                {
                    current = conversion.Operand;
                    continue;
                }

                if (current is IParenthesizedOperation parenthesized)
                {
                    current = parenthesized.Operand;
                    continue;
                }

                break;
            }

            return false;
        }

        private static INamedTypeSymbol? GetKnownReceiverType(IOperation? invocationInstance)
        {
            var current = invocationInstance;

            while (true)
            {
                current = NormalizeReceiverOperation(current);

                if (current == null)
                {
                    return null;
                }

                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                    continue;
                }

                if (current is IConditionalOperation conditional)
                {
                    var whenTrueType = GetKnownReceiverType(conditional.WhenTrue);
                    var whenFalseType = GetKnownReceiverType(conditional.WhenFalse);

                    if (whenTrueType != null &&
                        whenFalseType != null &&
                        SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
                    {
                        return whenTrueType;
                    }

                    return current.Type as INamedTypeSymbol;
                }

                if (TryGetAsConversion(current, out var asOperand, out var asTargetType))
                {
                    if (asTargetType != null)
                    {
                        var operandType = asOperand?.Type as INamedTypeSymbol;
                        if (operandType != null &&
                            TypeHierarchyEnumeration.ImplementsInterface(operandType, asTargetType, includeInterfaceSelf: true))
                        {
                            current = asOperand;
                            continue;
                        }

                        if (asOperand?.Type is ITypeParameterSymbol typeParameter)
                        {
                            var constrainedType = ResolveConstrainedSealedType(typeParameter);
                            if (constrainedType != null &&
                                TypeHierarchyEnumeration.ImplementsInterface(constrainedType, asTargetType, includeInterfaceSelf: true))
                            {
                                current = asOperand;
                                continue;
                            }
                        }
                    }

                    return asTargetType;
                }

                if (current is IConversionOperation conversion)
                {
                    current = conversion.Operand;
                    continue;
                }

                if (current is IParenthesizedOperation parenthesized)
                {
                    current = parenthesized.Operand;
                    continue;
                }

                if (current.Type is ITypeParameterSymbol typeParameterSymbol)
                {
                    var constrainedSealedType = ResolveConstrainedSealedType(typeParameterSymbol);
                    if (constrainedSealedType != null)
                    {
                        return constrainedSealedType;
                    }

                    return null;
                }

                break;
            }

            return current?.Type as INamedTypeSymbol;
        }

        private static INamedTypeSymbol? GetKnownStaticInterfaceReceiverType(IMethodSymbol invokedMethodSymbol)
        {
            if (!invokedMethodSymbol.IsStatic ||
                invokedMethodSymbol.ContainingType?.TypeKind != TypeKind.Interface ||
                invokedMethodSymbol.ContainingType is not INamedTypeSymbol interfaceType ||
                interfaceType.TypeArguments.IsEmpty)
            {
                return null;
            }

            var interfaceArg = interfaceType.TypeArguments[0];

            if (interfaceArg is INamedTypeSymbol namedType)
            {
                return namedType.TypeKind is TypeKind.Class or TypeKind.Struct
                    ? namedType
                    : null;
            }

            if (interfaceArg is ITypeParameterSymbol typeParameter)
            {
                return ResolveConstrainedSealedType(typeParameter);
            }

            return null;
        }

        private static INamedTypeSymbol? ResolveConstrainedSealedType(ITypeParameterSymbol typeParameter)
        {
            return ResolveConstrainedSealedType(typeParameter, new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
        }

        private static INamedTypeSymbol? ResolveConstrainedSealedType(
            ITypeParameterSymbol typeParameter,
            HashSet<ITypeParameterSymbol> visitedTypeParameters)
        {
            if (!visitedTypeParameters.Add(typeParameter))
            {
                return null;
            }

            INamedTypeSymbol? constrainedType = null;

            foreach (var constraintType in typeParameter.ConstraintTypes)
            {
                INamedTypeSymbol? resolvedConstraintType = null;

                if (constraintType is ITypeParameterSymbol nestedTypeParameter)
                {
                    resolvedConstraintType = ResolveConstrainedSealedType(nestedTypeParameter, visitedTypeParameters);
                }
                else if (constraintType is INamedTypeSymbol namedType)
                {
                    if (namedType.TypeKind == TypeKind.Interface)
                    {
                        continue;
                    }

                    if (namedType.TypeKind != TypeKind.Class &&
                        constraintType.TypeKind != TypeKind.Struct ||
                        !namedType.IsSealed)
                    {
                        return null;
                    }

                    resolvedConstraintType = namedType;
                }

                if (resolvedConstraintType == null)
                {
                    continue;
                }

                if (constrainedType != null &&
                    !SymbolEqualityComparer.Default.Equals(constrainedType, resolvedConstraintType))
                {
                    return null;
                }

                constrainedType = resolvedConstraintType;
            }

            return constrainedType;
        }

        private static bool IsTypeEffectivelyExternallyAccessible(INamedTypeSymbol typeSymbol)
        {
            for (var current = typeSymbol; current != null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility == Accessibility.Private ||
                    current.DeclaredAccessibility == Accessibility.Internal)
                {
                    return false;
                }
            }

            return true;
        }
        private static bool IsAllocationOnlyInterfaceReceiver(IOperation? invocationInstance)
        {
            var current = invocationInstance;

            while (current != null)
            {
                current = NormalizeReceiverOperation(current);

                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                    continue;
                }

                if (current is IConversionOperation conversion)
                {
                    current = conversion.Operand;
                    continue;
                }

                if (current is IParenthesizedOperation parenthesized)
                {
                    current = parenthesized.Operand;
                    continue;
                }

                if (TryGetAsConversion(current, out var asOperand, out _))
                {
                    current = asOperand;
                    continue;
                }

                return current is IObjectCreationOperation;
            }

            return false;
        }

        private static IOperation? NormalizeReceiverOperation(IOperation? operation)
        {
            if (operation is not IConditionalAccessInstanceOperation)
            {
                return operation;
            }

            for (var current = operation.Parent; current != null; current = current.Parent)
            {
                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    return conditionalAccess.Operation;
                }
            }

            return operation;
        }

        private static bool IsBaseReference(IOperation? operation)
        {
            return operation is IInstanceReferenceOperation instanceReference &&
                instanceReference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
                operation.Syntax.IsKind(SyntaxKind.BaseExpression);
        }

        private static bool TryGetAsConversion(
            IOperation? operation,
            out IOperation? operand,
            out INamedTypeSymbol? targetType)
        {
            if (operation is IConversionOperation conversion &&
                IsAsConversionSyntax(conversion.Syntax))
            {
                operand = conversion.Operand;
                targetType = conversion.Type as INamedTypeSymbol;
                return true;
            }

            operand = null;
            targetType = null;
            return false;
        }

        private static bool IsAsConversionSyntax(SyntaxNode syntax)
        {
            if (syntax.IsKind(SyntaxKind.AsExpression))
            {
                return true;
            }

            return syntax.DescendantNodesAndSelf()
                .Any(node => node.IsKind(SyntaxKind.AsExpression));
        }
    }
}

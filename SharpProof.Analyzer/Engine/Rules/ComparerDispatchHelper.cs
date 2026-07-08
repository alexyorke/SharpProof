using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal static class ComparerDispatchHelper
    {
        internal static PurityAnalysisEngine.PurityAnalysisResult CheckKnownConstructionComparerPurity(
            IOperation? receiverOperation,
            PurityAnalysisContext context,
            Func<ITypeSymbol?, bool> isCollectionType,
            Func<ITypeSymbol, bool> isComparerParameterType,
            Func<IOperation, PurityAnalysisEngine.PurityAnalysisResult> checkComparerValuePurity)
        {
            var unwrappedReceiver = PurityAnalysisEngine.SkipImplicitConversions(receiverOperation) ?? receiverOperation;
            if (unwrappedReceiver is IObjectCreationOperation objectCreationOperation)
            {
                return CheckObjectCreationComparerPurity(
                    objectCreationOperation,
                    context,
                    isCollectionType,
                    isComparerParameterType,
                    checkComparerValuePurity);
            }

            if (FieldOrPropertyInitializerOperationHelper.TryGetFieldOrPropertyInitializerOperation(
                    unwrappedReceiver,
                    context,
                    out var initializerOperation) &&
                PurityAnalysisEngine.SkipImplicitConversions(initializerOperation) is IObjectCreationOperation initializerObjectCreation)
            {
                return CheckObjectCreationComparerPurity(
                    initializerObjectCreation,
                    context,
                    isCollectionType,
                    isComparerParameterType,
                    checkComparerValuePurity);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckObjectCreationComparerPurity(
            IObjectCreationOperation objectCreationOperation,
            PurityAnalysisContext context,
            Func<ITypeSymbol?, bool> isCollectionType,
            Func<ITypeSymbol, bool> isComparerParameterType,
            Func<IOperation, PurityAnalysisEngine.PurityAnalysisResult> checkComparerValuePurity)
        {
            if (!isCollectionType(objectCreationOperation.Type))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var argument in objectCreationOperation.Arguments)
            {
                var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
                if (!IsComparerArgument(value, argument.Parameter?.Type, isComparerParameterType))
                {
                    continue;
                }

                var comparerArgumentResult = PurityAnalysisEngine.CheckSingleOperation(value, context, PurityAnalysisEngine.PurityAnalysisState.Pure);
                if (!comparerArgumentResult.IsPure)
                {
                    return comparerArgumentResult;
                }

                var comparerResult = checkComparerValuePurity(value);
                if (!comparerResult.IsPure)
                {
                    return comparerResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsComparerArgument(
            IOperation? value,
            ITypeSymbol? parameterType,
            Func<ITypeSymbol, bool> isComparerParameterType)
        {
            return value?.Type != null &&
                parameterType is INamedTypeSymbol namedParameterType &&
                (isComparerParameterType(namedParameterType) || IsComparerOrDerivedInterface(value.Type));
        }

        internal static IEnumerable<IMethodSymbol> EnumerateComparerImplementations(ITypeSymbol comparerType)
        {
            if (comparerType is not INamedTypeSymbol namedComparerType)
            {
                yield break;
            }

            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var interfaceType in namedComparerType.AllInterfaces)
            {
                if (!IsComparerInterface(interfaceType))
                {
                    continue;
                }

                foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = namedComparerType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                    if (implementation == null || implementation.DeclaringSyntaxReferences.Length == 0)
                    {
                        continue;
                    }

                    if (seen.Add(implementation.OriginalDefinition))
                    {
                        yield return implementation;
                    }
                }
            }
        }

        internal static bool IsUnresolvedComparerDispatch(ITypeSymbol comparerType)
        {
            if (comparerType is ITypeParameterSymbol typeParameter)
            {
                return typeParameter.ConstraintTypes
                    .OfType<INamedTypeSymbol>()
                    .Any(IsComparerOrDerivedInterface);
            }

            if (comparerType is not INamedTypeSymbol namedComparerType)
            {
                return false;
            }

            if (IsComparerInterface(namedComparerType))
            {
                return true;
            }

            if (namedComparerType.TypeKind != TypeKind.Interface && !namedComparerType.IsAbstract)
            {
                return false;
            }

            return IsComparerOrDerivedInterface(namedComparerType);
        }

        internal static bool IsComparerOrDerivedInterface(ITypeSymbol typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                (IsComparerInterface(namedType) || namedType.AllInterfaces.Any(IsComparerInterface));
        }

        internal static bool IsBuiltinValueComparerKey(ITypeSymbol keyType)
        {
            if (keyType.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            return keyType.SpecialType is
                SpecialType.System_Boolean or
                SpecialType.System_Byte or
                SpecialType.System_SByte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_Char or
                SpecialType.System_String;
        }

        private static bool IsComparerInterface(INamedTypeSymbol typeSymbol)
        {
            var displayString = typeSymbol.OriginalDefinition.ToDisplayString();
            return displayString == "System.Collections.Generic.IEqualityComparer<T>" ||
                displayString == "System.Collections.Generic.IComparer<T>";
        }
    }
}

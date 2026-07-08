using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal static class ComparerDispatchHelper
    {
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

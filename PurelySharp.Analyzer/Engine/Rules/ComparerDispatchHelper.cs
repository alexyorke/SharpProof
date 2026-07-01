using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PurelySharp.Analyzer.Engine.Rules
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

        private static bool IsComparerInterface(INamedTypeSymbol typeSymbol)
        {
            var displayString = typeSymbol.OriginalDefinition.ToDisplayString();
            return displayString == "System.Collections.Generic.IEqualityComparer<T>" ||
                displayString == "System.Collections.Generic.IComparer<T>";
        }
    }
}

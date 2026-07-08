using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Analysis
{
    internal static class TypeHierarchyEnumeration
    {
        internal static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol root)
        {
            foreach (var member in root.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    foreach (var inner in EnumerateAllNamedTypes(ns))
                    {
                        yield return inner;
                    }
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var nested in EnumerateNestedTypes(type))
                    {
                        yield return nested;
                    }
                }
            }
        }

        internal static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
        {
            foreach (var member in type.GetTypeMembers())
            {
                yield return member;
                foreach (var nested in EnumerateNestedTypes(member))
                {
                    yield return nested;
                }
            }
        }

        internal static bool OverridesTargetMethod(IMethodSymbol method, IMethodSymbol target)
        {
            var current = method.OverriddenMethod;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }

                current = current.OverriddenMethod;
            }

            return false;
        }

        internal static bool ImplementsInterface(
            INamedTypeSymbol type,
            INamedTypeSymbol? interfaceSymbol,
            bool includeInterfaceSelf = false)
        {
            if (interfaceSymbol == null)
            {
                return false;
            }

            if (includeInterfaceSelf &&
                SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, interfaceSymbol.OriginalDefinition))
            {
                return true;
            }

            return type.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, interfaceSymbol.OriginalDefinition));
        }

        internal static bool DerivesFrom(
            INamedTypeSymbol type,
            INamedTypeSymbol potentialBase,
            bool includeSelf = false)
        {
            for (var current = includeSelf ? type : type.BaseType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, potentialBase.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasMethodBody(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
        {
            if (methodSymbol.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var methodSyntax = syntaxReference.GetSyntax(cancellationToken);
                if (methodSyntax is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclaration &&
                    (methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

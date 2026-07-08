using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer
{
    internal static class AttributeSymbolResolution
    {
        internal static INamedTypeSymbol? ResolveAttributeSymbol(Compilation compilation, string qualifiedMetadataName, string fallbackMetadataName)
        {
            return compilation.GetTypeByMetadataName(qualifiedMetadataName)
                ?? compilation.GetTypeByMetadataName(fallbackMetadataName)
                ?? FindTypeByName(compilation.Assembly.GlobalNamespace, fallbackMetadataName);
        }

        internal static INamedTypeSymbol? FindTypeByName(INamespaceSymbol namespaceSymbol, string typeName)
        {
            var directMatch = namespaceSymbol.GetTypeMembers(typeName).FirstOrDefault();
            if (directMatch != null)
            {
                return directMatch;
            }

            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                var nestedMatch = FindTypeByName(nestedNamespace, typeName);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
        }

        internal static INamedTypeSymbol? GetAppliedAttributeSymbol(IMethodSymbol methodSymbol, string attributeTypeName)
        {
            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                var attributeClass = attributeData.AttributeClass;
                if (attributeClass != null && string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal))
                {
                    return attributeClass;
                }
            }

            return null;
        }
    }
}

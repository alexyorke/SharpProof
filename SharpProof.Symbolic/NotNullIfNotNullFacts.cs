using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic
{
    internal static class NotNullIfNotNullFacts
    {
        private const string NotNullIfNotNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute";

        internal static bool TryGetNotNullIfNotNullParameterName(IMethodSymbol methodSymbol, out string parameterName)
        {
            if (TryGetNotNullIfNotNullParameterName(methodSymbol.GetReturnTypeAttributes(), out parameterName))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(methodSymbol, methodSymbol.OriginalDefinition) &&
                TryGetNotNullIfNotNullParameterName(methodSymbol.OriginalDefinition.GetReturnTypeAttributes(), out parameterName))
            {
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        internal static bool TryGetNotNullIfNotNullParameterName(IPropertySymbol propertySymbol, out string parameterName)
        {
            if (TryGetNotNullIfNotNullParameterName(propertySymbol.GetAttributes(), out parameterName) ||
                TryGetNotNullIfNotNullParameterName(propertySymbol.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty, out parameterName))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(propertySymbol, propertySymbol.OriginalDefinition) &&
                (TryGetNotNullIfNotNullParameterName(propertySymbol.OriginalDefinition.GetAttributes(), out parameterName) ||
                 TryGetNotNullIfNotNullParameterName(propertySymbol.OriginalDefinition.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty, out parameterName)))
            {
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        internal static bool TryGetNotNullIfNotNullParameterName(
            ImmutableArray<AttributeData> attributes,
            out string parameterName)
        {
            foreach (var attribute in attributes)
            {
                if (!string.Equals(
                        SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                        NotNullIfNotNullAttributeMetadataName,
                        StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not string candidate ||
                    string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                parameterName = candidate;
                return true;
            }

            parameterName = string.Empty;
            return false;
        }
    }
}

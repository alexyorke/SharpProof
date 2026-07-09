using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer
{
    internal static class InvalidContractArgumentDiagnostics
    {
        internal static Diagnostic Create(
            string attributeName,
            string argument,
            string reason,
            Location location,
            ISymbol? baselineSymbol = null,
            SyntaxTree? syntaxTree = null)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ContractAttributeProperty, attributeName)
                .Add(SharpProofDiagnostics.ContractArgumentProperty, argument)
                .Add(SharpProofDiagnostics.ContractInvalidReasonProperty, reason);

            if (baselineSymbol != null && syntaxTree != null)
            {
                properties = BaselineDiagnosticProperties.Add(properties, baselineSymbol, syntaxTree);
            }

            return Diagnostic.Create(
                SharpProofDiagnostics.InvalidContractArgumentRule,
                location,
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[]
                {
                    attributeName,
                    argument,
                    reason,
                });
        }
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer
{
    internal static class InvalidContractArgumentDiagnostics
    {
        internal static Diagnostic Create(
            string attributeName,
            string argument,
            string reason,
            Location location)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ContractAttributeProperty, attributeName)
                .Add(SharpProofDiagnostics.ContractArgumentProperty, argument)
                .Add(SharpProofDiagnostics.ContractInvalidReasonProperty, reason);

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

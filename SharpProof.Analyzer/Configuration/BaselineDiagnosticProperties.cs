using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Configuration
{
    internal static class BaselineDiagnosticProperties
    {
        internal static ImmutableDictionary<string, string?> Add(
            ImmutableDictionary<string, string?> properties,
            ISymbol symbol,
            SyntaxTree syntaxTree)
        {
            var symbolId = DiagnosticBaseline.GetPreferredSymbolId(symbol);
            if (!string.IsNullOrWhiteSpace(symbolId))
            {
                properties = properties.SetItem(SharpProofDiagnostics.BaselineSymbolProperty, symbolId);
            }

            var path = DiagnosticBaseline.NormalizePath(syntaxTree.FilePath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(path))
            {
                properties = properties.SetItem(SharpProofDiagnostics.BaselinePathProperty, path);
            }

            return properties;
        }
    }
}

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

internal static class AnalyzerDiagnosticCatalog
{
    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =
        typeof(SharpProofDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)
            .OrderBy(static descriptor => int.Parse(descriptor.Id.Substring(2)))
            .ToImmutableArray();
}

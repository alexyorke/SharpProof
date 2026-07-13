using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Test;

internal static class CSharpVerifierHelper
{
    internal static ImmutableDictionary<string, ReportDiagnostic> NullableWarnings { get; } =
        GetNullableWarningsFromCompiler();

    internal static ImmutableDictionary<string, ReportDiagnostic> ProfileEnabledCommonBugDiagnostics { get; } =
        Enumerable.Range(48, 29).ToImmutableDictionary(
            static number => $"SP{number:0000}",
            static _ => ReportDiagnostic.Suppress,
            StringComparer.Ordinal);

    private static ImmutableDictionary<string, ReportDiagnostic> GetNullableWarningsFromCompiler()
    {
        string[] args = { "/warnaserror:nullable" };
        var commandLineArguments =
            CSharpCommandLineParser.Default.Parse(args, Environment.CurrentDirectory, Environment.CurrentDirectory);
        var nullableWarnings = commandLineArguments.CompilationOptions.SpecificDiagnosticOptions;


        nullableWarnings = nullableWarnings
            .SetItem("CS8632", ReportDiagnostic.Error)
            .SetItem("CS8669", ReportDiagnostic.Error);

        return nullableWarnings;
    }
}

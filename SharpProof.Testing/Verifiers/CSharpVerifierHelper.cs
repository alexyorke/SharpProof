using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

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

    internal static ReferenceAssemblies Net80ReferenceAssemblies { get; } = ReferenceAssemblies.Net.Net80;

    internal static Func<Solution, ProjectId, Solution> ConfigureCompilationOptions { get; } =
        static (solution, projectId) =>
        {
            var project = solution.GetProject(projectId);
            if (project?.CompilationOptions == null) return solution;

            var compilationOptions = project.CompilationOptions.WithSpecificDiagnosticOptions(
                project.CompilationOptions.SpecificDiagnosticOptions
                    .SetItems(NullableWarnings)
                    .SetItems(ProfileEnabledCommonBugDiagnostics));

            return solution.WithProjectCompilationOptions(projectId, compilationOptions);
        };

    internal static string CreateGlobalConfigText(
        string source,
        ImmutableDictionary<string, string>? analyzerOptions = null,
        bool suppressMissingPurity = false)
    {
        var text = "is_global = true\nsharpproof_attribute_stub_namespaces = <global>\n";
        if (suppressMissingPurity || AnalyzerTestHost.HasFileLevelMissingPuritySuppression(source))
            text += "sharpproof_suggest_missing_enforce_pure = false\n";
        if (analyzerOptions != null)
            foreach (var option in analyzerOptions.OrderBy(static option => option.Key, StringComparer.Ordinal))
                text += option.Key + " = " + option.Value + "\n";

        return text;
    }

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

using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class GoldenAnalyzerTests
{
    public static IEnumerable<string> Cases()
    {
        return GoldenTest.Cases("analyzer");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task DiagnosticsMatchGolden(string caseName)
    {
        var fixture = GoldenTest.Load("analyzer", caseName);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(fixture.Source, "contracts", ["SP0027"], filePath: caseName + ".cs");
        GoldenTest.Compare(fixture, FormatDiagnostics(diagnostics));
    }

    internal static string FormatDiagnostics(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        var rows = diagnostics.Select(diagnostic =>
        {
            var location = diagnostic.Location.GetMappedLineSpan();
            return $"{diagnostic.Id} {diagnostic.Severity} {location.Path}:" +
                $"{location.StartLinePosition.Line + 1}:{location.StartLinePosition.Character + 1}-" +
                $"{location.EndLinePosition.Line + 1}:{location.EndLinePosition.Character + 1} " +
                diagnostic.GetMessage(CultureInfo.InvariantCulture);
        }).Order(StringComparer.Ordinal);
        return string.Join('\n', rows);
    }
}

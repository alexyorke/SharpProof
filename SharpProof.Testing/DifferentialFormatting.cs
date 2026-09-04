using System.Globalization;
using Microsoft.CodeAnalysis;
using SharpProof.Ir;

namespace SharpProof.Testing;

public static class DifferentialFormatting
{
    public static string Describe(IrEvaluationResult result)
    {
        return result.Status switch
        {
            IrEvaluationStatus.Value => "a value",
            IrEvaluationStatus.Exception =>
                "exception " + result.Exception!.Kind,
            IrEvaluationStatus.Unsupported =>
                "unsupported " + result.Unsupported!.Reason,
            _ => result.Status.ToString()
        };
    }

    public static string FormatErrors(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(
            " | ",
            diagnostics
                .Where(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .OrderBy(static diagnostic =>
                    diagnostic.Location.SourceSpan.Start)
                .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .Select(static diagnostic =>
                    diagnostic.Id +
                    ": " +
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }
}

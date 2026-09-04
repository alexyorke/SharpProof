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

    public static string FormatErrors(
        IEnumerable<Diagnostic> diagnostics,
        bool includeIdTieBreak = false)
    {
        var ordered = diagnostics
                .Where(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .OrderBy(static diagnostic =>
                    diagnostic.Location.SourceSpan.Start);
        if (includeIdTieBreak)
        {
            ordered = ordered.ThenBy(
                static diagnostic => diagnostic.Id,
                StringComparer.Ordinal);
        }

        return string.Join(
            " | ",
            ordered
                .Select(static diagnostic =>
                    diagnostic.Id +
                    ": " +
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }
}

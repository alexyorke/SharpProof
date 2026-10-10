using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// An array or string length is unbounded above as a cardinality, but as an
// `int` value it stays within the type range. Leaving it unbounded let a loop
// head widen below its own input, and the analyzer crashed (AD0001) instead
// of reporting the precondition violation.
[TestFixture]
public sealed class AdvisoryLengthRangeRegressionTests
{
    private const string Violation = "Call to 'Need' violates precondition 'value > 0'";

    [TestCase("do { if (input >= 0) { int v = items == null ? 0 : items.Length; _ = Need(0); } } while (false); return 0;", Violation)]
    [TestCase("do { if (input >= 0) { int v = text == null ? 0 : text.Length; _ = Need(0); } } while (false); return 0;", Violation)]
    [TestCase("do { if (input >= 0) { if ((items == null ? 0 : items.Length) < input) return 2; _ = Need(0); } } while (false); return 0;", Violation)]
    [TestCase("for (int i = 0; i < 2; i++) { if (input >= 0) { int v = items == null ? 0 : items.Length; _ = Need(0); } } return 0;", Violation)]
    [TestCase("do { if (input >= 0) { int v = items == null ? 0 : items.Length; _ = Need(v + 1); } } while (false); return 0;", null)]
    [TestCase("do { if (input >= 0) { if ((items == null ? 0 : items.Length) < input) return 2; _ = Need(1); } } while (false); return 0;", null)]
    // Controls: the same bodies without a loop were already analyzed.
    [TestCase("if (input >= 0) { int v = items == null ? 0 : items.Length; _ = Need(0); } return 0;", Violation)]
    [TestCase("if (input >= 0) { int v = items == null ? 0 : items.Length; _ = Need(v + 1); } return 0;", null)]
    public async Task LengthsInLoopsKeepPreconditionDiagnostics(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Caller(int input, int[]? items, string? text) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo(["SP0027: " + message]));
    }
}

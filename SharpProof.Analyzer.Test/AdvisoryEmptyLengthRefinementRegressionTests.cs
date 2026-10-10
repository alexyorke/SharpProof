using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP-LEN-002: an empty-array branch gives Length the exact value zero.
[TestFixture]
public sealed class AdvisoryEmptyLengthRefinementRegressionTests
{
    [TestCase("if (items != null && items.Length == 0) return Need(items.Length); return 0;", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("if (items != null && items.Length > 0) return Need(items.Length); return 0;", null)]
    [TestCase("if (items != null && items.Length != 0) return Need(items.Length); return 0;", null)]
    [TestCase("if (items != null) return Need(items.Length); return 0;", null)]
    public async Task EmptyLengthBranchPreservesPreconditionDiagnostic(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Caller(int[]? items) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }

    [TestCase("if (items != null && items.Length == 0) { items = new int[1]; return Need(items.Length); } return 0;", null)]
    [TestCase("if (items != null && items.Length == 0) { items = new int[0]; return Need(items.Length); } return 0;", "Call to 'Need' violates precondition 'value > 0'")]
    public async Task ReplacingArrayInvalidatesCapturedLength(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Caller(int[]? items) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }

    [Test]
    public async Task EmptyStringBranchPreservesPreconditionDiagnostic()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Caller(string? text) { if (text != null && text.Length == 0) return Need(text.Length); return 0; } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(["Call to 'Need' violates precondition 'value > 0'"]));
    }
}

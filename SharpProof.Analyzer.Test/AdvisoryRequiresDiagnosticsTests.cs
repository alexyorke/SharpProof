using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP0027 reports a call whose callee precondition is false in every state the
// advisory interpreter reaches it with; a call it cannot decide stays silent.
[TestFixture]
public sealed class AdvisoryRequiresDiagnosticsTests
{
    [TestCase("return Need(0);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("var x = -3; return Need(x + 1);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(1);", null)]
    [TestCase("return Need(input);", null)]
    // Source callees run as written, so Unknown() is known to return -1.
    [TestCase("return Need(Unknown());", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return input > 0 ? Need(input) : 0;", null)]
    [TestCase("return Twice(0); static int Twice(int y) => Need(y) * 2;", null)]
    [TestCase("return Local(); static int Local() => Need(0);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Bounded(11);", "Call to 'Bounded' violates precondition '[InRange(0, 10)] value'")]
    [TestCase("return new Box(-1).Value;", "Call to '.ctor' violates precondition 'value > 0'")]
    [TestCase("return new Box(2).Value;", null)]
    public async Task ReportsCallsThatAlwaysViolateAPrecondition(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "private static int Bounded([InRange(0, 10)] int value) => value; " +
            "private static int Unknown() => -1; " +
            "public sealed class Box { public int Value; public Box(int value) { Contract.Requires(value > 0); Value = value; } } " +
            "public static int Caller(int input) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }
}

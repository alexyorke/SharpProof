using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// An expression-bodied getter (`int P => 0;`) runs as written, exactly like
// `int P { get => 0; }`: a caller that reads it is still analyzed, so a call
// whose argument it makes violate a precondition is reported.
[TestFixture]
public sealed class ExpressionBodiedGetterRequiresRegressionTests
{
    [TestCase("return Need(Arrow);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(Accessor);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(new Grid().Arrow);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(new Grid().Accessor);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(new Grid()[0, 0]);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(new Grid()[0]);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(Arrow + 1);", null)]
    [TestCase("return Need(new Grid().Arrow + 1);", null)]
    [TestCase("return Need(new Grid()[0, 1]);", null)]
    [TestCase("return Need(new Grid()[1]);", null)]
    [TestCase("return Need(Arrow + input);", null)]
    public async Task ReportsViolationsThroughExpressionBodiedGetters(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "private static int Arrow => 0; " +
            "private static int Accessor { get => 0; } " +
            "public sealed class Grid { public int Arrow => 0; public int Accessor { get => 0; } " +
            "public int this[int a, int b] => a + b; public int this[int a] { get => a; } } " +
            "public static int Caller(int input) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }
}

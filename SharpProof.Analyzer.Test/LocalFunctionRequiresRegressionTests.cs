using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// A local function that captures nothing runs from its arguments alone, like
// a private static method: a caller that calls it is still analyzed, so a
// call whose argument violates its precondition is reported.
[TestFixture]
public sealed class LocalFunctionRequiresRegressionTests
{
    [TestCase("return Need(-1); static int Need(int value) { Contract.Requires(value > 0); return value; }",
        "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("int Need(int value) { Contract.Requires(value > 0); return value; } return Need(0);",
        "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(Zero()); static int Zero() => 0;", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Pass(0); static int Pass(int value) => Need(value);", "Call to 'Need' violates precondition 'value > 0'")]
    // A site in a local function's own body that its caller also reaches is
    // reported once.
    [TestCase("return Local() + Local(); static int Local() => Need(0);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(1); static int Need(int value) { Contract.Requires(value > 0); return value; }", null)]
    [TestCase("return Need(input); static int Need(int value) { Contract.Requires(value > 0); return value; }", null)]
    [TestCase("return Need(Zero() + 1); static int Zero() => 0;", null)]
    [TestCase("return Pass(1); static int Pass(int value) => Need(value);", null)]
    [TestCase("int Shift(int value) => value + input; return Need(Shift(0));", null)]
    // A local function that only calls a capturing one runs its captures too.
    [TestCase("var count = 0; void Bump() { count++; } void Run() { Bump(); } Run(); return Need(count);", null)]
    public async Task ReportsViolationsThroughLocalFunctions(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Caller(int input) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }
}

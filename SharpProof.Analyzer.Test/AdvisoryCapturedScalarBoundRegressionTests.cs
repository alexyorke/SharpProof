using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP-RANGE-001: a captured scalar comparison can bound a call argument.
[TestFixture]
public sealed class AdvisoryCapturedScalarBoundRegressionTests
{
    private const string Violation = "Call to 'Need' violates precondition 'value > 0'";

    [TestCase("if (input < 1) return Need(input); return 0;", Violation)]
    [TestCase("if (input <= 0) return Need(input); return 0;", Violation)]
    [TestCase("if (input == 0) return Need(input); return 0;", Violation)]
    [TestCase("if (input > 0) return Need(input); return 0;", null)]
    [TestCase("return Need(input);", null)]
    [TestCase("if (input <= 1) return Need(input); return 0;", null)]
    [TestCase("if (input < 2) return Need(input); return 0;", null)]
    [TestCase("if (input != 0) return Need(input); return 0;", null)]
    [TestCase("if (input < 1) { input = 1; return Need(input); } return 0;", null)]
    [TestCase("int saved = input; input = 1; if (saved < 1) return Need(input); return 0;", null)]
    [TestCase("int saved = input; if (saved < 1) return Need(input); return 0;", Violation)]
    public async Task CapturedBoundPreservesDefinitePreconditionViolation(string body, string? message)
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

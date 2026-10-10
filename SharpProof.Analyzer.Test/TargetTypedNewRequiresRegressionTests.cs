using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP0027 reports a constructor call that always violates its precondition
// however the creation is spelled: a target-typed `new(...)` is the same call
// as `new Box(...)`.
[TestFixture]
public sealed class TargetTypedNewRequiresRegressionTests
{
    [TestCase("return new Box(-1).Value;", "Call to '.ctor' violates precondition 'value > 0'")]
    [TestCase("Box b = new(-1); return b.Value;", "Call to '.ctor' violates precondition 'value > 0'")]
    [TestCase("return ((Box)new(-1)).Value;", "Call to '.ctor' violates precondition 'value > 0'")]
    [TestCase("return Read(new(-1));", "Call to '.ctor' violates precondition 'value > 0'")]
    [TestCase("Box b = new(2); return b.Value;", null)]
    [TestCase("return ((Box)new(2)).Value;", null)]
    [TestCase("return Read(new(2));", null)]
    [TestCase("Box b = new(input); return b.Value;", null)]
    public async Task ReportsTargetTypedConstructionThatAlwaysViolatesAPrecondition(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "public sealed class Box { public int Value; public Box(int value) { Contract.Requires(value > 0); Value = value; } } " +
            "private static int Read(Box box) => box.Value; " +
            "public static int Caller(int input) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }
}

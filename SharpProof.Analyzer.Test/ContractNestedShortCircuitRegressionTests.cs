using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP-REQ-002: a nested decisive Boolean must keep an unreachable helper out of a clause.
[TestFixture]
public sealed class ContractNestedShortCircuitRegressionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void CompiledClrNeverCallsHelperAfterNestedFalse(bool flag)
    {
        var calls = 0;
        bool SideEffectFalse()
        {
            calls++;
            return false;
        }

        var result = (flag && false) && SideEffectFalse();

        Assert.That(result, Is.False);
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void CompiledClrFaultsBeforeNestedTrue()
    {
        var calls = 0;
        bool SideEffectFalse()
        {
            calls++;
            return false;
        }

        bool Evaluate(int divisor)
        {
            return ((1 / divisor == 0) || true) || SideEffectFalse();
        }

        Assert.Throws<DivideByZeroException>(new Action(() => { _ = Evaluate(0); }));
        Assert.That(calls, Is.Zero);
    }
    [TestCase("(flag && false) && SideEffectFalse()", true)]
    [TestCase("(flag || true) || SideEffectFalse()", false)]
    [TestCase("(flag && true) && SideEffectFalse()", false)]
    [TestCase("(flag || false) || SideEffectFalse()", false)]
    public async Task NestedBooleanControlsCallPrecondition(string clause, bool violation)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int calls; " +
            "private static bool SideEffectFalse() { calls++; return false; } " +
            "private static bool Need(bool flag) { Contract.Requires(" + clause + "); return flag; } " +
            "public static bool Caller(bool flag) { return Need(flag); } }",
            "contracts", ["SP0027"]);
        var expected = "Call to 'Need' violates precondition '" + clause + "'";
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            violation ? Is.EqualTo([expected]) : Is.Empty);
    }

    [TestCase(0, true)]
    [TestCase(1, false)]
    public async Task FaultingLeftStillGuardsNestedTrueClause(int divisor, bool violation)
    {
        const string clause = "((1 / divisor == 0) || true) || SideEffectFalse()";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static bool SideEffectFalse() { throw new System.InvalidOperationException(); } " +
            "private static bool Need(int divisor) { Contract.Requires(" + clause + "); return true; } " +
            "public static bool Caller() { return Need(" + divisor + "); } }",
            "contracts", ["SP0027"]);
        var expected = "Call to 'Need' violates precondition '" + clause + "'";
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            violation ? Is.EqualTo([expected]) : Is.Empty);
    }
}
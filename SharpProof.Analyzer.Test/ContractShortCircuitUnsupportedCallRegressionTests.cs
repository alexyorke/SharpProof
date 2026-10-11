using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP-REQ-001: an unreachable unsupported operand must not discard an exact contract clause.
[TestFixture]
public sealed class ContractShortCircuitUnsupportedCallRegressionTests
{
    [TestCase("false && SideEffectFalse()", true)]
    [TestCase("false && Throws()", true)]
    [TestCase("true || SideEffectFalse()", false)]
    [TestCase("true || Throws()", false)]
    [TestCase("true && SideEffectFalse()", false)]
    [TestCase("true && Throws()", false)]
    [TestCase("false || SideEffectFalse()", false)]
    [TestCase("false || Throws()", false)]
    [TestCase("flag && SideEffectFalse()", false)]
    public async Task EffectfulRightOperandOnlyMattersWhenReachable(string clause, bool violation)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int effects; " +
            "private static bool SideEffectFalse() { effects++; return false; } " +
            "private static bool Throws() { throw new System.InvalidOperationException(); } " +
            "private static bool Need(bool flag) { Contract.Requires(" + clause + "); return flag; } " +
            "public static bool Caller(bool flag) { return Need(flag); } }",
            "contracts", ["SP0027"]);
        var expected = "Call to 'Need' violates precondition '" + clause + "'";
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            violation ? Is.EqualTo([expected]) : Is.Empty);
    }
}

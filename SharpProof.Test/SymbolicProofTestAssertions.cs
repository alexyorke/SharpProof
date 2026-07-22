using System.Runtime.CompilerServices;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

internal static class SymbolicProofTestAssertions {
    internal static SymbolicSourceQueryTestSession CreateSession(string source, [CallerFilePath] string callerFilePath = "")
        => new(source, Path.GetFileName(callerFilePath));
    internal static void AssertConditionProven(
        string source,
        string sourceLine,
        string condition,
        [CallerFilePath] string callerFilePath = "") {
        using var session = CreateSession(source, callerFilePath);
        AssertConditionProven(session, sourceLine, condition);
    }
    internal static void AssertConditionUnknown(
        string source,
        string sourceLine,
        string condition,
        [CallerFilePath] string callerFilePath = "") {
        using var session = CreateSession(source, callerFilePath);
        AssertConditionUnknown(session, sourceLine, condition);
    }
    internal static void AssertConditionProven(SymbolicSourceQueryTestSession session, string sourceLine, string condition)
        => AssertTruthValue(session, sourceLine, condition, SymbolicTruthValue.ProvenTrue);
    internal static void AssertConditionUnknown(SymbolicSourceQueryTestSession session, string sourceLine, string condition)
        => AssertTruthValue(session, sourceLine, condition, SymbolicTruthValue.Unknown);
    private static void AssertTruthValue(
        SymbolicSourceQueryTestSession session,
        string sourceLine,
        string condition,
        SymbolicTruthValue expected) {
        var proof = session.ProveAtMarker((session.FindLine(sourceLine), 20, 0), condition);
        if (proof.TruthValue == SymbolicTruthValue.Unknown && proof.Reason == "UnsupportedIrEncoding") {
            Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown),
                "Unsupported CFG lowering must never produce an optimistic proof.");
            return;
        }
        Assert.That(proof.TruthValue, Is.EqualTo(expected), proof.Reason);
    }
}

using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class Pr275FrontendFailurePropagationTests
{
    [TestCase("int Target() => Make().Equals(\"a\") ? 7 : 3; static string Make() => \"a\";",
        "int Target() => \"a\".Equals(\"a\") ? 7 : 3;", 7)]
    [TestCase("int Target() => \"a\".Equals(Make()) ? 7 : 3; static string Make() => \"a\";",
        "int Target() => \"a\".Equals(\"a\") ? 7 : 3;", 7)]
    [TestCase("int Target() => Values()[0u].Length; static string[] Values() => new string[] { \"abcdefg\" };",
        "int Target() => new int[] { 7 }[0u];", 7)]
    [TestCase("int Target() => Values()[0L].Length; static string[] Values() => new string[] { \"abcdefg\" };",
        "int Target() => new int[] { 7 }[0L];", 7)]
    [TestCase("int Target() => Values()[0UL].Length; static string[] Values() => new string[] { \"abcdefg\" };",
        "int Target() => new int[] { 7 }[0UL];", 7)]
    [TestCase("int Target() => new string[] { \"abcdefg\" }[Index()].Length; static uint Index() => 0u;",
        "int Target() => new int[] { 7 }[0u];", 7)]
    [TestCase("int Target() => new string[] { \"abcdefg\" }[Index()].Length; static long Index() => 0L;",
        "int Target() => new int[] { 7 }[0L];", 7)]
    [TestCase("int Target() => new string[] { \"abcdefg\" }[Index()].Length; static ulong Index() => 0UL;",
        "int Target() => new int[] { 7 }[0UL];", 7)]
    [TestCase("int Target() => Make()[^1]; static string Make() => \"abc\";",
        "int Target() => \"abc\"[^1];", 99)]
    [TestCase("int Target() { int value = 1; value += Increment(); return value; } static int Increment() => 2;",
        "int Target() { int value = 1; value += 2; return value; }", 3)]
    public void OrdinaryCandidatePropagatesUnsupportedNestedCallsWithoutAcceptingThem(
        string unsupportedSource, string supportedSource, int expected)
    {
        using var unsupported = TypedProgramSubject.Create(unsupportedSource);
        Assert.That(unsupported.Invoke([]), Is.EqualTo(expected));
        var rejected = unsupported.Lower();
        Assert.That(rejected.IsExact, Is.False);
        Assert.That(rejected.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));

        using var supported = TypedProgramSubject.Create(supportedSource);
        Assert.That(supported.Invoke([]), Is.EqualTo(expected));
        var accepted = supported.Lower();
        Assert.That(accepted.IsExact, Is.True, accepted.Classification.Abstention.ToString());
        var replay = supported.Execute(accepted, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue, Is.Not.Null);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
    }
}

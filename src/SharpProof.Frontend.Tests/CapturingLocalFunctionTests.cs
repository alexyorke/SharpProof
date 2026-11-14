using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// A local function called from its owner that only reads the owner's locals
// and parameters is inlined over the owner's own variables. Each case runs
// on the CLR and through the lowered program, which must agree.
[TestFixture]
public sealed class CapturingLocalFunctionTests
{
    [TestCase("int Target(int x) { var y = x * 2; return Add(1); int Add(int z) => y + z + x; }", 3, 10)]
    // The function reads the variable, not a copy: the second call sees the new value.
    [TestCase("int Target(int x) { var y = x; var a = Get(); y = 10; return a * 100 + Get(); int Get() => y; }", 3, 310)]
    [TestCase("int Target(int x) { var s = 0; var i = 0; while (i < 3) { s += Square(); i++; } return s + x; int Square() => i * i; }", 1, 6)]
    [TestCase("int Target(int x) { var limit = x; return Clamp(5) + Clamp(-5); int Clamp(int v) { if (v > limit) { return limit; } return v < -limit ? -limit : v; } }", 2, 0)]
    public void ReadOnlyCapturesReplayLikeTheRuntime(string members, int input, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var replay = subject.Execute(lowered, [input]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    // A write to a captured variable, a capture through another capturing
    // local function, and a nested lambda are not admitted.
    [TestCase("int Target(int x) { var y = x; Bump(); return y; void Bump() { y++; } }")]
    [TestCase("int Target(int x) { var y = x; Set(); return y; void Set() => y = 7; }")]
    [TestCase("int Target(int x) { return Outer(); int Outer() => Inner() + 1; int Inner() => x; }")]
    [TestCase("int Target(int x) { return Make()(); System.Func<int> Make() => () => x; }")]
    public void OtherCapturesClose(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);
    }
}

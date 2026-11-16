using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// An allocation records its object's exact runtime type, and boxes record the
// boxed value type and value. Type tests, `as`, unboxing, downcasts and
// Equals(object) read them. Each case runs on the CLR and through the lowered
// program, which must agree on every input, faults included.
[TestFixture]
public sealed class RuntimeTypeLoweringTests
{
    private const string Types =
        " readonly struct Span { private readonly long ticks; public Span(long ticks) { this.ticks = ticks; } public long Ticks => ticks; }";

    [TestCase("int Target(int x) { object o = x; return o is int ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = x; return o is long ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = x; return (int)o + 1; }")]
    [TestCase("int Target(int x) { object o = x; return o is int i ? i * 2 : -1; }")]
    [TestCase("int Target(int x) { object o = x; return (long)o > 0 ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? (object)x : null; return (int)o; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? (object)x : null; int? n = (int?)o; return n ?? -1; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? (object)x : null; var n = o as int?; return n ?? -1; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? (object)x : (object)(long)x; var n = o as int?; return n ?? -1; }")]
    [TestCase("int Target(int x) { object o = (System.DayOfWeek)x; return (int)o; }")]
    [TestCase("int Target(int x) { object o = (System.DayOfWeek)x; return o is int ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = (System.DayOfWeek)x; return o is System.DayOfWeek d ? (int)d + 10 : -1; }")]
    [TestCase("int Target(int x) { object o = x; return (int)(System.DayOfWeek)o; }")]
    [TestCase("int Target(int x) { System.IComparable c = x; return c is int ? 1 : 0; }")]
    // A boxed enum is not a box of its underlying type.
    [TestCase("int Target(int x) { object o = (Small)x; return o is byte ? 1 : o is Small ? 2 : 0; } enum Small : byte { A }")]
    [TestCase("int Target(int x) { System.DayOfWeek? d = x > 0 ? (System.DayOfWeek)x : null; object o = d; return o is System.DayOfWeek e ? (int)e : -1; }")]
    [TestCase("int Target(int x) { object o = x; return x.Equals(o) ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = (long)x; return x.Equals(o) ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? (object)x : null; int? n = 2; return n.Equals(o) ? 1 : 0; }")]
    [TestCase("int Target(int x) { int? n = x > 0 ? x : null; object o = n; return o is int k ? k : -1; }")]
    [TestCase("int Target(int x) { object o = new int[x > 0 ? 1 : 0]; return o is int[] ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? new object() : (object)x; return o is int ? 1 : o is string ? 2 : 0; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? new object() : (object)x; var n = o as int?; return n ?? -1; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? new object() : (object)new int[1]; return o is int[] ? 1 : 0; }")]
    [TestCase("int Target(int x) { object o = x > 0 ? new object() : (object)new long[2]; return o is int[] ? 1 : o is long[] ? 2 : 0; }")]
    [TestCase("int Target(int x) { object o = new Span(x); return o is Span s ? (int)s.Ticks : -1; }")]
    [TestCase("int Target(int x) { object o = new Span(x); return (int)((Span)o).Ticks; }")]
    public void RuntimeTypesReplayLikeTheRuntime(string members)
    {
        foreach (var input in new[] { -2, 0, 1, 3 })
        { AssertAgrees(members + Types, [input]); }
    }

    // An object the program did not allocate has no known runtime type: the
    // test lowers exactly, but replay can only approximate it.
    [Test]
    public void AParameterHasNoKnownRuntimeType()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { return Check(\"s\"); } static int Check(object o) => o is string ? 1 : 0;");
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(subject.Execute(lowered, [1]).ConsumedApproximation, Is.True);
    }

    private static void AssertAgrees(string members, System.Collections.Immutable.ImmutableArray<object> inputs)
    {
        using var subject = TypedProgramSubject.Create(members);
        var expected = subject.Invoke(inputs);
        var lowered = subject.LowerSourceCalls();
        var context = members + " with " + string.Join(", ", inputs);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention + ": " + context);
        var replay = subject.Execute(lowered, inputs);
        Assert.That(replay.ConsumedApproximation, Is.False, context);
        if (expected is System.Exception thrown)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception), context + " threw " + thrown.GetType().Name);
            return;
        }
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), context);
        Assert.That(replay.ReturnValue!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(System.Convert.ToInt64(expected, System.Globalization.CultureInfo.InvariantCulture))), context);
    }
}

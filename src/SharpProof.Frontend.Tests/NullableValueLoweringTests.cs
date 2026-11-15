using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// A T? over a scalar is a reference to an immutable object holding the value,
// and null is "no value". Each case runs on the CLR and through the lowered
// program, which must agree, including on which inputs throw.
[TestFixture]
public sealed class NullableValueLoweringTests
{
    private const string Maybe = "int? n = x < 0 ? null : x; ";
    private const string Pair = "int? a = x < 0 ? null : x; int? b = y < 0 ? null : y; ";

    [TestCase("int Target(int x) { " + Maybe + "return n ?? -7; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n.HasValue ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n.GetValueOrDefault(); }")]
    [TestCase("int Target(int x) { " + Maybe + "return n.GetValueOrDefault(9); }")]
    [TestCase("int Target(int x) { " + Maybe + "return n == null ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n != null ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n >= 3 ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n == 3 ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n != 3 ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n is 3 ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n is > 2 ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n is null ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n is not null ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n is > 0 and < 10 ? 1 : 0; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n is int v ? v + 1 : -1; }")]
    [TestCase("int Target(int x) { " + Maybe + "return n switch { null => -1, 0 => 0, > 5 => 2, _ => 1 }; }")]
    [TestCase("int Target(int x) { " + Maybe + "return (-n) ?? 5; }")]
    [TestCase("int Target(int x) { " + Maybe + "return (~n) ?? 5; }")]
    [TestCase("long Target(int x) { " + Maybe + "long? w = n; return w ?? -1L; }")]
    [TestCase("long Target(int x) { " + Maybe + "return (n + 1L) ?? -1L; }")]
    [TestCase("int Target(int x) { bool? b = x < 0 ? null : x > 5; return b == true ? 1 : b == false ? 0 : -1; }")]
    [TestCase("int Target(int x) { bool? b = x < 0 ? null : x > 5; return (!b) == true ? 1 : 0; }")]
    [TestCase("int Target(int x) { System.DayOfWeek? d = x < 0 ? null : (System.DayOfWeek)x; return d == System.DayOfWeek.Monday ? 1 : d < System.DayOfWeek.Friday ? 2 : 0; }")]
    // The variable holds the value, not a copy of a mutable cell.
    [TestCase("int Target(int x) { " + Maybe + "var m = n; n = null; return (m ?? -1) * 10 + (n ?? -1); }")]
    public void UnaryNullablesReplayLikeTheRuntime(string members)
    {
        foreach (var input in new[] { -3, 0, 1, 3, 6, 12 })
        { AssertAgrees(members, [input]); }
    }

    [TestCase("int Target(int x, int y) { " + Pair + "return (a + b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a - b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a * b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a & b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a | b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a << b) ?? -1; }")]
    // Dividing by a zero value faults only when the dividend has a value too.
    [TestCase("int Target(int x, int y) { " + Pair + "return (a / b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a % b) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return checked(a * b * 1000000) ?? -1; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return a < b ? 1 : 0; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return a <= b ? 1 : 0; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return a == b ? 1 : 0; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return a != b ? 1 : 0; }")]
    [TestCase("int Target(int x, int y) { " + Pair + "return (a ?? b) ?? -1; }")]
    public void BinaryNullablesReplayLikeTheRuntime(string members)
    {
        foreach (var x in new[] { -1, 0, 3, 50000 })
        {
            foreach (var y in new[] { -1, 0, 2, 50000 })
            { AssertAgrees(members, [x, y]); }
        }
    }

    // Reading the value of an empty T? builds an InvalidOperationException
    // through runtime resources: the lowered program reaches an unknown call
    // there and never returns a value.
    [TestCase("int Target(int x) { " + Maybe + "return n.Value; }")]
    [TestCase("int Target(int x) { " + Maybe + "return (int)n; }")]
    public void ReadingAnEmptyValueDoesNotReturn(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(subject.Invoke([-1]), Is.InstanceOf<System.InvalidOperationException>());
        Assert.That(subject.Execute(lowered, [-1]).Status, Is.Not.EqualTo(IrProgramExecutionStatus.Returned));
        AssertAgrees(members, [4]);
    }

    // Boxing, member calls other than HasValue, Value and GetValueOrDefault,
    // narrowing and lifted user-defined conversions, the three-valued bool?
    // operators, lifted enum arithmetic and lifted increments stay closed.
    [TestCase("object Target(int x) { int? n = x; return n; }")]
    [TestCase("bool Target(int x) { int? n = x; return n.Equals(3); }")]
    [TestCase("int Target(int x) { int? n = x; return n.GetHashCode(); }")]
    [TestCase("string Target(int x) { int? n = x; return n.ToString(); }")]
    [TestCase("int Target(int x) { int? n = x; byte? b = (byte?)n; return b ?? 0; }")]
    [TestCase("int Target(int x) { long? n = x; int? b = (int?)n; return b ?? 0; }")]
    [TestCase("int Target(bool x) { bool? b = x ? null : true; return (b & false) == false ? 1 : 0; }")]
    [TestCase("int Target(bool x) { bool? b = x ? null : true; return (b | true) == true ? 1 : 0; }")]
    [TestCase("int Target(int x) { System.DayOfWeek? d = (System.DayOfWeek)x; return (int)(d | System.DayOfWeek.Monday ?? 0); }")]
    [TestCase("int Target(int x) { int? n = x; n++; return n ?? 0; }")]
    [TestCase("int Target(int x) { decimal? n = x; return n > 1 ? 1 : 0; }")]
    public void OtherNullableShapesStayClosed(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);
    }

    private static void AssertAgrees(string members, System.Collections.Immutable.ImmutableArray<object> inputs)
    {
        using var subject = TypedProgramSubject.Create(members);
        var expected = subject.Invoke(inputs);
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var replay = subject.Execute(lowered, inputs);
        var context = members + " with " + string.Join(", ", inputs);
        Assert.That(replay.ConsumedApproximation, Is.False, context);
        if (expected is System.Exception thrown)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception), context + " threw " + thrown.GetType().Name);
            return;
        }
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), context);
        var value = replay.ReturnValue!;
        Assert.That(value.Kind == IrValueKind.Boolean ? (object)value.Boolean : value.IntegerNumericValue,
            Is.EqualTo(expected is bool flag ? flag : new System.Numerics.BigInteger(System.Convert.ToInt64(expected, System.Globalization.CultureInfo.InvariantCulture))),
            context);
    }
}

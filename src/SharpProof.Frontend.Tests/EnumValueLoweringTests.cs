using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// An enum value lowers as its underlying integer. Each case runs on the CLR
// and through the lowered program, which must agree.
[TestFixture]
public sealed class EnumValueLoweringTests
{
    private const string Enums =
        "public enum Small : byte { A = 1, B = 200 } " +
        "[System.Flags] public enum Flags { None = 0, A = 1, B = 2, C = 4 } " +
        "public enum Wide : uint { Low = 0, High = 0xFFFFFFFF } " +
        "public enum Signed : sbyte { Min = -128, Zero = 0 } " +
        "public enum Half : ushort { Top = 0x8000 }";

    [TestCase("int Target(int x) { var e = (Small)x; return e == Small.B ? 1 : 0; }", 200, 1)]
    [TestCase("int Target(int x) { var e = (Small)x; return e == Small.B ? 1 : 0; }", 456, 1)]
    [TestCase("int Target(int x) { var e = (Small)x; return e != Small.A ? 1 : 0; }", 257, 0)]
    [TestCase("int Target(int x) => (int)(Small)x;", -56, 200)]
    [TestCase("int Target(int x) { var f = (Flags)x; f |= Flags.C; return (int)(f & ~Flags.A); }", 3, 6)]
    [TestCase("int Target(int x) => ((Flags)x & Flags.B) != 0 ? 1 : 0;", 2, 1)]
    [TestCase("int Target(int x) => ((Flags)x ^ Flags.A) == Flags.None ? 1 : 0;", 1, 1)]
    [TestCase("int Target(int x) => (Wide)x < Wide.High ? 1 : 0;", -1, 0)]
    [TestCase("int Target(int x) => (Wide)x > Wide.Low ? 1 : 0;", -1, 1)]
    [TestCase("int Target(int x) => (Signed)x < Signed.Zero ? 1 : 0;", 128, 1)]
    [TestCase("int Target(int x) { switch ((Flags)x) { case Flags.A: return 10; case Flags.B: return 20; default: return 0; } }", 2, 20)]
    [TestCase("int Target(int x) => (Flags)x is Flags.C ? 1 : 0;", 4, 1)]
    [TestCase("int Target(int x) { Flags f = default; return f == Flags.None ? x : -x; }", 7, 7)]
    // Complement at the underlying width of narrow enums.
    [TestCase("int Target(int x) => (int)~(Small)x;", 1, 254)]
    [TestCase("int Target(int x) => (int)~(Signed)x;", -128, 127)]
    [TestCase("int Target(int x) => (int)~(Signed)x;", 5, -6)]
    [TestCase("int Target(int x) => ~(Small)x == Small.A ? 1 : 0;", 254, 1)]
    [TestCase("int Target(int x) => (int)~(Half)x;", 0x8000, 0x7FFF)]
    public void EnumValuesReplayLikeTheRuntime(string members, int input, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members, Enums);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var replay = subject.Execute(lowered, [input]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    // A checked enumeration conversion faults like the underlying conversion
    // (Roslyn reports IsChecked for numeric conversions only).
    [TestCase("int Target(int x) => (int)checked((Small)x);", 300)]
    [TestCase("int Target(int x) => checked((int)(Wide)x);", -1)]
    [TestCase("int Target(int x) => checked((int)unchecked((Wide)x));", -1)]
    [TestCase("int Target(int x) { checked { return (int)(Signed)x; } }", 200)]
    public void CheckedEnumConversionOverflowsLikeTheRuntime(string members, int input)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members, Enums);
        Assert.That(subject.Invoke([input]), Is.TypeOf<System.OverflowException>());
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var replay = subject.Execute(lowered, [input]);
        Assert.That(replay.Status, Is.Not.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
    }

    [TestCase("int Target(int x) => unchecked((int)(Wide)x);", -1, -1)]
    [TestCase("int Target(int x) => checked((int)unchecked((Small)x));", 300, 44)]
    public void UncheckedEnumConversionWrapsLikeTheRuntime(string members, int input, int expected)
    { EnumValuesReplayLikeTheRuntime(members, input, expected); }

    // Enum arithmetic and increments, and boxing (a boxed enum is not a boxed
    // integer), are not modeled.
    [TestCase("int Target(int x) => (int)((Small)x + 1);")]
    [TestCase("int Target(int x) => (int)((Small)x - Small.A);")]
    [TestCase("int Target(int x) { var e = (Small)x; e++; return (int)e; }")]
    [TestCase("int Target(int x) { var e = (Small)x; e += 1; return (int)e; }")]
    [TestCase("int Target(int x) { object o = (Small)x; return o is byte ? 1 : 0; }")]
    public void UnmodeledEnumOperationsClose(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members, Enums);
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);
    }
}

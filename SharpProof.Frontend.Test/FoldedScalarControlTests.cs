using System.Globalization;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FoldedScalarControlTests
{
    [TestCase("int Target(int x) => x % -1;", int.MinValue)]
    [TestCase("int Target(int x) => checked(x % -1);", int.MinValue)]
    [TestCase("long Target(long x) => x % -1L;", long.MinValue)]
    [TestCase("long Target(long x) => checked(x % -1L);", long.MinValue)]
    public void RuntimeRemainderRetainsOverflow(string members, object input)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.TypeOf<OverflowException>());
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
    }

    [TestCase("int Target(int x) => unchecked(int.MaxValue + 1);", (long)int.MinValue)]
    [TestCase("int Target(int x) => checked(7 / 2);", 3L)]
    [TestCase("long Target(int x) => checked(3L * 4L);", 12L)]
    [TestCase("int Target(int x) => checked(int.MaxValue % -1);", 0L)]
    [TestCase("long Target(int x) => checked(long.MaxValue % -1L);", 0L)]
    [TestCase("int Target(int x) { return ((x = 2) > 0 && false) ? 100 : x; }", 2L)]
    [TestCase("int Target(int x) { return ((x = 2) > 0 || true) ? x : 100; }", 2L)]
    public void FoldedNeighborsAndOperandEffectsMatchCompiledRuntime(string members, long expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(Convert.ToInt64(subject.Invoke([0]), CultureInfo.InvariantCulture), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}

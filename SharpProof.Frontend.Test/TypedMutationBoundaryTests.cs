using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedMutationBoundaryTests
{
    [TestCase("ref", "cell.Value", "cell.Value = 5;")]
    [TestCase("in", "cell.Value", "cell.Value = 5;")]
    [TestCase("ref", "values[0]", "values[0] = 5;")]
    [TestCase("in", "values[0]", "values[0] = 5;")]
    public void RuntimeReadonlyReferenceObservesAliasedStore(string modifier, string argument, string mutation)
    {
        using var subject = TypedProgramSubject.Create("int Target() { var cell = new Cell(); cell.Value = 3; " +
            "var values = new int[] { 3 }; return Helper(" + modifier + " " + argument + ", cell, values); } " +
            "private static int Helper(" + modifier + " int value, Cell cell, int[] values) { " + mutation + " return value; }",
            "public sealed class Cell { public int Value; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(5));
    }

    [TestCase("public int Value;")]
    [TestCase("public int Value { get; set; }")]
    public void CompoundFieldStoreRetainsReceiverBeforeRightHandSide(string member)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target() {
                Box a = new Box(); a.Value = 3;
                Box b = new Box(); b.Value = 5;
                Box original = a;
                a.Value += (a = b).Value;
                return original.Value * 10 + b.Value;
            }
            """, "public sealed class Box { " + member + " }");
        Assert.That(subject.Invoke([]), Is.EqualTo(85));
        var lowered = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var replay = subject.Execute(lowered, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(85));
    }

    [TestCase("new int?[] { null }")]
    [TestCase("new int?[] { default(int?) }")]
    [TestCase("[null]")]
    public void NullableConstantArrayInitializerAbstainsWithoutCrashing(string initializer)
    {
        using var subject = TypedProgramSubject.Create("int Target() { int?[] values = " + initializer + "; return values.Length; }");
        Assert.That(subject.Invoke([]), Is.EqualTo(1));
        FrontendProgramLoweringResult? lowered = null;
        Assert.DoesNotThrow((Action)(() => lowered = subject.LowerSourceCalls(false, opaqueCalls: true)));
        Assert.That(lowered!.IsExact, Is.False);
    }

    [TestCase("public int Value;")]
    [TestCase("public int Value { get; set; }")]
    public void CompoundNullReceiverFaultsBeforeRightHandSide(string member)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target() {
                Box value = null;
                int seen = 0;
                try { value.Value += ++seen; }
                catch (System.NullReferenceException) { return seen; }
                return -1;
            }
            """, "public sealed class Box { " + member + " }");
        Assert.That(subject.Invoke([]), Is.EqualTo(0));
        var lowered = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        var replay = subject.Execute(lowered, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.Zero);
    }
}

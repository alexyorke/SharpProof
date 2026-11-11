using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class MultidimensionalMutationEffectCoverageTests
{
    [Test]
    public void ActualMultidimensionalIncrementRetainsRuntimeMutationAndElementWriteEffect()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int[,] values) { values[0, 0]++; return values[0, 0]; }");
        var values = Array.CreateInstance(typeof(int), 1, 1);
        values.SetValue(1, 0, 0);
        Assert.That(subject.Invoke([values]), Is.EqualTo(2));
        Assert.That(values.GetValue(0, 0), Is.EqualTo(2));
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Any(write => write.Region == IrWriteRegion.Element), Is.True);
    }
}

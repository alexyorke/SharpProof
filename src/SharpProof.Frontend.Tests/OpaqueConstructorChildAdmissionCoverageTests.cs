using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OpaqueConstructorChildAdmissionCoverageTests
{
    [Test]
    public void ActualExceptionConstructionRetainsUnboxingChildAndTypedAllocationControl()
    {
        using var rejected = TypedProgramSubject.Create(
            "object Target(object value) => new System.ArgumentOutOfRangeException(\"value\", (int)value, \"bad\");");
        var actual = rejected.Invoke([17]);
        Assert.That(actual, Is.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(((ArgumentOutOfRangeException)actual!).ActualValue, Is.EqualTo(17));
        Assert.That(((ArgumentOutOfRangeException)actual!).ParamName, Is.EqualTo("value"));
        var rejectedLowering = rejected.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(rejectedLowering.IsExact, Is.False);
        Assert.That(rejectedLowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        Assert.That(rejectedLowering.ConstructionLimitExceeded, Is.False);

        using var supported = TypedProgramSubject.Create(
            "object Target(int value) => new System.ArgumentOutOfRangeException(\"value\", value, \"bad\");");
        var supportedActual = supported.Invoke([17]);
        Assert.That(supportedActual, Is.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(((ArgumentOutOfRangeException)supportedActual!).ActualValue, Is.EqualTo(17));
        Assert.That(((ArgumentOutOfRangeException)supportedActual!).ParamName, Is.EqualTo("value"));
        var lowering = supported.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var instructions = lowering.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToArray();
        Assert.That(instructions.OfType<IrAllocationInstruction>().Count(), Is.EqualTo(2));
        Assert.That(instructions.OfType<IrCallInstruction>().Count(), Is.EqualTo(1));
    }
}

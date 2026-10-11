using System.Reflection;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GenericNullReceiverLoweringTests
{
    [Test]
    public void UnconstrainedGenericReceiverRetainsNullFault()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target<T>(T value) { try { value.ToString(); return 1; } " +
            "catch (System.NullReferenceException) { return 9; } }");
        using var assemblyBytes = new MemoryStream();
        Assert.That(subject.Compilation.Emit(assemblyBytes).Success, Is.True);
        var method = Assembly.Load(assemblyBytes.ToArray()).GetType("Subject")!.GetMethod("Target")!
            .MakeGenericMethod(typeof(string));
        Assert.That(method.Invoke(null, [null]), Is.EqualTo(9));
        Assert.That(method.Invoke(null, ["value"]), Is.EqualTo(1));

        var lowered = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var faults = lowered.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrThrowInstruction>().Select(instruction => instruction.ExceptionKind);
        Assert.That(faults, Does.Contain(IrExceptionKind.NullReference),
            "An unconstrained T can be a null reference, so its instance call must have a null-fault edge.");
    }

    [TestCase("where T : class", true)]
    [TestCase("where T : struct", false)]
    [TestCase("where T : System.Enum", true)]
    public void GenericReceiverNullGuardMatchesConstraint(string constraint, bool mayBeNull)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target<T>(T value) " + constraint + " { value.ToString(); return 1; }" +
            (constraint == "where T : System.Enum" ? " static int Witness() => Target<System.Enum>(null);" : ""));
        var lowered = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var hasNullFault = lowered.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrThrowInstruction>().Any(instruction => instruction.ExceptionKind == IrExceptionKind.NullReference);
        Assert.That(hasNullFault, Is.EqualTo(mayBeNull));
    }
}

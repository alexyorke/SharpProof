using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ExceptionKindBoundaryCoverageTests
{
    private static readonly IrExceptionKind[] OverflowKinds = [IrExceptionKind.Overflow];

    [Test]
    public void MetadataNameRejectsUndefinedKindAndPreservesCanonicalOverflowName()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            CSharpOperationSemantics.ExceptionMetadataName((IrExceptionKind)int.MaxValue)));
        Assert.That(error!.ParamName, Is.EqualTo("kind"));
        Assert.That(CSharpOperationSemantics.ExceptionMetadataName(IrExceptionKind.Overflow),
            Is.EqualTo("System.OverflowException"));
    }

    [TestCase(SpecialType.System_Int32)]
    [TestCase(SpecialType.System_String)]
    public void CatchClassificationRejectsOrdinaryNonExceptionTypesAndKeepsOverflowCatch(SpecialType rejectedType)
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) { try { return checked(value + 1); } catch (System.OverflowException) { return 17; } }");
        Assert.That(subject.Invoke([int.MaxValue]), Is.EqualTo(17));
        Assert.That(CSharpOperationSemantics.TryCatchKinds(subject.Compilation,
            subject.Compilation.GetSpecialType(rejectedType), out var rejectedKinds), Is.False);
        Assert.That(rejectedKinds.IsDefault, Is.True);
        var overflow = subject.Compilation.GetTypeByMetadataName("System.OverflowException")!;
        Assert.That(CSharpOperationSemantics.TryCatchKinds(subject.Compilation, overflow, out var kinds), Is.True);
        Assert.That(kinds, Is.EquivalentTo(OverflowKinds));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [int.MaxValue]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}

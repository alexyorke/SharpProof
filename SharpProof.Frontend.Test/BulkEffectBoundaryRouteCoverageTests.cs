using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkEffectBoundaryRouteCoverageTests
{
    [TestCase("int")]
    [TestCase("string")]
    public void RegisteredEmptyArrayRetainsItsCacheAllocationEffect(string elementType)
    {
        using var subject = TypedProgramSubject.Create("int Target() => System.Array.Empty<" + elementType + ">().Length;");
        Assert.That(subject.Invoke([]), Is.EqualTo(0));
        var lowering = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context,
            static _ => true, null, default, ResolveArrayEmpty);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var call = lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Single();
        Assert.That(IrOpaqueCallSite.Effects(subject.Factory, call.Operation), Is.EqualTo(IrOpaqueCallEffects.Allocates));

        TotalScalarCallModel? ResolveArrayEmpty(IMethodSymbol method)
        {
            Assert.That(method.IsStatic, Is.True);
            Assert.That(method.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_Array));
            Assert.That(method.Name, Is.EqualTo("Empty"));
            Assert.That(method.Parameters, Is.Empty);
            Assert.That(method.ReturnType, Is.InstanceOf<IArrayTypeSymbol>());
            return new(0, _ => CSharpOperationSemantics.ArrayEmpty(subject.Factory,
                new RoslynTypeMapper(subject.Factory).GetTypeId(method.ReturnType)), effects: IrOpaqueCallEffects.Allocates);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscardedFieldIncrementRetainsRejectedReceiverConversion(bool missing)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(object value) { try { ((Box)value).Value++; return 17; } catch (System.NullReferenceException) { return 19; } }",
            "public sealed class Box { public int Value; }");
        if (missing)
        { Assert.That(subject.Invoke([null!]), Is.EqualTo(19)); }
        else
        { Assert.That(subject.Invoke([new object()]), Is.InstanceOf<InvalidCastException>()); }
        var lowering = new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context,
            static _ => true, null, default, approximateElementReads: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void OpaqueSetterRetainsRejectedAssignedUnboxing()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(object value) { var box = new Box(); box.Value = (int)value; return 17; }",
            "public sealed class Box { public int Value { set { } } }");
        Assert.That(subject.Invoke([23]), Is.EqualTo(17));
        Assert.That(subject.Invoke(["word"]), Is.InstanceOf<InvalidCastException>());
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        using var supported = TypedProgramSubject.Create(
            "int Target(int value) { var box = new Box(); box.Value = value; return 17; }",
            "public sealed class Box { public int Value { set { } } }");
        Assert.That(supported.Invoke([23]), Is.EqualTo(17));
        Assert.That(supported.LowerSourceCalls(false, opaqueCalls: true).IsExact, Is.True);
    }
}

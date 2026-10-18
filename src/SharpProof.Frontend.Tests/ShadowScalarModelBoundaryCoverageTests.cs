using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ShadowScalarModelBoundaryCoverageTests
{
    [TestCase("int")]
    [TestCase("string")]
    public void RegisteredArrayEmptyModelIsEvaluatedWithoutExecutableShadowAdmission(string elementType)
    {
        System.ArgumentNullException.ThrowIfNull(elementType);
        using var subject = TypedProgramSubject.Create("int Target() => System.Array.Empty<" + elementType + ">().Length;");
        Assert.That(subject.Invoke([]), Is.EqualTo(0));
        var resolutions = 0;
        var shadow = new RoslynProgramLowerer(subject.Factory).LowerShadowSourceBody(subject.Graph, subject.Context,
            static _ => true, default, ResolveArrayEmpty);
        Assert.That(resolutions, Is.EqualTo(1));
        Assert.That(shadow.Classification.IsExact, Is.True);
        Assert.That(shadow.IsShadowCallSkeleton, Is.True);
        Assert.That(shadow.IsExact, Is.False);
        Assert.That(shadow.ConstructionLimitExceeded, Is.False);
        Assert.That(shadow.PreservedSourceCalls, Is.Empty);
        var replay = subject.Execute(shadow, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));

        TotalScalarCallModel? ResolveArrayEmpty(IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            Assert.That(method.IsStatic, Is.True);
            Assert.That(method.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_Array));
            Assert.That(method.Name, Is.EqualTo("Empty"));
            Assert.That(method.Arity, Is.EqualTo(1));
            Assert.That(method.Parameters, Is.Empty);
            Assert.That(method.ReturnType, Is.InstanceOf<IArrayTypeSymbol>());
            resolutions++;
            return new(0, _ => CSharpOperationSemantics.ArrayEmpty(subject.Factory,
                new RoslynTypeMapper(subject.Factory).GetTypeId(method.ReturnType)));
        }
    }
}

using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedLiteralInputBoundaryCoverageTests
{
    [Test]
    public void NonNullLiteralRequiresTypeAndPreservesTypedValue()
    {
        using var subject = TypedProgramSubject.Create("int Target() => 42;");
        Assert.That(subject.Invoke([]), Is.EqualTo(42));
        var factory = new IrFactory();
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            CSharpOperationSemantics.Literal(factory, null, 42)));
        Assert.That(error!.ParamName, Is.EqualTo("type"));
        var term = CSharpOperationSemantics.Literal(factory,
            subject.Compilation.GetSpecialType(SpecialType.System_Int32), 42);
        var result = new IrInterpreter(factory).Evaluate(term);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(42));
    }
}

using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class DowncastChildAdmissionCoverageTests
{
    [Test]
    public void ActualReferenceDowncastKeepsRejectedChildAndOwnedParameterAdmission()
    {
        const string declaration = "public sealed class Box { }";
        using var rejected = TypedProgramSubject.Create("bool Target() => (Box)Read() != null; " +
            "private static object Read() => new Box();", declaration);
        Assert.That(rejected.Invoke([]), Is.True);
        var tree = rejected.Compilation.SyntaxTrees.Single();
        var cast = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
        var operation = rejected.Compilation.GetSemanticModel(tree).GetOperation(cast)!;
        var builder = new IrProgramBuilder(rejected.Factory);
        var entry = builder.CreateBlock("entry");
        builder.SetEntry(entry);
        var child = new RoslynTotalExpressionLowerer(rejected.Context, builder).LowerBodyValue(operation, entry);
        Assert.That(child.Classification.IsExact, Is.False);
        Assert.That(child.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        var rejectedProgram = rejected.Lower();
        Assert.That(rejectedProgram.IsExact, Is.False);
        Assert.That(rejectedProgram.ConstructionLimitExceeded, Is.False);

        using var supported = TypedProgramSubject.Create("bool Target(object value) => (Box)value != null;", declaration);
        Assert.That(supported.Invoke([null!]), Is.False);
        Assert.That(supported.Lower().IsExact, Is.True);
    }
}

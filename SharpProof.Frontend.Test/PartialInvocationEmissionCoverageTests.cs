using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class PartialInvocationEmissionCoverageTests
{
    [TestCase(false, 17)]
    [TestCase(true, 18)]
    public void RealPartialInvocationElisionPreservesArgumentEffects(bool implemented, int expected)
    {
        var implementation = implemented ? "static partial void Skip(int value) { }" : "";
        using var subject = TypedProgramSubject.Create("int Target() => Helper.Run();",
            "public static partial class Helper { public static int Run() { int value = 17; Skip(value++); return value; } static partial void Skip(int value); " + implementation + " }");
        Assert.That(subject.Invoke([]), Is.EqualTo(expected));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var invocationSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "Skip" });
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(invocationSyntax)!;
        Assert.That(operation, Is.InstanceOf<Microsoft.CodeAnalysis.Operations.IInvocationOperation>());
        var policy = new CSharpInvocationEmissionPolicy(subject.Compilation);
        Assert.That(policy.IsElided(operation), Is.EqualTo(!implemented));
        Assert.That(policy.IsElided(operation), Is.EqualTo(!implemented));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, []);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}

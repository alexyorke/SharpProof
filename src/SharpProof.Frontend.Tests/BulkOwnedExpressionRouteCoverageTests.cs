using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkOwnedExpressionRouteCoverageTests
{

    [Test]
    public void RealOpaqueArgumentBoxingRetainsUnsupportedStructUnboxingChild()
    {
        using var subject = TypedProgramSubject.Create("object Target(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue((System.DateTime)value);");
        var input = new DateTime(2020, 1, 2);
        Assert.That(subject.Invoke([input]), Is.EqualTo(input));
        Assert.That(subject.Invoke(["word"]), Is.InstanceOf<InvalidCastException>());
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var boxing = (IConversionOperation)invocation.Arguments.Single().Value;
        Assert.That(boxing.IsImplicit, Is.True);
        Assert.That(boxing.Parent, Is.InstanceOf<IArgumentOperation>());
        Assert.That(CSharpOperationSemantics.IsImplicitOpaqueBoxing(boxing, subject.Compilation), Is.True);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        using var supported = TypedProgramSubject.Create("int Target(int value) => value;");
        Assert.That(supported.Invoke([17]), Is.EqualTo(17));
        ExactInteger(supported, [17], 17);
    }

    private static void ExactInteger(TypedProgramSubject subject, ImmutableArray<object> inputs, int expected)
    {
        if (inputs.IsDefault)
        { throw new System.ArgumentNullException(nameof(inputs)); }
        System.ArgumentNullException.ThrowIfNull(subject);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, inputs);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}

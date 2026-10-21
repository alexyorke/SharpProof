using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class StoredSourceValueAdmissionCoverageTests
{
    [TestCase("array", false)]
    [TestCase("field", false)]
    [TestCase("field", true)]
    public void ActualRefCalleeValueKeepsStoreAdmissionConservative(string storageKind, bool compound)
    {
        System.ArgumentNullException.ThrowIfNull(storageKind);
        var array = storageKind == "array";
        var setup = array ? "var storage = new int[1];" : "var storage = new Cell();";
        var access = array ? "storage[0]" : "storage.Value";
        var assignment = compound ? " += " : " = ";
        const string additional = "public sealed class Cell { public int Value; }";
        var prefix = "int Target(int value) { " + setup + access + assignment;
        var suffix = "; return " + access + "; }";
        using var subject = TypedProgramSubject.Create(prefix + "Helper(ref value)" + suffix +
            " private static int Helper(ref int value) { value++; return value; }", additional);
        Assert.That(subject.Invoke([5]), Is.EqualTo(6));
        Assert.That(subject.Context.HasScalarSignature, Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var callSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(callSyntax)!;
        Assert.That(invocation.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(invocation.TargetMethod.IsStatic, Is.True);
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        Assert.That(invocation.Arguments[0].ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
        Assert.That(invocation.Arguments[0].Parameter!.RefKind, Is.EqualTo(RefKind.Ref));
        Assert.That(invocation.Arguments[0].Value, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.Arguments[0].Parameter!.ContainingSymbol,
            invocation.TargetMethod), Is.True);
        var storeSyntax = callSyntax.Ancestors().OfType<AssignmentExpressionSyntax>().Single();
        var store = model.GetOperation(storeSyntax)!;
        var target = compound ? ((ICompoundAssignmentOperation)store).Target : ((ISimpleAssignmentOperation)store).Target;
        var value = compound ? ((ICompoundAssignmentOperation)store).Value : ((ISimpleAssignmentOperation)store).Value;
        Assert.That(value, Is.SameAs(invocation));
        Assert.That(target.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(target, array ? Is.InstanceOf<IArrayElementReferenceOperation>() : Is.InstanceOf<IFieldReferenceOperation>());
        var session = new TotalSourceCallSession(subject.Compilation, null, null, default, opaqueCalls: true);
        var lowering = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        Assert.That(session.IsActive(invocation.TargetMethod), Is.False);
        using var control = TypedProgramSubject.Create(prefix + "value + 1" + suffix, additional);
        Assert.That(control.Invoke([5]), Is.EqualTo(6));
        var controlSession = new TotalSourceCallSession(control.Compilation, null, null, default, opaqueCalls: true);
        var exact = new RoslynTotalProgramLowerer(control.Context, default, controlSession).Lower(control.Graph);
        Assert.That(exact.IsExact, Is.True);
        Assert.That(exact.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(exact, [5]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(6)));
    }
}

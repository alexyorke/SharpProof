using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ElementGuardNarrowInputCoverageTests
{
    private static readonly ImmutableArray<IrExceptionKind> ExpectedFaults = [IrExceptionKind.NullReference, IrExceptionKind.IndexOutOfRange];

    [Test]
    public void PureGuardWidensActualByteParameterAndPreservesNativeFaults()
    {
        using var subject = TypedProgramSubject.Create("int Target(int[] items, byte position) => items[position];");
        var nativeItems = new[] { 17 };
        Assert.That(subject.Invoke([nativeItems, (byte)0]), Is.EqualTo(17));
        Assert.That(subject.Invoke([null!, (byte)0]), Is.TypeOf<NullReferenceException>());
        Assert.That(subject.Invoke([Array.Empty<int>(), (byte)0]), Is.TypeOf<IndexOutOfRangeException>());
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var names = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().ToArray();
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var receiver = lowerer.LowerClause(model.GetOperation(names.Single(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name.Identifier.ValueText == "items")))!);
        var index = lowerer.LowerClause(model.GetOperation(names.Single(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name.Identifier.ValueText == "position")))!);
        Assert.That(receiver.Classification.IsExact, Is.True);
        Assert.That(index.Classification.IsExact, Is.True);
        var guard = CSharpOperationSemantics.ElementGuard(subject.Factory, receiver.Value, index.Value, null);
        Assert.That(guard.Classification.IsExact, Is.True);
        Assert.That(guard.Throws.Select(fault => fault.Kind), Is.EqualTo(ExpectedFaults));
        var array = subject.Factory.CreateSequenceValue(receiver.Value.Type, nativeItems.Select(value => subject.Factory.CreateIntegerValue(value)));
        var environment = ImmutableDictionaryCapture.Create(subject.Context.Parameters, binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Current), binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.Ordinal == 0
            ? array : subject.Value(binding.Current, (byte)0))).ToBuilder();
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(guard.Value, environment.ToImmutable()).Value!.Boolean, Is.True);
        Assert.That(guard.Throws.All(fault => !interpreter.Evaluate(fault.Condition, environment.ToImmutable()).Value!.Boolean), Is.True);
        var receiverVariable = subject.Context.Parameters.Single(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.Ordinal == 0)).Current;
        environment[receiverVariable] = subject.Factory.CreateNullValue(receiver.Value.Type);
        Assert.That(interpreter.Evaluate(guard.Throws[0].Condition, environment.ToImmutable()).Value!.Boolean, Is.True);
        environment[receiverVariable] = subject.Factory.CreateSequenceValue(receiver.Value.Type, []);
        Assert.That(interpreter.Evaluate(guard.Throws[1].Condition, environment.ToImmutable()).Value!.Boolean, Is.True);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var arguments = ImmutableDictionaryCapture.Create(subject.Context.Parameters, binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry), binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.Ordinal == 0
            ? array : subject.Value(binding.Entry, (byte)0)));
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, arguments);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}

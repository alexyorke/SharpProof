using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ElementGuardInputDomainCoverageTests
{
    [TestCase("bool Target(int[] items, long position) => items != null && position >= 0;", 1L)]
    [TestCase("bool Target(int[] items, bool position) => items != null && position;", true)]
    public void PureGuardRejectsActualWideOrBooleanInput(string members, object position)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        System.ArgumentNullException.ThrowIfNull(position);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([new int[1], position]), Is.True);
        Assert.That(subject.Invoke([null!, position]), Is.False);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var names = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().ToArray();
        var receiver = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(model.GetOperation(names.Single(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name.Identifier.ValueText == "items")))!);
        var index = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(model.GetOperation(names.Single(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name.Identifier.ValueText == "position")))!);
        Assert.That(receiver.Classification.IsExact, Is.True);
        Assert.That(index.Classification.IsExact, Is.True);
        var guard = CSharpOperationSemantics.ElementGuard(subject.Factory, receiver.Value, index.Value, null);
        Assert.That(guard.Classification.IsExact, Is.False);
        Assert.That(guard.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(guard.Throws, Is.Empty);
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(guard.Value, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty).Value!.Boolean, Is.False);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var environment = ImmutableDictionaryCapture.Create(subject.Context.Parameters, binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry), binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.Ordinal == 0
            ? subject.Factory.CreateSequenceValue(subject.Factory.GetVariableInfo(binding.Entry).Type, [subject.Factory.CreateIntegerValue(0)])
            : subject.Value(binding.Entry, position)));
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, environment);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
    }
}

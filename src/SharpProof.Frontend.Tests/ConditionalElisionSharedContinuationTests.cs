using System.Numerics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalElisionSharedContinuationTests
{
    [Test]
    public void SiblingThrowArgumentsShareAnOmittedSecondArgumentContinuation()
    {
        using var subject = TypedProgramSubject.Create("int Target(string value) { Log(" +
            "value == null ? (value == null ? throw new System.NullReferenceException() : true) : " +
            "(value.Length == 0 ? throw new System.InvalidOperationException() : false), " +
            "value != null && value.Length > 0); return 7; } " +
            "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool first, bool second) { }");
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var conditions = subject.Graph.Blocks.Where(block =>
            block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable && block.ConditionKind != ControlFlowConditionKind.None)).ToArray();
        Assert.That(conditions.Length, Is.GreaterThanOrEqualTo(4));
        var throws = subject.Graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable &&
            block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)).ToArray();
        Assert.That(throws.Length, Is.EqualTo(2));
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        foreach (var block in conditions.Concat(throws))
        {
            Assert.That(block.BranchValue, Is.Not.Null);
            Assert.That(emission.IsElided(block.BranchValue!), Is.True);
        }
        foreach (var input in new string?[] { null, "word", "" })
        { Assert.That(subject.Invoke([input!]), Is.EqualTo(7)); }
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        foreach (var input in new string?[] { null, "word", "" })
        {
            var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
            var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
                new Dictionary<IrVarId, IrValue> { [parameter] = value });
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}

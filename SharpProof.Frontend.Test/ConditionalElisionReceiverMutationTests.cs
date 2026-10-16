using System.Numerics;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalElisionReceiverMutationTests
{
    [Test]
    public void OmittedReceiverThrowPreservesFollowingRuntimeMutation()
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(string value)
            {
                int result = 7;
                (value == null ? throw new System.NullReferenceException() : new Sink())?.Log(value.Length > 0);
                result += 3;
                return result;
            }
            """, additionalSource: """
            public sealed class Sink
            {
                [System.Diagnostics.Conditional("ABSENT")]
                public void Log(bool value) { }
            }
            """);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var inputs = new string?[] { null, "word", "" };
        foreach (var input in inputs)
        { Assert.That(subject.Invoke([input!]), Is.EqualTo(10)); }
        var mutation = subject.Graph.Blocks.SelectMany(block => block.Operations)
            .Select(operation => operation is IExpressionStatementOperation statement ? statement.Operation : operation)
            .OfType<ICompoundAssignmentOperation>().Single();
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        Assert.That(emission.IsElided(mutation), Is.False);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        foreach (var input in inputs)
        {
            var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
            var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
                new Dictionary<IrVarId, IrValue> { [parameter] = value });
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(10)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}

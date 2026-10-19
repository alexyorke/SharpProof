using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalElisionStatementBoundaryTests
{
    [Test]
    public void ElidedCallsPreserveInterveningAndFollowingRuntimeMutations()
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(string value)
            {
                int result = 7;
                Log(value == null ? throw new System.NullReferenceException() : new System.Collections.Generic.List<int> { result }.Count > 0);
                result += 1;
                Log(value.Length == 0 ? throw new System.InvalidOperationException() : new System.Collections.Generic.List<int> { result }.Count > 0);
                Log(value != null && value.Length > 0);
                result += 2;
                return result;
            }
            [System.Diagnostics.Conditional("ABSENT")]
            static void Log(bool value) { }
            """);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        var mutations = subject.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations))
            .Select(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation is IExpressionStatementOperation statement ? statement.Operation : operation))
            .OfType<ICompoundAssignmentOperation>().ToImmutableArray();
        Assert.That(mutations.Length, Is.EqualTo(2));
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        foreach (var mutation in mutations)
        { Assert.That(emission.IsElided(mutation), Is.False); }
        foreach (var input in ImmutableArray.Create<string?>(null, "word", ""))
        { Assert.That(subject.Invoke([input!]), Is.EqualTo(10)); }
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        foreach (var input in ImmutableArray.Create<string?>(null, "word", ""))
        {
            var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
            var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter, value));
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(10)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}

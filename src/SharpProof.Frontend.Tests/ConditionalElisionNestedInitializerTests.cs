using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalElisionNestedInitializerTests
{
    [Test]
    public void NestedCollectionInitializersInElidedArgumentsNeverExecute()
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(string value)
            {
                Log(value == null
                    ? (value == null ? throw new System.NullReferenceException() : true)
                    : (value.Length == 0 ? throw new System.InvalidOperationException()
                        : (value.Length == 1 ? false : new System.Collections.Generic.List<int> { 1 }.Count > 0)),
                    new System.Collections.Generic.List<int> { value != null ? 2 : 3 }.Count > 0);
                return 7;
            }
            [System.Diagnostics.Conditional("ABSENT")]
            static void Log(bool first, bool second) { }
            """);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        var additions = subject.Graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable))
            .SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations))
            .Select(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation is IExpressionStatementOperation statement ? statement.Operation : operation))
            .OfType<IInvocationOperation>().Where(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.TargetMethod.Name == "Add")).ToImmutableArray();
        Assert.That(additions.Length, Is.EqualTo(2));
        var list = subject.Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        Assert.That(list, Is.Not.Null);
        foreach (var addition in additions)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(addition.TargetMethod.ContainingType.OriginalDefinition, list), Is.True);
            Assert.That(emission.IsElided(addition), Is.True);
        }
        var throws = subject.Graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable &&
            block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)).ToImmutableArray();
        Assert.That(throws.Length, Is.EqualTo(2));
        foreach (var thrown in throws)
        { Assert.That(emission.IsElided(thrown.BranchValue!), Is.True); }
        foreach (var input in ImmutableArray.Create<string?>(null, "word", ""))
        { Assert.That(subject.Invoke([input!]), Is.EqualTo(7)); }
        var lowered = subject.Lower();
        TestContext.WriteLine($"Nested initializer lowering exact={lowered.IsExact}; constructionLimitExceeded={lowered.ConstructionLimitExceeded}; abstention={lowered.Classification.Abstention}");
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
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}

using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalElisionReceiverContinuationTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void ConditionalAccessReceiverThrowPreservesOmittedAndOrdinaryEvaluation(bool elided)
    {
        const string members = """
            int Target(string value)
            {
                (value == null ? throw new System.NullReferenceException() : new Sink())?.Log(value.Length > 0);
                return 7;
            }
            """;
        const string conditionalSink = """
            public sealed class Sink
            {
                [System.Diagnostics.Conditional("ABSENT")]
                public void Log(bool value) { }
            }
            """;
        const string ordinarySink = """
            public sealed class Sink
            {
                public void Log(bool value) { }
            }
            """;
        using var subject = TypedProgramSubject.Create(members,
            additionalSource: elided ? conditionalSink : ordinarySink);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var inputs = new string?[] { null, "word", "" };
        foreach (var input in inputs)
        {
            var native = subject.Invoke([input!]);
            if (!elided && input == null)
            { Assert.That(native, Is.TypeOf<NullReferenceException>()); }
            else
            { Assert.That(native, Is.EqualTo(7)); }
        }
        var tree = subject.Compilation.SyntaxTrees.Single();
        var accessSyntax = tree.GetRoot().DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().Single();
        var access = (IConditionalAccessOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(accessSyntax)!;
        Assert.That(access.Operation, Is.InstanceOf<IConditionalOperation>());
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        var reachable = subject.Graph.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable)).ToArray();
        var thrown = reachable.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw));
        Assert.That(emission.IsElided(thrown.BranchValue!), Is.EqualTo(elided));
        var callBlock = reachable.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations.Any(operation =>
            operation is null ? throw new System.ArgumentNullException(nameof(operation)) : ((operation is IExpressionStatementOperation statement ? statement.Operation : operation)
                is IInvocationOperation invocation && invocation.TargetMethod.Name == "Log"))));
        var call = callBlock.Operations.Select(operation =>
                operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation is IExpressionStatementOperation statement ? statement.Operation : operation))
            .OfType<IInvocationOperation>().Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.TargetMethod.Name == "Log"));
        Assert.That(call.TargetMethod.ContainingType.Name, Is.EqualTo("Sink"));
        Assert.That(emission.IsElided(call, requireOwningInvocation: true), Is.EqualTo(elided));
        var returned = reachable.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return));
        Assert.That(callBlock, Is.Not.SameAs(returned));
        var nullTest = reachable.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.BranchValue is IIsNullOperation));
        Assert.That(nullTest.ConditionalSuccessor!.Destination, Is.SameAs(returned));
        Assert.That(emission.IsElided(nullTest.BranchValue!), Is.EqualTo(elided));
        var lowered = elided ? subject.Lower() : subject.LowerSourceCalls();
        TestContext.WriteLine($"elided={elided}, exact={lowered.IsExact}, limit={lowered.ConstructionLimitExceeded}, abstention={lowered.Classification.Abstention}");
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var binding = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(binding).Type;
        foreach (var input in inputs)
        {
            var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
            var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
                new Dictionary<IrVarId, IrValue> { [binding] = value },
                maximumSteps: 10000, replayOptions: ExceptionConstructorNormalReplay.Options(subject.Factory));
            if (!elided && input == null)
            {
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
                Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Explicit));
            }
            else
            {
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            }
            ExceptionConstructorNormalReplay.AssertApproximation(replay, !elided && input == null);
        }
    }
}

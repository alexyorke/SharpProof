using System.Numerics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NestedConditionalRethrowRegionCoverageTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void ConditionalRethrowFindsItsLexicalCatchThroughInnerTryAndFinally(int input)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int x) {
                try { return 10 / (x - x); }
                catch (System.DivideByZeroException) {
                    try {
                        if (x == 0) throw;
                        return x;
                    }
                    finally { x++; }
                }
            }
            """);
        var actual = subject.Invoke([input]);
        Assert.That(actual, input == 0 ? Is.TypeOf<DivideByZeroException>() : Is.EqualTo(1));
        var source = subject.Graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.BranchValue != null &&
            (block.ConditionalSuccessor?.Semantics == ControlFlowBranchSemantics.Rethrow ||
             block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Rethrow)));
        var region = source.EnclosingRegion;
        Assert.That(region.Kind, Is.EqualTo(ControlFlowRegionKind.Try));
        var climbed = 0;
        while (region.Kind != ControlFlowRegionKind.Catch)
        {
            Assert.That(region.EnclosingRegion, Is.Not.Null);
            region = region.EnclosingRegion!;
            climbed++;
        }
        Assert.That(climbed, Is.GreaterThan(0));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, [input]);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(input + 1)));
        if (input == 0)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
            var span = subject.Factory.GetOperationInfo(replay.Instruction!.Operation).SourceSpan!;
            Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("10 / (x - x)"));
        }
        else
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.One));
        }
    }
}
